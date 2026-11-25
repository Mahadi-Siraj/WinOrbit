# WinOrbit privacy audit

**Summary: WinOrbit contains no telemetry, analytics, tracking, advertising identifiers, crash uploading, accounts or automatic update checks. With default settings it makes zero network connections.**

This document describes what the application stores, what it can transmit, and how that was verified.

## 1. What WinOrbit stores

Everything lives in one folder: `%APPDATA%\WinOrbit` (or `Data\` beside the exe in portable mode).

| File | Contents | Why |
|---|---|---|
| `settings.json` | Active theme, island settings, widget positions/options, layout, hotkeys, automation rules, Rainmeter favourites/recent list, privacy switches | Restore your desktop after a restart |
| `data.json` | Notes, task lists, focus-session history (start time + minutes), clipboard items **you pinned** (text only), how often you opened each launcher result (to rank your favourites first), events you added in the island's calendar (day, optional time, title), reminders you set from the launcher (stored the same way, marked as a reminder) or events imported from an `.ics` file you picked (plus the event's UID and the file's name, so a re-import updates instead of doubling; locations, attendees, descriptions and alarms in the file are not kept) | Widget content, launcher ranking, event reminders |
| `backups\nightlight-state.bin` | Windows' night-light state as it was before the island's Night light tile first changed it | Restore if a future Windows changes the format |
| `themes\*.json`, `profiles\*.json` | Themes and profiles you created | Your customisations |
| `sets\*.json` | Desktop sets you saved or imported: widget kinds, styles, sizes and positions (as shares of the screen), a theme name, the island's glance and the liquid-glass choice. Nothing is sent anywhere; a set leaves only as a `.winorbitset` file you save yourself with **Share**, and that file never carries pinned apps or their paths, the greeting's name or quotes you typed | Your desktop looks, one click away |
| `backups\windows-settings-backup.json` | Original values of Windows settings WinOrbit changed (e.g. `AppsUseLightTheme`, accent colour, wallpaper path) | **Revert all** and uninstall rollback |
| `wallpapers\gradient-*.png` | Only if you use "Apply as wallpaper" | The generated wallpaper file |
| `logs\winorbit.log` | Local diagnostic messages (start-up, errors, which Windows setting changed). Rotates at 512 KB, max 2 files | Troubleshooting. Never uploaded |
| `startup.jitprofile` | Installed version only. Written by .NET itself (multicore JIT): which methods of WinOrbit, .NET and the Windows Runtime were compiled while WinOrbit started. Method names only; nothing about you, your files or what is on screen. Rewritten at each start, not included in backups, safe to delete | Start faster: next time .NET compiles those methods on a spare core in advance |

WinOrbit also writes, **only when you ask it to**, per-user Windows values: the `WinOrbit` entry under `HKCU\…\Run` (start with Windows), and the appearance values listed on the Windows Tweaks page. All are under `HKEY_CURRENT_USER`; nothing machine-wide is changed.

Not stored anywhere: unpinned clipboard history (memory only, see §2), browsing data, window titles, keystrokes, screenshots, lyrics, the small blurred screen pictures used by the opt-in Live glass (memory only, see §4b). The live-wallpaper snapshot (see §4) is kept in memory only.

## 2. What WinOrbit reads on your PC

| Data | Purpose | Leaves the PC? |
|---|---|---|
| Wallpaper image file / live-wallpaper layer | Colour extraction and frosted backdrops; bare widgets also average the blurred picture under them to choose light or dark text | No |
| CPU, memory, disk, network counters (Windows PDH), GPU via NVIDIA's NVML | System widgets and island tiles — sampled **only while one is visible** | No |
| Now-playing info from the Windows media session API, including the artwork thumbnail | Media widget/island; the artwork's colours are worked out in memory for the album-colour glass, equaliser and rim | No |
| Name of the foreground process + whether it is fullscreen; Windows' "this window moved or resized" notices for that one app's windows | Hiding during games and for the apps you choose; opt-in automation rules | No (not logged) |
| Names and file descriptions of apps with a window open | Only while the "Add app…" list is open in island settings; the app you pick is saved by process name | No |
| File names of in-progress downloads in your Downloads folder, and the finished file's name | Opt-in island download indicator and "downloaded" card, **off by default**. A click opens the file with its usual app — except programs, installers, scripts and shortcuts, which are only shown in their folder | No |
| Names of new pictures in your Screenshots folder, and a small thumbnail of each new one | **Screenshot** card. Windows reports each new file (`FileSystemWatcher`); nothing is scanned on a timer. The thumbnail is decoded in memory; nothing is copied or uploaded | No (not logged) |
| `Rainmeter.ini` and skin `.ini` files | Rainmeter library (read-only) | No |
| Clipboard (text, links, images, file paths) | Opt-in **Clipboard history**, **off by default**. Last 40 items in memory; forgotten on quit or when switched off; only pinned text is saved. Copies flagged private by the copying app (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory = 0`, `Clipboard Viewer Ignore` — what password managers set) are never read into history | No |
| Which apps currently use the microphone/camera (`HKCU\…\CapabilityAccessManager\ConsentStore`) | The orange/green privacy dot. Read when Windows signals a change; app names only, not audio or video | No (not logged) |
| Output audio level (Windows' peak meter) and volume/mute changes | Volume meter, music-reactive bars and the **Music line** widget. A single loudness number, read only while music plays in a visible capsule or Music line; no audio is recorded | No |
| Battery status (`GetSystemPowerStatus`): on mains or battery, charge and time left | **Battery** widget; refreshed when Windows reports a power change | No |
| Size and free space of fixed drives (`DriveInfo`) | **Drive space** widget, every five minutes while it is on screen; network and removable drives are skipped, no files or folders are read | No |
| Which apps have audio on the default output device, each one's volume and mute (Windows' own volume-mixer sessions), and those apps' program names and icons | **Volume by app** in the Control Centre. Read only while that list is open (every 1.5 s, to follow apps that start or stop); a volume is changed only when you move its slider. No audio is read | No (not logged) |
| Names of your playback devices and which one is the default (Core Audio endpoint list) | **Output** list in the Volume-by-app view, read only while it is open. The default device changes only when you click one (through `IPolicyConfig`, an undocumented Windows interface — the one Windows' own Sound settings uses) | No (not logged) |
| Files you drop on the island's shelf | Paths only (files are not copied); memory only | No |
| Windows' "All apps" list (`shell:AppsFolder`) and the shortcuts in your Recent folder | Launcher search, read when you open the launcher | No |
| Window titles of known media players only (VLC, foobar2000, Winamp, PotPlayer, MPC-HC/BE, mpv) and those players' own audio level | Now-playing for players that don't report to Windows. WinOrbit gets Windows' "a title changed" event, checks which program the window belongs to, and reads the title only for those players | No (not logged) |
| Power source, battery %, laptop brightness, Energy Saver state | Charging/low-battery/brightness activities and the Control Centre | No |
| Default microphone's mute state | Microphone tile | No |
| Default microphone's peak level (Windows' meter: one loudness number) and its mute state | **Microphone pill**, only while an app is using the microphone and the pill is on screen (20 times a second; once a second while muted). The meter reports the stream the other app already has open — WinOrbit never opens a microphone stream, so Windows never lists WinOrbit as using the microphone (checked by a test). No audio is read or recorded. Mute is changed only when you press the pill's button | No (not logged) |
| Mouse-wheel and sideways-scroll steps (a low-level mouse hook, `WH_MOUSE_LL`) | Volume and swipe-to-skip on the island. The hook is installed **only while the pointer is over the island** and removed when it leaves; it runs inside WinOrbit (nothing is injected into other apps). Wheel steps over the island are used; everything else — clicks, movement, scrolling elsewhere — is passed straight on, unread and unstored. No keyboard input is involved | No |
| Whether Caps Lock, Num Lock and Scroll Lock are on (`GetKeyState`, three on/off bits) | Lock-key alert. Checked about eight times a second **only while that alert is on**. WinOrbit never receives keystrokes — no keyboard hook, no raw input | No |
| Which keyboard layout the app in front is using (`GetKeyboardLayout`, a layout ID such as "English (US), US keys") and the list of installed keyboards | Keyboard-language card. Runs **only when more than one keyboard is installed**, checking about four times a second; with one keyboard nothing runs, and adding one is noticed through Windows' registry change notification on `HKCU\Keyboard Layout\Preload`. Layout names are read from `HKLM\SYSTEM\…\Keyboard Layouts` (read-only). No keystrokes are ever read | No (not logged) |
| Current network connection's type and name (Windows `NetworkStatusChanged` event, and the Wi-Fi service's own connected/disconnected notifications via the WLAN API) | Wi-Fi / Ethernet connected and disconnected alerts. Only the name of the network you joined is read — no scanning of nearby networks | No (not logged) |
| Whether the Wi-Fi and Bluetooth radios are on (`Windows.Devices.Radios` state-change events) | "Wi-Fi on/off" and "Bluetooth on/off" alerts | No (not logged) |
| The connection type (Wi-Fi, Ethernet or none), whether Bluetooth is on, and battery level and charging, from the same Windows events as above | The island's **Status** glance (off by default; Dynamic Island › Collapsed capsule). Listened to only while that glance is on screen in a collapsed capsule; network names are not read for it | No (not logged) |
| The mouse pointer's position on the screen (`GetCursorPos`) | The island's **Eyes** glance (off by default), so the eyes can look at the pointer. Read 25 times a second while the eyes are moving or blinking and 5 times a second while they rest, **only while that glance is on screen in a collapsed capsule**; nothing runs while it's hidden, open or covered by a fullscreen app. Only where the pointer is, not clicks or keys. The position is kept for one step and then replaced; it is not stored or logged | No |
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

