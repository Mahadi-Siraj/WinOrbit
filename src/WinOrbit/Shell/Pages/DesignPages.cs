using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Rainmeter;
using WinOrbit.Services;
using WinOrbit.Wallpaper;
using Microsoft.Win32;

namespace WinOrbit.Shell.Pages;

/// <summary>
/// A miniature of the desktop: the real wallpaper with glass surfaces whose backdrop is the matching slice
/// of the blurred wallpaper, so previews show exactly how the material will look on screen.
/// </summary>
public sealed class PreviewStage : Grid
{
    private readonly Image _wall = new() { Stretch = Stretch.UniformToFill };
    private readonly Canvas _layer = new();

    public PreviewStage()
    {
        ClipToBounds = true;
        Children.Add(new Border { Background = Brushes.Black });
        Children.Add(_wall);
        Children.Add(_layer);
        Loaded += (_, _) => { Hub.Wallpaper.BackdropsChanged += Refresh; Hub.Theme.Applied += Layout_; Refresh(); };
        Unloaded += (_, _) => { Hub.Wallpaper.BackdropsChanged -= Refresh; Hub.Theme.Applied -= Layout_; };
        SizeChanged += (_, _) => Layout_();
    }

    /// <summary>Add a glass surface at a relative position (0..1) and size within the stage.</summary>
    public GlassSurface AddGlass(double rx, double ry, double rw, double rh, UIElement? content = null, double radius = -1)
    {
        var g = new GlassSurface { Content = content, Tag = new Rect(rx, ry, rw, rh), ShadowSize = 18 };
        if (radius >= 0) g.CornerRadius = new CornerRadius(radius);
        _layer.Children.Add(g);
        Layout_();
        return g;
    }

    private void Refresh()
    {
        _wall.Source = Hub.Wallpaper.Preview(Monitors.Primary.Device);
        Layout_();
    }

    private void Layout_()
    {
        if (ActualWidth < 2 || ActualHeight < 2) return;
        var blurred = Hub.Wallpaper.Backdrop(Monitors.Primary.Device);
        // The wallpaper image is shown UniformToFill; work out which part of it is visible.
        double imgAspect = blurred is null ? 16 / 9.0 : (double)blurred.PixelWidth / blurred.PixelHeight, stageAspect = ActualWidth / ActualHeight;
        double vx = 0, vy = 0, vw = 1, vh = 1;
        if (imgAspect > stageAspect) { vw = stageAspect / imgAspect; vx = (1 - vw) / 2; } else { vh = imgAspect / stageAspect; vy = (1 - vh) / 2; }
        var refraction = TryFindResource("G.Refraction") as double? ?? 0;
        foreach (var g in _layer.Children.OfType<GlassSurface>())
        {
            var r = (Rect)g.Tag;
            g.Width = r.Width * ActualWidth; g.Height = r.Height * ActualHeight;
            Canvas.SetLeft(g, r.X * ActualWidth); Canvas.SetTop(g, r.Y * ActualHeight);
            if (blurred is null) { g.Backdrop = null; continue; }
            var box = new Rect(vx + r.X * vw, vy + r.Y * vh, r.Width * vw, r.Height * vh);
            box = new Rect(box.X + box.Width * refraction / 2, box.Y + box.Height * refraction / 2, box.Width * (1 - refraction), box.Height * (1 - refraction));
            g.Backdrop = new ImageBrush(blurred) { Viewbox = box, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
        }
    }
}

// =================================================================================================
public sealed class HomePage : PageBase
{
    public override string Title => Greeting();
    public override string Subtitle => "Your desktop at a glance. Everything here runs locally — nothing leaves this PC.";
    private readonly Dictionary<string, TextBlock> _stats = new();
    private Sparkline? _cpuSpark;
    private TextBlock? _mediaTitle, _mediaArtist;

    private static string Greeting() => DateTime.Now.Hour switch { < 5 => "Good night", < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };

    /// <summary>True while the first-start flow fills the page instead of the dashboard.</summary>
    private bool _welcome;

    protected override void Build()
    {
        // First start: the three-step look picker takes the whole page until Finish or Skip setup.
        _welcome = !Hub.Settings.General.WelcomeDone;
        if (_welcome)
        {
            Children.Add(new WelcomeFlow(() => Owner, () => { Rebuild(); OnEnter(); }));
            return;
        }

        // ---------- updates: a new version, or the one-time question ----------
        Children.Add(_updates);
        ShowUpdates();

        // ---------- hero ----------
        var hero = Ui.Grid("2*,16,*", "auto");
        hero.Margin = new Thickness(0, 0, 0, 16);

        var stage = new PreviewStage { MinHeight = 300 };
        var pill = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pill.Children.Add(Ui.Icon(Glyph.Music, 10, "T.Accent").With(t => t.Margin = new Thickness(0, 0, 6, 0)));
        pill.Children.Add(Ui.Text(DateTime.Now.ToString("HH:mm"), "Text.Body", 10.5, FontWeights.SemiBold));
        stage.AddGlass(0.39, 0.035, 0.22, 0.08, pill, 13);
        var clock = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
        clock.Children.Add(Ui.Text(DateTime.Now.ToString("HH:mm"), "Text.Display", 30));
        clock.Children.Add(Ui.Text(DateTime.Now.ToString("dddd d MMMM", CultureInfo.CurrentCulture), "Text.Caption", 11));
        stage.AddGlass(0.66, 0.2, 0.3, 0.25, clock, 14);
        var bars = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };
        foreach (var (l, v) in new[] { ("CPU", 34.0), ("RAM", 58.0), ("GPU", 21.0) })
        {
            bars.Children.Add(Ui.Text(l, "Text.Caption", 9.5));
            bars.Children.Add(new ProgressBar { Value = v, Margin = new Thickness(0, 3, 0, 6), Height = 3 });
        }
        stage.AddGlass(0.66, 0.5, 0.3, 0.32, bars, 14);
        var heroCard = new Border { CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = stage, BorderThickness = new Thickness(1) };
        heroCard.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
        heroCard.Clip = null;
        var clip = new Border { CornerRadius = new CornerRadius(20) };
        heroCard.Loaded += (_, _) => heroCard.Clip = new RectangleGeometry(new Rect(0, 0, heroCard.ActualWidth, heroCard.ActualHeight), 20, 20);
        heroCard.SizeChanged += (_, _) => heroCard.Clip = new RectangleGeometry(new Rect(0, 0, heroCard.ActualWidth, heroCard.ActualHeight), 20, 20);
        hero.Children.Add(heroCard);

