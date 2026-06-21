using RemoteKm.Client.ViewModels;

namespace RemoteKm.Client.Views;

public partial class StatusView : ContentView
{
    private readonly StatusViewModel _viewModel;

    public StatusView(StatusViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void Activate() => _viewModel.OnAppearing();

    public void Deactivate() => _viewModel.OnDisappearing();
}
