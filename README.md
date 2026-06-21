# RemoteKm — turn your phone into a wireless mouse & keyboard

RemoteKm lets you control your Windows PC from your Android phone over your home Wi-Fi.
Use your phone as a trackpad and a full keyboard — no cables, no internet, no account.

---

## What you need

- A **Windows 10/11 PC** (the thing you want to control).
- An **Android 8.0+ phone**.
- Both on the **same Wi-Fi / network**.

## Get the two apps

RemoteKm comes as two parts: a small PC app and a phone app.

**PC app (single file):**

```powershell
dotnet publish RemoteKm.Host -c Release
```

This creates one file — `RemoteKm.Host.exe` (under
`RemoteKm.Host\bin\Release\net10.0-windows\win-x64\publish\`). Copy it anywhere and run it;
it lives in the system tray (next to the clock). Nothing else needs to be installed.

**Phone app (single file):**

```powershell
dotnet publish RemoteKm.Client -f net10.0-android -c Release
```

This creates one `.apk` (under `RemoteKm.Client\bin\Release\net10.0-android\publish\`,
named `com.remotekm.client-Signed.apk`). Copy it to your phone and tap to install
(you may need to allow "install from unknown sources").

### One-time PC setup (important)

The first time, run this **once** in an **Administrator** PowerShell so the PC can accept
connections:

```powershell
netsh http add urlacl url=http://+:45455/ user=Everyone
```

And allow it through the firewall (also as Administrator):

```powershell
New-NetFirewallRule -DisplayName "RemoteKm UDP" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 45454
New-NetFirewallRule -DisplayName "RemoteKm TCP" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 45455
```

If you skip the first command, the PC app will pop up a reminder with the exact line to run.
(As an alternative, you can just run the PC app as Administrator.)

---

## How to connect

1. Start the PC app — a tray icon appears.
2. Open the phone app. It automatically lists PCs it finds on your network.
3. Tap your PC. (Or type its IP and port by hand, or tap **Scan QR** and point it at the
   code from the tray icon's **Show QR code**.)
4. The PC shows a one-time **"Allow this device?"** prompt — click **Accept**.
   Your phone is now remembered, so next time it connects instantly.

---

## What it can do

**Trackpad**
- Move the cursor by dragging one finger.
- Tap to left-click; tap with two fingers to right-click.
- Drag with two fingers to scroll (direction can be reversed in PC settings).
- Press-and-hold, then drag, to click-and-drag (move windows, select text).
- Dedicated Left / Middle / Right buttons and a pointer-speed slider.

**Keyboard**
- Works like a real keyboard: hold a key to repeat it, hold **Alt** and tap **Tab** to switch
  apps, and press several keys at once (multi-touch).
- Keys show your PC's language — e.g. Slovak shows **ľ** and **2** on the same key. When you
  hold **Shift**, the character that will be typed is highlighted.
- Separate **Caps Lock**, and a laptop-style **Fn** button: with Fn on, F1–F12 become media
  and navigation keys, and the arrows become **Home / End / Page Up / Page Down**.
- Dedicated **Insert** and **Delete** keys.
- Built-in **media controls**: Previous / Play-Pause / Next and Volume Down / Up / Mute.
- If you change your PC's input language (Alt+Shift) while connected, the on-screen keyboard
  updates automatically.

**Connecting & convenience**
- Automatic discovery of PCs on the network, manual IP/port entry, or QR-code connect.
- Trusted devices are remembered; pair once, connect instantly after.
- Runs **landscape and full-screen**; the side menu **collapses** to give the trackpad and
  keyboard the whole screen.

**Status & info (phone)**
- Live connection latency, uptime, and data sent/received.
- **Export logs** to share a diagnostic file if something goes wrong.
- Disconnect button.

**On the PC (tray icon menu)**
- See how many phones are connected.
- **Connected clients** window — disconnect any device.
- **Trusted devices** window — revoke a device's access.
- **Show QR code**, **Disconnect all**, **Settings**, **Exit**.

**PC settings**
- Shows the address it's listening on.
- Change the control port.
- **Auto-start with Windows.**
- Require approval for new devices (on by default).
- **Reverse scroll direction.**
- **Open logs folder.**
- **Remove all data & settings** (wipes saved settings, trusted devices and logs, and removes
  the auto-start entry) — with a confirmation prompt.

**Safety**
- A new device must be approved on the PC before it can do anything.
- If a key ever gets stuck down for more than 15 seconds, the PC releases it automatically and
  warns you.
- If a phone drops off the network, the PC notices and clears it from the connected list.

---

## Troubleshooting

- **Phone can't find the PC:** make sure both are on the same Wi-Fi, the PC app is running, and
  the firewall rules above are added. Some routers block device-to-device traffic ("AP/client
  isolation") — turn that off, or connect by IP / QR code.
- **It finds the PC but won't connect:** run the one-time `netsh …urlacl…` command above (or
  run the PC app as Administrator).
- **Camera won't scan:** allow the camera permission when the phone asks.
- **Something's wrong:** grab the logs — PC: Settings → **Open logs folder**; phone: Status →
  **Export logs** — they record what happened.

## Good to know / limits

- Works on your **local network only** — there's no internet relay, and traffic isn't encrypted,
  so use it on networks you trust.
- The keyboard's printed characters are tuned for **Slovak/Czech** and **US English**; other
  languages fall back to a US layout, but what you type is still correct.
- **It can't control Task Manager or other elevated (Administrator) windows.** When such a
  window is in the foreground — Task Manager, a UAC prompt, or any app run "as administrator" —
  Windows blocks input from a normal program, so the mouse and keyboard won't reach it. To use
  RemoteKm with those, run the PC app **as Administrator** too.

## Adding a keyboard layout (for builders)

Keyboard layouts live as small JSON files embedded in the phone app
(`RemoteKm.Client/Resources/Layouts/<language>.json`, e.g. `sk.json`, `en.json`). To add a
language, copy `en.json`, rename it to the two-letter language code, fill in the characters
each key produces, add it to the project as an **EmbeddedResource**, and rebuild the app.
The unshifted/shifted characters are `"n"`/`"s"`; the `"vk"` is the key it sits on. Letters
follow the layout family (QWERTY/QWERTZ/AZERTY) automatically, so the JSON only describes the
top number row and the punctuation keys.

## Licenses & credits

- **Icons by Axialis** — https://www.axialis.com
- Built with open-source components, each under its own license:
  CommunityToolkit.Maui / .Mvvm (MIT), FluentIcons.Maui (MIT), Camera.MAUI & Camera.MAUI.ZXing
  (MIT), ZXing.Net (Apache-2.0), QRCoder (MIT), and Microsoft.Maui /
  Microsoft.Extensions.DependencyInjection / System.Text.Json (MIT).

Both apps show this list in-app: the PC app under **Settings → About & licenses**, and the
phone app under **Status → Licenses**.
