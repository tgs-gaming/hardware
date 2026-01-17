using Newtonsoft.Json;

namespace board_firmware_setup
{
    public partial class TGSBoardSetup : Form
    {
        const int COL_PIN = 0;
        const int COL_KEY = 1;
        const int COL_MODE = 2;
        const int COL_INVERT = 3;
        const int COL_DEBOUNCE = 4;
        const int COL_TAP = 5;
        const int COL_STATE = 6;

        TgsBoardSerial _board;
        string _connectedPort;

        public TGSBoardSetup()
        {
            InitializeComponent();

            Load += (_, __) =>
            {
                UIEnabled(false);
                Reset();
            };

            FormClosed += async (_, __) =>
            {
                await DisconnectBoardAsync();
            };
        }

        int ParseBaudOrDefault()
        {
            if (int.TryParse((baudCombobox.Text ?? "").Trim(), out var b) && b > 0) return b;
            return 9600;
        }

        bool IsConnected() => _board != null && _board.IsConnected;

        int FindPinRowIndex(int pin)
        {
            for (int i = 0; i < pinsDatagrid.Rows.Count; i++)
            {
                var r = pinsDatagrid.Rows[i];
                if (r.IsNewRow) continue;

                var v = r.Cells[COL_PIN].Value?.ToString();
                if (int.TryParse(v, out var p) && p == pin)
                    return i;
            }
            return -1;
        }

        void SetPinStateInGrid(int pin, string state)
        {
            if (!pinsDatagrid.IsHandleCreated) return;
            if (pinsDatagrid.Columns.Count <= COL_STATE) return;

            var rowIndex = FindPinRowIndex(pin);
            if (rowIndex < 0) return;

            pinsDatagrid.Rows[rowIndex].Cells[COL_STATE].Value = state;
        }

        async Task ConnectToPortAsync(string port, int baud, CancellationToken ct = default)
        {
            await DisconnectBoardAsync();

            _connectedPort = port;

            var b = new TgsBoardSerial();
            b.OnEvent += ev =>
            {
                try
                {
                    BeginInvoke(() =>
                    {
                        eventsList.Items.Add($"Pin: {ev.Pin} State:{ev.State}");
                        eventsList.Invalidate();

                        var s = (ev.State ?? "").Trim().ToLowerInvariant();
                        if (s == "on") SetPinStateInGrid(ev.Pin, "on");
                        else if (s == "off") SetPinStateInGrid(ev.Pin, "off");
                    });
                }
                catch { }
            };

            _board = b;

            await _board.ConnectAsync(port, baud, openTimeoutMs: 400, ct: ct);

            UIEnabled(true);
            await RefreshStateAsync(ct);

            try
            {
                var sync = await _board.SyncAsync(timeoutMs: 1500, ct: ct);

                if (sync?.States != null)
                {
                    foreach (var st in sync.States)
                    {
                        var s = (st.State ?? "").Trim().ToLowerInvariant();
                        if (s == "on") SetPinStateInGrid(st.Pin, "on");
                        else if (s == "off") SetPinStateInGrid(st.Pin, "off");
                    }
                }
            }
            catch { }
        }

        async Task DisconnectBoardAsync()
        {
            var b = _board;
            _board = null;

            if (b != null)
            {
                try { await b.DisconnectAsync(); } catch { }
                try { b.Dispose(); } catch { }
            }

            _connectedPort = null;
            UIEnabled(false);
            Reset();
        }

        private async void connectButton_Click(object sender, EventArgs e)
        {
            if (IsConnected())
            {
                connectButton.Enabled = false;
                try { await DisconnectBoardAsync(); }
                finally { connectButton.Enabled = true; }
                return;
            }

            connectButton.Enabled = false;

            try
            {
                var baud = ParseBaudOrDefault();

                var probe = new TgsBoardSerial();
                var found = await probe.TryConnectByBoardNameAsync(
                    "TGS Input Board",
                    baudRate: baud,
                    openTimeoutMs: 120,
                    boardTimeoutMs: 220,
                    connectOpenTimeoutMs: 400);

                probe.Dispose();

                if (!found.Ok || string.IsNullOrWhiteSpace(found.PortName))
                {
                    MessageBox.Show("Board not found on COM1..COM12.");
                    return;
                }

                await ConnectToPortAsync(found.PortName, baud);
                connectButton.Text = "DISCONNECT";
            }
            catch (Exception ex)
            {
                await DisconnectBoardAsync();
                MessageBox.Show($"Auto-connect failed: {ex.Message}");
            }
            finally
            {
                connectButton.Enabled = true;
            }
        }

