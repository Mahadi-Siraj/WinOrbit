using System.Globalization;
using System.Windows.Media;

namespace Glasswave.Wallpaper;

/// <summary>Colour maths used by the theme and gradient engines. Pure functions, unit tested.</summary>
public static class ColorMath
{
    public static Color Parse(string? hex, Color fallback = default)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return fallback;
        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Alpha(this Color c, double opacity) => Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B);

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Relative luminance 0..1 (sRGB, WCAG definition).</summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a); var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2;
        var d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l, byte alpha = 255)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        if (s < 1e-9) { var v = (byte)Math.Round(l * 255); return Color.FromArgb(alpha, v, v, v); }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1 / 2.0) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        return Color.FromArgb(alpha, (byte)Math.Round(Hue(p, q, h + 1 / 3.0) * 255), (byte)Math.Round(Hue(p, q, h) * 255), (byte)Math.Round(Hue(p, q, h - 1 / 3.0) * 255));
    }

    public static Color Saturate(Color c, double factor)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl(h, s * factor, l, c.A);
    }

    public static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Re-light an accent so it reads well on a dark or light surface while keeping its hue.</summary>
    public static Color ForMode(Color accent, bool dark)
    {
        var (h, s, l) = ToHsl(accent);
        s = Math.Clamp(s, 0.3, 0.95);
        l = dark ? Math.Clamp(l, 0.64, 0.78) : Math.Clamp(l, 0.36, 0.48);
        // Then walk lightness until the accent clears a contrast floor against the surface it sits on
        // (yellows and cyans are very bright at any given HSL lightness).
        var surface = dark ? Color.FromRgb(0x12, 0x15, 0x1C) : Colors.White;
        var target = dark ? 4.5 : 3.0;
        var c = FromHsl(h, s, l);
        for (var i = 0; i < 40 && Contrast(c, surface) < target; i++)
        {
            l = dark ? Math.Min(0.95, l + 0.015) : Math.Max(0.12, l - 0.015);
            c = FromHsl(h, s, l);
        }
        return c;
    }
}

/// <summary>A set of coordinated colours extracted from an image.</summary>
public sealed class Palette
{
    public Color Dominant { get; init; } = Color.FromRgb(0x2B, 0x3A, 0x55);
    public Color Accent { get; init; } = Color.FromRgb(0x5B, 0x9D, 0xFF);
    public Color Secondary { get; init; } = Color.FromRgb(0x8E, 0x7C, 0xFF);
    public Color Complement { get; init; } = Color.FromRgb(0xFF, 0xB3, 0x6B);
    public Color[] Swatches { get; init; } = Array.Empty<Color>();
    public double AverageLuminance { get; init; } = 0.3;
    public bool IsNeutral { get; init; }

    public static Palette Default { get; } = new()
    {
        Swatches = new[] { Color.FromRgb(0x2B, 0x3A, 0x55), Color.FromRgb(0x5B, 0x9D, 0xFF), Color.FromRgb(0x8E, 0x7C, 0xFF), Color.FromRgb(0xFF, 0xB3, 0x6B) }
    };

