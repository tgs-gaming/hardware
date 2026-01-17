using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RJCP.IO.Ports;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public interface IMessage { }

public sealed class UnknownMessage : IMessage
{
    public string Raw { get; }
    public JObject Json { get; }
    public UnknownMessage(string raw, JObject json) { Raw = raw; Json = json; }
}

public abstract class Response : IMessage
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("ok")] public bool Ok { get; set; }
    [JsonProperty("cmd")] public string Cmd { get; set; }
    [JsonProperty("error")] public string Error { get; set; }
}

public sealed class OkResponse : Response { }

public sealed class BoardResponse : Response
{
    [JsonProperty("board")] public string Board { get; set; }
}

public sealed class VersionResponse : Response
{
    [JsonProperty("version")] public string Version { get; set; }
}

public sealed class ExportResponse : Response
{
    [JsonProperty("data")] public ExportData Data { get; set; }
}

public sealed class SyncResponse : Response
{
    [JsonProperty("mask")] public ulong Mask { get; set; }
    [JsonProperty("states")] public SyncState[] States { get; set; }
}

public sealed class SyncState
{
    [JsonProperty("pin")] public int Pin { get; set; }
    [JsonProperty("state")] public string State { get; set; }
}

public sealed class ExportData
{
    [JsonProperty("schema")] public int Schema { get; set; }
    [JsonProperty("cfg_ver")] public int CfgVer { get; set; }
    [JsonProperty("baud")] public int Baud { get; set; }
    [JsonProperty("events")] public bool Events { get; set; }
    [JsonProperty("keyboard")] public bool Keyboard { get; set; }
    [JsonProperty("buttons")] public ExportButton[] Buttons { get; set; }
}

public sealed class ExportButton
{
    [JsonProperty("pin")] public int Pin { get; set; }
    [JsonProperty("mode")] public string Mode { get; set; }
    [JsonProperty("invert")] public bool Invert { get; set; }
    [JsonProperty("key")] public string Key { get; set; }
    [JsonProperty("debounce")] public int Debounce { get; set; }
    [JsonProperty("tap")] public int Tap { get; set; }
}

public sealed class MessageEvent : IMessage
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("pin")] public int Pin { get; set; }
    [JsonProperty("state")] public string State { get; set; }
}

public static class MessageParser
{
    public static IMessage Parse(string line)
    {
        JObject jo;
        try { jo = JObject.Parse(line); }
        catch { return null; }

        var type = (string)jo["type"];

        if (string.Equals(type, "event", StringComparison.OrdinalIgnoreCase))
        {
            if (jo["pin"] == null || jo["state"] == null) return new UnknownMessage(line, jo);
            return jo.ToObject<MessageEvent>();
        }

        if (string.Equals(type, "resp", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = (string)jo["cmd"] ?? "";

            if (cmd.Equals("export", StringComparison.OrdinalIgnoreCase))
                return jo.ToObject<ExportResponse>();

            if (cmd.Equals("board", StringComparison.OrdinalIgnoreCase))
                return jo.ToObject<BoardResponse>();

            if (cmd.Equals("version", StringComparison.OrdinalIgnoreCase))
                return jo.ToObject<VersionResponse>();

            if (cmd.Equals("sync", StringComparison.OrdinalIgnoreCase))
                return jo.ToObject<SyncResponse>();

            return jo.ToObject<OkResponse>();
        }

        if (jo["ok"] != null && jo["cmd"] != null)
            return jo.ToObject<OkResponse>();

        if (jo["pin"] != null && jo["state"] != null && jo["cmd"] == null)
            return jo.ToObject<MessageEvent>();

        return new UnknownMessage(line, jo);
    }
}

public sealed class TgsBoardSerial : IDisposable
{
    SerialPortStream _port;
    CancellationTokenSource _cts;
    Task _readerTask;

    readonly ConcurrentDictionary<string, TaskCompletionSource<Response>> _pending =
        new ConcurrentDictionary<string, TaskCompletionSource<Response>>(StringComparer.OrdinalIgnoreCase);

    readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

    readonly ConcurrentQueue<MessageEvent> _eventQueue = new ConcurrentQueue<MessageEvent>();
    readonly SemaphoreSlim _eventSignal = new SemaphoreSlim(0, int.MaxValue);

    public event Action<MessageEvent> OnEvent;
    public event Action<IMessage> OnAnyMessage;

