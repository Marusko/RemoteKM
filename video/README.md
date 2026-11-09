# RemoteKM promo video

A promotional video for RemoteKM, made with plain HTML, CSS and JavaScript. It runs 2:01 with music (download it first, see [Music](#music)), in 1920×1080, with headlines and captions in English or Slovak.

## Watch it

The rendered English video is at <https://download.susky.net/remotekm-promo-en.mp4>, linked from the main README through `poster.jpg`. To redo the poster, save the frame at second 3 with `node render.mjs --stills 3` and scale it to 1280 px wide (`ffmpeg -i still-en-3.png -vf scale=1280:-1 -q:v 3 poster.jpg`).

To watch it locally, open `index.html` in Chrome or Edge.

Keyboard: Space plays and pauses, the arrow keys jump 5 seconds, R restarts, M mutes the music and F goes fullscreen. The **SK/EN** button switches the language. While the video plays, the controls hide when the mouse is still.

URL options:

| Option | Effect |
|---|---|
| `?lang=sk` | Slovak headlines and captions |
| `?autoplay` | starts playing at once |
| `?clean` | hides the controls |
| `?t=24` | opens at second 24 |

## Export it

**MP4, frame by frame (best quality).** Needs Node 22+, Chrome, Edge or Chromium, and ffmpeg on the PATH. Nothing else needs installing.

```bash
node render.mjs
```

This writes `remotekm-promo-en.mp4` at 1920×1080 and 30 fps. If the browser isn't found, set `CHROME` to its path.

| Flag | Effect |
|---|---|
| `--lang sk` | renders the Slovak version |
| `--fps 60` | changes the frame rate |
| `--out file.mp4` | changes the output path |
| `--stills 3,15,24` | saves PNG frames at those seconds instead of a video, for checking a scene |
| `--mute` | leaves the music out |

A render takes a few minutes, and you can run several at once.

**WebM, in real time.** The ● Record button captures the page while it plays, then downloads a `.webm` when the video ends. Tick "Also share tab audio" in the browser's prompt to keep the music. For a 1080p recording, run the page in a fullscreen window.

## Scenes

| Time | Scene |
|---|---|
| 0:00 | Logo, "Remote Keyboard & Mouse" and the tagline |
| 0:06 | "Control your PC from the couch. No cables." |
| 0:13 | Connect: the phone finds the PC on the network, an IP typed by hand |
| 0:18.7 | QR connect: "Show QR code" on the PC, the phone's scanner |
| 0:22.3 | Pairing: the PC asks to accept the new phone |
| 0:29 | Trackpad: drag to move, tap to click |
| 0:34 | Two-finger right-click: context menu, open the folder |
| 0:38.7 | Two-finger scroll through the photos |
| 0:43.4 | Hold, then drag a window by its title bar |
| 0:48 | Mouse buttons and the speed slider |
| 0:51 | Keyboard: typing into Notes, with Shift shown |
| 0:56.4 | Layout switch to Slovak (QWERTZ), ľ š č keycaps, the shifted characters lit |
| 1:02.7 | Alt + Tab |
| 1:07.5 | Media keys and the Fn layer, Vol+ on F3 |
| 1:13.8 | Phone app: the menu folds away for a full-screen trackpad |
| 1:18.7 | Status page: latency, uptime, data, Export logs |
| 1:23.4 | PC app: the tray menu, then Settings |
| 1:34.4 | All 12 features |
| 1:44.2 | One .exe, one .apk, no accounts, no cables; requirements |
| 1:51.8 | Call to action and the GitHub URL; the logo lands on the music's drop |

## Editing

- **Text:** headlines and captions are in `strings.js`, one block per language. In Slovak, a one-letter word (a, k, v, z…) is kept off the end of a line automatically. The app's own screens (phone pages, PC windows, tray menu) stay in English, as the app is, and are written in `index.html`.
- **Timing:** scenes start at the times in the `T` table in `video.js`, and the long middle scene (connect → trackpad → keyboard → phone app) uses the beat times in `B` and the caption list right after it. The PC pointer follows the keyframes in `cursorAt`; the fingertips on the trackpad are worked out from those same keyframes, so the finger and the pointer always move together.
- **Keyboard:** built the same way as `RemoteKm.Client/Views/KeyboardView.xaml.cs` (rows, key widths, Fn labels), with the characters from `Resources/Layouts/en.json` and `sk.json`. What gets typed and when is the `type(...)` and `press(...)` calls.

`render(t)` rebuilds the whole frame from the time alone, which is why scrubbing and frame-exact export work.

The colours come from `RemoteKm.Client/Resources/Styles/Colors.xaml` and `RemoteKm.Host/Resources/HostTheme.xaml`. `assets/logo.svg` is the app logo, copied from `branding/`. The Roboto font is vendored in `assets/` so the page renders offline. Host names, the phone's name, the photos and the music track in the Music window are made up.

## Music

The track is "Energetic Breakbeat Powerful Action Rock" by [Alexey Kazantsev](https://pixabay.com/users/331music-40510117/) from Pixabay, used under the Pixabay Content License. The credit is shown faintly at the bottom of every frame.

**The track is not in the repo.** Pixabay's license allows using it in a video, but not distributing the file on its own, unchanged, as a public repo would. Download it from [Alexey Kazantsev's Pixabay page](https://pixabay.com/users/331music-40510117/) (track ID 600481) and save it as:

```
video/assets/331music-energetic-breakbeat-powerful-action-rock-600481.mp3
```

`.gitignore` keeps it out of commits. Without it the page plays silently and `render.mjs` renders a silent video, with a warning. Rendered videos with the music in them are fine to publish.

The file name is set by `MUSIC` near the top of the timeline in `video.js`. The track runs 2:12. Its drop hits at 1:51.9, after a breakdown that starts at 1:50.3, so the outro starts there. If you swap the track, move `T.outro` to the new track's drop.

At the end, the music fades out over the last 2.5 seconds while the picture fades to black. This happens both in the browser and in `render.mjs` output.
