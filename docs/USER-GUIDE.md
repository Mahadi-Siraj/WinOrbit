# WinOrbit user guide

## Getting around

| Action | How |
|---|---|
| Open WinOrbit | Tray icon (click), Start menu, or **Ctrl+Alt+G** |
| Open the Dynamic Island with the keyboard | **Ctrl+Alt+Space** (if another app owns it, WinOrbit moves it to **Ctrl+Alt+I** and tells you on Settings) |
| Show / hide all widgets | **Ctrl+Alt+W** |
| Jump between sections | **Ctrl+1 … Ctrl+9** in the main window |
| Undo / redo theme edits | **Ctrl+Z / Ctrl+Y** in the Liquid Glass Editor |
| Open a section from a shortcut | `WinOrbit.exe --page widgets` (any of: home, appearance, studio, wallpaper, island, widgets, rainmeter, tweaks, profiles, settings, about) |
| Switch profile from a shortcut, Stream Deck or Task Scheduler | `WinOrbit.exe --profile lofi` (a profile's name or id, case and spaces don't matter: `study`, `gaming`, `"Lo-fi"`, `night`…) |
| Screenshot that includes WinOrbit | **Screenshot with WinOrbit** in the tray menu, or `WinOrbit.exe --screenshot` (saved to your Screenshots folder and copied; add a folder after it to save there instead) |

Closing the main window keeps the island and widgets running from the tray (turn this off in Settings). **Quit WinOrbit** is in the tray menu.

### First start

The first time WinOrbit opens, Home walks you through three short steps. Every choice shows on your desktop the moment you make it, and you can change all of it later.

