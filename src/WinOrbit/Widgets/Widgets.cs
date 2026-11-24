using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;

namespace WinOrbit.Widgets;

public static class WidgetCatalog
{
    // Default sizes come from WidgetSizes (Small 160×160, Medium 336×160, Wide 512×160, Large 336×336), so new widgets
    // line up with each other. Widgets placed by earlier versions keep the size they were given.
    public static readonly List<WidgetKind> All = new()
    {
        new("clock", "Clock", "Seventeen styles, from minimal to Orbit letters, posters and dot matrix. Pick one under Clocks above, or right-click › Style.", Glyph.Clock, "Time", 336, 160, 120, 80, i => new ClockWidget(i)),
        new("calendar", "Calendar", "Month view with today highlighted.", Glyph.Calendar, "Time", 336, 336, 230, 236, i => new CalendarWidget(i)),
        new("worldclock", "World clock", "Up to five cities with their offset from you.", Glyph.Globe, "Time", 336, 160, 200, 96, i => new WorldClockWidget(i)),
        new("system", "System monitor", "CPU, memory, GPU, network and disk at a glance.", Glyph.Chip, "System", 336, 336, 220, 130, i => new SystemWidget(i)),
        new("media", "Now playing", "Artwork, track details and playback controls.", Glyph.Music, "Media", 336, 160, 260, 112, i => new MediaWidget(i)),
        new("focus", "Focus timer", "Pomodoro, countdown and stopwatch with today's session count.", Glyph.Stopwatch, "Productivity", 336, 336, 220, 240, i => new FocusWidget(i)),
        new("notes", "Notes", "A sticky note that saves as you type. Stored only on this PC.", Glyph.Note, "Productivity", 336, 160, 160, 120, i => new NotesWidget(i)),
        new("tasks", "Tasks", "A checklist for tasks or daily objectives.", Glyph.List, "Productivity", 336, 336, 220, 160, i => new TasksWidget(i)),
        new("launcher", "Launcher", "Pin apps, files and folders. Drop items onto it to add them.", Glyph.Tiles, "Desktop", 336, 160, 150, 96, i => new LauncherWidget(i)),
        new("recyclebin", "Recycle Bin", "Item count and size, with open and empty actions.", Glyph.Delete, "Desktop", 160, 160, 150, 104, i => new RecycleBinWidget(i)),
        new("text", "Text", "A line of your own: a quote, a label or a reminder.", Glyph.Edit, "Aesthetic", 336, 160, 140, 64, i => new TextWidget(i)) { DefaultStyle = "bare" },
        new("ambient", "Ambient card", "A wallpaper-matched gradient card with a greeting and the date.", Glyph.Color, "Aesthetic", 336, 160, 200, 110, i => new AmbientWidget(i)),
        new("weather", "Weather", "Optional. Uses the internet only if you enable it in Settings › Privacy.", Glyph.Cloud, "Online (optional)", 336, 160, 230, 128, i => new WeatherWidget(i)),
    };

    public static WidgetKind? Find(string id) => All.FirstOrDefault(k => k.Id == id);

    /// <summary>
    /// Text-only styles of other widgets, made to sit beside the clock skins on the wallpaper (all added Bare):
    /// (kind, style, name, what it shows, size).
    /// </summary>
    public static readonly (string Kind, string Variant, string Name, string About, string Size)[] Companions =
    {
        ("media", "caption", "Now playing · Caption", "The song between two hairlines, centred. Controls show on hover.", "m"),
        ("system", "slashes", "System · Slashes", "CPU, RAM and GPU as a terminal readout of slashes.", "m"),
        ("weather", "line", "Weather · Line", "The place and the sky in spaced capitals. Needs weather turned on.", "m"),
        ("recyclebin", "label", "Recycle bin · Label", "Files and size beside the bin. Double-click opens it.", "m"),
        ("launcher", "icons", "Launcher · Icon row", "Your apps as one row of monochrome icons, no names.", "m"),
    };
}

// =====================================================================================
/// <summary>
/// The desktop clock in seventeen styles. Each style builds its own little layout inside a Viewbox (so it scales with
/// the widget) and an update function; the clock then sleeps until the next minute (or second, when seconds are shown).
/// Only fonts that ship with Windows and WinOrbit's own Orbit letters are used, so nothing is downloaded and nothing
/// needs a licence. The skin styles (Orbit, Hairline, Poster…) live in ClockSkins.cs.
/// </summary>
internal sealed partial class ClockWidget : WidgetView
{
    /// <summary>The styles in the order the menu and the gallery show them: (variant, name, the size it looks best at).</summary>
    public static readonly (string Id, string Name, string Size)[] Styles =
    {
        ("", "Minimal", "m"), ("orbit", "Orbit", "w"), ("hairline", "Hairline", "l"), ("poster", "Poster", "l"),
        ("label", "Label", "m"), ("week", "Week", "w"), ("heavy", "Heavy", "m"), ("story", "Story", "w"),
        ("stacked", "Stacked", "m"), ("date", "Ambient date", "m"), ("greeting", "Greeting", "w"), ("spaced", "Spaced", "w"),
        ("outline", "Outline", "m"), ("bigdate", "Big date", "m"), ("script", "Script", "m"), ("mono", "Mono", "m"), ("dots", "Dot matrix", "m"),
    };

    private static readonly FontFamily ScriptFont = new("Segoe Script, Ink Free, Segoe Print, Segoe UI");
    private static readonly FontFamily CondensedFont = new("Bahnschrift Condensed, Bahnschrift, Segoe UI");
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private Action<DateTime> _update = _ => { };
    private bool Seconds => Instance.Opt("seconds") == "1";
    private bool TicksEverySecond => Seconds && Instance.Variant is "" or "minimal" or "mono";

    public ClockWidget(WidgetInstance i) : base(i)
    {
        var box = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
        box.Child = Instance.Variant switch
        {
            "stacked" => BuildStacked(),
            "date" => BuildAmbientDate(box),
            "greeting" => BuildGreeting(box),
            "spaced" => BuildSpaced(),
            "outline" => BuildOutline(),
            "bigdate" => BuildBigDate(box),
            "script" => BuildScript(),
            "mono" => BuildMono(box),
            "dots" => BuildDots(),
            "orbit" => BuildOrbit(),
            "hairline" => BuildHairline(),
            "poster" => BuildPoster(),
            "label" => BuildLabel(),
            "week" => BuildWeek(),
            "heavy" => BuildHeavy(),
            "story" => BuildStory(),
            _ => BuildMinimal()
        };
        Children.Add(box);
        _timer.Tick += (_, _) => Tick();
    }

    // ----------------------------------------------------------------- shared helpers
    private static TextBlock T(double size, FontWeight weight, string brush = "T.Text", FontFamily? family = null)
    {
        var t = new TextBlock { FontSize = size, FontWeight = weight };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (family is not null) t.FontFamily = family;
        return t;
    }

    /// <summary>Tight lines for big numerals: the line box is the size of the digits, not the font's tall default.</summary>
    private static TextBlock Tight(TextBlock t, double lineHeight)
    {
        t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        t.LineHeight = lineHeight;
        return t;
    }

    private static Shapes.Rectangle Rule(double width, double height, Thickness margin)
    {
        var r = new Shapes.Rectangle { Width = width, Height = height, Margin = margin, SnapsToDevicePixels = true };
        r.SetResourceReference(Shapes.Shape.FillProperty, "T.Text3");
        return r;
    }

    private static CultureInfo Ci => CultureInfo.CurrentCulture;
    private static bool H24 => Hub.Settings.General.Use24Hour;

