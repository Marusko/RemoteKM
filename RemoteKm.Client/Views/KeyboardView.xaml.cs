using FluentIcons.Common;
using FluentIcons.Maui;
using Microsoft.Maui.Controls.Shapes;
using RemoteKm.Client.ViewModels;
using RemoteKm.Shared;

namespace RemoteKm.Client.Views;

/// <summary>
/// A full on-screen keyboard that behaves like a physical one. Every key sends a real
/// virtual-key <b>down</b> on touch and <b>up</b> on release, so holding repeats, holding a
/// modifier and tapping another key performs the shortcut (e.g. hold Alt, tap Tab), and
/// multi-touch lets several keys be held at once. Keys are labelled from the host's language
/// table (Slovak shows "ľ"/"2" on one key); the host's real layout produces the character.
/// A laptop-style <b>Fn</b> toggle remaps the function row to media keys and the arrows to
/// Home/End/PageUp/PageDown.
/// </summary>
public partial class KeyboardView : ContentView
{
    private enum KeyKind { Char, Mod, Special, Caps, Fn }

    private sealed class KeyDef
    {
        public KeyKind Kind;
        public string Vk = "";
        public string Normal = "";
        public string Shift = "";
        public string Label = "";
        public Symbol? Icon;
        public string? FnVk;
        public string FnLabel = "";
        public bool Repeatable;
        public double Weight = 1;

        public Border Border = null!;
        public Label? Primary;
        public Label? Secondary;
        public bool IsLetter;
    }

    private readonly KeyboardViewModel _viewModel;

    private bool _built;
    private bool _subscribed;
    private bool _fn;
    private bool _caps;
    private bool _shiftHeld;

    private readonly Dictionary<KeyDef, string> _pressed = new();   // pressed key → vk actually sent
    private readonly Dictionary<KeyDef, IDispatcherTimer> _repeat = new();
    private readonly List<KeyDef> _modKeys = new();
    private readonly List<KeyDef> _capsKeys = new();
    private readonly List<KeyDef> _fnKeys = new();
    private readonly List<KeyDef> _fnRelabel = new();
    private readonly List<KeyDef> _charKeys = new();

    public KeyboardView(KeyboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        AddMediaButton(MediaPrev, "MediaPrev", Symbol.Previous);
        AddMediaButton(MediaPlay, "MediaPlayPause", Symbol.Play);
        AddMediaButton(MediaNext, "MediaNext", Symbol.Next);
        AddMediaButton(VolDown, "VolumeDown", Symbol.Speaker1);
        AddMediaButton(VolUp, "VolumeUp", Symbol.Speaker2);
        AddMediaButton(VolMute, "VolumeMute", Symbol.SpeakerMute);
    }

    public void Activate()
    {
        if (!_subscribed)
        {
            _viewModel.LayoutChanged += OnLayoutChanged;
            _subscribed = true;
        }
        if (_built)
            return;
        Build(_viewModel.Layout, _viewModel.Language);
        _built = true;
    }

    public void Deactivate() => _ = ReleaseAllAsync();

