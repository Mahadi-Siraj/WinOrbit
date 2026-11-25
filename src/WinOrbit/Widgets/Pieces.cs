using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;

namespace WinOrbit.Widgets;

// Matching pieces: small text-and-line widgets made to sit beside the clock skins, straight on the wallpaper.
// Each one does its work only while it is on screen.

// =====================================================================================
/// <summary>
/// The battery as a slim outline that fills with the charge, the percentage beside it and one line under it
/// (charging, time left). Windows tells WinOrbit when the charge or the power source changes; nothing is polled.
/// A PC without a battery shows a plug and "AC power".
/// </summary>
internal sealed class BatteryWidget : WidgetView
{
    private readonly Shapes.Rectangle _fill = new() { RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Grid _cell = new() { Width = 16, Height = 28, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _plug = Ui.Icon("", 22, "T.Text");   // a lightning bolt: mains power
    private readonly TextBlock _value = WidgetLook.Big(30);
    private readonly TextBlock _line = WidgetLook.Caps("", 10, "T.Text2");

    public BatteryWidget(WidgetInstance i) : base(i)
    {
        Background = Brushes.Transparent;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        // The cell: a cap on top, an outline, and a fill that rises with the charge.
        var cap = new Shapes.Rectangle { Width = 6, Height = 2.5, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        cap.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        var body = new Border { Margin = new Thickness(0, 3.5, 0, 0), BorderThickness = new Thickness(1.4), CornerRadius = new CornerRadius(3.5), Padding = new Thickness(2) };
        body.SetResourceReference(Border.BorderBrushProperty, "T.Text");
        _fill.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        body.Child = new Grid { Children = { _fill } };
        _cell.Children.Add(cap); _cell.Children.Add(body);
        _plug.Margin = new Thickness(0, 0, 12, 0);
        _plug.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_cell); row.Children.Add(_plug);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _value.Text = "—";
        _line.Margin = new Thickness(1, 3, 0, 0);
        text.Children.Add(_value); text.Children.Add(_line);
        row.Children.Add(text);
        Children.Add(row);
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Island.IslandWindow.OpenUri("ms-settings:batterysaver"); e.Handled = true; } };
    }

    private void Refresh()
    {
        var s = Native.GetSystemPowerStatus(out var p) ? Describe(p.ACLineStatus, p.BatteryFlag, p.BatteryLifePercent, p.BatteryLifeTime) : Describe(255, 255, 255, -1);
        _cell.Visibility = s.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        _plug.Visibility = s.HasBattery ? Visibility.Collapsed : Visibility.Visible;
        _value.Text = s.Value;
        WidgetLook.SetCaps(_line, s.Line);
        // 21 px of room inside the outline; a sliver stays even when nearly empty, so it never reads as missing.
        _fill.Height = s.Percent is { } pct ? Math.Max(2, 21 * pct / 100.0) : 0;
        _fill.SetResourceReference(Shapes.Shape.FillProperty, s.Percent is <= 15 && !s.Charging ? "T.Warn" : "T.Text");
    }

    /// <summary>What the widget shows for Windows' power status: the figure, the line under it, and the fill level.</summary>
    internal static (bool HasBattery, int? Percent, bool Charging, string Value, string Line) Describe(byte acLine, byte flags, byte percent, int secondsLeft)
    {
        // BatteryFlag 128 = no system battery, 255 = unknown; percent 255 = unknown.
        if (flags == 255 && percent == 255 && acLine == 255) return (false, null, false, "—", "Power unknown");
        if ((flags & 128) != 0 || percent > 100) return (false, null, false, "AC", "No battery");
        var charging = acLine == 1;
        var full = charging && percent >= 100;
        var line = full ? "Fully charged"
            : charging ? "Charging"
            : secondsLeft > 0 ? Left(TimeSpan.FromSeconds(secondsLeft)) + " left"
            : "On battery";
        return (true, percent, charging, $"{percent}%", line);
    }