        // quick controls
        var quick = new StackPanel();
        quick.Children.Add(Ui.Text("Quick controls", "Text.Sub").With(t => t.Margin = new Thickness(0, 0, 0, 6)));
        quick.Children.Add(Kit.ToggleRow("Dynamic Island", null, Hub.Settings.Island.Enabled, v => { Hub.Settings.Island.Enabled = v; Hub.Store.Save(); Hub.Island.Apply(); }, false, Glyph.More));
        quick.Children.Add(Kit.ToggleRow("Desktop widgets", null, Hub.Settings.Layout.WidgetsVisible, v => { Hub.Settings.Layout.WidgetsVisible = v; Hub.Store.Save(); Hub.Widgets.Apply(); }, true, Glyph.Tiles));
        quick.Children.Add(Kit.ToggleRow("Wallpaper colours", null, Hub.Settings.WallpaperAutoExtract, v => { Hub.Settings.WallpaperAutoExtract = v; Hub.Store.Save(); Hub.Theme.Apply(); }, true, Glyph.Color));
        var modes = new[] { ThemeMode.Auto, ThemeMode.Dark, ThemeMode.Light };
        quick.Children.Add(Ui.Text("MODE", "Text.Label").With(t => t.Margin = new Thickness(0, 14, 0, 8)));
        quick.Children.Add(Kit.Segmented(new[] { "Auto", "Dark", "Light" }, Math.Max(0, Array.IndexOf(modes, Hub.Theme.Current.Mode)), i => Hub.Theme.Edit(t => t.Mode = modes[i]), "Theme mode"));
        quick.Children.Add(Ui.Text("PALETTE", "Text.Label").With(t => t.Margin = new Thickness(0, 16, 0, 8)));
        quick.Children.Add(PaletteStrip(26));
        hero.Children.Add(Kit.Card(quick, new Thickness(20, 16, 20, 18)).At(0, 2));
        Children.Add(hero);

        // ---------- live row ----------
        var row = Ui.Grid("*,16,*,16,*", "auto");
        row.Margin = new Thickness(0, 0, 0, 16);

