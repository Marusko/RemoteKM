using FluentIcons.Common;
using FluentIcons.Maui;
using RemoteKm.Client.ViewModels;

namespace RemoteKm.Client.Views;

/// <summary>
/// Trackpad surface. On Android the gestures are driven directly from the native touch
/// stream (MotionEvent) for reliable multi-touch: one-finger drag moves the cursor, a quick
/// one-finger tap left-clicks, press-and-hold then drag holds the left button (drag/select),
/// a two-finger tap right-clicks, a three-finger tap middle-clicks, and a two-finger drag scrolls.
/// </summary>
public partial class TrackpadView : ContentView
{
    private readonly TrackpadViewModel _viewModel;

    /// <summary>A press held longer than this is never a tap.</summary>
    private static readonly TimeSpan TapMaxDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>Finger wobble tolerated inside a tap, in device-independent pixels.</summary>
    private const double TapSlopDip = 10;

    private const double LongPressMs = 450;

    /// <summary>Two-finger travel that makes up one wheel notch.</summary>
    private const double ScrollDipPerNotch = 28.0;

    private float _density = 1f;
    private int _pointerCount;
    private int _maxPointers;
    private bool _moved;
    private bool _dragging;
    private DateTime _start;
    private int _longPressGeneration;

    // Cursor finger, tracked by pointer id so lifting another finger never makes the cursor jump.
    private int _activePointerId = -1;
    private float _lastX, _lastY;

    /// <summary>How far the gesture has wandered from where it started, in dip.</summary>
    private double _travelX, _travelY;

    // Two-finger scroll: the midpoint of the fingers that are down, plus the sub-notch remainder.
    private float _lastScrollX, _lastScrollY;
    private double _scrollRemainderDip;

    public TrackpadView(TrackpadViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        HintPanel.Children.Insert(0, new SymbolIcon
        {
            Symbol = Symbol.Cursor,
            FontSize = 40,
            ForegroundColor = (Color)Application.Current!.Resources["RkMuted"],
            HorizontalOptions = LayoutOptions.Center,
        });

        WireMouseButton(MouseLeft, Symbol.PanelLeft, () => _viewModel.LeftClickCommand.Execute(null));
        WireMouseButton(MouseMiddle, Symbol.TextBoxAlignMiddleRotate90, () => _viewModel.MiddleClickCommand.Execute(null));
        WireMouseButton(MouseRight, Symbol.PanelRight, () => _viewModel.RightClickCommand.Execute(null));

        Surface.HandlerChanged += OnSurfaceHandlerChanged;
    }

    private void WireMouseButton(Border button, Symbol icon, Action onTap)
    {
        var accent = (Color)Application.Current!.Resources["RkSurfaceAlt"];
        var pressed = (Color)Application.Current!.Resources["RkAccent"];

        button.Content = new SymbolIcon
        {
            Symbol = icon,
            FontSize = 26,
            ForegroundColor = (Color)Application.Current!.Resources["RkText"],
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, _) => button.BackgroundColor = pressed;
        pointer.PointerReleased += (_, _) => button.BackgroundColor = accent;
        pointer.PointerExited += (_, _) => button.BackgroundColor = accent;
        button.GestureRecognizers.Add(pointer);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        button.GestureRecognizers.Add(tap);
    }

    private void OnSurfaceHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (Surface.Handler?.PlatformView is Android.Views.View view)
        {
            _density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            if (_density <= 0) _density = 1f;
            view.Touch -= OnAndroidTouch;
            view.Touch += OnAndroidTouch;
        }
#endif
    }

