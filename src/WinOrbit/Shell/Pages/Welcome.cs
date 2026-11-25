using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.Widgets;

namespace WinOrbit.Shell.Pages;

/// <summary>
/// The first start, in three short steps: pick a look (a desktop set, or keep the starter widgets), choose liquid or
/// classic glass and the colour mode on a live sample, then simple or all settings. Each choice shows on the desktop
/// as soon as it's made, so what people pick is what they see. Skip leaves everything as it is.
/// </summary>
public sealed class WelcomeFlow : Border
{
    internal const int Steps = 3;
    private const string Keep = "";

    private readonly Func<MainWindow?> _owner;
    private readonly Action _done;
    private readonly StackPanel _body = new();
    private readonly List<WidgetView> _previews = new();

    // What the desktop was before the flow, for "Keep the starter widgets" after trying a set.
    private readonly List<WidgetInstance> _startWidgets;
    private readonly string _startTheme, _startGlance;
    private readonly bool _startLiquid;

    private int _step;
    private string _chosen = Keep, _applied = Keep;
    private bool _simple;

    public WelcomeFlow(Func<MainWindow?> owner, Action done)
    {
        _owner = owner;
        _done = done;
        var s = Hub.Settings;
        _startWidgets = s.Widgets.Select(w => w.Clone()).ToList();
        _startTheme = s.ActiveThemeId;
        _startGlance = s.Island.Glance;
        _startLiquid = s.General.LiquidGlass;
        _simple = !s.General.Advanced;
        SetResourceReference(StyleProperty, "Card");
        Padding = new Thickness(28, 22, 28, 18);
        Margin = new Thickness(0, 0, 0, 16);
        Child = _body;
        Loaded += (_, _) => { foreach (var p in _previews) p.Start(); };
        Unloaded += (_, _) => { foreach (var p in _previews) p.Stop(); };
        Render();
    }

    /// <summary>The step after a Next (true) or Back (false) press, kept within the flow.</summary>
    internal static int Move(int step, bool forward) => Math.Clamp(step + (forward ? 1 : -1), 0, Steps - 1);

    private void Render()
    {
        foreach (var p in _previews) p.Stop();
        _previews.Clear();
        _body.Children.Clear();

        var top = Ui.Grid("*,auto");
        top.Children.Add(Ui.Text($"WELCOME TO WINORBIT  ·  STEP {_step + 1} OF {Steps}", "Text.Label", 10));
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < Steps; i++)
        {
            var d = new Border { Width = i == _step ? 22 : 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(5, 0, 0, 0) };
            d.SetResourceReference(BackgroundProperty, i <= _step ? "T.Accent" : "T.Track");
            dots.Children.Add(d);
        }
        top.Children.Add(dots.At(0, 1));
        _body.Children.Add(top);