    /// <summary>
    /// Letter-spacing for WPF, which has none: a gap between every letter ("F R I D A Y"). Hair spaces give a light
    /// tracking, thin spaces a wide one, ordinary spaces the very wide look. Surrogate pairs and accents stay whole.
    /// </summary>
    internal static string Track(string text, string gap)
    {
        if (text.Length < 2 || gap.Length == 0) return text;
        var parts = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) parts.Add(e.GetTextElement());
        return string.Join(gap, parts);
    }

    internal const string Hair = " ", Thin = " ";

    /// <summary>"Good morning" from 5:00, "Good afternoon" from 12:00, "Good evening" from 18:00, "Good night" after midnight.</summary>
    internal static string Greeting(DateTime now) => now.Hour switch { < 5 => "Good night", < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };

    /// <summary>How much of today has gone, 0–100.</summary>
    internal static int DayPercent(DateTime now) => (int)Math.Clamp(Math.Floor(now.TimeOfDay.TotalMinutes / 14.4), 0, 100);

    /// <summary>12- or 24-hour hours as two digits, the way stacked clocks show them ("09", "21").</summary>
    internal static string Hours(DateTime now, bool h24) => now.ToString(h24 ? "HH" : "hh", CultureInfo.InvariantCulture);

    // ----------------------------------------------------------------- the styles
    private FrameworkElement BuildMinimal()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var time = T(58, FontWeights.SemiBold);
        var tail = T(22, FontWeights.SemiBold, "T.Accent");
        tail.VerticalAlignment = VerticalAlignment.Bottom; tail.Margin = new Thickness(6, 0, 0, 11);
        var date = T(14, FontWeights.Normal, "T.Text2");
        date.HorizontalAlignment = HorizontalAlignment.Center; date.Margin = new Thickness(0, -4, 0, 0);
        line.Children.Add(time); line.Children.Add(tail);
        sp.Children.Add(line); sp.Children.Add(date);
        _update = now =>
        {
            time.Text = Fmt.Time(now, H24);
            tail.Text = Seconds ? now.ToString("ss", Ci) : H24 ? "" : now.ToString("tt", Ci);
            tail.Visibility = tail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            date.Text = now.ToString("dddd, d MMMM", Ci);
        };
        return sp;
    }

    private FrameworkElement BuildStacked()
    {
        var g = Ui.Grid("auto,auto", "auto,auto");
        var hh = Tight(T(62, FontWeights.Bold), 58);
        var mm = Tight(T(62, FontWeights.Light, "T.Accent"), 58);
        var side = T(13, FontWeights.Normal, "T.Text2");
        side.Margin = new Thickness(16, 6, 0, 0); side.VerticalAlignment = VerticalAlignment.Center; side.LineHeight = 20;
        g.Children.Add(hh); g.Children.Add(mm.At(1, 0)); g.Children.Add(side.At(0, 1, 2));
        _update = now =>
        {
            hh.Text = Hours(now, H24); mm.Text = now.ToString("mm", Ci);
            side.Text = now.ToString("dddd", Ci) + "\n" + now.ToString("d MMMM", Ci);
        };
        return g;
    }

    private FrameworkElement BuildAmbientDate(Viewbox box)
    {
        var sp = new StackPanel();
        var day = T(13, FontWeights.SemiBold, "T.Accent");
        var num = T(64, FontWeights.SemiBold);
        num.Margin = new Thickness(-3, -10, 0, -8);
        var rest = T(15, FontWeights.Normal, "T.Text2");
        sp.Children.Add(day); sp.Children.Add(num); sp.Children.Add(rest);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        _update = now =>
        {
            day.Text = now.ToString("dddd", Ci).ToUpper(Ci); num.Text = now.Day.ToString(Ci);
            rest.Text = now.ToString("MMMM yyyy", Ci) + "  ·  " + Fmt.Time(now, H24);
        };
        return sp;
    }

    /// <summary>A spaced-caps greeting, the weekday in a light display weight, then the date and time between thin rules.</summary>
    private FrameworkElement BuildGreeting(Viewbox box)
    {
        var sp = new StackPanel();
        var hello = T(12, FontWeights.SemiBold, "T.Text2");
        var day = T(46, FontWeights.Light);
        day.Margin = new Thickness(-2, 0, 0, 0);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var date = T(15, FontWeights.Normal, "T.Text2");
        var time = T(15, FontWeights.SemiBold);
        line.Children.Add(date);
        line.Children.Add(Rule(1, 14, new Thickness(12, 2, 12, 0)));
        line.Children.Add(time);
        sp.Children.Add(hello);
        sp.Children.Add(day);
        sp.Children.Add(Rule(double.NaN, 1, new Thickness(0, 6, 0, 9)).With(r => r.HorizontalAlignment = HorizontalAlignment.Stretch));
        sp.Children.Add(line);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        _update = now =>
        {
            hello.Text = Track(Greeting(now).ToUpper(Ci), Thin);
            day.Text = now.ToString("dddd", Ci);
            date.Text = now.ToString("d MMMM", Ci);
            time.Text = Fmt.Time(now, H24);
        };
        return sp;
    }

    /// <summary>The weekday in very wide caps, the date under it, then the time between dashes. Centred.</summary>
    private FrameworkElement BuildSpaced()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var day = T(34, FontWeights.Light);
        var date = T(12.5, FontWeights.Normal, "T.Text2");
        var time = T(16, FontWeights.SemiBold);
        foreach (var t in new[] { day, date, time }) { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; }
        date.Margin = new Thickness(0, 2, 0, 6);
        sp.Children.Add(day); sp.Children.Add(date); sp.Children.Add(time);
        _update = now =>
        {
            day.Text = Track(now.ToString("dddd", Ci).ToUpper(Ci), " ");
            date.Text = Track(now.ToString("d MMMM yyyy", Ci).ToUpper(Ci), Hair);
            time.Text = "—   " + Fmt.Time(now, H24) + "   —";
        };
        return sp;
    }

    /// <summary>Hours solid over minutes drawn as an outline, with the weekday and date beside them.</summary>
    private FrameworkElement BuildOutline()
    {
        var g = Ui.Grid("auto,auto", "auto,auto");
        var hh = new Shapes.Path { Margin = new Thickness(0, 0, 0, 8) };
        hh.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        var mm = new Shapes.Path { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        mm.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text");
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        var day = T(15, FontWeights.SemiBold);
        var date = T(13, FontWeights.Normal, "T.Text2");
        var ampm = T(12, FontWeights.SemiBold, "T.Accent");
        ampm.Margin = new Thickness(0, 6, 0, 0);
        side.Children.Add(day); side.Children.Add(date); side.Children.Add(ampm);
        g.Children.Add(hh); g.Children.Add(mm.At(1, 0)); g.Children.Add(side.At(0, 1, 2));
        _update = now =>
        {
            var family = TryFindResource("F.Family") as FontFamily ?? new FontFamily("Segoe UI");
            hh.Data = Numerals(Hours(now, H24), family, FontWeights.Bold, 70);
            mm.Data = Numerals(now.ToString("mm", Ci), family, FontWeights.Bold, 70);
            day.Text = Track(now.ToString("ddd", Ci).ToUpper(Ci), Thin);
            date.Text = now.ToString("d MMM", Ci);
            ampm.Text = H24 ? "" : now.ToString("tt", Ci);
            ampm.Visibility = ampm.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        return g;
    }

    /// <summary>Digits as a shape (for outlines), moved so the shape starts at its own top-left corner.</summary>
    private static Geometry Numerals(string text, FontFamily family, FontWeight weight, double size)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1.0);
        var geo = ft.BuildGeometry(new Point(0, 0));
        var b = geo.Bounds;
        if (b.IsEmpty) return Geometry.Empty;
        geo.Transform = new TranslateTransform(-b.X, -b.Y);
        geo.Freeze();
        return geo;
    }

    /// <summary>A huge day number in the accent colour, with the month and weekday laid over its lower half.</summary>
    private FrameworkElement BuildBigDate(Viewbox box)
    {
        var g = new Grid();
        var num = Tight(T(150, FontWeights.Bold, "T.Accent"), 118);
        num.Opacity = 0.92;
        var over = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(34, 0, 0, -6) };
        var month = T(34, FontWeights.SemiBold);
        var rest = T(15, FontWeights.Normal, "T.Text2");
        over.Children.Add(month); over.Children.Add(rest);
        g.Children.Add(num); g.Children.Add(over);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        _update = now =>
        {
            num.Text = now.Day.ToString("00", Ci);
            month.Text = Track(now.ToString("MMMM", Ci).ToUpper(Ci), Hair);
            rest.Text = now.ToString("dddd", Ci) + "  ·  " + Fmt.Time(now, H24);
        };
        return g;
    }

    /// <summary>A handwritten weekday in the accent colour over a tall, condensed time, and the date in spaced caps.</summary>
    private FrameworkElement BuildScript()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var day = T(30, FontWeights.Normal, "T.Accent", ScriptFont);
        day.HorizontalAlignment = HorizontalAlignment.Center;
        day.Margin = new Thickness(0, 0, 0, -14);   // tucks into the top of the digits without hiding them
        day.RenderTransform = new RotateTransform(-5);
        day.RenderTransformOrigin = new Point(0.5, 0.5);
        Panel.SetZIndex(day, 1);
        var time = Tight(T(86, FontWeights.Bold, "T.Text", CondensedFont), 84);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var date = T(11.5, FontWeights.SemiBold, "T.Text2");
        date.HorizontalAlignment = HorizontalAlignment.Center;
        date.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(day); sp.Children.Add(time); sp.Children.Add(date);
        _update = now =>
        {
            day.Text = now.ToString("dddd", Ci);
            time.Text = Fmt.Time(now, H24);
            date.Text = Track(now.ToString("d MMMM yyyy", Ci).ToUpper(Ci), Thin);
        };
        return sp;
    }

    /// <summary>A terminal-style clock in a monospaced font, and a bar showing how much of the day has gone.</summary>
    private FrameworkElement BuildMono(Viewbox box)
    {
        var sp = new StackPanel();
        var time = T(54, FontWeights.Light, "T.Text", MonoFont);
        time.Margin = new Thickness(-2, 0, 0, 0);
        var date = T(13, FontWeights.Normal, "T.Text2", MonoFont);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var cells = new List<Shapes.Rectangle>();
        for (var k = 0; k < 10; k++)
        {
            var cell = new Shapes.Rectangle { Width = 14, Height = 6, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            cells.Add(cell);
            bar.Children.Add(cell);
        }
        var pct = T(11.5, FontWeights.Normal, "T.Text2", MonoFont);
        pct.Margin = new Thickness(7, 0, 0, 0); pct.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(pct);
        sp.Children.Add(time); sp.Children.Add(date); sp.Children.Add(bar);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        _update = now =>
        {
            time.Text = now.ToString(H24 ? "HH:mm" : "hh:mm", Ci) + (Seconds ? now.ToString(":ss", Ci) : "");
            date.Text = now.ToString("ddd dd.MM.yyyy", Ci).ToLower(Ci) + (H24 ? "" : "  " + now.ToString("tt", Ci).ToLower(Ci));
            var p = DayPercent(now);
            for (var k = 0; k < cells.Count; k++) cells[k].SetResourceReference(Shapes.Shape.FillProperty, k < p / 10 ? "T.Accent" : "T.Track");
            pct.Text = $"{p}% of today";
        };
        return sp;
    }

    /// <summary>
    /// The time on a dot-matrix display: every dot of the grid is drawn, the lit ones bright and the rest faint, like
    /// an LED panel. Two shapes in all (lit and unlit dots), rebuilt once a minute.
    /// </summary>
    private FrameworkElement BuildDots()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        var off = new Shapes.Path();
        off.SetResourceReference(Shapes.Shape.FillProperty, "T.Track");
        var on = new Shapes.Path();
        on.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        panel.Children.Add(off); panel.Children.Add(on);
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        var date = T(13, FontWeights.SemiBold, "T.Text2", MonoFont);
        var ampm = T(13, FontWeights.SemiBold, "T.Accent", MonoFont);
        ampm.Margin = new Thickness(12, 0, 0, 0);
        line.Children.Add(date); line.Children.Add(ampm);
        sp.Children.Add(panel); sp.Children.Add(line);
        _update = now =>
        {
            var (lit, unlit) = DotShapes(Hours(now, H24) + ":" + now.ToString("mm", CultureInfo.InvariantCulture), 7, 3);
            on.Data = lit; off.Data = unlit;
            date.Text = Track(now.ToString("ddd d MMM", Ci).ToUpper(Ci), " ");
            ampm.Text = H24 ? "" : now.ToString("tt", Ci).ToUpper(Ci);
            ampm.Visibility = ampm.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        return sp;
    }

    /// <summary>5 × 7 dot-matrix figures, top row first ("1" = a lit dot). The colon is one dot wide.</summary>
    private static readonly Dictionary<char, string[]> DotFont = new()
    {
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
        ['3'] = new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
        ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
        ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
        ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
        [':'] = new[] { "0", "0", "1", "0", "1", "0", "0" },
    };

    internal static string[]? DotGlyph(char c) => DotFont.TryGetValue(c, out var g) ? g : null;

    /// <summary>Every dot position of <paramref name="text"/> on the grid, lit or not, with one blank column between figures. Unknown characters leave a gap.</summary>
    internal static List<(int X, int Y, bool On)> DotLayout(string text)
    {
        var dots = new List<(int, int, bool)>();
        var x = 0;
        foreach (var c in text)
        {
            if (DotGlyph(c) is not { } g) { x += 3; continue; }
            var w = g[0].Length;
            for (var row = 0; row < g.Length; row++)
                for (var col = 0; col < w; col++) dots.Add((x + col, row, g[row][col] == '1'));
            x += w + 1;
        }
        return dots;
    }

    private static (Geometry Lit, Geometry Unlit) DotShapes(string text, double dot, double gap)
    {
        var lit = new GeometryGroup();
        var unlit = new GeometryGroup();
        var r = dot / 2;
        foreach (var (x, y, on) in DotLayout(text))
            (on ? lit : unlit).Children.Add(new EllipseGeometry(new Point(x * (dot + gap) + r, y * (dot + gap) + r), r, r));
        lit.Freeze(); unlit.Freeze();
        return (lit, unlit);
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _update(now);
        // Sleep until the next change: one wake-up a minute unless seconds are shown.
        _timer.Interval = TicksEverySecond ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 5) : TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(30);
    }

    /// <summary>Re-colours what a style paints from the accent itself (Story's gradient month); the rest follows the theme by resource.</summary>
    private Action _retint = () => { };
    private void OnThemeApplied() => _retint();

    protected override void OnStart() { Tick(); _timer.Start(); if (Hub.Ready) Hub.Theme.Applied += OnThemeApplied; }
    protected override void OnStop() { _timer.Stop(); if (Hub.Ready) Hub.Theme.Applied -= OnThemeApplied; }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Style", "variant", Styles.Select(s => (s.Name, s.Id)).ToArray()));
        if (Instance.Variant is "" or "minimal" or "mono")
            items.Add(Item("Show seconds", () => { Instance.Options["seconds"] = Seconds ? "0" : "1"; Save(); Host?.Rebuild(); }, Seconds));
    }
}

