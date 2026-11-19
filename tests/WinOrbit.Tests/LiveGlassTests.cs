using WinOrbit.Interop;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class LiveGlassTests
{
    private static MonitorInfo Mon(int handle, string device, int left) =>
        new((IntPtr)handle, device, new Native.RECT { Left = left, Right = left + 1920, Bottom = 1080 }, new Native.RECT { Left = left, Right = left + 1920, Bottom = 1040 }, left == 0, 1);

    [Fact]
    public void Only_screens_showing_glass_are_captured()
    {
        var monitors = new[] { Mon(1, "DISPLAY1", 0), Mon(2, "DISPLAY2", 1920) };
        var want = LiveGlass.Wanted(monitors, new[] { (IntPtr)2, (IntPtr)2 }, screenOn: true, fullscreenDevice: null);
        Assert.Equal(new[] { "DISPLAY2" }, want);
        Assert.Empty(LiveGlass.Wanted(monitors, Array.Empty<IntPtr>(), true, null));
    }

    [Fact]
    public void Nothing_is_captured_with_the_screen_off_or_under_a_fullscreen_app()
    {
        var monitors = new[] { Mon(1, "DISPLAY1", 0), Mon(2, "DISPLAY2", 1920) };
        var both = new[] { (IntPtr)1, (IntPtr)2 };
        Assert.Empty(LiveGlass.Wanted(monitors, both, screenOn: false, fullscreenDevice: null));
        Assert.Equal(new[] { "DISPLAY2" }, LiveGlass.Wanted(monitors, both, true, fullscreenDevice: "display1"));
    }

    [Fact]
    public void Updates_are_spaced_out_more_while_changes_are_small()
    {
        Assert.Equal(LiveGlass.MinGapMs, LiveGlass.Due(1000, 1000, calm: false));
        Assert.Equal(0, LiveGlass.Due(1000, 1000 + LiveGlass.MinGapMs + 5, calm: false));
        Assert.Equal(LiveGlass.CalmGapMs - LiveGlass.MinGapMs, LiveGlass.Due(1000, 1000 + LiveGlass.MinGapMs, calm: true));
    }

    [Fact]
    public void A_small_local_change_counts_even_when_the_rest_is_still()
    {
        const int w = 64, h = 32;
        var a = new byte[w * h * 4];
        var b = (byte[])a.Clone();
        Assert.Equal(0, LiveGlass.Change(a, b, w, h));
        // A window moving behind a small island: one 8 × 8 patch changes a lot.
        for (var y = 8; y < 16; y++)
            for (var x = 16; x < 24; x++)
                b[(y * w + x) * 4 + 2] = 200;
        Assert.True(LiveGlass.Change(a, b, w, h) >= LiveGlass.ChangeThreshold);
        // A gentle shimmer over everything stays below the bar.
        var c = (byte[])a.Clone();
        for (var i = 0; i < c.Length; i += 4) c[i] = 2;
        Assert.True(LiveGlass.Change(a, c, w, h) < LiveGlass.ChangeThreshold);
    }

    [Fact]
    public void Pictures_are_an_eighth_of_the_screen_with_matching_blur()
    {
        Assert.Equal((240, 135), LiveGlass.Shrunk(1920, 1080));
        Assert.Equal((480, 270), LiveGlass.Shrunk(3840, 2160));
        Assert.Equal((1, 1), LiveGlass.Shrunk(4, 4));
        Assert.Equal(3, LiveGlass.BlurRadius(44));
        Assert.Equal(1, LiveGlass.BlurRadius(0));
    }
}
