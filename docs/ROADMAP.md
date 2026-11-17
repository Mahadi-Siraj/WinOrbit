# Glasswave roadmap — what people want, and what's next

17 November 2026. Based on: Reddit threads about Windows Dynamic Island apps (NabyScape, Notchify, Dyno, Dynamic Edge, BuildNotch and the Windhawk island mod), Windows Central, MakeUseOf and Windows Forum coverage of those apps, and 2026 "best Windows customisation tools" round-ups.

## The goal

**One free, private, lightweight app instead of six.** People currently stack several separate tools, and the round-ups themselves warn that stacking hurts performance and stability:

| What people install today | What it does | In Glasswave |
|---|---|---|
| A Dynamic Island app (Notchify, NabyScape, Dynamic Edge …) | Media, timers, live activities | ✅ Island |
| Rainmeter / widgets | Clock, system, media on the desktop | ✅ Widgets (+ Rainmeter manager) |
| FluentFlyout / ModernFlyouts | Nicer volume and brightness pop-ups | ✅ Volume meter in the island (brightness: next) |
| Ditto / Win+V | Clipboard history | ✅ Clipboard tab (opt-in, private) |
| Dropover-style shelves | Park files while you work | ✅ File shelf |
| TranslucentTB | Transparent or blurred taskbar | ⏳ Next (experimental) |
| Flow Launcher / PowerToys Run | Search apps, calculate, open settings | ⏳ Next |
| f.lux / night-light toggles, Awake | Quick system switches | ✅ Quick tiles |
| Wallpaper Engine colour matching | Glass that fits the wallpaper | ✅ |

## What users said they want (and where Glasswave stands)

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

Wave 2 (high impact, fits light and private):
1. **Spotlight-style launcher** — type to open apps, settings, files; calculator; unit conversion. Replaces Flow Launcher / PowerToys Run for most people.
2. **Taskbar styling** (TranslucentTB-style clear, blur or acrylic) — experimental tier: uses an undocumented but injection-free Windows call that must be re-applied when Explorer resets it.
3. **Brightness HUD and battery/charging activity** — on laptops: plug-in animation, low-battery warning, brightness meter.
4. **First-run setup** — choose Minimal / Productivity / Music / Gamer, so the first minute looks great (NabyScape added this and users liked it).
5. **Settings search** ("notch width" etc.).
6. **Island on other edges, and cursor-follow across monitors.**

Wave 3 (reach and trust):
7. **ARM64 build** (requested in threads). Cross-compiling is easy; it can't be tested without ARM hardware.
8. **Code signing.** Unsigned self-contained .NET installers get flagged by Windows Defender (a top complaint in the NabyScape thread). This needs a certificate (cost) or the Microsoft Store.
9. **Languages** — Spanish and others were asked for.
10. **Notifications in the island** — reading Windows notifications needs the app to be packaged (MSIX) and the user's permission, so it comes with a Store or MSIX release.

Deliberately not planned:
* **"Eyes that follow the cursor" / desktop pets** — in the Dyno thread, most replies said it's the first thing they'd turn off.
* **A built-in AI chatbot** — needs API keys or large local models. That's against "light", and it's a different product.
* **A built-in Rainmeter engine** — see STATUS-REPORT §4.
