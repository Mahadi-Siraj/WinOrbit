using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.Wallpaper;
using WinOrbit.Widgets;
using WinOrbit.WindowsIntegration;

namespace WinOrbit.Shell.Pages;

// =================================================================================================
public sealed class WallpaperPage : PageBase
{
    public override string Title => "Wallpaper & Gradients";
    public override string Subtitle => "WinOrbit reads your wallpaper locally, extracts its colours and blends every surface into it.";

    private Border? _gradientPreview;
    private StackPanel? _stops;

    protected override void Build()
    {
        var top = Ui.Grid("3*,16,2*", "auto");
        top.Margin = new Thickness(0, 0, 0, 16);
        var img = new Image { Stretch = Stretch.UniformToFill, Source = Hub.Wallpaper.Preview(Monitors.Primary.Device) };
        var frame = new Border { Height = 250, CornerRadius = new CornerRadius(18), Child = img, BorderThickness = new Thickness(1), ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
        frame.SizeChanged += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 18, 18);
        top.Children.Add(frame);

        var info = new StackPanel();
        var path = Hub.Wallpaper.CurrentPath;
        info.Children.Add(Ui.Text("CURRENT WALLPAPER", "Text.Label"));
        var liveName = Hub.Wallpaper.UsingLiveCapture ? $"{Hub.Wallpaper.LiveApp} (live)" : null;
        info.Children.Add(Ui.Text(liveName ?? (string.IsNullOrEmpty(path) ? "Solid colour" : Path.GetFileName(path)), "Text.Sub", 15).With(t => { t.Margin = new Thickness(0, 4, 0, 2); t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
        info.Children.Add(Ui.Text((Hub.Wallpaper.UsingLiveCapture ? $"Snapshot {Hub.Wallpaper.LastCapture:HH:mm} · " : $"Fit: {Hub.Wallpaper.Fit} · ") + $"{(Hub.Wallpaper.Palette.AverageLuminance > 0.5 ? "bright" : "dark")} image" + (Monitors.All().Count > 1 ? $" · {Monitors.All().Count} displays" : ""), "Text.Caption", 12));
        info.Children.Add(Ui.Text("EXTRACTED PALETTE", "Text.Label").With(t => t.Margin = new Thickness(0, 18, 0, 8)));
        info.Children.Add(HomePage.PaletteStrip(30, c => Hub.Theme.Edit(x => { x.AccentFromWallpaper = false; x.AccentDark = ColorMath.Hex(ColorMath.ForMode(c, true)); x.AccentLight = ColorMath.Hex(ColorMath.ForMode(c, false)); })));
        info.Children.Add(Ui.Text("Click a colour to use it as your accent.", "Text.Caption", 11.5));
        info.Children.Add(Kit.Buttons(
            Kit.Btn("Change wallpaper", () => Island.IslandWindow.OpenUri("ms-settings:personalization-background"), Glyph.Picture),
            Kit.Btn("Re-scan", async () => { await Hub.Wallpaper.RefreshAsync(true); Rebuild(); }, Glyph.Refresh)).With(b => b.Margin = new Thickness(0, 16, 0, 0)));
        top.Children.Add(Kit.Card(info, new Thickness(20, 16, 20, 18)).At(0, 2));
        Children.Add(top);

        Children.Add(Kit.Section("Wallpaper awareness", null,
            Kit.ToggleRow("Extract colours from my wallpaper", "When off, WinOrbit uses a neutral built-in palette and never reads the wallpaper's pixels.", Hub.Settings.WallpaperAutoExtract, v =>
            {
                Hub.Settings.WallpaperAutoExtract = v; Hub.Store.Save(); Hub.Theme.Apply();
            }, false),
            Kit.ToggleRow("Match live wallpapers", Hub.Wallpaper.LiveApp is { } app
                    ? $"{app} is drawing your desktop. WinOrbit takes an in-memory snapshot of just the wallpaper layer every few minutes (paused during fullscreen apps) so the glass matches what you see. Nothing is saved to disk."
                    : "For Wallpaper Engine and Lively Wallpaper. Takes an in-memory snapshot of just the wallpaper layer every few minutes; never captures other windows.",
                Hub.Settings.LiveWallpaperCapture, async v => { Hub.Settings.LiveWallpaperCapture = v; Hub.Store.Save(); await Hub.Wallpaper.RefreshAsync(true); Rebuild(); }),
            Kit.Row("Updates", "Changes are detected the moment Windows reports a new wallpaper or slideshow image — no polling.", Kit.Chip("Automatic", "T.Good", Glyph.Check))));

        // ---------------- gradient editor ----------------
        var g = Hub.Theme.Current.Gradient;
        _gradientPreview = new Border { Height = 120, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 4, 0, 14), BorderThickness = new Thickness(1) };
        _gradientPreview.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
        _stops = new StackPanel();
        var custom = new StackPanel { Visibility = g.FromWallpaper ? Visibility.Collapsed : Visibility.Visible };
        custom.Children.Add(Kit.Row("Colour stops", "Two to six stops; offset is the position along the gradient.", Kit.Btn("Add stop", AddStop, Glyph.Add)));
        custom.Children.Add(_stops);
        var editor = new StackPanel();
        editor.Children.Add(_gradientPreview);
        editor.Children.Add(Kit.ToggleRow("Generate from wallpaper", "Keep the gradient in step with the wallpaper automatically.", g.FromWallpaper, v =>
        {
            Hub.Theme.Edit(t =>
            {
                t.Gradient.FromWallpaper = v;
                if (!v && t.Gradient.Stops.Count < 2) t.Gradient.Stops = Hub.Wallpaper.Palette.ToGradientStops();
            });
            custom.Visibility = v ? Visibility.Collapsed : Visibility.Visible;
            RenderStops();
        }, false));
        editor.Children.Add(Kit.Row("Type", null, Kit.Segmented(new[] { "Linear", "Radial" }, g.Kind == GradientKind.Linear ? 0 : 1, i => Edit(t => t.Gradient.Kind = i == 0 ? GradientKind.Linear : GradientKind.Radial))));
        editor.Children.Add(Kit.SliderRow("Angle", "Direction of a linear gradient.", 0, 360, g.Angle, v => Edit(t => t.Gradient.Angle = Math.Round(v)), v => $"{v:0}°"));
        editor.Children.Add(Kit.SliderRow("Strength", "How strongly the gradient washes through glass surfaces.", 0, 1, g.Opacity, v => Edit(t => t.Gradient.Opacity = v), v => $"{v * 100:0}%"));
        editor.Children.Add(Kit.SliderRow("Saturation", null, 0, 2, g.Saturation, v => Edit(t => t.Gradient.Saturation = v), v => $"{v * 100:0}%"));
        editor.Children.Add(custom);
        Children.Add(Kit.Section("Gradient", "The gradient tints the island, widgets and this window so they feel cut from the same material as your desktop.", editor));

        // presets
        var presets = Kit.Gallery();
        foreach (var (name, stops) in GradientPresets)
        {
            var def = new GradientDef { FromWallpaper = false, Angle = 135, Stops = stops.Select((c, i) => new GradientStopDef(c, i / (double)(stops.Length - 1))).ToList() };
            var b = new Border { Width = 132, Height = 76, Margin = new Thickness(6), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, Background = ThemeEngine.BuildGradient(def, def.Stops, 1), ToolTip = "Use " + name };
            b.Child = Ui.Text(name, "Text.Caption", 11.5, FontWeights.SemiBold).With(t => { t.Foreground = Brushes.White; t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = new Thickness(10, 0, 0, 8); });
            b.MouseLeftButtonUp += (_, _) =>
            {
                Hub.Theme.Edit(t => { t.Gradient.FromWallpaper = false; t.Gradient.Stops = def.Stops.Select(s => new GradientStopDef(s.Color, s.Offset)).ToList(); });
                Rebuild();
            };
            presets.Children.Add(b);
        }
        Children.Add(Kit.Section("Gradient presets", "Hand-picked starting points. Click to use one; tweak it above.", presets));

        Children.Add(Kit.Section("Gradient wallpaper", null,
            Kit.Row("Use this gradient as my wallpaper", "Renders the gradient at your screen resolution and sets it as the desktop background. Your current wallpaper is remembered and restored by “Revert all” on Windows Tweaks.",
                Kit.Btn("Apply as wallpaper", ApplyAsWallpaper, Glyph.Picture), false)));
        RenderStops();
    }

    private static readonly (string, string[])[] GradientPresets =
    {
        ("Aurora", new[] { "#5EE7DF", "#4F7CFF", "#7B3FE4" }), ("Dusk", new[] { "#FF9A8B", "#FF6A88", "#7A3C8F" }),
        ("Ocean", new[] { "#2BC0E4", "#1E5FAF", "#0B1E3F" }), ("Ember", new[] { "#FFC371", "#FF5F6D", "#5A1F3D" }),
        ("Forest", new[] { "#A8E063", "#2E8B57", "#0F3B2E" }), ("Graphite", new[] { "#8E9EAB", "#4B5763", "#1C2128" }),
        ("Lavender", new[] { "#E0C3FC", "#8EC5FC", "#5B6CD8" }), ("Sunset", new[] { "#FDE38A", "#F6A15B", "#B0466D" }),
    };

    private void Edit(Action<Theme> a) { Hub.Theme.Edit(a); UpdatePreview(); }

    private void UpdatePreview()
    {
        if (_gradientPreview is null) return;
        var g = Hub.Theme.Current.Gradient;
        var stops = g.FromWallpaper || g.Stops.Count < 2 ? Hub.Wallpaper.Palette.ToGradientStops() : g.Stops;
        _gradientPreview.Background = ThemeEngine.BuildGradient(g, stops, 1);
    }

    private void RenderStops()
    {
        UpdatePreview();
        if (_stops is null) return;
        _stops.Children.Clear();
        var list = Hub.Theme.Current.Gradient.Stops;
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            var field = new ColorField(ColorMath.Parse(list[i].Color), $"Stop {i + 1} colour");
            field.Changed += c => Edit(t => t.Gradient.Stops[index].Color = ColorMath.Hex(c));
            var row = Kit.Buttons(field,
                Kit.Slider(0, 1, list[i].Offset, v => Edit(t => t.Gradient.Stops[index].Offset = Math.Round(v, 3)), v => $"{v * 100:0}%", 180),
                Ui.IconButton(Glyph.Delete, "Remove stop", () => { if (Hub.Theme.Current.Gradient.Stops.Count <= 2) return; Edit(t => t.Gradient.Stops.RemoveAt(index)); RenderStops(); }));
            row.Margin = new Thickness(0, 4, 0, 4);
            _stops.Children.Add(row);
        }
    }

    private void AddStop()
    {
        if (Hub.Theme.Current.Gradient.Stops.Count >= 6) return;
        Edit(t => t.Gradient.Stops.Add(new GradientStopDef(ColorMath.Hex(Hub.Wallpaper.Palette.Complement), 1)));
        RenderStops();
    }

    private void ApplyAsWallpaper()
    {
        if (!Dialogs.Confirm("Change your wallpaper?", "Your desktop background will be replaced with this gradient. Your current wallpaper is backed up and can be restored from Windows Tweaks › Revert all.", "Set wallpaper")) return;
        var g = Hub.Theme.Current.Gradient;
        var stops = g.FromWallpaper || g.Stops.Count < 2 ? Hub.Wallpaper.Palette.ToGradientStops() : g.Stops;
        var mon = Monitors.Primary;
        var file = Tweaks.ApplyGradientWallpaper(ThemeEngine.BuildGradient(g, stops, 1), mon.Bounds.Width, mon.Bounds.Height);
        Log.Info("Gradient wallpaper written to " + file);
    }
}

// =================================================================================================
public sealed class IslandPage : PageBase
{
    public override string Title => "Dynamic Island";
    public override string Subtitle => "A living capsule at the top of your screen. Click it to expand; scroll on it for volume; drag it to move. Press " + Hub.Settings.General.HotkeyIsland + " to open it with the keyboard.";
    private static IslandSettings S => Hub.Settings.Island;
    private static void Save() { Hub.Store.Save(); Hub.Island.Apply(); }

