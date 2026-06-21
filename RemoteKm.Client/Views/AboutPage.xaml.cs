using RemoteKm.Shared;

namespace RemoteKm.Client.Views;

/// <summary>Shows third-party attributions and licenses.</summary>
public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        LicenseText.Text = Attributions.Text;
    }
}
