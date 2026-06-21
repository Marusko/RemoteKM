using Microsoft.Extensions.DependencyInjection;
using RemoteKm.Client.Views;

namespace RemoteKm.Client.Services;

/// <summary>
/// Thin wrapper over the root NavigationPage so view-models can move between the
/// discovery page, the connected shell, and the QR scanner without touching UI types.
/// </summary>
public sealed class NavigationService
{
    private readonly IServiceProvider _services;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    private static INavigation? Navigation
        => Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

    public Task GoToMainAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var page = _services.GetRequiredService<MainShellPage>();
            return Navigation?.PushAsync(page) ?? Task.CompletedTask;
        });
    }

    public Task GoToDiscoveryAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var nav = Navigation;
            if (nav is not null && nav.NavigationStack.Count > 1)
                await nav.PopToRootAsync();
        });
    }

    public Task GoToScanAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var page = _services.GetRequiredService<ScanPage>();
            return Navigation?.PushAsync(page) ?? Task.CompletedTask;
        });
    }

    public Task GoToAboutAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var page = _services.GetRequiredService<AboutPage>();
            return Navigation?.PushAsync(page) ?? Task.CompletedTask;
        });
    }
}
