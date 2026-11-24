# Developer guide

How WinOrbit is built, how the pieces fit, and how to change it safely. Read [ARCHITECTURE.md](ARCHITECTURE.md) for *why* it is built this way (technology choice, how the glass works); this guide is the *how*.

Last updated 25 November 2026: version 2.0.0, on .NET 10 (see [ROADMAP.md](ROADMAP.md) and [TEST-REPORT.md](TEST-REPORT.md)).

## 1. Get it running

Requirements: Windows 10 2004+ or 11, the .NET 10 SDK. Optional: Inno Setup 6 for the installer.

```powershell
dotnet build WinOrbit.sln                         # debug build
dotnet test tests\WinOrbit.Tests                  # 445 unit tests, under a second
src\WinOrbit\bin\Debug\net10.0-windows10.0.19041.0\WinOrbit.exe --startup   # start without opening the main window
.\tools\build-release.ps1                         # tests + installer + portable zip into .\artifacts
```

Only one copy runs at a time. Stop the running one before rebuilding (`Get-Process WinOrbit | Stop-Process`), or the build cannot overwrite the exe.

Useful command-line switches: `--startup` (tray only), `--page <name>` (open a settings page), `--demo <name>` (play an island preview in the running copy, e.g. `--demo call`; the names are the cases in `IslandWindow.Demo`, and `--demo open:<tab>` opens the panel on one tab, e.g. `open:controls`, for screenshots), `--quit`, `--revert-tweaks`.

## 2. Where things live on disk

