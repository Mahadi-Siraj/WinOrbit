using System.Text.Json;
using WinOrbit.Core;
using WinOrbit.Island;
using WinOrbit.Widgets;
using Xunit;

namespace WinOrbit.Tests;

public class WidgetSizeTests
{
    [Fact]
    public void Sizes_are_built_from_one_unit_and_one_gap()
    {
        Assert.Equal((160.0, 160.0), (WidgetSizes.Of("s").Width, WidgetSizes.Of("s").Height));
        Assert.Equal((336.0, 160.0), (WidgetSizes.Of("m").Width, WidgetSizes.Of("m").Height));
        Assert.Equal((512.0, 160.0), (WidgetSizes.Of("w").Width, WidgetSizes.Of("w").Height));
        Assert.Equal((336.0, 336.0), (WidgetSizes.Of("l").Width, WidgetSizes.Of("l").Height));
        // Every size is a whole number of the default 16 px snapping grid, so snapped widgets line up.
        foreach (var (id, _, _, _) in WidgetSizes.All)
        {
            Assert.Equal(0, WidgetSizes.Of(id).Width % 16);
            Assert.Equal(0, WidgetSizes.Of(id).Height % 16);
        }
    }

    [Fact]
    public void A_size_is_recognised_and_a_free_resize_is_not()
    {
        foreach (var (id, _, _, _) in WidgetSizes.All)
            Assert.Equal(id, WidgetSizes.Match(WidgetSizes.Of(id).Width, WidgetSizes.Of(id).Height));
        Assert.Null(WidgetSizes.Match(280, 150));    // the old default size, kept by widgets placed before 2.0
    }

    [Fact]
    public void Only_sizes_the_content_fits_are_offered()
    {
        Assert.Equal(new[] { "l" }, WidgetSizes.Fitting(230, 236).Select(s => s.Id));
        Assert.Equal(new[] { "s", "m", "w", "l" }, WidgetSizes.Fitting(120, 80).Select(s => s.Id));
        Assert.DoesNotContain("s", WidgetSizes.Fitting(200, 96).Select(s => s.Id));
    }

    [Fact]
    public void Every_new_widget_starts_at_a_standard_size_its_content_fits()
    {
        foreach (var k in WidgetCatalog.All)
        {
            Assert.NotNull(WidgetSizes.Match(k.Width, k.Height));
            Assert.True(k.Width >= k.MinWidth && k.Height >= k.MinHeight, k.Id);
        }
    }

    [Fact]
    public void Widgets_saved_before_2_0_load_with_their_size_and_glass()
    {
        var w = JsonSerializer.Deserialize<WidgetInstance>("""{ "Kind": "clock", "Width": 260, "Height": 128, "Variant": "stacked" }""", Json.Options)!;
        Assert.Equal((260.0, 128.0), (w.Width, w.Height));
        Assert.False(WidgetWindow.IsBare(w));
        w.Options["style"] = "bare";
        Assert.True(WidgetWindow.IsBare(w));
    }
}

public class ClockStyleTests
{
    [Fact]
    public void The_three_styles_from_before_keep_their_ids()
    {
        var ids = ClockWidget.Styles.Select(s => s.Id).ToList();
        Assert.Contains("", ids);
        Assert.Contains("stacked", ids);
        Assert.Contains("date", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ClockWidget.Styles, s => Assert.NotNull(WidgetSizes.All.FirstOrDefault(z => z.Id == s.Size).Name));
    }

    [Fact]
    public void Tracking_puts_a_gap_between_letters_and_keeps_characters_whole()
    {
        Assert.Equal("F R I D A Y", ClockWidget.Track("FRIDAY", " "));
        Assert.Equal("A", ClockWidget.Track("A", " "));
        Assert.Equal("", ClockWidget.Track("", " "));
        Assert.Equal("a 😀 b", ClockWidget.Track("a😀b", " "));          // the emoji is one character, not two halves
        Assert.Equal("é x", ClockWidget.Track("éx", " "));    // an accent stays on its letter
        Assert.Equal("AB", ClockWidget.Track("AB", ""));
    }

    [Theory]
    [InlineData(0, "Good night")]
    [InlineData(4, "Good night")]
    [InlineData(5, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(17, "Good afternoon")]
    [InlineData(18, "Good evening")]
    [InlineData(23, "Good evening")]
    public void Greeting_follows_the_hour(int hour, string expected) => Assert.Equal(expected, ClockWidget.Greeting(new DateTime(2026, 11, 25, hour, 30, 0)));

    [Fact]
    public void Day_progress_runs_from_0_to_100()
    {
        Assert.Equal(0, ClockWidget.DayPercent(new DateTime(2026, 11, 25, 0, 0, 0)));
        Assert.Equal(50, ClockWidget.DayPercent(new DateTime(2026, 11, 25, 12, 0, 0)));
        Assert.Equal(99, ClockWidget.DayPercent(new DateTime(2026, 11, 25, 23, 59, 59)));
    }

    [Fact]
    public void Stacked_hours_are_always_two_digits()
    {
        var t = new DateTime(2026, 11, 25, 21, 5, 0);
        Assert.Equal("21", ClockWidget.Hours(t, h24: true));
        Assert.Equal("09", ClockWidget.Hours(t, h24: false));
        Assert.Equal("12", ClockWidget.Hours(new DateTime(2026, 11, 25, 0, 5, 0), h24: false));
    }
}

public class IslandGlanceTests
{
    [Fact]
    public void Unknown_or_missing_glances_fall_back_to_the_clock()
    {
        Assert.Equal("clock", IslandWindow.GlanceOf(null));
        Assert.Equal("clock", IslandWindow.GlanceOf("sparkles"));
        foreach (var (id, _) in IslandWindow.Glances) Assert.Equal(id, IslandWindow.GlanceOf(id));
    }

