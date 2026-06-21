using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.Messaging;
using FluentIcons.Common;
using FluentIcons.Maui;
using RemoteKm.Client.Messages;
using RemoteKm.Client.Services;

namespace RemoteKm.Client.Views;

/// <summary>
/// The connected experience. A left nav rail swaps between Trackpad, Keyboard, and Status
/// views by toggling visibility — there is no swipe navigation, so trackpad panning never
/// changes tabs. Returns to discovery if the connection drops.
/// </summary>
public partial class MainShellPage : ContentPage
{
    private readonly TrackpadView _trackpad;
    private readonly KeyboardView _keyboard;
    private readonly StatusView _status;
    private readonly IMessenger _messenger;
    private readonly NavigationService _navigation;
    private readonly ConnectionService _connection;

    private Border[] _navTiles = null!;
    private SymbolIcon[] _navIcons = null!;
    private SymbolIcon _collapseIcon = null!;
    private int _selected = -1;
    private bool _railCollapsed;

    public MainShellPage(
        TrackpadView trackpad,
        KeyboardView keyboard,
        StatusView status,
        IMessenger messenger,
        NavigationService navigation,
        ConnectionService connection)
    {
        InitializeComponent();
        _trackpad = trackpad;
        _keyboard = keyboard;
        _status = status;
        _messenger = messenger;
        _navigation = navigation;
        _connection = connection;

        foreach (var view in new View[] { _trackpad, _keyboard, _status })
        {
            view.IsVisible = false;
            ContentHost.Children.Add(view);
        }

        SetupNav();
    }

    private void SetupNav()
    {
        _navTiles = new[] { NavTrackpad, NavKeyboard, NavStatus };
        _navIcons = new SymbolIcon[3];

        // Collapse toggle at the top of the rail.
        _collapseIcon = new SymbolIcon
        {
            Symbol = Symbol.ChevronLeft,
            FontSize = 22,
            ForegroundColor = (Color)Application.Current!.Resources["RkMuted"],
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        NavCollapse.Content = _collapseIcon;
        var collapseTap = new TapGestureRecognizer();
        collapseTap.Tapped += (_, _) => SetRailCollapsed(true);
        NavCollapse.GestureRecognizers.Add(collapseTap);

        // Floating expand button (visible only when collapsed) — small so the content
        // (incl. the keyboard's language label) gets the full screen.
        FloatingExpand.Content = new SymbolIcon
        {
            Symbol = Symbol.ChevronRight,
            FontSize = 20,
            ForegroundColor = (Color)Application.Current!.Resources["RkText"],
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        var expandTap = new TapGestureRecognizer();
        expandTap.Tapped += (_, _) => SetRailCollapsed(false);
        FloatingExpand.GestureRecognizers.Add(expandTap);

        AttachNav(NavTrackpad, Symbol.Cursor, 0, ref _navIcons[0]);
        AttachNav(NavKeyboard, Symbol.Keyboard, 1, ref _navIcons[1]);
        AttachNav(NavStatus, Symbol.Pulse, 2, ref _navIcons[2]);

        var power = new SymbolIcon
        {
            Symbol = Symbol.Power,
            FontSize = 24,
            ForegroundColor = (Color)Application.Current!.Resources["RkDanger"],
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        NavDisconnect.Content = power;
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await DisconnectAsync();
        NavDisconnect.GestureRecognizers.Add(tap);
    }

    private void AttachNav(Border tile, Symbol symbol, int index, ref SymbolIcon icon)
    {
        icon = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = 24,
            ForegroundColor = (Color)Application.Current!.Resources["RkMuted"],
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        tile.Content = icon;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Select(index);
        tile.GestureRecognizers.Add(tap);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _messenger.Register<MainShellPage, DisconnectedMessage>(this, static (page, msg) => page.OnDisconnected(msg.Value));
        Select(0);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _messenger.Unregister<DisconnectedMessage>(this);
        _keyboard.Deactivate();
        _status.Deactivate();
    }

    private void Select(int index)
    {
        if (index == _selected)
            return;

        // Tear down the view we're leaving.
        if (_selected == 1)
            _keyboard.Deactivate();
        if (_selected == 2)
            _status.Deactivate();

        _trackpad.IsVisible = index == 0;
        _keyboard.IsVisible = index == 1;
        _status.IsVisible = index == 2;

        switch (index)
        {
            case 1: _keyboard.Activate(); break;
            case 2: _status.Activate(); break;
        }

        UpdateNavVisual(index);
        _selected = index;
    }

    private void UpdateNavVisual(int active)
    {
        var accent = (Color)Application.Current!.Resources["RkAccent"];
        var muted = (Color)Application.Current!.Resources["RkMuted"];
        var activeBg = (Color)Application.Current!.Resources["RkSurfaceAlt"];

        for (int i = 0; i < _navTiles.Length; i++)
        {
            bool on = i == active;
            _navTiles[i].BackgroundColor = on ? activeBg : Colors.Transparent;
            _navIcons[i].ForegroundColor = on ? accent : muted;
        }
    }

    private void SetRailCollapsed(bool collapsed)
    {
        _railCollapsed = collapsed;
        // Collapsed: hide the whole rail (full width + height for the content) and show a
        // small floating expand button instead.
        RailColumn.Width = collapsed ? new GridLength(0) : new GridLength(84);
        RailBorder.IsVisible = !collapsed;
        FloatingExpand.IsVisible = collapsed;
    }

    private async Task DisconnectAsync()
    {
        await _connection.DisconnectAsync();
        await _navigation.GoToDiscoveryAsync();
    }

    private void OnDisconnected(string reason)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await _navigation.GoToDiscoveryAsync();
            await Toast.Make(string.IsNullOrWhiteSpace(reason) ? "Disconnected from the host." : reason,
                ToastDuration.Long).Show();
        });
    }
}