        var sys = new StackPanel();
        sys.Children.Add(Header(Glyph.Chip, "System", () => Owner?.Navigate("widgets")));
        var sg = Ui.Grid("*,*,*", "auto");
        int col = 0;
        foreach (var (id, label) in new[] { ("cpu", "CPU"), ("ram", "MEMORY"), ("gpu", "GPU") })
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(label, "Text.Label", 10));
            var v = Ui.Text("—", "Text.Title", 22);
            _stats[id] = v;
            sp.Children.Add(v);
            sg.Children.Add(sp.At(0, col++));
        }
        sys.Children.Add(sg);
        _cpuSpark = new Sparkline { Height = 38, Margin = new Thickness(0, 10, 0, 0) };
        sys.Children.Add(_cpuSpark);
        row.Children.Add(Kit.Card(sys, new Thickness(20, 16, 20, 16)));

        var media = new StackPanel();
        media.Children.Add(Header(Glyph.Music, "Now playing", null));
        _mediaTitle = Ui.Text("", "Text.Sub", 15).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; });
        _mediaArtist = Ui.Text("", "Text.Caption", 12.5).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; });
        media.Children.Add(_mediaTitle);
        media.Children.Add(_mediaArtist);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 10, 0, 0) };
        controls.Children.Add(Ui.IconButton(Glyph.Previous, "Previous", () => Hub.Media.Previous()));
        controls.Children.Add(Ui.IconButton(Glyph.Play, "Play or pause", () => Hub.Media.PlayPause(), 36, "Button.IconAccent").With(b => b.Margin = new Thickness(4, 0, 4, 0)));
        controls.Children.Add(Ui.IconButton(Glyph.Next, "Next", () => Hub.Media.Next()));
        media.Children.Add(controls);
        row.Children.Add(Kit.Card(media, new Thickness(20, 16, 20, 16)).At(0, 2));

        // Third card: Rainmeter for people who use it, otherwise a focus card.
        var r = Hub.Rainmeter;
        var third = new StackPanel();
        if (r.PageWanted() && r.Installed)
        {
            third.Children.Add(Header(Glyph.Diagnostic, "Rainmeter", () => Owner?.Navigate("rainmeter")));
            if (r.Skins.Count == 0) r.Scan();
            third.Children.Add(Ui.Text($"{r.Skins.Count(s => s.Active)} active skin{(r.Skins.Count(s => s.Active) == 1 ? "" : "s")}", "Text.Sub", 15));
            third.Children.Add(Ui.Text($"{r.Skins.Count} installed · version {r.Version}", "Text.Caption", 12.5));
            third.Children.Add(Kit.Chip(RainmeterService.IsRunning ? "Running" : "Not running", RainmeterService.IsRunning ? "T.Good" : "T.Warn").With(c => c.Margin = new Thickness(0, 12, 0, 0)));
        }
        else
        {
            var timers = Hub.Timers;
            third.Children.Add(Header(Glyph.Stopwatch, "Focus", () => Owner?.Navigate("island")));
            var headline = Ui.Text("", "Text.Sub", 15);
            var detail = Ui.Text("", "Text.Caption", 12.5);
            var button = Kit.Btn("", () => { }, Glyph.Play, true).With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); });
            void Refresh()
            {
                var focusing = timers.Running && timers.Mode == TimerMode.Focus;
                headline.Text = focusing ? $"{timers.Display} left" : $"{timers.MinutesToday} min today";
                detail.Text = focusing ? "Focus block running — the island shows the countdown." : $"{timers.CompletedToday} focus block{(timers.CompletedToday == 1 ? "" : "s")} completed today";
                button.Content = focusing ? "Stop" : $"Start {timers.FocusMinutes}-min focus";
            }
            button.Click += (_, _) =>
            {
                if (timers.Running && timers.Mode == TimerMode.Focus) timers.Stop();
                else { timers.SetMode(TimerMode.Focus); timers.Start(); }
                Refresh();
            };
            Action onTick = () => Refresh();
            third.Loaded += (_, _) => timers.Changed += onTick;
            third.Unloaded += (_, _) => timers.Changed -= onTick;
            Refresh();
            third.Children.Add(headline);
            third.Children.Add(detail);
            third.Children.Add(button);
        }
        row.Children.Add(Kit.Card(third, new Thickness(20, 16, 20, 16)).At(0, 4));
        Children.Add(row);

        // ---------- profiles ----------
        var profiles = new WrapPanel();
        foreach (var p in Hub.Profiles.All)
        {
            var active = p.Id == Hub.Settings.ActiveProfileId;
            var b = new Button { Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 9, 16, 9) };
            if (active) b.SetResourceReference(StyleProperty, "Button.Accent");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = p.Glyph, FontFamily = (FontFamily)FindResource("F.Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
            sp.Children.Add(new TextBlock { Text = p.Name, VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
            b.ToolTip = p.Description;
            b.Click += (_, _) => { Hub.Profiles.Apply(p); Rebuild(); OnEnter(); };
            profiles.Children.Add(b);
        }
        Children.Add(Kit.Section("Profiles", "Switch your whole desktop — theme, island, widgets and motion — in one click.", profiles));

        // ---------- privacy ----------
        var net = Hub.Settings.Privacy.NetworkAllowed;
        Children.Add(Kit.Notice(Glyph.Shield, net ? "Optional internet features are enabled" : "Private by design",
            net ? $"Only the features you enabled may connect. {Hub.Net.RequestCount} request(s) this session — every one is listed under About & Diagnostics."
                : "No telemetry, no analytics, no accounts. Network access is off, so WinOrbit makes no internet connections at all.",
            net ? "T.Warn" : "T.Good", Kit.Btn("Review", () => Owner?.Navigate("about"))));
    }

    private readonly StackPanel _updates = new();

    private void ShowUpdates()
    {
        _updates.Children.Clear();
        var p = Hub.Settings.Privacy;
        if (Updates.Available is { } v)
        {
            var n = v.ToString(3);
            _updates.Children.Add(Kit.Notice(Glyph.Download, $"WinOrbit {n} is out",
                Paths.Portable ? $"You have {Updates.Norm(Updates.Current).ToString(3)}. Download the new portable zip, close WinOrbit and unzip it over this copy; your settings are kept."
                               : $"You have {Updates.Norm(Updates.Current).ToString(3)}. Download the installer and run it; your settings are kept.",
                "T.Accent", Kit.Buttons(
                    Kit.Btn("Skip this version", () => { Updates.Skip(); ShowUpdates(); }),
                    Kit.Btn("What's new", () => Island.IslandWindow.OpenUri(Updates.ReleasesUrl)),
                    Kit.Btn("Download", () => Island.IslandWindow.OpenUri(Updates.DownloadUrl(v, Paths.Portable)), Glyph.Download, true))));
        }
        else if (!p.UpdateAsked)
        {
            _updates.Children.Add(Kit.Notice(Glyph.Download, "Get told when there's an update?",
                "Every 48 hours WinOrbit would ask GitHub for the latest version number, and show a card with a direct download link when there's a new one. Nothing else is sent and nothing installs by itself. Change it any time in Settings › Privacy & network.",
                "T.Accent", Kit.Buttons(
                    Kit.Btn("No thanks", () => { Updates.SetAutomatic(false); ShowUpdates(); }),
                    Kit.Btn("Yes, tell me", () => { Updates.SetAutomatic(true); ShowUpdates(); }, Glyph.Check, true))));
        }
        _updates.Margin = _updates.Children.Count > 0 ? new Thickness(0, 0, 0, 16) : new Thickness(0);
    }

    private Grid Header(string glyph, string title, Action? more)
    {
        var g = Ui.Grid("auto,*,auto");
        g.Margin = new Thickness(0, 0, 0, 10);
        g.Children.Add(Ui.Icon(glyph, 14, "T.Accent").With(t => t.Margin = new Thickness(0, 0, 9, 0)));
        g.Children.Add(Ui.Text(title, "Text.Body", 13.5, FontWeights.SemiBold).At(0, 1));
        if (more is not null) g.Children.Add(Ui.IconButton(Glyph.Right, "Open " + title, more, 26).At(0, 2).With(b => b.Margin = new Thickness(0, -4, -6, -4)));
        return g;
    }

    public static WrapPanel PaletteStrip(double size, Action<Color>? click = null)
    {
        var wrap = new WrapPanel();
        var p = Hub.Wallpaper.Palette;
        foreach (var c in new[] { p.Accent, p.Secondary, p.Complement }.Concat(p.Swatches).Distinct().Take(7))
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 6), ToolTip = ColorMath.Hex(c), BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
            if (click is not null) { b.Cursor = Cursors.Hand; b.MouseLeftButtonUp += (_, _) => click(c); }
            wrap.Children.Add(b);
        }
        return wrap;
    }

    protected override void OnEnter()
    {
        if (_welcome) return;
        Live(Hub.Monitor.Subscribe(s =>
        {
            _stats["cpu"].Text = $"{s.Cpu:0}%";
            _stats["ram"].Text = $"{s.Ram:0}%";
            _stats["gpu"].Text = s.Gpu is { } g ? $"{g:0}%" : "n/a";
            _cpuSpark?.Update(Hub.Monitor.CpuHistory);
        }));
        void Media()
        {
            var m = Hub.Media;
            var has = m.HasTrack;
            if (_mediaTitle is null || _mediaArtist is null) return;
            _mediaTitle.Text = has ? m.DisplayTitle : "Nothing playing";
            _mediaArtist.Text = has ? $"{m.Artist}{(m.Source.Length > 0 ? " · " + m.Source : "")}" : "Media from any app appears here";
        }
        Media();
        Live(() => Hub.Media.Changed += Media, () => Hub.Media.Changed -= Media);
        Live(() => Updates.AvailableChanged += ShowUpdates, () => Updates.AvailableChanged -= ShowUpdates);
    }
}

