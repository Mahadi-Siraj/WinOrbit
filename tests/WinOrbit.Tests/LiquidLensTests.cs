using System.Collections;
using System.IO;
using System.Resources;
using System.Text.Json;
using System.Windows;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.Wallpaper;
using Xunit;

namespace WinOrbit.Tests;

public class LiquidLensTests
{
    [Theory]
    [InlineData(200, 38)]     // the island's capsule
    [InlineData(432, 266)]    // the open island
    [InlineData(38, 38)]      // a satellite drop
    [InlineData(10, 8)]       // tiny
    public void Bevel_never_passes_the_middle_of_the_glass(double w, double h)
    {
        foreach (var liquid in new[] { 0.0, 0.5, 1.0 })
        {
            var (bevel, reach, split, rim) = LiquidLensEffect.Profile(w, h, liquid);
            Assert.InRange(bevel, 0, Math.Min(w, h) / 2 + 1e-9);
            Assert.InRange(reach, 0, bevel);
            Assert.InRange(split, 0, 0.2);
            Assert.InRange(rim, 0, 1);
        }
    }

    [Fact]
    public void A_capsule_reads_as_one_rounded_drop_and_a_panel_keeps_a_flat_middle()
    {
        Assert.True(LiquidLensEffect.Profile(200, 38, 1).Bevel >= 38 * 0.45);    // about half the height
        Assert.Equal(64, LiquidLensEffect.Profile(432, 266, 1).Bevel, 6);        // capped, the middle stays flat for text
        Assert.True(LiquidLensEffect.Profile(200, 38, 1).Reach > LiquidLensEffect.Profile(200, 38, 0.3).Reach);
        Assert.Equal((0.0, 0.0), (LiquidLensEffect.Profile(0, 40, 1).Bevel, LiquidLensEffect.Profile(0, 40, 1).Reach));
    }

    [Fact]
    public void Padding_covers_the_furthest_the_rim_can_reach()
    {
        foreach (var (w, h) in new[] { (200.0, 38.0), (432.0, 266.0), (38.0, 38.0) })
        {
            var (_, reach, split, _) = LiquidLensEffect.Profile(w, h, 1);
            Assert.True(LiquidLensEffect.PadFor(w, h, 1) >= reach * (1 + LiquidLensEffect.MaxFlex) * (1 + split));
        }
    }

    [Fact]
    public void Readability_calms_bright_backgrounds_in_dark_looks_and_lifts_dark_ones_in_light_looks()
    {
        Assert.True(LiquidLensEffect.Readability(1, dark: true) > 0);
        Assert.True(LiquidLensEffect.Readability(1, dark: false) < 0);
        Assert.Equal(0, LiquidLensEffect.Readability(0, dark: true));
    }

    [Fact]
    public void Resting_light_comes_from_the_upper_left_and_turns_with_the_sweep()
    {
        var rest = LiquidLensEffect.RestingLight(0);
        Assert.Equal(1, rest.Length, 6);
        Assert.True(rest.X < 0 && rest.Y < 0);
        Assert.NotEqual(rest, LiquidLensEffect.RestingLight(-1.1));
    }

    [Fact]
    public void Flex_follows_how_fast_the_glass_changes_size()
    {
        Assert.Equal(0, GlassSurface.FlexForSpeed(1, 16));                                      // creeping: no flex
        var medium = GlassSurface.FlexForSpeed(20, 16);
        Assert.InRange(medium, 0.01, LiquidLensEffect.MaxFlex);
        Assert.True(GlassSurface.FlexForSpeed(40, 16) > medium);
        Assert.Equal(LiquidLensEffect.MaxFlex, GlassSurface.FlexForSpeed(400, 16), 6);         // capped
    }

