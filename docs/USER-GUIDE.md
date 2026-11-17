# Glasswave user guide

## Getting around

| Action | How |
|---|---|
| Open Glasswave | Tray icon (click), Start menu, or **Ctrl+Alt+G** |
| Open the Dynamic Island with the keyboard | **Ctrl+Alt+Space** (if another app owns it, Glasswave moves it to **Ctrl+Alt+I** and tells you on Settings) |
| Show / hide all widgets | **Ctrl+Alt+W** |
| Jump between sections | **Ctrl+1 … Ctrl+9** in the main window |
| Undo / redo theme edits | **Ctrl+Z / Ctrl+Y** in the Liquid Glass Editor |
| Open a section from a shortcut | `Glasswave.exe --page widgets` (any of: home, appearance, studio, wallpaper, island, widgets, rainmeter, tweaks, profiles, settings, about) |

Closing the main window keeps the island and widgets running from the tray (turn this off in Settings). **Quit Glasswave** is in the tray menu.

## Dynamic Island

* **Click** the capsule to expand; **Esc** or clicking elsewhere collapses it. It also collapses a few seconds after the pointer leaves (configurable; pin it open with the pin button).
* **Scroll** on the capsule to change volume. **Drag** it to move it; **right-click** for quick options.
* In the expanded panel: **←/→** or **1–6** switch tabs, **Space** plays/pauses (Media) or starts/pauses (Focus).
* The capsule shows one live activity at a time — a running timer, a download (opt-in) or what's playing — followed by the time and date.
* It hides automatically during fullscreen games and videos.
* Brightness works on monitors that support DDC/CI; otherwise the slider is disabled and says so.

## Widgets

* Add from **Widgets**. Drag anywhere on the glass to move; drag the **corner grip** to resize. **Hold Alt** while dragging to ignore the grid.
* **Right-click** a widget for its own options (style, cities, timer length, rename, add shortcuts…) plus opacity, *Always on top*, lock, hide and remove.
* Widgets sit on the desktop beneath your windows. **Win+D** (show desktop) brings them forward with the desktop.
* The Launcher accepts files, apps and folders dropped onto it.
* Notes and tasks save as you type, locally.

## Themes and the Liquid Glass Editor

* Pick a theme in **Appearance**. Edits never change the built-in themes: the first edit creates "*Name* (custom)", saved automatically.
* **Liquid Glass Editor** has independent dark and light glass. **Reset** returns to the built-in values; **More** has rename, duplicate, import/export (`.glasstheme` JSON) and delete.
* **Mode**: Auto follows Windows; Scheduled switches at the times you set.

## Wallpaper & gradients

* Colours update the moment Windows reports a new wallpaper (including slideshows).
* **Wallpaper Engine / Lively**: Glasswave snapshots the wallpaper layer every 3 minutes (in memory, paused in fullscreen apps) so the glass matches what you see. Turn off with *Match live wallpapers*.
* The gradient can follow the wallpaper or be your own (2–6 stops, linear or radial). *Apply as wallpaper* renders it as a real wallpaper (your old one is backed up).

## Profiles and automation

* A profile stores theme + island + widget layout + reduced motion. **Restore previous desktop** undoes the last switch.
* Automation is **off** until you enable it: switch by time of day, while a fullscreen app runs, or when a chosen app is in front (e.g. `code`, `photoshop`). Rules end automatically and restore the previous profile.

## Rainmeter

* Glasswave finds a normal Rainmeter install automatically. A portable copy is found while it is running; otherwise use **Locate Rainmeter.exe** once. The library shows every skin config with preview images where the skin has one (`preview.png`, `@Resources\preview.png`, …), metadata, favourites and recents.
* Warnings: **Third-party plugin** means the skin loads a plugin DLL that didn't come with Rainmeter (Rainmeter's own plugins, such as InputText or WebParser, aren't flagged). **Runs programs** means a click action or RunCommand can start a program or script; hover the chip to see which. Links and folders aren't flagged.
* The switch on each card activates/deactivates through Rainmeter. Use the monitor button for position, layering and transparency.
* **Import .rmskin** shows what the package contains (skins, layouts, plugin DLLs) and then hands it to Rainmeter's own Skin Installer.
* If Rainmeter isn't installed, the page links to the official site; Glasswave never installs it.

## Windows Tweaks