    private void OnLayoutChanged()
    {
        // Host switched input language (Alt+Shift) — rebuild the keyboard live.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _pressed.Clear();
            foreach (var t in _repeat.Values) t.Stop();
            _repeat.Clear();
            _modKeys.Clear();
            _capsKeys.Clear();
            _fnKeys.Clear();
            _fnRelabel.Clear();
            _charKeys.Clear();
            _fn = false;
            _shiftHeld = false;
            Build(_viewModel.Layout, _viewModel.Language);
            UpdateEmphasis();
        });
    }

    private static Color Res(string key) => (Color)Application.Current!.Resources[key];

    // ---- Build ----

    private static KeyDef Char(string vk, string normal, string shift) =>
        new() { Kind = KeyKind.Char, Vk = vk, Normal = normal, Shift = shift, Repeatable = true };

    private static KeyDef Letter(string lower) =>
        new() { Kind = KeyKind.Char, Vk = lower.ToUpperInvariant(), Normal = lower, Shift = lower.ToUpperInvariant(), Repeatable = true };

    private static KeyDef Mod(string label, string vk, double w, Symbol? icon = null) =>
        new() { Kind = KeyKind.Mod, Vk = vk, Label = label, Icon = icon, Weight = w };

    private static KeyDef Spec(string label, string vk, double w = 1, Symbol? icon = null, bool repeat = false,
        string? fnVk = null, string fnLabel = "") =>
        new() { Kind = KeyKind.Special, Vk = vk, Label = label, Icon = icon, Weight = w, Repeatable = repeat, FnVk = fnVk, FnLabel = fnLabel };

    private void Build(KeyboardLayout family, string language)
    {
        LayoutLabel.Text = $"{language.ToUpperInvariant()} · {family.ToString().ToUpperInvariant()}";
        var caps = KeyLayouts.For(language, family);

        // Function row (F-keys carry media / nav as their Fn alternates)
        var fn = new List<KeyDef> { Spec("Esc", "Escape", 1.4) };
        string[] fnVks = { "VolumeMute", "VolumeDown", "VolumeUp", "MediaPrev", "MediaPlayPause", "MediaNext",
                           "MediaStop", "Home", "End", "PageUp", "PageDown", "Insert" };
        string[] fnLabels = { "Mute", "Vol−", "Vol+", "Prev", "Play", "Next", "Stop", "Home", "End", "PgUp", "PgDn", "Ins" };
        for (int i = 1; i <= 12; i++)
            fn.Add(Spec("F" + i, "F" + i, fnVk: fnVks[i - 1], fnLabel: fnLabels[i - 1]));

        // Number row
        var row1 = caps.NumberRow.Select(c => Char(c.Vk, c.Normal, c.Shift)).ToList();
        row1.Add(Spec("⌫", "Backspace", 1.8, Symbol.Backspace, repeat: true));

        // Letter rows + OEM
        var row2 = new List<KeyDef> { Spec("Tab", "Tab", 1.5, repeat: true) };
        row2.AddRange(caps.Letters2.Select(Letter));
        row2.AddRange(caps.Row2End.Select(c => Char(c.Vk, c.Normal, c.Shift)));

        var row3 = new List<KeyDef> { new() { Kind = KeyKind.Caps, Vk = "CapsLock", Label = "Caps", Weight = 1.7 } };
        row3.AddRange(caps.Letters3.Select(Letter));
        row3.AddRange(caps.Row3End.Select(c => Char(c.Vk, c.Normal, c.Shift)));
        row3.Add(Spec("Enter", "Enter", 2.0, Symbol.ArrowEnterLeft, repeat: true));

        var row4 = new List<KeyDef> { Mod("Shift", "Shift", 2.0, Symbol.KeyboardShift) };
        row4.AddRange(caps.Letters4.Select(Letter));
        row4.AddRange(caps.Row4End.Select(c => Char(c.Vk, c.Normal, c.Shift)));
        row4.Add(Mod("Shift", "Shift", 2.0, Symbol.KeyboardShift));

        var row5 = new List<KeyDef>
        {
            new() { Kind = KeyKind.Fn, Vk = "", Label = "Fn", Weight = 1.2 },
            Mod("Ctrl", "Ctrl", 1.3),
            Mod("Win", "Win", 1.1),
            Mod("Alt", "Alt", 1.1),
            Spec("Space", "Space", 5, repeat: true),
            Mod("Alt", "Alt", 1.1),
            Mod("Ctrl", "Ctrl", 1.3),
            Spec("Ins", "Insert", 1.1),
            Spec("Del", "Delete", 1.1, repeat: true),
            Spec("", "Left", 1, Symbol.ArrowLeft, repeat: true, fnVk: "Home"),
            Spec("", "Up", 1, Symbol.ArrowUp, repeat: true, fnVk: "PageUp"),
            Spec("", "Down", 1, Symbol.ArrowDown, repeat: true, fnVk: "PageDown"),
            Spec("", "Right", 1, Symbol.ArrowRight, repeat: true, fnVk: "End"),
        };

        KeyboardRoot.Children.Clear();
        foreach (var row in new[] { fn, row1, row2, row3, row4, row5 })
            KeyboardRoot.Children.Add(BuildRow(row));
    }

    private View BuildRow(List<KeyDef> keys)
    {
        var grid = new Grid { ColumnSpacing = 0 };
        for (int i = 0; i < keys.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keys[i].Weight, GridUnitType.Star) });
            var border = CreateKey(keys[i]);
            Grid.SetColumn(border, i);
            grid.Children.Add(border);
        }
        return grid;
    }

    private Border CreateKey(KeyDef def)
    {
        View content;
        if (def.Kind == KeyKind.Char)
        {
            var primary = new Label { Text = def.Normal, FontSize = 16, TextColor = Res("RkText"),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            var secondary = new Label { Text = def.Shift, FontSize = 9, TextColor = Res("RkMuted"),
                HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 3, 5, 0) };
            def.Primary = primary;
            def.Secondary = secondary;
            def.IsLetter = def.Normal.Length == 1 && char.IsLetter(def.Normal[0]);
            content = new Grid { Children = { secondary, primary } };
        }
        else if (def.Icon is { } icon)
        {
            content = new SymbolIcon { Symbol = icon, FontSize = 19, ForegroundColor = Res("RkText"),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        }
        else
        {
            var primary = new Label { Text = def.Label, FontSize = 13, TextColor = Res("RkMuted"),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            def.Primary = primary;
            content = primary;
        }

        var border = new Border
        {
            BackgroundColor = def.Kind is KeyKind.Mod or KeyKind.Caps or KeyKind.Fn ? Res("RkSurfaceAlt") : Res("RkKeyBg"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Margin = new Thickness(2),
            HeightRequest = 44,
            Content = content,
        };
        def.Border = border;

        var pgr = new PointerGestureRecognizer();
        pgr.PointerPressed += (_, _) => OnPress(def);
        pgr.PointerReleased += (_, _) => OnRelease(def);
        pgr.PointerExited += (_, _) => OnRelease(def);
        border.GestureRecognizers.Add(pgr);

        if (def.Kind == KeyKind.Mod) _modKeys.Add(def);
        if (def.Kind == KeyKind.Caps) _capsKeys.Add(def);
        if (def.Kind == KeyKind.Fn) _fnKeys.Add(def);
        if (def.Kind == KeyKind.Char) _charKeys.Add(def);
        if (!string.IsNullOrEmpty(def.FnLabel)) _fnRelabel.Add(def);

        return border;
    }

    private void AddMediaButton(Border border, string vk, Symbol icon)
    {
        border.Content = new SymbolIcon { Symbol = icon, FontSize = 20, ForegroundColor = Res("RkText"),
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

        var pgr = new PointerGestureRecognizer();
        pgr.PointerPressed += async (_, _) => { border.BackgroundColor = Res("RkKeyActive"); await _viewModel.PressAsync(vk); };
        pgr.PointerReleased += (_, _) => border.BackgroundColor = Res("RkSurfaceAlt");
        pgr.PointerExited += (_, _) => border.BackgroundColor = Res("RkSurfaceAlt");
        border.GestureRecognizers.Add(pgr);
    }

    // ---- Press / release ----

    private async void OnPress(KeyDef def)
    {
        if (def.Kind == KeyKind.Fn)
        {
            ToggleFn();
            return;
        }
        if (_pressed.ContainsKey(def))
            return;

        var vk = ActiveVk(def);
        _pressed[def] = vk;
        SetHighlight(def, true);

        if (def.Kind == KeyKind.Caps)
        {
            _caps = !_caps;
            UpdateEmphasis();
        }
        else if (def.Kind == KeyKind.Mod && def.Vk == "Shift")
        {
            _shiftHeld = true;
            UpdateEmphasis();
        }

        await _viewModel.KeyDownAsync(vk);

        if (def.Repeatable)
            StartRepeat(def, vk);
    }

    private async void OnRelease(KeyDef def)
    {
        if (def.Kind == KeyKind.Fn)
            return;
        if (!_pressed.Remove(def, out var vk))
            return;

        StopRepeat(def);
        await _viewModel.KeyUpAsync(vk);
        SetHighlight(def, false);

        if (def.Kind == KeyKind.Mod && def.Vk == "Shift")
        {
            _shiftHeld = _modKeys.Any(m => m.Vk == "Shift" && _pressed.ContainsKey(m));
            UpdateEmphasis();
        }
    }

    /// <summary>
    /// Highlights the glyph that will actually be typed on each key: when Shift is held (or
    /// Caps for letters) the shifted character is emphasised and the other is dimmed.
    /// </summary>
    private void UpdateEmphasis()
    {
        var text = Res("RkText");
        var muted = Res("RkMuted");
        var accent = Res("RkAccent2");

        foreach (var def in _charKeys)
        {
            if (def.Primary is null || def.Secondary is null)
                continue;

            bool shiftPrinted = def.IsLetter ? (_shiftHeld ^ _caps) : _shiftHeld;
            if (shiftPrinted)
            {
                def.Secondary.TextColor = accent;
                def.Secondary.FontAttributes = FontAttributes.Bold;
                def.Primary.TextColor = muted;
            }
            else
            {
                def.Secondary.TextColor = muted;
                def.Secondary.FontAttributes = FontAttributes.None;
                def.Primary.TextColor = text;
            }
        }
    }

    private void ToggleFn()
    {
        _fn = !_fn;
        foreach (var def in _fnRelabel)
            if (def.Primary is not null)
                def.Primary.Text = _fn ? def.FnLabel : def.Label;
        foreach (var def in _fnKeys)
            def.Border.BackgroundColor = _fn ? Res("RkKeyActive") : Res("RkSurfaceAlt");
    }

    private string ActiveVk(KeyDef def) => _fn && def.FnVk is not null ? def.FnVk : def.Vk;

    // ---- Repeat ----

    private void StartRepeat(KeyDef def, string vk)
    {
        StopRepeat(def);
        var timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(380);
        bool first = true;
        timer.Tick += async (_, _) =>
        {
            if (first) { first = false; timer.Interval = TimeSpan.FromMilliseconds(95); }
            await _viewModel.KeyDownAsync(vk);
        };
        _repeat[def] = timer;
        timer.Start();
    }

    private void StopRepeat(KeyDef def)
    {
        if (_repeat.Remove(def, out var timer))
            timer.Stop();
    }

    // ---- Highlight ----

    private void SetHighlight(KeyDef def, bool active)
    {
        if (def.Kind == KeyKind.Caps)
        {
            def.Border.BackgroundColor = _caps ? Res("RkKeyActive") : Res("RkSurfaceAlt");
            return;
        }
        Color bg = active ? Res("RkKeyActive")
            : def.Kind == KeyKind.Mod ? Res("RkSurfaceAlt") : Res("RkKeyBg");
        def.Border.BackgroundColor = bg;
    }

    private async Task ReleaseAllAsync()
    {
        foreach (var def in _repeat.Keys.ToList())
            StopRepeat(def);

        foreach (var kv in _pressed.ToList())
        {
            await _viewModel.KeyUpAsync(kv.Value);
            SetHighlight(kv.Key, false);
        }
        _pressed.Clear();
    }
}