    public bool IsConnected => _port != null && _port.IsOpen;
    public string PortName => _port?.PortName;

    public TgsBoardSerial() { }

    SerialPortStream CreatePort(string portName, int baudRate)
    {
        return new SerialPortStream
        {
            PortName = portName,
            BaudRate = baudRate,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
            Encoding = Encoding.UTF8,
            NewLine = "\n",
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
    }

    public async Task ConnectAsync(string portName, int baudRate, int openTimeoutMs, CancellationToken ct)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var p = CreatePort(portName, baudRate);

        var openTask = Task.Run(() => p.Open(), ct);
        if (await Task.WhenAny(openTask, Task.Delay(openTimeoutMs, ct)).ConfigureAwait(false) != openTask)
        {
            try { p.Dispose(); } catch { }
            throw new TimeoutException("open");
        }

        _port = p;
        _cts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReaderLoop(_cts.Token));
    }

    public async Task DisconnectAsync()
    {
        var cts = _cts;
        _cts = null;

        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
        }

        var rt = _readerTask;
        _readerTask = null;

        if (rt != null)
        {
            try { await rt.ConfigureAwait(false); } catch { }
        }

        foreach (var p in _pending.Values) p.TrySetCanceled();
        _pending.Clear();

        var port = _port;
        _port = null;

        if (port != null)
        {
            try { if (port.IsOpen) port.Close(); } catch { }
            try { port.Dispose(); } catch { }
        }

