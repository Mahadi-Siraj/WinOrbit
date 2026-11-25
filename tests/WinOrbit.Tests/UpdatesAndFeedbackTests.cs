using System.Text.Json;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class AutomaticUpdateTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0);

    [Fact]
    public void A_check_is_due_when_never_checked_after_48_hours_or_after_the_clock_went_back()
    {
        Assert.True(Updates.Due(null, Now));
        Assert.False(Updates.Due(Now.AddHours(-1), Now));
        Assert.False(Updates.Due(Now.AddHours(-47.9), Now));
        Assert.True(Updates.Due(Now.AddHours(-48), Now));
        Assert.True(Updates.Due(Now.AddDays(-30), Now));
        Assert.True(Updates.Due(Now.AddDays(2), Now));   // last check "in the future": the clock was set back
    }

    [Fact]
    public void The_wait_is_short_when_due_and_runs_to_the_48_hour_mark_otherwise()
    {
        Assert.Equal(TimeSpan.FromSeconds(40), Updates.Wait(null, Now));
        Assert.Equal(TimeSpan.FromSeconds(40), Updates.Wait(Now.AddHours(-50), Now));
        var wait = Updates.Wait(Now.AddHours(-10), Now);
        Assert.Equal(TimeSpan.FromHours(38) + TimeSpan.FromSeconds(5), wait);
        Assert.True(wait < TimeSpan.FromDays(24));   // within what a DispatcherTimer can wait
    }

    [Fact]
    public void Only_a_newer_version_that_was_not_skipped_gets_a_card()
    {
        var current = new Version(2, 0, 0);
        Assert.False(Updates.ShouldTell(null, current, ""));
        Assert.False(Updates.ShouldTell(new Version(2, 0, 0, 0), current, ""));
        Assert.False(Updates.ShouldTell(new Version(1, 9), current, ""));
        Assert.True(Updates.ShouldTell(new Version(2, 1), current, ""));
        Assert.False(Updates.ShouldTell(new Version(2, 1), current, "2.1.0"));
        Assert.True(Updates.ShouldTell(new Version(2, 2), current, "2.1.0"));
        Assert.True(Updates.ShouldTell(new Version(2, 1), current, "not a version"));
    }

    [Fact]
    public void Download_links_point_at_the_release_file_itself()
    {
        Assert.Equal(AppInfo.Repository + "/releases/download/v2.1.0/WinOrbit-Setup-2.1.0.exe", Updates.DownloadUrl(new Version(2, 1), false));
        Assert.Equal(AppInfo.Repository + "/releases/download/v2.1.3/WinOrbit-Portable-2.1.3-win-x64.zip", Updates.DownloadUrl(new Version(2, 1, 3, 7), true));
        Assert.StartsWith("https://", Updates.DownloadUrl(new Version(3, 0), false));
    }

    [Fact]
    public void Old_settings_load_with_the_automatic_check_off_and_unasked()
    {
        var p = JsonSerializer.Deserialize<PrivacySettings>("""{ "NetworkAllowed": true, "LyricsEnabled": true }""", Json.Options)!;
        Assert.False(p.UpdateCheck);
        Assert.False(p.UpdateAsked);
        Assert.Null(p.UpdateLastCheck);
        Assert.Equal("", p.UpdateSkipped);
        Assert.False(new PrivacySettings().UpdateCheck);   // off by default for new installs too
    }
}

public class FeedbackLinkTests
{
    [Fact]
    public void Mail_links_escape_the_subject_and_body_and_use_crlf()
    {
        var url = AppInfo.MailTo("someone@example.com", "WinOrbit 2.0: bug & idea", "Line one\nLine two?\r\nA=B");
        Assert.StartsWith("mailto:someone@example.com?subject=", url);
        Assert.Contains("subject=WinOrbit%202.0%3A%20bug%20%26%20idea", url);
        Assert.Contains("&body=Line%20one%0D%0ALine%20two%3F%0D%0AA%3DB", url);
        Assert.DoesNotContain("%0D%0D", url);
    }