* Every setting here is per-user and reversible. The first change of each setting records its original value in `%APPDATA%\Glasswave\backups\windows-settings-backup.json`.
* **Revert all** restores everything (including accent colour and wallpaper). The uninstaller offers the same.

---

## Configuration files

All in `%APPDATA%\Glasswave` (portable: `Data\` next to the exe). They are plain JSON and safe to back up. Use **Settings › Your data** to export/restore a `.zip` backup. If a file is damaged, Glasswave keeps a `.corrupt` copy and starts with defaults rather than failing.

## Troubleshooting

| Problem | Fix |
|---|---|
| Windows SmartScreen says "Windows protected your PC" | The installer is not code-signed (no certificate). Click **More info → Run anyway** if you built/obtained it yourself. |
| Setup says the .NET 8 Desktop Runtime is missing | Install ".NET Desktop Runtime 8 (x64)" from dotnet.microsoft.com, or use the portable zip which includes it. |
| A shortcut does nothing | Another app owns that key combination. Pick a new one in **Settings › Keyboard shortcuts** (the page lists conflicts). |
| Widgets disappeared | Press **Ctrl+Alt+W**, or check **Widgets › Show widgets**. Widgets stay hidden while a fullscreen app has focus. |
| The island is gone | Tray menu › **Dynamic Island**, or **Dynamic Island › Show**. It hides during fullscreen apps. |
| Glass colours don't match the wallpaper | **Wallpaper & Gradients › Re-scan**. With Wallpaper Engine, make sure *Match live wallpapers* is on. |
| No GPU usage | Glasswave reads NVIDIA GPUs through the driver's NVML and others through Windows GPU counters. Some drivers expose neither. |
| Brightness slider disabled | The monitor doesn't support DDC/CI (or it is disabled in the monitor's OSD menu). |
| A Rainmeter skin didn't load | Glasswave reports it. Open the skin in Rainmeter's **Manage** (button on the card) and check Rainmeter's log; missing plugins are the usual cause. |
| The settings window is blank | A graphics-driver state (e.g. after a display mode change) can stop GPU-rendered windows from appearing. Glasswave detects this within a second and switches that window to software rendering automatically (About & Diagnostics › Rendering says so). To make it permanent: **Settings › Software rendering for this window**. Restarting the PC or updating the GPU driver normally restores hardware rendering. |
| Anything else | **About & Diagnostics** shows the log, resource use and a copyable system summary. |

## Uninstall and rollback

1. *(Optional)* **Windows Tweaks › Revert all** to restore Windows appearance settings now.
2. **Settings › Apps › Installed apps › Glasswave › Uninstall** (no admin prompt). The uninstaller:
   * closes Glasswave,
   * asks whether to restore Windows settings Glasswave changed,
   * removes the program files, shortcuts and start-up entry,
   * asks whether to delete your settings/themes/notes (default **No**).
3. Rainmeter, its skins and its settings are never touched.

Portable version: quit from the tray and delete the folder.

## Known limitations

* **Not every part of Windows can be restyled.** Start, the taskbar, context menus and other apps' windows can only take the accent colour, dark/light mode and transparency that Windows exposes. Deeper restyling needs third-party tools that inject into Explorer (listed, not installed) — Glasswave deliberately doesn't.
* **Glass over other windows.** Island and widget glass frosts the *wallpaper*, not windows that happen to be behind them. (Windows offers no supported live blur for layered windows.) The main window uses real DWM Acrylic, which does blur what's behind it.
* **Live wallpapers** are matched with a snapshot every 3 minutes, not continuously; desktop icons can appear as faint blurs in that snapshot.
* **Media artwork** and controls depend on the playing app supporting the Windows media session (Spotify, Chromium-based browsers, Firefox and the Windows Media Player app do; some older desktop players don't, in which case the play/next buttons fall back to the standard media keys).
* **CPU temperature** is not shown: Windows has no reliable admin-free source. GPU temperature works on NVIDIA GPUs.
* **Download indicator** recognises browsers' temporary download files in the Downloads folder only; it can't show percentage progress.
* **Wi-Fi / Bluetooth** buttons open the Settings pages rather than toggling the radios (toggling needs APIs that require package identity or admin).
* **Rainmeter** skins can't be rendered inside Glasswave; they keep running in Rainmeter. Glasswave and Rainmeter widgets are managed side by side, not merged into one engine.
* The installer is **unsigned**.
