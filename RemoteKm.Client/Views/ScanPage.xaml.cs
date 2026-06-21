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
public partial class ScanPage : ContentPage
{
    private readonly IMessenger _messenger;
    private bool _scanned;

    public ScanPage(IMessenger messenger)
    {
        InitializeComponent();
        _messenger = messenger;
    }

    protected override async void OnAppearing()
    {
        _scanned = false;
        base.OnAppearing();
        try
        {
            AppLog.Info("[Scan] Camera starting…");
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                AppLog.Warn("[Scan] Camera permission denied");
                await Toast.Make("Camera permission is required to scan QR codes.", ToastDuration.Long).Show();
                await Navigation.PopAsync();
                return;
            }

            InitCamera();
            await Task.Delay(50);
            if (Camera.NumCamerasDetected > 0)
                Camera.Camera = Camera.Cameras.First();

            var res = await Camera.StartCameraAsync();
            if (res != CameraResult.Success)
                AppLog.Error($"[Scan] Camera failed to start: {res}");
            else
                AppLog.Info("[Scan] Camera started");
        }
        catch (Exception ex)
        {
            AppLog.Error("[Scan] Error starting camera", ex);
            await Toast.Make("Could not start the camera.", ToastDuration.Long).Show();
            await Navigation.PopAsync();
        }
    }

    protected override async void OnDisappearing()
    {
        try { await Camera.StopCameraAsync(); } catch { /* ignored */ }
        base.OnDisappearing();
    }

    private void InitCamera()
    {
        Camera.CamerasLoaded += OnCamerasLoaded;
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

    private async void OnBarcodeDetected(object? sender, BarcodeEventArgs args)
    {
        if (_scanned)
            return;

        var value = args.Result?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(value) || !TryParseEndpoint(value!, out var ip, out var port))
            return;

        _scanned = true;
        AppLog.Info($"[Scan] QR endpoint {ip}:{port}");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await Camera.StopCameraAsync(); } catch { /* ignored */ }
            await Navigation.PopAsync();
            _messenger.Send(new QrScannedMessage(ip, port));
        });
    }

    private void OnCancel(object? sender, EventArgs e) => Navigation.PopAsync();

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
