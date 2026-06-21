using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using RemoteKm.Shared;

namespace RemoteKm.Host.Services;

/// <summary>
/// Injects mouse, keyboard, and text input into the OS via user32 SendInput.
/// All P/Invoke definitions are inline; no third-party input libraries are used.
/// </summary>
public static class InputInjector
{
    // ---- SendInput plumbing -------------------------------------------------

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const int WHEEL_DELTA = 120;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // Carry fractional movement so slow drags don't lose sub-pixel deltas.
    private static readonly object _moveLock = new();
    private static double _accX;
    private static double _accY;

    // Stuck-key watchdog: keys held down (via KeyAction.Down) with the time they went down.
    private static readonly ConcurrentDictionary<ushort, (DateTime Since, string Name)> _heldKeys = new();
    private static readonly TimeSpan StuckKeyThreshold = TimeSpan.FromSeconds(15);
    private static readonly Timer _watchdog = new(_ => CheckHeldKeys(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    /// <summary>Raised (off the UI thread) with the key name when a key has been held too long.</summary>
    public static event Action<string>? KeyHeldWarning;

    /// <summary>When true, scroll direction is inverted (set from Settings).</summary>
    public static bool ReverseScroll { get; set; }

    // ---- Public API ---------------------------------------------------------

    /// <summary>Dispatches a single input command to the right injector.</summary>
    public static void Dispatch(InputCommand command)
    {
        switch (command)
        {
            case MouseMove m: MouseMove(m.DeltaX, m.DeltaY); break;
            case MouseClick c: MouseClick(c.Button, c.Type); break;
            case MouseButtonHold h: MouseButtonHold(h.Button, h.Down); break;
            case MouseScroll s: MouseScroll(s.DeltaY); break;
            case KeyPress k: KeyPress(k.Key, k.Action); break;
            case TextInput t: TextInput(t.Text); break;
            // Ping/Pong are handled at the transport layer, not here.
        }
    }

    public static void MouseMove(float dx, float dy)
    {
        int moveX, moveY;
        lock (_moveLock)
        {
            _accX += dx;
            _accY += dy;
            moveX = (int)_accX;
            moveY = (int)_accY;
            _accX -= moveX;
            _accY -= moveY;
        }

        if (moveX == 0 && moveY == 0)
            return;

        SendMouse(moveX, moveY, 0, MOUSEEVENTF_MOVE);
    }

    public static void MouseClick(MouseButton button, ClickType type)
    {
        (uint down, uint up) = button switch
        {
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        int repeats = type == ClickType.Double ? 2 : 1;
        for (int i = 0; i < repeats; i++)
        {
            SendMouse(0, 0, 0, down);
            SendMouse(0, 0, 0, up);
        }
    }

    public static void MouseButtonHold(MouseButton button, bool down)
    {
        uint flag = button switch
        {
            MouseButton.Right => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
        };
        SendMouse(0, 0, 0, flag);
    }

    public static void MouseScroll(float deltaY)
    {
        if (ReverseScroll)
            deltaY = -deltaY;
        int amount = (int)(deltaY * WHEEL_DELTA);
        if (amount == 0)
            return;
        SendMouse(0, 0, (uint)amount, MOUSEEVENTF_WHEEL);
    }

    public static void KeyPress(string key, KeyAction action)
    {
        if (!TryMapKey(key, out ushort vk))
            return;

        bool extended = ExtendedKeys.Contains(vk);

        switch (action)
        {
            case KeyAction.Down:
                SendKey(vk, false, extended);
                _heldKeys[vk] = (DateTime.UtcNow, key);
                break;
            case KeyAction.Up:
                SendKey(vk, true, extended);
                _heldKeys.TryRemove(vk, out _);
                break;
            case KeyAction.Press:
                SendKey(vk, false, extended);
                SendKey(vk, true, extended);
                break;
        }
    }

    /// <summary>Watchdog tick: release and report any key held longer than the threshold.</summary>
    private static void CheckHeldKeys()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _heldKeys)
        {
            if (now - kv.Value.Since < StuckKeyThreshold)
                continue;
            if (!_heldKeys.TryRemove(kv.Key, out var info))
                continue;
            // Force-release the stuck key, then warn.
            try { SendKey(kv.Key, keyUp: true, ExtendedKeys.Contains(kv.Key)); } catch { /* ignored */ }
            KeyHeldWarning?.Invoke(info.Name);
        }
    }

    public static void TextInput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Each UTF-16 code unit (incl. surrogate halves) is sent as a Unicode down+up.
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char ch in text)
        {
            inputs.Add(MakeUnicodeInput(ch, keyUp: false));
            inputs.Add(MakeUnicodeInput(ch, keyUp: true));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), InputSize);
    }

    // ---- Low-level senders --------------------------------------------------

    private static void SendMouse(int dx, int dy, uint data, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = data,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
        SendInput(1, new[] { input }, InputSize);
    }

    private static void SendKey(ushort vk, bool keyUp, bool extended)
    {
        uint flags = 0;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
        SendInput(1, new[] { input }, InputSize);
    }

    private static INPUT MakeUnicodeInput(char ch, bool keyUp)
    {
        uint flags = KEYEVENTF_UNICODE;
        if (keyUp) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
    }

    // ---- Key mapping --------------------------------------------------------

    private static bool TryMapKey(string key, out ushort vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(key))
            return false;
        return KeyMap.TryGetValue(key.Trim(), out vk);
    }

    // Keys that require the extended-key flag for correct behavior.
    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x25, 0x26, 0x27, 0x28, // arrows
        0x2D, 0x2E,             // Insert, Delete
        0x24, 0x23,             // Home, End
        0x21, 0x22,             // PageUp, PageDown
        0x5B, 0x5C,             // LWin, RWin
        0xA3,                   // RControl
        0xA5,                   // RMenu (right Alt)
        0x2C,                   // PrintScreen
        0xAD, 0xAE, 0xAF,       // Volume mute/down/up
        0xB0, 0xB1, 0xB2, 0xB3, // Media next/prev/stop/play-pause
    };

    private static readonly Dictionary<string, ushort> KeyMap = BuildKeyMap();

    private static Dictionary<string, ushort> BuildKeyMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        // Letters A-Z → 0x41-0x5A
        for (char c = 'A'; c <= 'Z'; c++)
            map[c.ToString()] = (ushort)c;

        // Digits 0-9 → 0x30-0x39
        for (char c = '0'; c <= '9'; c++)
            map[c.ToString()] = (ushort)c;

        // Function keys F1-F12 → 0x70-0x7B
        for (int i = 1; i <= 12; i++)
            map["F" + i] = (ushort)(0x70 + (i - 1));

        // Arrows
        map["Left"] = 0x25;
        map["Up"] = 0x26;
        map["Right"] = 0x27;
        map["Down"] = 0x28;

        // Modifiers
        map["Shift"] = 0x10;
        map["Ctrl"] = 0x11;
        map["Control"] = 0x11;
        map["Alt"] = 0x12;
        map["Menu"] = 0x12;
        map["Win"] = 0x5B;
        map["Windows"] = 0x5B;
        map["LWin"] = 0x5B;

        // Editing / navigation
        map["Escape"] = 0x1B;
        map["Esc"] = 0x1B;
        map["Tab"] = 0x09;
        map["Enter"] = 0x0D;
        map["Return"] = 0x0D;
        map["Backspace"] = 0x08;
        map["Back"] = 0x08;
        map["Delete"] = 0x2E;
        map["Del"] = 0x2E;
        map["Insert"] = 0x2D;
        map["Home"] = 0x24;
        map["End"] = 0x23;
        map["PageUp"] = 0x21;
        map["PgUp"] = 0x21;
        map["PageDown"] = 0x22;
        map["PgDn"] = 0x22;
        map["Space"] = 0x20;

        // Caps lock (a real, separate toggle key — distinct from Shift)
        map["CapsLock"] = 0x14;
        map["Caps"] = 0x14;

        // OEM / punctuation keys (characters produced depend on the host layout)
        map["Oem1"] = 0xBA;        // ;:
        map["OemPlus"] = 0xBB;     // =+
        map["OemComma"] = 0xBC;    // ,<
        map["OemMinus"] = 0xBD;    // -_
        map["OemPeriod"] = 0xBE;   // .>
        map["Oem2"] = 0xBF;        // /?
        map["Oem3"] = 0xC0;        // `~
        map["Oem4"] = 0xDB;        // [{
        map["Oem5"] = 0xDC;        // \|
        map["Oem6"] = 0xDD;        // ]}
        map["Oem7"] = 0xDE;        // '"
        map["Oem102"] = 0xE2;      // <> or \| on some ISO layouts

        // Media / volume keys
        map["VolumeMute"] = 0xAD;
        map["VolumeDown"] = 0xAE;
        map["VolumeUp"] = 0xAF;
        map["MediaPlayPause"] = 0xB3;
        map["MediaNext"] = 0xB0;
        map["MediaPrev"] = 0xB1;
        map["MediaStop"] = 0xB2;
        map["PrintScreen"] = 0x2C;
        map["PrtSc"] = 0x2C;

        return map;
    }
}
