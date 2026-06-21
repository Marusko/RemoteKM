using System.Globalization;
using System.Runtime.InteropServices;
using RemoteKm.Shared;
using WinForms = System.Windows.Forms;

namespace RemoteKm.Host.Services;

/// <summary>
/// Inspects the host's active keyboard layout to pick a layout family for the client's
/// on-screen keyboard and report the host's language (so the client can show the right
/// top-row characters, e.g. Slovak diacritics).
/// </summary>
public static class KeyboardLayoutInfo
{
    private static readonly HashSet<string> QwertzLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "sk", "cs", "hu", "sl", "hr", "bs", "sq", "sr",
    };

    private static readonly HashSet<string> AzertyLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "fr",
    };

    /// <summary>Returns the layout family and the host's two-letter language code.</summary>
    public static (KeyboardLayout Family, string Language) Describe()
    {
        var culture = GetActiveCulture();
        return (ToFamily(culture), culture?.TwoLetterISOLanguageName ?? "en");
    }

    public static KeyboardLayout Detect() => ToFamily(GetActiveCulture());

    private static KeyboardLayout ToFamily(CultureInfo? culture)
    {
        if (culture is null)
            return KeyboardLayout.Qwerty;
        if (culture.Name.EndsWith("-BE", StringComparison.OrdinalIgnoreCase))
            return KeyboardLayout.Azerty;

        var lang = culture.TwoLetterISOLanguageName;
        if (AzertyLanguages.Contains(lang)) return KeyboardLayout.Azerty;
        if (QwertzLanguages.Contains(lang)) return KeyboardLayout.Qwertz;
        return KeyboardLayout.Qwerty;
    }

    /// <summary>
    /// Resolves the culture of the <b>active</b> input language of the foreground window, so a
    /// live Alt+Shift switch is reflected. Falls back to the system default, then current culture.
    /// </summary>
    private static CultureInfo? GetActiveCulture()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            uint tid = hwnd != IntPtr.Zero ? GetWindowThreadProcessId(hwnd, out _) : 0;
            IntPtr hkl = GetKeyboardLayout(tid);
            int langId = (int)((long)hkl & 0xFFFF);
            if (langId != 0)
                return new CultureInfo(langId);
        }
        catch { /* ignored */ }

        try
        {
            var def = WinForms.InputLanguage.DefaultInputLanguage?.Culture;
            if (def is not null && !def.Equals(CultureInfo.InvariantCulture))
                return def;
        }
        catch { /* ignored */ }

        return CultureInfo.CurrentCulture;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);
}
