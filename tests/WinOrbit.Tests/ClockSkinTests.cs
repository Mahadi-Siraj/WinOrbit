using System.Globalization;
using WinOrbit.Core;
using WinOrbit.Widgets;
using Xunit;

namespace WinOrbit.Tests;

public class OrbitTypeTests
{
    [Fact]
    public void Every_capital_and_figure_has_an_orbit_letter_that_parses()
    {
        var chars = OrbitType.Characters.ToHashSet();
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:-.,") Assert.Contains(c, chars);
        // Building every letter parses every path; a typo in one would throw here.
        var all = new string(OrbitType.Characters.ToArray());
        var geo = OrbitType.Build(all, 24, 3);
        Assert.NotNull(geo);
        Assert.False(geo!.Bounds.IsEmpty);
    }

    [Fact]
    public void English_day_and_month_names_are_drawn_in_orbit_letters()
    {
        var en = CultureInfo.GetCultureInfo("en-GB").DateTimeFormat;
        foreach (var name in en.DayNames.Concat(en.AbbreviatedDayNames).Concat(en.MonthNames.Where(m => m.Length > 0)))
            Assert.True(OrbitType.Supports(name), name);
    }

    [Theory]
    [InlineData("miércoles")]   // accented: falls back to a Windows font
    [InlineData("星期三")]
    [InlineData("")]
    [InlineData("   ")]
    public void Text_orbit_letters_cannot_draw_falls_back(string text)
    {
        Assert.False(OrbitType.Supports(text));
        Assert.Null(OrbitType.Build(text, 24, 3));
    }

    [Fact]
    public void Width_is_the_letters_plus_the_tracking_between_them()
    {
        Assert.Equal(12 + 3 + 11, OrbitType.Measure("AB", 3));
        Assert.Equal(12 + OrbitType.SpaceWidth + 12, OrbitType.Measure("A A", 0));
        Assert.Equal(0, OrbitType.Measure("I", 9));
        Assert.Null(OrbitType.Advance('é'));
        Assert.Equal(12, OrbitType.Advance('a'));   // lower case draws as capitals
        // The drawn shape spans the measured width (scaled from the 12-unit cap height to 24 px).
        var geo = OrbitType.Build("FRIDAY", 24, 9)!;
        Assert.InRange(geo.Bounds.Width, OrbitType.Measure("FRIDAY", 9) * 2 - 0.5, OrbitType.Measure("FRIDAY", 9) * 2 + 0.5);
        Assert.InRange(geo.Bounds.Height, 23.5, 24.5);
    }
}

public class ClockSkinTests
{
    [Fact]
    public void Clock_styles_have_unique_ids_and_real_sizes()
    {
        Assert.Equal(17, ClockWidget.Styles.Length);
        Assert.Equal(ClockWidget.Styles.Length, ClockWidget.Styles.Select(s => s.Id).Distinct().Count());
        var sizes = WidgetSizes.All.Select(s => s.Id).ToHashSet();
        foreach (var s in ClockWidget.Styles) Assert.Contains(s.Size, sizes);
        foreach (var id in new[] { "orbit", "hairline", "poster", "label", "week", "heavy", "story" })
            Assert.Contains(ClockWidget.Styles, s => s.Id == id);
    }

