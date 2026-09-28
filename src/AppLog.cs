using System;
using System.IO;
using System.Text;

namespace MsmPayStation;

internal static class AppLog
{
    private static readonly object Sync = new();

    public static void Info(string area, string message) => Write("INFO", area, message);
    public static void Warn(string area, string message) => Write("WARN", area, message);
    public static void Error(string area, Exception ex) => Write("ERROR", area, ex.ToString());
    public static void Error(string area, string message) => Write("ERROR", area, message);

    private static void Write(string level, string area, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                var path = Path.Combine(AppPaths.LogDir, DateTime.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{area}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }
}