**Cards from your scripts and apps** (Dynamic Island › Live activities, **off by default**) is not a network feature and does not use the gate. While it is on, WinOrbit opens one local named pipe, `\\.\pipe\WinOrbit.Island`; while it is off the pipe does not exist. The pipe is created with `PipeOptions.CurrentUserOnly`, so only programs running under your own Windows account can connect, and WinOrbit also hangs up on any client that reaches it from another computer. It reads one line of JSON (at most 2 KB) and shows it as a plain-text card headed by the sending program's name (looked up with `GetNamedPipeClientProcessId`). The card has no links, commands or buttons, and nothing in it is stored. Each program gets three cards at once, then one every five seconds (six at once and one every two seconds for all programs together). The log records the program's name for each card, never the text.

**Coding agents** (Dynamic Island › Coding agents, **off by default**) uses the same local pipe and the same protections, and is not a network feature either. AI coding tools you connect yourself (Claude Code, Codex, your own scripts) run `WinOrbit.exe --agent <name>` from their hooks. That short-lived command reads what the tool hands its hooks, keeps only the session's id, the first ~60 characters of its first request as a title, the project folder's **name** (not its path), a state (working, needs you, done) and a few words such as "Editing app.cs" or Claude's own "needs your permission to use Bash". Prompts, answers, commands and file contents are dropped there, before anything reaches WinOrbit. The running app keeps at most eight sessions in memory, drops a session after three hours of silence, writes nothing to disk or the log, and forgets them all when the switch is turned off or WinOrbit quits. WinOrbit never edits those tools' settings; you paste the setup yourself. While the island shows a session's title, anyone who can see your screen (or your screen share) can read it.

