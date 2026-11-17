# Glasswave

**A Liquid Glass desktop for Windows 11.** Wallpaper-aware glass, a Dynamic Island, premium desktop widgets, Rainmeter management and coordinated Windows appearance — in one native, private, lightweight app.

* Native WPF on .NET 8 — no Electron, no WebView, **no third-party packages**.
* **No telemetry, analytics, accounts or update checks.** Zero network connections by default ([privacy audit](docs/PRIVACY.md)).
* Per-user install, **no administrator rights** for anything.

## Features

Status key: ✅ implemented and verified on the development PC · 🧪 implemented, verified by automated tests but not on real hardware/software here · ➖ intentionally not included.

| Area | What you get | Status |
|---|---|---|
| **Liquid Glass design system** | Wallpaper-sampled frosted glass with tint, refraction, specular highlight, lit rim, accent glow and soft shadow; consistent controls, typography, motion | ✅ |
| **Themes** | 10 hand-tuned built-ins (Pure Liquid Glass, Wallpaper Adaptive, Frosted Glass, Minimal Dark, Soft Light, Midnight, AMOLED Black, Productivity, Gaming, Study); separate dark & light glass; Auto / Dark / Light / Scheduled | ✅ |
| **Liquid Glass Editor** | Live preview on your real wallpaper; tint, blur, saturation, refraction, rim, highlight, glow, shadow, radius; undo/redo; save, duplicate, rename, reset, import/export | ✅ |
| **Wallpaper engine** | Detects the wallpaper per monitor (all Windows fit modes), extracts a palette, builds gradients, updates instantly on change; **Wallpaper Engine / Lively live-wallpaper matching** | ✅ |
| **Gradients** | Wallpaper-generated or custom, 2–6 stops, linear/radial, angle, strength, saturation, presets, "apply as wallpaper" | ✅ |
| **Dynamic Island** | Floating capsule (time, date, now-playing, timer, CPU/RAM, downloads); expands into Media, System, Quick controls, Focus timer, Calendar, Notes; drag with magnetic centre snap, Centre / Keep centred, scroll-for-volume, auto-hide, hover-expand, auto-collapse, fullscreen hide, keyboard control, never steals focus | ✅ |
| Quick toggles | Liquid-glass tiles that switch the real setting: Wi-Fi, Bluetooth, Night light, Dark mode, Power mode, Keep awake, Focus, Widgets | ✅ Wi-Fi, Bluetooth and power mode verified live; ⚠️ Night light is experimental (no public API); Night light, Dark mode and Keep awake still need checking by eye on a real screen |
| Island on several monitors | Per-display placement and offsets | 🧪 (single 1080p display here) |
| **Widgets** (13) | Clock (3 styles), Calendar, World clock, System monitor (3 styles), Now playing, Focus timer, Notes, Tasks/Objectives, Launcher, Recycle Bin, Text, Ambient card, Weather (optional, online) | ✅ |
| Widget interaction | Drag, snap-to-grid + margin magnets, resize, opacity, always-on-top, lock, hide, remove, auto-arrange, persistence, multi-monitor follow | ✅ (multi-monitor 🧪) |
| **Rainmeter** | Detection (installed/portable/custom path), read-only skin library with previews, metadata, search, filters, favourites, recents, third-party-plugin and launches-programs warnings; activate, deactivate, refresh, variants, move, z-order, transparency, Manage, refresh-all, start; safe `.rmskin` inspection → Rainmeter's own installer. The page appears only when Rainmeter is installed or running (Settings can force it on or off) | ✅ verified against a real portable Rainmeter 4.5.28 (detect, list, activate/deactivate, variant switch, refresh, move, layering, transparency, `.rmskin` import through the UI). Transparency is Rainmeter's temporary kind; a standard (non-portable) install wasn't tried live |
| **Windows tweaks** | App/Windows mode, transparency, accent on taskbar/title bars, animations, taskbar alignment/auto-hide/search/Task view, system accent colour, gradient wallpaper — all backed up with **Revert all** | ✅ |
| **Profiles** | Study, Gaming, Productivity, Minimal, Night + your own; restore previous; import/export | ✅ |
| **Automation** (opt-in) | By time of day, fullscreen app, or foreground app | ✅ (logic unit-tested; fullscreen rule tested manually) |
| Privacy & network gate | Master switch (off), HTTPS-only gate, request ledger, static test that no other code can open sockets | ✅ |
| Diagnostics | Live RAM/CPU of Glasswave and of Rainmeter separately, start-up time, network ledger, log viewer | ✅ |
| Render self-check | Detects when the GPU driver fails to present the window and falls back to software rendering automatically | ✅ (triggered for real on this PC) |
| Installer & portable | Inno Setup per-user installer with clean uninstall (keeps/removes data on request, offers Windows-settings rollback); self-contained portable zip | ✅ |
| Explorer/DWM injection, system file patching | | ➖ by design |

Measured results are in [docs/TEST-REPORT.md](docs/TEST-REPORT.md). Screenshots of every page are in `docs/screenshots/` on the development PC (kept out of Git because they show the owner's wallpaper and desktop).

## Install

* **Installer:** run `Glasswave-Setup-1.0.0.exe`. Needs the .NET 8 Desktop Runtime (x64), which setup checks for. Not code-signed: SmartScreen will show *More info → Run anyway*.
* **Portable:** unzip `Glasswave-Portable-1.0.0-win-x64.zip` anywhere and run `Glasswave.exe`. Everything (runtime included) is inside; settings stay in `Data\` next to it.

## Build from source

Requirements: Windows 10 2004+ / 11, .NET 8 SDK. Optional: Inno Setup 6 for the installer (`winget install JRSoftware.InnoSetup --scope user`).

```powershell
dotnet build Glasswave.sln                       # debug build
dotnet test tests\Glasswave.Tests                # unit tests
.\tools\build-release.ps1                        # tests + installer + portable zip into .\artifacts
```

Developer tools in `tools/`: `capture.ps1` and `qa-pages.ps1` (screenshots for visual QA), `bench.ps1` (idle resource benchmark), `input.ps1` (scripted clicks for UI tests).

## Documentation

* [User guide](docs/USER-GUIDE.md) — usage, configuration, troubleshooting, uninstall/rollback, known limitations
* [Architecture](docs/ARCHITECTURE.md) — technology decision, modules, how the glass works
* [Privacy audit](docs/PRIVACY.md)
* [Test & performance report](docs/TEST-REPORT.md)
