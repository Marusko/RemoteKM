using System.IO;

namespace RemoteKm.Client.Services;

/// <summary>
/// Minimal thread-safe file logger writing to the app data directory
/// (logs/client-yyyyMMdd.log). Exposed via the Status page "Export logs" button.
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();

    public static string LogDirectory { get; }
    public static string LogFile { get; }

    static AppLog()
    {
        LogDirectory = Path.Combine(FileSystem.AppDataDirectory, "logs");
        try { Directory.CreateDirectory(LogDirectory); } catch { /* ignored */ }
        LogFile = Path.Combine(LogDirectory, $"client-{DateTime.Now:yyyyMMdd}.log");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
                File.AppendAllText(LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Never let logging throw.
        }
    }
}
