using System.Collections.ObjectModel;
using System.Windows;
using RemoteKm.Host.Services;

namespace RemoteKm.Host.Windows;

/// <summary>Lists active control sessions, each with a disconnect action. Updates live.</summary>
public partial class ConnectedClientsWindow : Window
{
    public sealed record Row(string Title, string Subtitle, string ClientId);

    private readonly WebSocketServer _server;
    private readonly ObservableCollection<Row> _rows = new();

    public ConnectedClientsWindow(WebSocketServer server)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _server = server;
        List.ItemsSource = _rows;

        _server.SessionsChanged += OnSessionsChanged;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => _server.SessionsChanged -= OnSessionsChanged;
    }

    private void OnSessionsChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        _rows.Clear();
        foreach (var s in _server.ActiveSessions.OrderBy(s => s.ClientName))
        {
            var uptime = (DateTime.Now - s.ConnectedAt).ToString(@"hh\:mm\:ss");
            _rows.Add(new Row(s.ClientName, $"{s.RemoteIp} · connected {uptime}", s.ClientId));
        }
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Row row })
            _ = _server.DisconnectAsync(row.ClientId);
    }
}