// =====================================================================================
internal sealed class CalendarWidget : WidgetView
{
    private readonly MonthView _month = new();
    private readonly DispatcherTimer _midnight = new();

    public CalendarWidget(WidgetInstance i) : base(i)
    {
        Children.Add(_month);
        SizeChanged += (_, e) => { _month.CellSize = Math.Clamp(Math.Min((e.NewSize.Width - 8) / 7.4, (e.NewSize.Height - 54) / 6.3), 20, 44); _month.Build(); };
        _midnight.Tick += (_, _) => { _month.ShowToday(); Arm(); };
    }

    private void Arm() { _midnight.Interval = DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(2); _midnight.Start(); }
    protected override void OnStart() { _month.ShowToday(); Arm(); }
    protected override void OnStop() => _midnight.Stop();
    public override void BuildMenu(ItemCollection items) => items.Add(Item("Go to today", _month.ShowToday));
}

// =====================================================================================
internal sealed class WorldClockWidget : WidgetView
{
    public static readonly (string Id, string City)[] Cities =
    {
        ("UTC", "UTC"), ("Pacific Standard Time", "Los Angeles"), ("Eastern Standard Time", "New York"), ("E. South America Standard Time", "São Paulo"),
        ("GMT Standard Time", "London"), ("W. Europe Standard Time", "Berlin"), ("Arabian Standard Time", "Dubai"), ("India Standard Time", "Mumbai"),
        ("Singapore Standard Time", "Singapore"), ("China Standard Time", "Shanghai"), ("Tokyo Standard Time", "Tokyo"), ("AUS Eastern Standard Time", "Sydney")
    };

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly StackPanel _rows = new() { VerticalAlignment = VerticalAlignment.Center };
    private List<string> Zones => Instance.Opt("zones", "Eastern Standard Time;GMT Standard Time;Tokyo Standard Time").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    public WorldClockWidget(WidgetInstance i) : base(i)
    {
        Children.Add(_rows);
        _timer.Tick += (_, _) => Tick();
    }

