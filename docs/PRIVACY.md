# Glasswave privacy audit

**Summary: Glasswave contains no telemetry, analytics, tracking, advertising identifiers, crash uploading, accounts or automatic update checks. With default settings it makes zero network connections.**

This document describes what the application stores, what it can transmit, and how that was verified.

## 1. What Glasswave stores

Everything lives in one folder: `%APPDATA%\Glasswave` (or `Data\` beside the exe in portable mode).

| File | Contents | Why |
|---|---|---|
| `settings.json` | Active theme, island settings, widget positions/options, layout, hotkeys, automation rules, Rainmeter favourites/recent list, privacy switches | Restore your desktop after a restart |
| `data.json` | Notes, task lists, focus-session history (start time + minutes), clipboard items **you pinned** (text only) | Widget content |
| `backups\nightlight-state.bin` | Windows' night-light state as it was before the island's Night light tile first changed it | Restore if a future Windows changes the format |
| `themes\*.json`, `profiles\*.json` | Themes and profiles you created | Your customisations |
| `backups\windows-settings-backup.json` | Original values of Windows settings Glasswave changed (e.g. `AppsUseLightTheme`, accent colour, wallpaper path) | **Revert all** and uninstall rollback |
| `wallpapers\gradient-*.png` | Only if you use "Apply as wallpaper" | The generated wallpaper file |
| `logs\glasswave.log` | Local diagnostic messages (start-up, errors, which Windows setting changed). Rotates at 512 KB, max 2 files | Troubleshooting. Never uploaded |

Glasswave also writes, **only when you ask it to**, per-user Windows values: the `Glasswave` entry under `HKCU\…\Run` (start with Windows), and the appearance values listed on the Windows Tweaks page. All are under `HKEY_CURRENT_USER`; nothing machine-wide is changed.

Not stored anywhere: unpinned clipboard history (memory only, see §2), browsing data, window titles, keystrokes, screenshots, lyrics. The live-wallpaper snapshot (see §4) is kept in memory only.

## 2. What Glasswave reads on your PC

| Data | Purpose | Leaves the PC? |
|---|---|---|
| Wallpaper image file / live-wallpaper layer | Colour extraction and frosted backdrops | No |
| CPU, memory, disk, network counters (Windows PDH), GPU via NVIDIA's NVML | System widgets and island tiles — sampled **only while one is visible** | No |
| Now-playing info from the Windows media session API | Media widget/island | No |
| Name of the foreground process + whether it is fullscreen | Hiding during games; opt-in automation rules | No (not logged) |
| File names of in-progress downloads in your Downloads folder | Opt-in island download indicator, **off by default** | No |
| `Rainmeter.ini` and skin `.ini` files | Rainmeter library (read-only) | No |
| Clipboard (text, links, images, file paths) | Opt-in **Clipboard history**, **off by default**. Last 40 items in memory; forgotten on quit or when switched off; only pinned text is saved. Copies flagged private by the copying app (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory = 0`, `Clipboard Viewer Ignore` — what password managers set) are never read into history | No |
| Which apps currently use the microphone/camera (`HKCU\…\CapabilityAccessManager\ConsentStore`) | The orange/green privacy dot. Read when Windows signals a change; app names only, not audio or video | No (not logged) |
| Output audio level (Windows' peak meter) and volume/mute changes | Volume meter and music-reactive bars. A single loudness number, read only while music plays in a visible capsule; no audio is recorded | No |
| Files you drop on the island's shelf | Paths only (files are not copied); memory only | No |

## 3. Network access

There is exactly **one** class that can open a network connection: `NetworkGate` in `src/Glasswave/Services/Network.cs`.

* It refuses every request unless **Settings › Privacy › Allow optional internet features** is on. The switch is **off by default**.
* HTTPS only; no cookies, no redirects, no custom headers, no identifiers.
* Every attempt (including blocked ones) is recorded in an in-memory ledger visible under **About & Diagnostics › Network ledger**.

Two optional features use it, each with its own switch under **Settings › Privacy & network**, both off by default:

| Feature | Request | When | Data sent |
|---|---|---|---|
| Weather (Open-Meteo, no account or key) | `geocoding-api.open-meteo.com/v1/search?name=…` | When you search for a place | The place name you typed |
| | `api.open-meteo.com/v1/forecast?latitude=…&longitude=…` | At most every 30 minutes while a Weather widget is visible | Coordinates of the chosen place (rounded to 3 decimals) |
| Synced lyrics (LRCLIB, no account or key) | `lrclib.net/api/get?track_name=…&artist_name=…&album_name=…&duration=…`, then `/api/search` if there is no exact match | Once per new song while lyrics are on | The song's title, artist, album and length — what your player publishes to Windows. Results are cached in memory, never on disk |

Your IP address is inevitably visible to those services when you enable a feature, as with any web request.

Links such as "Open rainmeter.net" open in **your own browser**; Glasswave itself makes no request.

There is **no** update checker. New versions are installed by running a newer installer.

## 4. Live wallpaper snapshots

When Wallpaper Engine or Lively Wallpaper draws your desktop, the real wallpaper is not a file Windows knows about. If **Match live wallpapers** is on (default), Glasswave calls `PrintWindow` on the desktop's wallpaper host window — not on any application window — every 3 minutes, paused while a fullscreen app runs. The image is downscaled, blurred and kept in memory only. Desktop icons may be included in the snapshot (Windows composes them into that layer); they are reduced to soft blurs and excluded from colour extraction. Turn it off on **Wallpaper & Gradients**.

## 5. Verification performed

1. **Dependency audit.** The app has **zero NuGet package dependencies** — only the .NET 8 / WPF framework and Windows APIs. There is no third-party SDK, analytics or crash-reporting library to audit. (`src/Glasswave/Glasswave.csproj` contains no `PackageReference`.)
2. **Static check (automated test).** `PrivacyTests.Only_the_network_gate_creates_http_clients` scans every source file and fails the build if anything other than `Network.cs` constructs an `HttpClient`, `WebClient`, `Socket`, `TcpClient` or `WebRequest`.
3. **Behavioural tests.** `Network_gate_refuses_when_switched_off` and `Network_gate_refuses_plain_http` prove the gate blocks requests with the default settings and rejects non-HTTPS URLs. `Lyrics_need_both_switches_and_never_touch_the_network_when_off` proves a lyrics lookup makes no request unless both the network switch and the lyrics switch are on.
4. **Runtime check.** With default settings, during the idle benchmark, `Get-NetTCPConnection` and `Get-NetUDPEndpoint` for the Glasswave process returned **0 TCP connections and 0 UDP endpoints**. (The process does load the framework assemblies `System.Net.Requests` and `System.Net.WebClient`: WPF's resource loader uses them internally to resolve `pack://application` URIs for embedded images such as the app icon. They open no sockets — confirmed by the zero counts above.)
5. **Build tooling.** `tools/build-release.ps1` sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` so even the .NET SDK used to build Glasswave does not send usage data.

## 6. What is *not* Glasswave

Windows itself (Windows Update, Defender, Microsoft Store), the NVIDIA driver, Wallpaper Engine/Steam and Rainmeter are separate programs with their own network behaviour. Glasswave does not change, block or inspect their traffic.
