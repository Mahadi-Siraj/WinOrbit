using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace WinOrbit.Widgets;

/// <summary>
/// The look every WinOrbit widget shares, so a desktop full of them reads as one design instead of a pile of
/// generic cards. Tool widgets open with the same small header (an accent dot and a label in spaced capitals),
/// numbers are big and light, small data (sizes, rates, offsets) is set in a monospaced font, and meters are
/// segmented bars. Clocks are type only and carry no header. Fonts are ones Windows already has.
/// </summary>
internal static class WidgetLook
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");

    /// <summary>
    /// A lettering set: the fonts every widget's text, data and big figures use. Widgets take it from the clock
    /// (Widgets › Match the clock's lettering), so the desktop reads as one design: a Mono or Dot matrix clock turns
    /// the other widgets into a terminal, Poster and Label make them condensed, Orbit and Hairline make them airy.
    /// Null fonts mean the theme's own font. <paramref name="CapsGap"/> is the space between capitals in labels:
    /// none for the terminal set, since a monospaced font gives even a hair space a whole letter's width.
    /// </summary>
    internal sealed record Lettering(string Id, FontFamily? Text, FontFamily Data, FontFamily? Figures, FontWeight FigureWeight, FontWeight CapsWeight, string CapsGap);

    internal static readonly Lettering Hud = new("hud", null, Mono, null, FontWeights.Light, FontWeights.SemiBold, ClockWidget.Thin);
    internal static readonly Lettering Terminal = new("terminal", Mono, Mono, Mono, FontWeights.Light, FontWeights.SemiBold, "");
    internal static readonly Lettering Condensed = new("condensed", new FontFamily("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI"),
        new FontFamily("Bahnschrift, Segoe UI"), new FontFamily("Bahnschrift Condensed, Bahnschrift, Segoe UI"), FontWeights.Normal, FontWeights.SemiBold, ClockWidget.Thin);
    internal static readonly Lettering Airy = new("airy", new FontFamily("Segoe UI Variable Text, Segoe UI"),
        new FontFamily("Segoe UI Variable Text, Segoe UI"), new FontFamily("Segoe UI Variable Display, Segoe UI"), FontWeights.Light, FontWeights.Normal, ClockWidget.Thin);

    /// <summary>The lettering widgets use now. Changed only by <see cref="WidgetManager.RefreshLettering"/>, which rebuilds them.</summary>
    internal static Lettering Current { get; set; } = Hud;

    /// <summary>The lettering that goes with a clock style; null (no clock, or matching off) keeps the HUD look.</summary>
    internal static Lettering LetteringFor(string? clockStyle) => clockStyle switch
    {
        null => Hud,
        "mono" or "dots" => Terminal,
        "poster" or "label" or "heavy" or "script" or "week" => Condensed,
        "orbit" or "hairline" or "spaced" or "story" => Airy,
        _ => Hud
    };

    /// <summary>The clock the desktop's lettering follows: the first one that is shown.</summary>
    internal static string? LeadClock(IEnumerable<Core.WidgetInstance> widgets) => widgets.FirstOrDefault(w => w.Kind == "clock" && w.Visible)?.Variant;

    /// <summary>The header: an accent dot, then the label in spaced capitals; <paramref name="right"/> (optional) sits at the far end.</summary>
    public static Grid Head(string label, TextBlock? right = null) => Head(label, out _, right);

    /// <inheritdoc cref="Head(string, TextBlock?)"/>
    public static Grid Head(string label, out TextBlock text, TextBlock? right = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        dot.SetResourceReference(Shapes.Shape.FillProperty, "T.Accent");
        g.Children.Add(dot);
        text = Caps(label);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (right is not null)
        {
            Grid.SetColumn(right, 2);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(right);
        }
        return g;
    }

    /// <summary>A label in small spaced capitals.</summary>
    public static TextBlock Caps(string text, double size = 10.5, string brush = "T.Text2")
    {
        var t = new TextBlock { FontSize = size, FontWeight = Current.CapsWeight, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        if (Current.Text is { } family) t.FontFamily = family;
        SetCaps(t, text);
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static void SetCaps(TextBlock t, string text) => t.Text = ClockWidget.Track(text.ToUpper(CultureInfo.CurrentCulture), Current.CapsGap);

    /// <summary>Small data in the monospaced font.</summary>
    public static TextBlock Data(string text, double size = 11.5, string brush = "T.Text2")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontFamily = Current.Data, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>A big, light numeral with a line box no taller than its digits.</summary>
    public static TextBlock Big(double size, string brush = "T.Text")
    {
        var t = new TextBlock { FontSize = size, FontWeight = Current.FigureWeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = size * 1.02 };
        if (Current.Figures is { } family) t.FontFamily = family;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>
    /// How many of <paramref name="cells"/> segments a meter lights for <paramref name="percent"/>: none at 0, at
    /// least one for any use at all (so 0.4 % still shows life), all of them at 100.
    /// </summary>
    internal static int Lit(double percent, int cells)
    {
        if (cells <= 0 || double.IsNaN(percent) || percent <= 0) return 0;
        return (int)Math.Clamp(Math.Ceiling(percent / 100 * cells - 1e-9), 1, cells);
    }
}

/// <summary>
/// A segmented meter, the HUD bar of the WinOrbit look. Cells fill in the accent colour, and turn amber above
/// <see cref="HotAbove"/>. It only touches its cells when the number lit (or the colour) changes, so a sample that
/// moves 0.3 % costs nothing to draw.
/// </summary>
internal sealed class SegmentBar : UniformGrid
{
    private readonly List<Shapes.Rectangle> _cells = new();
    private int _lit = -1;
    private bool _hot;

    public double HotAbove { get; init; } = 90;

    public SegmentBar(int cells = 24, double height = 6)
    {
        Rows = 1;
        Height = height;
        for (var k = 0; k < cells; k++)
        {
            var r = new Shapes.Rectangle { RadiusX = 1.2, RadiusY = 1.2, Margin = new Thickness(k == 0 ? 0 : 1.25, 0, k == cells - 1 ? 0 : 1.25, 0), SnapsToDevicePixels = true };
            r.SetResourceReference(Shapes.Shape.FillProperty, "T.Track");
            _cells.Add(r);
            Children.Add(r);
        }
    }

    public void Set(double percent)
    {
        var lit = WidgetLook.Lit(percent, _cells.Count);
        var hot = percent >= HotAbove;
        if (lit == _lit && hot == _hot) return;
        _lit = lit; _hot = hot;
        for (var k = 0; k < _cells.Count; k++)
            _cells[k].SetResourceReference(Shapes.Shape.FillProperty, k < lit ? (hot ? "T.Warn" : "T.Accent") : "T.Track");
    }
}