    /// <summary>
    /// Extract a palette from BGRA pixels. Colours are bucketed into a 4-bit-per-channel histogram,
    /// the populated buckets are merged into visually distinct swatches, and the accent is the swatch
    /// that best balances vividness, coverage and usable lightness.
    /// </summary>
    public static Palette FromPixels(byte[] bgra, int width, int height)
    {
        if (bgra.Length < 4 || width <= 0 || height <= 0) return Default;
        var count = new int[4096];
        var sumR = new long[4096]; var sumG = new long[4096]; var sumB = new long[4096];
        double lumSum = 0; var total = 0; var colourfulPixels = 0;
        var step = Math.Max(1, (int)Math.Sqrt(width * height / 20000.0));   // ~20k samples is plenty
        for (var y = 0; y < height; y += step)
        for (var x = 0; x < width; x += step)
        {
            var i = (y * width + x) * 4;
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            count[key]++; sumR[key] += r; sumG[key] += g; sumB[key] += b;
            lumSum += 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (IsColourful(r, g, b)) colourfulPixels++;
            total++;
        }
        if (total == 0) return Default;

        var bins = Enumerable.Range(0, 4096).Where(k => count[k] > 0)
            .Select(k => (Color: Color.FromRgb((byte)(sumR[k] / count[k]), (byte)(sumG[k] / count[k]), (byte)(sumB[k] / count[k])), Weight: count[k] / (double)total))
            .OrderByDescending(b => b.Weight).Take(160).ToList();

        // Greedy merge: a bin joins the first swatch it is close to, otherwise it founds a new one.
        var swatches = new List<(Color Color, double Weight)>();
        foreach (var bin in bins)
        {
            var idx = swatches.FindIndex(s => Distance(s.Color, bin.Color) < 52);
            if (idx < 0) { if (swatches.Count < 12) swatches.Add(bin); }
            else
            {
                var s = swatches[idx];
                var w = s.Weight + bin.Weight;
                swatches[idx] = (ColorMath.Lerp(s.Color, bin.Color, bin.Weight / w), w);
            }
        }
        swatches = swatches.OrderByDescending(s => s.Weight).ToList();
        var dominant = swatches[0].Color;

        double Score((Color Color, double Weight) s)
        {
            var (_, sat, l) = ColorMath.ToHsl(s.Color);
            // HSL calls near-black and near-white "saturated"; scale saturation by how visible the colour is.
            var visible = sat * Math.Min(1, Math.Min(l, 1 - l) / 0.15);
            var lightFit = 1 - Math.Min(1, Math.Abs(l - 0.55) / 0.5);       // mid-tones make the best accents
            return Math.Pow(visible, 1.4) * Math.Pow(s.Weight, 0.25) * (0.35 + 0.65 * lightFit);
        }

        // Share of the image that is genuinely coloured, measured per pixel. A colourful wallpaper often
        // spreads its colour over many small swatches (gradients, many hues), so swatch weights alone
        // under-count it; a few desktop icons on a monochrome wallpaper stay well under the threshold.
        var colourfulShare = colourfulPixels / (double)total;
        var colourfulImage = colourfulShare >= 0.06;

        // Only colours covering a meaningful share of the image may lead. This stops small, scattered
        // details (desktop icons, a logo, a signature) from hijacking the accent of a mostly neutral wallpaper.
        // When the image is colourful overall, smaller coloured swatches may lead too.
        bool CanLead((Color Color, double Weight) s) =>
            s.Weight >= 0.015 || colourfulImage && s.Weight >= 0.004 && IsColourful(s.Color.R, s.Color.G, s.Color.B);
        var ranked = swatches.Where(CanLead).OrderByDescending(Score).ToList();
        if (ranked.Count == 0) ranked = swatches.OrderByDescending(s => s.Weight).ToList();
        var best = ranked[0];
        var (bh, bs, bl) = ColorMath.ToHsl(best.Color);
        var neutral = bs < 0.22 || Score(best) < 0.02 || !colourfulImage;

        Color accent, secondary;
        if (neutral)
        {
            // Greyscale wallpaper: a quiet steel accent and graphite tones rather than inventing colour.
            accent = ColorMath.FromHsl(212, 0.36, 0.64);
            secondary = ColorMath.FromHsl(216, 0.08, 0.46);
        }
        else
        {
            accent = ColorMath.FromHsl(bh, Math.Clamp(bs * 1.15, 0.5, 0.92), Math.Clamp(bl, 0.45, 0.66));
            var other = ranked.Skip(1).FirstOrDefault(s => ColorMath.ToHsl(s.Color).S > 0.2 && ColorMath.HueDistance(ColorMath.ToHsl(s.Color).H, bh) > 28);
            if (other.Weight > 0)
            {
                var (oh, os, ol) = ColorMath.ToHsl(other.Color);
                secondary = ColorMath.FromHsl(oh, Math.Clamp(os * 1.1, 0.42, 0.88), Math.Clamp(ol, 0.42, 0.66));
            }
            else secondary = ColorMath.FromHsl(bh + 38, Math.Clamp(bs, 0.45, 0.85), Math.Clamp(bl + 0.04, 0.45, 0.68));   // analogous
        }
        var (ah, asat, al) = ColorMath.ToHsl(accent);
        var complement = neutral
            ? ColorMath.FromHsl(30, 0.25, 0.66)
            : ColorMath.FromHsl(ah + 180, Math.Clamp(asat * 0.85, 0.35, 0.8), Math.Clamp(al + 0.05, 0.5, 0.7));

        return new Palette
        {
            Dominant = dominant, Accent = accent, Secondary = secondary, Complement = complement,
            Swatches = swatches.Take(8).Select(s => s.Color).ToArray(),
            AverageLuminance = lumSum / total / 255.0,
            IsNeutral = neutral
        };
    }

    /// <summary>HSL saturation ≥ 0.25 at a lightness where colour is actually visible (0.12–0.92).</summary>
    private static bool IsColourful(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 510.0;
        if (l <= 0.12 || l >= 0.92 || max == min) return false;
        var d = (max - min) / 255.0;
        var s = l > 0.5 ? d / (2 - (max + min) / 255.0) : d / ((max + min) / 255.0);
        return s >= 0.25;
    }

    private static double Distance(Color a, Color b)
    {
        // "Redmean" weighted RGB distance: cheap and noticeably closer to perception than plain Euclid.
        var rm = (a.R + b.R) / 2.0;
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db);
    }

    /// <summary>Gradient stops that echo the wallpaper: accent → secondary → a deepened dominant tone.</summary>
    public List<Core.GradientStopDef> ToGradientStops()
    {
        if (IsNeutral)
        {
            // Monochrome wallpapers get a graphite wash with the faintest cool cast — glass, not colour.
            return new List<Core.GradientStopDef>
            {
                new(ColorMath.Hex(ColorMath.FromHsl(212, 0.14, 0.6)), 0),
                new(ColorMath.Hex(ColorMath.FromHsl(216, 0.06, 0.4)), 0.55),
                new(ColorMath.Hex(ColorMath.FromHsl(220, 0.05, 0.2)), 1)
            };
        }
        var (dh, ds, dl) = ColorMath.ToHsl(Dominant);
        var deep = ColorMath.FromHsl(dh, Math.Clamp(ds * 1.1, 0.25, 0.8), Math.Clamp(dl, 0.3, 0.55));
        return new List<Core.GradientStopDef>
        {
            new(ColorMath.Hex(Accent), 0),
            new(ColorMath.Hex(Secondary), 0.52),
            new(ColorMath.Hex(deep), 1)
        };
    }
}