    protected override void Build()
    {
        Children.Add(Kit.Notice(Glyph.More, S.Enabled ? "The island is running" : "The island is off", S.Enabled ? "Try it now — the island will expand on your screen." : "Turn it on to get time, media, timers and quick controls at the top of your screen.", S.Enabled ? "T.Good" : "T.Warn",
            S.Enabled ? Kit.Btn("Expand now", () => Hub.Island.Expand()) : Kit.Btn("Turn on", () => { S.Enabled = true; Save(); Rebuild(); }, null, true)));

        var monitors = Monitors.All();
        var monitorOptions = new List<string> { "Primary display", "Every display" };
        monitorOptions.AddRange(monitors.Select(m => m.Label));
        var selected = S.Monitor == "primary" ? 0 : S.Monitor == "all" ? 1 : 2 + Math.Max(0, monitors.FindIndex(m => m.Device == S.Monitor));
        Children.Add(Kit.Section("Placement", null,
            Kit.ToggleRow("Show the Dynamic Island", null, S.Enabled, v => { S.Enabled = v; Save(); }, false),
            Kit.Row("Display", monitors.Count > 1 ? "With several displays each island can be dragged to its own position." : "Only one display is connected.",
                Kit.Combo(monitorOptions, selected, i => { S.Monitor = i == 0 ? "primary" : i == 1 ? "all" : monitors[i - 2].Device; Save(); })),
            Kit.SliderRow("Horizontal offset", "Distance from the centre of the screen. You can also drag the island.", -900, 900, S.OffsetX, v => { S.OffsetX = Math.Round(v); Save(); }, v => $"{v:0}"),
            Kit.SliderRow("Distance from top", null, 0, 120, S.OffsetY, v => { S.OffsetY = Math.Round(v); Save(); }, v => $"{v:0} px"),
            Kit.Row("Centre the island", S.OffsetX == 0 ? "It's exactly centred now." : $"It's {Math.Abs(S.OffsetX):0} px {(S.OffsetX < 0 ? "left" : "right")} of centre.",
                Kit.Btn("Centre now", () => { Hub.Island.Centre(); Rebuild(); }, Glyph.Check, true)),
            Kit.ToggleRow("Snap to centre while dragging", "The island clicks into the exact centre when you drag it close.", S.SnapToCentre, v => { S.SnapToCentre = v; Save(); }),
            Kit.ToggleRow("Keep centred", "Dragging only changes the distance from the top.", S.KeepCentred, v => { S.KeepCentred = v; if (v) Hub.Island.Centre(); Save(); Rebuild(); }),
            Kit.Row("Reset position", "Back to the top centre on every display.", Kit.Btn("Reset", () => { S.OffsetX = 0; S.OffsetY = 10; S.PerMonitorOffset.Clear(); Save(); Rebuild(); }, Glyph.Refresh))));

        var materials = new[] { "Glass", "Smoked", "Solid" };
        Children.Add(Kit.Section("Appearance", null,
            Kit.Row("Material", "Glass frosts your wallpaper, Smoked is a deep tint, Solid is fully opaque.", Kit.Segmented(materials, Math.Max(0, Array.IndexOf(materials, S.Material)), i => { S.Material = materials[i]; Save(); }), false),
            Kit.SliderRow("Size", null, 0.8, 1.4, S.Scale, v => { S.Scale = Math.Round(v, 2); Save(); }, v => $"{v * 100:0}%"),
            Kit.SliderRow("Opacity", null, 0.3, 1, S.Opacity, v => { S.Opacity = Math.Round(v, 2); Save(); }, v => $"{v * 100:0}%"),
            Kit.Row("Rim", "A coloured edge around the island. Glass keeps the material's own light rim.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Rims.Select(r => r.Name).ToArray(), Math.Max(0, Array.FindIndex(WinOrbit.Island.IslandWindow.Rims, r => r.Id == S.Rim)), i => { S.Rim = WinOrbit.Island.IslandWindow.Rims[i].Id; Save(); }), false),
            Kit.ToggleRow("Rim glow", "A soft halo in the rim's colours around the island.", S.RimGlow, v => { S.RimGlow = v; Save(); }, true),
            Kit.WideRow("Shape", "Capsule is fully round; Rounded and Square are soft and crisp rectangles; Notch sits flush with the top of the screen like a camera notch; Drop is tighter on top and round underneath.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Shapes, Math.Max(0, Array.IndexOf(WinOrbit.Island.IslandWindow.Shapes, S.Shape)), i => { S.Shape = WinOrbit.Island.IslandWindow.Shapes[i]; Save(); })),
            Kit.Row("Thickness", "How tall the collapsed island is.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Thicknesses, Math.Max(0, Array.IndexOf(WinOrbit.Island.IslandWindow.Thicknesses, S.Thickness)), i => { S.Thickness = WinOrbit.Island.IslandWindow.Thicknesses[i]; Save(); })),
            Kit.Row("Colours", "The island uses the active theme's glass, accent and wallpaper gradient.", Kit.Btn("Edit glass", () => Owner?.Navigate("studio"), Glyph.Drop))));

        Children.Add(Kit.Section("Collapsed capsule", "What the compact pill shows. Live activity (a timer, a download, or media) takes the left side.",
            Kit.ToggleRow("Time", null, S.ShowTime, v => { S.ShowTime = v; Save(); }, false, Glyph.Clock),
            Kit.ToggleRow("Date", null, S.ShowDate, v => { S.ShowDate = v; Save(); }, true, Glyph.Calendar),
            Kit.ToggleRow("Now playing", null, S.ShowMedia, v => { S.ShowMedia = v; Save(); }, true, Glyph.Music),
            Kit.ToggleRow("Active timer", null, S.ShowTimer, v => { S.ShowTimer = v; Save(); }, true, Glyph.Stopwatch),
            Kit.ToggleRow("CPU and memory", "Samples system load only while visible.", S.ShowSystem, v => { S.ShowSystem = v; Save(); }, true, Glyph.Chip),
            Kit.ToggleRow("Download activity", "Watches your Downloads folder for files that browsers are still downloading. Only file names are read, locally.", S.WatchDownloads, v => { S.WatchDownloads = v; Save(); }, true, Glyph.Download)));

        Children.Add(Kit.Section("Live activities", "Things the island shows on its own, the moment they happen. Windows pushes these events; the lock-key check is the one exception, noted below.",
            Kit.ToggleRow("Volume and brightness meter", "Changing the volume — keyboard keys, the taskbar, scrolling on the island — or a laptop's brightness shows a glass meter in the capsule.", S.VolumeHud, v => { S.VolumeHud = v; Save(); }, false, Glyph.Volume),
            Kit.ToggleRow("Charging and battery", "On laptops: a moment in the capsule when you plug in or unplug, and a warning at 20 % and 10 %.", S.BatteryAlerts, v => { S.BatteryAlerts = v; Save(); }, true, Glyph.Battery),
            Kit.ToggleRow("Caps Lock, Num Lock and Scroll Lock", "A key cap with its light when you switch one. WinOrbit asks Windows whether the three lock lights are on about eight times a second; it never sees what you type.", S.LockKeyAlerts, v => { S.LockKeyAlerts = v; Save(); }, true, Glyph.Keyboard),
            Kit.ToggleRow("Wi-Fi and Ethernet", "Connected (with the network's name) and disconnected. Read locally from Windows; nothing is sent anywhere.", S.NetworkAlerts, v => { S.NetworkAlerts = v; Save(); }, true, Glyph.Wifi),
            Kit.ToggleRow("Bluetooth devices", "Your paired headphones, keyboards, mice and phones connecting and disconnecting.", S.BluetoothAlerts, v => { S.BluetoothAlerts = v; Save(); }, true, Glyph.Bluetooth),
            Kit.ToggleRow("USB devices", "Drives, mice, controllers and other USB devices plugged in or removed, by name.", S.UsbAlerts, v => { S.UsbAlerts = v; Save(); }, true, Glyph.Usb),
            Kit.ToggleRow("Display and system modes", "Dark or light mode, night light and airplane mode switching on or off. Windows signals each change; nothing is checked on a timer.", S.SystemAlerts, v => { S.SystemAlerts = v; Save(); }, true, Glyph.Moon),
            Kit.ToggleRow("Event reminders", "A card ten minutes before, and when, an event you added in the island's calendar starts.", S.EventReminders, v => { S.EventReminders = v; Save(); }, true, Glyph.Calendar),
            Kit.ToggleRow("Album colours", "While a song plays, the glass glows with the colours of its artwork and cross-fades when the track changes. The Album rim uses them too. Worked out on this PC from the artwork Windows already has.", S.AlbumTint, v => { S.AlbumTint = v; Save(); }, true, Glyph.Color),
            Kit.ToggleRow("Bars follow the music", "The equaliser in the capsule moves with what's actually playing, tinted by the album art. Reads Windows' output level 20 times a second, only while music plays.", S.AudioReactive, v => { S.AudioReactive = v; Save(); }, true, Glyph.Music),
            Kit.ToggleRow("Privacy dots", "Orange while any app uses your microphone, green for the camera, blue for your location — hover to see which app. Read locally from Windows' own privacy records.", S.PrivacyDot, v => { S.PrivacyDot = v; Save(); }, true, Glyph.Shield),
            Kit.ToggleRow("Lyrics in the capsule", Hub.Settings.Privacy.LyricsEnabled ? "Shows the line being sung instead of the song title." : "Turn on Synced lyrics in Settings › Privacy & network first.", S.LyricsInCapsule, v => { S.LyricsInCapsule = v; Save(); }, true, Glyph.Note),
            Kit.ToggleRow("Clipboard history", "Keeps the last 40 copied items in memory for the Clipboard tab. Pinned text is saved; everything else is forgotten when WinOrbit quits. Private copies (password managers) are skipped.", S.ClipboardHistory, v => { S.ClipboardHistory = v; Save(); Hub.Clipboard.Apply(); }, true, Glyph.Copy),
            Kit.WideRow("Preview", "See what each live activity looks like on your island.", Kit.WrapButtons(
                Kit.Btn("Charging", () => Hub.Island.Demo("charging")), Kit.Btn("Low battery", () => Hub.Island.Demo("low")),
                Kit.Btn("Volume", () => Hub.Island.Demo("volume")), Kit.Btn("Microphone", () => Hub.Island.Demo("mic")),
                Kit.Btn("Caps Lock", () => Hub.Island.Demo("caps")), Kit.Btn("Wi-Fi", () => Hub.Island.Demo("wifi")),
                Kit.Btn("Bluetooth", () => Hub.Island.Demo("bluetooth")), Kit.Btn("USB", () => Hub.Island.Demo("usb")),
                Kit.Btn("Wi-Fi off", () => Hub.Island.Demo("wifi-radio")), Kit.Btn("Bluetooth on", () => Hub.Island.Demo("bt-radio")),
                Kit.Btn("Dark mode", () => Hub.Island.Demo("dark")), Kit.Btn("Night light", () => Hub.Island.Demo("night")),
                Kit.Btn("Airplane mode", () => Hub.Island.Demo("airplane")), Kit.Btn("Energy saver", () => Hub.Island.Demo("saver")),
                Kit.Btn("New song", () => Hub.Island.Demo("song")), Kit.Btn("Event reminder", () => Hub.Island.Demo("event"))))));

        var modules = new StackPanel();
        foreach (var (id, glyph, name, desc) in new[]
                 {
                     ("media", Glyph.Music, "Media", "Artwork, progress, transport and volume."), ("system", Glyph.Chip, "System monitor", "CPU, memory, GPU (with temperature on NVIDIA), network, disk."),
                     ("controls", Glyph.Settings, "Quick controls", "Volume, monitor brightness (DDC/CI), Wi-Fi, Bluetooth and settings shortcuts."), ("focus", Glyph.Stopwatch, "Focus timer", "Pomodoro, countdown and stopwatch."),
                     ("calendar", Glyph.Calendar, "Calendar", "The month, your events (kept on this PC) and reminders."), ("weather", Glyph.Cloud, "Weather", "Now, the next hours and days, with the sky's colours in the glass. Needs weather turned on in Settings › Privacy."), ("notes", Glyph.Note, "Quick note", "A scratchpad saved on this PC."),
                     ("clipboard", Glyph.Copy, "Clipboard", "Recent copies — text, links, images, files. Needs Clipboard history above."),
                     ("shelf", "", "File shelf", "Drag files onto the island to park them; drag them out into any app later.")
                 })
        {
            modules.Children.Add(Kit.ToggleRow(name, desc, S.Modules.Contains(id), v =>
            {
                if (v && !S.Modules.Contains(id)) S.Modules.Add(id);
                if (!v) S.Modules.Remove(id);
                Save();
            }, modules.Children.Count > 0, glyph));
        }
        var openTo = new[] { "Last", "Launcher" };
        modules.Children.Add(Kit.Row("Open to", "Last panel reopens what you used last; Launcher opens the grid of all modules first.",
            Kit.Segmented(new[] { "Last panel", "Launcher" }, Math.Max(0, Array.IndexOf(openTo, S.OpenTo)), i => { S.OpenTo = openTo[i]; Save(); }), true));
        Children.Add(Kit.Section("Expanded panels", "Tabs available when the island opens. The grid button (or Home / 0) shows every module as a launcher; ←/→ or the number keys switch with the keyboard.", modules));

        Children.Add(Kit.Section("Behaviour", null,
            Kit.ToggleRow("Hide during fullscreen apps and games", "Gets completely out of the way of games, videos and presentations.", S.HideInFullscreen, v => { S.HideInFullscreen = v; Save(); }, false),
            Kit.ToggleRow("Auto-hide", "Slides up out of view; touch the top edge of the screen to bring it back.", S.AutoHide, v => { S.AutoHide = v; Save(); }),
            Kit.ToggleRow("Expand on hover", "Opens after resting the pointer on it briefly.", S.ExpandOnHover, v => { S.ExpandOnHover = v; Save(); }),
            Kit.SliderRow("Hover delay", "How long the pointer must rest before it opens, so sweeping past the top of the screen doesn't open it.", 0, 1200, S.HoverDelayMs, v => { S.HoverDelayMs = (int)Math.Round(v / 20) * 20; Save(); }, v => $"{v:0} ms", true, 20),
            Kit.ToggleRow("Announce track changes", "Briefly shows the new song in the capsule.", S.PeekOnMediaChange, v => { S.PeekOnMediaChange = v; Save(); }),
            Kit.SliderRow("Shrink after", "Seconds without any interaction before the open island shrinks back (2.5 s after the pointer leaves). 0 keeps it open until you click away.", 0, 30, S.AutoCollapseSeconds, v => { S.AutoCollapseSeconds = (int)v; Save(); }, v => v < 1 ? "Never" : $"{v:0} s", true, 1)));

        Children.Add(Kit.Section("Accessibility", null,
            Kit.Row("Keyboard", $"{Hub.Settings.General.HotkeyIsland} opens the island and gives it keyboard focus. Tab moves between controls, Esc closes. The island never steals focus when clicked with the mouse.", Kit.Btn("Change shortcut", () => Owner?.Navigate("settings")), false),
            Kit.Row("Motion", "Reduce motion in Appearance turns off all island animation.", Kit.Btn("Appearance", () => Owner?.Navigate("appearance")))));
    }
}

// =================================================================================================
public sealed class WidgetsPage : PageBase
{
    public override string Title => "Widgets";
    public override string Subtitle => "Add glass widgets to your desktop. Drag to move, use the corner grip to resize, right-click any widget for its options. Hold Alt while dragging to ignore the grid.";
    private StackPanel? _placed;
    private static LayoutSettings L => Hub.Settings.Layout;

    protected override void Build()
    {
        var gallery = Kit.Gallery();
        foreach (var group in WidgetCatalog.All.GroupBy(k => k.Category))
        foreach (var kind in group)
        {
            var card = new Button { Width = 196, Margin = new Thickness(6), Padding = new Thickness(16, 14, 16, 14), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = "Add " + kind.Name };
            var sp = new StackPanel();
            var head = Ui.Grid("auto,*,auto");
            var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Child = Ui.Icon(kind.Glyph, 15, "T.Accent") };
            badge.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
            head.Children.Add(badge);
            head.Children.Add(Ui.Icon(Glyph.Add, 12, "T.Text3").At(0, 2));
            sp.Children.Add(head);
            sp.Children.Add(Ui.Text(kind.Name, "Text.Body", 13.5, FontWeights.SemiBold).With(t => t.Margin = new Thickness(0, 10, 0, 2)));
            sp.Children.Add(Ui.Text(kind.Description, "Text.Caption", 11.5).With(t => { t.Height = 46; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
            sp.Children.Add(Ui.Text(kind.Category.ToUpperInvariant(), "Text.Label", 9.5).With(t => t.Margin = new Thickness(0, 6, 0, 0)));
            card.Content = sp;
            card.Click += (_, _) => { Hub.Widgets.Add(kind.Id); RenderPlaced(); };
            gallery.Children.Add(card);
        }
        Children.Add(Kit.Section("Add a widget", "Each widget is its own lightweight window and does no work while hidden.", gallery));

        _placed = new StackPanel();
        Children.Add(Kit.Section("On your desktop", null, _placed));

        Children.Add(Kit.Section("Layout", null,
            Kit.ToggleRow("Show widgets", $"Shortcut: {Hub.Settings.General.HotkeyWidgets}", L.WidgetsVisible, v => { L.WidgetsVisible = v; Hub.Store.Save(); Hub.Widgets.Apply(); }, false),
            Kit.ToggleRow("Lock layout", "Prevents accidental moving and resizing.", L.Locked, v => { L.Locked = v; Hub.Store.Save(); }),
            Kit.ToggleRow("Snap to grid", "Widgets also snap magnetically to the screen margins.", L.SnapToGrid, v => { L.SnapToGrid = v; Hub.Store.Save(); }),
            Kit.SliderRow("Grid size", null, 4, 48, L.GridSize, v => { L.GridSize = (int)v; Hub.Store.Save(); }, v => $"{v:0} px", true, 4),
            Kit.SliderRow("Screen margin", "Space kept clear at the edges of the screen.", 0, 96, L.Margin, v => { L.Margin = (int)v; Hub.Store.Save(); }, v => $"{v:0} px", true, 2),
            Kit.SliderRow("Spacing", "Gap between widgets when arranging.", 0, 48, L.Spacing, v => { L.Spacing = (int)v; Hub.Store.Save(); }, v => $"{v:0} px", true, 2),
            Kit.Row("Tidy up", "Re-flow every visible widget into neat columns using the margin and spacing above.",
                Kit.Buttons(Kit.Btn("Arrange left", () => Hub.Widgets.Arrange(false)), Kit.Btn("Arrange right", () => Hub.Widgets.Arrange(true), null, true)))));

        Children.Add(Kit.Notice(Glyph.Profiles, "Save layouts as profiles", "Profiles store your complete widget layout together with the theme and island. Restore any of them after a restart or switch between them in one click.", "T.Accent",
            Kit.Btn("Profiles", () => Owner?.Navigate("profiles"))));
        RenderPlaced();
    }

    private void RenderPlaced()
    {
        if (_placed is null) return;
        _placed.Children.Clear();
        var list = Hub.Settings.Widgets;
        if (list.Count == 0)
        {
            _placed.Children.Add(Ui.Text("No widgets yet. Pick one above — it appears on your desktop immediately.", "Text.Muted").With(t => t.Margin = new Thickness(0, 6, 0, 12)));
            return;
        }
        var first = true;
        foreach (var w in list.ToList())
        {
            var kind = WidgetCatalog.Find(w.Kind);
            if (kind is null) continue;
            var mon = Monitors.Find(w.Monitor);
            var controls = Kit.Buttons(
                Kit.Switch(w.Visible, v => { Hub.Widgets.SetVisible(w, v); }, "Show " + kind.Name),
                Kit.Slider(0.2, 1, w.Opacity, v => { w.Opacity = Math.Round(v, 2); Hub.Widgets.WindowOf(w)?.ApplyAppearance(); Hub.Store.Save(); }, v => $"{v * 100:0}%", 110),
                Ui.IconButton(Glyph.Delete, "Remove " + kind.Name, () => { Hub.Widgets.Remove(w); RenderPlaced(); }));
            _placed.Children.Add(Kit.Row(kind.Name, $"{mon.Label} · {w.Width:0}×{w.Height:0}{(w.Pinned ? " · always on top" : "")}", controls, !first, kind.Glyph));
            first = false;
        }
    }

    protected override void OnEnter() => Live(() => Hub.Widgets.Changed += RenderPlaced, () => Hub.Widgets.Changed -= RenderPlaced);
}
