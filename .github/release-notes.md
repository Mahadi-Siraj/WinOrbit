## WinOrbit — the Dynamic Island and desktop suite Windows deserves

Free, open source (GPL-3.0), about 45–80 MB of RAM and ~0.1 % CPU at idle, zero telemetry.

### Download
| File | For |
|---|---|
| `WinOrbit-Setup-*.exe` | **Recommended.** Per-user install, no admin prompt. Needs the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) — setup checks and links it. |
| `WinOrbit-Portable-*-win-x64.zip` | No install and no runtime needed. Unzip anywhere, run `WinOrbit.exe`. |
| `SHA256SUMS.txt` | Checksums for both. |

Windows 10 (2004+) and Windows 11, 64-bit. **Not code-signed** — if SmartScreen warns, choose *More info → Run anyway*. [Code signing policy](https://github.com/Mahadi-Siraj/WinOrbit/blob/main/CODE_SIGNING_POLICY.md)

### New in 2.0
- 💧 **Liquid glass that moves.** Two new themes, **Liquid Clear** and **Liquid Frosted**, give the glass a thick, rounded rim that bends the wallpaper beyond its edge, with a hint of prism and a lit rim. The glass flexes when the island grows or shrinks and wobbles back, light sweeps round the rim when it opens or closes, and the panel comes into focus through it. Any theme can have it with the new **Liquid lens** slider in the Liquid Glass Editor. It runs on the graphics card (pixel shader 3.0); without one, or with Curved glass edges off, you get the classic curved edge. Reduce motion keeps the lens and drops the movement
- 🔍 **Sharper Live glass with the clear looks**: a quarter-size picture of the windows behind, up to 20 times a second, instead of an eighth at 10
- 🧩 **Widgets on one grid.** Four sizes built from one unit, **Small** 160 × 160, **Medium** 336 × 160, **Wide** 512 × 160 and **Large** 336 × 336, with the same 16 px margins inside every widget, so widgets side by side line up. Right-click › **Size** to switch. Widgets you placed before keep their size
- 🪟 **Bare widgets**: right-click › **Look** › **Bare** drops the glass card, leaving the text on your wallpaper with a soft shadow that reads on light and dark wallpapers
- 🕰️ **Seventeen clock styles**, with a live **Clocks** gallery at the top of the Widgets page: click one to put it on your desktop. New desktop skins: **Orbit** (the weekday in WinOrbit's own geometric **Orbit letters**, far apart), **Hairline** (a greeting stack between two fading lines), **Poster** (a huge accent numeral with the month across it), **Label** (a boxed weekday over ticks for the day's half hours, the time cut out of a solid bar), **Week** (outlined time, the week with today ringed), **Heavy** and **Story** (the time told as a sentence). Plus Minimal, Stacked, Ambient date, Greeting, Spaced, Outline, Big date, Script, Mono and an LED-style **Dot matrix**
- 🧷 **Matching pieces** for the skins: text-only Now playing (**Caption**), System (**Slashes**, a terminal readout), Weather (**Line**), Recycle bin (**Label**) and Launcher (**Icon row**, your apps drawn as glyphs in the text colour, without names), all straight on the wallpaper
- 🔤 **Widgets match the clock's lettering**: pick a Mono or Dot matrix clock and every widget turns monospaced, a Poster or Label clock makes them narrow, Orbit or Hairline makes them light and airy. Widgets › Layout › Match the clock's lettering (on by default)
- 🎛️ **One design for every widget**: a small header with an accent dot and spaced capitals, big light numbers, data in a monospaced font and segmented meters. The System monitor is now a HUD of segmented meters (its rings no longer spill out of their circles), the World clock marks day and night, and the Recycle bin, Notes, Tasks, Now playing, Launcher and Weather widgets follow the same look
- 🌗 **Bare widgets stay readable**: their text colour comes from the wallpaper under them, so a light theme over a dark wallpaper no longer puts dark text on a dark picture
- 🏝️ **Choose your island's glance.** Some of you would rather keep the time on the desktop, so the resting capsule can now show the **Clock** (as before, still the default), **System** (CPU and memory), **Status** (Wi-Fi or Ethernet, Bluetooth, battery), **Eyes** that follow your pointer, blink and doze off, or nothing (**Quiet**). Dynamic Island › Collapsed capsule › When nothing is happening
- 🔒 **Still private and light.** No new network access. The Status glance listens to Windows' own change events and the Eyes read only the pointer's position, both only while they're on screen. The Eyes cost about 1 % of one CPU core while visible on our test PC; the other glances cost nothing extra
- 🐛 **Fixed: a blank settings window that never recovered.** With some graphics drivers Windows lists no monitor under the graphics card, so WinOrbit thought the screen was off: the check that rescues a blank window never ran, and Live glass showed the wallpaper instead of the windows behind. It now asks Windows' display configuration too
- 🗂️ **Your settings carry over**: old widgets keep their size and glass, and the island keeps the clock

Untested here: multiple monitors, scaling above 100 %, Windows 10, an Arm64 PC, the Status battery on a laptop (the test PC is a desktop), the liquid lens on graphics cards other than an NVIDIA GTX 1660 SUPER, and whether a DisplayPort monitor that is switched off still counts as "no screen" with the new display check.

### New in 1.4
- 🧱 **Now on .NET 10 (LTS).** .NET 8, which WinOrbit 1.3 used, stopped getting security fixes from Microsoft on 10 November 2026; .NET 10 gets them until November 2028
- ⬇️ **The installer needs the .NET 10 Desktop Runtime (x64)** instead of .NET 8. Setup checks for it and links Microsoft's download page, and now also looks where Arm64 PCs keep the x64 runtime
- ⚡ **The portable version starts faster and uses half the memory:** 0.78 s instead of 1.30 s to start, and 166 MB instead of 332 MB committed, on our test PC. The zip is a little bigger (89 MB instead of 75 MB)
- ⚡ **The installed version starts faster too:** 0.63 s instead of 0.80 s for 1.3.2 on our test PC. .NET now compiles what WinOrbit needs while starting on a spare core, from a list of method names it keeps in your WinOrbit folder (`startup.jitprofile`). The first start after installing or updating writes that list, so that one start takes as long as before (about 0.9 s)
- 🗂️ **Your settings carry over unchanged** — notes, tasks, events, widgets and themes too. A new test loads files written by 1.3.2 itself, so a later change can't silently reset them

The installed version uses about the same memory as 1.3.2. Untested here: an Arm64 PC, Windows 10.

### New in 1.3.2
- 🎵 **Now playing shows the right song sooner.** When a player started (or another one took over) while WinOrbit was still loading the title and cover, a progress or play/pause update could cancel that load, leaving the previous app's title or no cover. Fixed, with tests
- 🎬 **A 39-second demo video** is attached to this release (`WinOrbit-demo.mp4`)
- 🖼️ **More to see in the README:** cards in motion, the island glowing with each song's colours, weather and calendar tabs, and widgets on a real desktop

### New in 1.3.1
- 🪄 Island cards show longer second lines in full (they were cut off at about 40 characters, e.g. a coding agent's permission request)
- 🩺 About & Diagnostics no longer calls software rendering a driver problem when you switched it on yourself in Settings
- 📖 README lists every 1.3 feature; the user guide explains the local pipe's reply

### New in 1.3
- 🤖 **Coding agents** (opt-in) — an Agents tab that shows what Claude Code, Codex or your own tools are doing: working, needs you, done. The island keeps an orange rim while a session waits for your OK and flashes green when one finishes. Setup is copy-and-paste from Settings; WinOrbit never edits those tools' settings, and only a short title, the folder name and a few words of status are kept (in memory)
- 🎚️ **Volume by app** and an **output device switch** (speakers, headphones, HDMI) in the Control Centre
- 📸 **Screenshot cards** — a thumbnail after Win+PrtScn: click to open, drag it into a chat, or show it in its folder
- ⬇️ **Finished-download cards** — click to open (programs are only shown in their folder, never started)
- 🫧 **Side bubble** — short alerts (Caps Lock, keyboard language, "copied") split off beside a busy capsule instead of replacing your music or call
- 🎙️ **Microphone pill** during calls — the app, a live level and Windows' own mute
- ⌨️ **Launcher** (Alt+Space) — `timer 5 min`, `focus 50`, `stopwatch`, `remind me in 20 min to stretch`, `time in Tokyo` (offline), and `clip` to search clipboard history
- 📅 **Calendar import** from `.ics` files, repeating events included (stays on this PC)
- 🌧️ **Rain soon** (opt-in), **keyboard language** card, **Bluetooth battery** on the "Connected" card
- 🧩 **Cards from your scripts** (opt-in) — a local, current-user-only pipe your own scripts can use to show "Build finished"
- 🪟 **Live glass** (opt-in, Windows 11) — glass shows the windows behind it, not just the wallpaper; **curved glass edges**; a new **Black** material
- 🌊 **Smoother motion** — calmer shared springs: the island opens and closes with one small overshoot and no slow creep, drawing about 40 % fewer frames per motion (less CPU)
- 🔒 **Check for updates** only when you click it, through the network switch; **copy a setup summary** for bug reports (no names or paths)
- 🐛 Fixes — a script that never read its reply could block the local pipe; turning Live glass off no longer risks waiting on a capture in progress; the settings window no longer falls back to software drawing for the whole session after the monitor was off; lyrics for "(Official Video)" titles

Untested here: multiple monitors, scaling above 100 %, Windows 10, Codex, a real call with the microphone pill.

### Highlights
- 🏝️ **Dynamic Island** — now playing with album art, seek bar and ±10 s, music-reactive equaliser, synced lyrics (opt-in), VLC/foobar2000/Winamp/PotPlayer/MPC/mpv support
- ⚡ **Live activities** — volume & brightness meter, charging animation, low battery, Energy Saver, focus timer, downloads
- 🛡️ **Privacy dots** — microphone 🟠, camera 🟢, location 🔵, with the app's name
- 🎛️ **Control Centre** — 12 liquid-glass tiles (Wi-Fi, Bluetooth, Night light, Dark mode, Power mode, Microphone, Keep awake, Focus, Widgets…)
- 🔎 **Launcher** (Alt+Space) — apps, settings, actions, recent files, calculator, unit conversion
- 📋 **Clipboard history** (opt-in, memory-only) and a **file shelf**
- 🧊 Wallpaper-aware **liquid-glass** widgets and themes, Wallpaper Engine matching, profiles, Windows tweaks with one-click revert

Upgrading from a build named *Glasswave* keeps your settings.

Made by **mssatalite** — [Instagram @mssatalite](https://www.instagram.com/mssatalite/) · [GitHub: Mahadi Siraj](https://github.com/Mahadi-Siraj)
