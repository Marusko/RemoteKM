using System.IO;
using System.Text.Json;
using RemoteKm.Host.Models;

namespace RemoteKm.Host.Services;

/// <summary>
/// Loads and persists <see cref="Settings"/> to %APPDATA%\RemoteKM\settings.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _lock = new();
    private readonly string _path;

    public Settings Current { get; private set; } = new();

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteKM");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    Current = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
                }
                else
                {
                    Current = new Settings();
                    SaveLocked();
                }
            }
            catch
            {
                // Corrupt file → fall back to defaults rather than crashing the tray app.
                Current = new Settings();
            }
        }
    }

    /// <summary>Replaces the current settings and persists them.</summary>
    public void Save(Settings settings)
    {
        lock (_lock)
        {
            Current = settings;
            SaveLocked();
        }
        AppLog.Info($"Settings saved: port={settings.ControlPort}, requireConfirm={settings.RequireConfirmation}, autoStart={settings.AutoStart}");
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
