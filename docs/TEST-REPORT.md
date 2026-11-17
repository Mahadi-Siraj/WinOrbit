# Test & performance report — Glasswave 1.0.0

**Test machine:** Windows 11 Home build 26300 · Intel Core i5-12400F (6C/12T) · NVIDIA GeForce GTX 1660 SUPER (driver 32.0.16.1047) · 16 GB RAM · NVMe SSD · one 1920×1080 display at 100 % scaling, 60 Hz · Wallpaper Engine running a black-and-white live wallpaper · Rainmeter tested with a throw-away **portable** Rainmeter 4.5.28 (official signed release) in a temporary folder, removed afterwards.

Date: 17 November 2026.

## 1. Performance (measured)

| Metric | Target | Result | How |
|---|---|---|---|
| Start-up (process start → island and widgets on screen) | fast | **0.75–0.93 s** release, 0.9–1.05 s debug | Logged by the app from process start time; includes .NET start, wallpaper decode/capture, palette and blur |
| Idle CPU, 5 min, release build | < 1 % | **0.050 %** of the machine (0.60 % of one core) | `tools/bench.ps1`: process CPU time over 300 s. State: main window closed, island + clock + now-playing + **system-monitor widget sampling live** (1.5 s), live-wallpaper snapshots on |
| Idle CPU, 5 min, debug build | < 1 % | 0.061 % of machine | same |
| Idle RAM, release build | < 200–250 MB | **69 MB working set average** (max 86 MB); 165–187 MB private bytes | same run, sampled every 10 s |
| Memory with the main window open | — | ~119 MB working set / ~171 MB private | About & Diagnostics, live |
| Handle / thread leak over 5 min | none | handles 854 → 845; threads constant | same run |
| Network sockets at idle | 0 | **0 TCP, 0 UDP** | `Get-NetTCPConnection` / `Get-NetUDPEndpoint` on the process |
| Installer / installed size | — | 5.9 MB setup → 26.8 MB installed (ReadyToRun) | |
| Portable package | — | 73.7 MB zip (includes the .NET runtime) | |

Notes:
* *Private bytes* includes reserved-but-idle .NET heap; *working set* is the physical RAM actually in use. Both are inside the target.
* Glasswave's numbers exclude Rainmeter and Wallpaper Engine, which are separate processes (About & Diagnostics shows Rainmeter's usage separately when it runs).
* Animation smoothness: expand/collapse and page transitions are 280–430 ms `DoubleAnimation`s on WPF's composition thread; they were observed smooth at 60 Hz in hardware mode. No frame-time instrumentation was run, so no FPS figure is claimed. While the GPU fallback (below) is active, the settings window animates in software; the island and widgets are unaffected.

Raw data: `docs/bench-idle-release.json`, `docs/bench-idle-debug.json`.

## 2. Automated tests — 43 passing

`dotnet test tests/Glasswave.Tests` (also run by `tools/build-release.ps1`, in Release):

| Area | Tests |
|---|---|
| Palette | monochrome wallpaper with small colourful icons → neutral graphite palette; blue wallpaper → blue accent; two-colour wallpaper → distinct secondary; colour spread over many hues on a dark background → not neutral, accent from the colour, not the background *(fails on the previous code)*; empty input → default |
| Colour maths | hex parsing (3/6/8 digit, invalid), HSL round-trip, WCAG contrast, accent legibility in dark and light mode *(this test caught a real bug: yellow accents were unreadable on white; fixed)* |
| Wallpaper | box blur preserves flat colour; live-capture maps 1:1 onto its monitor; blank-capture detection |
| Rainmeter | INI parser (BOM, comments, duplicate keys); skin scan reads state + metadata from the active variant **and modifies no file**; bundled vs third-party plugins; RunCommand and program launches flagged, links and folders not; non-Rainmeter zip rejected; `.rmskin` contents (skins, plugins) inspected |
| Rainmeter, live (`LiveRainmeterTests`, opt-in with `GLASSWAVE_RM_LIVE=1`; otherwise they return immediately) | detects the running portable install; lists the stock illustro skins with state and variants; activate, variant switch, refresh, refresh-all and deactivate take effect in Rainmeter; SHA-256 of every skin file identical before and after |
| Settings | JSON round-trip; privacy and automation default to off; atomic write; corrupt file preserved as `.corrupt`; presets complete and unique; scheduled dark/light boundaries; time-rule selection; hotkey parsing |
| Privacy | network gate refuses when off; refuses plain HTTP; **static scan proves no file other than `Network.cs` creates an HttpClient/WebClient/Socket/TcpClient/WebRequest**; weather parser |