        if (cts != null)
        {
            try { cts.Dispose(); } catch { }
        }
    }

    public bool TryDequeueEvent(out MessageEvent ev) => _eventQueue.TryDequeue(out ev);

    public async Task<MessageEvent> ReadEventAsync(int timeoutMs = Timeout.Infinite, CancellationToken ct = default)
    {
        if (_eventQueue.TryDequeue(out var ev)) return ev;

        if (timeoutMs == Timeout.Infinite)
        {
            await _eventSignal.WaitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            using (var tcts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                tcts.CancelAfter(timeoutMs);
                await _eventSignal.WaitAsync(tcts.Token).ConfigureAwait(false);
            }
        }

        while (!_eventQueue.TryDequeue(out ev))
            await Task.Yield();

        return ev;
    }

    public Task<ExportResponse> ExportAsync(int timeoutMs = 2000, CancellationToken ct = default)
        => SendAsync<ExportResponse>(new { cmd = "export" }, "export", timeoutMs, ct);

    public Task<BoardResponse> GetBoardAsync(int timeoutMs = 1500, CancellationToken ct = default)
        => SendAsync<BoardResponse>(new { cmd = "board" }, "board", timeoutMs, ct);

    public Task<VersionResponse> GetVersionAsync(int timeoutMs = 1500, CancellationToken ct = default)
        => SendAsync<VersionResponse>(new { cmd = "version" }, "version", timeoutMs, ct);

    public Task<SyncResponse> SyncAsync(int timeoutMs = 1500, CancellationToken ct = default)
        => SendAsync<SyncResponse>(new { cmd = "sync" }, "sync", timeoutMs, ct);

    public Task<OkResponse> ResetAsync(int timeoutMs = 2500, CancellationToken ct = default)
        => SendAsync<OkResponse>(new { cmd = "reset" }, "reset", timeoutMs, ct);

    public async Task<OkResponse> ImportAsync(ExportData data, int timeoutMs = 6000, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected");

        int oldBaud = _port.BaudRate;

        var resp = await SendAsync<OkResponse>(new { cmd = "import", data }, "import", timeoutMs, ct).ConfigureAwait(false);

        int newBaud = data?.Baud ?? oldBaud;
        if (newBaud != oldBaud && newBaud >= 300 && newBaud <= 3000000)
        {
            try { _port.Close(); } catch { }
            _port.BaudRate = newBaud;
            _port.Open();
        }

        return resp;
    }

    public async Task<TResponse> SendAsync<TResponse>(object payload, string expectedCmd, int timeoutMs, CancellationToken ct)
        where TResponse : Response
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected");

        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        TaskCompletionSource<Response> tcs = null;

        try
        {
            tcs = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pending.TryAdd(expectedCmd, tcs))
            {
                if (_pending.TryRemove(expectedCmd, out var old))
                    old.TrySetCanceled();
                _pending[expectedCmd] = tcs;
            }

            await WriteJsonAsync(payload, ct).ConfigureAwait(false);

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(timeoutMs);

                Task completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
                if (completed != tcs.Task)
                    throw new TimeoutException(expectedCmd);

                var resp = await tcs.Task.ConfigureAwait(false);
                if (resp is TResponse typed) return typed;

                return JObject.FromObject(resp).ToObject<TResponse>();
            }
        }
        finally
        {
            if (tcs != null)
            {
                if (_pending.TryGetValue(expectedCmd, out var cur) && ReferenceEquals(cur, tcs))
                    _pending.TryRemove(expectedCmd, out _);
            }

            _requestLock.Release();
        }
    }

    async Task WriteJsonAsync(object payload, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(payload, Formatting.None) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _port.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    async Task ReaderLoop(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        var sb = new StringBuilder(512);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await _port.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                }
                catch
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    continue;
                }

                if (read <= 0) continue;

                for (int i = 0; i < read; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\r') continue;

                    if (c == '\n')
                    {
                        var line = sb.ToString().Trim();
                        sb.Length = 0;
                        if (line.Length > 0) HandleLine(line);
                    }
                    else
                    {
                        if (sb.Length < 32768) sb.Append(c);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    void HandleLine(string line)
    {
        var msg = MessageParser.Parse(line);
        if (msg == null) return;

        OnAnyMessage?.Invoke(msg);

        if (msg is MessageEvent ev)
        {
            _eventQueue.Enqueue(ev);
            _eventSignal.Release();
            OnEvent?.Invoke(ev);
            return;
        }

        if (msg is Response resp)
        {
            var cmd = resp.Cmd ?? "";
            if (!string.IsNullOrEmpty(cmd) && _pending.TryRemove(cmd, out var tcs))
                tcs.TrySetResult(resp);
        }
    }

    static async Task<(bool Ok, string PortName)> TryFindPortByBoardNameAsync(
        string boardName,
        int baudRate,
        int openTimeoutMs,
        int boardTimeoutMs,
        CancellationToken ct)
    {
        for (int i = 1; i <= 12; i++)
        {
            ct.ThrowIfCancellationRequested();
            var portName = "COM" + i;

            SerialPortStream p = null;
            TgsBoardSerial temp = null;

            try
            {
                p = new SerialPortStream
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    Encoding = Encoding.UTF8,
                    NewLine = "\n",
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                var openTask = Task.Run(() => p.Open(), ct);
                if (await Task.WhenAny(openTask, Task.Delay(openTimeoutMs, ct)).ConfigureAwait(false) != openTask)
                {
                    try { p.Dispose(); } catch { }
                    continue;
                }

                temp = new TgsBoardSerial();
                temp._port = p;
                temp._cts = new CancellationTokenSource();
                temp._readerTask = Task.Run(() => temp.ReaderLoop(temp._cts.Token));

                BoardResponse resp;
                try
                {
                    resp = await temp.SendAsync<BoardResponse>(new { cmd = "board" }, "board", boardTimeoutMs, ct).ConfigureAwait(false);
                }
                finally
                {
                    await temp.DisconnectAsync().ConfigureAwait(false);
                }

                if (resp != null && resp.Ok &&
                    string.Equals(resp.Board ?? "", boardName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, portName);
                }
            }
            catch
            {
                try { p?.Dispose(); } catch { }
            }
            finally
            {
                try { p?.Dispose(); } catch { }
            }
        }

        return (false, null);
    }

    public async Task<(bool Ok, string PortName)> TryConnectByBoardNameAsync(
        string boardName,
        int baudRate = 9600,
        int openTimeoutMs = 120,
        int boardTimeoutMs = 220,
        int connectOpenTimeoutMs = 300,
        CancellationToken ct = default)
    {
        var found = await TryFindPortByBoardNameAsync(
            boardName,
            baudRate,
            openTimeoutMs,
            boardTimeoutMs,
            ct).ConfigureAwait(false);

        if (!found.Ok) return found;

        await ConnectAsync(found.PortName, baudRate, connectOpenTimeoutMs, ct).ConfigureAwait(false);
        return found;
    }

    public void Dispose()
    {
        try { DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        _writeLock.Dispose();
        _requestLock.Dispose();
        _eventSignal.Dispose();
    }
}
