using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WinOrbit.Design;

/// <summary>Glyph codes from Segoe Fluent Icons (falls back to Segoe MDL2 Assets on Windows 10).</summary>
public static class Glyph
{
    public const string Usb = "";
    public const string Home = "", Color = "", Personalize = "", Picture = "", More = "", Tiles = "",
        Drop = "", System = "", Profiles = "", Settings = "", Info = "", Play = "", Pause = "",
        Next = "", Previous = "", Volume = "", Mute = "", Clock = "", Calendar = "", Stopwatch = "",
        Add = "", Delete = "", Edit = "", Save = "", Copy = "", Refresh = "", Cancel = "",
        Check = "", Right = "", Left = "", Down = "", Up = "", Pin = "", Lock = "", Unlock = "",
        Folder = "", OpenFile = "", Download = "", Import = "", Export = "", Search = "", Star = "",
        StarFill = "", Undo = "", Redo = "", View = "", Hide = "", Wifi = "", Bluetooth = "",
        Brightness = "", Moon = "", Note = "", Game = "", Education = "", Work = "", Globe = "",
        Shield = "", Warning = "", Error = "", Link = "", Music = "", List = "", Launch = "",
        Chip = "", Diagnostic = "", Monitor = "", Cloud = "", Filter = "", Remove = "", Page = "",
        Share = "", Photo = "", Ethernet = "", Zoom = "", Battery = "", Sound = "", Keyboard = "";
}

/// <summary>A tiny filled line chart. Redraws only when <see cref="Update"/> is called.</summary>
public sealed class Sparkline : FrameworkElement
{
    private double[] _values = Array.Empty<double>();
    private double _max = 100;

    public Sparkline()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    /// <param name="max">Fixed scale, or 0 to scale to the largest sample.</param>
    public void Update(double[] values, double max = 100)
    {
        _values = values;
        _max = max;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_values.Length < 2 || ActualWidth < 4 || ActualHeight < 4) return;
        var max = _max > 0 ? _max : Math.Max(1, _values.Max() * 1.15);
        var stroke = TryFindResource("T.Accent") as Brush ?? Brushes.DodgerBlue;
        double w = ActualWidth, h = ActualHeight - 1, step = w / (_values.Length - 1);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var c = line.Open())
        using (var a = area.Open())
        {
            a.BeginFigure(new Point(0, h + 1), true, true);
            for (var i = 0; i < _values.Length; i++)
            {
                var p = new Point(i * step, h - Math.Clamp(_values[i] / max, 0, 1) * (h - 2));
                if (i == 0) c.BeginFigure(p, false, false); else c.LineTo(p, true, true);
                a.LineTo(p, false, false);
            }
            a.LineTo(new Point(w, h + 1), false, false);
        }
        line.Freeze(); area.Freeze();
        var fill = stroke.Clone(); fill.Opacity = 0.16; fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        var pen = new Pen(stroke, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}

