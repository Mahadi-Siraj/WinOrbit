# WinOrbit privacy audit

**Summary: WinOrbit contains no telemetry, analytics, tracking, advertising identifiers, crash uploading, accounts or automatic update checks. With default settings it makes zero network connections.**

This document describes what the application stores, what it can transmit, and how that was verified.

## 1. What WinOrbit stores

Everything lives in one folder: `%APPDATA%\WinOrbit` (or `Data\` beside the exe in portable mode).

| File | Contents | Why |
|---|---|---|
| `settings.json` | Active theme, island settings, widget positions/options, layout, hotkeys, automation rules, Rainmeter favourites/recent list, privacy switches | Restore your desktop after a restart |
| `data.json` | Notes, task lists, focus-session history (start time + minutes), clipboard items **you pinned** (text only), how often you opened each launcher result (to rank your favourites first), events you added in the island's calendar (day, optional time, title) or imported from an `.ics` file you picked (plus the event's UID and the file's name, so a re-import updates instead of doubling; locations, attendees, descriptions and alarms in the file are not kept) | Widget content, launcher ranking, event reminders |
| `backups\nightlight-state.bin` | Windows' night-light state as it was before the island's Night light tile first changed it | Restore if a future Windows changes the format |
| `themes\*.json`, `profiles\*.json` | Themes and profiles you created | Your customisations |
| `backups\windows-settings-backup.json` | Original values of Windows settings WinOrbit changed (e.g. `AppsUseLightTheme`, accent colour, wallpaper path) | **Revert all** and uninstall rollback |
| `wallpapers\gradient-*.png` | Only if you use "Apply as wallpaper" | The generated wallpaper file |
| `logs\winorbit.log` | Local diagnostic messages (start-up, errors, which Windows setting changed). Rotates at 512 KB, max 2 files | Troubleshooting. Never uploaded |

WinOrbit also writes, **only when you ask it to**, per-user Windows values: the `WinOrbit` entry under `HKCU\…\Run` (start with Windows), and the appearance values listed on the Windows Tweaks page. All are under `HKEY_CURRENT_USER`; nothing machine-wide is changed.

Not stored anywhere: unpinned clipboard history (memory only, see §2), browsing data, window titles, keystrokes, screenshots, lyrics. The live-wallpaper snapshot (see §4) is kept in memory only.

## 2. What WinOrbit reads on your PC

| Data | Purpose | Leaves the PC? |
|---|---|---|
| Wallpaper image file / live-wallpaper layer | Colour extraction and frosted backdrops | No |
| CPU, memory, disk, network counters (Windows PDH), GPU via NVIDIA's NVML | System widgets and island tiles — sampled **only while one is visible** | No |
| Now-playing info from the Windows media session API, including the artwork thumbnail | Media widget/island; the artwork's colours are worked out in memory for the album-colour glass, equaliser and rim | No |
| Name of the foreground process + whether it is fullscreen; Windows' "this window moved or resized" notices for that one app's windows | Hiding during games and for the apps you choose; opt-in automation rules | No (not logged) |
| Names and file descriptions of apps with a window open | Only while the "Add app…" list is open in island settings; the app you pick is saved by process name | No |
| File names of in-progress downloads in your Downloads folder, and the finished file's name | Opt-in island download indicator and "downloaded" card, **off by default**. A click opens the file with its usual app — except programs, installers, scripts and shortcuts, which are only shown in their folder | No |
| Names of new pictures in your Screenshots folder, and a small thumbnail of each new one | **Screenshot** card. Windows reports each new file (`FileSystemWatcher`); nothing is scanned on a timer. The thumbnail is decoded in memory; nothing is copied or uploaded | No (not logged) |
| `Rainmeter.ini` and skin `.ini` files | Rainmeter library (read-only) | No |
| Clipboard (text, links, images, file paths) | Opt-in **Clipboard history**, **off by default**. Last 40 items in memory; forgotten on quit or when switched off; only pinned text is saved. Copies flagged private by the copying app (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory = 0`, `Clipboard Viewer Ignore` — what password managers set) are never read into history | No |
| Which apps currently use the microphone/camera (`HKCU\…\CapabilityAccessManager\ConsentStore`) | The orange/green privacy dot. Read when Windows signals a change; app names only, not audio or video | No (not logged) |
| Output audio level (Windows' peak meter) and volume/mute changes | Volume meter and music-reactive bars. A single loudness number, read only while music plays in a visible capsule; no audio is recorded | No |
| Which apps have audio on the default output device, each one's volume and mute (Windows' own volume-mixer sessions), and those apps' program names and icons | **Volume by app** in the Control Centre. Read only while that list is open (every 1.5 s, to follow apps that start or stop); a volume is changed only when you move its slider. No audio is read | No (not logged) |
| Names of your playback devices and which one is the default (Core Audio endpoint list) | **Output** list in the Volume-by-app view, read only while it is open. The default device changes only when you click one (through `IPolicyConfig`, an undocumented Windows interface — the one Windows' own Sound settings uses) | No (not logged) |
| Files you drop on the island's shelf | Paths only (files are not copied); memory only | No |
| Windows' "All apps" list (`shell:AppsFolder`) and the shortcuts in your Recent folder | Launcher search, read when you open the launcher | No |
| Window titles of known media players only (VLC, foobar2000, Winamp, PotPlayer, MPC-HC/BE, mpv) and those players' own audio level | Now-playing for players that don't report to Windows. WinOrbit gets Windows' "a title changed" event, checks which program the window belongs to, and reads the title only for those players | No (not logged) |
| Power source, battery %, laptop brightness, Energy Saver state | Charging/low-battery/brightness activities and the Control Centre | No |
| Default microphone's mute state | Microphone tile | No |
| Whether Caps Lock, Num Lock and Scroll Lock are on (`GetKeyState`, three on/off bits) | Lock-key alert. Checked about eight times a second **only while that alert is on**. WinOrbit never receives keystrokes — no keyboard hook, no raw input | No |
| Which keyboard layout the app in front is using (`GetKeyboardLayout`, a layout ID such as "English (US), US keys") and the list of installed keyboards | Keyboard-language card. Runs **only when more than one keyboard is installed**, checking about four times a second; with one keyboard nothing runs, and adding one is noticed through Windows' registry change notification on `HKCU\Keyboard Layout\Preload`. Layout names are read from `HKLM\SYSTEM\…\Keyboard Layouts` (read-only). No keystrokes are ever read | No (not logged) |
| Current network connection's type and name (Windows `NetworkStatusChanged` event, and the Wi-Fi service's own connected/disconnected notifications via the WLAN API) | Wi-Fi / Ethernet connected and disconnected alerts. Only the name of the network you joined is read — no scanning of nearby networks | No (not logged) |
| Whether the Wi-Fi and Bluetooth radios are on (`Windows.Devices.Radios` state-change events) | "Wi-Fi on/off" and "Bluetooth on/off" alerts | No (not logged) |
| Three on/off values Windows stores when you flip them: dark/light mode (`HKCU\…\Themes\Personalize`), night light (its CloudStore state), airplane mode (`HKLM\SYSTEM\…\RadioManagement\SystemRadioState`, read-only) | Mode alerts. Windows wakes WinOrbit when one of those keys is written (`RegNotifyChangeKeyValue`); nothing is polled and nothing is written | No (not logged) |
| Names and connection state of **paired** Bluetooth devices (device watchers plus each paired device's `ConnectionStatusChanged` event); names of plugged-in USB devices | Bluetooth and USB alerts. Windows pushes the changes; nothing scans for nearby devices, and listening for a device's status does not connect to it | No (not logged) |
| A Bluetooth device's battery level, as Windows already records it (the `DEVPKEY_Bluetooth_Battery` device property), read once or twice in the few seconds after that device connects | "Connected · 80% battery" on the Bluetooth card. Nothing is sent to the device | No (not logged) |

**Rain soon** (Dynamic Island › Live activities, **off by default**) uses the weather request described below. While it is on — and only if weather itself is on — WinOrbit asks for your place's forecast every 30 minutes even when no weather is on screen. It shares the weather cache, so with a weather widget open there are no extra requests. Each request shows in the network ledger.

## 3. Network access

There is exactly **one** class that can open a network connection: `NetworkGate` in `src/WinOrbit/Services/Network.cs`.

* It refuses every request unless **Settings › Privacy › Allow optional internet features** is on. The switch is **off by default**.
* HTTPS only; no cookies, no redirects, no custom headers, no identifiers.
* Every attempt (including blocked ones) is recorded in an in-memory ledger visible under **About & Diagnostics › Network ledger**.

Two optional features use it, each with its own switch under **Settings › Privacy & network**, both off by default:

| Feature | Request | When | Data sent |
|---|---|---|---|
| Weather (Open-Meteo, no account or key) | `geocoding-api.open-meteo.com/v1/search?name=…` | When you search for a place | The place name you typed |
| | `api.open-meteo.com/v1/forecast?latitude=…&longitude=…` | At most every 30 minutes while a Weather widget is visible, or when you open the island's Weather page or launcher (the same 30-minute cache) | Coordinates of the chosen place (rounded to 3 decimals) and your unit choice |
| Synced lyrics (LRCLIB, no account or key) | `lrclib.net/api/get?track_name=…&artist_name=…&album_name=…&duration=…`, then up to two `/api/search` requests if there is no exact match (for example the name split from a video title "Artist - Song") | Once per new song while lyrics are on; at most three requests per song | The song's title, artist, album and length — what your player publishes to Windows, with video-page extras such as "(Official Video)" removed. Results are cached in memory, never on disk |

Your IP address is inevitably visible to those services when you enable a feature, as with any web request.

Links such as "Open rainmeter.net", and the launcher's "Search the web for …", open in **your own browser**; WinOrbit itself makes no request.

There is **no** automatic update checker. **Settings › Privacy & network › Updates › Check now** makes one request when you click it, and only while the network switch is on: `raw.githubusercontent.com/Mahadi-Siraj/WinOrbit/main/VERSION`, the one-line version file releases are published from. Nothing is sent but the request itself (GitHub sees your IP address), it shows in the request ledger, and nothing is downloaded or installed; if a newer version exists, WinOrbit offers to open the release page in your browser.

## 4. Live wallpaper snapshots

When Wallpaper Engine or Lively Wallpaper draws your desktop, the real wallpaper is not a file Windows knows about. If **Match live wallpapers** is on (default), WinOrbit calls `PrintWindow` on the desktop's wallpaper host window — not on any application window — every 3 minutes, paused while a fullscreen app runs. The image is downscaled, blurred and kept in memory only. Desktop icons may be included in the snapshot (Windows composes them into that layer); they are reduced to soft blurs and excluded from colour extraction. Turn it off on **Wallpaper & Gradients**.

## 5. Verification performed

1. **Dependency audit.** The app has **zero NuGet package dependencies** — only the .NET 8 / WPF framework and Windows APIs. There is no third-party SDK, analytics or crash-reporting library to audit. (`src/WinOrbit/WinOrbit.csproj` contains no `PackageReference`.)
2. **Static check (automated test).** `PrivacyTests.Only_the_network_gate_creates_http_clients` scans every source file and fails the build if anything other than `Network.cs` constructs an `HttpClient`, `WebClient`, `Socket`, `TcpClient` or `WebRequest`.
3. **Behavioural tests.** `Network_gate_refuses_when_switched_off` and `Network_gate_refuses_plain_http` prove the gate blocks requests with the default settings and rejects non-HTTPS URLs. `Lyrics_need_both_switches_and_never_touch_the_network_when_off` proves a lyrics lookup makes no request unless both the network switch and the lyrics switch are on.
4. **Runtime check.** With default settings, during the idle benchmark, `Get-NetTCPConnection` and `Get-NetUDPEndpoint` for the WinOrbit process returned **0 TCP connections and 0 UDP endpoints**. (The process does load the framework assemblies `System.Net.Requests` and `System.Net.WebClient`: WPF's resource loader uses them internally to resolve `pack://application` URIs for embedded images such as the app icon. They open no sockets — confirmed by the zero counts above.)
5. **Build tooling.** `tools/build-release.ps1` sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` so even the .NET SDK used to build WinOrbit does not send usage data.

## 6. What is *not* WinOrbit

Windows itself (Windows Update, Defender, Microsoft Store), the NVIDIA driver, Wallpaper Engine/Steam and Rainmeter are separate programs with their own network behaviour. WinOrbit does not change, block or inspect their traffic.
