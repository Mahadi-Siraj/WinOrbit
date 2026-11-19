# WinOrbit roadmap — what people want, and what's next

17 November 2026, updated 19 November 2026. Based on: Reddit threads about Windows Dynamic Island apps (NabyScape, Notchify, Dyno, Dynamic Edge, BuildNotch and the Windhawk island mod), Windows Central, MakeUseOf and Windows Forum coverage of those apps, and 2026 "best Windows customisation tools" round-ups.

## The goal

**One free, private, lightweight app instead of six.** People currently stack several separate tools, and the round-ups themselves warn that stacking hurts performance and stability:

| What people install today | What it does | In WinOrbit |
|---|---|---|
| A Dynamic Island app (Notchify, NabyScape, Dynamic Edge …) | Media, timers, live activities | ✅ Island |
| Rainmeter / widgets | Clock, system, media on the desktop | ✅ Widgets (+ Rainmeter manager) |
| FluentFlyout / ModernFlyouts | Nicer volume and brightness pop-ups | ✅ Volume and brightness meter in the island |
| Ditto / Win+V | Clipboard history | ✅ Clipboard tab (opt-in, private) |
| EarTrumpet / SoundSwitch | Volume per app, switch speakers ↔ headphones | ✅ Mixer and output devices in the Control Centre |
| Dropover-style shelves | Park files while you work | ✅ File shelf |
| TranslucentTB | Transparent or blurred taskbar | ❌ Needs code injected into Explorer — refused (see Wave 2) |
| Flow Launcher / PowerToys Run | Search apps, calculate, open settings | ✅ Launcher (Alt+Space) |
| f.lux / night-light toggles, Awake | Quick system switches | ✅ Quick tiles |
| Wallpaper Engine colour matching | Glass that fits the wallpaper | ✅ |

## What users said they want (and where WinOrbit stands)

**Wanted, and now built**
* Media in the island with album art → ✅. **Live synced lyrics** (the most-praised NabyScape feature, and asked for YouTube too) → ✅ opt-in via LRCLIB; works with any player that tells Windows the title and artist, including YouTube in a browser when the page publishes them.
* **Clipboard history and file shelf** (Notchify, NabyScape, Dynamic Edge all added them) → ✅.
* **Mic/camera privacy indicators** (Notchify) → ✅ orange/green dot with the app's name.
* **Volume HUD** (Dyno, ModernFlyouts) → ✅.
* Focus/Pomodoro timer, calendar, notes → ✅ (already there).
* System stats, CPU/GPU/temperature → ✅ (already there).
* **Hover delay** so the island doesn't open by accident (Dynamic Edge) → ✅.
* **Per-feature toggles** ("a toggle for each effect is more useful than more features") → ✅ every live activity has its own switch.
* **Low RAM/CPU** — the #1 complaint in threads (Notchify had a CPU problem; NabyScape users ask "how much RAM?") → ✅ measured: 0.05 % CPU and ~69 MB at idle, no polling.
* **Free, no paywall** (Dynamic Edge locks launcher slots behind $4.99) → ✅ everything free.
* **Privacy** → ✅ zero network by default, no telemetry, every request logged.

**Wanted, not built yet — next waves**

Wave 2 — **done** (17 Nov 2026):
1. ✅ **Spotlight-style launcher** (Alt+Space): apps with icons, settings, actions, profiles, recent files, calculator, unit conversion, web search.
2. ❌ **Taskbar styling — not possible within the rules.** Tested on this PC: Windows 11 ignores the documented accent call on its taskbar (pixels identical before/after). TranslucentTB-style results need code injected into Explorer, which WinOrbit refuses to do. Windows' own Transparency effects and taskbar accent colour remain on the Tweaks page.
3. ✅ **Charging, low battery, Energy Saver and brightness activities** (laptop hardware still to be tested).
4. ✅ **First-run welcome** with starting styles.
5. ✅ **Settings search**, plus the same settings in the launcher.
6. ✅ Also: **VLC and other classic players**, **seek bar / ±10 s**, **Control Centre** with 12 tiles (microphone, energy saver, airplane, DND added).
7. ⏳ **Island on other edges and cursor-follow** — moved to Wave 3.