// =================================================================================================
public sealed class AppearancePage : PageBase
{
    public override string Title => "Appearance";
    public override string Subtitle => "Choose a theme, set dark and light behaviour, accent colours, typography and motion. Changes apply instantly.";

    private static readonly string[] Fonts = { "Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Cascadia Code", "Inter", "Aptos", "Selawik" };

    protected override void Build()
    {
        Children.Add(Kit.Section("Themes", "Built-in themes are tuned for both dark and light. Editing one makes your own copy, so the originals are always available.", ThemeGallery()));

        var t = Hub.Theme.Current;
        var modes = new[] { ThemeMode.Auto, ThemeMode.Dark, ThemeMode.Light, ThemeMode.Scheduled };
        var schedule = Kit.Buttons(
            Ui.Text("Light at", "Text.Caption").With(x => x.VerticalAlignment = VerticalAlignment.Center),
            TimeBox(t.LightAt, v => Hub.Theme.Edit(x => x.LightAt = v)),
            Ui.Text("Dark at", "Text.Caption").With(x => { x.VerticalAlignment = VerticalAlignment.Center; x.Margin = new Thickness(14, 0, 0, 0); }),
            TimeBox(t.DarkAt, v => Hub.Theme.Edit(x => x.DarkAt = v)));
        var scheduleRow = Kit.Row("Schedule", "Switch at fixed times each day.", schedule);
        scheduleRow.Visibility = t.Mode == ThemeMode.Scheduled ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(Kit.Section("Mode", null,
            Kit.Row("Colour mode", "Auto follows Windows' app mode and updates the moment you change it.",
                Kit.Segmented(new[] { "Auto", "Dark", "Light", "Scheduled" }, Math.Max(0, Array.IndexOf(modes, t.Mode)), i =>
                {
                    Hub.Theme.Edit(x => x.Mode = modes[i]);
                    scheduleRow.Visibility = modes[i] == ThemeMode.Scheduled ? Visibility.Visible : Visibility.Collapsed;
                }, "Colour mode"), false),
            scheduleRow));

        var darkAccent = new ColorField(ColorMath.Parse(t.AccentDark), "Dark mode accent");
        darkAccent.Changed += c => Hub.Theme.Edit(x => { x.AccentDark = ColorMath.Hex(c); x.AccentFromWallpaper = false; });
        var lightAccent = new ColorField(ColorMath.Parse(t.AccentLight), "Light mode accent");
        lightAccent.Changed += c => Hub.Theme.Edit(x => { x.AccentLight = ColorMath.Hex(c); x.AccentFromWallpaper = false; });
        var custom = new StackPanel { Visibility = t.AccentFromWallpaper ? Visibility.Collapsed : Visibility.Visible };
        custom.Children.Add(Kit.Row("Dark mode accent", null, darkAccent));
        custom.Children.Add(Kit.Row("Light mode accent", null, lightAccent));
        Children.Add(Kit.Section("Accent colour", "The accent appears on controls, progress, selections and the glow inside glass.",
            Kit.ToggleRow("Match my wallpaper", "Pick the accent from the wallpaper automatically and update it when the wallpaper changes.", t.AccentFromWallpaper, v =>
            {
                Hub.Theme.Edit(x => x.AccentFromWallpaper = v);
                custom.Visibility = v ? Visibility.Collapsed : Visibility.Visible;
            }, false),
            custom,
            Kit.Row("Use a wallpaper colour", "Click a swatch to make it your accent.", HomePage.PaletteStrip(24, c => Hub.Theme.Edit(x =>
            {
                x.AccentFromWallpaper = false; x.AccentDark = ColorMath.Hex(ColorMath.ForMode(c, true)); x.AccentLight = ColorMath.Hex(ColorMath.ForMode(c, false));
                darkAccent.Color = ColorMath.Parse(x.AccentDark); lightAccent.Color = ColorMath.Parse(x.AccentLight); custom.Visibility = Visibility.Visible;
            })))));

        var fontIndex = Math.Max(0, Array.FindIndex(Fonts, f => f == t.FontFamily));
        var installed = new HashSet<string>(System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
        var available = Fonts.Where(f => installed.Contains(f) || f.StartsWith("Segoe UI")).ToList();
        Children.Add(Kit.Adv(Kit.Section("Typography & shape", null,
            Kit.Row("Typeface", "Fonts installed on this PC.", Kit.Combo(available, Math.Max(0, available.IndexOf(t.FontFamily)), i => Hub.Theme.Edit(x => x.FontFamily = available[i])), false),
            Kit.SliderRow("Text size", null, 0.85, 1.3, t.FontScale, v => Hub.Theme.Edit(x => x.FontScale = v), v => $"{v * 100:0}%"),
            Kit.SliderRow("Corner radius", "Roundness of cards, widgets and panels.", 0, 36, t.CornerRadius, v => Hub.Theme.Edit(x => x.CornerRadius = Math.Round(v)), v => $"{v:0} px"))));

        Children.Add(Kit.Section("Motion", null,
            Kit.ToggleRow("Reduce motion", "Removes movement everywhere in WinOrbit (the island, widgets and this window) without changing the look.", Hub.Settings.General.ReducedMotion, v =>
            {
                Hub.Settings.General.ReducedMotion = v; Hub.Store.Save(); Hub.Theme.Apply();
            }, false),
            Kit.Adv(Kit.SliderRow("Animation intensity", "How lively transitions feel. 0 turns animations off.", 0, 1.5, t.Animation, v => Hub.Theme.Edit(x => x.Animation = v), v => v < 0.05 ? "Off" : $"{v * 100:0}%"))));

        var materials = new[] { "acrylic", "mica", "glass" };
        var lens = HasLiquidLook(t);
        Children.Add(Kit.Section("Glass", null,
            Kit.ToggleRow("Liquid glass", !Hub.Settings.General.CurvedGlass
                    ? "Off because Curved glass edges is off. Turning liquid glass on turns curved edges back on."
                    : lens ?"A thick, rounded rim that bends your wallpaper round the edges of the island, widgets and launcher, and moves when the glass does. Off keeps a thin, calm frosted edge."
                    : $"This look has a thin, calm edge. Turning liquid glass on switches to {Hub.Theme.Find(ThemeEngine.LiquidFallback)?.Name ?? "a liquid look"}, with a thick rim that bends your wallpaper.",
                Hub.Theme.LiquidShowing, v => { Hub.Theme.SetLiquidGlass(v); Rebuild(); }, false, Glyph.Drop),
            Kit.Adv(Kit.ToggleRow("Curved glass edges", "The wallpaper bends round the rim of the island, widgets and launcher like thick glass, and the edge catches the light under the pointer. Uses the graphics card; turns itself off if WinOrbit falls back to software rendering.",
                Hub.Settings.General.CurvedGlass, v => { Hub.Settings.General.CurvedGlass = v; Hub.Store.Save(); Hub.Theme.Apply(); Rebuild(); })),
            Kit.Row("Surface", "Glass isn't for everyone. Pick what the island and widgets are made of: Glass, Gradient (a soft fade from your accent colour into your wallpaper's colours), Smoked, Solid or plain Black. You can still set them apart under Dynamic Island and Widgets.",
                SurfacePicker(), true, Glyph.Color),
            LiveGlassRow()));
        Children.Add(Kit.Adv(Kit.Section("Window material", "The material behind this settings window.",
            Kit.Row("Backdrop", "Acrylic blurs what is behind the window, Mica tints from the wallpaper, Glass uses WinOrbit's own frosted wallpaper.",
                Kit.Segmented(new[] { "Acrylic", "Mica", "Glass" }, Math.Max(0, Array.IndexOf(materials, Hub.Settings.General.WindowMaterial)), i =>
                {
                    Hub.Settings.General.WindowMaterial = materials[i]; Hub.Store.Save(); Owner?.RefreshBackdrop();
                }), false))));
    }

    /// <summary>Whether the look on now shows the liquid lens when liquid glass is on.</summary>
    private static bool HasLiquidLook(Theme t) => ThemeEngine.HasLiquid(t);

    /// <summary>One surface for the island and the widgets together (each can still be set on its own page).</summary>
    internal static FrameworkElement SurfacePicker()
    {
        var m = GlassSurface.Materials;
        return Kit.Segmented(m, Math.Max(0, Array.IndexOf(m, GlassSurface.Material(Hub.Settings.Island.Material))), i => SetSurface(m[i]), "Surface");
    }

    internal static void SetSurface(string material)
    {
        Hub.Settings.Island.Material = material;
        Hub.Settings.Layout.WidgetMaterial = material;
        Hub.Store.Save();
        Hub.Island.Apply();
        Hub.Widgets.Apply();
    }

    /// <summary>The Live glass switch, here and on the first start.</summary>
    internal static FrameworkElement LiveGlassRow(bool divider = true) =>
        Kit.ToggleRow("Live glass", LiveGlass.Supported
                ? "The island, widgets and launcher show the windows behind them, blurred, instead of only your wallpaper. WinOrbit watches the screen with Windows' screen-capture API, only while its glass is showing and only when something changes. The picture is shrunk to an eighth on the graphics card (a quarter for nearly clear looks such as Liquid Clear) and blurred, kept in memory, never saved or sent. "
                  + "While this is on, WinOrbit's island, widgets and launcher are left out of screenshots, recordings and screen sharing (otherwise the glass would capture itself). Only matters for the Glass surface. Off by default."
                  + (Hub.LiveGlass?.Problem is { } problem ? " Currently off: " + problem : "")
                : "Needs Windows 11: earlier versions mark a captured screen with a yellow frame.",
            Hub.Settings.General.LiveGlass, v => { Hub.Settings.General.LiveGlass = v; Hub.Store.Save(); Hub.LiveGlass?.Apply(); }, divider, Glyph.View)
            .With(r => r.IsEnabled = LiveGlass.Supported);

    private static TextBox TimeBox(string value, Action<string> changed)
    {
        var tb = new TextBox { Text = value, Width = 70, TextAlignment = TextAlignment.Center };
        tb.LostFocus += (_, _) =>
        {
            if (TimeSpan.TryParse(tb.Text, CultureInfo.InvariantCulture, out var ts) && ts < TimeSpan.FromDays(1)) { tb.Text = $"{ts.Hours:00}:{ts.Minutes:00}"; changed(tb.Text); }
            else tb.Text = value;
        };
        return tb;
    }

    private WrapPanel ThemeGallery()
    {
        var wrap = Kit.Gallery();
        foreach (var theme in Hub.Theme.All)
        {
            var active = theme.Id == Hub.Theme.Current.Id;
            var card = new Border { Width = 214, Margin = new Thickness(6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(active ? 2 : 1), Cursor = Cursors.Hand, Padding = new Thickness(0) };
            card.SetResourceReference(Border.BorderBrushProperty, active ? "T.Accent" : "T.Stroke");
            card.SetResourceReference(Border.BackgroundProperty, "T.Card");
            var sp = new StackPanel();
            sp.Children.Add(Swatch(theme));
            var text = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            var title = Ui.Grid("*,auto");
            title.Children.Add(Ui.Text(theme.Name, "Text.Body", 13.5, FontWeights.SemiBold).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis));
            if (active) title.Children.Add(Ui.Icon(Glyph.Check, 12, "T.Accent").At(0, 1));
            text.Children.Add(title);
            text.Children.Add(Ui.Text(theme.BuiltIn ? theme.Description : (theme.BasedOn.Length > 0 ? "Your theme · based on " + (Presets.All.FirstOrDefault(p => p.Id == theme.BasedOn)?.Name ?? "a preset") : "Your theme"), "Text.Caption", 11.5)
                .With(x => { x.MaxHeight = 34; x.TextTrimming = TextTrimming.CharacterEllipsis; x.Margin = new Thickness(0, 3, 0, 0); }));
            sp.Children.Add(text);
            card.Child = sp;
            card.MouseLeftButtonUp += (_, _) => { Hub.Theme.Use(theme.Id); Rebuild(); };
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "T.CardHover");
            card.MouseLeave += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "T.Card");
            System.Windows.Automation.AutomationProperties.SetName(card, "Theme " + theme.Name);
            wrap.Children.Add(card);
        }
        return wrap;
    }

    /// <summary>A small rendering of a theme: its gradient over the wallpaper with its dark tint, plus a light chip.</summary>
    private static Grid Swatch(Theme t)
    {
        var g = new Grid { Height = 96 };
        var clip = new RectangleGeometry(new Rect(0, 0, 214, 96), 15, 15);
        g.Clip = new CombinedGeometry(GeometryCombineMode.Intersect, clip, new RectangleGeometry(new Rect(0, 0, 214, 200)));
        var wall = Hub.Wallpaper.Backdrop(Monitors.Primary.Device);
        if (wall is not null) g.Children.Add(new Image { Source = wall, Stretch = Stretch.UniformToFill });
        var stops = t.Gradient.FromWallpaper || t.Gradient.Stops.Count < 2 ? Hub.Wallpaper.Palette.ToGradientStops() : t.Gradient.Stops;
        g.Children.Add(new Border { Background = ThemeEngine.BuildGradient(t.Gradient, stops, Math.Min(1, t.Gradient.Opacity + 0.25)) });
        var dark = t.Mode != ThemeMode.Light;
        var p = dark ? t.Dark : t.Light;
        var tint = ColorMath.Parse(p.Tint).Alpha(Math.Max(0.35, p.TintOpacity));
        var chip = new Border { Width = 110, Height = 46, CornerRadius = new CornerRadius(Math.Min(16, t.CornerRadius * 0.6)), Background = new SolidColorBrush(tint), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, 14), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.White.Alpha(0.25 + p.BorderIntensity * 0.35)) };
        var accent = t.AccentFromWallpaper ? ColorMath.ForMode(Hub.Wallpaper.Palette.Accent, dark) : ColorMath.Parse(dark ? t.AccentDark : t.AccentLight);
        var inner = new StackPanel { Margin = new Thickness(10, 9, 10, 0) };
        inner.Children.Add(new Border { Height = 5, Width = 54, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush((dark ? Colors.White : Colors.Black).Alpha(0.7)), HorizontalAlignment = HorizontalAlignment.Left });
        inner.Children.Add(new Border { Height = 5, Width = 76, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) });
        chip.Child = inner;
        g.Children.Add(chip);
        g.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 14) });
        return g;
    }
}

