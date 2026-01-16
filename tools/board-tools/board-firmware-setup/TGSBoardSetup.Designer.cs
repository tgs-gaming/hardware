namespace board_firmware_setup
{
    partial class TGSBoardSetup
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            connectButton = new Button();
            portCombobox = new ComboBox();
            pinsDatagrid = new DataGridView();
            eventsList = new ListBox();
            clearButton = new Button();
            saveButton = new Button();
            baudCombobox = new ComboBox();
            eventsCheckbox = new CheckBox();
            emulateKeyboardCheckbox = new CheckBox();
            exportButton = new Button();
            importButton = new Button();
            pin = new DataGridViewComboBoxColumn();
            key = new DataGridViewTextBoxColumn();
            mode = new DataGridViewComboBoxColumn();
            invert = new DataGridViewCheckBoxColumn();
            debounce = new DataGridViewTextBoxColumn();
            tap = new DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)pinsDatagrid).BeginInit();
            SuspendLayout();
            // 
            // connectButton
            // 
            connectButton.Location = new Point(11, 12);
            connectButton.Name = "connectButton";
            connectButton.Size = new Size(121, 40);
            connectButton.TabIndex = 0;
            connectButton.Text = "CONNECT";
            connectButton.UseVisualStyleBackColor = true;
            connectButton.Click += connect_Click;
            // 
            // portCombobox
            // 
            portCombobox.FormattingEnabled = true;
            portCombobox.Location = new Point(13, 58);
            portCombobox.Name = "portCombobox";
            portCombobox.Size = new Size(118, 23);
            portCombobox.TabIndex = 1;
            // 
            // pinsDatagrid
            // 
            pinsDatagrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            pinsDatagrid.Columns.AddRange(new DataGridViewColumn[] { pin, key, mode, invert, debounce, tap });
            pinsDatagrid.Location = new Point(140, 12);
            pinsDatagrid.Name = "pinsDatagrid";
            pinsDatagrid.Size = new Size(390, 424);
            pinsDatagrid.TabIndex = 15;
            // 
            // eventsList
            // 
            eventsList.FormattingEnabled = true;
            eventsList.ItemHeight = 15;
            eventsList.Location = new Point(536, 12);
            eventsList.Name = "eventsList";
            eventsList.ScrollAlwaysVisible = true;
            eventsList.Size = new Size(245, 424);
            eventsList.TabIndex = 16;
            // 
            // clearButton
            // 
            clearButton.Location = new Point(11, 334);
            clearButton.Name = "clearButton";
            clearButton.Size = new Size(120, 35);
            clearButton.TabIndex = 18;
            clearButton.Text = "CLEAR";
            clearButton.UseVisualStyleBackColor = true;
            clearButton.Click += clearButton_Click;
            // 
            // saveButton
            // 
            saveButton.Location = new Point(11, 375);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(120, 61);
            saveButton.TabIndex = 20;
            saveButton.Text = "SAVE TO BOARD";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += saveToBoardButton_Click;
            // 
            // baudCombobox
            // 
            baudCombobox.FormattingEnabled = true;
            baudCombobox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            baudCombobox.Location = new Point(11, 133);
            baudCombobox.Name = "baudCombobox";
            baudCombobox.Size = new Size(120, 23);
            baudCombobox.TabIndex = 21;
            // 
            // eventsCheckbox
            // 
            eventsCheckbox.AutoSize = true;
            eventsCheckbox.Location = new Point(11, 162);
            eventsCheckbox.Name = "eventsCheckbox";
            eventsCheckbox.Size = new Size(87, 19);
            eventsCheckbox.TabIndex = 22;
            eventsCheckbox.Text = "Emit Events";
            eventsCheckbox.UseVisualStyleBackColor = true;
            // 
            // emulateKeyboardCheckbox
            // 
            emulateKeyboardCheckbox.AutoSize = true;
            emulateKeyboardCheckbox.Location = new Point(11, 187);
            emulateKeyboardCheckbox.Name = "emulateKeyboardCheckbox";
            emulateKeyboardCheckbox.Size = new Size(122, 19);
            emulateKeyboardCheckbox.TabIndex = 23;
            emulateKeyboardCheckbox.Text = "Emulate Keyboard";
            emulateKeyboardCheckbox.UseVisualStyleBackColor = true;
            // 
            // exportButton
            // 
            exportButton.Location = new Point(11, 285);
            exportButton.Name = "exportButton";
            exportButton.Size = new Size(120, 43);
            exportButton.TabIndex = 24;
            exportButton.Text = "EXPORT TO FILE";
            exportButton.UseVisualStyleBackColor = true;
            exportButton.Click += exportButton_Click;
            // 
            // importButton
            // 
            importButton.Location = new Point(11, 234);
            importButton.Name = "importButton";
            importButton.Size = new Size(120, 45);
            importButton.TabIndex = 25;
            importButton.Text = "IMPORT FROM FILE";
            importButton.UseVisualStyleBackColor = true;
            importButton.Click += importButton_Click;
            // 
            // pin
            // 
            pin.FillWeight = 10F;
            pin.HeaderText = "PIN";
            pin.Items.AddRange(new object[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23" });
            pin.Name = "pin";
            pin.Width = 50;
            // 
            // key
            // 
            key.HeaderText = "KEY";
            key.Name = "key";
            key.Width = 60;
            // 
            // mode
            // 
            mode.HeaderText = "MODE";
            mode.Items.AddRange(new object[] { "tap", "hold" });
            mode.Name = "mode";
            mode.Width = 60;
            // 
            // invert
            // 
            invert.HeaderText = "INVERT";
            invert.Name = "invert";
            invert.Width = 50;
            // 
            // debounce
            // 
            debounce.HeaderText = "DEB";
            debounce.Name = "debounce";
            debounce.Width = 50;
            // 
            // tap
            // 
            tap.HeaderText = "TAP";
            tap.Name = "tap";
            tap.Width = 50;
            // 
            // TGSBoardSetup
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(793, 449);
            Controls.Add(importButton);
            Controls.Add(exportButton);
            Controls.Add(emulateKeyboardCheckbox);
            Controls.Add(eventsCheckbox);
            Controls.Add(baudCombobox);
            Controls.Add(saveButton);
            Controls.Add(clearButton);
            Controls.Add(eventsList);
            Controls.Add(pinsDatagrid);
            Controls.Add(portCombobox);
            Controls.Add(connectButton);
            Name = "TGSBoardSetup";
            Text = "sss";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)pinsDatagrid).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button connectButton;
        private ComboBox portCombobox;
        private DataGridView pinsDatagrid;
        private ListBox eventsList;
        private Button clearButton;
        private Button saveButton;
        private ComboBox baudCombobox;
        private CheckBox eventsCheckbox;
        private CheckBox emulateKeyboardCheckbox;
        private Button exportButton;
        private Button importButton;
        private DataGridViewComboBoxColumn pin;
        private DataGridViewTextBoxColumn key;
        private DataGridViewComboBoxColumn mode;
        private DataGridViewCheckBoxColumn invert;
        private DataGridViewTextBoxColumn debounce;
        private DataGridViewTextBoxColumn tap;
    }
}
