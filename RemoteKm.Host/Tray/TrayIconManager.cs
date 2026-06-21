using System.Drawing;
using System.Reflection;
using System.Windows.Threading;
using RemoteKm.Host.Models;
using RemoteKm.Host.Services;
using WinForms = System.Windows.Forms;

namespace RemoteKm.Host.Tray;

/// <summary>
/// Owns the system-tray NotifyIcon and its dynamically-built context menu.
/// All NotifyIcon mutations are marshaled onto the WPF UI thread.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly WebSocketServer _server;
    private readonly TrustStore _trustStore;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;

    private readonly Action _showConnected;
    private readonly Action _showTrusted;
    private readonly Action _showQr;
    private readonly Action _showSettings;
    private readonly Action _exit;

    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Icon _idleIcon;
    private readonly Icon _activeIcon;

    public TrayIconManager(
        WebSocketServer server,
        TrustStore trustStore,
        SettingsService settings,
        Action showConnected,
        Action showTrusted,
        Action showQr,
        Action showSettings,
        Action exit)
    {
        _server = server;
        _trustStore = trustStore;
        _settings = settings;
        _showConnected = showConnected;
        _showTrusted = showTrusted;
        _showQr = showQr;
        _showSettings = showSettings;
        _exit = exit;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _idleIcon = LoadIcon("idle.ico");
        _activeIcon = LoadIcon("active.ico");

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = "RemoteKM — 0 connected",
        };

        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = DarkMenuRenderer.Background,
            ForeColor = DarkMenuRenderer.Text,
            ShowImageMargin = false,
        };
        menu.Opening += (_, _) => RebuildMenu(menu);
        _notifyIcon.ContextMenuStrip = menu;

        _server.SessionsChanged += OnSessionsChanged;
        _trustStore.Changed += OnSessionsChanged;

        UpdateIconAndTooltip();
    }

    private void OnSessionsChanged()
    {
        if (_dispatcher.CheckAccess())
            UpdateIconAndTooltip();
        else
            _dispatcher.BeginInvoke(UpdateIconAndTooltip);
    }

    private void UpdateIconAndTooltip()
    {
        int count = _server.ActiveSessions.Count;
        _notifyIcon.Icon = count > 0 ? _activeIcon : _idleIcon;
        // NotifyIcon.Text is limited to 63 chars.
        _notifyIcon.Text = Truncate($"RemoteKM — {count} connected", 63);
    }

    private void RebuildMenu(WinForms.ContextMenuStrip menu)
    {
        menu.Items.Clear();

        int count = _server.ActiveSessions.Count;

        // Connection count (the listening address now lives in Settings).
        menu.Items.Add(new WinForms.ToolStripMenuItem($"{count} client(s) connected") { Enabled = false });
        menu.Items.Add(new WinForms.ToolStripSeparator());

        Add(menu, "Connected clients…", () => _showConnected());
        Add(menu, "Trusted devices…", () => _showTrusted());

        menu.Items.Add(new WinForms.ToolStripSeparator());

        Add(menu, "Show QR code", () => _showQr());
        Add(menu, "Disconnect all", () => _ = _server.DisconnectAllAsync(), enabled: count > 0);
        Add(menu, "Settings…", () => _showSettings());

        menu.Items.Add(new WinForms.ToolStripSeparator());

        Add(menu, "Exit", () => _exit());
    }

    private static void Add(WinForms.ContextMenuStrip menu, string text, Action onClick, bool enabled = true)
    {
        var item = new WinForms.ToolStripMenuItem(text) { Enabled = enabled };
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    public void ShowBalloon(string title, string message)
    {
        void Show()
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(5000);
        }

        if (_dispatcher.CheckAccess()) Show();
        else _dispatcher.BeginInvoke(Show);
    }

    private static Icon LoadIcon(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = $"RemoteKm.Host.Resources.{fileName}";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded icon '{resourceName}' not found.");
        return new Icon(stream);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);

    public void Dispose()
    {
        _server.SessionsChanged -= OnSessionsChanged;
        _trustStore.Changed -= OnSessionsChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _activeIcon.Dispose();
    }
}
