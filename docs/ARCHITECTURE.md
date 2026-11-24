# Architecture

## Technology decision

| Option | Verdict | Reasoning |
|---|---|---|
| **WPF on .NET 10** (LTS, supported to Nov 2028; was .NET 8 until 1.3.2) | **Chosen** | Mature, GPU-composited (DirectX) retained-mode UI; per-pixel transparent top-level windows for the island and widgets; full Win32/COM interop; per-monitor DPI v2; ships in the Windows Desktop Runtime so the app itself has **no third-party dependencies**; framework-dependent build is ~2 MB. Idle cost is near zero because WPF only renders on change. |
| WinUI 3 / Windows App SDK | Rejected for this product | Excellent Mica/Acrylic, but transparent, borderless, non-activating overlay windows (needed for the island and desktop widgets) are poorly supported; each window hosts its own XAML island with a higher memory floor; unpackaged deployment adds the App SDK runtime. |
| Electron | Rejected | 150 MB+ per process, Chromium background activity, weak native integration — contrary to the performance and "native app" requirements. |
| Tauri / WebView2 | Rejected | Lighter than Electron, but every widget would be a WebView2 (~30–60 MB each); transparency and click-through on WebView2 are awkward; still a web stack wrapped in a window. |
| Composition API / raw Direct2D | Rejected | Best theoretical efficiency but would mean writing a UI toolkit; unjustified complexity. |

### How Liquid Glass is achieved

Windows provides real-time backdrop blur only for whole, non-layered windows (DWM Mica/Acrylic). The island and widgets must be layered (per-pixel alpha) windows to have rounded, shadowed, click-through edges, and DWM does not blur behind those. WinOrbit therefore uses a **wallpaper-sampled glass**:

1. `WallpaperEngine` decodes the wallpaper (or snapshots the live-wallpaper layer) off the UI thread, lays it out per monitor exactly as Windows does (Fill/Fit/Stretch/Center/Tile/Span), downsamples ×4, boosts saturation and applies a three-pass box blur (≈ Gaussian).
2. Each `GlassSurface` maps its own on-screen rectangle into that pre-blurred image (an `ImageBrush` viewbox), with a slight inward inset to imitate refraction.
3. Over the backdrop it layers: tint → wallpaper-gradient wash → accent glow → specular highlight → dark hairline → illuminated rim, plus an outside-only shadow.

Because widgets live on the desktop, the backdrop is exactly what is behind them. The cost is one blur per wallpaper change — **not per frame**. The main window uses the real DWM Acrylic/Mica backdrop (switchable), falling back to the same sampled glass on Windows 10.

## Modules

```
src/WinOrbit
├─ Program.cs             Entry point: `--agent` (coding-agent hooks) is answered here and exits before WPF loads; starts .NET's multicore JIT profile
├─ App.xaml(.cs)          Application shell: single instance, start-up order, tray, hotkeys, CLI (--page, --quit, --revert-tweaks)
├─ Hub.cs                 Service locator for long-lived services
├─ Core/                  Models (settings, themes, profiles, widgets) + Store (atomic JSON, debounced saves, local log)
├─ Interop/Native.cs      Every P/Invoke and COM interface in one auditable file
├─ Design/                Design system: ThemeEngine (+10 presets, undo/redo), GlassSurface, Controls.xaml styles,
│                         Motion (reduced-motion aware), Visuals (sparkline, ring, equaliser, month view), Dialogs
├─ Wallpaper/             WallpaperEngine, Palette extraction & colour maths, LiveWallpaper (Wallpaper Engine / Lively)
├─ Island/                Dynamic Island window (split into partials: live activities, calendar, weather),
│                         controller (multi-monitor), Control Centre tiles, download watcher
├─ Launcher/              Alt+Space launcher: app index, icons, calculator, unit conversion
├─ Widgets/               Widget host window (drag/snap/resize/z-order), manager, 13 widget kinds
├─ Rainmeter/             Detection, read-only INI/skin scanning, documented bangs, .rmskin inspection
├─ WindowsIntegration/    Per-user appearance tweaks with backup/revert, accent colour, start-up entry, third-party info
├─ Services/              System monitor (PDH + NVML), media sessions, audio, brightness (DDC/CI), timers,
│                         foreground/fullscreen watcher, profiles & automation, network gate & weather,
│                         the opt-in local pipe (app cards, coding-agent sessions)
└─ Shell/                 Main window and the 11 pages
```

## Efficiency rules the code follows

* **No polling when nobody is looking.** The system sampler runs only while a subscriber exists (a visible system widget, the island's system tab, the Home page). Pages unsubscribe in `Leave()`. The main window is destroyed when closed.
* **Event-driven state.** Wallpaper and theme changes come from `WM_SETTINGCHANGE` and a file watcher; foreground changes from `SetWinEventHook` (out-of-context: nothing is injected); media from the Windows media-session events.
* **Timers wake only on change.** Clocks schedule the next tick at the next minute boundary; the focus timer ticks only while running; the scheduled theme timer fires once at the next switch time.
* **Fullscreen pause.** When a fullscreen app is in front: the island hides, widget content stops, sampling stops, live-wallpaper snapshots are skipped.
* **Animations** are short `DoubleAnimation`s on transforms/opacity; the equaliser runs at 20 fps only while music plays and the capsule is visible.

## Safety boundaries

* No DLL injection, no hooks inside other processes, no patched system files, no admin rights.
* Windows tweaks only write `HKEY_CURRENT_USER` values the Settings app itself writes, or call documented APIs (`SystemParametersInfo`, `SHAppBarMessage`, `IDesktopWallpaper`). Originals are backed up before the first change.
* Rainmeter: read-only access to its files; control only via documented bangs passed to `Rainmeter.exe`; packages installed by Rainmeter's own `SkinInstaller.exe` after WinOrbit shows the contents and warns about plugin DLLs.
