using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using RemoteKm.Host.Services;
using RemoteKm.Shared;

namespace RemoteKm.Host.Windows;

/// <summary>
/// Shows a QR code encoding "remotekm://ip:port" plus the raw URI text.
/// Auto-refreshes every 10 seconds in case the local IP or the bound port changes.
/// </summary>
public partial class QrWindow : Window
{
    private readonly WebSocketServer _server;
    private readonly DispatcherTimer _timer;
    private string _lastUri = string.Empty;

    public QrWindow(WebSocketServer server)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _server = server;

        Refresh();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var ip = NetworkInfo.GetLocalIPv4();
        // The port the server actually bound, which is not the configured one when a
        // fallback candidate had to be used.
        var uri = $"{Protocol.UriScheme}://{ip}:{_server.Port}";

        if (uri == _lastUri)
            return;
        _lastUri = uri;

        UriText.Text = uri;
        QrImage.Source = GenerateQr(uri);
    }

    private static BitmapImage GenerateQr(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var pngQr = new PngByteQRCode(data);
        byte[] png = pngQr.GetGraphic(20);

        var image = new BitmapImage();
        using (var ms = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }
}
