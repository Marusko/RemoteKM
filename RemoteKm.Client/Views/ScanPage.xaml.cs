using Camera.MAUI;
using Camera.MAUI.ZXing;
using Camera.MAUI.ZXingHelper;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.Messaging;
using RemoteKm.Client.Messages;
using RemoteKm.Client.Services;
using RemoteKm.Shared;

namespace RemoteKm.Client.Views;

/// <summary>
/// Camera QR scanner (Camera.MAUI + Camera.MAUI.ZXing). Parses a "remotekm://ip:port"
/// payload and reports it back via the messenger so the discovery page can connect.
/// </summary>
/// <remarks>
/// Every way off this page goes through <see cref="CloseAsync"/>, which stops the camera
/// <b>before</b> popping. Popping first lets MAUI tear the camera view down under a running
/// session with live decoder threads, and that crashes the process from a Java thread.
/// </remarks>
public partial class ScanPage : ContentPage
{
    private readonly IMessenger _messenger;
    private Task<bool>? _starting;
    private Task? _shutdown;
    private bool _closing;

    public ScanPage(IMessenger messenger)
    {
        InitializeComponent();
        _messenger = messenger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_closing)
            return;

        _starting = StartCameraAsync();
        if (!await _starting)
            await CloseAsync(null);
    }

    protected override async void OnDisappearing()
    {
        // Backstop only: the normal exits have already stopped the camera by now.
        await ShutDownCameraAsync();
        base.OnDisappearing();
    }

    // The Android back gesture/button would otherwise pop the page with the camera running.
    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async void OnCancel(object? sender, EventArgs e) => await CloseAsync(null);

    /// <summary>Returns false when the scanner cannot run and the page should close.</summary>
    private async Task<bool> StartCameraAsync()
    {
        try
        {
            AppLog.Info("[Scan] Camera starting…");
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                AppLog.Warn("[Scan] Camera permission denied");
                await Toast.Make("Camera permission is required to scan QR codes.", ToastDuration.Long).Show();
                return false;
            }

            // Cancelled while the permission prompt was up: never start the camera at all.
            if (_closing)
                return true;

            InitCamera();
            await Task.Delay(50);
            if (Camera.NumCamerasDetected > 0)
                Camera.Camera = Camera.Cameras.First();

            var res = await Camera.StartCameraAsync();
            if (res != CameraResult.Success)
                AppLog.Error($"[Scan] Camera failed to start: {res}");
            else
                AppLog.Info("[Scan] Camera started");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("[Scan] Error starting camera", ex);
            await Toast.Make("Could not start the camera.", ToastDuration.Long).Show();
            return false;
        }
    }

    private void InitCamera()
    {
        Camera.CamerasLoaded -= OnCamerasLoaded;
        Camera.CamerasLoaded += OnCamerasLoaded;
        Camera.BarcodeDetected -= OnBarcodeDetected;
        Camera.BarcodeDetected += OnBarcodeDetected;
        Camera.BarCodeDecoder = new ZXingBarcodeDecoder();
        Camera.BarCodeOptions = new BarcodeDecodeOptions
        {
            AutoRotate = true,
            PossibleFormats = { global::Camera.MAUI.BarcodeFormat.QR_CODE },
            ReadMultipleCodes = false,
        };
        Camera.BarCodeDetectionFrameRate = 10;
        Camera.BarCodeDetectionMaxThreads = 5;
        Camera.ControlBarcodeResultDuplicate = false;
        Camera.BarCodeDetectionEnabled = true;
    }

    private void OnCamerasLoaded(object? sender, EventArgs e)
    {
        if (Camera.NumCamerasDetected > 0)
            Camera.Camera = Camera.Cameras.First();
    }

    // Raised on a decoder thread, possibly several times for one code.
    private void OnBarcodeDetected(object? sender, BarcodeEventArgs args)
    {
        var value = args.Result?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(value) || !TryParseEndpoint(value!, out var ip, out var port))
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_closing)
                return;
            AppLog.Info($"[Scan] QR endpoint {ip}:{port}");
            await CloseAsync(new QrScannedMessage(ip, port));
        });
    }

    /// <summary>The single way off this page: stop the camera, pop, then report any result.</summary>
    private async Task CloseAsync(QrScannedMessage? result)
    {
        if (_closing)
            return;
        _closing = true;
        AppLog.Info(result is null ? "[Scan] Closing (cancelled)" : "[Scan] Closing (code scanned)");

        await ShutDownCameraAsync();
        await Navigation.PopAsync();

        if (result is not null)
            _messenger.Send(result);
    }

    private Task ShutDownCameraAsync() => _shutdown ??= ShutDownCameraCoreAsync();

    private async Task ShutDownCameraCoreAsync()
    {
        // A start still in flight would bring the camera up after we stopped it.
        if (_starting is not null)
        {
            try { await _starting; } catch { /* already logged */ }
        }

        Camera.BarcodeDetected -= OnBarcodeDetected;
        Camera.CamerasLoaded -= OnCamerasLoaded;
        Camera.BarCodeDetectionEnabled = false;

        try
        {
            await Camera.StopCameraAsync();
            AppLog.Info("[Scan] Camera stopped");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[Scan] Stopping the camera failed: {ex.Message}");
        }
    }

    /// <summary>Parses "remotekm://ip:port" (or a bare "ip:port").</summary>
    private static bool TryParseEndpoint(string text, out string ip, out int port)
    {
        ip = string.Empty;
        port = 0;
        text = text.Trim();

        if (text.StartsWith(Protocol.UriScheme + "://", StringComparison.OrdinalIgnoreCase))
            text = text.Substring((Protocol.UriScheme + "://").Length).TrimEnd('/');

        var parts = text.Split(':');
        if (parts.Length != 2)
            return false;

        ip = parts[0];
        return !string.IsNullOrWhiteSpace(ip)
            && int.TryParse(parts[1], out port)
            && port is > 0 and <= 65535;
    }
}
