using System.Windows;
using WinOrbit.Services;
using WinOrbit.Wallpaper;
using Xunit;

namespace WinOrbit.Tests;

/// <summary>
/// Live glass does less work per picture than it used to, and none of it may change a single pixel: these compare the
/// faster code with the code it replaced, byte for byte.
/// </summary>
public class LiveGlassCostTests
{
    // ---- the code that was replaced, kept here as the reference ----

    private static void OldAdjust(byte[] px, double saturation)
    {
        if (Math.Abs(saturation - 1) < 0.01) return;
        for (var i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            var grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            px[i] = (byte)Math.Clamp(grey + (b - grey) * saturation, 0, 255);
            px[i + 1] = (byte)Math.Clamp(grey + (g - grey) * saturation, 0, 255);
            px[i + 2] = (byte)Math.Clamp(grey + (r - grey) * saturation, 0, 255);
        }
    }

    private static void OldBoxBlur(byte[] px, int w, int h, int radius)
    {
        if (radius < 1) return;
        radius = Math.Min(radius, Math.Min(w, h) / 2 - 1);
        if (radius < 1) return;
        var tmp = new byte[px.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            OldBlurLine(px, tmp, w, h, radius, true);
            OldBlurLine(tmp, px, w, h, radius, false);
        }
    }

    private static void OldBlurLine(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        var win = r * 2 + 1;
        int outer = horizontal ? h : w, inner = horizontal ? w : h;
        int stepInner = horizontal ? 4 : w * 4, stepOuter = horizontal ? w * 4 : 4;
        for (var o = 0; o < outer; o++)
        {
            var line = o * stepOuter;
            int sb = 0, sg = 0, sr = 0;
            for (var i = -r; i <= r; i++)
            {
                var idx = line + Math.Clamp(i, 0, inner - 1) * stepInner;
                sb += src[idx]; sg += src[idx + 1]; sr += src[idx + 2];
            }
            for (var i = 0; i < inner; i++)
            {
                var d = line + i * stepInner;
                dst[d] = (byte)(sb / win); dst[d + 1] = (byte)(sg / win); dst[d + 2] = (byte)(sr / win); dst[d + 3] = 255;
                var add = line + Math.Min(i + r + 1, inner - 1) * stepInner;
                var rem = line + Math.Max(i - r, 0) * stepInner;
                sb += src[add] - src[rem]; sg += src[add + 1] - src[rem + 1]; sr += src[add + 2] - src[rem + 2];
            }
        }
    }

    private static byte[] Noise(Random rnd, int length)
    {
        var px = new byte[length];
        rnd.NextBytes(px);
        return px;
    }

    // ---- blur and colour: the same bytes ----

    [Theory]
    [InlineData(240, 135, 3)]    // an eighth of 1080p, Pure Liquid Glass
    [InlineData(480, 270, 1)]    // a quarter, Liquid Clear
    [InlineData(480, 270, 2)]
    [InlineData(960, 540, 6)]    // a wallpaper backdrop
    [InlineData(33, 17, 5)]      // odd sizes, radius cut down to fit
    [InlineData(5, 300, 9)]
    [InlineData(4, 4, 3)]        // too small to blur at all
    public void The_reused_blur_gives_the_same_bytes(int w, int h, int radius)
    {
        var rnd = new Random(w * 7919 + h * 31 + radius);
        var source = Noise(rnd, w * h * 4);
        var expected = (byte[])source.Clone();
        OldBoxBlur(expected, w, h, radius);

        // As the app runs it, then each way explicitly; with fresh working space, and with an earlier picture's leftovers.
        var fresh = (byte[])source.Clone();
        WallpaperEngine.BoxBlur(fresh, w, h, radius);
        Assert.Equal(expected, fresh);
        foreach (var vectors in new[] { true, false })
        {
            var reused = (byte[])source.Clone();
            WallpaperEngine.BoxBlur(reused, w, h, radius, Noise(rnd, w * h * 4 + 64), vectors);
            Assert.Equal(expected, reused);
        }
    }

