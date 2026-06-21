using System.Windows;
using RemoteKm.Shared;

namespace RemoteKm.Host.Windows;

/// <summary>Shows third-party attributions and licenses.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        LicenseText.Text = Attributions.Text;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