Links such as "Open rainmeter.net", and the launcher's "Search the web for …", open in **your own browser**; WinOrbit itself makes no request.

There is **no** automatic update checker. **Settings › Privacy & network › Updates › Check now** makes one request when you click it, and only while the network switch is on: `raw.githubusercontent.com/Mahadi-Siraj/WinOrbit/main/VERSION`, the one-line version file releases are published from. Nothing is sent but the request itself (GitHub sees your IP address), it shows in the request ledger, and nothing is downloaded or installed; if a newer version exists, WinOrbit offers to open the release page in your browser.

## 4. Live wallpaper snapshots

When Wallpaper Engine or Lively Wallpaper draws your desktop, the real wallpaper is not a file Windows knows about. If **Match live wallpapers** is on (default), WinOrbit calls `PrintWindow` on the desktop's wallpaper host window — not on any application window — every 3 minutes, paused while a fullscreen app runs. The image is downscaled, blurred and kept in memory only. Desktop icons may be included in the snapshot (Windows composes them into that layer); they are reduced to soft blurs and excluded from colour extraction. Turn it off on **Wallpaper & Gradients**.

## 4b. Live glass (screen capture, off by default)

**Live glass** (Appearance › Glass, **off by default**) makes the glass show the windows behind it. While it is on, WinOrbit captures the screens on which its glass is showing, through Windows' own screen-capture API (`Windows.Graphics.Capture`, without the cursor):