| What | Where |
|---|---|
| Settings (every option) | `%APPDATA%\WinOrbit\settings.json` — class `AppSettings` in `Core/Models.cs` |
| User data (notes, tasks, calendar events, launcher history) | `%APPDATA%\WinOrbit\data.json` — class `UserData` |
| Log | `%APPDATA%\WinOrbit\logs\winorbit.log` |
| Tweak backups | `%APPDATA%\WinOrbit\backups\` |
| Portable build | everything goes in a `Data` folder next to the exe while `portable.flag` exists |

`Core/Store.cs` saves atomically (write to a temp file, then replace) and debounces saves. When you test something that changes settings, **back up both JSON files first and restore them after**.

## 3. The code, folder by folder

| Folder | What is in it |
|---|---|
| `App.xaml.cs`, `Hub.cs` | Start-up order, single instance, tray, hotkeys. `Hub` holds every long-lived service (`Hub.Settings`, `Hub.Store`, `Hub.Media`, `Hub.Devices`, `Hub.Foreground`, `Hub.Weather`, `Hub.Island`…) |
| `Core/` | Settings and data models, the JSON store, app info |
| `Interop/Native.cs` | **Every** P/Invoke and COM interface, in one file you can audit |
| `Design/` | Theme engine, glass surface, `Controls.xaml` styles, `Motion` (timed easing), `Springs` (spring physics), `RenderGuard` (GPU fallback; it waits while the screen is off, no monitor is connected or the PC is locked, because every window looks blank then — `PowerEvents.ScreenShowing` counts a monitor from the display configuration's active paths as well as EnumDisplayDevices, since some drivers list none there; it raises `Switched` so GPU-only effects turn off), `LiquidGlassEffect` (curved glass edges: a ps_2_0 pixel shader on the glass backdrop layer only, never on text. Source `Design/Shaders/LiquidGlass.fx`; after editing it run `.\tools\compile-shader.ps1 src\WinOrbit\Design\Shaders\LiquidGlass.fx`, which uses Windows' own `D3DCompiler_47.dll`, and commit the `.fx` and `.ps` together. ps_2_0 allows 64 arithmetic instructions, so move anything per-surface to the CPU, as `Toward` does). Opt-in live window glass lives in `Services/LiveGlass.cs`: `GlassSurface.Slice` asks `Hub.LiveGlass.ImageFor` first and falls back to the wallpaper. A one-frame `Windows.Graphics.Capture` pool per screen (frames left waiting until due, so Windows stops capturing in between), `GenerateMips` on a D3D11 texture and a read-back of mip level 3 (level 2, up to 20 a second, for nearly clear looks such as Liquid Clear; `LiveGlass.LevelFor`). The liquid lens on those looks is `LiquidLensEffect` (ps_3_0, `Design/Shaders/LiquidLens.fx`, compiled the same way). Windows using it are excluded from capture; `Shell.MainWindow` never is (don't use `Application.MainWindow`, which is the first window opened, usually the island). The D3D device must be multithread-protected because capture uses it from its own threads, and COM calls go by slot number (`Native.ComSlot`), `Visuals` (month view, sparkline, equaliser), dialogs, line icons |
| `Island/` | The Dynamic Island (see §4) and Control Centre tiles |
| `Launcher/` | Alt+Space launcher: app index, icons, calculator, unit conversion (`LauncherIndex.cs`), and typed commands, each a small parser with tests: `TimerCommand` (timers), `ReminderCommand` (reminders saved as calendar events with `AtTimeOnly`), `WorldTime` (offline time zones), `ClipSearch` (clipboard history behind the `clip` prefix). A command returns a `LauncherItem` with `Score = 10_000` so it lists first; use `ItemKind.Calc` when its key must not be counted in `data.json` |
| `Widgets/` | Desktop widget host (drag, snap, resize) and the 13 widget kinds. New widgets start at one of the four `WidgetSizes` (160 × 160, 336 × 160, 512 × 160, 336 × 336: one 160 px unit, 16 px gaps) and keep `WidgetView.Inset` (16 px) as their margin, so every widget lines up. `Options["style"] = "bare"` draws a widget straight on the wallpaper (no glass, a soft `W.BareShadow`); `WidgetKind.DefaultStyle` picks that for new ones; a bare widget overrides `T.Text`/`T.Text2`/`T.Text3`/`T.Track`/`W.BareShadow` on its own body from the wallpaper's brightness under it (`WidgetWindow.LightInk`). Build widget content from `WidgetLook` (`Head` header with the accent dot, `Caps`, `Data` in the monospaced font, `Big` numerals, `SegmentBar` meters) so new widgets match the rest; they pick up the clock's lettering (`WidgetLook.Current`, set by `WidgetManager.RefreshLettering` from the first shown clock, which rebuilds the other widgets when it changes). The clock's looks are `ClockWidget.Styles`; the desktop-skin ones are in `ClockSkins.cs`, and `OrbitType` draws Orbit letters (stroke paths on a 12-unit cap height, falling back to a Windows font for characters it lacks). Text-only companion styles are listed in `WidgetCatalog.Companions` |
| `Wallpaper/` | Wallpaper sampling and blur, palette extraction, Wallpaper Engine / Lively detection |
| `Services/` | Everything that watches Windows: media sessions, audio, power, devices and radios, foreground/fullscreen, clipboard, privacy dots, profiles, network gate, weather and lyrics (`Network.cs`), the click-only update check (`Updates.cs`), screenshot and rain watchers, keyboard language, output devices, `.ics` import, running apps |
| `WindowsIntegration/` | Reversible Windows tweaks and the quick controls (Wi-Fi, Bluetooth, night light…) |
| `Rainmeter/` | Optional Rainmeter manager (read-only file access, documented bangs only) |
| `Shell/` | The main settings window and its pages (`Pages/*.cs`, built in code with the `Kit` helpers in `PageKit.cs`) |

## 4. The Dynamic Island

The island is one WPF window per display (`IslandWindow`), managed by `IslandController` (`Island/IslandController.cs`, reached through `Hub.Island`). The controller also owns the watchers every island shares — `Downloads`, `Screenshots`, `Rain` and `Language` — and switches each on or off in `Apply()` to match the settings. It is a layered, non-activating, topmost window, 560 × 420 px × the size setting. The visible glass (`Pill`) is a border inside it that changes size; the window itself never resizes.

The class is split into partial files:

| File | Holds |
|---|---|
| `IslandWindow.xaml` | Layout: the compact capsule, the card, the expanded panel and one `Grid` per tab |
| `IslandWindow.xaml.cs` | Shape and corners, placement on the screen edge, the collapsed capsule, liquid motion, expand/collapse, pointer (click, drag, wheel), auto-hide, fullscreen hiding, tab switching |
| `IslandWindow.Glance.cs` | What the collapsed capsule shows when nothing is happening (`IslandSettings.Glance`: clock, system, status, eyes, quiet). `GlanceParts` decides which parts show. The status row and the eyes are built on first use, and they only listen to Windows or poll the pointer while the capsule is visible and collapsed (`UpdateGlanceLife`) |
| `IslandWindow.Cards.cs` | `ShowCard` / `EndCard`: the card pop-up, its switch, artwork, and file actions (click, drag out, buttons) |
| `IslandWindow.Tabs.cs` | The pages: media, Control Centre (volume, brightness, quick tiles), system monitor, focus timer, quick note, header buttons |
| `IslandController.cs` | `IslandController` (one island per display, the shared watchers, `Apply()`), `DownloadWatcher`, and `ShowAppCard` for cards from the local pipe (`Services/IslandPipe.cs`: opt-in `\\.\pipe\WinOrbit.Island`, current user only, refuses remote clients; `Parse`, `Clean` and `RateGate` are the tested decision logic). A card's second line always starts with the sender's process name; never give pipe cards a click action, link or button |
| `IslandWindow.Live.cs` | Live activities: volume/brightness meter, charging, lock keys, keyboard language, network, radios, Bluetooth/USB, album colours, the `Demo()` previews |
| `IslandWindow.Calendar.cs` | Calendar page, event parsing, reminders |
| `IslandWindow.Weather.cs` | Weather page and sky colours |
| `IslandWindow.Mixer.cs` | Volume by app in the Control Centre (`Services/AppVolume.cs` does the Core Audio work) |
| `IslandWindow.Files.cs` | File cards: screenshot saved (`Services/ScreenshotWatcher.cs`) and download finished; open, drag out, show in folder |
| `IslandWindow.Split.cs` | The side bubble (`Satellite`): short alerts split off beside a busy capsule. One sprung value, `SplitGap`, places the capsule (`PillSplit`), the bubble (`SatShift`, `SatScale`) and the liquid neck (`Bridge`, filled with the same wallpaper slice through `GlassSurface.Slice`). `Peek`, `PeekKey`, lock keys and the keyboard language route here while `PillBusy` |
| `IslandWindow.Agents.cs` | The Agents tab (only while `CodingAgents` is on), the "needs you" card, the orange rim while any session waits (`AgentsWaiting`, read by `ApplyRim`) and the "finished" note with a 3-second green rim. The data comes from `Services/AgentSessions.cs`: `AgentClient` is `WinOrbit.exe --agent <tool>`, handled in `Program.Main` before WPF loads, which turns a tool's hook JSON into one small `AgentEvent` (`AgentHook.Translate` keeps a title, the folder's name and a few words, nothing else) and sends it to the pipe; `AgentBoard` keeps at most 8 sessions in memory; `AgentSetup` builds the snippets Settings copies. `--agent` must stay silent on stdout and always exit 0 |
| `IslandWindow.Call.cs` | The microphone pill (`Services/MicMeter.cs` reads the level and mute), the scroll-wheel hook that is installed only while the pointer is over the island (Windows' "scroll inactive windows" may be off), and `SwipeGesture` (sideways swipe → one track skip) |

### Motion

* `Springs.To(target, property, value, response, damping)` moves a value on a spring and keeps its current speed, so a motion can be redirected mid-way without a jump. `Springs.Kick` gives a value a push that wobbles back to rest. The engine runs only while something is moving.
* The capsule's size always moves on `IslandWindow.Shape` (grow: response 0.4, damping 0.8; shrink: 0.36, 0.9). Use it for any new size change instead of new numbers: low damping looks lively in one place but rings on for a second, and every extra frame costs CPU on WPF's drawing thread. `MotionAndDeviceTests` checks the overshoot and settle time.
* To measure motion, start the app with `WINORBIT_FRAMES=1` (logs how evenly frames arrived for each motion, and how many) and use `--demo open` / `--demo agent` to trigger it without clicking.
* `Motion.To` is a plain timed ease for things that should not bounce (fades, tucking away).
* Expanding and collapsing use `MorphResponse` 0.5 / `MorphDamping` 0.82 for width, height and corner radius together.
* Reduced Motion (`Motion.Intensity` = 0) makes both snap straight to the end.

### Shapes and screen edges

* `ShapeCorners(shape, r, edge)` describes each shape for the **top** edge; `Orient()` turns the corners for Bottom, Left or Right.
* `WindowSpot(edge, offset, flush, bounds, work, scale, width, height)` works out where the window goes. `OffsetX` means "along the edge from its middle"; `OffsetY` means "distance from the edge". The bottom and side edges use the monitor's work area, so the island stays clear of the taskbar.
* `ApplyEdge()` anchors the glass to its edge so it grows away from it; `ShiftOut` / `PlaceOut` slide it off the edge (entrance, auto-hide, coming back after fullscreen).
* Dragging maps the mouse movement to (along, away) for the current edge. Carrying it within 70 px of another edge — after leaving its own edge by 110 px, so corners do not flicker — moves it there (`EdgeNear`, `EdgeDistance`).

### Hiding for fullscreen apps

`ForegroundWatcher` (in `Services/SystemServices.cs`) listens for foreground changes with an out-of-context `SetWinEventHook` (nothing is injected). It also hooks move/resize events of the foreground app only, coalesced over 60 ms, so a video going fullscreen is noticed in about 90 ms. A 2.5 s timer is the safety net. `IslandWindow.ShouldHide()` decides using the two app lists (`ShowOverFullscreenApps`, `HideForApps`).

### Live activities

`Services/DeviceEvents.cs` raises `LockChanged`, `NetworkChanged`, `BluetoothChanged`, `UsbChanged`, `RadioChanged` and `ModeChanged`. Mode changes (dark mode, night light…) come from `RegistryWatcher`, which sleeps until Windows reports a write. `IslandWindow.Live.cs` turns each event into a short `Peek()` or a larger `ShowCard()`.

### Recipes

**Add a live activity**
1. Raise an event from a service in `Services/` (event-driven; poll only if Windows offers no notification).
2. Subscribe in `InitLive()` and unsubscribe in `CleanupLive()`.
3. Show it with `ShowCard(glyph, title, subtitle, colour, seconds)` or `Peek(glyph, text)`. A card about a file can take `onClick` (a click does this instead of opening the island), `file` (dragging the card drags the file out) and `actions` (small icon buttons on the right); it then waits while the pointer rests on it.
4. Add a case to `Demo()` and a Preview button on the Island settings page, so it can be checked without the real event.
5. Gate it behind a setting in `IslandSettings` if people may not want it.

**Add an island page (tab)**
1. Add a `<Grid x:Name="TabX"/>` in `IslandWindow.xaml`.
2. Add `("x", Glyph.Something, "Name")` to `AllTabs`.
3. Handle `"x"` in `ShowTab()` (visibility, the `view` switch, and what to refresh).
4. Bump `ModulesVersion` in `ActiveTabs()` with a migration that turns the new tab on for existing users.
5. Put the page code in its own `IslandWindow.X.cs` partial.

**Add a setting**
1. Add a property with a safe default to the right class in `Core/Models.cs` (old settings files load with that default).
2. Add a row to the settings page with the `Kit` helpers (`Kit.ToggleRow`, `Kit.SliderRow`, `Kit.Segmented`, `Kit.WideRow`…), then `Save()`.
3. Mention it in [USER-GUIDE.md](USER-GUIDE.md); if it touches the network or personal data, in [PRIVACY.md](PRIVACY.md) too.

## 5. Testing

* **Unit tests** (`tests/WinOrbit.Tests`, xUnit, 445 tests). Logic that decides something is kept in small `internal static` functions so it can be tested without a window: `ShapeCorners`, `WindowSpot`, `EdgeNear`, `ShouldHide`, `ParseEvent`, `ReminderDue`, `SkyColors`, the weather parser, colour maths, the calculator and others. Add a test with every new rule like these.
* **Old settings keep loading.** `Files_written_by_1_3_2_still_load` reads settings, data and a theme that the real 1.3.2 build wrote (`tests/WinOrbit.Tests/Fixtures`). If it fails, a change to `Core/Models.cs` would reset people's setup after an update: add the new property with a default instead of renaming or retyping the old one.
* **`Live*Tests`** touch the real machine (quick controls, Rainmeter, lyrics) and skip themselves when the hardware or app is not there.
* **On-screen checks** are PowerShell scripts that drive the running app through UI Automation and screenshot the island window: `tools/qa-pages.ps1` (every settings page), `tools/test-fullscreen.ps1` (hide and return timing), `tools/bench.ps1` (idle CPU and memory), `tools/capture.ps1`, `tools/input.ps1`.
* Never toggle someone's real Wi-Fi, Bluetooth, dark mode or night light in a test without asking; use the `Demo()` previews instead.

## 6. Releasing

1. Change the number in `VERSION` (single source of truth; the build passes it to the exe and installer).
2. Add a "New in x.y" section to `.github/release-notes.md` (it becomes the GitHub release text).
3. Optionally run `.\tools\build-release.ps1` locally to check it builds.
4. Push to `main`. `.github/workflows/release.yml` sees `VERSION` change, runs the tests, builds the installer and portable zip on GitHub's Windows runners, writes `SHA256SUMS.txt` and publishes the release. Pushing a `v*` tag does the same.

The builds are **not code-signed**. Do not describe them as signed.

## 7. Ground rules

* No DLL injection, no hooks inside other processes, no patched system files, no admin rights, nothing that weakens Windows security.
* Every network request goes through the network gate (`Services/Network.cs`), is off by default, has its own switch and shows in the request ledger.
* Prefer Windows' own notifications over polling; stop any sampling nobody is looking at.
* All P/Invoke goes in `Interop/Native.cs` (small, private ones next to their only user are fine when documented).
* `ThemeMode` means WinOrbit's own enum (a project-wide alias, because WPF has had a `System.Windows.ThemeMode` since .NET 9). Inside a class that derives from `Application` or `Window`, write `Core.ThemeMode`: there a bare `ThemeMode` is WPF's property of that name.
* Never commit secrets, personal data or local settings files.
* Keep the docs honest: a feature is "done" only when it works; known limits go in the README's status table.