    private void Tick()
    {
        _rows.Children.Clear();
        var now = DateTime.UtcNow;
        var local = TimeZoneInfo.Local.GetUtcOffset(now);
        foreach (var id in Zones.Take(5))
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { continue; }
            var there = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
            var diff = tz.GetUtcOffset(now) - local;
            var city = Cities.FirstOrDefault(c => c.Id == id).City ?? id;
            if (_rows.Children.Count > 0)
                _rows.Children.Add(new Shapes.Rectangle { Height = 1, Opacity = 0.7, Margin = new Thickness(16, 4, 0, 4), SnapsToDevicePixels = true }.With(r => r.SetResourceReference(Shapes.Shape.FillProperty, "T.Track")));
            var row = Ui.Grid("auto,*,auto");
            // A filled dot in daylight, a hollow one at night.
            var day = IsDaytime(there);
            var dot = new Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = day ? "Daytime there" : "Night-time there" };
            if (day) dot.SetResourceReference(Shapes.Shape.FillProperty, "T.Accent");
            else { dot.StrokeThickness = 1.2; dot.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text3"); }
            row.Children.Add(dot);
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(WidgetLook.Caps(city, 11, "T.Text"));
            var rel = diff == TimeSpan.Zero ? "same time" : $"{(diff > TimeSpan.Zero ? "+" : "−")}{Math.Abs(diff.TotalHours):0.#}h";
            if (there.Date != DateTime.Now.Date) rel += there.Date > DateTime.Now.Date ? " · tomorrow" : " · yesterday";
            left.Children.Add(WidgetLook.Data(rel, 10.5, "T.Text3").With(t => t.Margin = new Thickness(0, 2, 0, 0)));
            row.Children.Add(left.At(0, 1));
            var time = WidgetLook.Big(26);
            time.Text = Fmt.Time(there, Hub.Settings.General.Use24Hour);
            time.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(time.At(0, 2));
            _rows.Children.Add(row);
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(Ui.Text("Right-click to choose cities.", "Text.Caption"));
        _timer.Interval = TimeSpan.FromSeconds(60 - DateTime.Now.Second) + TimeSpan.FromMilliseconds(30);
    }

    /// <summary>Daytime is 6:00 to 17:59 wherever the city is.</summary>
    internal static bool IsDaytime(DateTime there) => there.Hour is >= 6 and < 18;

    protected override void OnStart() { Tick(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();

    public override void BuildMenu(ItemCollection items)
    {
        var cities = new MenuItem { Header = "Cities" };
        var zones = Zones;
        foreach (var (id, city) in Cities)
            cities.Items.Add(Item(city, () =>
            {
                if (!zones.Remove(id)) zones.Add(id);
                Instance.Options["zones"] = string.Join(';', zones);
                Save(); Tick();
            }, zones.Contains(id)));
        items.Add(cities);
    }
}

// =====================================================================================
internal sealed class SystemWidget : WidgetView
{
    private IDisposable? _sub;
    private readonly Dictionary<string, (TextBlock Value, SegmentBar? Bar, Ring? Ring)> _rows = new();
    private readonly Sparkline _spark = new() { Height = 34 };
    private readonly TextBlock _footer = WidgetLook.Data("", 10.5, "T.Text3");
    /// <summary>The parts that only show when the widget is tall enough (Large): header, graph, network and footer.</summary>
    private readonly List<FrameworkElement> _tallOnly = new();

    /// <summary>The slashes look: (lit slashes, unlit slashes) for each part.</summary>
    private readonly Dictionary<string, (System.Windows.Documents.Run Lit, System.Windows.Documents.Run Off)> _slashes = new();
    internal const int SlashCount = 20;

    public SystemWidget(WidgetInstance i) : base(i)
    {
        if (Instance.Variant == "slashes")
        {
            // A terminal readout for desktop skins: "CPU ////////////////////  12.5%", lit slashes bright and the rest faint.
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var (id, label) in new[] { ("cpu", "CPU"), ("ram", "RAM"), ("gpu", "GPU") })
            {
                var line = new TextBlock { FontFamily = WidgetLook.Mono, FontSize = 13, Margin = new Thickness(0, 0, 0, 5) };
                var name = new System.Windows.Documents.Run(label + " ");
                name.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "T.Text");
                var lit = new System.Windows.Documents.Run("");
                lit.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "T.Text");
                var off = new System.Windows.Documents.Run(new string('/', SlashCount));
                off.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "T.Track");
                var value = new TextBlock { FontFamily = WidgetLook.Mono, FontSize = 13, MinWidth = 64, TextAlignment = TextAlignment.Right, Text = "—" };
                value.SetResourceReference(TextBlock.ForegroundProperty, "T.Text");
                line.Inlines.Add(name); line.Inlines.Add(lit); line.Inlines.Add(off);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(line); row.Children.Add(value);
                lines.Children.Add(row);
                _rows[id] = (value, null, null);
                _slashes[id] = (lit, off);
            }
            var dashes = new Shapes.Line { X2 = 1, Stretch = Stretch.Fill, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Margin = new Thickness(0, 2, 0, 0) };
            dashes.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text3");
            lines.Children.Add(dashes);
            Children.Add(lines);
            return;
        }
        if (Instance.Variant == "rings")
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var (id, label) in new[] { ("cpu", "CPU"), ("ram", "RAM"), ("gpu", "GPU") })
            {
                // A fixed 100 × 100 dial scaled as a whole, so the figures always sit inside the ring.
                var dial = new Grid { Width = 100, Height = 100 };
                var ring = new Ring { Thickness = 6 };
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var value = WidgetLook.Big(27).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Text = "—"; });
                sp.Children.Add(value);
                sp.Children.Add(WidgetLook.Caps(label, 9.5).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 3, 0, 0); }));
                dial.Children.Add(ring); dial.Children.Add(sp);
                grid.Children.Add(new Viewbox { Child = dial, Margin = new Thickness(4) });
                _rows[id] = (value, null, ring);
            }
            Children.Add(grid);
            return;
        }

        // The HUD: a label and a figure over a segmented meter for each part, network and uptime below.
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = WidgetLook.Head("System");
        stack.Children.Add(head); _tallOnly.Add(head);
        if (Instance.Variant == "graph") { stack.Children.Add(_spark.With(s => s.Margin = new Thickness(0, 0, 0, 12))); _tallOnly.Add(_spark); }
        foreach (var (id, label) in new[] { ("cpu", "CPU"), ("ram", "Memory"), ("gpu", "GPU") })
        {
            var line = Ui.Grid("*,auto");
            line.Children.Add(WidgetLook.Caps(label));
            var value = WidgetLook.Data("—", 12, "T.Text").At(0, 1);
            line.Children.Add(value);
            stack.Children.Add(line);
            var bar = new SegmentBar { Margin = new Thickness(0, 6, 0, 12) };
            stack.Children.Add(bar);
            _rows[id] = (value, bar, null);
        }
        var net = Ui.Grid("*,auto");
        net.Children.Add(WidgetLook.Caps("Network"));
        var rate = WidgetLook.Data("—", 12, "T.Text").At(0, 1);
        net.Children.Add(rate);
        net.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(net); _tallOnly.Add(net);
        _rows["net"] = (rate, null, null);
        stack.Children.Add(_footer); _tallOnly.Add(_footer);
        Children.Add(stack);
        // Medium (one row high) keeps just the three meters; Large shows everything.
        SizeChanged += (_, e) =>
        {
            var tall = e.NewSize.Height >= 200;
            foreach (var part in _tallOnly) part.Visibility = tall ? Visibility.Visible : Visibility.Collapsed;
            stack.VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center;   // the header sits at the top, like every other widget's
        };
    }

    private void OnSample(SystemSample s)
    {
        void Set(string id, double? pct, string text)
        {
            if (!_rows.TryGetValue(id, out var r)) return;
            if (r.Value.Text != text) r.Value.Text = text;
            r.Bar?.Set(pct ?? 0);
            if (r.Ring is not null) r.Ring.Value = (pct ?? 0) / 100;
            if (_slashes.TryGetValue(id, out var sl))
            {
                var (lit, off) = Slashes(pct);
                if (sl.Lit.Text != lit) { sl.Lit.Text = lit; sl.Off.Text = off; }
            }
        }
        if (Instance.Variant == "slashes")
        {
            Set("cpu", s.Cpu, $"{s.Cpu:0.0}%");
            Set("ram", s.Ram, $"{s.Ram:0.0}%");
            Set("gpu", s.Gpu, s.Gpu is { } pg ? $"{pg:0.0}%" : "n/a");
            return;
        }
        Set("cpu", s.Cpu, $"{s.Cpu:0}%");
        Set("ram", s.Ram, Instance.Variant == "rings" ? $"{s.Ram:0}%" : $"{s.RamUsedGb:0.0}/{s.RamTotalGb:0} GB");
        Set("gpu", s.Gpu, s.Gpu is { } g ? (s.GpuTemp is { } t && Instance.Variant != "rings" ? $"{g:0}% · {t:0}°C" : $"{g:0}%") : "n/a");
        Set("net", null, $"↓{Fmt.Rate(s.NetDown)}  ↑{Fmt.Rate(s.NetUp)}");
        var footer = $"disk {s.DiskActive:0}%  ·  up {Fmt.Uptime(s.Uptime)}" + (s.Battery is { } b ? $"  ·  battery {b}%" : "");
        if (_footer.Text != footer) _footer.Text = footer;
        if (Instance.Variant == "graph") _spark.Update(Hub.Monitor.CpuHistory);
    }

    protected override void OnStart() => _sub = Hub.Monitor.Subscribe(OnSample);
    protected override void OnStop() { _sub?.Dispose(); _sub = null; }
    public override void BuildMenu(ItemCollection items) => items.Add(Choice("Style", "variant", ("Meters", ""), ("Meters + CPU graph", "graph"), ("Rings", "rings"), ("Slashes", "slashes")));

    /// <summary>The slashes readout for a percentage: the lit and the unlit part, together always <see cref="SlashCount"/> long.</summary>
    internal static (string Lit, string Off) Slashes(double? percent)
    {
        var lit = WidgetLook.Lit(percent ?? 0, SlashCount);
        return (new string('/', lit), new string('/', SlashCount - lit));
    }
}

