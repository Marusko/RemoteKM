using RemoteKm.Client.Services;
using RemoteKm.Client.Views;

namespace RemoteKm.Client;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;

		AppLog.Info("RemoteKm client starting.");
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			AppLog.Error("Unhandled exception", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			AppLog.Error("Unobserved task exception", e.Exception);
			e.SetObserved();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Start on the discovery page wrapped in a NavigationPage so we can push the
		// connected shell on top and pop back on disconnect.
		var discovery = _services.GetService(typeof(DiscoveryPage)) as DiscoveryPage
			?? throw new InvalidOperationException("DiscoveryPage not registered.");

		var nav = new NavigationPage(discovery);
		if (Resources.TryGetValue("RkSurface", out var bar) && bar is Color barColor)
			nav.BarBackgroundColor = barColor;
		if (Resources.TryGetValue("RkText", out var txt) && txt is Color textColor)
			nav.BarTextColor = textColor;

		return new Window(nav);
	}
}