        void UIEnabled(bool enabled)
        {
            connectButton.Text = enabled ? "DISCONNECT" : "CONNECT";

            pinsDatagrid.Enabled = enabled;
            baudCombobox.Enabled = enabled;
            eventsCheckbox.Enabled = enabled;
            emulateKeyboardCheckbox.Enabled = enabled;

            exportButton.Enabled = enabled;
            importButton.Enabled = enabled;
            clearButton.Enabled = enabled;
            saveButton.Enabled = enabled;
        }

        void Reset()
        {
            eventsList.Items.Clear();
            pinsDatagrid.Rows.Clear();
            emulateKeyboardCheckbox.Checked = false;
            eventsCheckbox.Checked = false;
            baudCombobox.Text = "9600";
        }

        async Task RefreshStateAsync(CancellationToken ct = default)
        {
            var b = await _board.GetBoardAsync(timeoutMs: 1500, ct: ct);
            eventsList.Items.Add($"Board Type: {b.Board}");
            if (!string.IsNullOrWhiteSpace(_connectedPort))
                eventsList.Items.Add($"Port: {_connectedPort}");

            var v = await _board.GetVersionAsync(timeoutMs: 1500, ct: ct);
            eventsList.Items.Add($"Firmware Version: {v.Version}");

            var cfg = await _board.ExportAsync(timeoutMs: 2000, ct: ct);

            baudCombobox.Text = cfg.Data.Baud.ToString();
            eventsCheckbox.Checked = cfg.Data.Events;
            emulateKeyboardCheckbox.Checked = cfg.Data.Keyboard;

            pinsDatagrid.Rows.Clear();
            foreach (var btn in cfg.Data.Buttons)
                pinsDatagrid.Rows.Add(
                    btn.Pin.ToString(),
                    btn.Key,
                    btn.Mode,
                    btn.Invert,
                    btn.Debounce.ToString(),
                    btn.Tap.ToString(),
                    ""
                );
        }

        private async void saveToBoardButton_Click(object sender, EventArgs e)
        {
            if (!IsConnected()) return;
            var export = CreateExportData();
            await _board.ImportAsync(export);

            try
            {
                var sync = await _board.SyncAsync(timeoutMs: 1500);
                if (sync?.States != null)
                {
                    foreach (var st in sync.States)
                    {
                        var s = (st.State ?? "").Trim().ToLowerInvariant();
                        if (s == "on") SetPinStateInGrid(st.Pin, "on");
                        else if (s == "off") SetPinStateInGrid(st.Pin, "off");
                    }
                }
            }
            catch { }
        }

        ExportData CreateExportData()
        {
            return new ExportData()
            {
                Baud = Convert.ToInt32(baudCombobox.Text),
                Events = eventsCheckbox.Checked,
                Keyboard = emulateKeyboardCheckbox.Checked,
                Buttons = pinsDatagrid.Rows
                    .OfType<DataGridViewRow>()
                    .Where(r => !r.IsNewRow)
                    .Select(r => new ExportButton()
                    {
                        Pin = Convert.ToInt32(r.Cells[COL_PIN].Value),
                        Key = r.Cells[COL_KEY].Value?.ToString(),
                        Mode = r.Cells[COL_MODE].Value?.ToString(),
                        Invert = Convert.ToBoolean(r.Cells[COL_INVERT].Value),
                        Debounce = Convert.ToInt32(r.Cells[COL_DEBOUNCE].Value),
                        Tap = Convert.ToInt32(r.Cells[COL_TAP].Value)
                    })
                    .ToArray()
            };
        }

        private void clearButton_Click(object sender, EventArgs e)
        {
            Reset();
        }

        private void exportButton_Click(object sender, EventArgs e)
        {
            var export = CreateExportData();
            var json = JsonConvert.SerializeObject(export, Formatting.Indented);

            FileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";

            var result = saveFileDialog.ShowDialog();
            if (result == DialogResult.OK)
                File.WriteAllText(saveFileDialog.FileName, json);
        }

        private void importButton_Click(object sender, EventArgs e)
        {
            FileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";

            var result = openFileDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                var json = File.ReadAllText(openFileDialog.FileName);
                var import = JsonConvert.DeserializeObject<ExportData>(json);

                baudCombobox.Text = import.Baud.ToString();
                eventsCheckbox.Checked = import.Events;
                emulateKeyboardCheckbox.Checked = import.Keyboard;

                pinsDatagrid.Rows.Clear();
                foreach (var btn in import.Buttons)
                    pinsDatagrid.Rows.Add(
                        btn.Pin.ToString(),
                        btn.Key,
                        btn.Mode,
                        btn.Invert,
                        btn.Debounce.ToString(),
                        btn.Tap.ToString(),
                        ""
                    );
            }
        }

        private void TGSBoardSetup_Load(object sender, EventArgs e)
        {
        }
    }
}
