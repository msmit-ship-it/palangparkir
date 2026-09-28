using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MsmPayStation;

public sealed class AppConfig
{
    public string BackendUrl { get; set; } = "https://pay.msmparking.com";
    public string DeviceKey { get; set; } = "";
    public string SiteId { get; set; } = "BDG001";
    public string PaystationId { get; set; } = "PS01";
    public string GateId { get; set; } = "OUT01";
    public string TerminalId { get; set; } = "BDG-PS01";
    public string DeviceId { get; set; } = "BDG-PS01";
    public decimal CarAmount { get; set; } = 10000m;
    public decimal MotorcycleAmount { get; set; } = 5000m;
    public int PollIntervalMs { get; set; } = 2000;
    public int ProviderRefreshSec { get; set; } = 10;
    public int QrTimeoutSec { get; set; } = 300;
    public int SuccessScreenSec { get; set; } = 4;
    public string SerialPort { get; set; } = "COM3";
    public int BaudRate { get; set; } = 9600;
    public string OpenHex { get; set; } = "A0 01 01 A2";
    public string CloseHex { get; set; } = "A0 01 00 A1";
    public int PulseMs { get; set; } = 800;
    public bool DryRunRelay { get; set; } = true;
    public int JoystickId { get; set; } = -1;
    public int CarButton { get; set; } = 1;
    public int MotorcycleButton { get; set; } = 2;
    public bool FullScreen { get; set; } = true;
    public string SettingsPin { get; set; } = "1234";

    public static AppConfig Load()
    {
        var cfg = new AppConfig();
        if (!System.IO.File.Exists(AppPaths.ConfigFile))
        {
            cfg.Save();
            return cfg;
        }

        cfg.BackendUrl = Ini.Read("Backend", "Url", cfg.BackendUrl);
        cfg.DeviceKey = Ini.Read("Backend", "DeviceKey", cfg.DeviceKey);
        cfg.SiteId = Ini.Read("Location", "SiteId", cfg.SiteId);
        cfg.PaystationId = Ini.Read("Location", "PaystationId", cfg.PaystationId);
        cfg.GateId = Ini.Read("Location", "GateId", cfg.GateId);
        cfg.TerminalId = Ini.Read("Location", "TerminalId", cfg.TerminalId);
        cfg.DeviceId = Ini.Read("Location", "DeviceId", cfg.DeviceId);
        cfg.CarAmount = ReadDecimal("Tariff", "CarAmount", cfg.CarAmount);
        cfg.MotorcycleAmount = ReadDecimal("Tariff", "MotorcycleAmount", cfg.MotorcycleAmount);
        cfg.PollIntervalMs = ReadInt("Network", "PollIntervalMs", cfg.PollIntervalMs, 500, 30000);
        cfg.ProviderRefreshSec = ReadInt("Network", "ProviderRefreshSec", cfg.ProviderRefreshSec, 5, 300);
        cfg.QrTimeoutSec = ReadInt("Network", "QrTimeoutSec", cfg.QrTimeoutSec, 60, 1800);
        cfg.SuccessScreenSec = ReadInt("UI", "SuccessScreenSec", cfg.SuccessScreenSec, 2, 30);
        cfg.SerialPort = Ini.Read("Relay", "SerialPort", cfg.SerialPort);
        cfg.BaudRate = ReadInt("Relay", "BaudRate", cfg.BaudRate, 1200, 921600);
        cfg.OpenHex = Ini.Read("Relay", "OpenHex", cfg.OpenHex);
        cfg.CloseHex = Ini.Read("Relay", "CloseHex", cfg.CloseHex);
        cfg.PulseMs = ReadInt("Relay", "PulseMs", cfg.PulseMs, 100, 5000);
        cfg.DryRunRelay = ReadBool("Relay", "DryRun", cfg.DryRunRelay);
        cfg.JoystickId = ReadInt("Encoder", "JoystickId", cfg.JoystickId, -1, 15);
        cfg.CarButton = ReadInt("Encoder", "CarButton", cfg.CarButton, 1, 32);
        cfg.MotorcycleButton = ReadInt("Encoder", "MotorcycleButton", cfg.MotorcycleButton, 1, 32);
        cfg.FullScreen = ReadBool("UI", "FullScreen", cfg.FullScreen);
        cfg.SettingsPin = Ini.Read("Security", "SettingsPin", cfg.SettingsPin);
        return cfg;
    }

    public void Save()
    {
        BackendUrl = (BackendUrl ?? "").Trim().TrimEnd('/');
        Ini.Write("Backend", "Url", BackendUrl);
        Ini.Write("Backend", "DeviceKey", DeviceKey ?? "");
        Ini.Write("Location", "SiteId", SiteId ?? "");
        Ini.Write("Location", "PaystationId", PaystationId ?? "");
        Ini.Write("Location", "GateId", GateId ?? "");
        Ini.Write("Location", "TerminalId", TerminalId ?? "");
        Ini.Write("Location", "DeviceId", DeviceId ?? "");
        Ini.Write("Tariff", "CarAmount", CarAmount.ToString(CultureInfo.InvariantCulture));
        Ini.Write("Tariff", "MotorcycleAmount", MotorcycleAmount.ToString(CultureInfo.InvariantCulture));
        Ini.Write("Network", "PollIntervalMs", PollIntervalMs.ToString());
        Ini.Write("Network", "ProviderRefreshSec", ProviderRefreshSec.ToString());
        Ini.Write("Network", "QrTimeoutSec", QrTimeoutSec.ToString());
        Ini.Write("UI", "SuccessScreenSec", SuccessScreenSec.ToString());
        Ini.Write("Relay", "SerialPort", SerialPort ?? "");
        Ini.Write("Relay", "BaudRate", BaudRate.ToString());
        Ini.Write("Relay", "OpenHex", OpenHex ?? "");
        Ini.Write("Relay", "CloseHex", CloseHex ?? "");
        Ini.Write("Relay", "PulseMs", PulseMs.ToString());
        Ini.Write("Relay", "DryRun", DryRunRelay ? "1" : "0");
        Ini.Write("Encoder", "JoystickId", JoystickId.ToString());
        Ini.Write("Encoder", "CarButton", CarButton.ToString());
        Ini.Write("Encoder", "MotorcycleButton", MotorcycleButton.ToString());
        Ini.Write("UI", "FullScreen", FullScreen ? "1" : "0");
        Ini.Write("Security", "SettingsPin", SettingsPin ?? "1234");
    }

    private static int ReadInt(string section, string key, int fallback, int min, int max)
    {
        var raw = Ini.Read(section, key, fallback.ToString());
        return int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;
    }

    private static decimal ReadDecimal(string section, string key, decimal fallback)
    {
        var raw = Ini.Read(section, key, fallback.ToString(CultureInfo.InvariantCulture));
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static bool ReadBool(string section, string key, bool fallback)
    {
        var raw = Ini.Read(section, key, fallback ? "1" : "0").Trim();
        return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class Ini
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder returnedString, uint size, string filePath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool WritePrivateProfileString(string section, string key, string? value, string filePath);

    public static string Read(string section, string key, string fallback)
    {
        var sb = new StringBuilder(4096);
        GetPrivateProfileString(section, key, fallback, sb, (uint)sb.Capacity, AppPaths.ConfigFile);
        return sb.ToString();
    }

    public static void Write(string section, string key, string value)
        => WritePrivateProfileString(section, key, value, AppPaths.ConfigFile);
}
