using System.IO;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteKm.Client.Services;

namespace RemoteKm.Client.ViewModels;

/// <summary>
/// Shows live connection stats (latency, uptime, byte counters) and a disconnect action.
/// </summary>
public partial class StatusViewModel : ObservableObject
{
    private readonly ConnectionService _connection;
    private readonly NavigationService _navigation;

    private IDispatcherTimer? _timer;

    public StatusViewModel(ConnectionService connection, NavigationService navigation)
    {
        _connection = connection;
        _navigation = navigation;
    }

    [ObservableProperty] private string _connectedTo = "—";
    [ObservableProperty] private string _latency = "—";
    [ObservableProperty] private string _uptime = "00:00:00";
    [ObservableProperty] private string _bytesSent = "0 B";
    [ObservableProperty] private string _bytesReceived = "0 B";

    public void OnAppearing()
    {
        Update();
        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Update();
        _timer.Start();
    }

    public void OnDisappearing()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Update()
    {
        ConnectedTo = string.IsNullOrEmpty(_connection.HostName)
            ? "—"
            : $"{_connection.HostName} ({_connection.HostIp}:{_connection.HostPort})";

        var ms = _connection.LatencyMs;
        Latency = ms <= 0 ? "—" : $"{ms:0} ms";

        Uptime = _connection.ConnectedAt is { } start
            ? (DateTime.Now - start).ToString(@"hh\:mm\:ss")
            : "00:00:00";

        BytesSent = FormatBytes(_connection.BytesSent);
        BytesReceived = FormatBytes(_connection.BytesReceived);
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        await _connection.DisconnectAsync();
        await _navigation.GoToDiscoveryAsync();
    }

    [RelayCommand]
    private Task Licenses() => _navigation.GoToAboutAsync();

    [RelayCommand]
    private async Task ExportLogs()
    {
        try
        {
            if (!File.Exists(AppLog.LogFile))
            {
                await Toast.Make("No logs to export yet.", ToastDuration.Short).Show();
                return;
            }
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "RemoteKm logs",
                File = new ShareFile(AppLog.LogFile),
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Export logs failed", ex);
            await Toast.Make("Could not export logs.", ToastDuration.Short).Show();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }
}
