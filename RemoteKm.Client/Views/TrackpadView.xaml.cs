using FluentIcons.Common;
using FluentIcons.Maui;
using RemoteKm.Client.ViewModels;

namespace RemoteKm.Client.Views;

/// <summary>
/// Trackpad surface. On Android the gestures are driven directly from the native touch
/// stream (MotionEvent) for reliable multi-touch: one-finger drag moves the cursor, a quick
/// one-finger tap left-clicks, press-and-hold then drag holds the left button (drag/select),
/// a two-finger tap right-clicks, and a two-finger drag scrolls.
/// </summary>
public partial class TrackpadView : ContentView
{
    private readonly TrackpadViewModel _viewModel;

    private static readonly TimeSpan TapMaxDuration = TimeSpan.FromMilliseconds(300);
    private const double MoveThresholdDip = 8;
    private const double LongPressMs = 450;

    private float _density = 1f;
    private int _pointerCount;
    private int _maxPointers;
    private bool _moved;
    private bool _dragging;
    private DateTime _start;
    private float _lastX, _lastY, _lastScrollY;
    private CancellationTokenSource? _longPressCts;

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
                _pointerCount = 1;
                _maxPointers = 1;
                _moved = false;
                _dragging = false;
                _start = DateTime.UtcNow;
                _lastX = me.GetX(0);
                _lastY = me.GetY(0);
                ScheduleLongPress();
                break;

            case Android.Views.MotionEventActions.PointerDown:
                _pointerCount = me.PointerCount;
                _maxPointers = Math.Max(_maxPointers, _pointerCount);
                CancelLongPress();
                _lastScrollY = AverageY(me);
                break;

            case Android.Views.MotionEventActions.Move:
                OnMove(me);
                break;

            case Android.Views.MotionEventActions.PointerUp:
                ResetPrimaryAfterLift(me);
                _pointerCount = Math.Max(1, me.PointerCount - 1);
                break;

            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                OnUp();
                _pointerCount = 0;
                break;
        }

        e.Handled = true;
    }

    private void OnMove(Android.Views.MotionEvent me)
    {
        if (_pointerCount >= 2)
        {
            float y = AverageY(me);
            float dy = y - _lastScrollY;
            _lastScrollY = y;
            double dyDip = dy / _density;
            if (Math.Abs(dyDip) > 0.5)
            {
                _moved = true;
                _ = _viewModel.ScrollAsync(-dyDip / 28.0);
            }
        }
        else
        {
            float x = me.GetX(0), y = me.GetY(0);
            double dx = (x - _lastX) / _density;
            double dy = (y - _lastY) / _density;
            _lastX = x;
            _lastY = y;

            if (Math.Abs(dx) + Math.Abs(dy) > 0.01)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > MoveThresholdDip)
                {
                    _moved = true;
                    CancelLongPress();
                }
                _ = _viewModel.MoveAsync(dx, dy);
            }
        }
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

        if (!_moved && DateTime.UtcNow - _start < TapMaxDuration)
        {
            if (_maxPointers >= 2)
                await _viewModel.RightClickCommand.ExecuteAsync(null);
            else
                await _viewModel.LeftClickCommand.ExecuteAsync(null);
        }
    }

    private void ResetPrimaryAfterLift(Android.Views.MotionEvent me)
    {
        // Keep tracking a finger that stays down to avoid a cursor jump.
        int lifted = me.ActionIndex;
        int remaining = lifted == 0 ? 1 : 0;
        if (remaining < me.PointerCount)
        {
            _lastX = me.GetX(remaining);
            _lastY = me.GetY(remaining);
        }
    }

    private static float AverageY(Android.Views.MotionEvent me)
    {
        float sum = 0;
        int n = me.PointerCount;
        for (int i = 0; i < n; i++)
            sum += me.GetY(i);
        return n > 0 ? sum / n : 0;
    }

    private void ScheduleLongPress()
    {
        CancelLongPress();
        var cts = new CancellationTokenSource();
        _longPressCts = cts;
        _ = Task.Delay((int)LongPressMs).ContinueWith(_ =>
        {
            if (cts.IsCancellationRequested)
                return;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!cts.IsCancellationRequested && _pointerCount == 1 && !_moved && !_dragging)
                {
                    _dragging = true;
                    await _viewModel.LeftDownAsync();
                }
            });
        });
    }

    private void CancelLongPress()
    {
        _longPressCts?.Cancel();
        _longPressCts = null;
    }
#endif
}