    [Fact]
    public void Feedback_links_lead_somewhere_without_a_GitHub_account()
    {
        Assert.Contains("@", AppInfo.Email);
        Assert.Equal("https://ig.me/m/" + AppInfo.Instagram.TrimStart('@'), AppInfo.InstagramMessageUrl);
        Assert.EndsWith("/issues/new/choose", AppInfo.IssuesUrl);
    }
}

public class LiveGlassRecoveryTests
{
    [Fact]
    public void Live_glass_retries_with_growing_waits_and_gives_up_after_four_recent_failures()
    {
        var now = new DateTime(2026, 11, 25, 13, 0, 0);
        Assert.Equal(TimeSpan.FromSeconds(3), LiveGlass.RetryAfter(Array.Empty<DateTime>(), now));
        Assert.Equal(TimeSpan.FromSeconds(6), LiveGlass.RetryAfter(new[] { now.AddMinutes(-1) }, now));
        Assert.Equal(TimeSpan.FromSeconds(24), LiveGlass.RetryAfter(new[] { now.AddMinutes(-3), now.AddMinutes(-2), now.AddMinutes(-1) }, now));
        Assert.Null(LiveGlass.RetryAfter(new[] { now.AddMinutes(-4), now.AddMinutes(-3), now.AddMinutes(-2), now.AddMinutes(-1) }, now));
        // Old failures (more than ten minutes ago, or "in the future" after a clock change) don't count.
        Assert.Equal(TimeSpan.FromSeconds(3), LiveGlass.RetryAfter(new[] { now.AddHours(-1), now.AddMinutes(-11), now.AddMinutes(-30), now.AddMinutes(-50), now.AddMinutes(5) }, now));
    }
}

public class ThemeLineupTests
{
    [Fact]
    public void Retired_themes_still_load_but_are_only_offered_while_in_use()
    {
        Assert.All(Presets.Retired, id => Assert.Contains(Presets.All, t => t.Id == id));   // old settings keep their look
        var offered = Presets.Offered(Presets.All, "neon").Select(t => t.Id).ToList();
        Assert.DoesNotContain("frosted-glass", offered);
        Assert.DoesNotContain("minimal-dark", offered);
        Assert.Contains("frosted-glass", Presets.Offered(Presets.All, "frosted-glass").Select(t => t.Id));
        // A user's own theme with a retired id-like name is never hidden.
        var mine = new Theme { Id = "minimal-dark", BuiltIn = false, Name = "Mine" };
        Assert.Contains(mine, Presets.Offered(new[] { mine }, "neon"));
    }

    [Fact]
    public void Built_in_themes_have_unique_ids_and_the_new_looks_have_their_own_colour()
    {
        Assert.Equal(Presets.All.Count, Presets.All.Select(t => t.Id).Distinct().Count());
        foreach (var id in new[] { "neon", "sakura", "ember" })
        {
            var t = Presets.All.Single(p => p.Id == id);
            Assert.False(t.AccentFromWallpaper);
            Assert.False(t.Gradient.FromWallpaper);
            Assert.Equal(3, t.Gradient.Stops.Count);
        }
        // Offered accents don't repeat, so no two fixed-colour looks are the same colour.
        var fixedAccents = Presets.Offered(Presets.All, "").Where(t => !t.AccentFromWallpaper).Select(t => t.AccentDark.ToUpperInvariant()).ToList();
        Assert.Equal(fixedAccents.Count, fixedAccents.Distinct().Count());
    }

    [Fact]
    public void Profiles_and_sets_only_use_themes_that_are_offered()
    {
        Assert.Equal(ProfileService.BuiltIn.Count, ProfileService.BuiltIn.Select(p => p.Id).Distinct().Count());
        var offered = Presets.Offered(Presets.All, "").Select(t => t.Id).ToHashSet();
        Assert.All(ProfileService.BuiltIn, p => Assert.Contains(p.ThemeId, offered));
        Assert.All(DesktopSets.BuiltIn.Where(s => s.ThemeId.Length > 0), s => Assert.Contains(s.ThemeId, offered));
        Assert.Contains(ProfileService.BuiltIn, p => p.Id == "lofi" && p.ThemeId == "neon");
    }
}

