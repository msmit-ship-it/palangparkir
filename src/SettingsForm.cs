using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MsmPayStation;

public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly TableLayoutPanel _grid = new();
    private readonly TextBox _backend = new();
    private readonly TextBox _deviceKey = new();
    private readonly TextBox _site = new();
    private readonly TextBox _paystation = new();
    private readonly TextBox _gate = new();
    private readonly TextBox _terminal = new();
    private readonly TextBox _deviceId = new();
    private readonly NumericUpDown _carAmount = Num(1, 50000000, 1000);
    private readonly NumericUpDown _motoAmount = Num(1, 50000000, 1000);
    private readonly NumericUpDown _poll = Num(500, 30000, 500);
    private readonly NumericUpDown _refresh = Num(5, 300, 5);
    private readonly ComboBox _com = new();
    private readonly NumericUpDown _baud = Num(1200, 921600, 1200);
    private readonly TextBox _openHex = new();
    private readonly TextBox _closeHex = new();
    private readonly NumericUpDown _pulse = Num(100, 5000, 100);
    private readonly CheckBox _dryRun = new();
    private readonly NumericUpDown _joyId = Num(-1, 15, 1);
    private readonly NumericUpDown _carButton = Num(1, 32, 1);
    private readonly NumericUpDown _motoButton = Num(1, 32, 1);
    private readonly CheckBox _fullscreen = new();
    private readonly TextBox _pin = new();
    private readonly Label _testStatus = new();

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg;
        Text = "MSM Pay Station - Pengaturan";
        Size = new Size(780, 780);
        MinimumSize = new Size(720, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(8, 24, 45);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Build();
        LoadValues();
    }

    private void Build()
    {
        var header = new Label
        {
            Text = "PENGATURAN PAY STATION",
            Dock = DockStyle.Top,
            Height = 66,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 0, 0),
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            BackColor = Color.FromArgb(11, 39, 71),
            ForeColor = Color.White
        };
        Controls.Add(header);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18), BackColor = BackColor };
        Controls.Add(scroll);
        scroll.BringToFront();

        _grid.Dock = DockStyle.Top;
        _grid.AutoSize = true;
        _grid.ColumnCount = 2;
        _grid.Padding = new Padding(4);
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(_grid);

        Section("BACKEND MSM PAY");
        Row("Backend URL", _backend);
        _deviceKey.UseSystemPasswordChar = true;
        Row("Device Key", _deviceKey);

        Section("LOKASI & DEVICE");
        Row("Site ID", _site);
        Row("Pay Station ID", _paystation);
        Row("Gate ID", _gate);
        Row("Terminal ID", _terminal);
        Row("Device ID", _deviceId);

        Section("TARIF TEST / FIXED");
        Row("Tarif Mobil (Rp)", _carAmount);
        Row("Tarif Motor (Rp)", _motoAmount);

        Section("NETWORK");
        Row("Polling status (ms)", _poll);
        Row("Refresh provider (detik)", _refresh);

        Section("M GATE USB RELAY");
        _com.DropDownStyle = ComboBoxStyle.DropDown;
        var comPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        _com.Width = 220;
        var scan = ButtonSmall("SCAN COM");
        scan.Click += (_, _) => LoadPorts();
        comPanel.Controls.Add(_com); comPanel.Controls.Add(scan);
        Row("COM Port", comPanel);
        Row("Baud Rate", _baud);
        Row("OpenGate HEX", _openHex);
        Row("CloseGate HEX", _closeHex);
        Row("Pulse (ms)", _pulse);
        _dryRun.Text = "Mode test / Dry Run (tidak kirim ke relay fisik)";
        _dryRun.AutoSize = true;
        Row("Relay Mode", _dryRun);

        Section("ZERO DELAY USB ENCODER");
        Row("Joystick ID (-1 = auto)", _joyId);
        Row("Tombol Mobil", _carButton);
        Row("Tombol Motor", _motoButton);

        Section("TAMPILAN & KEAMANAN");
        _fullscreen.Text = "Fullscreen kiosk"; _fullscreen.AutoSize = true;
        Row("Mode layar", _fullscreen);
        _pin.UseSystemPasswordChar = true;
        Row("PIN Pengaturan (F12)", _pin);

        Section("TEST");
        var tests = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var testBackend = ButtonSmall("TEST BACKEND");
        var testRelay = ButtonSmall("TEST OPEN GATE");
        testBackend.Click += async (_, _) => await TestBackendAsync();
        testRelay.Click += async (_, _) => await TestRelayAsync();
        tests.Controls.Add(testBackend); tests.Controls.Add(testRelay);
        Row("Uji koneksi", tests);
        _testStatus.AutoSize = true; _testStatus.ForeColor = Color.FromArgb(150, 200, 225); _testStatus.MaximumSize = new Size(440, 0);
        Row("Status", _testStatus);

        var action = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = Color.FromArgb(11, 39, 71) };
        var save = ButtonAction("SIMPAN", Color.FromArgb(31, 173, 113));
        var cancel = ButtonAction("BATAL", Color.FromArgb(80, 100, 125));
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        action.Controls.Add(save); action.Controls.Add(cancel);
        Controls.Add(action);
        action.BringToFront();
        header.BringToFront();
    }

    private void Section(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = false,
            Height = 42,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 211, 235),
            Padding = new Padding(0, 14, 0, 0)
        };
        var r = _grid.RowCount++;
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(label, 0, r);
        _grid.SetColumnSpan(label, 2);
    }

    private void Row(string labelText, Control input)
    {
        var r = _grid.RowCount++;
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = labelText, AutoSize = true, ForeColor = Color.FromArgb(180, 200, 220), Padding = new Padding(0, 9, 8, 0) };
        input.Margin = new Padding(3, 5, 3, 5);
        if (input is TextBox tb) { tb.Width = 410; tb.BackColor = Color.White; tb.ForeColor = Color.Black; }
        if (input is NumericUpDown num) { num.Width = 180; num.ThousandsSeparator = true; }
        _grid.Controls.Add(label, 0, r);
        _grid.Controls.Add(input, 1, r);
    }

    private void LoadValues()
    {
        _backend.Text = _cfg.BackendUrl;
        _deviceKey.Text = _cfg.DeviceKey;
        _site.Text = _cfg.SiteId;
        _paystation.Text = _cfg.PaystationId;
        _gate.Text = _cfg.GateId;
        _terminal.Text = _cfg.TerminalId;
        _deviceId.Text = _cfg.DeviceId;
        _carAmount.Value = Clamp(_carAmount, _cfg.CarAmount);
        _motoAmount.Value = Clamp(_motoAmount, _cfg.MotorcycleAmount);
        _poll.Value = Clamp(_poll, _cfg.PollIntervalMs);
        _refresh.Value = Clamp(_refresh, _cfg.ProviderRefreshSec);
        LoadPorts();
        _com.Text = _cfg.SerialPort;
        _baud.Value = Clamp(_baud, _cfg.BaudRate);
        _openHex.Text = _cfg.OpenHex;
        _closeHex.Text = _cfg.CloseHex;
        _pulse.Value = Clamp(_pulse, _cfg.PulseMs);
        _dryRun.Checked = _cfg.DryRunRelay;
        _joyId.Value = Clamp(_joyId, _cfg.JoystickId);
        _carButton.Value = Clamp(_carButton, _cfg.CarButton);
        _motoButton.Value = Clamp(_motoButton, _cfg.MotorcycleButton);
        _fullscreen.Checked = _cfg.FullScreen;
        _pin.Text = _cfg.SettingsPin;
    }

    private void LoadPorts()
    {
        var current = _com.Text;
        var ports = MGateRelay.GetPorts();
        _com.Items.Clear();
        _com.Items.AddRange(ports.Cast<object>().ToArray());
        if (!string.IsNullOrWhiteSpace(current)) _com.Text = current;
        _testStatus.Text = ports.Length == 0 ? "Tidak ada COM terdeteksi." : "COM: " + string.Join(", ", ports);
    }

    private AppConfig Snapshot()
    {
        return new AppConfig
        {
            BackendUrl = _backend.Text.Trim().TrimEnd('/'),
            DeviceKey = _deviceKey.Text.Trim(),
            SiteId = _site.Text.Trim(),
            PaystationId = _paystation.Text.Trim(),
            GateId = _gate.Text.Trim(),
            TerminalId = _terminal.Text.Trim(),
            DeviceId = _deviceId.Text.Trim(),
            CarAmount = _carAmount.Value,
            MotorcycleAmount = _motoAmount.Value,
            PollIntervalMs = (int)_poll.Value,
            ProviderRefreshSec = (int)_refresh.Value,
            QrTimeoutSec = _cfg.QrTimeoutSec,
            SuccessScreenSec = _cfg.SuccessScreenSec,
            SerialPort = _com.Text.Trim(),
            BaudRate = (int)_baud.Value,
            OpenHex = _openHex.Text.Trim(),
            CloseHex = _closeHex.Text.Trim(),
            PulseMs = (int)_pulse.Value,
            DryRunRelay = _dryRun.Checked,
            JoystickId = (int)_joyId.Value,
            CarButton = (int)_carButton.Value,
            MotorcycleButton = (int)_motoButton.Value,
            FullScreen = _fullscreen.Checked,
            SettingsPin = string.IsNullOrWhiteSpace(_pin.Text) ? "1234" : _pin.Text.Trim()
        };
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_backend.Text) || !_backend.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Backend URL harus HTTPS.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var s = Snapshot();
        Copy(s, _cfg);
        _cfg.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task TestBackendAsync()
    {
        var s = Snapshot();
        using var api = new MsmApiClient(s);
        _testStatus.Text = "Menguji backend...";
        var r = await api.TestBackendAsync();
        _testStatus.Text = r.ok ? "OK: " + r.message : "GAGAL: " + r.message;
        _testStatus.ForeColor = r.ok ? Color.FromArgb(48, 211, 135) : Color.FromArgb(248, 113, 113);
    }

    private async Task TestRelayAsync()
    {
        if (!_dryRun.Checked)
        {
            var yes = MessageBox.Show(this, "TEST OPEN GATE akan mengirim trigger ke relay fisik. Pastikan area palang aman. Lanjut?", "Konfirmasi K3", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (yes != DialogResult.Yes) return;
        }
        var relay = new MGateRelay(Snapshot());
        _testStatus.Text = "Menguji relay...";
        var r = await relay.PulseAsync();
        _testStatus.Text = r.ok ? "RELAY OK: " + r.message : "RELAY GAGAL: " + r.message;
        _testStatus.ForeColor = r.ok ? Color.FromArgb(48, 211, 135) : Color.FromArgb(248, 113, 113);
    }

    private static void Copy(AppConfig s, AppConfig d)
    {
        d.BackendUrl=s.BackendUrl; d.DeviceKey=s.DeviceKey; d.SiteId=s.SiteId; d.PaystationId=s.PaystationId; d.GateId=s.GateId; d.TerminalId=s.TerminalId; d.DeviceId=s.DeviceId;
        d.CarAmount=s.CarAmount; d.MotorcycleAmount=s.MotorcycleAmount; d.PollIntervalMs=s.PollIntervalMs; d.ProviderRefreshSec=s.ProviderRefreshSec; d.QrTimeoutSec=s.QrTimeoutSec; d.SuccessScreenSec=s.SuccessScreenSec;
        d.SerialPort=s.SerialPort; d.BaudRate=s.BaudRate; d.OpenHex=s.OpenHex; d.CloseHex=s.CloseHex; d.PulseMs=s.PulseMs; d.DryRunRelay=s.DryRunRelay;
        d.JoystickId=s.JoystickId; d.CarButton=s.CarButton; d.MotorcycleButton=s.MotorcycleButton; d.FullScreen=s.FullScreen; d.SettingsPin=s.SettingsPin;
    }

    private static NumericUpDown Num(decimal min, decimal max, decimal inc) => new() { Minimum = min, Maximum = max, Increment = inc };
    private static decimal Clamp(NumericUpDown n, decimal v) => Math.Min(n.Maximum, Math.Max(n.Minimum, v));

    private Button ButtonSmall(string text)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 55, 90), ForeColor = Color.White, Margin = new Padding(5, 0, 5, 0) };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 115, 150);
        return b;
    }

    private Button ButtonAction(string text, Color back)
    {
        var b = new Button { Text = text, Width = 130, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold), Margin = new Padding(8) };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}

internal sealed class PinDialog : Form
{
    private readonly TextBox _pin = new();
    private bool _ok;

    private PinDialog()
    {
        Text = "PIN Pengaturan";
        Size = new Size(360, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(8, 24, 45); ForeColor = Color.White;
        var label = new Label { Text = "Masukkan PIN untuk membuka pengaturan", Dock = DockStyle.Top, Height = 48, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
        _pin.UseSystemPasswordChar = true; _pin.Width = 220; _pin.Font = new Font("Segoe UI", 14f); _pin.Location = new Point(62, 58);
        var ok = new Button { Text = "BUKA", Width = 100, Height = 34, Location = new Point(122, 104), BackColor = Color.FromArgb(31, 173, 113), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        ok.Click += (_, _) => { _ok = true; Close(); };
        AcceptButton = ok;
        Controls.Add(label); Controls.Add(_pin); Controls.Add(ok);
        Shown += (_, _) => _pin.Focus();
    }

    public static bool Verify(string expected, IWin32Window owner)
    {
        using var d = new PinDialog();
        d.ShowDialog(owner);
        if (!d._ok) return false;
        if (d._pin.Text == expected) return true;
        MessageBox.Show(owner, "PIN salah.", "MSM Pay Station", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }
}
