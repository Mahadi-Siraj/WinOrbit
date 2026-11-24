using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using WinOrbit.Design;
using WinOrbit.Services;

namespace WinOrbit.Widgets;

/// <summary>
/// The desktop-skin clocks: typographic layouts in the spirit of the setups people share (spaced weekdays, a stack
/// between hairlines, a poster numeral, a boxed label), built from WinOrbit's own Orbit letters and fonts Windows
/// ships with. Each style is a small layout plus an update that runs once a minute.
/// </summary>
internal sealed partial class ClockWidget
{
    private static readonly FontFamily HeavyFont = new("Segoe UI Black, Arial Black, Segoe UI");
    private static readonly FontFamily SerifFont = new("Georgia, Sitka Text, Cambria");
    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily LabelFont = new("Bahnschrift SemiBold Condensed, Bahnschrift Condensed, Bahnschrift, Segoe UI");

    private static TextBlock Centred(TextBlock t)
    {
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = TextAlignment.Center;
        return t;
    }

    /// <summary>The weekday in Orbit letters, far apart; the date in spaced capitals; the time between dashes.</summary>
    private FrameworkElement BuildOrbit()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var day = new OrbitText(34, 9, 1.15) { HorizontalAlignment = HorizontalAlignment.Center };
        var date = Centred(T(12.5, FontWeights.SemiBold));
        date.Margin = new Thickness(0, 18, 0, 0);
        var time = Centred(T(13, FontWeights.SemiBold, "T.Text2"));
        time.Margin = new Thickness(0, 9, 0, 0);
        sp.Children.Add(day); sp.Children.Add(date); sp.Children.Add(time);
        _update = now =>
        {
            day.Text = now.ToString("dddd", Ci);
            date.Text = Track(now.ToString("d MMMM, yyyy", Ci).ToUpper(Ci) + ".", Thin);
            time.Text = "-   " + TimeWithMarker(now) + "   -";
        };
        return sp;
    }

    /// <summary>"11:30 AM", or "23:30" on a 24-hour clock.</summary>
    private static string TimeWithMarker(DateTime now) => H24 ? Fmt.Time(now, true) : Fmt.Time(now, false) + " " + now.ToString("tt", Ci).ToUpper(Ci);

    /// <summary>A hairline that fades out towards its far end.</summary>
    private static Shapes.Rectangle Hairline(double length, bool fadeDown)
    {
        var r = new Shapes.Rectangle { Width = 1.4, Height = length, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true };
        r.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(fadeDown ? (byte)255 : (byte)0, 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(fadeDown ? (byte)0 : (byte)255, 0, 0, 0), 1));
        mask.Freeze();
        r.OpacityMask = mask;
        return r;
    }

    /// <summary>A centred stack between two hairlines: the greeting, the short weekday in heavy Orbit letters, the date and the time.</summary>
    private FrameworkElement BuildHairline()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var hello = Centred(T(11.5, FontWeights.SemiBold, "T.Text2"));
        hello.LineHeight = 21; hello.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        hello.Margin = new Thickness(0, 16, 0, 10);
        var day = new OrbitText(40, 4.5, 1.9) { HorizontalAlignment = HorizontalAlignment.Center };
        var date = Centred(T(12, FontWeights.SemiBold));
        date.Margin = new Thickness(0, 14, 0, 0);
        var time = Centred(T(12, FontWeights.SemiBold, "T.Text2"));
        time.Margin = new Thickness(0, 7, 0, 16);
        sp.Children.Add(Hairline(46, false));
        sp.Children.Add(hello); sp.Children.Add(day); sp.Children.Add(date); sp.Children.Add(time);
        sp.Children.Add(Hairline(46, true));
        _update = now =>
        {
            hello.Text = string.Join("\n", Greeting(now).ToUpper(Ci).Split(' ').Select(w => Track(w, Thin)));
            day.Text = now.ToString("ddd", Ci).TrimEnd('.');
            date.Text = Track(now.ToString("dd MMMM", Ci).ToUpper(Ci), Thin);
            time.Text = Track(TimeWithMarker(now), Hair);
        };
        return sp;
    }

    /// <summary>A poster: the day of the month huge in the accent colour, the month in heavy capitals across it, the weekday standing up the side and the time underlined.</summary>
    private FrameworkElement BuildPoster()
    {
        var g = Ui.Grid("auto,auto", "*,auto");
        var side = T(12, FontWeights.SemiBold, "T.Text2");
        side.LayoutTransform = new RotateTransform(-90);
        side.VerticalAlignment = VerticalAlignment.Bottom;
        side.Margin = new Thickness(0, 0, 10, 4);
        var stage = new Grid();
        var num = Tight(T(176, FontWeights.Bold, "T.Accent", CondensedFont), 150);
        num.Opacity = 0.95;
        var month = T(50, FontWeights.Black, "T.Text", HeavyFont);
        month.VerticalAlignment = VerticalAlignment.Bottom;
        month.Margin = new Thickness(26, 0, 0, -2);
        month.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; month.LineHeight = 52;
        stage.Children.Add(num); stage.Children.Add(month);
        var foot = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 12, 0, 0) };
        var time = T(16, FontWeights.SemiBold);
        var underline = Rule(34, 2, new Thickness(0, 5, 0, 0));
        underline.HorizontalAlignment = HorizontalAlignment.Left;
        underline.SetResourceReference(Shapes.Shape.FillProperty, "T.Accent");
        foot.Children.Add(time); foot.Children.Add(underline);
        g.Children.Add(side.At(0, 0, 2)); g.Children.Add(stage.At(0, 1)); g.Children.Add(foot.At(1, 1));
        _update = now =>
        {
            side.Text = Track(now.ToString("dddd", Ci).ToUpper(Ci), Thin);
            num.Text = now.Day.ToString("00", Ci);
            month.Text = now.ToString("MMMM", Ci).ToUpper(Ci);
            time.Text = Fmt.Time(now, H24) + (H24 ? "" : " " + now.ToString("tt", Ci).ToUpper(Ci));
        };
        return g;
    }

    /// <summary>The time in outlined condensed figures, the week underneath with today ringed, and the date set in a serif.</summary>
    private FrameworkElement BuildWeek()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var time = new Shapes.Path { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Bottom };
        time.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text");
        var marker = new Shapes.Path { StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 0) };
        marker.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text3");
        top.Children.Add(time); top.Children.Add(marker);
        var week = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var cells = new List<(TextBlock Letter, Shapes.Ellipse Ring)>();
        for (var k = 0; k < 7; k++)
        {
            week.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            var cell = new Grid { Width = 24, Height = 24 };
            var ring = new Shapes.Ellipse { StrokeThickness = 1.1, Visibility = Visibility.Hidden };
            ring.SetResourceReference(Shapes.Shape.StrokeProperty, "T.Text");
            var letter = T(11, FontWeights.SemiBold, "T.Text2", SerifFont);
            letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
            cell.Children.Add(ring); cell.Children.Add(letter);
            Grid.SetColumn(cell, k);
            week.Children.Add(cell);
            cells.Add((letter, ring));
        }
        var dateLine = new TextBlock { FontFamily = SerifFont, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        var dayMonth = new System.Windows.Documents.Run();
        dayMonth.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "T.Text");
        var year = new System.Windows.Documents.Run();
        year.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "T.Text3");
        dateLine.Inlines.Add(dayMonth); dateLine.Inlines.Add(year);
        sp.Children.Add(top); sp.Children.Add(week); sp.Children.Add(dateLine);
        _update = now =>
        {
            time.Data = Numerals(Fmt.Time(now, H24).PadLeft(H24 ? 5 : 4, '0'), CondensedFont, FontWeights.SemiBold, 84);
            marker.Data = H24 ? Geometry.Empty : Numerals(now.ToString("tt", Ci).ToUpper(Ci), CondensedFont, FontWeights.SemiBold, 40);
            marker.Visibility = H24 ? Visibility.Collapsed : Visibility.Visible;
            var letters = WeekLetters(Ci);
            var today = WeekIndex(now, Ci.DateTimeFormat.FirstDayOfWeek);
            for (var k = 0; k < 7; k++)
            {
                cells[k].Letter.Text = letters[k];
                cells[k].Ring.Visibility = k == today ? Visibility.Visible : Visibility.Hidden;
                cells[k].Letter.SetResourceReference(TextBlock.ForegroundProperty, k == today ? "T.Text" : "T.Text2");
            }
            dayMonth.Text = now.ToString("MMMM, dd", Ci);
            year.Text = "  —  " + now.ToString("yyyy", Ci);
        };
        return sp;
    }

    /// <summary>One letter per day of the week, starting on the culture's first day ("S M T W T F S").</summary>
    internal static string[] WeekLetters(CultureInfo culture)
    {
        var first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        return Enumerable.Range(0, 7).Select(k => StringInfo.GetNextTextElement(names[(first + k) % 7]).ToUpper(culture)).ToArray();
    }

    /// <summary>Where today sits in a week that starts on <paramref name="firstDay"/> (0–6).</summary>
    internal static int WeekIndex(DateTime now, DayOfWeek firstDay) => ((int)now.DayOfWeek - (int)firstDay + 7) % 7;

    /// <summary>
    /// A label: a row of ticks for the day's half hours (the ones gone are lit), the weekday in a thin box and the
    /// time and date punched out of a solid bar, so the wallpaper shows through the letters.
    /// </summary>
    private FrameworkElement BuildLabel()
    {
        const double width = 232;
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = width };
        var ticksOff = new Shapes.Path();
        ticksOff.SetResourceReference(Shapes.Shape.FillProperty, "T.Track");
        var ticksOn = new Shapes.Path();
        ticksOn.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        var ticks = new Grid { Height = 16, Margin = new Thickness(0, 0, 0, 5) };
        ticks.Children.Add(ticksOff); ticks.Children.Add(ticksOn);
        var box = new Border { BorderThickness = new Thickness(1.5), Padding = new Thickness(0, 4, 0, 5), SnapsToDevicePixels = true };
        box.SetResourceReference(Border.BorderBrushProperty, "T.Text");
        var day = Centred(T(38, FontWeights.SemiBold, "T.Text", LabelFont));
        // Long weekdays ("QUARTA-FEIRA", "ВОСКРЕСЕНЬЕ") shrink to fit the box instead of running out of it.
        box.Child = new Viewbox { Child = day, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var bar = new Shapes.Path { Margin = new Thickness(0, 6, 0, 0) };
        bar.SetResourceReference(Shapes.Shape.FillProperty, "T.Text");
        sp.Children.Add(ticks); sp.Children.Add(box); sp.Children.Add(bar);
        _update = now =>
        {
            var (lit, unlit) = TickShapes(width, 16, HalfHoursGone(now));
            ticksOn.Data = lit; ticksOff.Data = unlit;
            day.Text = Track(now.ToString("dddd", Ci).ToUpper(Ci), Hair);
            var line = Track(Fmt.Time(now, H24) + (H24 ? "" : " " + now.ToString("tt", Ci).ToUpper(Ci)) + "  |  " + now.ToString("MMMM d", Ci).ToUpper(Ci), Hair);
            bar.Data = Knockout(line, width, 30, LabelFont, 15);
        };
        return sp;
    }

    /// <summary>Half hours gone today, 0–48: how many of the label's ticks are lit.</summary>
    internal static int HalfHoursGone(DateTime now) => Math.Clamp((int)(now.TimeOfDay.TotalMinutes / 30), 0, 48);

    /// <summary>The ticks' heights, 0–1: a fixed, gently uneven skyline (the same every day), never flat and never random.</summary>
    internal static double TickHeight(int k) => 0.3 + 0.7 * Math.Abs(Math.Sin(k * 0.53) * 0.6 + Math.Sin(k * 1.7 + 1) * 0.4);

    private static (Geometry Lit, Geometry Unlit) TickShapes(double width, double height, int lit)
    {
        const int count = 48;
        var step = width / count;
        var on = new GeometryGroup(); var off = new GeometryGroup();
        for (var k = 0; k < count; k++)
        {
            var h = Math.Round(height * Math.Clamp(TickHeight(k), 0.2, 1));
            (k < lit ? on : off).Children.Add(new RectangleGeometry(new Rect(k * step + (step - 1.6) / 2, height - h, 1.6, h)));
        }
        on.Freeze(); off.Freeze();
        return (on, off);
    }

    /// <summary>A solid bar with <paramref name="text"/> cut out of it, centred.</summary>
    private static Geometry Knockout(string text, double width, double height, FontFamily family, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, Brushes.Black, 1.0);
        var letters = ft.BuildGeometry(new Point(0, 0));
        var b = letters.Bounds;
        if (!b.IsEmpty)
        {
            // Centred, and shrunk when a long month or language would run past the bar's ends (and vanish).
            var k = KnockoutScale(b.Width, width);
            var t = new TransformGroup();
            t.Children.Add(new TranslateTransform(-b.X - b.Width / 2, -b.Y - b.Height / 2));
            t.Children.Add(new ScaleTransform(k, k));
            t.Children.Add(new TranslateTransform(width / 2, height / 2));
            letters.Transform = t;
        }
        var bar = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, width, height)), letters);
        bar.Freeze();
        return bar;
    }

    /// <summary>How much the label bar's letters shrink to keep 10 px clear at each end: 1 (full size) when they fit.</summary>
    internal static double KnockoutScale(double textWidth, double barWidth) =>
        textWidth <= 0 ? 1 : Math.Min(1, Math.Max(1, barWidth - 20) / textWidth);

    /// <summary>Heavy figures with the date written out under them, full stop included.</summary>
    private FrameworkElement BuildHeavy()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var time = Centred(Tight(T(88, FontWeights.Black, "T.Text", HeavyFont), 92));
        var date = Centred(T(17, FontWeights.Bold, "T.Text", DisplayFont));
        date.Margin = new Thickness(0, 2, 0, 0);
        sp.Children.Add(time); sp.Children.Add(date);
        _update = now =>
        {
            time.Text = now.ToString(H24 ? "HH:mm" : "hh:mm", Ci);
            date.Text = now.Day.ToString(Ci) + ", " + now.ToString("MMMM", Ci) + ", " + now.ToString("dddd", Ci) + ".";
        };
        return sp;
    }

    /// <summary>The month in a gradient of the accent colour, the weekday and date, then the time told as a sentence.</summary>
    private FrameworkElement BuildStory()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var month = Centred(Tight(T(64, FontWeights.Light, "T.Text", DisplayFont), 70));
        var day = Centred(T(21, FontWeights.Normal, "T.Text", DisplayFont));
        day.Margin = new Thickness(0, 0, 0, 14);
        var story = Centred(T(12.5, FontWeights.Normal, "T.Text2"));
        story.TextWrapping = TextWrapping.Wrap; story.MaxWidth = 330; story.LineHeight = 19;
        sp.Children.Add(month); sp.Children.Add(day); sp.Children.Add(story);
        // The gradient is worked out from the accent, so it is redone when the theme (or the wallpaper's accent) changes.
        _retint = () => month.Foreground = AccentGradient(TryFindResource("T.Accent") is SolidColorBrush b ? b.Color : Color.FromRgb(0x8A, 0x7D, 0xFF));
        _update = now =>
        {
            month.Text = now.ToString("MMMM", Ci);
            _retint();
            day.Text = now.ToString("dddd", Ci) + ", " + now.ToString("dd", Ci) + ".";
            story.Text = StoryLine(now, H24);
        };
        return sp;
    }

    /// <summary>The accent colour running into a neighbouring hue: the gradient the Story clock writes the month in.</summary>
    internal static LinearGradientBrush AccentGradient(Color accent)
    {
        var (h, s, l) = Wallpaper.ColorMath.ToHsl(accent);
        s = Math.Max(s, 0.55); l = Math.Clamp(l, 0.55, 0.78);
        var brush = new LinearGradientBrush(Wallpaper.ColorMath.FromHsl(h - 25, s, l), Wallpaper.ColorMath.FromHsl(h + 55, s, Math.Min(0.82, l + 0.06)), 0);
        brush.Freeze();
        return brush;
    }

    /// <summary>"It's 16:35 in the afternoon. 69% of the day has passed."</summary>
    internal static string StoryLine(DateTime now, bool h24)
    {
        var part = now.Hour switch { < 5 => "at night", < 12 => "in the morning", < 18 => "in the afternoon", < 22 => "in the evening", _ => "at night" };
        return $"It's {Fmt.Time(now, h24)} {part}. {DayPercent(now)}% of the day has passed.";
    }
}