    [Fact]
    public void Dividing_by_table_or_multiplier_matches_dividing()
    {
        for (var window = 1; window < 300; window += window < 70 ? 1 : 37)
        {
            var divide = WallpaperEngine.DividerFor(window);
            Assert.Equal(255 * window + 1, divide.Quotients.Length);
            for (var sum = 0; sum < divide.Quotients.Length; sum++)
            {
                Assert.Equal(sum / window, divide.Quotients[sum]);
                if (divide.Multiplier != 0) Assert.Equal(sum / window, (sum * divide.Multiplier) >>> WallpaperEngine.Shift);
            }
            if (window < 64) Assert.Same(divide, WallpaperEngine.DividerFor(window));   // made once, then reused
        }
        // Every window the glass can use has an exact multiplier, so it never falls back to the slow way.
        for (var radius = 1; radius < 64; radius++) Assert.NotEqual(0, WallpaperEngine.DividerFor(radius * 2 + 1).Multiplier);
    }

    [Theory]
    [InlineData(1.3)]
    [InlineData(1.15)]
    [InlineData(0.6)]
    [InlineData(2.4)]
    [InlineData(1.005)]   // close enough to 1 to be left alone, as before
    public void Saturation_gives_the_same_bytes(double saturation)
    {
        // Every grey level and every pure and mixed colour corner, then plenty of random colours.
        var rnd = new Random((int)(saturation * 1000));
        var colours = new List<byte>();
        for (var v = 0; v < 256; v++) colours.AddRange(new byte[] { (byte)v, (byte)v, (byte)v, 255, (byte)v, 0, 0, 255, 0, (byte)v, 0, 255, 0, 0, (byte)v, 255, (byte)v, 255, (byte)(255 - v), 255 });
        colours.AddRange(Noise(rnd, 400_000 * 4));
        colours.AddRange(new byte[] { 1, 2, 3, 4, 200, 100, 50 });   // a last pixel without alpha, which was handled too
        var expected = colours.ToArray();
        OldAdjust(expected, saturation);
        var actual = colours.ToArray();
        WallpaperEngine.Adjust(actual, saturation);
        Assert.Equal(expected, actual);
        foreach (var vectors in new[] { true, false })
        {
            actual = colours.ToArray();
            WallpaperEngine.Adjust(actual, saturation, vectors && System.Runtime.Intrinsics.X86.Avx.IsSupported);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Saturation_gives_the_same_bytes_for_every_colour()
    {
        // All 16.7 million colours, at the strongest saturation a look uses.
        var all = new byte[1 << 26];
        for (var c = 0; c < 1 << 24; c++) { all[c * 4] = (byte)c; all[c * 4 + 1] = (byte)(c >> 8); all[c * 4 + 2] = (byte)(c >> 16); all[c * 4 + 3] = 255; }
        var expected = (byte[])all.Clone();
        OldAdjust(expected, 1.3);
        WallpaperEngine.Adjust(all, 1.3);
        Assert.True(expected.AsSpan().SequenceEqual(all));
    }

    // ---- deciding whether a picture is new enough to show ----

    [Fact]
    public void Stopping_at_the_first_changed_patch_decides_the_same()
    {
        var rnd = new Random(11);
        foreach (var (w, h) in new[] { (240, 135), (61, 29), (8, 8), (3, 50) })
            for (var round = 0; round < 60; round++)
            {
                var a = Noise(rnd, w * h * 4);
                var b = (byte[])a.Clone();
                // Anything from no change, through a faint shimmer, to one strongly changed patch.
                var shimmer = rnd.Next(0, 6);
                for (var i = 0; i < b.Length; i++) b[i] = (byte)Math.Clamp(b[i] + rnd.Next(-shimmer, shimmer + 1), 0, 255);
                if (round % 3 == 0)
                {
                    int px = rnd.Next(w), py = rnd.Next(h);
                    b[(py * w + px) * 4 + rnd.Next(3)] ^= 0xFF;
                }
                foreach (var threshold in new[] { -1.0, 0, 0.5, 1, 2, 3, LiveGlass.ChangeThreshold, 4.5, 30, 300 })
                    Assert.Equal(LiveGlass.Change(a, b, w, h) >= threshold, LiveGlass.Changed(a, b, w, h, threshold));
            }
        // Pictures of different sizes always count as changed, as before.
        Assert.Equal(LiveGlass.Change(new byte[16], new byte[20], 2, 2) >= LiveGlass.ChangeThreshold, LiveGlass.Changed(new byte[16], new byte[20], 2, 2, LiveGlass.ChangeThreshold));
    }

    // ---- each window's copy: only the part its glass reads ----

    [Fact]
    public void A_glass_reads_its_view_and_a_little_round_it()
    {
        // A widget a quarter of the way in, a tenth of the screen wide, on a 240 × 135 picture.
        var area = LiveGlass.Area(new Rect(0.25, 0.5, 0.125, 0.25), 240, 135, margin: 3);
        Assert.Equal(new Int32Rect(57, 64, 36, 41), area);   // 60 − 3 … 90 + 3 across; 67.5 down to 67, − 3 … 101.25 up to 102, + 3 down
        Assert.True(LiveGlass.Contains(area, LiveGlass.Area(new Rect(0.26, 0.52, 0.05, 0.1), 240, 135, margin: 3)));
    }

    [Fact]
    public void What_a_glass_reads_stays_inside_the_picture()
    {
        Assert.Equal(new Int32Rect(0, 0, 18, 12), LiveGlass.Area(new Rect(-0.25, -0.125, 0.3125, 0.1875), 240, 135, margin: 3));       // off the top left
        Assert.Equal(new Int32Rect(222, 127, 18, 8), LiveGlass.Area(new Rect(0.9375, 0.96875, 0.25, 0.25), 240, 135, margin: 3));     // off the bottom right
        Assert.Equal(new Int32Rect(0, 0, 240, 135), LiveGlass.Area(Rect.Empty, 240, 135));                                 // unusable: everything
        Assert.Equal(new Int32Rect(0, 0, 240, 135), LiveGlass.Area(new Rect(double.NaN, 0, 1, 1), 240, 135));
        Assert.Equal(new Int32Rect(0, 0, 240, 135), LiveGlass.Area(new Rect(-1, -1, 3, 3), 240, 135));
        Assert.Equal(default, LiveGlass.Area(new Rect(0.1, 0.1, 0.1, 0.1), 0, 135));
    }

    [Fact]
    public void Areas_combine_into_one_rectangle()
    {
        var pill = new Int32Rect(100, 0, 40, 8);
        var bubble = new Int32Rect(150, 2, 6, 6);
        Assert.Equal(new Int32Rect(100, 0, 56, 8), LiveGlass.Union(pill, bubble));
        Assert.Equal(pill, LiveGlass.Union(default, pill));
        Assert.Equal(pill, LiveGlass.Union(pill, default));
        Assert.Equal(default, LiveGlass.Union(default, default));
        Assert.True(LiveGlass.Contains(LiveGlass.Union(pill, bubble), bubble));
        Assert.True(LiveGlass.Contains(pill, default));           // reading nothing is always covered
        Assert.False(LiveGlass.Contains(default, pill));          // a copy current nowhere covers nothing
        Assert.False(LiveGlass.Contains(pill, new Int32Rect(99, 0, 5, 5)));
        Assert.False(LiveGlass.Contains(pill, new Int32Rect(130, 0, 11, 8)));
    }

    [Fact]
    public void Only_a_change_inside_the_area_counts()
    {
        const int w = 64, h = 32;
        var before = Noise(new Random(5), w * h * 4);
        var after = (byte[])before.Clone();
        after[(20 * w + 50) * 4 + 1] ^= 1;   // one colour of one pixel, at (50, 20)
        Assert.True(LiveGlass.Same(before, after, w, new Int32Rect(0, 0, 50, 32)));     // just left of it
        Assert.True(LiveGlass.Same(before, after, w, new Int32Rect(40, 21, 24, 11)));   // just below it
        Assert.False(LiveGlass.Same(before, after, w, new Int32Rect(50, 20, 1, 1)));
        Assert.False(LiveGlass.Same(before, after, w, new Int32Rect(0, 0, w, h)));
        Assert.True(LiveGlass.Same(before, after, w, default));                          // nothing read, nothing changed
    }
}