#if ANDROID
    private void OnAndroidTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        var me = e.Event;
        if (me is null)
        {
            e.Handled = true;
            return;
        }

        switch (me.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                OnDown(me);
                break;

            case Android.Views.MotionEventActions.PointerDown:
                _pointerCount = me.PointerCount;
                _maxPointers = Math.Max(_maxPointers, _pointerCount);
                CancelLongPress();
                AnchorScroll(me, ignoreIndex: -1);
                break;

            case Android.Views.MotionEventActions.Move:
                OnMove(me);
                break;

            case Android.Views.MotionEventActions.PointerUp:
                OnPointerUp(me);
                break;

            case Android.Views.MotionEventActions.Up:
                OnUp();
                EndGesture();
                break;

            case Android.Views.MotionEventActions.Cancel:
                OnCancel();
                EndGesture();
                break;
        }

        e.Handled = true;
    }

    private void OnDown(Android.Views.MotionEvent me)
    {
        _pointerCount = 1;
        _maxPointers = 1;
        _moved = false;
        _dragging = false;
        _travelX = _travelY = 0;
        _scrollRemainderDip = 0;
        _start = DateTime.UtcNow;
        _activePointerId = me.GetPointerId(0);
        _lastX = me.GetX(0);
        _lastY = me.GetY(0);
        ScheduleLongPress();
    }

    private void OnMove(Android.Views.MotionEvent me)
    {
        if (_pointerCount >= 2)
            MoveMultiFinger(me);
        else
            MoveSingleFinger(me);
    }

    private void MoveMultiFinger(Android.Views.MotionEvent me)
    {
        var (x, y) = Centroid(me, ignoreIndex: -1);
        double dxDip = (x - _lastScrollX) / _density;
        double dyDip = (y - _lastScrollY) / _density;
        _lastScrollX = x;
        _lastScrollY = y;

        // Travel on either axis rules out a multi-finger tap, so a sideways
        // two-finger swipe no longer lands as a right click.
        MarkTravel(dxDip, dyDip);

        // Keep what is left of a notch instead of discarding it, so slow scrolling still moves.
        _scrollRemainderDip += dyDip;
        if (Math.Abs(_scrollRemainderDip) < 0.5)
            return;

        double scrolled = _scrollRemainderDip;
        _scrollRemainderDip = 0;
        _ = _viewModel.ScrollAsync(-scrolled / ScrollDipPerNotch);
    }

    private void MoveSingleFinger(Android.Views.MotionEvent me)
    {
        int index = ActivePointerIndex(me);
        if (index < 0)
            return;

        float x = me.GetX(index), y = me.GetY(index);
        double dx = (x - _lastX) / _density;
        double dy = (y - _lastY) / _density;
        _lastX = x;
        _lastY = y;

        MarkTravel(dx, dy);

        if (Math.Abs(dx) + Math.Abs(dy) > 0.01)
            _ = _viewModel.MoveAsync(dx, dy);
    }

    /// <summary>
    /// Tracks how far the gesture has wandered from where it started. A gesture stays a tap only
    /// while it keeps inside the slop; a single event's delta is a few tenths of a dip and far
    /// too small to decide that on its own, which is why an ordinary drag used to be reported as
    /// a click when it ended. The deltas are summed signed, so panel noise cancels out rather
    /// than adding up and stealing a press-and-hold.
    /// </summary>
    private void MarkTravel(double dxDip, double dyDip)
    {
        if (_moved)
            return;

        _travelX += dxDip;
        _travelY += dyDip;

        if (Math.Sqrt(_travelX * _travelX + _travelY * _travelY) <= TapSlopDip)
            return;

        _moved = true;
        CancelLongPress();
    }

    private void OnPointerUp(Android.Views.MotionEvent me)
    {
        int lifted = me.ActionIndex;
        _pointerCount = Math.Max(1, me.PointerCount - 1);

        // Re-anchor both gestures on the fingers that stay down; otherwise dropping from
        // three fingers to two jolts the scroll, and two to one jumps the cursor.
        AnchorScroll(me, lifted);
        AnchorCursor(me, lifted);
    }

    private async void OnUp()
    {
        CancelLongPress();

        if (_dragging)
        {
            _dragging = false;
            await _viewModel.LeftUpAsync();
            return;
        }

        if (_moved || DateTime.UtcNow - _start > TapMaxDuration)
            return;

        if (_maxPointers >= 3)
            await _viewModel.MiddleClickCommand.ExecuteAsync(null);
        else if (_maxPointers == 2)
            await _viewModel.RightClickCommand.ExecuteAsync(null);
        else
            await _viewModel.LeftClickCommand.ExecuteAsync(null);
    }

    /// <summary>Android took the gesture away (a parent scroll, the window changing): release, never click.</summary>
    private async void OnCancel()
    {
        CancelLongPress();

        if (!_dragging)
            return;

        _dragging = false;
        await _viewModel.LeftUpAsync();
    }

    private void EndGesture()
    {
        _pointerCount = 0;
        _activePointerId = -1;
    }

    private int ActivePointerIndex(Android.Views.MotionEvent me)
    {
        if (_activePointerId >= 0)
        {
            int index = me.FindPointerIndex(_activePointerId);
            if (index >= 0)
                return index;
        }
        return me.PointerCount > 0 ? 0 : -1;
    }

    /// <summary>Picks the cursor finger from those still down and rebases its origin.</summary>
    private void AnchorCursor(Android.Views.MotionEvent me, int ignoreIndex)
    {
        int index = _activePointerId >= 0 ? me.FindPointerIndex(_activePointerId) : -1;
        if (index < 0 || index == ignoreIndex)
        {
            index = FirstIndexExcept(me, ignoreIndex);
            _activePointerId = index >= 0 ? me.GetPointerId(index) : -1;
        }

        if (index < 0)
            return;

        _lastX = me.GetX(index);
        _lastY = me.GetY(index);
    }

    private void AnchorScroll(Android.Views.MotionEvent me, int ignoreIndex)
    {
        (_lastScrollX, _lastScrollY) = Centroid(me, ignoreIndex);
        _scrollRemainderDip = 0;
    }

    private static (float X, float Y) Centroid(Android.Views.MotionEvent me, int ignoreIndex)
    {
        float sumX = 0, sumY = 0;
        int n = 0;
        for (int i = 0; i < me.PointerCount; i++)
        {
            if (i == ignoreIndex)
                continue;
            sumX += me.GetX(i);
            sumY += me.GetY(i);
            n++;
        }
        return n > 0 ? (sumX / n, sumY / n) : (0f, 0f);
    }

    private static int FirstIndexExcept(Android.Views.MotionEvent me, int ignoreIndex)
    {
        for (int i = 0; i < me.PointerCount; i++)
        {
            if (i != ignoreIndex)
                return i;
        }
        return -1;
    }

    private void ScheduleLongPress()
    {
        int generation = ++_longPressGeneration;
        _ = Task.Delay((int)LongPressMs).ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(() => BeginLongPressDrag(generation)),
            TaskScheduler.Default);
    }

    private async void BeginLongPressDrag(int generation)
    {
        if (generation != _longPressGeneration || _pointerCount != 1 || _moved || _dragging)
            return;

        _dragging = true;
        await _viewModel.LeftDownAsync();
    }

    /// <summary>Invalidates any pending long press so a stale timer can't start a drag.</summary>
    private void CancelLongPress() => _longPressGeneration++;
#endif
}
