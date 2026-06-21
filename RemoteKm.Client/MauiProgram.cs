using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RemoteKm.Client.Services;
using RemoteKm.Client.ViewModels;
using Camera.MAUI;
using FluentIcons.Maui;
using RemoteKm.Client.Views;

namespace RemoteKm.Client;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseMauiCameraView()
			.UseFluentIcons()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Messaging
		builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

		// Services (singletons — one identity/connection for the app's lifetime)
		builder.Services.AddSingleton<ClientIdentity>();
		builder.Services.AddSingleton<DiscoveryService>();
		builder.Services.AddSingleton<ConnectionService>();
		builder.Services.AddSingleton<NavigationService>();

		// View-models
		builder.Services.AddTransient<DiscoveryViewModel>();
		builder.Services.AddTransient<TrackpadViewModel>();
		builder.Services.AddTransient<KeyboardViewModel>();
		builder.Services.AddTransient<StatusViewModel>();

		// Views & pages
		builder.Services.AddTransient<DiscoveryPage>();
		builder.Services.AddTransient<TrackpadView>();
		builder.Services.AddTransient<KeyboardView>();
		builder.Services.AddTransient<StatusView>();
		builder.Services.AddTransient<MainShellPage>();
		builder.Services.AddTransient<ScanPage>();
		builder.Services.AddTransient<AboutPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
