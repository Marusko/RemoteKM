# RemoteKM logo

RemoteKM stands for **Remote Keyboard & Mouse**: the logo is a keyboard with a mouse pointer.

| File | Use |
|---|---|
| `logo.svg` | The full logo: app icon at 48 px and up, the promo video, the splash screen |
| `logo-small.svg` | Simplified for 16–32 px: the tray icon while a phone is connected, title bars, the taskbar |
| `logo-small-idle.svg` | The same in grey: the tray icon while no phone is connected |

Where they are used:

- **Phone app:** `RemoteKm.Client/Resources/AppIcon/icon.svg` (background) and `icon_fg.svg` (foreground, kept inside the launcher's safe zone) make the adaptive launcher icon. `Resources/Splash/splash.svg` is a copy of `logo.svg`.
- **PC app:** `RemoteKm.Host/icon.ico` (16–32 px from `logo-small.svg`, 48–256 px from `logo.svg`), and `Resources/active.ico` and `idle.ico` for the tray (16–64 px from the small logos).

The `.ico` files are PNG renders of the SVGs, one per size, packed with ImageMagick, for example:

```bash
convert a16.png a20.png a24.png a32.png a40.png a48.png a64.png active.ico
```

Colours: `#8B7CF0` → `#5A4AD6` gradient, white keyboard, `#00D2A8` pointer, the same palette as the apps (`Colors.xaml`, `HostTheme.xaml`).