// =====================================================================================
internal sealed class MediaWidget : WidgetView
{
    private readonly Border _art = new() { CornerRadius = new CornerRadius(14) };
    private readonly TextBlock _artGlyph = Ui.Icon(Glyph.Music, 26, "T.Accent");
    private readonly TextBlock _title = Ui.Text("", "Text.Sub", 16), _artist = Ui.Text("", "Text.Caption", 12.5);
    private readonly TextBlock _source;
    private readonly Button _play;
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 6, 0, 0) };

    private bool Caption => Instance.Variant == "caption";

    public MediaWidget(WidgetInstance i) : base(i)
    {
        if (Caption)
        {
            _play = Ui.IconButton(Glyph.Play, "Play or pause", () => Hub.Media.PlayPause(), 34, "Button.IconAccent");
            _source = BuildCaption();
            return;
        }
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _art.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
        _art.Child = _artGlyph;
        _art.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualHeight") { Source = _art });
        Children.Add(_art);
        _title.TextTrimming = TextTrimming.CharacterEllipsis; _title.TextWrapping = TextWrapping.NoWrap;
        _artist.TextTrimming = TextTrimming.CharacterEllipsis; _artist.TextWrapping = TextWrapping.NoWrap;
        var sp = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(WidgetLook.Head("Now playing", out _source).With(h => h.Margin = new Thickness(0, 0, 0, 7)));
        sp.Children.Add(_title); sp.Children.Add(_artist);
        _controls.Children.Add(Ui.IconButton(Glyph.Previous, "Previous track", () => Hub.Media.Previous()));
        _play = Ui.IconButton(Glyph.Play, "Play or pause", () => Hub.Media.PlayPause(), 36, "Button.IconAccent");
        _play.Margin = new Thickness(4, 0, 4, 0);
        _controls.Children.Add(_play);
        _controls.Children.Add(Ui.IconButton(Glyph.Next, "Next track", () => Hub.Media.Next()));
        sp.Children.Add(_controls);
        Children.Add(sp.At(0, 1));
    }

    /// <summary>
    /// The caption look, for desktop skins: no artwork and no card, just the playing app in spaced capitals between
    /// two hairlines, the title and the artist, centred. The controls appear while the pointer is over it.
    /// </summary>
    private TextBlock BuildCaption()
    {
        Background = Brushes.Transparent;   // the pointer counts anywhere on it, not only over the letters
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 9) };
        Shapes.Rectangle Rule() => new Shapes.Rectangle { Width = 22, Height = 1, VerticalAlignment = VerticalAlignment.Center }.With(r => r.SetResourceReference(Shapes.Shape.FillProperty, "T.Text3"));
        var source = WidgetLook.Caps("Now playing", 10.5, "T.Text2");
        source.Margin = new Thickness(10, 0, 10, 0);
        head.Children.Add(Rule()); head.Children.Add(source); head.Children.Add(Rule());
        foreach (var t in new[] { _title, _artist }) { t.TextAlignment = TextAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }
        _title.FontSize = 17; _title.FontWeight = FontWeights.SemiBold;
        _artist.Margin = new Thickness(0, 2, 0, 0);
        _controls.Children.Add(Ui.IconButton(Glyph.Previous, "Previous track", () => Hub.Media.Previous(), 30));
        _play.Margin = new Thickness(4, 0, 4, 0);
        _controls.Children.Add(_play);
        _controls.Children.Add(Ui.IconButton(Glyph.Next, "Next track", () => Hub.Media.Next(), 30));
        _controls.HorizontalAlignment = HorizontalAlignment.Center;
        _controls.Margin = new Thickness(0, 6, 0, 0);
        _controls.Opacity = 0;
        sp.Children.Add(head); sp.Children.Add(_title); sp.Children.Add(_artist); sp.Children.Add(_controls);
        Children.Add(sp);
        MouseEnter += (_, _) => Motion.To(_controls, OpacityProperty, ControlsOpacity(true), 160);
        MouseLeave += (_, _) => Motion.To(_controls, OpacityProperty, 0, 220);
        return source;
    }

    private double ControlsOpacity(bool hover) => Caption && !hover ? 0 : Hub.Media.HasTrack ? 1 : 0.45;

    private void Update()
    {
        var m = Hub.Media;
        var has = m.HasTrack;
        _title.Text = has ? m.DisplayTitle : "Nothing playing";
        _artist.Text = has ? (string.IsNullOrWhiteSpace(m.Artist) ? m.Source : m.Artist) : "Start music in any app";
        WidgetLook.SetCaps(_source, has && !string.IsNullOrWhiteSpace(m.Source) ? m.Source : "Now playing");
        _play.Content = m.IsPlaying ? Glyph.Pause : Glyph.Play;
        _controls.Opacity = ControlsOpacity(IsMouseOver);
        if (has && m.Art is not null) { _art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; _artGlyph.Visibility = Visibility.Collapsed; }
        else { _art.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft"); _artGlyph.Visibility = Visibility.Visible; }
    }

    protected override void OnStart() { Hub.Media.Changed += Update; Update(); }
    protected override void OnStop() => Hub.Media.Changed -= Update;
    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Style", "variant", ("Artwork and controls", ""), ("Caption", "caption")));
        items.Add(Item("Open sound output settings", () => Island.IslandWindow.OpenUri("ms-settings:sound")));
    }
}

// =====================================================================================
internal sealed class FocusWidget : WidgetView
{
    private readonly Ring _ring = new() { Thickness = 8 };
    private readonly TextBlock _time = Ui.Text("25:00", "Text.Display", 34), _mode = Ui.Text("Focus", "Text.Caption", 12), _stats = Ui.Text("", "Text.Caption", 11.5);
    private readonly Button _start;

    public FocusWidget(WidgetInstance i) : base(i)
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dial = new Grid { MaxWidth = 190, MaxHeight = 190 };
        dial.Children.Add(_ring);
        var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _time.HorizontalAlignment = HorizontalAlignment.Center; _mode.HorizontalAlignment = HorizontalAlignment.Center;
        centre.Children.Add(_time); centre.Children.Add(_mode);
        dial.Children.Add(new Viewbox { Child = centre, Margin = new Thickness(26) });
        Children.Add(dial);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        _start = Ui.Button("Start", () => Hub.Timers.Toggle(), "Button.Accent");
        _start.MinWidth = 84;
        buttons.Children.Add(_start);
        buttons.Children.Add(Ui.IconButton(Glyph.Refresh, "Reset", () => Hub.Timers.Reset()).With(b => b.Margin = new Thickness(8, 0, 0, 0)));
        Children.Add(buttons.At(1, 0));
        _stats.HorizontalAlignment = HorizontalAlignment.Center; _stats.Margin = new Thickness(0, 9, 0, 0); _stats.TextAlignment = TextAlignment.Center;
        Children.Add(_stats.At(2, 0));
    }

    private void Update()
    {
        var t = Hub.Timers;
        _time.Text = t.Display;
        _mode.Text = t.ModeLabel;
        _ring.Value = t.Mode == TimerMode.Stopwatch ? (t.Elapsed.TotalSeconds % 60) / 60.0 : 1 - t.Progress;
        _start.Content = t.Running ? "Pause" : t.IsActive ? "Resume" : "Start";
        _stats.Text = t.CompletedToday == 0 ? "No sessions yet today" : $"Today: {t.CompletedToday} session{(t.CompletedToday == 1 ? "" : "s")} · {t.MinutesToday} min";
    }

    protected override void OnStart() { Hub.Timers.Changed += Update; Update(); }
    protected override void OnStop() => Hub.Timers.Changed -= Update;

    public override void BuildMenu(ItemCollection items)
    {
        var t = Hub.Timers;
        var mode = new MenuItem { Header = "Mode" };
        mode.Items.Add(Item("Focus (Pomodoro)", () => t.SetMode(TimerMode.Focus), t.Mode is TimerMode.Focus or TimerMode.Break));
        mode.Items.Add(Item("Countdown", () => t.SetMode(TimerMode.Countdown), t.Mode == TimerMode.Countdown));
        mode.Items.Add(Item("Stopwatch", () => t.SetMode(TimerMode.Stopwatch), t.Mode == TimerMode.Stopwatch));
        items.Add(mode);
        var len = new MenuItem { Header = "Length" };
        foreach (var m in new[] { 5, 10, 15, 25, 45, 50, 60, 90 })
            len.Items.Add(Item($"{m} minutes", () => t.SetMode(t.Mode is TimerMode.Stopwatch or TimerMode.Break ? TimerMode.Focus : t.Mode, m), Math.Abs(t.Total.TotalMinutes - m) < 0.01 && t.Mode != TimerMode.Stopwatch));
        items.Add(len);
        var brk = new MenuItem { Header = "Break length" };
        foreach (var m in new[] { 3, 5, 10, 15 }) brk.Items.Add(Item($"{m} minutes", () => t.BreakMinutes = m, t.BreakMinutes == m));
        items.Add(brk);
    }
}