    [Fact]
    public void Week_row_starts_on_the_cultures_first_day()
    {
        Assert.Equal(new[] { "S", "M", "T", "W", "T", "F", "S" }, ClockWidget.WeekLetters(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(new[] { "M", "T", "W", "T", "F", "S", "S" }, ClockWidget.WeekLetters(CultureInfo.GetCultureInfo("en-GB")));
        var wednesday = new DateTime(2026, 11, 25);
        Assert.Equal(3, ClockWidget.WeekIndex(wednesday, DayOfWeek.Sunday));
        Assert.Equal(2, ClockWidget.WeekIndex(wednesday, DayOfWeek.Monday));
        Assert.Equal(6, ClockWidget.WeekIndex(new DateTime(2026, 11, 29), DayOfWeek.Monday));   // Sunday ends a Monday week
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 29, 0)]
    [InlineData(0, 30, 1)]
    [InlineData(12, 0, 24)]
    [InlineData(23, 59, 47)]
    public void Label_lights_one_tick_per_half_hour_gone(int h, int m, int lit) =>
        Assert.Equal(lit, ClockWidget.HalfHoursGone(new DateTime(2026, 11, 25, h, m, 0)));

    [Fact]
    public void Label_ticks_form_an_uneven_skyline_that_never_vanishes()
    {
        var heights = Enumerable.Range(0, 48).Select(ClockWidget.TickHeight).ToList();
        Assert.All(heights, v => Assert.InRange(v, 0.3, 1.0));
        Assert.True(heights.Max() - heights.Min() > 0.4);
        Assert.Equal(heights, Enumerable.Range(0, 48).Select(ClockWidget.TickHeight));   // the same every day
    }

    [Theory]
    [InlineData(150, 232, 1.0)]     // fits: full size
    [InlineData(212, 232, 1.0)]     // exactly 10 px clear at each end
    [InlineData(424, 232, 0.5)]     // a long month or language: shrinks to keep the margins
    [InlineData(0, 232, 1.0)]       // nothing drawn
    public void Label_bar_letters_shrink_only_when_they_would_run_off_the_bar(double text, double bar, double scale) =>
        Assert.Equal(scale, ClockWidget.KnockoutScale(text, bar), 3);

    [Fact]
    public void Story_tells_the_time_as_a_sentence()
    {
        Assert.Equal("It's 16:35 in the afternoon. 69% of the day has passed.", ClockWidget.StoryLine(new DateTime(2026, 10, 2, 16, 35, 0), true));
        Assert.Equal("It's 2:12 at night. 9% of the day has passed.", ClockWidget.StoryLine(new DateTime(2026, 11, 25, 2, 12, 0), false));
        Assert.Equal("It's 09:05 in the morning. 37% of the day has passed.", ClockWidget.StoryLine(new DateTime(2026, 11, 25, 9, 5, 0), true));
        Assert.Contains("in the evening", ClockWidget.StoryLine(new DateTime(2026, 11, 25, 19, 0, 0), true));
    }

    [Fact]
    public void Story_gradient_runs_from_the_accent_into_a_neighbouring_hue()
    {
        var brush = ClockWidget.AccentGradient(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        Assert.True(brush.IsFrozen);
        Assert.Equal(2, brush.GradientStops.Count);
        Assert.NotEqual(brush.GradientStops[0].Color, brush.GradientStops[1].Color);
        // A grey accent still gives a coloured gradient (saturation is lifted), never a flat grey.
        var grey = ClockWidget.AccentGradient(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
        Assert.NotEqual(grey.GradientStops[0].Color.R, grey.GradientStops[0].Color.B);
    }
}

public class MatchingWidgetTests
{
    [Theory]
    [InlineData("mono", "terminal")]
    [InlineData("dots", "terminal")]
    [InlineData("poster", "condensed")]
    [InlineData("label", "condensed")]
    [InlineData("heavy", "condensed")]
    [InlineData("script", "condensed")]
    [InlineData("week", "condensed")]
    [InlineData("orbit", "airy")]
    [InlineData("hairline", "airy")]
    [InlineData("spaced", "airy")]
    [InlineData("story", "airy")]
    [InlineData("", "hud")]
    [InlineData("greeting", "hud")]
    [InlineData("something-new", "hud")]
    [InlineData(null, "hud")]
    public void Widgets_take_the_lettering_of_the_clock(string? style, string lettering) =>
        Assert.Equal(lettering, WidgetLook.LetteringFor(style).Id);

    [Fact]
    public void The_lettering_follows_the_first_clock_that_is_shown()
    {
        var list = new List<WidgetInstance>
        {
            new() { Kind = "system" },
            new() { Kind = "clock", Variant = "mono", Visible = false },
            new() { Kind = "clock", Variant = "label" },
            new() { Kind = "clock", Variant = "orbit" },
        };
        Assert.Equal("label", WidgetLook.LeadClock(list));
        Assert.Null(WidgetLook.LeadClock(new[] { new WidgetInstance { Kind = "media" } }));
    }

    [Fact]
    public void Old_settings_match_the_clock_lettering_by_default()
    {
        Assert.True(new LayoutSettings().MatchClockLettering);
        var old = System.Text.Json.JsonSerializer.Deserialize<LayoutSettings>("{\"SnapToGrid\":true,\"GridSize\":16}")!;
        Assert.True(old.MatchClockLettering);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.0, 0)]
    [InlineData(0.4, 1)]
    [InlineData(57.0, 12)]
    [InlineData(100.0, 20)]
    public void Slashes_light_in_proportion_and_always_total_twenty(double? percent, int lit)
    {
        var (on, off) = SystemWidget.Slashes(percent);
        Assert.Equal(lit, on.Length);
        Assert.Equal(SystemWidget.SlashCount, on.Length + off.Length);
        Assert.All((on + off).ToCharArray(), c => Assert.Equal('/', c));
    }

    [Fact]
    public void Recycle_bin_label_lines()
    {
        Assert.Equal(("Files 0", "Size 0 B"), RecycleBinWidget.LabelLines(0, 0));
        var (files, size) = RecycleBinWidget.LabelLines(11, 707_264_512);
        Assert.Equal("Files 11", files);
        Assert.StartsWith("Size ", size);
        Assert.NotEqual("Size 0 B", size);
    }

    [Fact]
    public void Icon_row_inks_light_parts_fully_and_dark_parts_faintly()
    {
        // A dark tile with a white logo, a clear pixel and a soft edge: white gets full ink, black the floor, clear stays clear.
        byte[] px = { 255, 255, 255, 255, 0, 0, 0, 255, 10, 20, 30, 0, 255, 255, 255, 100 };
        LauncherWidget.Ink(px, false);
        var floor = (byte)Math.Round(255 * LauncherWidget.InkFloor);
        Assert.Equal(new byte[] { 255, 255, 255, 255, floor, floor, floor, floor, 0, 0, 0, 0, 100, 100, 100, 100 }, px);
    }

    [Fact]
    public void Icon_row_draws_a_one_colour_icon_as_a_solid_silhouette()
    {
        byte[] px = { 40, 40, 200, 255, 50, 40, 200, 255, 40, 40, 200, 128 };   // all red; one pixel half see-through
        LauncherWidget.Ink(px, false);
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 128, 128, 128, 128 }, px);
    }

    [Fact]
    public void Icon_row_reads_premultiplied_pixels_by_their_real_colour()
    {
        // White at half alpha is stored premultiplied as 128s: it is still the lightest colour, so it gets all of its alpha as ink.
        byte[] px = { 128, 128, 128, 128, 0, 0, 0, 255 };
        LauncherWidget.Ink(px, true);
        Assert.Equal(128, px[3]);
        Assert.Equal((byte)Math.Round(255 * LauncherWidget.InkFloor), px[7]);
    }

    [Fact]
    public void Matching_pieces_are_real_widget_styles()
    {
        var sizes = WidgetSizes.All.Select(s => s.Id).ToHashSet();
        foreach (var (kind, variant, _, _, size) in WidgetCatalog.Companions)
        {
            Assert.NotNull(WidgetCatalog.Find(kind));
            Assert.False(string.IsNullOrEmpty(variant));
            Assert.Contains(size, sizes);
        }
    }
}
