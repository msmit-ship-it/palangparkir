using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace MsmPayStation;

public sealed class PaymentTransaction
{
    public string ReferenceNo { get; set; } = "";
    public string Status { get; set; } = "";
    public string QrContent { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool CanClaim { get; set; }
    public string VehicleType { get; set; } = "";
}

public sealed class GateClaim
{
    public bool Success { get; set; }
    public bool OpenGate { get; set; }
    public string ClaimToken { get; set; } = "";
    public int PulseMs { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class MsmApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppConfig _cfg;

    public MsmApiClient(AppConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MSM-PayStation/1.0");
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? json = null)
    {
        var url = (_cfg.BackendUrl ?? "").TrimEnd('/') + path;
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(_cfg.DeviceKey)) req.Headers.TryAddWithoutValidation("X-MSM-DEVICE-KEY", _cfg.DeviceKey.Trim());
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }

    private static async Task<(int code, string body)> SendAsync(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ((int)resp.StatusCode, body);
    }

    public async Task<(bool ok, string message)> TestBackendAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Get, "/health");
            var (code, body) = await SendAsync(_http, req, ct);
            if (code is >= 200 and < 300) return (true, "Backend aktif");
            return (false, $"HTTP {code}: {Trim(body, 180)}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<PaymentTransaction> CreatePaymentAsync(string vehicleType, decimal amount, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["site"] = _cfg.SiteId,
            ["paystation"] = _cfg.PaystationId,
            ["gate"] = _cfg.GateId,
            ["terminal_id"] = _cfg.TerminalId,
            ["vehicle_type"] = vehicleType,
            ["amount"] = amount
        });
        using var req = Request(HttpMethod.Post, "/api/v1/payment/create", payload);
        var (code, body) = await SendAsync(_http, req, ct);
        AppLog.Info("HTTP", $"CREATE -> {code} {Trim(body, 600)}");
        if (code is < 200 or >= 300) throw new InvalidOperationException($"Create payment gagal HTTP {code}: {GetError(body)}");
        using var doc = JsonDocument.Parse(body);
        if (!GetBool(doc.RootElement, "success")) throw new InvalidOperationException(GetError(body));
        var tx = doc.RootElement.GetProperty("transaction");
        return ParseTransaction(tx, false);
    }

    public async Task<PaymentTransaction> GetStatusAsync(string reference, bool refreshProvider, CancellationToken ct = default)
    {
        var path = "/api/v1/payment/status/" + Uri.EscapeDataString(reference) + (refreshProvider ? "?refresh=1" : "");
        using var req = Request(HttpMethod.Get, path);
        var (code, body) = await SendAsync(_http, req, ct);
        AppLog.Info("HTTP", $"STATUS -> {code} {Trim(body, 500)}");
        if (code is < 200 or >= 300) throw new InvalidOperationException($"Status gagal HTTP {code}: {GetError(body)}");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!GetBool(root, "success")) throw new InvalidOperationException(GetError(body));
        var tx = ParseTransaction(root.GetProperty("transaction"), false);
        if (root.TryGetProperty("gate", out var gate) && gate.ValueKind == JsonValueKind.Object && gate.TryGetProperty("can_claim", out var cc))
            tx.CanClaim = cc.ValueKind == JsonValueKind.True;
        return tx;
    }

    public async Task<GateClaim> ClaimGateAsync(string reference, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["reference_no"] = reference,
            ["device_id"] = _cfg.DeviceId
        });
        using var req = Request(HttpMethod.Post, "/api/v1/gate/claim", payload);
        var (code, body) = await SendAsync(_http, req, ct);
        AppLog.Info("HTTP", $"CLAIM -> {code} {Trim(body, 600)}");
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        var result = new GateClaim
        {
            Success = GetBool(root, "success"),
            OpenGate = GetBool(root, "open_gate"),
            ClaimToken = GetString(root, "claim_token"),
            PulseMs = GetInt(root, "pulse_ms", _cfg.PulseMs),
            Reason = GetString(root, "reason")
        };
        if (code is < 200 or >= 300 || !result.Success)
        {
            if (string.IsNullOrWhiteSpace(result.Reason)) result.Reason = GetError(body);
        }
        return result;
    }

    public async Task<bool> AckGateAsync(string reference, string token, bool success, string? message, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["reference_no"] = reference,
            ["claim_token"] = token,
            ["success"] = success,
            ["message"] = message
        });
        using var req = Request(HttpMethod.Post, "/api/v1/gate/ack", payload);
        var (code, body) = await SendAsync(_http, req, ct);
        AppLog.Info("HTTP", $"ACK -> {code} {Trim(body, 600)}");
        if (code is < 200 or >= 300) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GetBool(doc.RootElement, "success");
        }
        catch { return false; }
    }

    private static PaymentTransaction ParseTransaction(JsonElement tx, bool canClaim)
    {
        var p = new PaymentTransaction
        {
            ReferenceNo = GetString(tx, "reference_no"),
            Status = GetString(tx, "status"),
            QrContent = GetString(tx, "qr_content"),
            Amount = GetDecimal(tx, "amount"),
            CanClaim = canClaim,
            VehicleType = GetString(tx, "vehicle_type")
        };
        var exp = GetString(tx, "expires_at");
        if (DateTimeOffset.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto)) p.ExpiresAt = dto;
        return p;
    }

    private static bool GetBool(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var v) && v));
    private static string GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? (p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ToString()) : "";
    private static int GetInt(JsonElement e, string name, int fallback)
        => e.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : fallback;
    private static decimal GetDecimal(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return 0m;
        if (p.TryGetDecimal(out var d)) return d;
        if (decimal.TryParse(p.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        return 0m;
    }
    private static string GetError(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var root = d.RootElement;
            var err = GetString(root, "error");
            return string.IsNullOrWhiteSpace(err) ? Trim(json, 220) : err;
        }
        catch { return Trim(json, 220); }
    }
    private static string Trim(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";

    public void Dispose() => _http.Dispose();
}

public sealed class MGateRelay
{
    private readonly AppConfig _cfg;
    public MGateRelay(AppConfig cfg) => _cfg = cfg;

    public static string[] GetPorts()
    {
        var ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
        return ports;
    }

    public async Task<(bool ok, string message)> PulseAsync(int? overridePulseMs = null, CancellationToken ct = default)
    {
        var pulse = Math.Clamp(overridePulseMs ?? _cfg.PulseMs, 100, 5000);
        if (_cfg.DryRunRelay)
        {
            AppLog.Info("RELAY", $"DRY RUN pulse {pulse}ms");
            await Task.Delay(Math.Min(pulse, 1500), ct).ConfigureAwait(false);
            return (true, "DRY RUN");
        }

        if (string.IsNullOrWhiteSpace(_cfg.SerialPort)) return (false, "COM port belum diset");
        try
        {
            var open = ParseHex(_cfg.OpenHex);
            var close = ParseHex(_cfg.CloseHex);
            using var port = new SerialPort(_cfg.SerialPort.Trim(), _cfg.BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 1000,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };
            port.Open();
            port.Write(open, 0, open.Length);
            port.BaseStream.Flush();
            AppLog.Info("RELAY", $"OPEN {_cfg.SerialPort} HEX={BitConverter.ToString(open)}");
            await Task.Delay(pulse, ct).ConfigureAwait(false);
            port.Write(close, 0, close.Length);
            port.BaseStream.Flush();
            AppLog.Info("RELAY", $"CLOSE {_cfg.SerialPort} HEX={BitConverter.ToString(close)}");
            return (true, "OK");
        }
        catch (Exception ex)
        {
            AppLog.Error("RELAY", ex);
            return (false, ex.Message);
        }
    }

    private static byte[] ParseHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("HEX kosong");
        var parts = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                        .Replace(",", " ").Replace("-", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++) bytes[i] = Convert.ToByte(parts[i], 16);
        return bytes;
    }
}

