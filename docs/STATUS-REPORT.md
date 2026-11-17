# WinOrbit — status report

17 November 2026 · version 1.0.0 · 85 automated tests passing · local Git only (not published)

This is an honest account of where WinOrbit stands: what works, what broke during testing and was fixed, what is still weak, and what only you can check.

## 1. Works and verified on this PC

| Area | Verified |
|---|---|
| Glass that matches the wallpaper | Static wallpapers and Wallpaper Engine (snapshot); switches between the two within a minute of Wallpaper Engine opening or closing |
| Dynamic Island | Expand/collapse, tabs, real media control (your YouTube Short showed in the capsule), fullscreen hide, drag, centre snap |
| Quick toggles | Wi-Fi and Bluetooth radios off→on, power mode through all three modes — all against the real Windows state, all restored |
| Widgets | Placement, drag, snap, persistence, live system data |
| Rainmeter | Real Rainmeter 4.5.28 (portable, since removed): detect, list, activate/deactivate, variants, refresh, move, layering, transparency, .rmskin import through the UI |
| Performance | 0.05 % CPU and ~69 MB RAM average at idle (release build, 5 min); 0 network connections; ~0.75–1 s start-up |
| Installer | Per-user install, upgrade, uninstall; no admin prompt |
| Rendering | GPU ("Hardware") again since Wallpaper Engine was closed; automatic software fallback proved when GPU drawing failed |

## 2. Bugs found while testing — all fixed

1. Glass colours came from the wallpaper Windows reports, not what Wallpaper Engine draws → live snapshot.
2. Desktop icons hijacked the accent colour of a black-and-white wallpaper → small areas ignored.
3. Yellow accents unreadable in light mode → contrast check (a unit test caught it).
4. Colourful wallpapers with many hues (your Windows "ribbon") were treated as grey → colourfulness measured per pixel.
5. WinOrbit never noticed Wallpaper Engine starting, and noticed it closing only after up to 3 min → once-a-minute check.
6. Every GPU-drawn window went blank after a display change → render self-check switches that window to software drawing within ~1 s.
7. Ctrl+Alt+Space taken by another app → shortcut moved to Ctrl+Alt+I. (The owner isn't Wallpaper Engine.)
8. Media sessions without a title showed "Nothing playing" → "Playing in ‹app›".
9. Widget/island drags could lose the last position.
10. Window opened behind other apps on relaunch.
11. Rainmeter warnings: harmless skins flagged "runs programs", Rainmeter's own plugins flagged as untrusted, RunCommand never detected.
12. Rainmeter move dialog defaulted to 100,100 (would have moved the skin); transparency claimed to be permanent but Rainmeter resets it.
13. Brightness slider showed "n/a" for the whole session after one missed DDC/CI reply (your monitor does support it: 28 %) → retries.
14. Quick tiles only opened Settings → now real toggles.

## 3. Known limitations and drawbacks (still true)

**Design limits**
* **Glass blurs the wallpaper, not the windows behind it.** The island and widgets frost a snapshot of your wallpaper. When a window is behind a widget, the widget still shows the wallpaper. True "see-through-apps" blur on Windows needs private DWM APIs that break with updates and can't do the custom shapes and lighting. The main settings window does use Windows' real Acrylic/Mica.
* **Live wallpapers are snapshots**, refreshed every 3 minutes, not animated glass. The snapshot includes desktop icons (their colour is ignored, but they can blur faintly into widget glass).
* **Night light uses an undocumented Windows setting.** It checks the format first and falls back to opening Settings, but a future Windows update could change it.
* **Power mode works only with the Balanced plan** (yours). With other plans the tile opens Settings.
* **No Windows "Do not disturb" control** — no public API. The Focus tile runs WinOrbit's own focus timer.
* **Wi-Fi tile shows "Connected", not the network name.** Windows 11 only reveals network names to apps with location permission, and WinOrbit doesn't ask for location.
* **Brightness works only for DDC/CI monitors**, primary display only. Laptop built-in screens aren't supported yet.
* **Keep awake ends when WinOrbit quits** (by design, so nothing is left switched on).

**Quality gaps**
* **Not code-signed**, so SmartScreen warns on first install.
* **Untested here:** multiple monitors, scaling above 100 %, Windows 10, a standard (non-portable) Rainmeter install, actually applying the Windows Tweaks (only Dark mode via the tile), weather (network is off).
* **No long soak test.** The longest runs were 5-minute benchmarks and about an hour of development use.
* **Memory jumps to ~120–170 MB while the main window is open** (still inside the 200–250 MB target). Closing it returns to ~70 MB.
* **In software-fallback mode the settings window animates on the CPU** — fine, but not as smooth.
* **Accessibility is partial:** keyboard control and screen-reader names exist, but there's no full screen-reader pass and no high-contrast theme check.
* **The island's keyboard control** needs Ctrl+Alt+I first, because it deliberately never takes focus from your apps.

## 4. "Be Rainmeter itself" — why I didn't build it

Running Rainmeter skins without Rainmeter means re-implementing Rainmeter:

* about 15 meter types and 25 measure types
* its variable and formula language
* over 100 bangs
* mouse actions
* Lua scripting
* native plugin DLLs written against Rainmeter's C API

Popular skins lean on exactly the hard parts: plugins, Lua, WebParser and Shape meters. A partial engine would draw simple clocks and break most other skins, often silently. That's the "placeholder dressed as a feature" the brief forbids, so per your instruction I skipped it.

What's there now:
* Rainmeter management works when Rainmeter is installed.
* Without Rainmeter, the page just says so; it costs nothing.
* WinOrbit's own 13 widgets cover the common Rainmeter uses (clock, calendar, system monitor, media, notes, tasks, launcher, weather …) with the glass look.

Decision: the Rainmeter page now appears only when Rainmeter is installed or running, and Home shows a Focus card instead. **Settings › Rainmeter page** can force it on or off.

## 5. Please check these yourself (I can't from here)

1. **Night light tile** — does the screen warm up within about a second, and go back to normal on the second click? Does Settings › Display › Night light agree?
2. **Dark mode tile** — do apps and the taskbar switch, with WinOrbit following? Windows Tweaks › Revert all should restore your original.
3. **Keep awake** — turn it on and leave the PC past your screen-off time. The screen should stay on.
4. **Look and feel of the tiles and sliders** — the press spring, the hover chevron, and right-click opening Settings. Does it feel premium enough, and what would you change?
5. **Centring** — drag the island near the middle (it should click in); try right-click › Centre the island and Keep centred.
6. **Brightness slider** — does it change your monitor's brightness, and how fast?
7. **Media with your usual players** (YouTube, Spotify, VLC …) — titles, artwork, controls.
8. **After a restart** — the island and widgets should appear at sign-in, and About › Rendering should say "Hardware".
9. **Wallpaper Engine** — reopen it; within about a minute the glass should return to its colours.
10. **New live activities** — press your volume keys (meter in the capsule); join a call or open the Camera app (orange/green dot, hover for the app); play music (the bars should move with the beat in the album's colour); turn on **Settings › Privacy & network › Synced lyrics** and play a song with lyrics; turn on the Clipboard tab, copy a few things, click one, then Ctrl+V; drag a file onto the island, then drag it from the shelf into a folder or email.
11. **Home and sidebar** — with Rainmeter not installed, the sidebar has no Rainmeter entry and Home shows a Focus card. Does Start focus work from there?
12. **Anything that looks off, feels slow, or that you'd expect to work and doesn't.**

What comes next, and why, is in [ROADMAP.md](ROADMAP.md).
