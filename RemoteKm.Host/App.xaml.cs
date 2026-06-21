using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RemoteKm.Host.Services;
using RemoteKm.Host.Tray;
using RemoteKm.Host.Windows;

namespace RemoteKm.Host;

/// <summary>
/// Tray-only WPF application. Boots a DI container, starts the discovery and control
/// services, and shows a system-tray icon. No main window is ever shown.
/// </summary>
public partial class App : Application
{
    private ServiceProvider _provider = null!;
    private SettingsService _settings = null!;
    private TrustStore _trustStore = null!;
    private DiscoveryService _discovery = null!;
    private WebSocketServer _server = null!;
    private TrayIconManager _tray = null!;

    private SettingsWindow? _settingsWindow;
    private QrWindow? _qrWindow;
    private ConnectedClientsWindow? _connectedWindow;
    private TrustedDevicesWindow? _trustedWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Info("RemoteKM host starting.");
        DispatcherUnhandledException += (_, ex) => { AppLog.Error("Unhandled UI exception", ex.Exception); };

        // The app lives in the tray; dialogs closing must never shut it down.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<TrustStore>();
        services.AddSingleton<WebSocketServer>();
        // Discovery advertises the control server's *actual* bound port.
        services.AddSingleton<DiscoveryService>(sp =>
            new DiscoveryService(() => sp.GetRequiredService<WebSocketServer>().Port));
        _provider = services.BuildServiceProvider();

        _settings = _provider.GetRequiredService<SettingsService>();
        _trustStore = _provider.GetRequiredService<TrustStore>();
        _discovery = _provider.GetRequiredService<DiscoveryService>();
        _server = _provider.GetRequiredService<WebSocketServer>();

        InputInjector.ReverseScroll = _settings.Current.ReverseScroll;

        var dispatcher = Dispatcher;

        // Confirm unknown clients with a modal dialog on the UI thread.
        _server.ConfirmPairing = (request, remoteIp) =>
            dispatcher.InvokeAsync(() =>
            {
                var dialog = new PairingDialog(request.ClientName, remoteIp);
                dialog.ShowDialog();
                return dialog.Accepted;
            }).Task;

        _server.StartupFailed += msg => { AppLog.Warn($"Server startup: {msg}"); _tray?.ShowBalloon("RemoteKM — startup problem", msg); };
        _discovery.StartupFailed += msg => { AppLog.Warn($"Discovery startup: {msg}"); _tray?.ShowBalloon("RemoteKM — discovery problem", msg); };
        _server.SessionsChanged += () => AppLog.Info($"Active sessions: {_server.ActiveSessions.Count}");

        // Warn (and auto-release happens in the injector) if a key is stuck down too long.
        InputInjector.KeyHeldWarning += key =>
        {
            AppLog.Warn($"Key '{key}' held over 15s — auto-released.");
            _tray?.ShowBalloon("RemoteKM — key held too long",
                $"'{key}' was held for over 15 seconds and was released automatically.");
        };

        _tray = new TrayIconManager(
            _server, _trustStore, _settings,
            showConnected: ShowConnectedClientsWindow,
            showTrusted: ShowTrustedDevicesWindow,
            showQr: ShowQrWindow,
            showSettings: ShowSettingsWindow,
            exit: ExitApplication);

        _discovery.Start();
        _server.Start();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _server, RestartNetworkServicesAsync, RemoveAllDataAndExit);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowDialog();
    }

    private void RemoveAllDataAndExit() => _ = RemoveAllDataAndExitAsync();

    private async Task RemoveAllDataAndExitAsync()
    {
        try
        {
            await _server.DisconnectAllAsync().ConfigureAwait(true);
            await _discovery.StopAsync().ConfigureAwait(true);
            await _server.StopAsync().ConfigureAwait(true);
        }
        catch { /* ignored */ }

        AppDataCleaner.RemoveAll();

        _tray.Dispose();
        _provider.Dispose();
        Shutdown();
    }

    private void ShowConnectedClientsWindow()
    {
        if (_connectedWindow is not null) { _connectedWindow.Activate(); return; }
        _connectedWindow = new ConnectedClientsWindow(_server);
        _connectedWindow.Closed += (_, _) => _connectedWindow = null;
        _connectedWindow.Show();
    }

    private void ShowTrustedDevicesWindow()
    {
        if (_trustedWindow is not null) { _trustedWindow.Activate(); return; }
        _trustedWindow = new TrustedDevicesWindow(_trustStore);
        _trustedWindow.Closed += (_, _) => _trustedWindow = null;
        _trustedWindow.Show();
    }

    private void ShowQrWindow()
    {
        if (_qrWindow is not null)
        {
            _qrWindow.Activate();
            return;
        }

        _qrWindow = new QrWindow(_settings);
        _qrWindow.Closed += (_, _) => _qrWindow = null;
        _qrWindow.Show();
    }

    private async Task RestartNetworkServicesAsync()
    {
        await _discovery.StopAsync().ConfigureAwait(true);
        await _server.StopAsync().ConfigureAwait(true);
        _discovery.Start();
        _server.Start();
    }

    private void ExitApplication() => _ = ShutdownAsync();

    private async Task ShutdownAsync()
    {
        try
        {
            await _server.DisconnectAllAsync().ConfigureAwait(true);
            await _discovery.StopAsync().ConfigureAwait(true);
            await _server.StopAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore shutdown errors
        }
        finally
        {
            _tray.Dispose();
            _provider.Dispose();
            Shutdown();
        }
    }
}