/// <summary>Circular progress ring.</summary>
public sealed class Ring : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public Ring() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8) return;
        var r = (size - Thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var track = new Pen(TryFindResource("T.Track") as Brush ?? Brushes.Gray, Thickness);
        dc.DrawEllipse(null, track, c, r, r);
        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0.001) return;
        var pen = new Pen(TryFindResource("T.Accent") as Brush ?? Brushes.DodgerBlue, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        var angle = v * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Three animated bars shown while music plays. Runs at 20 fps and stops completely when paused.</summary>
public sealed class Equalizer : StackPanel
{
    private readonly ScaleTransform[] _scales = new ScaleTransform[3];
    private bool _running;

    public Equalizer()
    {
        Orientation = Orientation.Horizontal;
        Height = 14;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        for (var i = 0; i < 3; i++)
        {
            _scales[i] = new ScaleTransform(1, 0.35);
            var bar = new Rectangle { Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(1, 0, 1, 0), RenderTransformOrigin = new Point(0.5, 1), RenderTransform = _scales[i] };
            bar.SetResourceReference(Shape.FillProperty, "T.Accent");
            Children.Add(bar);
        }
    }

    /// <summary>When true, bar heights come from <see cref="Levels"/> (real audio) instead of the built-in loop.</summary>
    public bool External { get; set; }

    /// <summary>Set the three bar heights (0..1) directly — used for audio-reactive bars.</summary>
    public void Levels(double a, double b, double c)
    {
        _scales[0].ScaleY = Math.Clamp(a, 0.18, 1);
        _scales[1].ScaleY = Math.Clamp(b, 0.18, 1);
        _scales[2].ScaleY = Math.Clamp(c, 0.18, 1);
    }

    /// <summary>Colour the bars (e.g. from album art); null returns to the theme accent.</summary>
    public void Tint(Color? color)
    {
        foreach (var bar in Children.OfType<Rectangle>())
        {
            if (color is { } c) bar.Fill = new SolidColorBrush(c);
            else bar.SetResourceReference(Shape.FillProperty, "T.Accent");
        }
    }

    public void Set(bool playing)
    {
        if (playing == _running) return;
        _running = playing;
        if (External)
        {
            for (var i = 0; i < 3; i++) { _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null); _scales[i].ScaleY = 0.3; }
            return;
        }
        var durations = new[] { 520, 380, 640 };
        for (var i = 0; i < 3; i++)
        {
            if (playing && Motion.Intensity > 0.01)
            {
                var a = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(durations[i])) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Motion.EaseInOut };
                Timeline.SetDesiredFrameRate(a, 20);
                _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _scales[i].ScaleY = playing ? 0.7 : 0.35;
            }
        }
    }
}

/// <summary>Month calendar grid with today highlighted. Follows the user's regional first day of week.</summary>
public sealed class MonthView : Grid
{
    private readonly UniformGrid _days = new() { Columns = 7, Rows = 6 };
    private readonly UniformGrid _header = new() { Columns = 7, Rows = 1 };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public double CellSize { get; set; } = 26;

    public MonthView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Grid { Margin = new Thickness(4, 0, 0, 6) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _title.SetResourceReference(TextBlock.ForegroundProperty, "T.Text");
        top.Children.Add(_title);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(NavButton(Glyph.Left, -1, "Previous month"));
        nav.Children.Add(NavButton(Glyph.Right, 1, "Next month"));
        SetColumn(nav, 1);
        top.Children.Add(nav);
        Children.Add(top);

        SetRow(_header, 1);
        Children.Add(_header);
        SetRow(_days, 2);
        Children.Add(_days);
        Loaded += (_, _) => Build();
    }

    private Button NavButton(string glyph, int delta, string name)
    {
        var b = new Button { Content = glyph, Width = 26, Height = 26, FontSize = 11, ToolTip = name };
        b.SetResourceReference(StyleProperty, "Button.Icon");
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => { _month = _month.AddMonths(delta); Build(); };
        return b;
    }

    public void ShowToday()
    {
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Build();
    }

    public void Build()
    {
        var culture = CultureInfo.CurrentCulture;
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        _title.Text = _month.ToString("MMMM yyyy", culture);

        _header.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            var t = new TextBlock { Text = culture.DateTimeFormat.GetShortestDayName(day), FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "T.Text3");
            _header.Children.Add(t);
        }

        _days.Children.Clear();
        var offset = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        var start = _month.AddDays(-offset);
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var inMonth = date.Month == _month.Month;
            var cell = new Grid { Width = CellSize, Height = CellSize, HorizontalAlignment = HorizontalAlignment.Center };
            var text = new TextBlock { Text = date.Day.ToString(culture), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (date == DateTime.Today)
            {
                var dot = new Ellipse();
                dot.SetResourceReference(Shape.FillProperty, "T.Accent");
                cell.Children.Add(dot);
                text.FontWeight = FontWeights.SemiBold;
                text.SetResourceReference(TextBlock.ForegroundProperty, "T.OnAccent");
            }
            else text.SetResourceReference(TextBlock.ForegroundProperty, inMonth ? "T.Text" : "T.Text3");
            if (!inMonth) text.Opacity = 0.55;
            cell.Children.Add(text);
            _days.Children.Add(cell);
        }
    }
}

