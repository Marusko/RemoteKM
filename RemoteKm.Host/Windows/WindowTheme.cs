using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RemoteKm.Host.Windows;

/// <summary>
/// Applies a native dark title bar (Windows 10 1809+/11) to a window so the standard,
/// crisp, hardware-accelerated chrome matches the dark content — no blurry transparency.
/// </summary>
public static class WindowTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Hooks the window so its title bar turns dark once the native handle exists.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        if (window.IsLoaded)
            Apply(window);
        else
            window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int on = 1;
            // Try the modern attribute id first, then the pre-20H1 one.
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
        }
        catch
        {
            // Older OS without the attribute — leave the default title bar.
        }
    }
}
