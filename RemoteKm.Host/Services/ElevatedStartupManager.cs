using System.Diagnostics;

namespace RemoteKm.Host.Services;

/// <summary>
/// Manages an "at logon, with highest privileges" Scheduled Task so the host can start
/// <b>elevated</b> at sign-in (a normal HKCU Run entry can't elevate). Creating/removing the
/// task needs administrator rights, so those operations prompt for elevation (UAC) when the
/// app isn't already elevated. Querying the task does not.
/// </summary>
public static class ElevatedStartupManager
{
    private const string TaskName = "RemoteKM";

    /// <summary>True if the elevated-startup scheduled task exists.</summary>
    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Creates the elevated logon task (prompts for admin). Returns true on success.</summary>
    public static bool Enable()
    {
        var exe = AutoStartManager.ExecutablePath;
        if (string.IsNullOrEmpty(exe))
            return false;

        // /TR value must be a quoted path (the exe name contains a space). The "\"...\"" form
        // makes the task action a single quoted token.
        var tr = "\"\\\"" + exe + "\\\"\"";
        return RunElevated($"/Create /TN \"{TaskName}\" /TR {tr} /SC ONLOGON /RL HIGHEST /F");
    }

    /// <summary>Removes the elevated logon task (prompts for admin). Returns true on success.</summary>
    public static bool Disable()
        => RunElevated($"/Delete /TN \"{TaskName}\" /F");

    private static bool RunElevated(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", arguments)
            {
                UseShellExecute = true,        // required for the "runas" verb
                Verb = "runas",                // elevate (UAC prompt if not already admin)
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            // Typically the user declined the UAC prompt.
            AppLog.Warn($"Elevated startup change failed/cancelled: {ex.Message}");
            return false;
        }
    }
}