    /// <summary>Time left on battery, short: "3 h 10 min", "45 min".</summary>
    internal static string Left(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{Math.Max(1, t.Minutes)} min";

    private void OnPower(bool _) => Refresh();
    private void OnPercent(int _) => Refresh();

    protected override void OnStart()
    {
        Refresh();
        if (!Hub.Ready) return;
        Hub.Power.PowerSourceChanged += OnPower;
        Hub.Power.BatteryChanged += OnPercent;
    }

    protected override void OnStop()
    {
        if (!Hub.Ready) return;
        Hub.Power.PowerSourceChanged -= OnPower;
        Hub.Power.BatteryChanged -= OnPercent;
    }

    public override void BuildMenu(ItemCollection items) => items.Add(Item("Open battery settings", () => Island.IslandWindow.OpenUri("ms-settings:batterysaver")));
}

// =====================================================================================
/// <summary>
/// Drive space as diamonds: each fixed drive gets a small diamond that fills from the bottom as the drive fills,
/// with its letter, the free space and the percentage used beside it in a slanted tag. Read from Windows every
/// five minutes while visible (and when the pointer comes over it); only fixed drives, never network drives.
/// </summary>
internal sealed class DrivesWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
    private readonly StackPanel _list = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private string _shown = "";
    internal const int MaxDrives = 4;

