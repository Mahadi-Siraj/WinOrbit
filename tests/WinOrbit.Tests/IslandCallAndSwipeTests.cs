using WinOrbit.Island;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class IslandCallAndSwipeTests
{
    [Fact]
    public void A_touchpad_swipe_skips_once_however_many_steps_it_sends()
    {
        var g = new SwipeGesture();
        var results = new List<int>();
        for (var i = 0; i < 30; i++) results.Add(g.Feed(30, 1000 + i * 8));   // 900 units over a quarter second
        Assert.Equal(1, results.Count(r => r != 0));
        Assert.Equal(1, results.First(r => r != 0));
    }

    [Fact]
    public void Swiping_left_goes_back()
    {
        var g = new SwipeGesture();
        Assert.Equal(0, g.Feed(-120, 0));
        Assert.Equal(-1, g.Feed(-120, 40));
    }

    [Fact]
    public void Small_accidental_sideways_movement_does_nothing()
    {
        var g = new SwipeGesture();
        Assert.Equal(0, g.Feed(40, 0));
        Assert.Equal(0, g.Feed(50, 30));
        Assert.Equal(0, g.Feed(60, 60));
    }

    [Fact]
    public void After_a_pause_the_next_swipe_counts_again()
    {
        var g = new SwipeGesture();
        Assert.Equal(1, g.Feed(240, 0));
        Assert.Equal(0, g.Feed(240, 100));                       // the same swipe carrying on
        Assert.Equal(1, g.Feed(240, 100 + SwipeGesture.PauseMs + 1));
    }

    [Fact]
    public void Bouncing_back_at_the_end_of_a_swipe_does_not_undo_it()
    {
        var g = new SwipeGesture();
        Assert.Equal(1, g.Feed(300, 0));
        Assert.Equal(0, g.Feed(-300, 30));
    }

    [Fact]
    public void Direction_change_before_firing_restarts_the_count()
    {
        var g = new SwipeGesture();
        Assert.Equal(0, g.Feed(150, 0));
        Assert.Equal(0, g.Feed(-150, 20));   // would be +150 -150 = 0; restarts at -150 instead
        Assert.Equal(-1, g.Feed(-60, 40));
    }

    [Theory]
    [InlineData(new string[0], false, "Microphone")]
    [InlineData(new[] { "Teams" }, false, "Teams")]
    [InlineData(new[] { "Teams" }, true, "Teams · muted")]
    [InlineData(new[] { "Discord", "Zoom", "OBS" }, false, "Discord +2")]
    public void Pill_names_the_app_and_says_when_muted(string[] apps, bool muted, string expected)
        => Assert.Equal(expected, MicMeter.Label(apps, muted));

    [Fact]
    public void Voice_level_rests_low_in_silence_and_never_overflows()
    {
        Assert.Equal(0.18, MicMeter.Level(0), 3);
        Assert.Equal(1, MicMeter.Level(1), 3);
        Assert.Equal(1, MicMeter.Level(5), 3);
        Assert.Equal(0.18, MicMeter.Level(-1), 3);
        // Normal speech (a peak around 0.05) is clearly visible, not a flicker.
        Assert.InRange(MicMeter.Level(0.05), 0.35, 0.6);
    }
}
