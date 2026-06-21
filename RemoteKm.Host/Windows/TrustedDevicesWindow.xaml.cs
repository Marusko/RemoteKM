using System.Collections.ObjectModel;
using System.Windows;
using RemoteKm.Host.Services;

namespace RemoteKm.Host.Windows;

/// <summary>Lists trusted clients, each with a revoke action. Updates live.</summary>
public partial class TrustedDevicesWindow : Window
{
    public sealed record Row(string Title, string Subtitle, string ClientId);

    private readonly TrustStore _trustStore;
    private readonly ObservableCollection<Row> _rows = new();

    public TrustedDevicesWindow(TrustStore trustStore)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _trustStore = trustStore;
        List.ItemsSource = _rows;

        _trustStore.Changed += OnChanged;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => _trustStore.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        _rows.Clear();
        foreach (var t in _trustStore.GetAll())
        {
            var shortId = t.ClientId.Length >= 8 ? t.ClientId.Substring(0, 8) : t.ClientId;
            _rows.Add(new Row(t.ClientName,
                $"{shortId}… · trusted {t.TrustedSince:yyyy-MM-dd} · last seen {t.LastSeen:yyyy-MM-dd HH:mm}",
                t.ClientId));
        }
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRevoke(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Row row })
            _trustStore.Revoke(row.ClientId);
    }
}