Wave 3 (reach and trust):
7. **ARM64 build** (requested in threads). Cross-compiling is easy; it can't be tested without ARM hardware.
8. **Code signing.** Unsigned self-contained .NET installers get flagged by Windows Defender (a top complaint in the NabyScape thread). This needs a certificate (cost) or the Microsoft Store.
9. **Languages** — Spanish and others were asked for.
10. **Notifications in the island** — reading Windows notifications needs the app to be packaged (MSIX) and the user's permission, so it comes with a Store or MSIX release. The same applies to Clock-app alarms/timers (they only surface as notifications).
11. **Do not disturb on/off from the island** — Windows 11 keeps DND state in an undocumented kernel store with no public API; revisit if Microsoft documents one.

Wave 4 — **done in 1.2** (18 Nov 2026): liquid spring motion, every screen edge, shapes, album colours, calendar events with reminders, weather tab, connection and radio cards.

Wave 5 — **built after 1.2, not released yet** (18–19 Nov 2026; committed, waiting for the owner's go-ahead to publish as 1.3):
* ✅ **Volume by app** and a one-click **output device** switch in the Control Centre.
* ✅ **Screenshot cards** and **finished-download cards** (click to open, drag into a chat, show in folder; programs are never started from a card).
* ✅ **Rain soon** card (opt-in, uses the weather the user already turned on).
* ✅ **Calendar import** from `.ics` files (Outlook, Google, Apple exports; repeating events) — no accounts.
* ✅ **Keyboard language** card for people with more than one keyboard.
* ✅ **Bluetooth battery** on the "Connected" card, when the device reports it to Windows (🧪 no Bluetooth headset on the test PC; checked with the preview).
* ✅ **Smarter lyrics matching**: "Artist - Song (Official Video)" titles from video sites are cleaned and split.
* ✅ **Check for updates** button — only when clicked, through the network gate.
* ✅ **Timers from the launcher** — `timer 5 min`, `focus 50`, `stopwatch`; a "Timer done" card when a countdown ends.
* ✅ **Reminders from the launcher** — `remind me in 20 min to stretch`, `remind me at 6pm to call mom`; one card on that minute, kept in the island's calendar.
* ✅ **World time in the launcher** — `time in Tokyo`, `dhaka time`; offline, from Windows' time-zone data.
* ✅ **Clipboard search in the launcher** — `clip`, `clip invoice` (🧪 unit-tested; not yet checked on screen).
* ✅ **Microphone pill** — during a call the capsule shows the app, your voice level and Windows' own mute (🧪 checked with the preview; no real call yet).
* ✅ **Swipe to skip** — two-finger sideways swipe or a tilted wheel on the island (🧪 the gesture reaches the skip code; not yet tried with music playing).
* ✅ **Side bubble** — short alerts split off beside a busy capsule and flow back in.
* ✅ **Curved glass edges** — a GPU shader bends the wallpaper round the rim of every glass surface; off under software rendering.
* ✅ **Cards from your scripts and apps** — opt-in local pipe, current user only, plain text naming the sender, rate-limited.
* ✅ Copy a setup summary for bug reports; backups leave out Windows-tweak originals; accessibility fixes; island code split into smaller files; render check no longer misfires while the monitor is off.
* 📦 winget manifest prepared in `packaging/winget/` (not submitted).

Still open:
1. **Island follows the cursor between monitors** — each display already gets its own island; a "follow the pointer" mode needs a multi-monitor PC to test.
2. **Automatic update check** (opt-in, daily) — deliberately not added: the app promises it never checks by itself. The owner decides.
3. **README demo GIF and 1.2/1.3 screenshots.**
4. **Microsoft Store (MSIX)** — fixes the SmartScreen warning and unlocks notifications in the island; needs a developer account.
5. Items from Wave 3 above (ARM64, code signing, languages). For signing, the SignPath Foundation offers free certificates to open-source projects; applying is the owner's call. Until then builds stay unsigned.
6. **Live window glass** — glass that bends the actual windows behind it rather than the wallpaper (Windows.Graphics.Capture). It would capture the screen, so it would have to be opt-in; waiting for the owner's decision.
7. **Display scaling 125 % / 150 %** — the code is per-monitor-DPI aware, but changing the test PC's scaling needs the owner's OK.

Deliberately not planned:
* **"Eyes that follow the cursor" / desktop pets** — in the Dyno thread, most replies said it's the first thing they'd turn off.
* **A built-in AI chatbot** — needs API keys or large local models. That's against "light", and it's a different product.
* **A built-in Rainmeter engine** — Rainmeter's skin format is large and its plugins are native code; WinOrbit manages a real Rainmeter install instead (read-only file access, documented bangs only).
