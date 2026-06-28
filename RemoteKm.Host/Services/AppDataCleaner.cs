using System.IO;

namespace RemoteKm.Host.Services;

/// <summary>
/// Removes everything the app persisted: the %APPDATA%\RemoteKM folder (settings, trusted
/// devices, logs) and the auto-start registry entry. Used by the "remove all data" action.
/// </summary>
public static class AppDataCleaner
{
    public static void RemoveAll()
    {
        try { AutoStartManager.Disable(); } catch { /* ignored */ }
        // Remove the elevated-startup scheduled task too (prompts for admin only if it exists).
        try { if (ElevatedStartupManager.IsEnabled()) ElevatedStartupManager.Disable(); } catch { /* ignored */ }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteKM");
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Some files (e.g. the active log) may be locked; best effort.
        }
    }
}