    [Fact]
    public void Island_settings_from_before_2_0_keep_the_clock()
    {
        var s = JsonSerializer.Deserialize<IslandSettings>("""{ "ShowTime": true, "ShowDate": false, "ShowSystem": true }""", Json.Options)!;
        Assert.Equal("clock", s.Glance);
        Assert.Equal((true, false, true, false, false), IslandWindow.GlanceParts(s.Glance, s.ShowTime, s.ShowDate, s.ShowSystem, activity: false));
    }

    [Fact]
    public void The_clock_never_leaves_an_empty_capsule_but_quiet_may()
    {
        // Time, date and CPU all switched off, nothing playing: the time comes back so the pill isn't empty.
        Assert.Equal((true, false, false, false, false), IslandWindow.GlanceParts("clock", false, false, false, activity: false));
        // ...but with music playing there is something to show already.
        Assert.Equal((false, false, false, false, false), IslandWindow.GlanceParts("clock", false, false, false, activity: true));
        // Quiet is a choice: nothing beside the glass.
        Assert.Equal((false, false, false, false, false), IslandWindow.GlanceParts("quiet", true, true, true, activity: false));
    }

    [Fact]
    public void Each_other_glance_shows_only_its_own_part()
    {
        Assert.Equal((false, false, true, false, false), IslandWindow.GlanceParts("system", true, true, false, false));
        Assert.Equal((false, false, false, true, false), IslandWindow.GlanceParts("status", true, true, true, false));
        Assert.Equal((false, false, false, false, true), IslandWindow.GlanceParts("eyes", true, true, true, true));
    }

    [Fact]
    public void Eyes_look_towards_the_pointer_and_stop_at_the_rim()
    {
        Assert.Equal((0.0, 0.0), IslandWindow.LookAt(0, 0, 4.5, 2.6, 260));
        var (x, y) = IslandWindow.LookAt(2000, 0, 4.5, 2.6, 260);
        Assert.Equal(4.5, x, 9); Assert.Equal(0, y, 9);
        var (hx, _) = IslandWindow.LookAt(130, 0, 4.5, 2.6, 260);
        Assert.Equal(2.25, hx, 9);                                     // halfway out, halfway over
        var (dx, dy) = IslandWindow.LookAt(-3000, 3000, 4.5, 2.6, 260);
        Assert.True(dx < 0 && dy > 0);                                   // down and to the left
        Assert.True(Math.Abs(dx) <= 4.5 && Math.Abs(dy) <= 2.6);
    }

    [Fact]
    public void Eyes_move_in_half_pixel_steps_and_come_to_rest()
    {
        Assert.Equal(1.5, IslandWindow.Step(1.6));
        Assert.Equal(-2.0, IslandWindow.Step(-2.1));
        Assert.Equal(0.5, IslandWindow.Step(0.25));
        // Easing lands exactly on the target, so the timer can slow down and nothing redraws.
        var v = 0.0;
        for (var k = 0; k < 40 && v != 4.5; k++) v = IslandWindow.Approach(v, 4.5, 0.4);
        Assert.Equal(4.5, v);
        Assert.Equal(1.0, IslandWindow.Approach(0, 1, 1));
    }

    [Fact]
    public void A_blink_closes_and_reopens_the_lids()
    {
        Assert.Equal(0, IslandWindow.BlinkAt(0));
        Assert.Equal(0, IslandWindow.BlinkAt(150));
        Assert.Equal(0, IslandWindow.BlinkAt(500));
        Assert.Equal(1, IslandWindow.BlinkAt(0.45 * 150), 9);
        Assert.InRange(IslandWindow.BlinkAt(30), 0.01, 0.99);
        Assert.InRange(IslandWindow.BlinkAt(120), 0.01, 0.99);
    }

    [Fact]
    public void The_status_battery_warns_when_low_and_turns_green_when_charging()
    {
        Assert.Equal("T.Danger", IslandWindow.BatteryLook(15, charging: false).Brush);
        Assert.Equal("T.Good", IslandWindow.BatteryLook(15, charging: true).Brush);
        Assert.Equal("T.Text", IslandWindow.BatteryLook(80, charging: false).Brush);
        Assert.Equal(0.8, IslandWindow.BatteryLook(80, false).Fill, 9);
        Assert.True(IslandWindow.BatteryLook(0, false).Fill > 0);         // an empty battery still shows a sliver
        Assert.Equal(1, IslandWindow.BatteryLook(140, false).Fill, 9);
    }
}
