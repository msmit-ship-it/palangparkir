using QRCoder;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MsmPayStation;

public sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private MsmApiClient _api;
    private MGateRelay _relay;
    private JoystickEncoder _encoder;

    private readonly Panel _header = new();
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _backendStatus = new();
    private readonly Label _relayStatus = new();
    private readonly Label _encoderStatus = new();
    private readonly Panel _content = new();
    private readonly Label _footer = new();
    private readonly System.Windows.Forms.Timer _encoderTimer = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new();

    private CancellationTokenSource? _txCts;
    private PaymentTransaction? _currentTx;
    private DateTime _lastProviderRefresh = DateTime.MinValue;
    private bool _pollBusy;
    private bool _gateBusy;
    private string _state = "IDLE";

    private readonly Color Navy = Color.FromArgb(5, 19, 38);
    private readonly Color Navy2 = Color.FromArgb(9, 31, 58);
    private readonly Color Cyan = Color.FromArgb(52, 211, 235);
    private readonly Color Green = Color.FromArgb(48, 211, 135);
    private readonly Color Amber = Color.FromArgb(251, 191, 36);
    private readonly Color Red = Color.FromArgb(248, 113, 113);
    private readonly Color Muted = Color.FromArgb(158, 180, 205);

    public MainForm(AppConfig cfg)
    {
        _cfg = cfg;
        _api = new MsmApiClient(_cfg);
        _relay = new MGateRelay(_cfg);
        _encoder = new JoystickEncoder(_cfg);

        Text = "MSM PAY STATION";
        BackColor = Navy;
        ForeColor = Color.White;
        MinimumSize = new Size(900, 620);
        KeyPreview = true;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);

        BuildShell();
        ApplyWindowMode();
        ShowIdle();

        _encoderTimer.Interval = 70;
        _encoderTimer.Tick += EncoderTimer_Tick;
        _encoderTimer.Start();

        _pollTimer.Interval = Math.Clamp(_cfg.PollIntervalMs, 500, 30000);
        _pollTimer.Tick += async (_, _) => await PollPaymentAsync();

        _clockTimer.Interval = 500;
        _clockTimer.Tick += (_, _) => RefreshWaitingCountdown();
        _clockTimer.Start();

        Shown += async (_, _) => await CheckBackendAsync();
        KeyDown += MainForm_KeyDown;
        FormClosing += (_, _) => _txCts?.Cancel();
        Resize += (_, _) => LayoutShell();
    }

    private void BuildShell()
    {
        Controls.Add(_content);
        Controls.Add(_header);
        Controls.Add(_footer);

        _header.BackColor = Navy2;
        _content.BackColor = Navy;
        _footer.BackColor = Navy2;
        _footer.ForeColor = Muted;
        _footer.TextAlign = ContentAlignment.MiddleCenter;
        _footer.Text = "F1 Mobil   •   F2 Motor   •   F12 Pengaturan   |   msmparking.com";

        _title.Text = "MSM PAY STATION";
        _title.ForeColor = Color.White;
        _title.Font = new Font("Segoe UI Semibold", 24f, FontStyle.Bold);
        _title.AutoSize = true;

        _subtitle.Text = $"{_cfg.SiteId}  /  {_cfg.PaystationId}  /  {_cfg.GateId}";
        _subtitle.ForeColor = Muted;
        _subtitle.Font = new Font("Segoe UI", 11f);
        _subtitle.AutoSize = true;

        _backendStatus.AutoSize = true;
        _relayStatus.AutoSize = true;
        _encoderStatus.AutoSize = true;
        _backendStatus.Font = _relayStatus.Font = _encoderStatus.Font = new Font("Segoe UI Semibold", 10f);

        _header.Controls.Add(_title);
        _header.Controls.Add(_subtitle);
        _header.Controls.Add(_backendStatus);
        _header.Controls.Add(_relayStatus);
        _header.Controls.Add(_encoderStatus);
        SetBackendStatus(false, "BACKEND: CHECK");
        SetRelayStatus();
        SetEncoderStatus(false);
        LayoutShell();
    }

    private void LayoutShell()
    {
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        _header.SetBounds(0, 0, w, 92);
        _footer.SetBounds(0, Math.Max(92, h - 38), w, 38);
        _content.SetBounds(0, 92, w, Math.Max(100, h - 130));

        _title.Location = new Point(28, 15);
        _subtitle.Location = new Point(31, 56);

        var x = Math.Max(400, w - 590);
        _backendStatus.Location = new Point(x, 24);
        _relayStatus.Location = new Point(x + 200, 24);
        _encoderStatus.Location = new Point(x + 385, 24);
    }

    private void ApplyWindowMode()
    {
        if (_cfg.FullScreen)
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            Size = new Size(1120, 760);
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F12)
        {
            e.Handled = true;
            OpenSettings();
            return;
        }
        if (_state != "IDLE") return;
        if (e.KeyCode == Keys.F1) { e.Handled = true; _ = StartPaymentAsync("CAR", _cfg.CarAmount); }
        if (e.KeyCode == Keys.F2) { e.Handled = true; _ = StartPaymentAsync("MOTORCYCLE", _cfg.MotorcycleAmount); }
    }

    private void EncoderTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var p = _encoder.Poll();
            SetEncoderStatus(p.connected);
            if (_state != "IDLE") return;
            if (p.carPressed) _ = StartPaymentAsync("CAR", _cfg.CarAmount);
            if (p.motorcyclePressed) _ = StartPaymentAsync("MOTORCYCLE", _cfg.MotorcycleAmount);
        }
        catch (Exception ex)
        {
            AppLog.Error("ENCODER", ex);
            SetEncoderStatus(false);
        }
    }

    private void OpenSettings()
    {
        if (!PinDialog.Verify(_cfg.SettingsPin, this)) return;
        using var dlg = new SettingsForm(_cfg);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _cfg.Save();
            _api.Dispose();
            _api = new MsmApiClient(_cfg);
            _relay = new MGateRelay(_cfg);
            _encoder = new JoystickEncoder(_cfg);
            _pollTimer.Interval = Math.Clamp(_cfg.PollIntervalMs, 500, 30000);
            _subtitle.Text = $"{_cfg.SiteId}  /  {_cfg.PaystationId}  /  {_cfg.GateId}";
            SetRelayStatus();
            ApplyWindowMode();
            _ = CheckBackendAsync();
        }
    }

    private async Task CheckBackendAsync()
    {
        var r = await _api.TestBackendAsync();
        SetBackendStatus(r.ok, r.ok ? "BACKEND: ONLINE" : "BACKEND: OFFLINE");
        if (!r.ok) AppLog.Warn("BACKEND", r.message);
    }

    private void SetBackendStatus(bool ok, string text)
    {
        _backendStatus.Text = text;
        _backendStatus.ForeColor = ok ? Green : Amber;
    }

    private void SetRelayStatus()
    {
        _relayStatus.Text = _cfg.DryRunRelay ? "RELAY: DRY RUN" : $"RELAY: {_cfg.SerialPort}";
        _relayStatus.ForeColor = _cfg.DryRunRelay ? Amber : Cyan;
    }

    private void SetEncoderStatus(bool connected)
    {
        _encoderStatus.Text = connected ? $"ENCODER: JOY {_encoder.ActiveId}" : "ENCODER: KEYBOARD";
        _encoderStatus.ForeColor = connected ? Green : Muted;
    }

    private void ClearContent()
    {
        foreach (Control c in _content.Controls) c.Dispose();
        _content.Controls.Clear();
    }

    private void ShowIdle()
    {
        _state = "IDLE";
        _currentTx = null;
        _pollTimer.Stop();
        _txCts?.Cancel();
        _txCts?.Dispose();
        _txCts = null;
        ClearContent();

        var heading = NewLabel("SILAKAN PILIH KENDARAAN", 28, Color.White, FontStyle.Bold);
        heading.TextAlign = ContentAlignment.MiddleCenter;
        heading.Dock = DockStyle.Top;
        heading.Height = 100;
        heading.Padding = new Padding(0, 28, 0, 0);
        _content.Controls.Add(heading);

        var info = NewLabel("Tekan tombol fisik pada box atau pilih di layar", 14, Muted, FontStyle.Regular);
        info.TextAlign = ContentAlignment.MiddleCenter;
        info.Dock = DockStyle.Top;
        info.Height = 44;
        _content.Controls.Add(info);
        info.BringToFront();

        var holder = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(70, 40, 70, 70)
        };
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _content.Controls.Add(holder);

        holder.Controls.Add(VehicleButton("MOBIL", "Rp " + Money(_cfg.CarAmount), Cyan, async () => await StartPaymentAsync("CAR", _cfg.CarAmount)), 0, 0);
        holder.Controls.Add(VehicleButton("MOTOR", "Rp " + Money(_cfg.MotorcycleAmount), Green, async () => await StartPaymentAsync("MOTORCYCLE", _cfg.MotorcycleAmount)), 1, 0);
        holder.BringToFront();
    }

    private Control VehicleButton(string title, string amount, Color accent, Func<Task> action)
    {
        var card = new Button
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(22),
            FlatStyle = FlatStyle.Flat,
            BackColor = Navy2,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Text = $"{title}\r\n\r\n{amount}",
            Font = new Font("Segoe UI Semibold", 28f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        card.FlatAppearance.BorderSize = 3;
        card.FlatAppearance.BorderColor = accent;
        card.Click += async (_, _) => await action();
        return card;
    }

    private async Task StartPaymentAsync(string vehicleType, decimal amount)
    {
        if (_state != "IDLE") return;
        if (string.IsNullOrWhiteSpace(_cfg.DeviceKey))
        {
            MessageBox.Show(this, "Device Key belum diisi. Tekan F12 > Pengaturan.", "MSM Pay Station", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _state = "CREATING";
        _txCts = new CancellationTokenSource();
        ShowMessage("MEMBUAT QRIS", "Menghubungi server MSM Pay...", Cyan);
        try
        {
            var tx = await _api.CreatePaymentAsync(vehicleType, amount, _txCts.Token);
            _currentTx = tx;
            if (string.IsNullOrWhiteSpace(tx.QrContent)) throw new InvalidOperationException("Server tidak mengembalikan qr_content");
            _state = "WAITING";
            _lastProviderRefresh = DateTime.MinValue;
            ShowWaiting(tx);
            _pollTimer.Start();
            await PollPaymentAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("PAYMENT", ex);
            SetBackendStatus(false, "BACKEND: ERROR");
            ShowFailure("GAGAL MEMBUAT QRIS", ex.Message, true);
        }
    }

    private void ShowWaiting(PaymentTransaction tx)
    {
        ClearContent();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(70, 42, 70, 54),
            BackColor = Navy
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        _content.Controls.Add(layout);

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(12, 0, 35, 0), Padding = new Padding(28) };
        var qrBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        qrBox.Image = MakeQr(tx.QrContent);
        left.Controls.Add(qrBox);
        layout.Controls.Add(left, 0, 0);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Navy, Padding = new Padding(15, 15, 15, 15) };
        layout.Controls.Add(right, 1, 0);

        var vehicle = tx.VehicleType == "MOTORCYCLE" ? "MOTOR" : "MOBIL";
        var title = NewLabel("SCAN QRIS UNTUK MEMBAYAR", 24, Color.White, FontStyle.Bold);
        title.Dock = DockStyle.Top; title.Height = 76; title.TextAlign = ContentAlignment.MiddleLeft;
        right.Controls.Add(title);

        var amount = NewLabel("Rp " + Money(tx.Amount), 36, Cyan, FontStyle.Bold);
        amount.Name = "AmountLabel"; amount.Dock = DockStyle.Top; amount.Height = 76; amount.TextAlign = ContentAlignment.MiddleLeft;
        right.Controls.Add(amount); amount.BringToFront();

        var vehicleLabel = NewLabel(vehicle + "  •  " + _cfg.GateId, 14, Muted, FontStyle.Bold);
        vehicleLabel.Dock = DockStyle.Top; vehicleLabel.Height = 46;
        right.Controls.Add(vehicleLabel); vehicleLabel.BringToFront();

        var refLabel = NewLabel("REF: " + tx.ReferenceNo, 10, Muted, FontStyle.Regular);
        refLabel.Dock = DockStyle.Top; refLabel.Height = 32;
        right.Controls.Add(refLabel); refLabel.BringToFront();

        var waiting = NewLabel("MENUNGGU PEMBAYARAN", 17, Amber, FontStyle.Bold);
        waiting.Name = "WaitingLabel"; waiting.Dock = DockStyle.Bottom; waiting.Height = 54; waiting.TextAlign = ContentAlignment.MiddleLeft;
        right.Controls.Add(waiting);

        var countdown = NewLabel("05:00", 24, Color.White, FontStyle.Bold);
        countdown.Name = "CountdownLabel"; countdown.Dock = DockStyle.Bottom; countdown.Height = 58; countdown.TextAlign = ContentAlignment.MiddleLeft;
        right.Controls.Add(countdown); countdown.BringToFront();

        var cancel = new Button
        {
            Text = "BATAL / KEMBALI",
            Dock = DockStyle.Bottom,
            Height = 52,
            FlatStyle = FlatStyle.Flat,
            BackColor = Navy2,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold)
        };
        cancel.FlatAppearance.BorderColor = Muted;
        cancel.Click += (_, _) => ShowIdle();
        right.Controls.Add(cancel); cancel.BringToFront();
    }

    private void RefreshWaitingCountdown()
    {
        if (_state != "WAITING" || _currentTx == null) return;
        var c = FindControl(_content, "CountdownLabel") as Label;
        if (c == null) return;
        var expiry = _currentTx.ExpiresAt ?? DateTimeOffset.Now.AddSeconds(_cfg.QrTimeoutSec);
        var remain = expiry - DateTimeOffset.Now;
        if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
        c.Text = $"{(int)remain.TotalMinutes:00}:{remain.Seconds:00}";
    }

    private async Task PollPaymentAsync()
    {
        if (_pollBusy || _currentTx == null || _state != "WAITING") return;
        _pollBusy = true;
        try
        {
            var refresh = (DateTime.Now - _lastProviderRefresh).TotalSeconds >= _cfg.ProviderRefreshSec;
            if (refresh) _lastProviderRefresh = DateTime.Now;
            var tx = await _api.GetStatusAsync(_currentTx.ReferenceNo, refresh, _txCts?.Token ?? CancellationToken.None);
            _currentTx = tx;
            SetBackendStatus(true, "BACKEND: ONLINE");
            switch (tx.Status.ToUpperInvariant())
            {
                case "PAID":
                    _pollTimer.Stop();
                    await ProcessPaidAsync(tx);
                    break;
                case "GATE_OPENED":
                    _pollTimer.Stop();
                    await ShowSuccessAndResetAsync(tx.Amount);
                    break;
                case "EXPIRED":
                    _pollTimer.Stop();
                    ShowFailure("QRIS KEDALUWARSA", "Silakan buat transaksi baru.", true);
                    break;
                case "FAILED":
                case "CANCELLED":
                    _pollTimer.Stop();
                    ShowFailure("TRANSAKSI GAGAL", "Silakan ulangi transaksi.", true);
                    break;
                default:
                    if (tx.ExpiresAt.HasValue && DateTimeOffset.Now > tx.ExpiresAt.Value.AddSeconds(2))
                    {
                        _pollTimer.Stop();
                        ShowFailure("QRIS KEDALUWARSA", "Silakan buat transaksi baru.", true);
                    }
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("POLL", ex);
            SetBackendStatus(false, "BACKEND: RETRY");
            var lbl = FindControl(_content, "WaitingLabel") as Label;
            if (lbl != null) lbl.Text = "KONEKSI TERPUTUS — MENCOBA LAGI";
        }
        finally { _pollBusy = false; }
    }

    private async Task ProcessPaidAsync(PaymentTransaction tx)
    {
        if (_gateBusy) return;
        _gateBusy = true;
        _state = "PAID";
        PlaySound("paid.wav");
        ShowMessage("PEMBAYARAN BERHASIL", "Menyiapkan palang keluar...", Green);
        try
        {
            var claim = await _api.ClaimGateAsync(tx.ReferenceNo, _txCts?.Token ?? CancellationToken.None);
            if (!claim.Success || !claim.OpenGate || string.IsNullOrWhiteSpace(claim.ClaimToken))
            {
                throw new InvalidOperationException("Gate claim ditolak: " + (string.IsNullOrWhiteSpace(claim.Reason) ? "unknown" : claim.Reason));
            }

            ShowMessage("MEMBUKA PALANG", _cfg.DryRunRelay ? "MODE TEST — tanpa relay fisik" : $"M Gate {_cfg.SerialPort}", Cyan);
            var pulse = claim.PulseMs > 0 ? claim.PulseMs : _cfg.PulseMs;
            var relayResult = await _relay.PulseAsync(pulse, _txCts?.Token ?? CancellationToken.None);
            var ackOk = await _api.AckGateAsync(tx.ReferenceNo, claim.ClaimToken, relayResult.ok, relayResult.ok ? null : relayResult.message, _txCts?.Token ?? CancellationToken.None);

            if (!relayResult.ok) throw new InvalidOperationException("Relay gagal: " + relayResult.message + (ackOk ? "" : " | ACK gagal"));
            if (!ackOk) throw new InvalidOperationException("Palang dipulse tetapi ACK server gagal. Cek log sebelum mengulang transaksi.");

            PlaySound("open.wav");
            await ShowSuccessAndResetAsync(tx.Amount);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("GATE", ex);
            ShowGateFailure(tx, ex.Message);
        }
        finally { _gateBusy = false; }
    }

    private void ShowGateFailure(PaymentTransaction tx, string message)
    {
        _state = "GATE_ERROR";
        ClearContent();
        var panel = CenterPanel();
        panel.Controls.Add(NewCenteredLabel("PEMBAYARAN SUDAH BERHASIL", 25, Green, FontStyle.Bold, 70));
        panel.Controls.Add(NewCenteredLabel("PALANG BELUM TERBUKA", 27, Red, FontStyle.Bold, 72));
        panel.Controls.Add(NewCenteredLabel(message, 12, Muted, FontStyle.Regular, 84));
        var retry = new Button
        {
            Text = "COBA BUKA PALANG LAGI",
            Width = 330,
            Height = 58,
            FlatStyle = FlatStyle.Flat,
            BackColor = Navy2,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            Anchor = AnchorStyles.None
        };
        retry.FlatAppearance.BorderColor = Amber;
        retry.Click += async (_, _) =>
        {
            _state = "WAITING";
            _currentTx = tx;
            await ProcessPaidAsync(tx);
        };
        panel.Controls.Add(retry);
        panel.Resize += (_, _) => retry.Location = new Point((panel.Width - retry.Width) / 2, Math.Max(270, panel.Height - 130));
        retry.Location = new Point((panel.Width - retry.Width) / 2, Math.Max(270, panel.Height - 130));
        _content.Controls.Add(panel);
    }

    private async Task ShowSuccessAndResetAsync(decimal amount)
    {
        _state = "SUCCESS";
        ShowMessage("PEMBAYARAN BERHASIL", $"Rp {Money(amount)}\r\n\r\nSILAKAN JALAN — PALANG TERBUKA", Green, 29);
        try { await Task.Delay(TimeSpan.FromSeconds(_cfg.SuccessScreenSec), _txCts?.Token ?? CancellationToken.None); }
        catch { }
        if (!IsDisposed) BeginInvoke(new Action(ShowIdle));
    }

    private void ShowFailure(string title, string message, bool autoReset)
    {
        _state = "FAIL";
        ShowMessage(title, message, Red);
        if (autoReset)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 4500 };
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); if (!IsDisposed) ShowIdle(); };
            timer.Start();
        }
    }

    private void ShowMessage(string title, string message, Color accent, float titleSize = 26)
    {
        ClearContent();
        var panel = CenterPanel();
        panel.Controls.Add(NewCenteredLabel(title, titleSize, accent, FontStyle.Bold, 94));
        panel.Controls.Add(NewCenteredLabel(message, 15, Color.White, FontStyle.Regular, 150));
        _content.Controls.Add(panel);
    }

    private Panel CenterPanel() => new() { Dock = DockStyle.Fill, BackColor = Navy, Padding = new Padding(80, 80, 80, 80) };

    private Label NewCenteredLabel(string text, float size, Color color, FontStyle style, int height)
    {
        var l = NewLabel(text, size, color, style);
        l.Dock = DockStyle.Top;
        l.Height = height;
        l.TextAlign = ContentAlignment.MiddleCenter;
        return l;
    }

    private static Label NewLabel(string text, float size, Color color, FontStyle style)
        => new()
        {
            Text = text,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", size, style),
            AutoEllipsis = true
        };

    private static string Money(decimal value) => value.ToString("N0", CultureInfo.GetCultureInfo("id-ID"));

    private static Bitmap MakeQr(string content)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M, forceUtf8: true, utf8BOM: false);
        using var code = new QRCode(data);
        return code.GetGraphic(12, Color.Black, Color.White, drawQuietZones: true);
    }

    private static Control? FindControl(Control root, string name)
    {
        if (root.Name == name) return root;
        foreach (Control child in root.Controls)
        {
            var found = FindControl(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void PlaySound(string fileName)
    {
        try
        {
            var path = Path.Combine(AppPaths.SoundDir, fileName);
            if (File.Exists(path)) new SoundPlayer(path).Play();
            else SystemSounds.Asterisk.Play();
        }
        catch { }
    }
}