        var (title, text) = _step switch
        {
            0 => ("Pick a look", "Each look is a desktop set: a clock with the pieces made for it, a theme and the island's glance. Tap one to see it on your desktop; you can change it any time in Widgets › Desktop sets."),
            1 => ("Glass and colour", "The sample below is live: it changes the moment you switch. Liquid glass bends the wallpaper like a lens (drawn by your graphics card); classic glass is plain frosted glass. Rather have no glass at all? Pick another surface below."),
            _ => ("How many settings?", "Simple shows the settings most people change. Everything else stays one click away (Settings › Settings mode), and search always finds every setting."),
        };
        _body.Children.Add(Ui.Text(title, "Text.Title", 24).With(t => t.Margin = new Thickness(0, 10, 0, 4)));
        _body.Children.Add(Ui.Text(text, "Text.Muted").With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 16); t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; }));

        _body.Children.Add(_step switch { 0 => LookStep(), 1 => GlassStep(), _ => SettingsStep() });

        var nav = Ui.Grid("auto,*,auto,auto");
        nav.Margin = new Thickness(0, 18, 0, 2);
        nav.Children.Add(Kit.Btn("Skip setup", Finish).With(b => b.ToolTip = "Keep everything as it is now"));
        if (_step > 0) nav.Children.Add(Kit.Btn("Back", () => { _step = Move(_step, false); Render(); }, Glyph.Left).At(0, 2).With(b => b.Margin = new Thickness(0, 0, 8, 0)));
        var last = _step == Steps - 1;
        nav.Children.Add(Kit.Btn(last ? "Finish" : "Next", () =>
        {
            if (last) { Finish(); return; }
            _step = Move(_step, true);
            Render();
        }, last ? Glyph.Check : Glyph.Right, true).At(0, 3));
        _body.Children.Add(nav);
        if (IsLoaded) foreach (var p in _previews) p.Start();
    }

    // ----- step 1: the look -----

    private UIElement LookStep()
    {
        var wrap = new WrapPanel { Margin = new Thickness(-6, 0, -6, 0) };
        const double width = 368;
        foreach (var set in DesktopSets.Starters)
        {
            var sp = new StackPanel();
            sp.Children.Add(WidgetsPage.SetPreview(set, width, _previews));
            sp.Children.Add(Ui.Text(set.Name, "Text.Body", 14, FontWeights.SemiBold).With(t => t.Margin = new Thickness(2, 10, 0, 2)));
            sp.Children.Add(Ui.Text(set.Description, "Text.Caption", 11.5).With(t => { t.Margin = new Thickness(2, 0, 0, 0); t.Height = 46; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.Wrap; }));
            wrap.Children.Add(Choice(sp, _chosen == set.Id, $"Use the {set.Name} look", () => PickLook(set.Id), width));
        }
        // The starter widgets WinOrbit put on the desktop, for people who'd rather build their own.
        var keep = new StackPanel();
        var mon = Monitors.Primary;
        var h = Math.Round(width * Math.Max(1, mon.Bounds.Bottom - mon.Bounds.Top) / Math.Max(1, mon.Bounds.Right - mon.Bounds.Left));
        var plain = new Border { Width = width, Height = h, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        plain.SetResourceReference(BackgroundProperty, "T.WashStrong");
        plain.SetResourceReference(BorderBrushProperty, "T.Stroke");
        plain.Child = Ui.Icon(Glyph.Tiles, 30, "T.Text3");
        keep.Children.Add(plain);
        keep.Children.Add(Ui.Text("Keep it simple", "Text.Body", 14, FontWeights.SemiBold).With(t => t.Margin = new Thickness(2, 10, 0, 2)));
        keep.Children.Add(Ui.Text("A clock, your music and a system card on glass: what's on your desktop now. Build your own from there.", "Text.Caption", 11.5).With(t => { t.Margin = new Thickness(2, 0, 0, 0); t.Height = 46; t.TextWrapping = TextWrapping.Wrap; }));
        wrap.Children.Add(Choice(keep, _chosen == Keep, "Keep the starter widgets", () => PickLook(Keep), width));
        return wrap;
    }

    private void PickLook(string id)
    {
        _chosen = id;
        if (id != _applied)
        {
            if (id.Length > 0 && DesktopSets.Find(id) is { } set) DesktopSets.Apply(set);
            else RestoreStart();
            _applied = id;
        }
        Render();
    }

    private void RestoreStart()
    {
        var s = Hub.Settings;
        s.Widgets = _startWidgets.Select(w => w.Clone()).ToList();
        s.ActiveThemeId = _startTheme;
        s.Island.Glance = _startGlance;
        s.General.LiquidGlass = _startLiquid;
        s.ActiveSetId = "";
        Hub.Store.Save();
        Hub.ApplyAll();
    }

    // ----- step 2: glass and colour -----

    private UIElement GlassStep()
    {
        var root = new StackPanel();
        var sample = new PreviewStage { Height = 230 };
        var pill = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pill.Children.Add(Ui.Icon(Glyph.Music, 10, "T.Accent").With(t => t.Margin = new Thickness(0, 0, 6, 0)));
        pill.Children.Add(Ui.Text(DateTime.Now.ToString("HH:mm"), "Text.Body", 10.5, FontWeights.SemiBold));
        var island = sample.AddGlass(0.38, 0.05, 0.24, 0.12, pill, 14);
        var clock = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        clock.Children.Add(Ui.Text(DateTime.Now.ToString("HH:mm"), "Text.Display", 34));
        clock.Children.Add(Ui.Text(DateTime.Now.ToString("dddd d MMMM", CultureInfo.InvariantCulture), "Text.Caption", 11));
        var clockCard = sample.AddGlass(0.06, 0.3, 0.34, 0.5, clock, 18);
        var music = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        music.Children.Add(Ui.Text("NOW PLAYING", "Text.Label", 9.5));
        music.Children.Add(Ui.Text("Your music", "Text.Body", 13, FontWeights.SemiBold).With(t => t.Margin = new Thickness(0, 6, 0, 2)));
        music.Children.Add(new ProgressBar { Value = 42, Height = 3, Margin = new Thickness(0, 8, 0, 0) });
        var musicCard = sample.AddGlass(0.56, 0.36, 0.38, 0.4, music, 18);
        Dress(island, Hub.Settings.Island.Material);
        Dress(clockCard, Hub.Settings.Layout.WidgetMaterial);
        Dress(musicCard, Hub.Settings.Layout.WidgetMaterial);
        var frame = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, BorderThickness = new Thickness(1), Child = sample, Margin = new Thickness(0, 0, 0, 14) };
        frame.SetResourceReference(BorderBrushProperty, "T.Stroke");
        frame.SizeChanged += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 16, 16);
        root.Children.Add(frame);
        // Under software rendering no glass gets a lens, so say why the sample can't show the difference.
        var noGpu = Ui.Text("This window is being drawn without the graphics card, so the sample can't show the lens. The island and widgets show it whenever the graphics card draws them.", "Text.Caption", 11.5)
            .With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(2, -6, 0, 12); t.Visibility = Visibility.Collapsed; });
        sample.Loaded += (_, _) => noGpu.Visibility = LiquidGlassEffect.Usable(sample) ? Visibility.Collapsed : Visibility.Visible;
        root.Children.Add(noGpu);

        var liquid = Hub.Theme.LiquidShowing;
        var choices = Ui.Grid("*,12,*");
        choices.Children.Add(Choice(Option(Glyph.Drop, "Liquid glass", "Edges bend and magnify the wallpaper like a real lens. Looks without a lens switch to Liquid Frosted."),
            liquid, "Turn liquid glass on", () => { Hub.Theme.SetLiquidGlass(true); Render(); }));
        choices.Children.Add(Choice(Option(Glyph.Picture, "Classic glass", "Frosted glass with a gentle curve at the rim and no lens. Lighter work for older graphics cards."),
            !liquid, "Turn liquid glass off", () => { Hub.Theme.SetLiquidGlass(false); Render(); }).At(0, 2));
        root.Children.Add(choices.With(g => g.Margin = new Thickness(0, 0, 0, 6)));

        var m = GlassSurface.Materials;
        root.Children.Add(Kit.Row("Surface", "Not a glass person? Gradient fades from your accent colour into your wallpaper's colours, Smoked is a deep tint, Solid is one plain colour and Black is true black. For the island and widgets.",
            Kit.Segmented(m, Math.Max(0, Array.IndexOf(m, GlassSurface.Material(Hub.Settings.Island.Material))), i => { AppearancePage.SetSurface(m[i]); Render(); }, "Surface"), false, Glyph.Color));
        var modes = new[] { ThemeMode.Auto, ThemeMode.Dark, ThemeMode.Light };
        root.Children.Add(Kit.Row("Colour mode", "Auto follows Windows' own light or dark setting.",
            Kit.Segmented(new[] { "Auto", "Dark", "Light" }, Math.Max(0, Array.IndexOf(modes, Hub.Theme.Current.Mode)), i => Hub.Theme.Edit(t => t.Mode = modes[i]), "Colour mode"), true, Glyph.Moon));
        root.Children.Add(AppearancePage.LiveGlassRow());
        return root;
    }

    /// <summary>Show the sample in the surface picked (Glass keeps the stage's own wallpaper slice).</summary>
    private static void Dress(GlassSurface g, string material)
    {
        var m = GlassSurface.Material(material);
        if (m == "Glass") return;
        g.UseMaterial(m);
        if (m == "Black") WidgetWindow.PutInk(g.Resources, true);
    }

    // ----- step 3: settings -----

    private UIElement SettingsStep()
    {
        var root = new StackPanel();
        var choices = Ui.Grid("*,12,*");
        choices.Children.Add(Choice(Option(Glyph.Home, "Simple settings", "The settings most people change, and nothing to get lost in. Recommended."),
            _simple, "Use simple settings", () => { _simple = true; Render(); }));
        choices.Children.Add(Choice(Option(Glyph.Settings, "All settings", "Every option on every page, with the theme studio, wallpaper, tweaks and fine-tuning."),
            !_simple, "Show all settings", () => { _simple = false; Render(); }).At(0, 2));
        root.Children.Add(choices.With(g => g.Margin = new Thickness(0, 0, 0, 12)));
        var s = Hub.Settings;
        root.Children.Add(Kit.ToggleRow("Clipboard history", "Recent copies in the island. Memory only; private copies are skipped.", s.Island.ClipboardHistory,
            v => { s.Island.ClipboardHistory = v; Hub.Store.Save(); Hub.Clipboard.Apply(); }, false, Glyph.Copy));
        root.Children.Add(Kit.ToggleRow("Synced lyrics", "Uses the internet (LRCLIB) to fetch lyrics for the song that's playing. Turns on the network switch.", s.Privacy.LyricsEnabled && s.Privacy.NetworkAllowed,
            v => { s.Privacy.LyricsEnabled = v; if (v) s.Privacy.NetworkAllowed = true; Hub.Store.Save(); }, true, Glyph.Music));
        root.Children.Add(Kit.ToggleRow("Tell me about updates", "Every 48 hours WinOrbit asks GitHub for the latest version number, and a card links you straight to the download when there's a new one. Nothing is installed by itself. Turns on the network switch.", s.Privacy.UpdateCheck && s.Privacy.NetworkAllowed,
            v => Updates.SetAutomatic(v), true, Glyph.Download));
        root.Children.Add(Ui.Text($"Press {s.General.HotkeyLauncher} anywhere to search apps and settings.", "Text.Caption", 12).With(t => t.Margin = new Thickness(2, 10, 0, 0)));
        return root;
    }

    private void Finish()
    {
        var s = Hub.Settings;
        s.General.WelcomeDone = true;
        Hub.Store.Save();
        if (_owner() is { } window) Kit.SetAdvanced(!_simple, window);   // saves, relists the pages and redraws Home
        else { s.General.ShowAdvanced = !_simple; Hub.Store.Save(); _done(); }
    }

    // ----- pieces -----

    private static StackPanel Option(string glyph, string title, string text)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Icon(glyph, 18, "T.Accent").With(t => { t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 10); }));
        sp.Children.Add(Ui.Text(title, "Text.Body", 14, FontWeights.SemiBold));
        sp.Children.Add(Ui.Text(text, "Text.Caption", 11.5).With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 3, 0, 0); }));
        return sp;
    }

    /// <summary>A choice card: the content, with an accent ring and a tick when it's the one picked.</summary>
    private static Button Choice(UIElement content, bool selected, string name, Action pick, double width = double.NaN)
    {
        var grid = new Grid();
        grid.Children.Add(content);
        if (selected)
        {
            var tick = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Child = Ui.Icon(Glyph.Check, 11, "T.OnAccent") };
            tick.SetResourceReference(BackgroundProperty, "T.Accent");
            grid.Children.Add(tick);
        }
        var b = new Button { Margin = new Thickness(6), Padding = new Thickness(12), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Content = grid, ToolTip = name };
        if (!double.IsNaN(width)) b.Width = width + 26;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        if (selected)
        {
            b.SetResourceReference(BorderBrushProperty, "T.Accent");
            b.BorderThickness = new Thickness(2);
        }
        b.Click += (_, _) => pick();
        return b;
    }
}