/// <summary>Small UI construction helpers for code-built views.</summary>
public static class Ui
{
    public static TextBlock Text(string text, string style = "Text.Body", double? size = null, FontWeight? weight = null, string? brush = null)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (size is { } s) t.FontSize = s;
        if (weight is { } w) t.FontWeight = w;
        if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string brush = "T.Text")
    {
        var t = new TextBlock { Text = glyph, FontSize = size };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Icon");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>A <see cref="LineIcons"/> drawing (24×24 path data) at the given size, in a theme brush.</summary>
    public static FrameworkElement LineIcon(string pathData, double size = 16, string brush = "T.Text", double stroke = 1.7)
    {
        var path = LinePath(pathData, stroke);
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brush);
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    /// <summary>Same, in a fixed colour (the system monitor gives each metric its own).</summary>
    public static FrameworkElement LineIcon(string pathData, double size, Brush brush, double stroke = 1.7)
    {
        var path = LinePath(pathData, stroke);
        path.Stroke = brush;
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    private static System.Windows.Shapes.Path LinePath(string data, double stroke) => new()
    {
        Data = Geometry.Parse(data),
        StrokeThickness = stroke,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    /// <summary>Per-metric icon colours for the system monitor (soft, readable on dark and light glass).</summary>
    public static Brush MetricBrush(string id)
    {
        var c = id switch
        {
            "cpu" => Color.FromRgb(0x4C, 0xC9, 0xF0),
            "ram" => Color.FromRgb(0xF5, 0xB8, 0x3D),
            "gpu" => Color.FromRgb(0xFF, 0x7A, 0x7A),
            "net" => Color.FromRgb(0x5E, 0xEA, 0xD4),
            "disk" => Color.FromRgb(0xA7, 0x8B, 0xFA),
            _ => Color.FromRgb(0x7D, 0xD3, 0xFC),
        };
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static string MetricIcon(string id) => id switch
    {
        "cpu" => LineIcons.Cpu,
        "ram" => LineIcons.Memory,
        "gpu" => LineIcons.Gpu,
        "net" => LineIcons.Network,
        "disk" => LineIcons.Disk,
        _ => LineIcons.Clock,
    };

    public static Button IconButton(string glyph, string tooltip, Action click, double size = 34, string style = "Button.Icon")
    {
        var b = new Button { Content = glyph, ToolTip = tooltip, Width = size, Height = size, FontSize = Math.Max(11, size * 0.42) };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Button(string text, Action click, string? style = null)
    {
        var b = new Button { Content = text };
        if (style is not null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.Click += (_, _) => click();
        return b;
    }

    public static T With<T>(this T element, Action<T> configure) where T : DependencyObject
    {
        configure(element);
        return element;
    }

    public static T At<T>(this T element, int row, int column, int rowSpan = 1, int columnSpan = 1) where T : UIElement
    {
        System.Windows.Controls.Grid.SetRow(element, row); System.Windows.Controls.Grid.SetColumn(element, column);
        if (rowSpan > 1) System.Windows.Controls.Grid.SetRowSpan(element, rowSpan);
        if (columnSpan > 1) System.Windows.Controls.Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    public static Grid Grid(string columns = "*", string rows = "*")
    {
        var g = new Grid();
        foreach (var c in columns.Split(',')) g.ColumnDefinitions.Add(new ColumnDefinition { Width = Length(c) });
        foreach (var r in rows.Split(',')) g.RowDefinitions.Add(new RowDefinition { Height = Length(r) });
        return g;
    }

    private static GridLength Length(string s)
    {
        s = s.Trim();
        if (s.Equals("auto", StringComparison.OrdinalIgnoreCase)) return GridLength.Auto;
        if (s.EndsWith('*')) return new GridLength(s.Length == 1 ? 1 : double.Parse(s[..^1], CultureInfo.InvariantCulture), GridUnitType.Star);
        return new GridLength(double.Parse(s, CultureInfo.InvariantCulture));
    }
}
