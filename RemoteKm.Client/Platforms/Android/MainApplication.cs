using Android.App;
using Android.Runtime;
using RemoteKm.Client.Services;

namespace RemoteKm.Client;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		// A crash on a Java thread (camera, barcode decoders) kills the process without ever
		// reaching the .NET handlers in App, so it would leave no trace in the log.
		Java.Lang.Thread.DefaultUncaughtExceptionHandler =
			new JavaCrashLogger(Java.Lang.Thread.DefaultUncaughtExceptionHandler);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>Logs a fatal Java exception, then hands it to the previous handler as before.</summary>
	private sealed class JavaCrashLogger : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
	{
		private readonly Java.Lang.Thread.IUncaughtExceptionHandler? _next;

		public JavaCrashLogger(Java.Lang.Thread.IUncaughtExceptionHandler? next) => _next = next;

		public void UncaughtException(Java.Lang.Thread thread, Java.Lang.Throwable ex)
		{
			AppLog.Error($"Fatal Java exception on thread '{thread.Name}':{Environment.NewLine}" +
				Android.Util.Log.GetStackTraceString(ex));
			_next?.UncaughtException(thread, ex);
		}
	}
}