// =================================================================================================
public sealed class StudioPage : PageBase
{
    public override string Title => "Liquid Glass Editor";
    public override string Subtitle => "Shape the glass material itself. The preview uses your real wallpaper; the island and widgets update live as you drag.";

    private bool _editDark = Hub.Theme.IsDark;
    private StackPanel? _sliders;
    private Button? _undo, _redo;
    private TextBlock? _name;

    protected override void Build()
    {
        var layout = Ui.Grid("*,18,360", "auto");

        // ----- preview -----
        var left = new StackPanel();
        var stage = new PreviewStage { Height = 380 };
        var island = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        island.Children.Add(new Equalizer().With(e => { e.Margin = new Thickness(0, 0, 8, 0); e.Set(true); }));
        island.Children.Add(Ui.Text("Midnight City", "Text.Body", 11, FontWeights.SemiBold));
        island.Children.Add(Ui.Text("  ·  21:48", "Text.Caption", 11));
        stage.AddGlass(0.34, 0.04, 0.32, 0.085, island, 15);
        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        panel.Children.Add(Ui.Text("Liquid Glass", "Text.Title", 20));
        panel.Children.Add(Ui.Text("Translucent, layered and lit from above.", "Text.Caption", 12).With(t => t.Margin = new Thickness(0, 3, 0, 12)));
        panel.Children.Add(new ProgressBar { Value = 64, Maximum = 100 });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        chips.Children.Add(new Button { Content = "Primary", FontSize = 11.5, Padding = new Thickness(12, 5, 12, 5) }.With(b => b.SetResourceReference(StyleProperty, "Button.Accent")));
        chips.Children.Add(new Button { Content = "Secondary", FontSize = 11.5, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(chips);
        stage.AddGlass(0.06, 0.24, 0.48, 0.48, panel);
        var widget = new StackPanel { Margin = new Thickness(16, 12, 16, 12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        widget.Children.Add(Ui.Text("09:41", "Text.Display", 34).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        widget.Children.Add(Ui.Text("Tuesday, 17 November", "Text.Caption", 11).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        stage.AddGlass(0.6, 0.24, 0.34, 0.27, widget);
        var ring = new Grid { Margin = new Thickness(14) };
        ring.Children.Add(new Ring { Value = 0.68, Thickness = 6 });
        ring.Children.Add(Ui.Text("18:24", "Text.Sub", 15).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }));
        stage.AddGlass(0.6, 0.56, 0.16, 0.27, ring);
        stage.AddGlass(0.78, 0.56, 0.16, 0.27, new Viewbox { Margin = new Thickness(22), Child = Ui.Icon(Glyph.Play, 20, "T.Accent") });
        var stageCard = new Border { CornerRadius = new CornerRadius(20), Child = stage, BorderThickness = new Thickness(1) };
        stageCard.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
        stageCard.SizeChanged += (_, _) => stageCard.Clip = new RectangleGeometry(new Rect(0, 0, stageCard.ActualWidth, stageCard.ActualHeight), 20, 20);
        left.Children.Add(stageCard);

        // toolbar under the preview
        var bar = Ui.Grid("*,auto", "auto");
        bar.Margin = new Thickness(0, 14, 0, 0);
        var nameSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameSp.Children.Add(Ui.Text("EDITING", "Text.Label"));
        _name = Ui.Text(Hub.Theme.Current.Name, "Text.Sub", 15);
        nameSp.Children.Add(_name);
        bar.Children.Add(nameSp);
        _undo = Ui.IconButton(Glyph.Undo, "Undo (Ctrl+Z)", () => { Hub.Theme.Undo(); RefreshAll(); });
        _redo = Ui.IconButton(Glyph.Redo, "Redo (Ctrl+Y)", () => { Hub.Theme.Redo(); RefreshAll(); });
        var tools = Kit.Buttons(_undo, _redo,
            Kit.Btn("Reset", () => { if (Dialogs.Confirm("Reset to default?", "This returns the glass, colours and shape to the original built-in values. You can undo it.", "Reset")) { Hub.Theme.ResetToBase(); RefreshAll(); } }, Glyph.Refresh),
            Kit.Btn("Save as…", SaveAs, Glyph.Save),
            Kit.Btn("More", ShowMore, Glyph.More));
        bar.Children.Add(tools.At(0, 1));
        left.Children.Add(bar);
        left.Children.Add(Kit.Notice(Glyph.Info, "Edits are saved automatically",
            "Built-in themes are never changed — your first edit creates a custom copy you can rename, export or delete.", "T.Accent").With(n => n.Margin = new Thickness(0, 16, 0, 0)));
        layout.Children.Add(left);

        // ----- controls -----
        var right = new StackPanel();
        right.Children.Add(Kit.Segmented(new[] { "Dark glass", "Light glass" }, _editDark ? 0 : 1, i => { _editDark = i == 0; BuildSliders(); }, "Edit dark or light glass")
            .With(s => s.Margin = new Thickness(0, 0, 0, 12)));
        _sliders = new StackPanel();
        right.Children.Add(_sliders);
        layout.Children.Add(right.At(0, 2));
        Children.Add(layout);
        BuildSliders();
    }

    private void BuildSliders()
    {
        if (_sliders is null) return;
        _sliders.Children.Clear();
        var g = _editDark ? Hub.Theme.Current.Dark : Hub.Theme.Current.Light;
        void E(Action<GlassParams> a) => Hub.Theme.Edit(t => a(_editDark ? t.Dark : t.Light));
        StackPanel S(string label, string hint, double min, double max, double val, Action<double> set, Func<double, string> fmt)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var head = Ui.Grid("*,auto");
            head.Children.Add(Ui.Text(label, "Text.Body", 13.5));
            sp.Children.Add(head);
            var slider = Kit.Slider(min, max, val, set, fmt, 260);
            sp.Children.Add(slider);
            sp.ToolTip = hint;
            return sp;
        }
        var tint = new ColorField(ColorMath.Parse(g.Tint), "Glass tint");
        tint.Changed += c => E(p => p.Tint = ColorMath.Hex(c));
        var material = new StackPanel();
        material.Children.Add(Kit.Row("Tint colour", null, tint, false));
        material.Children.Add(S("Tint strength", "How much colour fills the glass. Higher is more opaque and more readable.", 0.05, 1, g.TintOpacity, v => E(p => p.TintOpacity = v), v => $"{v * 100:0}%"));
        material.Children.Add(S("Blur", "How strongly the wallpaper behind the glass is diffused.", 0, 90, g.Blur, v => E(p => p.Blur = v), v => $"{v:0}"));
        material.Children.Add(S("Backdrop saturation", "Colour intensity of what shows through the glass.", 0.5, 2, g.BackdropSaturation, v => E(p => p.BackdropSaturation = v), v => $"{v * 100:0}%"));
        material.Children.Add(S("Refraction", "A subtle lens magnification of the backdrop.", 0, 0.2, g.Refraction, v => E(p => p.Refraction = v), v => $"{v * 100:0}%"));
        material.Children.Add(S("Liquid lens", "A thick, rounded rim that bends the wallpaper round the glass's edges and moves when the glass does. Off (0%) keeps the classic thin curved edge. Needs Curved glass edges on and a graphics card with pixel shader 3.0.", 0, 1, g.Liquid, v => E(p => p.Liquid = v), v => v < 0.01 ? "Off" : $"{v * 100:0}%"));
        _sliders.Children.Add(Kit.Section("Material", null, material));

        var light = new StackPanel();
        light.Children.Add(S("Rim light", "The thin illuminated border around every glass edge.", 0, 1.2, g.BorderIntensity, v => E(p => p.BorderIntensity = v), v => $"{v * 100:0}%"));
        light.Children.Add(S("Specular highlight", "Soft light falling across the top of the glass.", 0, 1.2, g.HighlightIntensity, v => E(p => p.HighlightIntensity = v), v => $"{v * 100:0}%"));
        light.Children.Add(S("Accent glow", "A breath of accent colour pooled at the bottom of surfaces.", 0, 1, g.Glow, v => E(p => p.Glow = v), v => $"{v * 100:0}%"));
        light.Children.Add(S("Shadow depth", "How far surfaces float above the desktop.", 0, 1, g.ShadowIntensity, v => E(p => p.ShadowIntensity = v), v => $"{v * 100:0}%"));
        _sliders.Children.Add(Kit.Section("Light & depth", null, light));

        var t = Hub.Theme.Current;
        var shape = new StackPanel();
        shape.Children.Add(S("Corner radius", "", 0, 36, t.CornerRadius, v => Hub.Theme.Edit(x => x.CornerRadius = Math.Round(v)), v => $"{v:0} px"));
        shape.Children.Add(S("Wallpaper wash", "Strength of the wallpaper-coloured gradient inside glass.", 0, 1, t.Gradient.Opacity, v => Hub.Theme.Edit(x => x.Gradient.Opacity = v), v => $"{v * 100:0}%"));
        _sliders.Children.Add(Kit.Section("Shape & colour", null, shape));
        UpdateHistory();
    }

    private void RefreshAll()
    {
        if (_name is not null) _name.Text = Hub.Theme.Current.Name;
        BuildSliders();
    }

    private void UpdateHistory()
    {
        if (_undo is not null) _undo.IsEnabled = Hub.Theme.CanUndo;
        if (_redo is not null) _redo.IsEnabled = Hub.Theme.CanRedo;
        if (_name is not null) _name.Text = Hub.Theme.Current.Name;
    }

    protected override void OnEnter()
    {
        Live(() => Hub.Theme.HistoryChanged += UpdateHistory, () => Hub.Theme.HistoryChanged -= UpdateHistory);
        UpdateHistory();
    }

    public override void OnKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Z) { Hub.Theme.Undo(); RefreshAll(); e.Handled = true; }
        else if (e.Key == Key.Y) { Hub.Theme.Redo(); RefreshAll(); e.Handled = true; }
    }

    private void SaveAs()
    {
        var name = Dialogs.Prompt("Save theme", "Name for the new theme.", Hub.Theme.Current.Name.Replace(" (custom)", "") + " copy");
        if (string.IsNullOrWhiteSpace(name)) return;
        var t = Hub.Theme.SaveAsNew(Hub.Theme.Current, name);
        Hub.Theme.Use(t.Id);
        RefreshAll();
    }

    private void ShowMore()
    {
        var menu = new ContextMenu();
        MenuItem I(string h, Action a, bool enabled = true) { var m = new MenuItem { Header = h, IsEnabled = enabled }; m.Click += (_, _) => a(); return m; }
        var cur = Hub.Theme.Current;
        menu.Items.Add(I("Rename…", () =>
        {
            if (Dialogs.Prompt("Rename theme", "New name.", cur.Name) is { Length: > 0 } n) { Hub.Theme.Edit(t => t.Name = n); RefreshAll(); }
        }, !cur.BuiltIn));
        menu.Items.Add(I("Duplicate", () => { var t = Hub.Theme.SaveAsNew(cur, cur.Name.Replace(" (custom)", "") + " copy"); Hub.Theme.Use(t.Id); RefreshAll(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(I("Export theme…", () => ThemeIO.Export(cur)));
        menu.Items.Add(I("Import theme…", () => { if (ThemeIO.Import() is { } t) { Hub.Theme.Use(t.Id); RefreshAll(); } }));
        menu.Items.Add(new Separator());
        menu.Items.Add(I("Delete this theme", () =>
        {
            if (!Dialogs.Confirm("Delete theme?", $"“{cur.Name}” will be removed. Built-in themes are not affected.", "Delete")) return;
            Hub.Theme.Delete(cur);
            Hub.Theme.Use(cur.BasedOn.Length > 0 ? cur.BasedOn : Presets.All[0].Id);
            RefreshAll();
        }, !cur.BuiltIn));
        menu.IsOpen = true;
    }
}

/// <summary>Theme import/export as plain JSON files (".glasstheme").</summary>
public static class ThemeIO
{
    public static void Export(Theme t)
    {
        var dlg = new SaveFileDialog { Title = "Export theme", FileName = string.Concat(t.Name.Split(Path.GetInvalidFileNameChars())) + ".glasstheme", Filter = "WinOrbit theme|*.glasstheme|JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        var copy = t.Clone();
        copy.BuiltIn = false;
        Json.Write(dlg.FileName, copy);
    }

    public static Theme? Import()
    {
        var dlg = new OpenFileDialog { Title = "Import theme", Filter = "WinOrbit theme|*.glasstheme;*.json" };
        if (dlg.ShowDialog() != true) return null;
        var t = Json.Read<Theme>(dlg.FileName);
        if (t is null || t.Dark is null || t.Light is null) { Dialogs.Message("Not a theme", "That file is not a valid WinOrbit theme."); return null; }
        // Sanitise: clamp every number, never trust imported values blindly.
        foreach (var g in new[] { t.Dark, t.Light })
        {
            g.TintOpacity = Math.Clamp(g.TintOpacity, 0.05, 1); g.Blur = Math.Clamp(g.Blur, 0, 90); g.BackdropSaturation = Math.Clamp(g.BackdropSaturation, 0.5, 2);
            g.BorderIntensity = Math.Clamp(g.BorderIntensity, 0, 1.2); g.HighlightIntensity = Math.Clamp(g.HighlightIntensity, 0, 1.2); g.Glow = Math.Clamp(g.Glow, 0, 1);
            g.ShadowIntensity = Math.Clamp(g.ShadowIntensity, 0, 1); g.Refraction = Math.Clamp(g.Refraction, 0, 0.2); g.Liquid = Math.Clamp(g.Liquid, 0, 1);
        }
        t.CornerRadius = Math.Clamp(t.CornerRadius, 0, 36); t.FontScale = Math.Clamp(t.FontScale, 0.85, 1.3); t.Animation = Math.Clamp(t.Animation, 0, 1.5);
        t.Gradient ??= new GradientDef();
        return Hub.Theme.SaveAsNew(t, string.IsNullOrWhiteSpace(t.Name) ? "Imported theme" : t.Name);
    }
}