public sealed class JoystickEncoder
{
    private readonly AppConfig _cfg;
    private uint _lastButtons;
    private int _activeId = -1;

    public JoystickEncoder(AppConfig cfg) => _cfg = cfg;
    public int ActiveId => _activeId;
    public uint LastButtons => _lastButtons;

    public (bool carPressed, bool motorcyclePressed, bool connected) Poll()
    {
        var id = _cfg.JoystickId >= 0 ? _cfg.JoystickId : DetectFirst();
        _activeId = id;
        if (id < 0) { _lastButtons = 0; return (false, false, false); }
        var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };
        var result = joyGetPosEx((uint)id, ref info);
        if (result != 0) { _lastButtons = 0; return (false, false, false); }
        var current = info.dwButtons;
        var rising = current & ~_lastButtons;
        _lastButtons = current;
        var carMask = Mask(_cfg.CarButton);
        var motoMask = Mask(_cfg.MotorcycleButton);
        return ((rising & carMask) != 0, (rising & motoMask) != 0, true);
    }

    private static uint Mask(int button) => button is >= 1 and <= 32 ? 1u << (button - 1) : 0u;

    private static int DetectFirst()
    {
        var count = Math.Min((int)joyGetNumDevs(), 16);
        for (var i = 0; i < count; i++)
        {
            var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };
            if (joyGetPosEx((uint)i, ref info) == 0) return i;
        }
        return -1;
    }

    private const uint JOY_RETURNBUTTONS = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();
    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
}
