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
    private const string GlyphAgentsRow = "";

    /// <summary>A button that copies some text and says so for a moment.</summary>
    private static Button CopyButton(string label, Func<string> text)
    {
        Button? b = null;
        b = Kit.Btn(label, () =>
        {
            try { Clipboard.SetText(text()); } catch { return; }
            b!.Content = "Copied";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { t.Stop(); b.Content = label; };
            t.Start();
        }, Glyph.Copy);
        return b;
    }
    private const string GlyphMicRow = "";

    private void ImportCalendar()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import calendar events", Filter = "Calendar file|*.ics;*.ical;*.ifb" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var info = new FileInfo(dlg.FileName);
            if (info.Length > 20 * 1024 * 1024) { Dialogs.Message("File too large", "That calendar file is over 20 MB. Export a shorter date range and try again."); return; }
            var result = IcsImport.Parse(File.ReadAllText(dlg.FileName), DateTime.Now, info.Name);
            if (result.Events.Count == 0)
            {
                Dialogs.Message("No events found", "That file has no events between 30 days ago and a year from now.");
                return;
            }
            var added = IcsImport.Merge(Hub.Store.Data.Events, result.Events);
            Hub.Store.SaveData();
            Hub.Island.RefreshCalendar();
            Dialogs.Message("Events imported", $"{added} event{(added == 1 ? "" : "s")} from {info.Name} are in the island's calendar now."
                + (result.Skipped > 0 ? $" {result.Skipped} cancelled or unreadable event{(result.Skipped == 1 ? " was" : "s were")} left out." : ""));
            Rebuild();
        }
        catch (Exception ex) { Dialogs.Message("Import failed", ex.Message); }
    }
    private static bool Side => S.Edge is "Left" or "Right";

    /// <summary>A list of apps (process names) as removable chips, plus "Add app…" which lists the apps open right now.</summary>
    private static WrapPanel AppPicker(List<string> list)
    {
        var panel = new WrapPanel();
        void Fill()
        {
            panel.Children.Clear();
            foreach (var app in list.ToList())
                panel.Children.Add(Kit.Btn(app, () => { list.Remove(app); Save(); Fill(); }, Glyph.Cancel, tooltip: $"Remove {app}").With(b => b.Margin = new Thickness(0, 0, 8, 8)));
            panel.Children.Add(Kit.Btn("Add app…", () =>
            {
                var apps = RunningApps.List().Where(a => !list.Contains(a.Process, StringComparer.OrdinalIgnoreCase)).ToList();
                var options = apps.Select(a => a.Name.Equals(a.Process, StringComparison.OrdinalIgnoreCase) ? a.Name : $"{a.Name}  ({a.Process})").ToList();
                options.Add("Type a program name…");
                var i = Dialogs.Choose("Add app", "Apps open right now. Open the one you want first if it isn't here.", options);
                if (i < 0) return;
                var pick = i < apps.Count ? apps[i].Process : Dialogs.Prompt("Add app", "Program name, as in Task Manager's Details tab (e.g. obs64 or vlc.exe)", "", "Add")?.Trim();
                if (string.IsNullOrEmpty(pick)) return;
                if (pick.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) pick = pick[..^4];
                if (!list.Contains(pick, StringComparer.OrdinalIgnoreCase)) { list.Add(pick); Save(); }
                Fill();
            }, Glyph.Add).With(b => b.Margin = new Thickness(0, 0, 8, 8)));
        }
        Fill();
        return panel;
    }

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
            Kit.Row("Screen edge", "Top, just above the taskbar, or against either side; it opens away from the edge. You can also drag the island right up to another edge.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Edges, Math.Max(0, Array.IndexOf(WinOrbit.Island.IslandWindow.Edges, S.Edge)), i => { S.Edge = WinOrbit.Island.IslandWindow.Edges[i]; S.OffsetX = 0; S.PerMonitorOffset.Clear(); Save(); Rebuild(); })),
            Kit.Adv(Kit.SliderRow(Side ? "Vertical offset" : "Horizontal offset", "Distance from the middle of the edge. You can also drag the island.", -900, 900, S.OffsetX, v => { S.OffsetX = Math.Round(v); Save(); }, v => $"{v:0}")),
            Kit.Adv(Kit.SliderRow($"Distance from {(S.Edge == "Top" ? "top" : S.Edge == "Bottom" ? "the taskbar" : "the edge")}", null, 0, 120, S.OffsetY, v => { S.OffsetY = Math.Round(v); Save(); }, v => $"{v:0} px")),
            Kit.Row("Centre the island", S.OffsetX == 0 ? "It's exactly centred now." : $"It's {Math.Abs(S.OffsetX):0} px {(S.OffsetX < 0 ? (Side ? "above" : "left") : (Side ? "below" : "right"))} of centre.",
                Kit.Btn("Centre now", () => { Hub.Island.Centre(); Rebuild(); }, Glyph.Check, true)),
            Kit.Adv(Kit.ToggleRow("Snap to centre while dragging", "The island clicks into the exact centre when you drag it close.", S.SnapToCentre, v => { S.SnapToCentre = v; Save(); })),
            Kit.Adv(Kit.ToggleRow("Keep centred", "Dragging only changes the distance from the edge.", S.KeepCentred, v => { S.KeepCentred = v; if (v) Hub.Island.Centre(); Save(); Rebuild(); })),
            Kit.Row("Reset position", "Back to the top centre on every display.", Kit.Btn("Reset", () => { S.Edge = "Top"; S.OffsetX = 0; S.OffsetY = 10; S.PerMonitorOffset.Clear(); Save(); Rebuild(); }, Glyph.Refresh))));

        var materials = GlassSurface.Materials;
        Children.Add(Kit.Section("Appearance", null,
            Kit.Row("Material", "Glass frosts your wallpaper, Gradient fades from your accent colour into your wallpaper's colours (opaque), Smoked is a deep tint, Solid is fully opaque in the theme's colour, Black is plain true black with a fine edge (the cleanest and cheapest to draw).", Kit.Segmented(materials, Math.Max(0, Array.IndexOf(materials, GlassSurface.Material(S.Material))), i => { S.Material = materials[i]; Save(); }), false),
            Kit.SliderRow("Size", null, 0.8, 1.4, S.Scale, v => { S.Scale = Math.Round(v, 2); Save(); }, v => $"{v * 100:0}%"),
            Kit.Adv(Kit.SliderRow("Opacity", null, 0.3, 1, S.Opacity, v => { S.Opacity = Math.Round(v, 2); Save(); }, v => $"{v * 100:0}%")),
            Kit.Adv(Kit.Row("Rim", "A coloured edge around the island. Glass keeps the material's own light rim.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Rims.Select(r => r.Name).ToArray(), Math.Max(0, Array.FindIndex(WinOrbit.Island.IslandWindow.Rims, r => r.Id == S.Rim)), i => { S.Rim = WinOrbit.Island.IslandWindow.Rims[i].Id; Save(); }), false)),
            Kit.Adv(Kit.ToggleRow("Rim glow", "A soft halo in the rim's colours around the island.", S.RimGlow, v => { S.RimGlow = v; Save(); }, true)),
            Kit.WideRow("Shape", "Capsule is fully round; Rounded and Square are soft and crisp rectangles; Notch sits flush against its screen edge like a camera notch; Drop is tighter at the edge and round away from it.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Shapes, Math.Max(0, Array.IndexOf(WinOrbit.Island.IslandWindow.Shapes, S.Shape)), i => { S.Shape = WinOrbit.Island.IslandWindow.Shapes[i]; Save(); })),
            Kit.Adv(Kit.Row("Thickness", "How tall the collapsed island is.",
                Kit.Segmented(WinOrbit.Island.IslandWindow.Thicknesses, Math.Max(0, Array.IndexOf(WinOrbit.Island.IslandWindow.Thicknesses, S.Thickness)), i => { S.Thickness = WinOrbit.Island.IslandWindow.Thicknesses[i]; Save(); }))),
            Kit.Adv(Kit.Row("Colours", "The island uses the active theme's glass, accent and wallpaper gradient.", Kit.Btn("Edit glass", () => Owner?.Navigate("studio"), Glyph.Drop)))));

        var glances = WinOrbit.Island.IslandWindow.Glances;
        Children.Add(Kit.Section("Collapsed capsule", "What the compact pill shows. Live activity (a timer, a download, or media) takes the left side.",
            Kit.WideRow("When nothing is happening", "Clock shows the time and date (switch them below). Rather have a clock widget on the desktop? Pick System for CPU and memory, Status for Wi-Fi, Bluetooth and battery, Eyes for a pair of eyes that follow your pointer and blink (they nod off when the pointer rests for a minute), or Quiet for just the glass.",
                Kit.Segmented(glances.Select(g => g.Name).ToArray(), Math.Max(0, Array.FindIndex(glances, g => g.Id == WinOrbit.Island.IslandWindow.GlanceOf(S.Glance))), i => { S.Glance = glances[i].Id; Save(); Rebuild(); })),
            Kit.ToggleRow("Time", "With Clock.", S.ShowTime, v => { S.ShowTime = v; Save(); }, true, Glyph.Clock),
            Kit.ToggleRow("Date", "With Clock.", S.ShowDate, v => { S.ShowDate = v; Save(); }, true, Glyph.Calendar),
            Kit.ToggleRow("Now playing", null, S.ShowMedia, v => { S.ShowMedia = v; Save(); }, true, Glyph.Music),
            Kit.ToggleRow("Active timer", null, S.ShowTimer, v => { S.ShowTimer = v; Save(); }, true, Glyph.Stopwatch),
            Kit.Adv(Kit.ToggleRow("CPU and memory", "Beside the clock. Samples system load only while visible.", S.ShowSystem, v => { S.ShowSystem = v; Save(); }, true, Glyph.Chip)),
            Kit.ToggleRow("Download activity","Watches your Downloads folder for files that browsers are still downloading, and shows a card when one finishes — click it to open the file, or drag it into any app. Only file names are read, locally.", S.WatchDownloads, v => { S.WatchDownloads = v; Save(); }, true, Glyph.Download)));

        Children.Add(Kit.Section("Live activities", "Things the island shows on its own, the moment they happen. Windows pushes these events; the lock-key check is the one exception, noted below.",
            Kit.ToggleRow("Volume and brightness meter", "Changing the volume — keyboard keys, the taskbar, scrolling on the island — or a laptop's brightness shows a glass meter in the capsule.", S.VolumeHud, v => { S.VolumeHud = v; Save(); }, false, Glyph.Volume),
            Kit.ToggleRow("Charging and battery", "On laptops: a moment in the capsule when you plug in or unplug, and a warning at 20 % and 10 %.", S.BatteryAlerts, v => { S.BatteryAlerts = v; Save(); }, true, Glyph.Battery),
            Kit.ToggleRow("Caps Lock, Num Lock and Scroll Lock", "A key cap with its light when you switch one. WinOrbit asks Windows whether the three lock lights are on about eight times a second; it never sees what you type.", S.LockKeyAlerts, v => { S.LockKeyAlerts = v; Save(); }, true, Glyph.Keyboard),
            Kit.Adv(Kit.ToggleRow("Keyboard language", "A key cap with the language (EN, BN, FR…) when you switch keyboards with Win+Space, Alt+Shift or the taskbar. Only runs when you have more than one keyboard: WinOrbit then asks Windows which keyboard the app in front uses, four times a second. It never sees what you type.", S.LanguageAlerts, v => { S.LanguageAlerts = v; Save(); }, true, Glyph.Globe)),
            Kit.ToggleRow("Wi-Fi and Ethernet", "Connected (with the network's name) and disconnected. Read locally from Windows; nothing is sent anywhere.", S.NetworkAlerts, v => { S.NetworkAlerts = v; Save(); }, true, Glyph.Wifi),
            Kit.ToggleRow("Bluetooth devices", "Your paired headphones, keyboards, mice and phones connecting and disconnecting.", S.BluetoothAlerts, v => { S.BluetoothAlerts = v; Save(); }, true, Glyph.Bluetooth),
            Kit.Adv(Kit.ToggleRow("USB devices", "Drives, mice, controllers and other USB devices plugged in or removed, by name.", S.UsbAlerts, v => { S.UsbAlerts = v; Save(); }, true, Glyph.Usb)),
            Kit.ToggleRow("Screenshots", "A card with a thumbnail when a screenshot lands in your Screenshots folder (Win+PrtScn, Snipping Tool). Click it to open the picture, drag it straight into a chat or email, or show it in its folder.", S.ScreenshotCards, v => { S.ScreenshotCards = v; Save(); }, true, Glyph.Photo),
            Kit.Adv(Kit.ToggleRow("Rain soon", Hub.Weather.Enabled
                    ? "A card when rain or snow is likely within two hours while it's dry now, once per spell. Checks the forecast for your place every 30 minutes, even when no weather is on screen; each check shows in the network ledger."
                    : "Turn on Weather (with your place) in Settings › Privacy & network first. Then it checks the forecast every 30 minutes and shows a card when rain is likely within two hours.",
                S.RainAlerts, v => { S.RainAlerts = v; Save(); }, true, Glyph.Cloud)),
            Kit.Adv(Kit.ToggleRow("Display and system modes", "Dark or light mode, night light and airplane mode switching on or off. Windows signals each change; nothing is checked on a timer.", S.SystemAlerts, v => { S.SystemAlerts = v; Save(); }, true, Glyph.Moon)),
            Kit.ToggleRow("Event reminders", "A card ten minutes before, and when, an event you added in the island's calendar starts.", S.EventReminders, v => { S.EventReminders = v; Save(); }, true, Glyph.Calendar),
            Kit.Adv(Kit.ToggleRow("Album colours", "While a song plays, the glass glows with the colours of its artwork and cross-fades when the track changes. The Album rim uses them too. Worked out on this PC from the artwork Windows already has.", S.AlbumTint, v => { S.AlbumTint = v; Save(); }, true, Glyph.Color)),
            Kit.Adv(Kit.ToggleRow("Bars follow the music", "The equaliser in the capsule moves with what's actually playing, tinted by the album art. Reads Windows' output level 20 times a second, only while music plays.", S.AudioReactive, v => { S.AudioReactive = v; Save(); }, true, Glyph.Music)),
            Kit.ToggleRow("Privacy dots", "Orange while any app uses your microphone, green for the camera, blue for your location — hover to see which app. Read locally from Windows' own privacy records.", S.PrivacyDot, v => { S.PrivacyDot = v; Save(); }, true, Glyph.Shield),
            Kit.Adv(Kit.ToggleRow("Microphone pill", "During a call (or whenever an app uses your microphone) the capsule shows the app, bars that follow your voice and a mute button. Mute is Windows' own microphone mute, so it works in every app. The level is read from Windows' meter; WinOrbit never records or opens the microphone itself.", S.CallPill, v => { S.CallPill = v; Save(); }, true, GlyphMicRow)),
            Kit.Adv(Kit.ToggleRow("Side bubble for short alerts", "While the capsule shows music, a call, a timer or a download, short alerts (Caps Lock, keyboard language, \"copied\", microphone muted…) split off into a small bubble beside it instead of replacing it, then flow back in.", S.SplitAlerts, v => { S.SplitAlerts = v; Save(); }, true, Glyph.Info)),
            Kit.Adv(Kit.ToggleRow("Cards from your scripts and apps", @"Lets programs you run on this PC show a short card on the island — ""Build finished"", ""Backup done"" — by writing one line of JSON to the local pipe \\.\pipe\WinOrbit.Island. Only programs running as you can connect; it is never reachable from the network. Each card names the program that sent it, shows plain text only (no links or buttons), and each program gets a few cards at a time. The user guide has a one-line example. Off by default.",
                S.AppCards, v => { S.AppCards = v; Save(); }, true, Glyph.Info)),
            Kit.Adv(Kit.ToggleRow("Swipe to skip tracks", "Swipe sideways on the island with two fingers on a touchpad, or tilt the mouse wheel, to go to the next or previous track.", S.SwipeToSkip, v => { S.SwipeToSkip = v; Save(); }, true, Glyph.Music)),
            Kit.Adv(Kit.ToggleRow("Lyrics in the capsule", Hub.Settings.Privacy.LyricsEnabled ? "Shows the line being sung instead of the song title." : "Turn on Synced lyrics in Settings › Privacy & network first.", S.LyricsInCapsule, v => { S.LyricsInCapsule = v; Save(); }, true, Glyph.Note)),
            Kit.ToggleRow("Clipboard history", "Keeps the last 40 copied items in memory for the Clipboard tab. Pinned text is saved; everything else is forgotten when WinOrbit quits. Private copies (password managers) are skipped.", S.ClipboardHistory, v => { S.ClipboardHistory = v; Save(); Hub.Clipboard.Apply(); }, true, Glyph.Copy),
            Kit.WideRow("Preview", "See what each live activity looks like on your island.", Kit.WrapButtons(
                Kit.Btn("Charging", () => Hub.Island.Demo("charging")), Kit.Btn("Low battery", () => Hub.Island.Demo("low")),
                Kit.Btn("Volume", () => Hub.Island.Demo("volume")), Kit.Btn("Microphone", () => Hub.Island.Demo("mic")), Kit.Btn("Microphone pill", () => Hub.Island.Demo("call")), Kit.Btn("Side bubble", () => Hub.Island.Demo("split")),
                Kit.Btn("Caps Lock", () => Hub.Island.Demo("caps")), Kit.Btn("Wi-Fi", () => Hub.Island.Demo("wifi")),
                Kit.Btn("Bluetooth", () => Hub.Island.Demo("bluetooth")), Kit.Btn("USB", () => Hub.Island.Demo("usb")),
                Kit.Btn("Wi-Fi off", () => Hub.Island.Demo("wifi-radio")), Kit.Btn("Bluetooth on", () => Hub.Island.Demo("bt-radio")),
                Kit.Btn("Dark mode", () => Hub.Island.Demo("dark")), Kit.Btn("Night light", () => Hub.Island.Demo("night")),
                Kit.Btn("Airplane mode", () => Hub.Island.Demo("airplane")), Kit.Btn("Energy saver", () => Hub.Island.Demo("saver")),
                Kit.Btn("New song", () => Hub.Island.Demo("song")), Kit.Btn("Event reminder", () => Hub.Island.Demo("event")),
                Kit.Btn("Screenshot", () => Hub.Island.Demo("screenshot")), Kit.Btn("Download", () => Hub.Island.Demo("download")),
                Kit.Btn("Rain soon", () => Hub.Island.Demo("rain")), Kit.Btn("Keyboard language", () => Hub.Island.Demo("language"))))));

        var exe = AgentSetup.ExePath();
        Children.Add(Kit.Adv(Kit.Section("Coding agents", "See what AI coding tools on this PC are doing — Claude Code, Codex or your own scripts — in an Agents tab on the island, with a card when one needs your OK and a short note when it's done. Off by default. WinOrbit doesn't change those tools' settings: copy the setup for each tool you use.",
            Kit.ToggleRow("Coding agents", @"Adds the Agents tab and listens for the tools' updates on the local pipe \\.\pipe\WinOrbit.Island (programs running as you only; never reachable from the network). Each session shows a short title (its first request), its folder's name and a few words like ""Editing app.cs"". Nothing is saved: the list is in memory, sessions drop off after three hours of silence, and turning this off clears it.",
                S.CodingAgents, v => { S.CodingAgents = v; Save(); }, false, GlyphAgentsRow),
            Kit.Row("Claude Code", @"Copy, then paste into %USERPROFILE%\.claude\settings.json (merge it with any hooks you already have) — or paste it into Claude Code and ask it to add these hooks. Claude never waits for WinOrbit: every hook runs in the background.",
                CopyButton("Copy setup", () => AgentSetup.ClaudeCode(exe))),
            Kit.Row("Codex", @"Add this line to %USERPROFILE%\.codex\config.toml (near the top, before any [section]). Codex reports when it finishes a turn.",
                CopyButton("Copy line", () => AgentSetup.Codex(exe))),
            Kit.Row("Your own tool", @"Send one line of JSON — session, state (working, needs-you, done or ended), title, text, folder — to ""WinOrbit.exe --agent <name>"". The button copies a PowerShell example.",
                CopyButton("Copy example", () => AgentSetup.Own(exe))),
            Kit.Row("Preview", "The card a session shows when it needs you.", Kit.Btn("Needs you", () => Hub.Island.Demo("agent"))))));

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
        Children.Add(Kit.Section("Expanded panels", "Tabs available when the island opens. Use ←/→ or the number keys to switch with the keyboard.", modules));

        var imported = Hub.Store.Data.Events.Count(e => e.Source is not null);
        Children.Add(Kit.Adv(Kit.Section("Calendar events", "Bring in events from Outlook, Google Calendar or Apple Calendar: export the calendar as an .ics file, then import it here. It is a one-time copy onto this PC — nothing syncs and nothing goes online.",
            Kit.Row("Import an .ics file", "Repeating events are filled in from 30 days ago to a year ahead. Importing the same calendar again updates its events instead of doubling them.", Kit.Btn("Import…", ImportCalendar, Glyph.Import), false),
            Kit.Row("Remove imported events", imported == 0 ? "No imported events." : $"{imported} imported event{(imported == 1 ? "" : "s")}. Events you typed in the island stay.",
                Kit.Btn("Remove", () =>
                {
                    if (!Dialogs.Confirm("Remove imported events?", "Every event that came from an .ics file is removed from WinOrbit. Events you typed yourself are kept, and your other calendars are not touched.", "Remove")) return;
                    Hub.Store.Data.Events.RemoveAll(e => e.Source is not null);
                    Hub.Store.SaveData();
                    Hub.Island.RefreshCalendar();
                    Rebuild();
                }, Glyph.Delete).With(b => b.IsEnabled = imported > 0)))));

        Children.Add(Kit.Section("Behaviour", null,
            Kit.ToggleRow("Hide during fullscreen apps and games", "Gets out of the way the moment a game, video or presentation goes fullscreen on the island's screen, and drops back in when it ends.", S.HideInFullscreen, v => { S.HideInFullscreen = v; Save(); }, false),
            Kit.Adv(Kit.WideRow("Keep showing over", "Fullscreen apps the island stays visible over — say, slides you time with the focus timer.", AppPicker(S.ShowOverFullscreenApps))),
            Kit.Adv(Kit.WideRow("Always hide for", "Apps the island hides for whenever they're in front on its screen, fullscreen or not — a game in a window, a video editor, a screen recorder.", AppPicker(S.HideForApps))),
            Kit.ToggleRow("Auto-hide", "Slides up out of view; touch the top edge of the screen to bring it back.", S.AutoHide, v => { S.AutoHide = v; Save(); }),
            Kit.ToggleRow("Expand on hover", "Opens after resting the pointer on it briefly.", S.ExpandOnHover, v => { S.ExpandOnHover = v; Save(); }),
            Kit.Adv(Kit.SliderRow("Hover delay", "How long the pointer must rest before it opens, so sweeping past the top of the screen doesn't open it.", 0, 1200, S.HoverDelayMs, v => { S.HoverDelayMs = (int)Math.Round(v / 20) * 20; Save(); }, v => $"{v:0} ms", true, 20)),
            Kit.Adv(Kit.ToggleRow("Announce track changes", "Briefly shows the new song in the capsule.", S.PeekOnMediaChange, v => { S.PeekOnMediaChange = v; Save(); })),
            Kit.Adv(Kit.SliderRow("Shrink after", "Seconds without any interaction before the open island shrinks back (2.5 s after the pointer leaves). 0 keeps it open until you click away.", 0, 30, S.AutoCollapseSeconds, v => { S.AutoCollapseSeconds = (int)v; Save(); }, v => v < 1 ? "Never" : $"{v:0} s", true, 1))));

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

    private readonly List<WidgetView> _previews = new();

    protected override void Build()
    {
        // Clocks first: the most asked-for desktop widget, in every style, live. Added Bare (straight on the wallpaper).
        foreach (var p in _previews) p.Stop();
        _previews.Clear();

        // Desktop sets: a whole look (clock skin, matching pieces, theme, island glance, liquid glass) in one click.
        var sets = Kit.Gallery();
        foreach (var set in DesktopSets.All) sets.Children.Add(SetCard(set, _previews));
        Children.Add(Kit.Section("Desktop sets", "A whole look in one click: a clock skin with the pieces made for it, a theme, the island's glance and liquid glass on or off. Using a set replaces the widgets on your desktop; Profiles › Restore previous desktop brings yours back.",
            sets,
            Kit.Buttons(
                Kit.Btn("Save my desktop as a set…", () =>
                {
                    if (Hub.Settings.Widgets.Count(w => w.Visible) == 0) { Dialogs.Message("Nothing to save", "Put a few widgets on your desktop first; a set is made from the ones that are showing."); return; }
                    if (Dialogs.Prompt("Save as a set", "Name", "My desktop") is { Length: > 0 } name) DesktopSets.SaveCurrentAs(name.Trim());
                }, Glyph.Save),
                Kit.Btn("Import a set…", ImportSet, Glyph.Import))));

        var clocks = Kit.Gallery();
        foreach (var (id, name, size) in ClockWidget.Styles)
        {
            // Big stages and a thin inset: wide skins (Orbit) and tall ones (Hairline) still read at gallery size.
            var preview = new ClockWidget(new WidgetInstance { Kind = "clock", Variant = id }) { Margin = new Thickness(10) };
            _previews.Add(preview);
            var stage = new Grid { Height = 124, ClipToBounds = true };
            var wash = new Border { Opacity = 0.55 };
            wash.SetResourceReference(Border.BackgroundProperty, "T.WashStrong");
            wash.SetResourceReference(Border.CornerRadiusProperty, "R.Control");
            stage.Children.Add(wash);
            preview.SetResourceReference(EffectProperty, "W.BareShadow");
            stage.Children.Add(preview);
            var sp = new StackPanel();
            sp.Children.Add(stage);
            sp.Children.Add(Ui.Text(name, "Text.Body", 13, FontWeights.SemiBold).With(t => t.Margin = new Thickness(2, 9, 0, 0)));
            var card = new Button { Width = 264, Margin = new Thickness(6), Padding = new Thickness(10, 10, 10, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = $"Add the {name} clock to your desktop", Content = sp };
            card.Click += (_, _) => { Hub.Widgets.Add("clock", id, new Dictionary<string, string> { ["style"] = "bare" }, null, size); RenderPlaced(); };
            clocks.Children.Add(card);
        }
        Children.Add(Kit.Section("Clocks", "Date and time as part of your wallpaper. Each one sits straight on the desktop with a soft shadow; right-click it to change the style or size, or to put it on a glass card.", clocks));

        // Text-only pieces that go with the clock skins, so the whole desktop reads as one design.
        var matching = Kit.Gallery();
        foreach (var (kindId, variant, name, about, size) in WidgetCatalog.Companions)
        {
            if (WidgetCatalog.Find(kindId) is not { } kind) continue;
            var card = new Button { Width = 196, Margin = new Thickness(6), Padding = new Thickness(16, 14, 16, 14), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = $"Add {name} to your desktop" };
            var sp = new StackPanel();
            var head = Ui.Grid("auto,*,auto");
            head.Children.Add(Ui.Icon(kind.Glyph, 15, "T.Accent").With(t => t.VerticalAlignment = VerticalAlignment.Center));
            head.Children.Add(Ui.Icon(Glyph.Add, 12, "T.Text3").At(0, 2));
            sp.Children.Add(head);
            sp.Children.Add(Ui.Text(name, "Text.Body", 13.5, FontWeights.SemiBold).With(t => t.Margin = new Thickness(0, 10, 0, 2)));
            sp.Children.Add(Ui.Text(about, "Text.Caption", 11.5).With(t => { t.Height = 46; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
            card.Content = sp;
            card.Click += (_, _) => { Hub.Widgets.Add(kindId, variant, new Dictionary<string, string> { ["style"] = "bare" }, null, size); RenderPlaced(); };
            matching.Children.Add(card);
        }
        Children.Add(Kit.Section("Matching pieces", "Text-only widgets made for the clock skins: no card, straight on the wallpaper. With Match the clock's lettering on (below), every widget takes the clock's letters: mono, condensed or airy.", matching));

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
            Kit.Row("Material", "What widget cards are made of. Glass frosts your wallpaper, Gradient fades from your accent colour into your wallpaper's colours, Smoked is a deep tint, Solid is the theme's colour, Black is true black. Bare widgets stay straight on the wallpaper.",
                Kit.Segmented(GlassSurface.Materials, Math.Max(0, Array.IndexOf(GlassSurface.Materials, GlassSurface.Material(L.WidgetMaterial))), i => { L.WidgetMaterial = GlassSurface.Materials[i]; Hub.Store.Save(); Hub.Widgets.Apply(); }, "Widget material")),
            Kit.Adv(Kit.ToggleRow("Match the clock's lettering", "Widgets take their letters from your clock: mono with Mono and Dot matrix, condensed with Poster, Label, Week, Heavy and Script, airy with Orbit, Hairline, Spaced and Story.", L.MatchClockLettering, v => { L.MatchClockLettering = v; Hub.Store.Save(); Hub.Widgets.RefreshLettering(); })),
            Kit.ToggleRow("Lock layout", "Prevents accidental moving and resizing.", L.Locked, v => { L.Locked = v; Hub.Store.Save(); }),
            Kit.Adv(Kit.ToggleRow("Snap to grid", "Widgets also snap magnetically to the screen margins.", L.SnapToGrid, v => { L.SnapToGrid = v; Hub.Store.Save(); })),
            Kit.Adv(Kit.SliderRow("Grid size", null, 4, 48, L.GridSize, v => { L.GridSize = (int)v; Hub.Store.Save(); }, v => $"{v:0} px", true, 4)),
            Kit.Adv(Kit.SliderRow("Screen margin", "Space kept clear at the edges of the screen.", 0, 96, L.Margin, v => { L.Margin = (int)v; Hub.Store.Save(); }, v => $"{v:0} px", true, 2)),
            Kit.Adv(Kit.SliderRow("Spacing", "Gap between widgets when arranging.", 0, 48, L.Spacing, v => { L.Spacing = (int)v; Hub.Store.Save(); }, v => $"{v:0} px", true, 2)),
            Kit.Row("Tidy up", "Line every visible widget up in neat columns, along the left or the right of the screen.",
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
            var size = WidgetSizes.Match(w.Width, w.Height) is { } sid ? WidgetSizes.All.First(s => s.Id == sid).Name : $"{w.Width:0}×{w.Height:0}";
            var style = kind.Id == "clock" ? " · " + (ClockWidget.Styles.FirstOrDefault(s => s.Id == w.Variant).Name ?? "Minimal") : "";
            _placed.Children.Add(Kit.Row(kind.Name + style, $"{mon.Label} · {size}{(WidgetWindow.IsBare(w) ? " · bare" : "")}{(w.Pinned ? " · always on top" : "")}", controls, !first, kind.Glyph));
            first = false;
        }
    }

    private void ImportSet()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = $"WinOrbit desktop set|*{DesktopSets.Extension}" };
        if (dlg.ShowDialog() != true) return;
        DesktopSet? set = null;
        try { set = DesktopSets.Import(dlg.FileName); } catch (Exception ex) { Log.Error("Import set failed", ex); }
        if (set is null) { Dialogs.Message("Not a desktop set", "That file isn't a WinOrbit desktop set, or none of its widgets are ones WinOrbit has."); return; }
        if (Dialogs.Confirm("Set imported", $"“{set.Name}” is with your sets now. Use it?", "Use it", "Later", "The widgets on your desktop are replaced. Profiles › Restore previous desktop brings them back."))
            DesktopSets.Apply(set);
    }

    private static void ExportSet(DesktopSet set)
    {
        var safe = string.Concat(set.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = (safe.Length > 0 ? safe : "Desktop set") + DesktopSets.Extension, Filter = $"WinOrbit desktop set|*{DesktopSets.Extension}" };
        if (dlg.ShowDialog() != true) return;
        try { DesktopSets.Export(set, dlg.FileName); }
        catch (Exception ex) { Log.Error("Export set failed", ex); Dialogs.Message("Couldn't save the set", ex.Message); }
    }

    /// <summary>Put a set on the desktop after saying what happens to the widgets there now.</summary>
    internal static bool ConfirmAndApply(DesktopSet set)
    {
        if (Hub.Settings.Widgets.Count > 0 && !Dialogs.Confirm($"Use the {set.Name} set?", "Your widgets are replaced by the set's, and the theme, the island's glance and liquid glass change with it.", "Use it", "Cancel",
                "Profiles › Restore previous desktop brings back what you have now.")) return false;
        DesktopSets.Apply(set);
        return true;
    }

    /// <summary>A set's card: a live miniature of the desktop it makes, its name, what's in it, and its buttons.</summary>
    private static Border SetCard(DesktopSet set, List<WidgetView> previews)
    {
        var active = Hub.Settings.ActiveSetId == set.Id;
        var sp = new StackPanel();
        var stage = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"Use the {set.Name} set", Content = SetPreview(set, 372, previews) };
        stage.Click += (_, _) => ConfirmAndApply(set);
        sp.Children.Add(stage);
        var head = Ui.Grid("*,auto");
        head.Margin = new Thickness(2, 11, 0, 2);
        head.Children.Add(Ui.Text(set.Name, "Text.Body", 14, FontWeights.SemiBold));
        if (active) head.Children.Add(Ui.Text("IN USE", "Text.Label", 9.5).With(t => { t.SetResourceReference(TextBlock.ForegroundProperty, "T.Accent"); t.VerticalAlignment = VerticalAlignment.Center; }).At(0, 1));
        sp.Children.Add(head);
        sp.Children.Add(Ui.Text(set.Description, "Text.Caption", 11.5).With(t => { t.Margin = new Thickness(2, 0, 0, 0); t.Height = 48; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
        var theme = set.ThemeId.Length > 0 ? Hub.Theme.Find(set.ThemeId)?.Name : null;
        var facts = string.Join(" · ", new[] { theme, set.LiquidGlass switch { true => "liquid glass", false => "no liquid glass", _ => null }, $"{set.Widgets.Count} widgets" }.Where(s => s is not null));
        sp.Children.Add(Ui.Text(facts, "Text.Label", 9.5).With(t => { t.Margin = new Thickness(2, 6, 0, 10); t.TextTrimming = TextTrimming.CharacterEllipsis; }));
        var buttons = new List<UIElement> { Kit.Btn(active ? "Use again" : "Use", () => ConfirmAndApply(set), null, !active) };
        buttons.Add(Ui.IconButton(Glyph.Share, "Share this set as a file", () => ExportSet(set)));
        if (!set.BuiltIn)
            buttons.Add(Ui.IconButton(Glyph.Delete, "Delete this set", () =>
            {
                if (Dialogs.Confirm("Delete this set?", $"“{set.Name}” is removed from your sets. Widgets on the desktop stay as they are.", "Delete")) DesktopSets.Delete(set);
            }));
        sp.Children.Add(Kit.Buttons(buttons.ToArray()));
        var card = Kit.Card(sp, new Thickness(12));
        card.Width = 398;
        card.Margin = new Thickness(6);
        if (active) { card.SetResourceReference(Border.BorderBrushProperty, "T.Accent"); card.BorderThickness = new Thickness(1.5); }
        return card;
    }

    /// <summary>
    /// A small picture of the desktop a set makes: your wallpaper with the set's own widgets on it, live, placed the way
    /// <see cref="DesktopSets.Apply"/> places them on your main display and lettered after the set's clock.
    /// </summary>
    internal static FrameworkElement SetPreview(DesktopSet set, double width, List<WidgetView> previews)
    {
        var mon = Monitors.Primary;
        var scale = mon.Scale > 0 ? mon.Scale : 1;
        double dw = Math.Max(320, (mon.Bounds.Right - mon.Bounds.Left) / scale), dh = Math.Max(200, (mon.Bounds.Bottom - mon.Bounds.Top) / scale);
        var work = DesktopSets.WorkArea(mon);
        var workDip = new Rect(work.X / scale, work.Y / scale, work.Width / scale, work.Height / scale);
        var canvas = new Canvas { Width = dw, Height = dh, ClipToBounds = true, IsHitTestVisible = false };
        if (Hub.Wallpaper.Preview(mon.Device) is { } wall)
            canvas.Children.Add(new Image { Source = wall, Stretch = Stretch.UniformToFill, Width = dw, Height = dh });
        else
            canvas.SetResourceReference(Panel.BackgroundProperty, "T.WashStrong");
        // Bare pieces take light or dark ink from the picture, as they do on the desktop.
        WidgetWindow.PutInk(canvas.Resources, WidgetWindow.LightInk(Hub.Wallpaper.Palette.AverageLuminance));

        var keep = WidgetLook.Current;
        WidgetLook.Current = WidgetLook.LetteringFor(WidgetLook.LeadClock(set.Widgets.Select(w => new WidgetInstance { Kind = w.Kind, Variant = w.Variant, Visible = true })));
        try
        {
            var battery = DesktopSets.HasBattery();
            foreach (var w in set.Widgets)
            {
                if (WidgetCatalog.Find(w.Kind) is not { } kind || (w.Kind == "battery" && !battery)) continue;
                WidgetView view;
                try { view = kind.Create(w.Clone()); } catch (Exception ex) { Log.Error($"Set preview: {w.Kind}", ex); continue; }
                view.Width = w.Width;
                view.Height = w.Height;
                if (WidgetWindow.IsBare(w)) view.SetResourceReference(EffectProperty, "W.BareShadow");
                var (x, y) = DesktopSets.ToPixels(w.X, w.Y, w.Width, w.Height, workDip);
                Canvas.SetLeft(view, x);
                Canvas.SetTop(view, y);
                canvas.Children.Add(view);
                previews.Add(view);
            }
        }
        finally { WidgetLook.Current = keep; }

        var height = Math.Round(width * dh / dw);
        var frame = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderThickness = new Thickness(1), Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas } };
        frame.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
        frame.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 10, 10);
        return frame;
    }

    protected override void OnEnter()
    {
        Live(() => DesktopSets.Changed += Rebuild, () => DesktopSets.Changed -= Rebuild);
        Live(() => Hub.Widgets.Changed += RenderPlaced, () => Hub.Widgets.Changed -= RenderPlaced);
        // The previews tick once a minute, and only while this page is open.
        Live(() => { foreach (var p in _previews) p.Start(); }, () => { foreach (var p in _previews) p.Stop(); });
    }
}
