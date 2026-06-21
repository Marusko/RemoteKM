using System.Windows;
using System.Windows.Threading;

namespace RemoteKm.Host.Windows;

/// <summary>
/// Modal, always-on-top dialog asking the user to confirm an unknown client.
/// Auto-rejects after 30 seconds so a connection slot is never blocked indefinitely.
/// </summary>
public partial class PairingDialog : Window
{
    private const int CountdownSeconds = 30;

    private readonly DispatcherTimer _timer;
    private int _remaining = CountdownSeconds;

    /// <summary>True if the user accepted; false if rejected or timed out.</summary>
    public bool Accepted { get; private set; }

    public PairingDialog(string clientName, string remoteIp)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        MessageText.Text = $"'{clientName}' ({remoteIp}) wants to control this PC.";
        UpdateCountdown();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();

        Loaded += (_, _) =>
        {
            // Force the window to the foreground even if the user is busy elsewhere.
            Activate();
            Topmost = true;
            AcceptButton.Focus();
        };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            Close(false);
            return;
        }
        UpdateCountdown();
    }

    private void UpdateCountdown()
        => CountdownText.Text = $"Auto-rejects in {_remaining}s if no response.";

    private void OnAccept(object sender, RoutedEventArgs e) => Close(true);

    private void OnReject(object sender, RoutedEventArgs e) => Close(false);

    private void Close(bool accepted)
    {
        _timer.Stop();
        Accepted = accepted;
        DialogResult = accepted;
    }
}