1. **Pick a look**: three desktop sets (**Orbit**, **Poster** and **Terminal**, each drawn live on a picture of your own wallpaper), or **Keep it simple** (a clock, your music and a system card on glass). Trying a set and then picking Keep it simple puts the starter widgets back.
2. **Glass and colour**: **Liquid glass** or **Classic glass**, on a live sample; the **Surface** (Glass, Gradient, Smoked, Solid or Black, for people who'd rather have no glass); the colour mode (Auto, Dark or Light); and **Live glass** (off unless you turn it on). If this window is being drawn without the graphics card, the sample says so, because it can't show the lens then.
3. **How many settings**: **Simple settings** (recommended) or **All settings**, plus clipboard history, synced lyrics and **Tell me about updates** (all off unless you turn them on).

**Skip setup** keeps everything as it is.

### Simple and advanced settings

New installs start with **simple settings**: each page shows the settings most people change, and a note at the bottom says how many more are hidden there, with **Show advanced settings**. The Liquid Glass Editor (studio), Wallpaper & Gradients, Windows Tweaks and Rainmeter pages appear in the sidebar only with advanced settings on. Switch any time in **Settings › Settings mode**. Search always finds every setting: a hidden one is shown when you pick it. If you used WinOrbit before 2.0, you keep all settings.

## Dynamic Island

* **Click** the capsule to expand; **Esc** or clicking elsewhere collapses it. It also collapses a few seconds after the pointer leaves (configurable; pin it open with the pin button).
* **Scroll** on the capsule to change volume. **Swipe sideways** on it (two fingers on a touchpad, or tilt the mouse wheel) to skip to the next or previous track (Dynamic Island › Live activities › Swipe to skip tracks). Both work even when Windows' "Scroll inactive windows when I hover over them" is off. **Drag** it to move it; **right-click** for quick options.
* **Microphone pill** — while an app uses your microphone (a call in Teams, Zoom, Discord, a recording in OBS), the capsule shows a mute button, the app's name and three orange bars that follow your voice. Press the button to mute: it's Windows' own microphone mute, the same switch as the taskbar's, so the app can't hear you whatever it is; the button turns red and the pill says "muted". Muting from the keyboard or Settings shows here within half a second. If several apps use the mic, the pill shows the first and "+2". The level comes from Windows' meter, which only reports how loud the app's existing stream is; WinOrbit never opens or records the microphone. Turn it off in Dynamic Island › Live activities › Microphone pill (the orange privacy dot then comes back).
* **Look** (Dynamic Island › Appearance › Material): **Glass** frosts your wallpaper, **Gradient** fades from your accent colour into your wallpaper's colours (opaque), **Smoked** is a deep tint, **Solid** is opaque in the theme's colour, and **Black** is plain true black with one fine light edge: no wallpaper colour, glow or highlight, and always light text, even with a light theme. Black is the cleanest look and the cheapest to draw. A coloured rim, if you pick one, still shows on it.
* **Centring:** while you drag, the island clicks into the exact centre when it gets within 18 px. To put it back in one go: right-click › **Centre the island**, or Dynamic Island page › **Centre now**. **Keep centred** locks it horizontally so dragging only changes the distance from the top.
* **Volume by app** — in the Control Centre, the mixer button at the end of the volume row swaps the tiles for one slider per app that has sound (the same list as Windows' volume mixer), each with its own mute button. Apps playing right now come first; apps that start or stop appear within a couple of seconds. Press the button again (now showing tiles) to go back. The volumes are Windows' own, so they stay set after WinOrbit closes.
* **Output device** — with two or more playback devices (speakers, headphones, a monitor's HDMI sound, Bluetooth), the same mixer view lists them under **Output**, with a tick on the one in use. Click another to send all sound there (music, games and calls). Windows has no public way for apps to do this, so WinOrbit uses the same internal interface Windows' own Sound settings uses; if Windows ever refuses, the click opens Settings › Sound instead.
* **Screenshots** — press **Win+PrtScn** (or let the Snipping Tool save one) and the island opens a card with a thumbnail. **Click** it to open the picture, **drag** the card straight into a chat, an email or a folder, or press the folder button to see it in File Explorer. The card waits while your pointer is on it. Watches your Screenshots folder (Pictures › Screenshots, or wherever you moved it); turn it off in Dynamic Island › Live activities › Screenshots.
* **Finished downloads** — with Download activity on, a card appears when a download finishes: click to open it, drag it into another app, or press the folder button. Programs, installers and scripts are never started from the card; clicking those shows them in their folder instead.
* **Keyboard language** — if you have more than one keyboard, switching (Win+Space, Alt+Shift, the taskbar's language button) shows a key cap with the language code and its name, e.g. **EN** English (United States) · US keyboard. Switching apps that each keep their own keyboard doesn't count as a switch. Turn it off in Dynamic Island › Live activities › Keyboard language.
* **Rain soon** (off by default; needs Weather on in Settings › Privacy & network) — when it's dry now but rain or snow is likely within two hours, the island shows a card like "Showers in about 40 min · 70% chance", once per spell. Click it to open the Weather tab. To do this, WinOrbit checks your place's forecast every 30 minutes; each check shows in the network ledger.
* **Bluetooth battery** — when headphones or another Bluetooth device connect, the "Connected" card adds the battery level a moment later, e.g. **Connected · 80% battery**, or "battery low" in red at 20% or less. It only appears for devices that report their battery to Windows, the same ones that show a level in Settings › Bluetooth & devices.
* **Synced lyrics** (off by default; Settings › Privacy & network › Synced lyrics, with network access on) — the line being sung shows in the capsule. Lyrics come from LRCLIB, a free open database, using the title and artist your player tells Windows. Names from video pages are cleaned up first: "Artist - Song (Official Video) [4K]" on a channel such as "ArtistVEVO" is looked up as *Song* by *Artist*. A song makes at most three requests, and if none matches its length within four seconds, no lyrics are shown rather than out-of-time ones.
* **Quick controls tab** — click a tile to switch it; the **›** in its corner (or right-click) opens the matching Windows settings page:

  | Tile | What a click does | How |
  |---|---|---|
  | Wi-Fi, Bluetooth | Turns the radio on/off | Windows radio API (supported) |
  | Night light | On/off | Experimental: Windows has no public API; WinOrbit flips the same stored state Windows' own toggle does, after checking its format (unknown format → opens Settings instead). The original is saved in `backups\nightlight-state.bin`. |
  | Dark mode | Windows apps + taskbar dark/light; WinOrbit follows when its mode is Auto | Same backed-up setting as Windows Tweaks › Revert all |
  | Power mode | Balanced → Best performance → Best power efficiency | Windows' power-mode slider (only with the Balanced plan; otherwise opens Settings) |
  | Keep awake | Stops sleep and screen-off until switched off or WinOrbit quits | Documented power request |
  | Focus | Starts/stops a focus block (WinOrbit's timer, not Windows "Do not disturb", which has no public API) | |
  | Widgets | Shows/hides all WinOrbit widgets | |
  | Microphone | Mutes/unmutes your default microphone | Windows audio API (supported) |
  | Energy saver | Shows whether it's on; opens Settings to change it | Windows reports the state; it offers apps no switch |
  | Airplane mode, Do not disturb | Open the right Windows panel (DND: the notification centre, where the bell is one click) | Windows offers apps no way to switch these |

* **Launcher — Alt+Space** (or the shortcut shown in Settings): type an app, a setting ("bluetooth", "hover delay"), an action ("dark mode", "lock", "focus"), a sum (`12*7`, `15% of` → `200*15%`) a conversion (`10 km to mi`, `70 f to c`) or a timer (`timer 5 min`, `10 minute timer`, `timer 1h 30m`, `countdown 90s`, `focus 50`, `stopwatch`). Timers count down in the island and end with a sound and a "Timer done" card; a bare `5m` stays a unit conversion (five metres). Reminders work the same way: `remind me in 20 min to stretch`, `remind me at 15:30 to call the bank`, `remind me tomorrow at 9 stand-up`, `remind me to water the plants at 6pm`. The island shows one card on that minute (no ten-minute heads-up), and the reminder appears in the island's calendar, where you can remove it. "At 5" means the next 5 o'clock; a time already past today means tomorrow; the result line always shows the exact time before you press Enter. Ask for the time somewhere else with `time in Tokyo`, `dhaka time` or `what time is it in New York` (cities, countries and abbreviations like PST; worked out from Windows' own time-zone data, no internet); Enter copies it. With clipboard history on, `clip` lists what you copied (newest first) and `clip invoice` finds the items containing "invoice"; Enter puts one back on the clipboard. Copied text only appears after the word `clip`, never in ordinary searches. Enter opens; Lock and Sleep ask for a second Enter. Nothing matches? It offers a web search in your own browser (choose DuckDuckGo, Google or Bing in Settings).
* **Seeking:** on the Media tab, click or drag the bar, or use −10 / +10. Works when the player allows it (YouTube in Chrome/Brave/Edge, Spotify, Media Player); otherwise the bar is greyed out.
* **VLC and other classic players:** the island reads the song from the player's title bar. VLC doesn't accept outside controls unless you turn on its global media keys: VLC › Tools › Preferences › Hotkeys, in the **Global** column set Play/Pause, Next and Previous to your keyboard's media keys, then Save. (WinOrbit never sends commands straight to VLC's window — that crashes VLC 3.0.23.)
* **Preview** live activities (charging, low battery, volume, microphone, the microphone pill (its mute button is pretend and leaves your real microphone alone), the side bubble, Caps Lock, Wi-Fi, Bluetooth, USB, Wi-Fi switched off, Bluetooth switched on, dark mode, night light, airplane mode, Energy Saver, a new song, an event reminder, a screenshot, a finished download, rain soon, the keyboard language) from Dynamic Island › Live activities, or type "preview" in the launcher.
* In the expanded panel: **←/→** or **1–9** switch tabs, **Space** plays/pauses (Media) or starts/pauses (Focus).
* **Calendar:** click a day to see its events. To add one, type in the box under the list and press Enter. Put the time first ("9:30 Stand-up", "7pm Dinner", "14:30 Dentist"); with no time it's an all-day event. Hover an event and click × to remove it. Events with a time pop up as a card ten minutes before and when they start (Dynamic Island › Live activities › Event reminders). Events are saved on this PC only.
* **Importing a calendar:** export your calendar from Outlook (File › Save Calendar), Google Calendar (Settings › Import & export › Export) or Apple Calendar (File › Export) as an `.ics` file, then use Dynamic Island › Calendar events › **Import…**. Repeating events are filled in from 30 days ago to a year ahead (daily, weekly on chosen days, monthly on a date, yearly; moved and cancelled occurrences are respected). It's a one-time copy — nothing syncs and nothing goes online — so import again after big changes; events from the same calendar are updated, not doubled. **Remove** on the same page deletes every imported event and keeps the ones you typed. Rules WinOrbit can't follow exactly (like "the second Tuesday of each month") keep only their first date.
* **Weather:** turn it on and choose a place in Settings › Privacy (it's the same switch as the Weather widget). The page shows now, every other hour for the next twelve, and three days; the glass takes on the sky's colours while it's open.
* The capsule shows one live activity at a time — the microphone pill, a running timer, a download (opt-in) or what's playing, in that order — followed by your **glance**.
* **Glance** (Dynamic Island › Collapsed capsule › When nothing is happening) — what the capsule shows besides its live activity. Many people would rather keep the time in a desktop clock widget, so the island can show something else:
  * **Clock** (the default): the time and date, plus CPU and memory if you turn them on below. If you switch all three off, the time comes back while nothing is playing, so the capsule is never empty.
  * **System**: CPU and memory use, updated every 1.5 seconds while the capsule is visible.
  * **Status**: Wi-Fi (or an Ethernet plug, or a crossed-out Wi-Fi when there's no connection), Bluetooth (dimmed when it's off; hidden on PCs without it) and, on laptops, a small battery that turns red at 20 % and green while charging. Hover for the details. They change when Windows says something changed; nothing is checked on a timer.
  * **Eyes**: two little eyes that follow your mouse pointer, blink now and then and doze off after a minute without movement. They only watch while the capsule is visible and closed, and stop during fullscreen apps. With **Reduce motion** on they don't blink and look straight at the pointer without gliding. They cost a little more power than the others (about 1 % of one CPU core on the test PC); pick another glance if that matters to you.
  * **Quiet**: nothing; just the glass, and your live activities when there are any.
* **Side bubble** — while the capsule shows one of those, a short alert (Caps Lock, the keyboard language, "Copied", "Microphone muted"…) no longer covers it: a small bubble of the same glass pulls away from the capsule's end, the two move aside so they stay centred, and when the alert is over the bubble flows back in and the capsule wobbles as it takes it back. The words show when there's room beside the capsule; otherwise the bubble is just the icon (hover it to read the alert). Click the bubble to open the island. On the left and right edges the bubble sits below the capsule. Bigger news — a device connecting, a screenshot, a download — still opens a full card. Turn it off in Dynamic Island › Live activities › Side bubble for short alerts.
* **Cards from your scripts and apps** (off by default; Dynamic Island › Live activities). Your own scripts can pop a card on the island, for example when a build or backup finishes. Turn it on, then send one line of JSON to the local pipe `WinOrbit.Island`. From PowerShell:

  ```powershell
  $p = [IO.Pipes.NamedPipeClientStream]::new('.', 'WinOrbit.Island', 'InOut'); $p.Connect(2000); $w = [IO.StreamWriter]::new($p); $w.WriteLine('{"title":"Backup done","text":"42 files","icon":"done"}'); $w.Flush(); [IO.StreamReader]::new($p).ReadLine(); $p.Dispose()
  ```

  `title` is required (up to 60 characters). `text` (up to 120), `icon` (`info`, `done`, `warning`, `error` or `bell`) and `seconds` (2–10, default 4) are optional. WinOrbit answers `ok`, `busy` (each program gets three cards at once, then one every five seconds) or `error: …` with the reason; read the answer and close the pipe (WinOrbit waits up to two seconds for that, then moves on to the next sender). The card always starts its second line with the sending program's name (`pwsh · 42 files`), shows plain text only and has no links or buttons. Only programs running under your Windows account on this PC can connect.
* **Coding agents** (off by default; Dynamic Island › Coding agents). If you use AI coding tools on this PC, an **Agents** tab lists each session: **needs you** (orange, e.g. "Claude needs your permission to use Bash"), **working** (blue, with the current step like "Editing app.cs") and **done** (green), with its folder and how long ago. When a session starts waiting for you, a card pops up on the island, and the island keeps an **orange rim** for as long as it waits, so you can't miss it while you're busy; clicking the island then opens straight on the Agents tab. When one finishes, a short "Claude Code finished · api" note shows and the rim turns green for a moment. (A session only "needs you" when the tool actually stops to ask: in modes that let it run everything without asking, such as Claude Code's Bypass permissions, it never waits, so it never turns orange.) **Clear** removes finished sessions and × removes one; the list is in memory only and sessions drop off after three hours of silence. To connect a tool, copy its setup from the same settings section:
  * **Claude Code**: **Copy setup**, then paste it into `%USERPROFILE%\.claude\settings.json` (merge it with any `hooks` you already have), or paste it into Claude Code and ask it to add the hooks. The hooks run in the background, so Claude never waits for WinOrbit. Claude Code reads hooks when a session starts, so sessions that were already open don't report; new ones do. If nothing appears, check that the path in the command still points at the WinOrbit.exe you actually run. **Copy setup** always uses the path of the copy that's open, so after moving a portable copy, or switching between the portable and the installed copy, copy the setup again from the one you use and replace the old hooks. The installed copy lives in `%LOCALAPPDATA%\Programs\WinOrbit` and keeps that path across updates.
  * **Codex**: **Copy line** and add it near the top of `%USERPROFILE%\.codex\config.toml`. Codex tells WinOrbit when it finishes a turn (it has no "needs you" signal through this line).
  * **Anything else** (your own scripts, other tools with hooks): pipe one line of JSON into `WinOrbit.exe --agent <name>` with `session`, `state` (`working`, `needs-you`, `done` or `ended`) and optionally `title`, `text` and `folder`. Hooks in the formats of Gemini CLI (BeforeAgent, AfterAgent, Notification, SessionEnd) and Cursor (beforeSubmitPrompt, stop, sessionEnd) are understood too, but haven't been tried with those tools.

  `--agent` never opens WinOrbit, prints nothing and always succeeds, so a tool can't be held up by it; if WinOrbit isn't running or the switch is off, the update is simply dropped. To turn it all off, switch off **Coding agents** (or press **Turn off** on the empty Agents tab): the tab disappears and the list is forgotten. You can leave the hooks in place; they then do nothing.
* **Screen edge:** Dynamic Island › Placement › Screen edge puts it at the top, the bottom (just above the taskbar), or the left or right side; or right-click it › Screen edge; or simply drag it right up to another edge. It opens away from its edge, auto-hide tucks it behind that edge, and the offsets become "along the edge" and "distance from the edge".
* It hides the moment a game, video or presentation goes fullscreen on its screen (within about a tenth of a second) and drops back in when it ends. In Dynamic Island › Behaviour you can pick apps it should **keep showing over** (e.g. slides you time with the focus timer) and apps it should **always hide for** whenever they're in front, fullscreen or not (a game in a window, a video editor, a screen recorder). "Add app…" lists the apps open right now; or type a program name as Task Manager shows it.
* Brightness works on monitors that support DDC/CI (most external monitors; not laptop panels yet); otherwise the slider is disabled and says so. A missed reply is retried, so a busy monitor no longer disables the slider.

## Widgets

* Add from **Widgets**. Drag anywhere on the glass to move; drag the **corner grip** to resize. **Hold Alt** while dragging to ignore the grid.
* **Sizes** — new widgets come in four sizes built on one grid: **Small** 160 × 160, **Medium** 336 × 160, **Wide** 512 × 160 and **Large** 336 × 336 (the gaps are 16 px, the grid's step), so widgets side by side line up and share the same margins. Right-click › **Size** switches between the sizes the widget's content fits in. Resizing with the grip still works; the Size menu then shows no tick until you pick one again. Widgets you placed before 2.0 keep their size.
* **Look** — right-click › **Look** › **Glass card** (the usual glass) or **Bare (on the wallpaper)**: no card at all, just the text with a soft shadow. A bare widget picks its own text colour from the wallpaper under it, whatever the theme: light text with a dark shadow on a dark picture, dark text with a light shadow on a bright one. Move it and it checks again. New Text widgets start bare.
* **One design** — every widget shares the same details, so a desktop full of them looks planned: a small header with an accent dot and a label in spaced capitals, big light numbers, sizes and rates in a monospaced font, and segmented meters. The **System monitor** shows CPU, memory and GPU as segmented meters (amber above 90 %); in the Large size it adds network, disk and uptime. Its other looks are **Meters + CPU graph** and **Rings**. The **World clock** marks each city with a filled dot in daylight and a hollow one at night, and shows up to five cities (right-click › Cities). Clocks, the World clock and the Calendar follow a change of Windows' time or time zone at once.
* **Clocks** — the top of the Widgets page shows every clock style, live: click one to put it on your desktop (bare, at its best size). Right-click a clock › **Style** to change it later. The styles:

  | Style | What it looks like |
  |---|---|
  | Minimal | Big time, date underneath (optional seconds) |
  | Orbit | The weekday in **Orbit letters**, far apart, the date in spaced capitals, then the time between dashes (Wide) |
  | Hairline | A centred stack between two fading hairlines: "GOOD / EVENING", the short weekday in bold Orbit letters, the date and the time (Large) |
  | Poster | The day of the month huge in your accent colour, the month in heavy capitals across it, the weekday standing up the side and the time underlined (Large) |
  | Label | A row of ticks for the day's half hours (the ones gone are lit), the weekday in a thin box, and the time and date cut out of a solid bar so the wallpaper shows through |
  | Week | The time in outlined, narrow figures, the week underneath with today ringed, and the date in a serif (Wide) |
  | Heavy | Heavy figures with the date written out underneath ("25, November, Wednesday.") |
  | Story | The month in a gradient of your accent colour, the weekday and date, then the time told as a sentence ("It's 16:35 in the afternoon. 69% of the day has passed.") (Wide) |
  | Stacked | Hours over minutes, with the weekday and date beside them |
  | Ambient date | The day of the month large, the weekday above it, the month and time below |
  | Greeting | "GOOD EVENING", the weekday large and light, then the date and time between thin lines (Wide) |
  | Spaced | The weekday in very wide capitals, the date, then the time between dashes (Wide) |
  | Outline | Solid hours over hollow, outlined minutes, with the weekday and date beside them |
  | Big date | A huge day of the month in your accent colour, with the month and weekday over it |
  | Script | The weekday handwritten over a tall, narrow time, and the date in spaced capitals |
  | Mono | A terminal look in a monospaced font, with a bar for how much of today has gone (optional seconds) |
  | Dot matrix | The time on an LED-style dot grid, every dot drawn with the unlit ones faint, and the date in spaced capitals |

  Clocks update once a minute (once a second only when seconds are shown). **Orbit letters** are WinOrbit's own geometric capitals, drawn as lines rather than taken from a font file; they cover A–Z and the figures. Dates and times are always in English, whatever language Windows uses, so every skin reads the same; only the Week row starts on your region's first day of the week.
* **Matching pieces** — under the clocks, text-only versions of other widgets made to sit beside a clock skin, straight on the wallpaper: **Now playing · Caption** (the song between two hairlines, controls when you point at it), **System · Slashes** (`CPU ////////////  12.5%`), **Weather · Line** (the place and the sky in spaced capitals; while weather is off it says so on one line, and double-clicking it opens the switch), **Recycle bin · Label** (files and size beside the bin; double-click opens it) and **Launcher · Icon row** (your apps drawn as glyphs in the text colour, without names). You can switch any of these widgets back and forth with right-click › **Style**. Also among them:
  * **Now you're listening** — the artist and the song under a spaced "# Now you're listening #" heading, the song in your accent colour (a Now playing style).
  * **Music line** — one thin line that moves with what's playing, mirrored from the middle. It runs only while music plays (20 times a second, from Windows' own level meter for your speakers; nothing is recorded) and lies flat and still otherwise, and with Reduce motion on.
  * **Battery** — the charge in big figures with a small battery outline that fills, and "Charging", "Fully charged" or the time left under it. It changes when Windows reports a change. On a PC without a battery it says so; desktop sets leave it out there. Double-click opens Windows' battery settings.
  * **Drive space** — a diamond for each fixed drive (up to four) that fills as the drive fills, turning amber above 90 %, with the letter, free space and percentage used. Read every five minutes while it's on screen and when you point at it; network and removable drives are left out. Double-click opens This PC.
  * **Quote** — a short quote each day, in turn. Right-click for **Next quote**, **Add my own quote…** (text, then the author), **Only my quotes** and **Remove my quotes…**. Your quotes are saved with the widget on this PC.
* **Desktop sets** (top of the Widgets page) — a whole look in one click: a clock skin with the pieces made for it, a theme, the island's glance and liquid glass on or off. Each card shows the set live on a picture of your wallpaper. **Use** replaces the widgets on your desktop (on your main display) with the set's; **Profiles › Restore previous desktop** brings your own back. Built in: **Orbit**, **Poster**, **Terminal**, **Heavy night** and **Hairline**. The tray menu's **Desktop set** switches without opening WinOrbit.
  * **Save my desktop as a set…** keeps what's showing now (positions are saved as shares of the screen, so the set fits any screen size).
  * **Share** (the arrow on a card) saves a `.winorbitset` file to send to a friend; **Import a set…** reads one. Shared files leave out your own things: pinned apps and their paths, the greeting's name and your own quotes. A custom theme goes with the set. Imported sets are checked first: unknown widgets are dropped, at most 24 widgets are kept and every position is kept on screen.
* **Match the clock's lettering** (**Widgets › Layout**, on by default) — every widget takes its letters from your clock, so the desktop reads as one design: monospaced with **Mono** and **Dot matrix**, narrow (Bahnschrift) with **Poster**, **Label**, **Week**, **Heavy** and **Script**, light and airy with **Orbit**, **Hairline**, **Spaced** and **Story**, and the usual theme font with the others. With several clocks, the first one that is shown decides. Turn it off to keep the theme's font everywhere.
* **Right-click** a widget for its own options (style, cities, timer length, rename, add shortcuts…) plus size, look, opacity, *Always on top*, lock, hide and remove.
* Widgets sit on the desktop beneath your windows. **Win+D** (show desktop) brings them forward with the desktop.
* The Launcher accepts files, apps and folders dropped onto it.
* Notes and tasks save as you type, locally.

## Themes and the Liquid Glass Editor

* Pick a theme in **Appearance**. Edits never change the built-in themes: the first edit creates "*Name* (custom)", saved automatically.
* Themes with their own colour: **Midnight** (indigo), **Neon** (violet liquid glass with a hot-pink glow and a pink-to-cyan sheen), **Sakura** (blush pink by day, plum at night, liquid glass), **AMOLED Black**, **Productivity** (blue), **Gaming** (teal), **Ember** (charcoal with a molten-orange glow) and **Study** (warm amber). Pure Liquid Glass, Liquid Clear, Liquid Frosted and Wallpaper Adaptive take their colour from your wallpaper; Soft Light is white glass for daylight.
* Frosted Glass and Minimal Dark looked too much like Liquid Frosted and AMOLED Black, so they're no longer offered. If you use one, it keeps working and stays in the list until you pick another; for heavier frost, use **Surface › Smoked** or **Solid**.
* **Liquid Clear** and **Liquid Frosted** are liquid glass: the glass has a thick, rounded rim that gathers in and bends the wallpaper beyond its edge, splits the light a little at the rim like a prism, and moves like liquid. It flexes when the island grows or shrinks and wobbles back, light sweeps round the rim when the island opens or closes, and a panel's contents come into focus through it. Liquid Clear is nearly clear; Liquid Frosted keeps the same rim with a softer, easier-to-read middle. Any theme can have it: **Liquid lens** in the Liquid Glass Editor (0 % is the classic thin curved edge). It needs **Curved glass edges** on and a graphics card with pixel shader 3.0 (nearly any from the last 15 years); otherwise you get the curved edge. With **Reduce motion** on, the glass still bends the light but doesn't flex, wobble or sweep.
* **Liquid glass switch** (**Appearance › Glass › Liquid glass**, or **Liquid glass** in the tray menu): not everyone likes the lens, so it's one switch. Off keeps your theme and gives it the thin, calm curved edge instead. Turning it on with a theme that has no lens switches to Liquid Frosted (and turns Curved glass edges on).
* **Liquid Glass Editor** has independent dark and light glass. **Reset** returns to the built-in values; **More** has rename, duplicate, import/export (`.glasstheme` JSON) and delete.
* **Mode**: Auto follows Windows; Scheduled switches at the times you set.
* **Curved glass edges** (**Appearance › Glass**, on by default): the wallpaper bends round the rim of the island, widgets and launcher like thick glass, the rim has a faint sheen, and the edge nearest the pointer catches the light. The island's rim also glints when it wobbles (a bubble splitting off or merging back). It runs on the graphics card and turns itself off when WinOrbit falls back to software rendering. With **Reduce motion** on, the curve and sheen stay but the light no longer follows the pointer or glints. Turn it off for flat glass.
* **Surface** (**Appearance › Glass › Surface**): glass isn't for everyone, so the island and widgets can be made of something else. **Glass** frosts your wallpaper (or, with Live glass, the windows behind). **Gradient** is an opaque, gently coloured fade from your accent colour into your wallpaper's colours. **Smoked** is a deep tint, **Solid** is the theme's plain colour and **Black** is true black with light text and a fine edge. This sets both; **Dynamic Island › Material** and **Widgets › Material** set them apart. Bare widgets stay straight on the wallpaper.
* **Live glass** (**Appearance › Glass**, or **Live glass** in the tray menu; off by default, Windows 11; shown in simple settings too): the island, widgets and launcher show the windows behind them, blurred, instead of only your wallpaper. Drag a window under the island and its colours flow through the glass. WinOrbit uses Windows' screen-capture API, but only for screens where its glass is showing, never while a fullscreen app covers that screen or the screen is off, and only when something changes. Updates come up to 10 times a second, fewer while the change is small. The picture is shrunk to an eighth on the graphics card and blurred, kept in memory and never saved or sent. Nearly clear looks such as Liquid Clear and Liquid Frosted take a sharper quarter-size picture up to 20 times a second instead, because you can see through them. **While it's on, WinOrbit's island, widgets and launcher don't appear in screenshots, screen recordings or screen sharing**, because otherwise the glass would capture itself. **Screenshot with WinOrbit** (tray menu) puts them back into a screenshot. The settings window keeps the wallpaper glass and still shows up in screenshots. It costs more than anything else in WinOrbit when the screen keeps moving: on the test PC, with the island and four glass widgets over a picture that moves everywhere, it used about 7 % of one CPU core more than with it off (about 15 % with the nearly clear looks, which update twice as often at four times the detail) and ~55 MB more memory. When something moves only in another part of the screen, away from the glass, it costs far less (about 1–4 %), because a glass is only redrawn when its own part of the picture changed. Over a still screen it costs next to nothing, because Windows sends no new pictures.

## Wallpaper & gradients

* Colours update the moment Windows reports a new wallpaper (including slideshows).
* **Wallpaper Engine / Lively**: WinOrbit snapshots the wallpaper layer every 3 minutes (in memory, paused in fullscreen apps) so the glass matches what you see. Turn off with *Match live wallpapers*.
* The gradient can follow the wallpaper or be your own (2–6 stops, linear or radial). *Apply as wallpaper* renders it as a real wallpaper (your old one is backed up).

## Profiles and automation

* A profile stores theme + island + widget layout + reduced motion. **Restore previous desktop** undoes the last switch.
* Built in: **Study** (warm and calm, focus timer and calendar), **Gaming** (teal, island hides in fullscreen), **Productivity** (calendar, tasks, media, system), **Lo-fi** (Neon glass, a big clock with a visualiser and now playing under it, new songs pop up on the island), **Minimal** (one clock) and **Night** (Midnight glass, reduced motion, dims a DDC/CI monitor to 35 %). Leaving Night for a profile that doesn't set a brightness puts the screen back to where it was.
* Automation is **off** until you enable it: switch by time of day, while a fullscreen app runs, or when a chosen app is in front (e.g. `code`, `photoshop`). Rules end automatically and restore the previous profile.

## Rainmeter

* The Rainmeter page appears in the sidebar only when Rainmeter is installed or running; otherwise Home shows a Focus card in its place. Change this in **Settings › Rainmeter page** (Automatic / Always show / Hide).

* WinOrbit finds a normal Rainmeter install automatically. A portable copy is found while it is running; otherwise use **Locate Rainmeter.exe** once. The library shows every skin config with preview images where the skin has one (`preview.png`, `@Resources\preview.png`, …), metadata, favourites and recents.
* Warnings: **Third-party plugin** means the skin loads a plugin DLL that didn't come with Rainmeter (Rainmeter's own plugins, such as InputText or WebParser, aren't flagged). **Runs programs** means a click action or RunCommand can start a program or script; hover the chip to see which. Links and folders aren't flagged.
* The switch on each card activates/deactivates through Rainmeter. Use the monitor button for position, layering and transparency. Position and layering are saved by Rainmeter; transparency set here lasts until the skin is refreshed or Rainmeter restarts (set it in Rainmeter's Manage window to keep it).
* **Import .rmskin** shows what the package contains (skins, layouts, plugin DLLs) and then hands it to Rainmeter's own Skin Installer.
* If Rainmeter isn't installed, the page links to the official site; WinOrbit never installs it.

## Windows Tweaks

* Every setting here is per-user and reversible. The first change of each setting records its original value in `%APPDATA%\WinOrbit\backups\windows-settings-backup.json`.
* **Revert all** restores everything (including accent colour and wallpaper). The uninstaller offers the same.

---

## Configuration files

All in `%APPDATA%\WinOrbit` (portable: `Data\` next to the exe). They are plain JSON and safe to back up. Use **Settings › Your data** to export/restore a `.zip` backup — handy for moving WinOrbit to another PC. A backup holds your settings, notes, tasks, events, themes, profiles and desktop sets (`sets\`); it leaves out the logs and the `backups` folder (this PC's original Windows values for **Revert all**), so restoring an old backup never changes what Revert all puts back. If a file is damaged, WinOrbit keeps a `.corrupt` copy and starts with defaults rather than failing. The installed version also keeps `startup.jitprofile`, which only lists what .NET compiled while WinOrbit started, so it can compile that on a spare core next time and start faster; it is rewritten at each start, left out of backups and safe to delete.

## Troubleshooting

| Problem | Fix |
|---|---|
| Windows SmartScreen says "Windows protected your PC" | The installer is not code-signed (no certificate). Click **More info → Run anyway** if you built/obtained it yourself. |
| Setup says the .NET 10 Desktop Runtime is missing | WinOrbit 1.4 and later need .NET 10 (1.3 used .NET 8, so an update can ask for it even though .NET 8 is installed). Install ".NET Desktop Runtime 10 (x64)" from dotnet.microsoft.com, or use the portable zip which includes it. |
| A shortcut does nothing | Another app owns that key combination. Pick a new one in **Settings › Keyboard shortcuts** (the page lists conflicts). |
| Widgets disappeared | Press **Ctrl+Alt+W**, or check **Widgets › Show widgets**. Widgets stay hidden while a fullscreen app has focus. |
| The island is gone | Tray menu › **Dynamic Island**, or **Dynamic Island › Show**. It hides during fullscreen apps. |
| With Live glass on, the glass shows my wallpaper instead of the window behind it | Live glass shows the windows behind only while it is running. If Windows refuses a capture or the graphics driver resets, it switches back to the wallpaper glass for a few seconds and starts again by itself (after four failures in ten minutes it stays off, and Appearance › Glass › Live glass says why). Gradient, Smoked, Solid and Black surfaces never show what's behind: only Glass does. |
| The island and widgets are missing from my screenshots, recordings or screen share | That's **Live glass** (Appearance › Glass): while it's on, WinOrbit's glass windows ask Windows to leave them out of every capture, otherwise the glass would capture itself. Use **Screenshot with WinOrbit** in the tray menu instead: it takes a normal screenshot and draws WinOrbit's windows back in, exactly where they are, live glass and all (saved to your Screenshots folder and copied). For recordings and screen sharing, turn Live glass off first. The settings window always shows up. |
| Glass colours don't match the wallpaper | **Wallpaper & Gradients › Re-scan**. With Wallpaper Engine, make sure *Match live wallpapers* is on. |
| No GPU usage | WinOrbit reads NVIDIA GPUs through the driver's NVML and others through Windows GPU counters. Some drivers expose neither. |
| Brightness slider disabled | The monitor doesn't support DDC/CI (or it is disabled in the monitor's OSD menu). |
| A Rainmeter skin didn't load | WinOrbit reports it. Open the skin in Rainmeter's **Manage** (button on the card) and check Rainmeter's log; missing plugins are the usual cause. |
| The settings window is blank | A graphics-driver state (e.g. after a display mode change) can stop GPU-rendered windows from appearing. WinOrbit detects this within a second and switches that window to software rendering automatically (About & Diagnostics › Rendering says so). It doesn't check while the screen is off, the monitor is switched off or unplugged, or the PC is locked, since every window looks blank then; it checks once the screen is back. (Before 2.0 some graphics drivers made WinOrbit think no monitor was connected at all, so the check never ran and the window stayed blank; it now asks Windows' display configuration as well.) To make it permanent: **Settings › Software rendering for this window**. Restarting the PC or updating the GPU driver normally restores hardware rendering; on the test PC, closing Wallpaper Engine was also followed by hardware rendering working again. |
| Anything else | **About & Diagnostics** shows the log and resource use. **Copy for a bug report** puts your WinOrbit and Windows versions, displays, rendering mode and island setup on the clipboard — no names of networks, devices, files or places, and no log lines — ready to paste into a report. To report a bug or share an idea, see **Reporting bugs and ideas** below. |

## Reporting bugs and ideas

You don't need a GitHub account. **About & Diagnostics › Report a bug or share an idea** (or **Report a bug or idea…** in the tray menu) offers three ways:

* **Email**: opens your email app with a new message to mahadisiraj.06@gmail.com, the subject filled in and the setup summary (the same text as **Copy for a bug report**) already in the body. Add what happened and send it yourself; WinOrbit sends nothing. If no email app is set up, the address is copied for you to paste into webmail.
* **Instagram**: opens a direct message to @mssatalite (in the Instagram app or your browser).
* **GitHub**: opens the bug report and idea forms, if you have an account.

## Updating

WinOrbit only checks for updates if you say so. Home asks once (**Get told when there's an update?**), the first start offers **Tell me about updates**, and the same switch is in **Settings › Privacy & network**. With it on:

* When WinOrbit starts, and every 48 hours after, it asks GitHub for the latest version number: one small request for the public `VERSION` file, shown in the request ledger. It waits for the 48 hours to pass, so starting WinOrbit often doesn't make it ask more often, and it doesn't ask while a fullscreen app or game is in front.
* When there's a new version, a card on the island says so; clicking it opens Home, where **Download** gets the installer straight from the release (the portable zip for a portable copy), **What's new** opens the release notes, and **Skip this version** stops the card until the next one.
* Nothing is downloaded or installed by WinOrbit itself: your browser downloads the file and you run it. Your settings are kept.

Turning it on also turns on **Allow optional internet features**, which the check needs. **Check now** (same page) asks once, right away, whether or not the automatic check is on.

## Uninstall and rollback

1. *(Optional)* **Windows Tweaks › Revert all** to restore Windows appearance settings now.
2. **Settings › Apps › Installed apps › WinOrbit › Uninstall** (no admin prompt). The uninstaller:
   * closes WinOrbit,
   * asks whether to restore Windows settings WinOrbit changed,
   * removes the program files, shortcuts and start-up entry,
   * asks whether to delete your settings/themes/notes (default **No**).
3. Rainmeter, its skins and its settings are never touched.

Portable version: quit from the tray and delete the folder.

## Known limitations

* **Not every part of Windows can be restyled.** Start, the taskbar, context menus and other apps' windows can only take the accent colour, dark/light mode and transparency that Windows exposes. Deeper restyling needs third-party tools that inject into Explorer (listed, not installed) — WinOrbit deliberately doesn't.
* **Glass over other windows.** Island and widget glass frosts the *wallpaper*, not windows that happen to be behind them. (Windows offers no supported live blur for layered windows.) The main window uses real DWM Acrylic, which does blur what's behind it.
* **Live wallpapers** are matched with a snapshot every 3 minutes, not continuously; desktop icons can appear as faint blurs in that snapshot.
* **Media artwork** and controls depend on the playing app supporting the Windows media session (Spotify, Chromium-based browsers, Firefox and the Windows Media Player app do; some older desktop players don't, in which case the play/next buttons fall back to the standard media keys).
* **CPU temperature** is not shown: Windows has no reliable admin-free source. GPU temperature works on NVIDIA GPUs.
* **Download indicator** recognises browsers' temporary download files in the Downloads folder only; it can't show percentage progress.
* **Wi-Fi / Bluetooth** buttons open the Settings pages rather than toggling the radios (toggling needs APIs that require package identity or admin).
* **Rainmeter** skins can't be rendered inside WinOrbit; they keep running in Rainmeter. WinOrbit and Rainmeter widgets are managed side by side, not merged into one engine.
* The installer is **unsigned**.
