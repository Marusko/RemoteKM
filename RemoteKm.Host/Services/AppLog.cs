using System.IO;

namespace RemoteKm.Host.Services;

/// <summary>
/// Minimal thread-safe file logger. Writes to %APPDATA%\RemoteKM\logs\host-yyyyMMdd.log.
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();
    private static readonly string Dir;
    private static readonly string File;

    static AppLog()
    {
        Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteKM", "logs");
        try { Directory.CreateDirectory(Dir); } catch { /* ignored */ }
        File = Path.Combine(Dir, $"host-{DateTime.Now:yyyyMMdd}.log");
    }

    public static string LogDirectory => Dir;
    public static string LogFile => File;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
                System.IO.File.AppendAllText(File,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Never let logging throw.
        }
    }
}
