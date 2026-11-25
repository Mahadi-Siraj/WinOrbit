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

        // With no wallpaper colours it still fades from the accent into the plain tint.
        var plain = Assert.IsType<LinearGradientBrush>(ThemeEngine.GradientTint(def, new List<GradientStopDef>(), darkTint, accent, true));
        Assert.Equal(2, plain.GradientStops.Count);
        Assert.Equal(darkTint, plain.GradientStops[1].Color);
        Assert.NotEqual(darkTint, plain.GradientStops[0].Color);
    }
}
