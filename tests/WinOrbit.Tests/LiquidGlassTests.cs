using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using WinOrbit.Design;
using Xunit;

namespace WinOrbit.Tests;

public class LiquidGlassTests
{
    [Fact]
    public void Big_glass_gets_a_full_band_and_more_bend_with_more_refraction()
    {
        var (band, bend) = LiquidGlassEffect.Profile(600, 220, 0.06);
        Assert.Equal(16, band, 6);
        Assert.InRange(bend, 8, 20);
        Assert.True(LiquidGlassEffect.Profile(600, 220, 0.12).Bend > bend);
        Assert.Equal(20, LiquidGlassEffect.Profile(600, 220, 1).Bend, 6);    // capped
        Assert.Equal(8, LiquidGlassEffect.Profile(600, 220, 0).Bend, 6);     // flat themes still read as glass
    }

    [Fact]
    public void Small_chips_bend_less_so_they_never_turn_into_a_fish_eye()
    {
        var (band, bend) = LiquidGlassEffect.Profile(38, 38, 0.2);
        Assert.True(band <= 38 / 2.5 + 1e-9);
        Assert.True(bend <= 38 / 3.0 + 1e-9);
        Assert.Equal((0.0, 0.0), LiquidGlassEffect.Profile(0, 40, 0.06));
    }

    [Fact]
    public void Corners_are_clamped_to_half_the_short_side_and_keep_their_order()
    {
        var c = LiquidGlassEffect.Corners(new CornerRadius(0, 4, 40, 19), 200, 38);
        Assert.Equal(0, c.X);    // top-left
        Assert.Equal(4, c.Y);    // top-right
        Assert.Equal(19, c.Z);   // bottom-right, clamped from 40
        Assert.Equal(19, c.W);   // bottom-left
    }

    [Fact]
    public void Light_points_from_the_centre_towards_the_pointer()
    {
        var right = LiquidGlassEffect.Toward(new Point(190, 20), 200, 40);
        Assert.Equal(1, right.Length, 6);
        Assert.True(right.X > 0.99);
        var centre = LiquidGlassEffect.Toward(new Point(100, 20), 200, 40);
        Assert.Equal(new Vector(0, -1), centre);   // no pointer: light from above
    }

    [Fact]
    public void Compiled_shader_ships_inside_the_app()
    {
        var asm = typeof(LiquidGlassEffect).Assembly;
        using var stream = asm.GetManifestResourceStream(asm.GetName().Name + ".g.resources");
        Assert.NotNull(stream);
        using var reader = new ResourceReader(stream!);
        byte[]? shader = null;
        foreach (DictionaryEntry e in reader)
            if ((string)e.Key == "design/shaders/liquidglass.ps" && e.Value is Stream s)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                shader = ms.ToArray();
            }
        Assert.NotNull(shader);
        // Pixel shader 2.0 bytecode begins with its version token: 0xFFFF0200.
        Assert.Equal(0xFFFF0200u, BitConverter.ToUInt32(shader!, 0));
    }
}
