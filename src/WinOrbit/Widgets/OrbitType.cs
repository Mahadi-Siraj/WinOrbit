using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace WinOrbit.Widgets;

/// <summary>
/// Orbit letters: WinOrbit's own geometric display alphabet, drawn as thin strokes, never a font file. Every letter
/// is built from straight lines and arcs on a 12-unit cap height, with one idea running through the set: parts
/// float free of the stem (the bowls of B, D, P and R, the arms of E and F, the bar of A, H and T), so a word in
/// wide tracking reads like a futuristic logotype. Capital A–Z, the figures and a little punctuation; anything else
/// (accented or non-Latin letters) makes the caller fall back to a Windows font, so every language still reads.
/// </summary>
internal static class OrbitType
{
    public const double CapHeight = 12;

    // (advance width in units, stroke path in units). Paths use the WPF/SVG path mini-language.
    private static readonly Dictionary<char, (double Width, string Path)> Glyphs = new()
    {
        ['A'] = (12, "M0,12 L6,0 L12,12 M4.4,8.4 L7.6,8.4"),
        ['B'] = (11, "M0,0 V12 M2.4,0 H7.5 A3,3 0 0 1 7.5,6 H2.4 M2.4,6 H8 A3,3 0 0 1 8,12 H2.4"),
        ['C'] = (12, "M12,0 H6 A6,6 0 0 0 6,12 H12"),
        ['D'] = (12, "M0,0 V12 M2.4,0 H6 A6,6 0 0 1 6,12 H2.4"),
        ['E'] = (10, "M0,0 V12 M2.4,0 H10 M2.4,6 H9 M2.4,12 H10"),
        ['F'] = (10, "M0,0 V12 M2.4,0 H10 M2.4,6 H9"),
        ['G'] = (12, "M12,0 H6 A6,6 0 0 0 6,12 H12 V7 H8.5"),
        ['H'] = (12, "M0,0 V12 M12,0 V12 M2.4,6 H9.6"),
        ['I'] = (0, "M0,0 V12"),
        ['J'] = (10, "M10,0 V7 A5,5 0 0 1 0,7"),
        ['K'] = (11, "M0,0 V12 M11,0 L3,6 L11,12"),
        ['L'] = (10, "M0,0 V12 H10"),
        ['M'] = (13, "M0,12 V0 L6.5,7 L13,0 V12"),
        ['N'] = (12, "M0,12 V0 L12,12 V0"),
        ['O'] = (12, "M6,0 A6,6 0 0 1 6,12 A6,6 0 0 1 6,0 Z"),
        ['P'] = (11, "M0,0 V12 M2.4,0 H7.5 A3.5,3.5 0 0 1 7.5,7 H2.4"),
        ['Q'] = (12, "M6,0 A6,6 0 0 1 6,12 A6,6 0 0 1 6,0 Z M8.5,8.5 L12,12"),
        ['R'] = (11, "M0,0 V12 M2.4,0 H7.5 A3.5,3.5 0 0 1 7.5,7 H2.4 M6.5,7 L11,12"),
        ['S'] = (11, "M11,0 H3 A3,3 0 0 0 3,6 H8 A3,3 0 0 1 8,12 H0"),
        ['T'] = (12, "M0,0 H12 M6,2.4 V12"),
        ['U'] = (12, "M0,0 V6 A6,6 0 0 0 12,6 V0"),
        ['V'] = (12, "M0,0 L6,12 L12,0"),
        ['W'] = (16, "M0,0 L4,12 L8,2 L12,12 L16,0"),
        ['X'] = (12, "M0,0 L12,12 M12,0 L0,12"),
        ['Y'] = (12, "M0,0 L6,6.5 L12,0 M6,6.5 V12"),
        ['Z'] = (11, "M0,0 H11 L0,12 H11"),
        ['0'] = (10, "M5,0 A5,5 0 0 1 10,5 V7 A5,5 0 0 1 0,7 V5 A5,5 0 0 1 5,0 Z"),
        ['1'] = (4, "M0,2.5 L4,0 V12"),
        ['2'] = (10, "M0,0 H7 A3,3 0 0 1 7,6 H3 A3,3 0 0 0 0,9 V12 H10"),
        ['3'] = (10, "M0,0 H7 A3,3 0 0 1 7,6 A3,3 0 0 1 7,12 H0 M3,6 H7"),
        ['4'] = (10, "M0,0 V7 H10 M7.5,0 V12"),
        ['5'] = (10, "M10,0 H0 V5.5 H7 A3.25,3.25 0 0 1 7,12 H0"),
        ['6'] = (10, "M10,0 H5 A5,5 0 0 0 0,5 V9 A3,3 0 0 0 3,12 H7 A3,3 0 0 0 10,9 A3,3 0 0 0 7,6 H0"),
        ['7'] = (10, "M0,0 H10 L4,12"),
        ['8'] = (10, "M3,0 H7 A3,3 0 0 1 7,6 H3 A3,3 0 0 1 3,0 Z M3,6 H7 A3,3 0 0 1 7,12 H3 A3,3 0 0 1 3,6 Z"),
        ['9'] = (10, "M0,12 H5 A5,5 0 0 0 10,7 V3 A3,3 0 0 0 7,0 H3 A3,3 0 0 0 0,3 A3,3 0 0 0 3,6 H10"),
        [':'] = (0, "M0,3.6 V5 M0,8.6 V10"),
        ['-'] = (6, "M0,6.5 H6"),
        ['.'] = (0, "M0,10.6 V12"),
        [','] = (1, "M1,10.6 L0,13"),
        ['/'] = (7, "M0,12 L7,0"),
        ['\''] = (0, "M0,0 V3"),
    };