// =====================================================================================
internal sealed class NotesWidget : WidgetView
{
    private readonly TextBox _box = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13.5 };
    private readonly TextBlock _hint = Ui.Text("Write a note…", "Text.Caption", 13.5, null, "T.Text3");
    private bool _loading;

    public NotesWidget(WidgetInstance i) : base(i)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(WidgetLook.Head(Instance.Opt("title", "Notes")));
        _box.SetResourceReference(StyleProperty, "TextBox.Bare");
        System.Windows.Automation.AutomationProperties.SetName(_box, "Note text");
        _hint.IsHitTestVisible = false;
        Children.Add(_hint.At(1, 0));
        Children.Add(_box.At(1, 0));
        _box.TextChanged += (_, _) =>
        {
            _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_loading) return;
            Hub.Store.Data.Notes[Instance.Id] = _box.Text;
            Hub.Store.SaveData();
        };
    }

    protected override void OnStart()
    {
        _loading = true;
        _box.Text = Hub.Store.Data.Notes.TryGetValue(Instance.Id, out var t) ? t : "";
        _loading = false;
    }

    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items) => items.Add(Item("Rename…", () =>
    {
        if (Dialogs.Prompt("Rename note", "Title shown at the top of this note.", Instance.Opt("title", "NOTES")) is { Length: > 0 } t) { Instance.Options["title"] = t.ToUpperInvariant(); Save(); Host?.Rebuild(); }
    }));
}

// =====================================================================================
internal sealed class TasksWidget : WidgetView
{
    private readonly StackPanel _list = new();
    private readonly TextBox _input = new() { FontSize = 13 };
    private readonly TextBlock _count = WidgetLook.Data("", 10.5, "T.Text3");
    private List<TaskItem> Items
    {
        get
        {
            if (!Hub.Store.Data.Tasks.TryGetValue(Instance.Id, out var l)) Hub.Store.Data.Tasks[Instance.Id] = l = new List<TaskItem>();
            return l;
        }
    }

    public TasksWidget(WidgetInstance i) : base(i)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = WidgetLook.Head(Instance.Variant == "objectives" ? "Today's objectives" : Instance.Opt("title", "Tasks"), _count);
        head.Margin = new Thickness(0, 0, 4, 8);
        Children.Add(head);
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Focusable = false }.At(1, 0));
        _input.Margin = new Thickness(0, 8, 4, 0);
        _input.ToolTip = "Type a task and press Enter";
        System.Windows.Automation.AutomationProperties.SetName(_input, "New task");
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_input.Text)) return;
            Items.Add(new TaskItem { Text = _input.Text.Trim() });
            _input.Clear();
            Hub.Store.SaveData();
            Render();
        };
        Children.Add(_input.At(2, 0));
    }

    private void Render()
    {
        _list.Children.Clear();
        var items = Items;
        foreach (var task in items)
        {
            var row = Ui.Grid("*,auto");
            row.Margin = new Thickness(0, 3, 0, 3);
            var text = new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Opacity = task.Done ? 0.5 : 1, TextDecorations = task.Done ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { IsChecked = task.Done, Content = text };
            check.Click += (_, _) => { task.Done = check.IsChecked == true; Hub.Store.SaveData(); Render(); };
            row.Children.Add(check);
            var del = Ui.IconButton(Glyph.Cancel, "Delete task", () => { items.Remove(task); Hub.Store.SaveData(); Render(); }, 24).At(0, 1);
            del.Opacity = 0;
            row.MouseEnter += (_, _) => del.Opacity = 0.8;
            row.MouseLeave += (_, _) => del.Opacity = 0;
            row.Background = Brushes.Transparent;
            row.Children.Add(del);
            _list.Children.Add(row);
        }
        if (items.Count == 0) _list.Children.Add(Ui.Text("Nothing here yet. Add your first item below.", "Text.Caption", 12, null, "T.Text3"));
        _count.Text = items.Count == 0 ? "" : $"{items.Count(t => t.Done)}/{items.Count}";
    }

    protected override void OnStart() => Render();
    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Kind", "variant", ("Tasks", ""), ("Daily objectives", "objectives")));
        items.Add(Item("Clear completed", () => { Items.RemoveAll(t => t.Done); Hub.Store.SaveData(); Render(); }));
    }
}