    public DrivesWidget(WidgetInstance i) : base(i)
    {
        Background = Brushes.Transparent;
        Children.Add(_list);
        _timer.Tick += (_, _) => Refresh();
        MouseEnter += (_, _) => Refresh();
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Island.IslandWindow.OpenUri("shell:MyComputerFolder"); e.Handled = true; } };
    }

    internal readonly record struct DriveFacts(string Name, bool Fixed, bool Ready, long Total, long Free);

    /// <summary>The drives worth showing: fixed and ready, with a size, at most <see cref="MaxDrives"/>, in letter order.</summary>
    internal static List<DriveFacts> Pick(IEnumerable<DriveFacts> drives) =>
        drives.Where(d => d.Fixed && d.Ready && d.Total > 0).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Take(MaxDrives).ToList();

    /// <summary>How full a drive is, 0..100.</summary>
    internal static double UsedPercent(long total, long free) => total <= 0 ? 0 : Math.Clamp((total - Math.Clamp(free, 0, total)) * 100.0 / total, 0, 100);

    private static IEnumerable<DriveFacts> Read()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            DriveFacts f;
            try
            {
                var isFixed = d.DriveType == DriveType.Fixed;
                f = isFixed && d.IsReady ? new DriveFacts(d.Name, true, true, d.TotalSize, d.AvailableFreeSpace) : new DriveFacts(d.Name, isFixed, false, 0, 0);
            }
            catch { continue; }
            yield return f;
        }
    }

    private void Refresh()
    {
        List<DriveFacts> drives;
        try { drives = Pick(Read()); } catch { drives = new(); }
        var key = string.Join("|", drives.Select(d => $"{d.Name}{d.Total}/{d.Free / 104857600}"));   // redraw when free space moves by 100 MB
        if (key == _shown && _list.Children.Count > 0) return;
        _shown = key;
        _list.Children.Clear();
        if (drives.Count == 0) { _list.Children.Add(WidgetLook.Caps("No drives to show", 10.5)); return; }
        foreach (var d in drives) _list.Children.Add(Row(d));
    }

    private static Grid Row(DriveFacts d)
    {
        var used = UsedPercent(d.Total, d.Free);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The tag: a slanted plate with the letter and the free space.
        var tag = new Grid { Height = 26, VerticalAlignment = VerticalAlignment.Center };
        var plate = new Shapes.Path { Stretch = Stretch.Fill, Data = Geometry.Parse("M 8,0 L 100,0 L 92,26 L 0,26 Z"), StrokeThickness = 1, Opacity = 0.9 };
        plate.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text3");
        var plateFill = new Shapes.Path { Stretch = Stretch.Fill, Data = plate.Data, Opacity = 0.12 };
        plateFill.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        tag.Children.Add(plateFill); tag.Children.Add(plate);
        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(WidgetLook.Caps(d.Name.TrimEnd('\\'), 10.5, "T.Text").With(t => t.Margin = new Thickness(0, 0, 10, 0)));
        label.Children.Add(WidgetLook.Data(Fmt.Size(d.Free) + " free", 11, "T.Text2"));
        tag.Children.Add(label);
        row.Children.Add(tag);

        // The diamond: an outline, filled from the bottom to the share in use, the percentage inside.
        var diamond = new Grid { Width = 34, Height = 34, Margin = new Thickness(-4, 0, 0, 0), ToolTip = $"{d.Name.TrimEnd('\\')} {used:0}% used · {Fmt.Size(d.Free)} free of {Fmt.Size(d.Total)}" };
        var shape = Geometry.Parse("M 17,1 L 33,17 L 17,33 L 1,17 Z");
        var fill = new Shapes.Path { Data = shape, Opacity = 0.85 };
        fill.SetResourceReference(Shapes.Shape.FillProperty, used >= 90 ? "T.Warn" : "T.Accent");
        var top = 34 * (1 - used / 100);
        fill.Clip = new RectangleGeometry(new Rect(0, top, 34, 34 - top));
        var back = new Shapes.Path { Data = shape, Opacity = 0.14 };
        back.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        var outline = new Shapes.Path { Data = shape, StrokeThickness = 1.2 };
        outline.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text");
        var pct = WidgetLook.Data($"{used:0}", 9.5, "T.Text").With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
        diamond.Children.Add(back); diamond.Children.Add(fill); diamond.Children.Add(outline); diamond.Children.Add(pct);
        Grid.SetColumn(diamond, 1);
        row.Children.Add(diamond);
        return row;
    }

    protected override void OnStart() { Refresh(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();
    public override void BuildMenu(ItemCollection items) => items.Add(Item("Open This PC", () => Island.IslandWindow.OpenUri("shell:MyComputerFolder")));
}

// =====================================================================================
/// <summary>
/// A quote line: a new quote each day from a small built-in set (or your own), the author in the accent colour,
/// and "Next quote" to move on. Everything is on this PC; nothing is fetched.
/// </summary>
internal sealed class QuoteWidget : WidgetView
{
    /// <summary>Short, well-known lines with their usual attribution.</summary>
    internal static readonly (string Text, string Author)[] BuiltIn =
    {
        ("The best way to predict the future is to invent it.", "Alan Kay"),
        ("Simplicity is prerequisite for reliability.", "Edsger W. Dijkstra"),
        ("Less is more.", "Ludwig Mies van der Rohe"),
        ("Good design is as little design as possible.", "Dieter Rams"),
        ("Form follows function.", "Louis Sullivan"),
        ("Stay hungry. Stay foolish.", "Steve Jobs"),
        ("Talk is cheap. Show me the code.", "Linus Torvalds"),
        ("Make it work, make it right, make it fast.", "Kent Beck"),
        ("Programs must be written for people to read, and only incidentally for machines to execute.", "Harold Abelson"),
        ("Imagination is more important than knowledge.", "Albert Einstein"),
        ("The journey of a thousand miles begins with one step.", "Lao Tzu"),
        ("Well done is better than well said.", "Benjamin Franklin"),
        ("We are what we repeatedly do.", "Will Durant"),
        ("Fortune favours the bold.", "Virgil"),
        ("Seize the day.", "Horace"),
        ("The unexamined life is not worth living.", "Socrates"),
        ("I think, therefore I am.", "René Descartes"),
        ("Hope is the thing with feathers.", "Emily Dickinson"),
        ("Not all those who wander are lost.", "J. R. R. Tolkien"),
        ("The only thing we have to fear is fear itself.", "Franklin D. Roosevelt"),
        ("Dwell on the beauty of life. Watch the stars, and see yourself running with them.", "Marcus Aurelius"),
        ("The happiness of your life depends upon the quality of your thoughts.", "Marcus Aurelius"),
    };

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly TextBlock _text = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _author = WidgetLook.Caps("", 10.5, "T.Accent");
    private int _offset;

    public QuoteWidget(WidgetInstance i) : base(i)
    {
        Background = Brushes.Transparent;
        _text.SetResourceReference(TextBlock.ForegroundProperty, "T.Text");
        _offset = int.TryParse(Instance.Opt("offset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) ? o : 0;
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var mark = new TextBlock { Text = "“", FontSize = 30, FontWeight = FontWeights.Bold, LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 0, 10, -8), VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "T.Accent");
        var next = WidgetLook.Caps("› Next quote", 10, "T.Text2");
        next.Cursor = Cursors.Hand;
        next.VerticalAlignment = VerticalAlignment.Center;
        next.MouseLeftButtonUp += (_, e) => { Next(); e.Handled = true; };
        head.Children.Add(mark); head.Children.Add(next);
        sp.Children.Add(head);
        sp.Children.Add(_text);
        _author.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(_author);
        Children.Add(sp);
        _timer.Tick += (_, _) => Show();
    }

    private List<(string Text, string Author)> Quotes()
    {
        var mine = Parse(Instance.Opt("mine"));
        return Instance.Opt("only") == "1" && mine.Count > 0 ? mine : BuiltIn.Concat(mine).ToList();
    }

    /// <summary>Your own quotes, stored one per line as "text<tab>author".</summary>
    internal static List<(string Text, string Author)> Parse(string stored) =>
        stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('\t') is var p && p.Length > 1 ? (p[0].Trim(), p[1].Trim()) : (l, ""))
            .Where(q => q.Item1.Length > 0).ToList();

    /// <summary>Today's quote: one per day in turn, moved on by "Next quote".</summary>
    internal static int Pick(int count, DateTime day, int offset) =>
        count <= 0 ? -1 : (int)(((day.Date - new DateTime(2024, 1, 1)).Days + (long)offset) % count + count) % count;

    private void Show()
    {
        var all = Quotes();
        var now = DateTime.Now;
        var i = Pick(all.Count, now, _offset);
        if (i < 0) { _text.Text = ""; _author.Text = ""; return; }
        _text.Text = all[i].Text;
        WidgetLook.SetCaps(_author, all[i].Author);
        _author.Visibility = all[i].Author.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _timer.Interval = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(2);   // the next one at midnight
    }

    private void Next()
    {
        _offset++;
        Instance.Options["offset"] = _offset.ToString(CultureInfo.InvariantCulture);
        Save();
        Show();
    }

    protected override void OnStart() { Show(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Item("Next quote", Next));
        items.Add(Item("Add my own quote…", () =>
        {
            if (Dialogs.Prompt("Add a quote", "The quote. It stays on this PC.", "") is not { Length: > 0 } text) return;
            var author = Dialogs.Prompt("Add a quote", "Who said it? (Leave empty for none.)", "") ?? "";
            var line = text.Replace('\t', ' ').Replace('\n', ' ').Trim() + "\t" + author.Replace('\t', ' ').Replace('\n', ' ').Trim();
            var mine = Instance.Opt("mine");
            Instance.Options["mine"] = mine.Length > 0 ? mine + "\n" + line : line;
            // Show the new one next.
            var all = Quotes();
            _offset += (all.Count - 1 - Pick(all.Count, DateTime.Now, _offset) + all.Count) % all.Count;
            Instance.Options["offset"] = _offset.ToString(CultureInfo.InvariantCulture);
            Save(); Show();
        }));
        var hasMine = Parse(Instance.Opt("mine")).Count > 0;
        items.Add(Item("Only my quotes", () => { Instance.Options["only"] = Instance.Opt("only") == "1" ? "0" : "1"; Save(); Show(); }, Instance.Opt("only") == "1").With(m => m.IsEnabled = hasMine));
        items.Add(Item("Remove my quotes…", () =>
        {
            if (!Dialogs.Confirm("Remove your quotes?", "The quotes you added to this widget are removed. The built-in ones stay.", "Remove")) return;
            Instance.Options.Remove("mine"); Instance.Options.Remove("only"); Save(); Show();
        }).With(m => m.IsEnabled = hasMine));
    }
}

// =====================================================================================
/// <summary>
/// A thin line that moves with the music: the loudness of what is playing, newest in the middle and spreading out
/// to both sides. It reads Windows' output level 20 times a second only while something is playing and the widget is
/// on screen; otherwise (and with Reduce motion on) it rests as a flat line and costs nothing.
/// The level is one number for all sound, so the line shows loudness, not separate frequencies.
/// </summary>
internal sealed class VisualiserWidget : WidgetView
{
    internal const int History = 48;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly LineTrace _trace = new();
    private readonly double[] _history = new double[History];
    private readonly Random _rng = new();
    private double _level;
    private int _ticks;

    public VisualiserWidget(WidgetInstance i) : base(i)
    {
        _trace.SetResourceReference(LineTrace.StrokeProperty, "T.Text");
        Children.Add(_trace);
        _timer.Tick += (_, _) => Tick();
    }

    private bool ShouldRun => Running && Hub.Ready && Hub.Media.IsPlaying && Motion.Intensity > 0.01;

    private void Drive()
    {
        if (ShouldRun) { if (!_timer.IsEnabled) _timer.Start(); return; }
        _timer.Stop();
        Array.Clear(_history);
        _level = 0;
        _trace.Show(Shape(_history, 0));
    }

    private void Tick()
    {
        if (!ShouldRun) { Drive(); return; }
        var target = Math.Sqrt(Hub.Audio.Peak());   // perceptual: quiet passages still move
        _level = target > _level ? _level + (target - _level) * 0.75 : _level * 0.8 + target * 0.2;
        Array.Copy(_history, 0, _history, 1, History - 1);
        _history[0] = _level * (0.55 + 0.45 * _rng.NextDouble());
        _ticks = (_ticks + 1) & 0xFFFF;
        _trace.Show(Shape(_history, _ticks));
    }

    /// <summary>
    /// The line's heights (0..1, positive up) across the width: the newest level in the middle, older ones further out
    /// on both sides, fading to nothing at the ends; every other sample dips a little below the line, like a trace.
    /// <paramref name="tick"/> counts the samples taken, so each sample keeps its direction as it travels outwards.
    /// </summary>
    internal static double[] Shape(IReadOnlyList<double> history, int tick)
    {
        var n = history.Count;
        var points = new double[n * 2 + 1];
        var half = n;
        for (var k = 0; k < points.Length; k++)
        {
            var age = Math.Abs(k - half);          // 0 in the middle
            var v = age < n ? history[age] : 0;
            var window = 1 - Math.Pow(age / (double)(n + 1), 2);
            var dip = ((tick - age) & 1) == 0 ? 1 : -0.22;
            points[k] = Math.Clamp(v * window * dip, -1, 1);
        }
        return points;
    }

    private void OnMedia() => Drive();
    protected override void OnStart() { if (Hub.Ready) Hub.Media.Changed += OnMedia; Drive(); }
    protected override void OnStop() { if (Hub.Ready) Hub.Media.Changed -= OnMedia; Drive(); }

    /// <summary>Draws the trace: a smooth line through the points, resting on a baseline two thirds of the way down.</summary>
    internal sealed class LineTrace : FrameworkElement
    {
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(LineTrace),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        private double[] _points = new double[History * 2 + 1];

        public LineTrace() { IsHitTestVisible = false; }

        public void Show(double[] points) { _points = points; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4 || _points.Length < 2) return;
            var baseline = h * 0.66;
            var amp = h * 0.6;
            var step = w / (_points.Length - 1);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, baseline - _points[0] * amp), false, false);
                for (var k = 1; k < _points.Length; k++)
                {
                    var prev = new Point((k - 1) * step, baseline - _points[k - 1] * amp);
                    var here = new Point(k * step, baseline - _points[k] * amp);
                    c.QuadraticBezierTo(prev, new Point((prev.X + here.X) / 2, (prev.Y + here.Y) / 2), true, true);
                }
                c.LineTo(new Point(w, baseline - _points[^1] * amp), true, true);
            }
            g.Freeze();
            var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }
}