* Each frame stays on the graphics card, which shrinks it to an eighth of its width and height (240 × 135 for a 1080p screen), or to a quarter (480 × 270) with nearly clear looks such as Liquid Clear and Liquid Frosted. Only that small picture reaches WinOrbit's memory, where it is blurred like the wallpaper glass and drawn behind the glass. Each picture replaces the last; none is saved to disk, logged or sent anywhere.
* Capture runs only while WinOrbit glass is visible on that screen, never during a fullscreen app on it or while the screen is off, and Windows delivers a frame only when something changed. At most 10 pictures a second are taken (20 with the nearly clear looks).
* WinOrbit's island, widgets and launcher are marked to be left out of every screen capture (`SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE`) so the glass doesn't capture itself. That also leaves them out of **your** screenshots, recordings and screen sharing while the setting is on. Turning it off removes the mark. The settings window is never marked.
* Windows shows no capture frame for it on Windows 11; on Windows 10 (which would draw a yellow frame round the screen) the setting is unavailable.

## 5. Verification performed

1. **Dependency audit.** The app has **zero NuGet package dependencies** — only the .NET 10 / WPF framework and Windows APIs. There is no third-party SDK, analytics or crash-reporting library to audit. (`src/WinOrbit/WinOrbit.csproj` contains no `PackageReference`.)
2. **Static check (automated test).** `PrivacyTests.Only_the_network_gate_creates_http_clients` scans every source file and fails the build if anything other than `Network.cs` constructs an `HttpClient`, `WebClient`, `Socket`, `TcpClient` or `WebRequest`.
3. **Behavioural tests.** `Network_gate_refuses_when_switched_off` and `Network_gate_refuses_plain_http` prove the gate blocks requests with the default settings and rejects non-HTTPS URLs. `Lyrics_need_both_switches_and_never_touch_the_network_when_off` proves a lyrics lookup makes no request unless both the network switch and the lyrics switch are on.
4. **Runtime check.** With default settings, during the idle benchmark, `Get-NetTCPConnection` and `Get-NetUDPEndpoint` for the WinOrbit process returned **0 TCP connections and 0 UDP endpoints**. (The process does load the framework assemblies `System.Net.Requests` and `System.Net.WebClient`: WPF's resource loader uses them internally to resolve `pack://application` URIs for embedded images such as the app icon. They open no sockets — confirmed by the zero counts above.)
5. **Build tooling.** `tools/build-release.ps1` sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` so even the .NET SDK used to build WinOrbit does not send usage data.

## 6. What is *not* WinOrbit

Windows itself (Windows Update, Defender, Microsoft Store), the NVIDIA driver, Wallpaper Engine/Steam and Rainmeter are separate programs with their own network behaviour. WinOrbit does not change, block or inspect their traffic.
