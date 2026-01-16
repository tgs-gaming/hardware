using Newtonsoft.Json;
using RJCP.IO.Ports;

namespace board_firmware_setup
{
    public partial class TGSBoardSetup : Form
    {
        TgsBoardSerial _board;


        public TGSBoardSetup()
        {
            InitializeComponent();
            LoadPorts();
            UIEnabled(false);
        }

        private async void connect_Click(object sender, EventArgs e)
        {
            if (connectButton.Text == "DISCONNECT")
            {
                _board.Close();
                UIEnabled(false);
                Reset();
                return;
            }

            _board = new TgsBoardSerial(portCombobox.Text);
            _board.OnEvent += ev =>
            {
                BeginInvoke(() =>
                {
                    eventsList.Items.Add($"Pin: {ev.Pin} State:{ev.State}");
                    eventsList.Invalidate();
                });
            };
            try
            {
                _board.Open();
                UIEnabled(true);
                RefreshState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open port: {ex.Message}");
                return;
            }
        }

        private void UIEnabled(bool enabled)
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

        private void Reset()
        {
            eventsList.Items.Clear();
            pinsDatagrid.Rows.Clear();
            emulateKeyboardCheckbox.Checked = false;
            eventsCheckbox.Checked = false;
            baudCombobox.Text = "9600";
        }

        private async void RefreshState()
        {
            var b = await _board.GetBoardAsync();
            eventsList.Items.Add($"Board Type: {b.Board}");
            var v = await _board.GetVersionAsync();
            eventsList.Items.Add($"Firmware Version: {v.Version}");

            var cfg = await _board.ExportAsync();

            baudCombobox.Text = cfg.Data.Baud.ToString();
            eventsCheckbox.Checked = cfg.Data.Events;
            emulateKeyboardCheckbox.Checked = cfg.Data.Keyboard;

            pinsDatagrid.Rows.Clear();
            foreach (var btn in cfg.Data.Buttons)
            {
                pinsDatagrid.Rows.Add(btn.Pin.ToString(), btn.Key, btn.Mode, btn.Invert, btn.Debounce.ToString(), btn.Tap.ToString());
            }



        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        void LoadPorts()
        {
            portCombobox.Items.Clear();

            var ports = new SerialPortStream().GetPortNames()
                                              .OrderBy(p => p)
                                              .ToArray();

            portCombobox.Items.AddRange(ports);

            if (portCombobox.Items.Count > 0)
                portCombobox.SelectedIndex = 0;
        }

        private async void saveToBoardButton_Click(object sender, EventArgs e)
        {
            var export = CreateExportData();
            await _board.ImportAsync(export);
        }

        private ExportData CreateExportData()
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
                        Pin = Convert.ToInt32(r.Cells[0].Value),
                        Key = r.Cells[1].Value.ToString(),
                        Mode = r.Cells[2].Value.ToString(),
                        Invert = Convert.ToBoolean(r.Cells[3].Value),
                        Debounce = Convert.ToInt32(r.Cells[4].Value),
                        Tap = Convert.ToInt32(r.Cells[5].Value)
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
            {
                File.WriteAllText(saveFileDialog.FileName, json);
            }
        }

        private void importButton_Click(object sender, EventArgs e)
        {
            FileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
            var result = openFileDialog.ShowDialog();
            if (result == DialogResult.OK) { }
            {
                var json = File.ReadAllText(openFileDialog.FileName);
                var import = JsonConvert.DeserializeObject<ExportData>(json);
                baudCombobox.Text = import.Baud.ToString();
                eventsCheckbox.Checked = import.Events;
                emulateKeyboardCheckbox.Checked = import.Keyboard;
                pinsDatagrid.Rows.Clear();
                foreach (var btn in import.Buttons)
                {
                    pinsDatagrid.Rows.Add(btn.Pin.ToString(), btn.Key, btn.Mode, btn.Invert, btn.Debounce.ToString(), btn.Tap.ToString());
                }
            }
        }
    }
}
