using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using RemoteKm.Client.Messages;
using RemoteKm.Client.Services;
using RemoteKm.Shared;

namespace RemoteKm.Client.ViewModels;

/// <summary>
/// Drives the discovery page: live host list, manual connect, and the connect flow.
/// </summary>
public partial class DiscoveryViewModel : ObservableObject
{
    private const int StaleSeconds = 10;

    private readonly DiscoveryService _discovery;
    private readonly ConnectionService _connection;
    private readonly NavigationService _navigation;
    private readonly IMessenger _messenger;

    private IDispatcherTimer? _uiTimer;

    public ObservableCollection<HostItem> Hosts { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isManualExpanded;

    [ObservableProperty]
    private string _manualIp = string.Empty;

    [ObservableProperty]
    private string _manualPort = Protocol.ControlPort.ToString();

    public bool IsEmpty => Hosts.Count == 0 && !IsBusy;

    public DiscoveryViewModel(
        DiscoveryService discovery,
        ConnectionService connection,
        NavigationService navigation,
        IMessenger messenger)
    {
        _discovery = discovery;
        _connection = connection;
        _navigation = navigation;
        _messenger = messenger;
    }

    public void OnAppearing()
    {
        _discovery.HostDiscovered += OnHostDiscovered;
        _discovery.Start();

        // A QR scan completes on the ScanPage and reports the endpoint back here.
        _messenger.Register<DiscoveryViewModel, QrScannedMessage>(this,
            static (vm, msg) => _ = vm.ConnectAsync(msg.Ip, msg.Port, msg.Ip));

        _uiTimer = Application.Current!.Dispatcher.CreateTimer();
        _uiTimer.Interval = TimeSpan.FromSeconds(1);
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();
    }

    public async Task OnDisappearingAsync()
    {
        if (_uiTimer is not null)
        {
            _uiTimer.Stop();
            _uiTimer.Tick -= OnUiTick;
            _uiTimer = null;
        }

        _messenger.Unregister<QrScannedMessage>(this);
        _discovery.HostDiscovered -= OnHostDiscovered;
        await _discovery.StopAsync();
    }

    private void OnHostDiscovered(DiscoveredHost host)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = Hosts.FirstOrDefault(h => h.Ip == host.Ip && h.Port == host.Port);
            if (existing is null)
            {
                var item = new HostItem(host);
                item.Tick();
                Hosts.Add(item);
                OnPropertyChanged(nameof(IsEmpty));
            }
            else
            {
                existing.Refresh(host);
                existing.Tick();
            }
        });
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        for (int i = Hosts.Count - 1; i >= 0; i--)
        {
            var item = Hosts[i];
            item.Tick();
            if (item.SecondsAgo > StaleSeconds)
                Hosts.RemoveAt(i);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void ToggleManual() => IsManualExpanded = !IsManualExpanded;

    [RelayCommand]
    private Task ConnectToHost(HostItem? item)
        => item is null ? Task.CompletedTask : ConnectAsync(item.Ip, item.Port, item.HostName);

    [RelayCommand]
    private async Task ManualConnect()
    {
        var ip = ManualIp?.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            await ShowToastAsync("Enter a host IP address.");
            return;
        }
        if (!int.TryParse(ManualPort?.Trim(), out int port) || port is < 1 or > 65535)
        {
            await ShowToastAsync("Enter a valid port (1–65535).");
            return;
        }
        await ConnectAsync(ip, port, ip);
    }

    [RelayCommand]
    private Task ScanQr() => _navigation.GoToScanAsync();

    private async Task ConnectAsync(string ip, int port, string hostName)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _connection.ConnectAsync(ip, port, hostName);
            switch (result.Kind)
            {
                case ConnectResultKind.Accepted:
                    await _navigation.GoToMainAsync();
                    break;
                case ConnectResultKind.Rejected:
                    await ShowToastAsync(result.Message ?? "Connection rejected by the host.", ToastDuration.Long);
                    break;
                case ConnectResultKind.Timeout:
                    await ShowToastAsync(result.Message ?? "No response from the host.", ToastDuration.Long);
                    break;
                default:
                    await ShowToastAsync(result.Message ?? "Could not connect to the host.", ToastDuration.Long);
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Task ShowToastAsync(string message, ToastDuration duration = ToastDuration.Short)
        => Toast.Make(message, duration).Show();
}
