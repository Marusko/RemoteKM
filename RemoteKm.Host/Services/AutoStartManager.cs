using System.Diagnostics;
using Microsoft.Win32;

namespace RemoteKm.Host.Services;

/// <summary>
/// Manages the HKCU "Run" registry entry that launches the host at logon.
/// The registry is the source of truth for auto-start state.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RemoteKM";

    internal static string ExecutablePath
    {
        get
        {
            // Prefer the real .exe path (Process.MainModule), not the .dll under dotnet.
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            return string.IsNullOrEmpty(path) ? Environment.ProcessPath ?? string.Empty : path!;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, $"\"{ExecutablePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Set(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }
}
