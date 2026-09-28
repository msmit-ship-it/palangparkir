using System;
using System.IO;
using System.Windows.Forms;
using QRCoder;

namespace MsmPayStation;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        if (Environment.GetCommandLineArgs().Length >= 2 && Environment.GetCommandLineArgs()[1].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            var outFile = Environment.GetCommandLineArgs().Length >= 3 ? Environment.GetCommandLineArgs()[2] : Path.Combine(AppContext.BaseDirectory, "SELFTEST.txt");
            try
            {
                AppPaths.Ensure();
                using var gen = new QRCodeGenerator();
                using var data = gen.CreateQrCode("MSM-PAY-STATION-SELFTEST", QRCodeGenerator.ECCLevel.M);
                var ports = MGateRelay.GetPorts();
                File.WriteAllText(outFile, $"SELFTEST=PASS{Environment.NewLine}VERSION=1.0.0{Environment.NewLine}QR_MODULES={data.ModuleMatrix.Count}{Environment.NewLine}COM_COUNT={ports.Length}{Environment.NewLine}");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(outFile, "SELFTEST=FAIL" + Environment.NewLine + ex);
                Environment.ExitCode = 2;
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        AppPaths.Ensure();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Error("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Error("APP", ex);
        };

        var config = AppConfig.Load();
        Application.Run(new MainForm(config));
    }
}

internal static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;
    public static string ConfigFile => Path.Combine(BaseDir, "config.ini");
    public static string LogDir => Path.Combine(BaseDir, "logs");
    public static string SoundDir => Path.Combine(BaseDir, "sounds");

    public static void Ensure()
    {
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(SoundDir);
    }
}