public class PolishTests
{
    [Fact]
    public void Theme_cards_show_each_looks_own_frost_tint_and_rim()
    {
        var clear = WinOrbit.Shell.Pages.AppearancePage.SwatchLook(Presets.All.Single(t => t.Id == "liquid-clear").Dark);
        var frosted = WinOrbit.Shell.Pages.AppearancePage.SwatchLook(Presets.All.Single(t => t.Id == "liquid-frosted").Dark);
        var pure = WinOrbit.Shell.Pages.AppearancePage.SwatchLook(Presets.All.Single(t => t.Id == "pure-liquid-glass").Dark);
        var amoled = WinOrbit.Shell.Pages.AppearancePage.SwatchLook(Presets.All.Single(t => t.Id == "amoled-black").Dark);
        Assert.True(clear.Blur < frosted.Blur && frosted.Blur < pure.Blur);   // clear is sharpest, pure the most frosted
        Assert.True(clear.Tint < pure.Tint && pure.Tint < amoled.Tint);
        Assert.Equal(1, amoled.Tint);
        Assert.Equal(0, amoled.Blur);
        Assert.True(clear.Rim > pure.Rim);                                    // liquid looks get the thick rim
        Assert.InRange(clear.Tint, 0.14, 0.2);                                // floor keeps the lines readable
    }

    [Fact]
    public void Wide_controls_go_under_their_label_only_when_the_row_is_narrow()
    {
        Assert.False(WinOrbit.Shell.Pages.Kit.StackControl(700, 290));   // Surface picker in a normal window
        Assert.True(WinOrbit.Shell.Pages.Kit.StackControl(500, 290));    // the same in a narrow one
        Assert.False(WinOrbit.Shell.Pages.Kit.StackControl(500, 46));    // a switch never moves
        Assert.False(WinOrbit.Shell.Pages.Kit.StackControl(0, 290));     // not laid out yet
    }
}