    [Fact]
    public void Padded_view_widens_the_slice_round_the_glass()
    {
        var (view, got) = GlassSurface.PaddedView(new Rect(0.4, 0.4, 0.1, 0.05), 0, 0.5, 0.5);
        Assert.Equal(0.35, view.X, 9); Assert.Equal(0.2, view.Width, 9);
        Assert.Equal(0.375, view.Y, 9); Assert.Equal(0.1, view.Height, 9);
        Assert.Equal(new Thickness(0.5), got);
    }

    [Fact]
    public void Padded_view_stops_at_the_screen_edge_and_says_so()
    {
        var (view, got) = GlassSurface.PaddedView(new Rect(0, 0.9, 0.2, 0.1), 0, 0.5, 0.5);
        Assert.Equal(0, got.Left); Assert.Equal(0.5, got.Right, 9);
        Assert.Equal(0.5, got.Top, 9); Assert.Equal(0, got.Bottom, 9);
        Assert.InRange(view.Left, 0, 1); Assert.InRange(view.Right, 0, 1 + 1e-9);
        Assert.InRange(view.Top, 0, 1); Assert.InRange(view.Bottom, 0, 1 + 1e-9);
        // What the shader is told matches the picture: the glass is the view minus the padding on each side.
        Assert.Equal(0.2 * (1 + got.Left + got.Right), view.Width, 9);
        Assert.Equal(0.1 * (1 + got.Top + got.Bottom), view.Height, 9);
    }

    [Fact]
    public void Padded_view_keeps_the_gentle_magnification()
    {
        var (view, got) = GlassSurface.PaddedView(new Rect(0.4, 0.4, 0.1, 0.1), 0.1, 0.5, 0.5);
        Assert.Equal(0.09 * 2, view.Width, 9);      // magnified glass (0.09) plus half of it on each side
        Assert.Equal(0.36, view.X, 9);
        Assert.Equal(new Thickness(0.5), got);
        var (plain, none) = GlassSurface.PaddedView(new Rect(0.4, 0.4, 0.1, 0.1), 0.1, 0, 0);
        Assert.Equal(0.405, plain.X, 9); Assert.Equal(0.405, plain.Y, 9);
        Assert.Equal(0.09, plain.Width, 9); Assert.Equal(0.09, plain.Height, 9);
        Assert.Equal(new Thickness(0), none);
    }

    [Fact]
    public void Liquid_glass_needs_less_tint_to_stay_readable()
    {
        const double darkTint = 0.01, lightTint = 1.0;
        // Dark look on a bright wallpaper: the classic guard thickens the tint a lot; liquid glass, less.
        var classic = ThemeEngine.ReadableTintOpacity(0.8, darkTint, 0.12, dark: true, liquid: 0);
        var liquid = ThemeEngine.ReadableTintOpacity(0.8, darkTint, 0.12, dark: true, liquid: 1);
        Assert.True(classic > liquid);
        Assert.True(liquid > 0.12);
        // On a dark wallpaper nothing changes.
        Assert.Equal(0.12, ThemeEngine.ReadableTintOpacity(0.1, darkTint, 0.12, dark: true, liquid: 1), 9);
        // Light look on a dark wallpaper: the same the other way round.
        Assert.True(ThemeEngine.ReadableTintOpacity(0.05, lightTint, 0.16, dark: false, liquid: 0)
                    > ThemeEngine.ReadableTintOpacity(0.05, lightTint, 0.16, dark: false, liquid: 1));
        // Never more than fully opaque, never less than the floor.
        Assert.InRange(ThemeEngine.ReadableTintOpacity(1, darkTint, 0.9, true, 0), 0.05, 1);
        Assert.Equal(0.05, ThemeEngine.ReadableTintOpacity(0, darkTint, 0, true, 0), 9);
    }

