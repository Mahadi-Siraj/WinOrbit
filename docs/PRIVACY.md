# Glasswave privacy audit

**Summary: Glasswave contains no telemetry, analytics, tracking, advertising identifiers, crash uploading, accounts or automatic update checks. With default settings it makes zero network connections.**

This document describes what the application stores, what it can transmit, and how that was verified.

## 1. What Glasswave stores

Everything lives in one folder: `%APPDATA%\Glasswave` (or `Data\` beside the exe in portable mode).

| File | Contents | Why |
|---|---|---|
| `settings.json` | Active theme, island settings, widget positions/options, layout, hotkeys, automation rules, Rainmeter favourites/recent list, privacy switches | Restore your desktop after a restart |
| `data.json` | Notes, task lists, focus-session history (start time + minutes) | Widget content |
| `themes\*.json`, `profiles\*.json` | Themes and profiles you created | Your customisations |
| `backups\windows-settings-backup.json` | Original values of Windows settings Glasswave changed (e.g. `AppsUseLightTheme`, accent colour, wallpaper path) | **Revert all** and uninstall rollback |
| `wallpapers\gradient-*.png` | Only if you use "Apply as wallpaper" | The generated wallpaper file |
| `logs\glasswave.log` | Local diagnostic messages (start-up, errors, which Windows setting changed). Rotates at 512 KB, max 2 files | Troubleshooting. Never uploaded |

Glasswave also writes, **only when you ask it to**, per-user Windows values: the `Glasswave` entry under `HKCU\…\Run` (start with Windows), and the appearance values listed on the Windows Tweaks page. All are under `HKEY_CURRENT_USER`; nothing machine-wide is changed.

Not stored anywhere: clipboard contents (there is no clipboard history feature), browsing data, window titles, keystrokes, screenshots. The live-wallpaper snapshot (see §4) is kept in memory only.

## 2. What Glasswave reads on your PC

| Data | Purpose | Leaves the PC? |
|---|---|---|
| Wallpaper image file / live-wallpaper layer | Colour extraction and frosted backdrops | No |
| CPU, memory, disk, network counters (Windows PDH), GPU via NVIDIA's NVML | System widgets and island tiles — sampled **only while one is visible** | No |
| Now-playing info from the Windows media session API | Media widget/island | No |
| Name of the foreground process + whether it is fullscreen | Hiding during games; opt-in automation rules | No (not logged) |
| File names of in-progress downloads in your Downloads folder | Opt-in island download indicator, **off by default** | No |
| `Rainmeter.ini` and skin `.ini` files | Rainmeter library (read-only) | No |

## 3. Network access

There is exactly **one** class that can open a network connection: `NetworkGate` in `src/Glasswave/Services/Network.cs`.

* It refuses every request unless **Settings › Privacy › Allow optional internet features** is on. The switch is **off by default**.
* HTTPS only; no cookies, no redirects, no custom headers, no identifiers.
* Every attempt (including blocked ones) is recorded in an in-memory ledger visible under **About & Diagnostics › Network ledger**.

The only feature that uses it is the optional **Weather** widget (Open-Meteo, no account or key):

| Request | When | Data sent |
|---|---|---|
| `geocoding-api.open-meteo.com/v1/search?name=…` | When you search for a place | The place name you typed |
| `api.open-meteo.com/v1/forecast?latitude=…&longitude=…` | At most every 30 minutes while a Weather widget is visible | Coordinates of the chosen place (rounded to 3 decimals) |

Your IP address is inevitably visible to Open-Meteo when you enable this, as with any web request.

Links such as "Open rainmeter.net" open in **your own browser**; Glasswave itself makes no request.

There is **no** update checker. New versions are installed by running a newer installer.

## 4. Live wallpaper snapshots

When Wallpaper Engine or Lively Wallpaper draws your desktop, the real wallpaper is not a file Windows knows about. If **Match live wallpapers** is on (default), Glasswave calls `PrintWindow` on the desktop's wallpaper host window — not on any application window — every 3 minutes, paused while a fullscreen app runs. The image is downscaled, blurred and kept in memory only. Desktop icons may be included in the snapshot (Windows composes them into that layer); they are reduced to soft blurs and excluded from colour extraction. Turn it off on **Wallpaper & Gradients**.

## 5. Verification performed

1. **Dependency audit.** The app has **zero NuGet package dependencies** — only the .NET 8 / WPF framework and Windows APIs. There is no third-party SDK, analytics or crash-reporting library to audit. (`src/Glasswave/Glasswave.csproj` contains no `PackageReference`.)
2. **Static check (automated test).** `PrivacyTests.Only_the_network_gate_creates_http_clients` scans every source file and fails the build if anything other than `Network.cs` constructs an `HttpClient`, `WebClient`, `Socket`, `TcpClient` or `WebRequest`.
3. **Behavioural tests.** `Network_gate_refuses_when_switched_off` and `Network_gate_refuses_plain_http` prove the gate blocks requests with the default settings and rejects non-HTTPS URLs.
4. **Runtime check.** With default settings, during the idle benchmark, `Get-NetTCPConnection` and `Get-NetUDPEndpoint` for the Glasswave process returned **0 TCP connections and 0 UDP endpoints**. (The process does load the framework assemblies `System.Net.Requests` and `System.Net.WebClient`: WPF's resource loader uses them internally to resolve `pack://application` URIs for embedded images such as the app icon. They open no sockets — confirmed by the zero counts above.)
5. **Build tooling.** `tools/build-release.ps1` sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` so even the .NET SDK used to build Glasswave does not send usage data.

## 6. What is *not* Glasswave

Windows itself (Windows Update, Defender, Microsoft Store), the NVIDIA driver, Wallpaper Engine/Steam and Rainmeter are separate programs with their own network behaviour. Glasswave does not change, block or inspect their traffic.