// =====================================================================================
internal sealed class LauncherWidget : WidgetView
{
    private readonly WrapPanel _panel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _empty = new() { Margin = new Thickness(6) };
    private List<string> Paths => Instance.Opt("items").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    public LauncherWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(Inset - 6);   // the buttons' own padding brings the icons to the common inset
        Background = Brushes.Transparent;
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _panel, Focusable = false });
        // Empty: a dashed drop zone that says what to do.
        var zone = new Shapes.Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 3 }, SnapsToDevicePixels = true };
        zone.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text3");
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10) };
        label.Children.Add(Ui.Icon(Glyph.Add, 16, "T.Accent").With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        label.Children.Add(WidgetLook.Caps("Drop apps here").With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 8, 0, 3); }));
        label.Children.Add(Ui.Text("files and folders too, or right-click › Add shortcut…", "Text.Caption", 11.5, null, "T.Text3").With(t => { t.TextAlignment = TextAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center; }));
        _empty.Children.Add(zone); _empty.Children.Add(label);
        _empty.IsHitTestVisible = false;
        Children.Add(_empty);
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { var p = Paths; p.AddRange(files.Where(f => !p.Contains(f, StringComparer.OrdinalIgnoreCase))); Store(p); }
        };
    }

    private void Store(List<string> paths) { Instance.Options["items"] = string.Join('|', paths.Take(24)); Save(); Render(); }

    private bool IconRow => Instance.Variant == "icons";

    private void Render()
    {
        _panel.Children.Clear();
        _panel.HorizontalAlignment = IconRow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        var paths = Paths;
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name)) name = path;
            var b = new Button { Margin = new Thickness(2), ToolTip = IconRow ? name : path };
            b.SetResourceReference(StyleProperty, "Button.Ghost");
            if (IconRow)
            {
                // The icon row, for desktop skins: each app drawn as a glyph in the text colour, no names, like the white
                // icon rows of the skins people share. A 64 px icon drawn at 26 stays sharp, also on scaled screens.
                b.Width = 46; b.Height = 46; b.Padding = new Thickness(10);
                b.Content = InkIcon(path) is { } mask ? InkGlyph(mask, 26) : Ui.Icon(Glyph.Page, 22);
            }
            else
            {
                var icon = ShellIcon(path);
                b.Width = 76; b.Padding = new Thickness(4, 8, 4, 6);
                var sp = new StackPanel();
                sp.Children.Add(icon is not null ? new Image { Source = icon, Width = 32, Height = 32 } : Ui.Icon(Glyph.Page, 26));
                sp.Children.Add(Ui.Text(name, "Text.Caption", 11).With(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 5, 0, 0); }));
                b.Content = sp;
            }
            b.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { Dialogs.Message("Could not open item", ex.Message, path); }
            };
            var menu = new ContextMenu();
            menu.Items.Add(Item("Remove from launcher", () => { var p = Paths; p.Remove(path); Store(p); }));
            b.ContextMenu = menu;
            _panel.Children.Add(b);
        }
        _empty.Visibility = paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The icon row's glyph: the theme's text colour seen through the icon's ink mask, so it follows light and dark themes and bare text.</summary>
    private static FrameworkElement InkGlyph(ImageSource mask, double size)
    {
        var glyph = new Shapes.Rectangle { Width = size, Height = size, OpacityMask = new ImageBrush(mask) { Stretch = Stretch.Uniform } };
        glyph.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        RenderOptions.SetBitmapScalingMode(glyph, BitmapScalingMode.HighQuality);
        return glyph;
    }

    /// <summary>A file's icon (64 px from the shell, or the 32 px one) turned into an ink mask by <see cref="Ink"/>, or null.</summary>
    private static ImageSource? InkIcon(string path)
    {
        if ((Launcher.AppIcons.ForFile(path, 64) ?? ShellIcon(path)) is not BitmapSource src) return null;
        try
        {
            var premultiplied = src.Format == PixelFormats.Pbgra32;
            var bgra = premultiplied || src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var stride = bgra.PixelWidth * 4;
            var px = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(px, stride, 0);
            Ink(px, premultiplied);
            var mask = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, px, stride);
            mask.Freeze();
            return mask;
        }
        catch { return null; }
    }

    /// <summary>How much ink the darkest part of an icon keeps, so a dark tile still shows its outline.</summary>
    internal const double InkFloor = 0.28;

    /// <summary>
    /// Turns icon pixels (BGRA) into an ink mask in place: white, premultiplied, with the alpha saying how much ink each
    /// pixel gets. The icon's lightest colour gets full ink and its darkest <see cref="InkFloor"/>, stretched over the
    /// icon's own range, so a logo stays visible inside its tile the way a glyph would, and an icon in one flat colour
    /// becomes a solid silhouette. Transparency is kept. <paramref name="premultiplied"/> pixels are un-premultiplied first.
    /// </summary>
    internal static void Ink(byte[] bgra, bool premultiplied)
    {
        var count = bgra.Length / 4;
        var light = new double[count];
        double lo = 255, hi = 0;
        for (var i = 0; i < count; i++)
        {
            var k = i * 4;
            var a = bgra[k + 3];
            if (a == 0) continue;
            double b = bgra[k], g = bgra[k + 1], r = bgra[k + 2];
            if (premultiplied) { var s = 255.0 / a; b *= s; g *= s; r *= s; }
            light[i] = Math.Min(255, 0.114 * b + 0.587 * g + 0.299 * r);
            if (a >= 128) { lo = Math.Min(lo, light[i]); hi = Math.Max(hi, light[i]); }   // soft edges don't set the range
        }
        var range = hi - lo;
        for (var i = 0; i < count; i++)
        {
            var k = i * 4;
            var t = range < 24 ? 1 : Math.Clamp((light[i] - lo) / range, 0, 1);
            var ink = (byte)Math.Round(bgra[k + 3] * (InkFloor + (1 - InkFloor) * t));
            bgra[k] = bgra[k + 1] = bgra[k + 2] = bgra[k + 3] = ink;
        }
    }

    internal static ImageSource? ShellIcon(string path)
    {
        try
        {
            var info = new Native.SHFILEINFO();
            var res = Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Native.DestroyIcon(info.hIcon); }
        }
        catch { return null; }
    }

    protected override void OnStart() => Render();
    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Style", "variant", ("Icons and names", ""), ("Icon row", "icons")));
        items.Add(Item("Add shortcut…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose an app or file to pin", Multiselect = true, DereferenceLinks = false, Filter = "Apps and shortcuts|*.exe;*.lnk;*.url|All files|*.*" };
            if (dlg.ShowDialog() == true) { var p = Paths; p.AddRange(dlg.FileNames.Where(f => !p.Contains(f, StringComparer.OrdinalIgnoreCase))); Store(p); }
        }));
        items.Add(Item("Add folder…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to pin" };
            if (dlg.ShowDialog() == true) { var p = Paths; if (!p.Contains(dlg.FolderName)) p.Add(dlg.FolderName); Store(p); }
        }));
    }
}

// =====================================================================================
internal sealed class RecycleBinWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(90) };
    private readonly TextBlock _value = WidgetLook.Big(46), _detail = WidgetLook.Data("");
    private readonly Button _empty;
    private readonly TextBlock? _files, _size;

    public RecycleBinWidget(WidgetInstance i) : base(i)
    {
        _empty = Ui.IconButton(Glyph.Delete, "Empty the Recycle Bin…", Empty, 30);
        _timer.Tick += (_, _) => Refresh();
        MouseEnter += (_, _) => Refresh();
        if (Instance.Variant == "label")
        {
            // The label look, for desktop skins: right-aligned spaced capitals beside the bin. Double-click opens it.
            Background = Brushes.Transparent;
            var g = Ui.Grid("*,auto");
            var lines = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(WidgetLook.Caps("Recycle bin", 13, "T.Text").With(t => t.HorizontalAlignment = HorizontalAlignment.Right));
            _files = WidgetLook.Caps("", 10, "T.Text2").With(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 4, 0, 0); });
            _size = WidgetLook.Caps("", 10, "T.Text2").With(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(0, 2, 0, 0); });
            lines.Children.Add(_files); lines.Children.Add(_size);
            g.Children.Add(lines);
            g.Children.Add(Ui.Icon(Glyph.Delete, 30, "T.Accent").With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(12, 0, 0, 0); }).At(0, 1));
            Children.Add(g);
            MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Island.IslandWindow.OpenUri("shell:RecycleBinFolder"); e.Handled = true; } };
            return;
        }
        // Header, a big count with its size underneath, and two small actions: the same layout at every size.
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(WidgetLook.Head("Recycle bin"));
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _value.Text = "—";
        _detail.Margin = new Thickness(1, 2, 0, 0);
        sp.Children.Add(_value); sp.Children.Add(_detail);
        Children.Add(sp.At(1, 0));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-7, 0, 0, -6) };
        actions.Children.Add(Ui.IconButton(Glyph.Folder, "Open the Recycle Bin", () => Island.IslandWindow.OpenUri("shell:RecycleBinFolder"), 30));
        actions.Children.Add(_empty);
        Children.Add(actions.At(2, 0));
    }

    private void Refresh()
    {
        var info = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        if (Native.SHQueryRecycleBin(null, ref info) != 0)
        {
            _value.Text = "—"; _detail.Text = "unavailable";
            if (_files is not null && _size is not null) { WidgetLook.SetCaps(_files, "Unavailable"); WidgetLook.SetCaps(_size, ""); }
            return;
        }
        var (count, detail) = Describe(info.i64NumItems, info.i64Size);
        _value.Text = count; _detail.Text = detail;
        if (_files is not null && _size is not null)
        {
            var (files, size) = LabelLines(info.i64NumItems, info.i64Size);
            WidgetLook.SetCaps(_files, files); WidgetLook.SetCaps(_size, size);
        }
        _empty.IsEnabled = info.i64NumItems > 0;
        _empty.Opacity = info.i64NumItems > 0 ? 1 : 0.4;
    }

    /// <summary>The big figure and the line under it: "11" and "items · 674.5 MB", or "0" and "all clear".</summary>
    internal static (string Count, string Detail) Describe(long items, long bytes) =>
        items <= 0 ? ("0", "all clear") : (items.ToString("N0", CultureInfo.CurrentCulture), $"{(items == 1 ? "item" : "items")} · {Fmt.Size(bytes)}");

    /// <summary>The label look's two lines: "Files 11" and "Size 674.5 MB" ("Size 0 B" when empty).</summary>
    internal static (string Files, string Size) LabelLines(long items, long bytes) =>
        ($"Files {Math.Max(0, items).ToString("N0", CultureInfo.CurrentCulture)}", $"Size {(items <= 0 ? "0 B" : Fmt.Size(bytes))}");

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Style", "variant", ("Count and actions", ""), ("Label", "label")));
        items.Add(Item("Open the Recycle Bin", () => Island.IslandWindow.OpenUri("shell:RecycleBinFolder")));
        items.Add(Item("Empty the Recycle Bin…", Empty));
    }

    private void Empty()
    {
        if (!Dialogs.Confirm("Empty the Recycle Bin?", "Everything in the Recycle Bin will be permanently deleted. This cannot be undone.", "Empty")) return;
        Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION);
        Refresh();
    }

    protected override void OnStart() { Refresh(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();
}

// =====================================================================================
internal sealed class TextWidget : WidgetView
{
    public TextWidget(WidgetInstance i) : base(i)
    {
        var text = Instance.Opt("text", "Make it simple, but significant.");
        var size = double.TryParse(Instance.Opt("size", "20"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 20;
        var t = new TextBlock
        {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Instance.Variant == "bold" ? FontWeights.Bold : Instance.Variant == "light" ? FontWeights.Light : FontWeights.SemiBold,
            TextAlignment = Instance.Opt("align") switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left },
            LineHeight = size * 1.3
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, Instance.Opt("accent") == "1" ? "T.Accent" : "T.Text");
        Children.Add(t);
        Background = Brushes.Transparent;
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Edit(); e.Handled = true; } };
    }

    private void Edit()
    {
        if (Dialogs.Prompt("Edit text", "What should this widget say?", Instance.Opt("text", "Make it simple, but significant.")) is { Length: > 0 } t)
        {
            Instance.Options["text"] = t; Save(); Host?.Rebuild();
        }
    }

    protected override void OnStart() { }
    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Item("Edit text…", Edit));
        items.Add(Choice("Size", "size", ("Small", "14"), ("Medium", "20"), ("Large", "28"), ("Display", "40")));
        items.Add(Choice("Weight", "variant", ("Light", "light"), ("Semibold", ""), ("Bold", "bold")));
        items.Add(Choice("Align", "align", ("Left", ""), ("Centre", "center"), ("Right", "right")));
        items.Add(Item("Use accent colour", () => { Instance.Options["accent"] = Instance.Opt("accent") == "1" ? "0" : "1"; Save(); Host?.Rebuild(); }, Instance.Opt("accent") == "1"));
    }
}