## 3. Functional tests on the real desktop

| Test | Method | Result |
|---|---|---|
| Launch, first-run starter layout | fresh settings | ✅ island + 3 widgets placed top-right, clear of the island |
| Settings persistence across restarts | ~12 restarts during development | ✅ widgets, positions, theme, hotkey reassignment all restored |
| Single instance + `--page` + `--quit` | scripted | ✅ second launch opens the requested page in the running copy and now brings it to the front; `--quit` exits cleanly |
| Hotkey conflict | Ctrl+Alt+Space is owned by another app on this PC | ✅ detected, island shortcut moved to Ctrl+Alt+I, logged and shown in Settings |
| Wallpaper detection | Windows reports the stock wallpaper while Wallpaper Engine shows another | ✅ live wallpaper detected and snapshotted; palette correctly neutral (graphite + steel accent) |
| Glass backdrop follows position | dragged a widget over a different part of the wallpaper | ✅ frost updates to what's behind it |
| Island expand/collapse, tabs, system tiles | scripted clicks + screenshots | ✅ |
| Media integration | a WinRT media session playing a silent tagged track | ✅ capsule shows track + equaliser; panel shows title, artist · album, live progress; **play/pause from the island toggled the real session Playing → Paused → Playing**. Found and fixed: untitled sessions were shown as "Nothing playing" (now "Playing in <app>"), and a stale capsule width for a few seconds after a track appeared |
| Fullscreen behaviour | `tools/test-fullscreen.ps1` opens a borderless fullscreen window | ✅ PASS: island visible → hidden during fullscreen → visible again |
| Widget drag, snap, persist | scripted drag | ✅ moved 400×200 px, snapped to the 16 px grid, saved; found and fixed: release point could be lost if the last move event was coalesced |
| Dark mode | default | ✅ |
| Light mode | temporary light theme | ✅ window, island and widgets; readability guard thickened the tint over the dark wallpaper |
| GPU presentation failure | **happened for real**: after a display re-enumeration (Windows event 4107 at 13:20) every hardware-rendered WPF window on this PC, including a minimal test window, showed nothing | ✅ Glasswave's render self-check detected the blank window within ~1 s and switched it to software rendering; UI fully usable; Diagnostics reports it. Later, with Wallpaper Engine closed by the owner, the same minimal test window drew correctly with the GPU and Glasswave reported "Rendering: Hardware". Wallpaper Engine had also been running earlier while GPU rendering worked, so it isn't proven to be the cause; the failure started with the display re-enumeration |
| Wallpaper Engine closed | owner closed it; Windows wallpaper is Windows' `img24.jpg` (colourful ribbon on near-black) | **Found and fixed:** the palette called this wallpaper neutral and gave grey glass, because its colour is spread over many small swatches that each fell under the 1 % threshold meant to ignore desktop icons. Colourfulness is now measured per pixel; result: pink accent `#F62761`, gold secondary. All 12 stock Windows wallpapers checked give colourful palettes; the monochrome-with-icons test still passes. **Also fixed:** Glasswave didn't notice a live-wallpaper app starting, and noticed it closing only at the next 3-minute snapshot. A once-a-minute process check now handles both; verified with a stand-in `wallpaper64` process (start noticed after 29 s, stop after 19 s) |
| Quick toggles | island Quick controls tab; opt-in live tests `LiveQuickControlTests` (`GLASSWAVE_QC_LIVE=1`) | ✅ tiles show the real state (Wi-Fi on, Bluetooth off, Night light off, Dark, Performance …). Wi-Fi and Bluetooth radios switched off and back on, and power mode cycled through all three modes, each confirmed by reading Windows' state back and then restored. Night-light format handling unit-tested on this PC's real state blob. ⚠️ Night light, Dark mode and Keep awake not toggled live, because the owner was using the screen and the effect has to be checked by eye |
| Brightness | DDC/CI on the primary monitor | **Found and fixed:** one missed DDC/CI reply disabled the slider for the whole session ("n/a"), although the monitor answers reliably (28 %, ~60 ms). Reads and writes now retry, and the slider is only disabled after repeated failures, for 5 minutes |
| Ctrl+Alt+Space with Wallpaper Engine closed | `RegisterHotKey` | still owned by another app, so Wallpaper Engine wasn't the owner; Ctrl+Alt+I remains |
| Rainmeter, real install | portable Rainmeter 4.5.28 in a temp folder | ✅ Rainmeter page shows version, Running, 7 skins / 4 active, the active variant of Disk; live tests above pass. **Found and fixed:** every `["…"]` click action was flagged "Runs programs" (so illustro's Disk and Welcome, which only open a drive or a web page, were flagged); Rainmeter's own bundled plugins (InputText in illustro\Google) were flagged as untrusted native code; RunCommand was looked for as a measure type, but it is a plugin, so it was never detected. Now only third-party plugins and real program/script launches are flagged, and the chips name them |
| `.rmskin` import, real | test package handed over by Glasswave's `Install()` → Rainmeter's Skin Installer → Install | ✅ installer showed name/author/version; skin installed into the portable Skins folder, loaded by Rainmeter (`LoadType=Skin`), and listed active by Glasswave. A package with plugin DLLs was inspected (plugin listed) but not installed. Nothing written to `%APPDATA%\Rainmeter` or `Documents\Rainmeter` |
| `.rmskin` import through the UI | Import .rmskin → Windows file picker → Glasswave's confirm → Skin Installer → Install | ✅ package with plugin DLLs: warning shown with the DLL listed; **Cancel installed nothing**. Clean package: plain confirm → Rainmeter's installer → installed; the library refreshed itself to 8 skins / 5 active and showed the new skin's name, author and description |
| Skin position, layering, transparency | position button on illustro\Clock | ✅ moved to 1200,300 and set to Always on top; both visible on screen and saved by Rainmeter. Transparency 55 % visibly applied. **Found and fixed:** the position box started at 100,100 because illustro stores formulas such as `(#SCREENAREAWIDTH#-200)`, so pressing the button would have moved the skin; it now reads the skin's real window position and leaves it alone if unchanged. Rainmeter's `!SetTransparency` is temporary (a refresh resets it), and the dialog now says so and points to Rainmeter's Manage window for a permanent setting |
| Library layout | screenshot | **Found and fixed:** fixed-width cards left a 260 px gap on the right at the default window size; cards now share the row (3 across) |
| Installer | silent install, per-user | ✅ no UAC; files, Start-menu shortcut, start-up entry, Apps & Features entry created; app launches (start-up 0.75 s) |
| Uninstaller | silent uninstall | ✅ program folder, shortcut, start-up value and uninstall entry removed; user data kept (default answer); Rainmeter untouched |
| Reinstall over existing data | | ✅ settings picked up |

## 4. Not verified here (and why)

| Item | Status |
|---|---|
| Rainmeter: standard (non-portable) install | Tested with a portable copy only, so detection through the registry and `%APPDATA%\Rainmeter\Rainmeter.ini` is unit-tested but not live. |
| Multiple monitors, scaling ≠ 100 % | Only one 1080p display at 100 %. Code paths are per-monitor-DPI aware (PerMonitorV2 manifest, per-monitor wallpaper layout, per-display island instances) but untested on hardware. |
| Windows tweaks applied for real | Not applied, to avoid changing your Windows settings without asking. Read paths were exercised (the page shows your current values); write + backup + revert logic is straightforward registry/API code but was not run on this PC. |
| Weather | Network is off by default and wasn't enabled. Parsing is unit-tested. |
| Windows 10 | Not available. The app targets 10 2004+; Mica/Acrylic fall back to Glasswave's own glass there. |
| Long-running stability | 5-minute benchmarks plus ~45 minutes of continuous development sessions without crash or handle growth; no multi-day soak test. |
