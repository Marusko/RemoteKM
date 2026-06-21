using RemoteKm.Client.ViewModels;

namespace RemoteKm.Client.Views;

public partial class DiscoveryPage : ContentPage
{
    private readonly DiscoveryViewModel _viewModel;

    public DiscoveryPage(DiscoveryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.OnDisappearingAsync();
    }
}