// =====================================================================================
internal sealed class AmbientWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly TextBlock _greeting = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _date = new() { FontSize = 13.5, Foreground = Brushes.White, Opacity = 0.86, Margin = new Thickness(0, 4, 0, 0) };

    public AmbientWidget(WidgetInstance i) : base(i)
    {
        // The wallpaper gradient at full strength, softened by a dark veil so white text always reads.
        Margin = new Thickness(0);   // the card fills the glass; its text keeps the common inset below
        var wash = new Border { Margin = new Thickness(6) };
        wash.SetResourceReference(Border.BackgroundProperty, "T.WashStrong");
        wash.SetResourceReference(Border.CornerRadiusProperty, "R.Control");
        var veil = new Border { Margin = new Thickness(6), Background = new LinearGradientBrush(Color.FromArgb(40, 0, 0, 0), Color.FromArgb(120, 0, 0, 0), 90) };
        veil.SetResourceReference(Border.CornerRadiusProperty, "R.Control");
        Children.Add(wash); Children.Add(veil);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Inset + 6, 0, Inset + 6, Inset + 4) };
        sp.Children.Add(_greeting); sp.Children.Add(_date);
        Children.Add(sp);
        _timer.Tick += (_, _) => Tick();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        var name = Instance.Opt("name");
        var hello = ClockWidget.Greeting(now);
        _greeting.Text = string.IsNullOrWhiteSpace(name) ? hello : $"{hello}, {name}";
        _date.Text = now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        _timer.Interval = TimeSpan.FromMinutes(60 - now.Minute);
    }

    protected override void OnStart() { Tick(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();

    public override void BuildMenu(ItemCollection items) => items.Add(Item("Set name…", () =>
    {
        if (Dialogs.Prompt("Greeting name", "Name to greet (leave empty for none). Stored only on this PC.", Instance.Opt("name")) is { } n) { Instance.Options["name"] = n; Save(); Tick(); }
    }));
}

// =====================================================================================
internal sealed class WeatherWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(31) };
    private readonly StackPanel _body = new() { VerticalAlignment = VerticalAlignment.Center };
    private const double ForecastHeight = 200;

    public WeatherWidget(WidgetInstance i) : base(i)
    {
        Children.Add(_body);
        _timer.Tick += async (_, _) => await Refresh(false);
        if (Line)
        {
            Background = Brushes.Transparent;   // the whole line answers the pointer, not only its letters
            MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && !Hub.Weather.Enabled) { App.Current.ShowMain("settings"); e.Handled = true; } };
        }
    }

    private async Task Refresh(bool force)
    {
        _body.Children.Clear();
        var p = Hub.Settings.Privacy;
        if (!Hub.Weather.Enabled)
        {
            if (Line)
            {
                // Still one quiet line on the wallpaper while weather is off, not a paragraph; a double-click leads to the switch.
                _body.Children.Add(LineRow(LineIcons.Cloud, "Weather", "Off · double-click to turn on", "T.Text3"));
                return;
            }
            _body.Children.Add(WidgetLook.Head("Weather", WidgetLook.Data("off", 10.5, "T.Text3")));
            _body.Children.Add(Ui.Text("It needs the internet, so it waits until you turn it on and choose a place.", "Text.Caption", 12.5));
            _body.Children.Add(Ui.Button("Turn on in Privacy ›", () => App.Current.ShowMain("settings"), "Button.Ghost").With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-8, 6, 0, 0); b.FontSize = 12; b.Padding = new Thickness(8, 4, 8, 4); }));
            return;
        }
        try
        {
            var r = await Hub.Weather.GetAsync(force);
            if (r is null || !Running) return;
            _body.Children.Clear();
            var unit = p.WeatherFahrenheit ? "°F" : "°C";
            var (text, icon) = WeatherService.Describe(r.Code);
            var place = string.IsNullOrWhiteSpace(p.WeatherPlace) ? "Weather" : p.WeatherPlace;
            if (Line)
            {
                // The line look, for desktop skins: the sky as a line icon, the place and "Cloudy · 24°C" in spaced capitals.
                _body.Children.Add(LineRow(icon, place, $"{text} · {r.Temperature:0}{unit}", "T.Accent"));
                return;
            }
            _body.Children.Add(WidgetLook.Head(place));
            var top = Ui.Grid("auto,*");
            top.Children.Add(Ui.LineIcon(icon, 38, "T.Accent", 1.5).With(v => v.VerticalAlignment = VerticalAlignment.Center));
            var sp = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(WidgetLook.Big(40).With(t => t.Text = $"{r.Temperature:0}°"));
            sp.Children.Add(Ui.Text($"{text} · feels {r.Feels:0}{unit}", "Text.Caption", 12).With(t => t.Margin = new Thickness(0, 3, 0, 0)));
            top.Children.Add(sp.At(0, 1));
            _body.Children.Add(top);
            if (Instance.Height < ForecastHeight) return;   // Medium: the place and now; the next days need a taller widget
            _body.Children.Add(new Shapes.Rectangle { Height = 1, Opacity = 0.7, Margin = new Thickness(0, 14, 0, 12) }.With(x => x.SetResourceReference(Shapes.Shape.FillProperty, "T.Track")));
            var days = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var d in r.Days.Skip(1).Take(3))
            {
                var cell = new StackPanel();
                cell.Children.Add(WidgetLook.Caps(d.Date.ToString("ddd", CultureInfo.CurrentCulture), 10).With(t => t.HorizontalAlignment = HorizontalAlignment.Left));
                cell.Children.Add(Ui.LineIcon(WeatherService.Describe(d.Code).Icon, 17, "T.Text").With(t => { t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 6, 0, 6); }));
                cell.Children.Add(WidgetLook.Data($"{d.Max:0}° {d.Min:0}°", 11.5, "T.Text"));
                days.Children.Add(cell);
            }
            _body.Children.Add(days);
        }
        catch (Exception ex)
        {
            _body.Children.Clear();
            if (Line) { _body.Children.Add(LineRow(LineIcons.Cloud, "Weather", "Unavailable right now", "T.Text3").With(r => r.ToolTip = ex.Message)); return; }
            _body.Children.Add(Ui.Text("Weather unavailable", "Text.Sub", 15));
            _body.Children.Add(Ui.Text(ex.Message, "Text.Caption", 12));
        }
    }

    private bool Line => Instance.Variant == "line";

    /// <summary>The line look: a line icon, then a title and a detail in spaced capitals.</summary>
    private static Grid LineRow(string icon, string title, string detail, string iconBrush)
    {
        var row = Ui.Grid("auto,*");
        row.Children.Add(Ui.LineIcon(icon, 36, iconBrush, 1.5).With(v => v.VerticalAlignment = VerticalAlignment.Center));
        var lines = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(WidgetLook.Caps(title, 13, "T.Text"));
        lines.Children.Add(WidgetLook.Caps(detail, 10.5, "T.Text2").With(t => t.Margin = new Thickness(0, 4, 0, 0)));
        row.Children.Add(lines.At(0, 1));
        return row;
    }

    protected override async void OnStart() { _timer.Start(); await Refresh(false); }
    protected override void OnStop() => _timer.Stop();
    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Style", "variant", ("Card", ""), ("Line", "line")));
        items.Add(Item("Refresh now", async () => await Refresh(true)));
    }
}
