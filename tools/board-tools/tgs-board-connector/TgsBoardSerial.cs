using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RJCP.IO.Ports;
using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

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

public sealed class ExportData
{
    [JsonProperty("schema")] public int Schema { get; set; }
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
    readonly SerialPortStream _port;
    readonly CancellationTokenSource _cts = new();
    readonly Task _readerTask;

    readonly ConcurrentDictionary<string, TaskCompletionSource<Response>> _pending = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly SemaphoreSlim _requestLock = new(1, 1);

    readonly ConcurrentQueue<MessageEvent> _eventQueue = new();
    readonly SemaphoreSlim _eventSignal = new(0, int.MaxValue);

    public event Action<MessageEvent> OnEvent;
    public event Action<IMessage> OnAnyMessage;

    public TgsBoardSerial(string portName, int baudRate = 9600)
    {
        _port = new SerialPortStream
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

        _readerTask = Task.Run(() => ReaderLoop(_cts.Token));
    }

    public void Open()
    {
        if (!_port.IsOpen) _port.Open();
    }

    public void Close()
    {
        if (_port.IsOpen) _port.Close();
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
            using var tcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tcts.CancelAfter(timeoutMs);
            await _eventSignal.WaitAsync(tcts.Token).ConfigureAwait(false);
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

    public async Task<OkResponse> ImportAsync(ExportData data, int timeoutMs = 4000, CancellationToken ct = default)
    {
        int oldBaud = _port.BaudRate;

        var resp = await SendAsync<OkResponse>(new { cmd = "import", data }, "import", timeoutMs, ct).ConfigureAwait(false);

        int newBaud = data?.Baud ?? oldBaud;
        if (newBaud != oldBaud && newBaud >= 300 && newBaud <= 3000000)
        {
            try { if (_port.IsOpen) _port.Close(); } catch { }
            _port.BaudRate = newBaud;
            try { _port.Open(); } catch { }
        }

        return resp;
    }

    public async Task<TResponse> SendAsync<TResponse>(object payload, string expectedCmd, int timeoutMs = 1500, CancellationToken ct = default)
        where TResponse : Response
    {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[expectedCmd] = tcs;

            await WriteJsonAsync(payload, ct).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled(), false);

            var resp = await tcs.Task.ConfigureAwait(false);
            if (resp is TResponse typed) return typed;
            return JObject.FromObject(resp).ToObject<TResponse>();
        }
        finally
        {
            _requestLock.Release();
        }
    }

    async Task WriteJsonAsync(object payload, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(payload) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_port.IsOpen) _port.Open();
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
                if (!_port.IsOpen)
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    continue;
                }

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
                        if (sb.Length < 8192) sb.Append(c);
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

        var any = OnAnyMessage;
        if (any != null) any(msg);

        if (msg is MessageEvent ev)
        {
            _eventQueue.Enqueue(ev);
            _eventSignal.Release();
            var onEvent = OnEvent;
            if (onEvent != null) onEvent(ev);
            return;
        }

        if (msg is Response resp)
        {
            var cmd = resp.Cmd ?? "";
            if (!string.IsNullOrEmpty(cmd) && _pending.TryRemove(cmd, out var tcs))
                tcs.TrySetResult(resp);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try { _readerTask.GetAwaiter().GetResult(); } catch { }

        foreach (var p in _pending.Values) p.TrySetCanceled();
        _pending.Clear();

        try { Close(); } catch { }

        _port.Dispose();
        _writeLock.Dispose();
        _requestLock.Dispose();
        _eventSignal.Dispose();
        _cts.Dispose();
    }
}