    /// <summary>The width of a space between words, in units.</summary>
    internal const double SpaceWidth = 7;

    private static readonly Dictionary<char, Geometry> Parsed = new();

    /// <summary>The characters Orbit letters can draw (after upper-casing).</summary>
    internal static IEnumerable<char> Characters => Glyphs.Keys;

    /// <summary>Whether every character of <paramref name="text"/> has an Orbit letter (spaces always do).</summary>
    internal static bool Supports(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var c in text.ToUpperInvariant())
            if (c != ' ' && !Glyphs.ContainsKey(c)) return false;
        return true;
    }

    /// <summary>The advance width of one character in units (0 for a stem-only letter such as I), or null if it has none.</summary>
    internal static double? Advance(char c) => c == ' ' ? SpaceWidth : Glyphs.TryGetValue(char.ToUpperInvariant(c), out var g) ? g.Width : null;

    /// <summary>The width of <paramref name="text"/> in units with <paramref name="tracking"/> units between letters.</summary>
    internal static double Measure(string text, double tracking)
    {
        double x = 0;
        var first = true;
        foreach (var c in text.ToUpperInvariant())
        {
            if (!first) x += tracking;
            first = false;
            x += Advance(c) ?? 0;
        }
        return x;
    }

    /// <summary>
    /// The stroke path of <paramref name="text"/>, scaled so capitals are <paramref name="size"/> pixels tall and moved
    /// by <paramref name="inset"/> (half the stroke) so nothing is cut off at the top or left. Null if a character has
    /// no Orbit letter.
    /// </summary>
    internal static Geometry? Build(string text, double size, double tracking, double inset = 0)
    {
        if (!Supports(text)) return null;
        var scale = size / CapHeight;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        double x = 0;
        var first = true;
        foreach (var c in text.ToUpperInvariant())
        {
            if (!first) x += tracking;
            first = false;
            if (c == ' ') { x += SpaceWidth; continue; }
            var glyph = Glyph(c);
            var copy = glyph.Clone();
            copy.Transform = new MatrixTransform(scale, 0, 0, scale, inset + x * scale, inset);
            group.Children.Add(copy);
            x += Glyphs[c].Width;
        }
        group.Freeze();
        return group;
    }

    private static Geometry Glyph(char c)
    {
        if (Parsed.TryGetValue(c, out var g)) return g;
        g = Geometry.Parse(Glyphs[c].Path);
        g.Freeze();
        Parsed[c] = g;
        return g;
    }
}

/// <summary>
/// A line of Orbit letters that falls back to a light Windows font for text they can't draw. Stroke width is given
/// in units of the 12-unit cap height, so a heavier or lighter look keeps its proportions at any size.
/// </summary>
internal sealed class OrbitText : Grid
{
    private readonly Shapes.Path _path = new() { StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat, StrokeLineJoin = PenLineJoin.Miter, StrokeMiterLimit = 3, SnapsToDevicePixels = false };
    private readonly TextBlock _fallback = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly double _size, _tracking, _weight;
    private string _text = "";

    public OrbitText(double size, double tracking, double weight = 1.2, string brush = "T.Text")
    {
        _size = size; _tracking = tracking; _weight = weight;
        _path.StrokeThickness = weight * size / OrbitType.CapHeight;
        _path.SetResourceReference(Shapes.Shape.StrokeProperty, brush);
        _fallback.FontSize = size * 1.3;
        _fallback.FontWeight = weight >= 1.5 ? FontWeights.SemiBold : FontWeights.Light;
        _fallback.FontFamily = new FontFamily("Bahnschrift, Segoe UI Variable Display, Segoe UI");
        _fallback.SetResourceReference(TextBlock.ForegroundProperty, brush);
        _fallback.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _fallback.LineHeight = size * 1.3;
        Children.Add(_path);
        Children.Add(_fallback);
    }

    /// <summary>The text, drawn in upper case. Rebuilds the shape only when it changes.</summary>
    public string Text
    {
        get => _text;
        set
        {
            var upper = (value ?? "").ToUpper(CultureInfo.CurrentCulture);
            if (upper == _text) return;
            _text = upper;
            var inset = _path.StrokeThickness / 2;
            if (OrbitType.Build(upper, _size, _tracking, inset) is { } geo)
            {
                _path.Data = geo;
                // The shape's own box: the letters plus half a stroke all round (Path doesn't count the stroke itself).
                _path.Width = OrbitType.Measure(upper, _tracking) * _size / OrbitType.CapHeight + inset * 2;
                _path.Height = _size + inset * 2;
                _path.Visibility = Visibility.Visible;
                _fallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                _fallback.Text = ClockWidget.Track(upper, ClockWidget.Thin);
                _fallback.Visibility = Visibility.Visible;
                _path.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>The weight in units this text was made with (for tests and layouts that align to the stroke).</summary>
    public double Weight => _weight;
}