public class ProfileBrightnessTests
{
    [Fact]
    public void Leaving_a_dimming_profile_puts_the_brightness_back_once()
    {
        Assert.Equal((35, true, false), ProfileService.BrightnessPlan(35, null));    // Night: remember, then dim
        Assert.Equal((35, false, false), ProfileService.BrightnessPlan(35, 80));     // Night again: keep the first level
        Assert.Equal((80, false, true), ProfileService.BrightnessPlan(null, 80));    // Study after Night: back to 80
        Assert.Equal(((int?)null, false, false), ProfileService.BrightnessPlan(null, null));   // nothing to do
        Assert.Equal("lofi", ProfileService.Find(ProfileService.BuiltIn, "Lo-fi")!.Id);
        Assert.Equal("lofi", ProfileService.Find(ProfileService.BuiltIn, "LOFI")!.Id);
        Assert.Equal("night", ProfileService.Find(ProfileService.BuiltIn, " night ")!.Id);
        Assert.Null(ProfileService.Find(ProfileService.BuiltIn, "--"));
        Assert.Null(ProfileService.Find(ProfileService.BuiltIn, "party"));
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{ "ActiveThemeId": "neon" }""", Json.Options)!.BrightnessBeforeProfile);
    }
}

public class SnapshotTests
{
    [Fact]
    public void Snapshots_get_sortable_names_and_windows_land_where_they_sit()
    {
        Assert.Equal("WinOrbit 2026-11-25 093005.png", Snapshot.FileName(new DateTime(2026, 11, 25, 9, 30, 5)));
        // A second screen to the left makes the picture start at a negative x.
        var r = Snapshot.Place(new WinOrbit.Interop.Native.RECT { Left = 860, Top = 10, Right = 1060, Bottom = 50 }, -1920, 0);
        Assert.Equal(new System.Windows.Int32Rect(2780, 10, 200, 40), r);
        var empty = Snapshot.Place(new WinOrbit.Interop.Native.RECT { Left = 5, Top = 5, Right = 3, Bottom = 3 }, 0, 0);
        Assert.Equal(0, empty.Width);
    }
}

public class SurfaceTests
{
    [Fact]
    public void Surface_names_are_read_forgivingly()
    {
        Assert.Equal("Glass", GlassSurface.Material(null));
        Assert.Equal("Glass", GlassSurface.Material(""));
        Assert.Equal("Glass", GlassSurface.Material("Frosted"));
        Assert.Equal("Black", GlassSurface.Material("black"));
        Assert.Equal("Gradient", GlassSurface.Material(" Gradient "));
        Assert.Equal(new[] { "Glass", "Gradient", "Smoked", "Solid", "Black" }, GlassSurface.Materials);
    }

    [Fact]
    public void Old_layouts_load_with_glass_widgets()
    {
        var l = JsonSerializer.Deserialize<LayoutSettings>("""{ "SnapToGrid": true, "GridSize": 16 }""", Json.Options)!;
        Assert.Equal("Glass", l.WidgetMaterial);
    }

    [Fact]
    public void The_gradient_surface_is_opaque_and_stays_close_to_the_tint()
    {
        var def = new GradientDef { Kind = GradientKind.Radial, Angle = 90 };
        var stops = new List<GradientStopDef> { new("#FF0000", 0), new("#0000FF", 1) };
        var darkTint = Color.FromRgb(0x12, 0x15, 0x1C);
        var accent = Color.FromRgb(0x7F, 0xB5, 0xFF);
        var brush = Assert.IsType<LinearGradientBrush>(ThemeEngine.GradientTint(def, stops, darkTint, accent, dark: true));
        Assert.Equal(1, brush.Opacity);
        Assert.Equal(3, brush.GradientStops.Count);   // the accent first, then the wallpaper's colours
        Assert.All(brush.GradientStops, s => Assert.Equal(255, s.Color.A));
        // About a third of the colour at most, so light text stays readable on dark looks.
        Assert.All(brush.GradientStops, s => Assert.True(WinOrbit.Wallpaper.ColorMath.Luminance(s.Color) < 0.16));
        Assert.True(ThemeEngine.GradientMix(true) <= 0.35 && ThemeEngine.GradientMix(false) <= 0.3);
        // Light looks stay light, so dark text stays readable.
        var light = Assert.IsType<LinearGradientBrush>(ThemeEngine.GradientTint(def, stops, Color.FromRgb(0xF6, 0xF8, 0xFC), Color.FromRgb(0x24, 0x63, 0xEB), false));
        Assert.All(light.GradientStops, s => Assert.True(WinOrbit.Wallpaper.ColorMath.Luminance(s.Color) > 0.4));

        // Bright wallpaper colours keep their hue and colour (deep orange, not brown); greys stay close to the tint.
        var orange = ThemeEngine.GradientShade(darkTint, Color.FromRgb(0xFF, 0x96, 0x28), true);
        var (oh, os, ol) = WinOrbit.Wallpaper.ColorMath.ToHsl(orange);
        Assert.InRange(oh, 20, 40);
        Assert.True(os >= 0.35 && ol < 0.25);
        var grey = ThemeEngine.GradientShade(darkTint, Color.FromRgb(0x80, 0x80, 0x80), true);
        Assert.True(WinOrbit.Wallpaper.ColorMath.Luminance(grey) < 0.08);
        foreach (var hue in new[] { 0, 60, 120, 180, 240, 300 })
        {
            var vivid = WinOrbit.Wallpaper.ColorMath.FromHsl(hue, 1, 0.5);
            Assert.True(WinOrbit.Wallpaper.ColorMath.Luminance(ThemeEngine.GradientShade(darkTint, vivid, true)) < 0.12);   // white text reads
            Assert.True(WinOrbit.Wallpaper.ColorMath.Luminance(ThemeEngine.GradientShade(Color.FromRgb(0xF6, 0xF8, 0xFC), vivid, false)) > 0.5);   // dark text reads
        }

        // With no wallpaper colours it still fades from the accent into the plain tint.
        var plain = Assert.IsType<LinearGradientBrush>(ThemeEngine.GradientTint(def, new List<GradientStopDef>(), darkTint, accent, true));
        Assert.Equal(2, plain.GradientStops.Count);
        Assert.Equal(darkTint, plain.GradientStops[1].Color);
        Assert.NotEqual(darkTint, plain.GradientStops[0].Color);
    }
}