    [Fact]
    public void Clear_looks_get_a_sharper_wallpaper_and_frosted_ones_stay_small()
    {
        Assert.Equal(WallpaperEngine.SharpDownscale, WallpaperEngine.DownscaleFor(4));
        Assert.Equal(WallpaperEngine.Downscale, WallpaperEngine.DownscaleFor(20));
        Assert.Equal(WallpaperEngine.Downscale, WallpaperEngine.DownscaleFor(44));
        Assert.Equal(720, WallpaperEngine.DecodeWidthFor(1920, WallpaperEngine.Downscale));   // unchanged for every existing look
        Assert.Equal(720, WallpaperEngine.DecodeWidthFor(3840, WallpaperEngine.Downscale));
        Assert.Equal(960, WallpaperEngine.DecodeWidthFor(1920, 2));
        Assert.Equal(1920, WallpaperEngine.DecodeWidthFor(3840, 2));
        Assert.Equal(1920, WallpaperEngine.DecodeWidthFor(7680, 2));                           // capped
        Assert.Equal(720, WallpaperEngine.DecodeWidthFor(1280, 2));
    }

    [Fact]
    public void Live_glass_is_sharper_and_quicker_for_clear_looks()
    {
        Assert.Equal(LiveGlass.SharpLevel, LiveGlass.LevelFor(4));
        Assert.Equal(LiveGlass.Level, LiveGlass.LevelFor(44));
        Assert.Equal((480, 270), LiveGlass.Shrunk(1920, 1080, LiveGlass.SharpLevel));
        Assert.Equal(LiveGlass.SharpGapMs, LiveGlass.Due(1000, 1000, calm: false, LiveGlass.GapFor(4)));
        Assert.Equal(LiveGlass.MinGapMs, LiveGlass.Due(1000, 1000, calm: false, LiveGlass.GapFor(44)));
        Assert.Equal(LiveGlass.CalmGapMs, LiveGlass.Due(1000, 1000, calm: true, LiveGlass.GapFor(4)));   // a still screen stays cheap
        Assert.Equal(1, LiveGlass.BlurRadius(4, LiveGlass.SharpLevel));
        Assert.Equal(LiveGlass.BlurRadius(44), LiveGlass.BlurRadius(44, LiveGlass.Level));
    }

    [Fact]
    public void Only_the_liquid_presets_use_the_lens_so_every_other_look_is_unchanged()
    {
        var liquid = Presets.All.Where(t => t.Dark.Liquid > 0 || t.Light.Liquid > 0).Select(t => t.Id).ToList();
        Assert.Equal(new[] { "liquid-clear", "liquid-frosted" }, liquid);
        Assert.All(Presets.All.Where(t => liquid.Contains(t.Id)), t => Assert.True(t.Dark.Liquid > 0 && t.Light.Liquid > 0));
    }

    [Fact]
    public void Glass_settings_saved_before_the_lens_load_without_it()
    {
        var g = JsonSerializer.Deserialize<GlassParams>("""{ "Tint": "#101010", "TintOpacity": 0.5, "Refraction": 0.06 }""", Json.Options)!;
        Assert.Equal(0, g.Liquid);
        Assert.Equal(0.5, g.TintOpacity);
        var edited = g.Clone();
        edited.Liquid = 1;
        Assert.Equal(1, edited.Clone().Liquid);   // copies carry it
        Assert.Equal(0, g.Liquid);
    }

    [Fact]
    public void Compiled_liquid_lens_ships_inside_the_app_as_pixel_shader_3()
    {
        var asm = typeof(LiquidLensEffect).Assembly;
        using var stream = asm.GetManifestResourceStream(asm.GetName().Name + ".g.resources");
        Assert.NotNull(stream);
        using var reader = new ResourceReader(stream!);
        byte[]? shader = null;
        foreach (DictionaryEntry e in reader)
            if ((string)e.Key == "design/shaders/liquidlens.ps" && e.Value is Stream s)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                shader = ms.ToArray();
            }
        Assert.NotNull(shader);
        // Pixel shader 3.0 bytecode begins with its version token: 0xFFFF0300.
        Assert.Equal(0xFFFF0300u, BitConverter.ToUInt32(shader!, 0));
    }
}
