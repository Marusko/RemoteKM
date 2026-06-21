using System.Diagnostics;
using System.Globalization;
using System.Windows;
using RemoteKm.Host.Models;
using RemoteKm.Host.Services;

namespace RemoteKm.Host.Windows;

/// <summary>
/// Modal settings editor. Reflects actual registry state for auto-start, validates the
/// port, and asks the host to restart the network services when the port changes.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly WebSocketServer _server;
    private readonly Func<Task> _restartServices;
    private readonly Action _removeAllData;

    public SettingsWindow(SettingsService settings, WebSocketServer server, Func<Task> restartServices, Action removeAllData)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _settings = settings;
        _server = server;
        _restartServices = restartServices;
        _removeAllData = removeAllData;

        var current = settings.Current;
        PortBox.Text = current.ControlPort.ToString(CultureInfo.InvariantCulture);
        RequireConfirmCheck.IsChecked = current.RequireConfirmation;
        ReverseScrollCheck.IsChecked = current.ReverseScroll;
        // Reflect the true registry state, not just the stored setting.
        AutoStartCheck.IsChecked = AutoStartManager.IsEnabled();

        var ip = NetworkInfo.GetLocalIPv4();
        ListeningText.Text = $"Listening on {ip}:{_server.Port}";
    }

    private bool TryValidatePort(out int port)
    {
        port = 0;
        if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
            || port < 1024 || port > 65535)
        {
            PortError.Text = "Port must be an integer between 1024 and 65535.";
            PortError.Visibility = Visibility.Visible;
            return false;
        }
        PortError.Visibility = Visibility.Collapsed;
        return true;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryValidatePort(out int port))
            return;

        bool autoStart = AutoStartCheck.IsChecked == true;
        bool requireConfirm = RequireConfirmCheck.IsChecked == true;
        bool reverseScroll = ReverseScrollCheck.IsChecked == true;

        int oldPort = _settings.Current.ControlPort;

        var updated = new Settings
        {
            ControlPort = port,
            RequireConfirmation = requireConfirm,
            ReverseScroll = reverseScroll,
            AutoStart = autoStart,
        };
        _settings.Save(updated);

        // Apply scroll direction immediately.
        InputInjector.ReverseScroll = reverseScroll;

        try
        {
            AutoStartManager.Set(autoStart);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not update auto-start: {ex.Message}",
                "RemoteKM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (port != oldPort)
        {
            try
            {
                await _restartServices().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not restart services on the new port: {ex.Message}",
                    "RemoteKM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLog.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open logs folder", ex);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void OnRemoveAllData(object sender, RoutedEventArgs e)
    {
        bool confirmed = ConfirmDialog.Ask(this,
            "Remove all data",
            "This deletes all RemoteKM settings, trusted devices, and logs, removes the auto-start " +
            "registry entry, and exits the app. This cannot be undone.\n\nContinue?",
            confirmText: "Remove & exit");

        if (!confirmed)
            return;

        AppLog.Info("User requested full data removal.");
        // Close this window first, then let the app tear down, wipe data, and exit.
        DialogResult = false;
        _removeAllData();
    }
}
