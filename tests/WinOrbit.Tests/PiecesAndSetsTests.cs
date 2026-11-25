using System.IO;
using System.Windows;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Island;
using WinOrbit.Services;
using WinOrbit.Shell;
using WinOrbit.Shell.Pages;
using WinOrbit.Widgets;
using Xunit;

namespace WinOrbit.Tests;

public class MatchingPieceTests
{
    [Fact]
    public void Battery_reads_charging_full_draining_and_no_battery()
    {
        var charging = BatteryWidget.Describe(1, 8, 64, -1);
        Assert.True(charging.HasBattery);
        Assert.Equal(64, charging.Percent);
        Assert.True(charging.Charging);
        Assert.Equal("64%", charging.Value);
        Assert.Equal("Charging", charging.Line);

        Assert.Equal("Fully charged", BatteryWidget.Describe(1, 1, 100, -1).Line);
        Assert.Equal("3 h 10 min left", BatteryWidget.Describe(0, 1, 70, 3 * 3600 + 600).Line);
        Assert.Equal("On battery", BatteryWidget.Describe(0, 1, 70, -1).Line);

        var desktop = BatteryWidget.Describe(1, 128, 255, -1);
        Assert.False(desktop.HasBattery);
        Assert.Null(desktop.Percent);
        Assert.Equal("No battery", desktop.Line);
        Assert.False(BatteryWidget.Describe(255, 255, 255, -1).HasBattery);
    }

    [Fact]
    public void Battery_time_left_is_short()
    {
        Assert.Equal("45 min", BatteryWidget.Left(TimeSpan.FromMinutes(45)));
        Assert.Equal("1 min", BatteryWidget.Left(TimeSpan.FromSeconds(20)));
        Assert.Equal("2 h 0 min", BatteryWidget.Left(TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Drives_shows_ready_fixed_drives_in_letter_order_and_at_most_four()
    {
        var drives = new[]
        {
            new DrivesWidget.DriveFacts("D:\\", true, true, 100, 50),
            new DrivesWidget.DriveFacts("C:\\", true, true, 100, 10),
            new DrivesWidget.DriveFacts("Z:\\", false, true, 100, 10),   // network
            new DrivesWidget.DriveFacts("E:\\", true, false, 100, 10),   // card reader with nothing in it
            new DrivesWidget.DriveFacts("F:\\", true, true, 0, 0),
            new DrivesWidget.DriveFacts("G:\\", true, true, 100, 1),
            new DrivesWidget.DriveFacts("H:\\", true, true, 100, 1),
            new DrivesWidget.DriveFacts("I:\\", true, true, 100, 1),
        };
        var picked = DrivesWidget.Pick(drives).Select(d => d.Name).ToList();
        Assert.Equal(new[] { "C:\\", "D:\\", "G:\\", "H:\\" }, picked);
    }

    [Fact]
    public void Drive_use_is_a_clamped_percentage()
    {
        Assert.Equal(90, DrivesWidget.UsedPercent(1000, 100));
        Assert.Equal(0, DrivesWidget.UsedPercent(0, 0));
        Assert.Equal(0, DrivesWidget.UsedPercent(1000, 5000));
        Assert.Equal(100, DrivesWidget.UsedPercent(1000, -5));
    }

    [Fact]
    public void Own_quotes_parse_with_and_without_an_author()
    {
        var q = QuoteWidget.Parse("Stay curious\tMe\n\n  Just text  \n\t\n");
        Assert.Equal(2, q.Count);
        Assert.Equal(("Stay curious", "Me"), q[0]);
        Assert.Equal(("Just text", ""), q[1]);
        Assert.Empty(QuoteWidget.Parse(""));
    }

    [Fact]
    public void The_quote_changes_once_a_day_and_next_moves_it_on()
    {
        var day = new DateTime(2026, 11, 25, 9, 0, 0);
        var today = QuoteWidget.Pick(22, day, 0);
        Assert.Equal(today, QuoteWidget.Pick(22, day.AddHours(14), 0));
        Assert.Equal((today + 1) % 22, QuoteWidget.Pick(22, day.AddDays(1), 0));
        Assert.Equal((today + 1) % 22, QuoteWidget.Pick(22, day, 1));
        Assert.InRange(QuoteWidget.Pick(22, day, -1000), 0, 21);
        Assert.Equal(-1, QuoteWidget.Pick(0, day, 0));
        Assert.NotEmpty(QuoteWidget.BuiltIn);
        Assert.All(QuoteWidget.BuiltIn, b => { Assert.NotEmpty(b.Text); Assert.NotEmpty(b.Author); });
    }

    [Fact]
    public void The_music_line_is_mirrored_quiet_at_the_ends_and_flat_in_silence()
    {
        var history = Enumerable.Range(0, VisualiserWidget.History).Select(i => 1.0 - i / (double)VisualiserWidget.History).ToList();
        var points = VisualiserWidget.Shape(history, 7);
        Assert.Equal(history.Count * 2 + 1, points.Length);
        for (var k = 0; k < points.Length; k++) Assert.Equal(points[k], points[points.Length - 1 - k], 9);
        Assert.Equal(0, points[0]);
        Assert.Equal(0, points[^1]);
        Assert.All(points, p => Assert.InRange(p, -1, 1));
        Assert.All(VisualiserWidget.Shape(new double[VisualiserWidget.History], 3), p => Assert.Equal(0, p));
    }

    [Fact]
    public void A_sample_keeps_its_direction_as_it_moves_outwards()
    {
        var loud = Enumerable.Repeat(0.8, VisualiserWidget.History).ToList();
        var mid = VisualiserWidget.History;
        var before = VisualiserWidget.Shape(loud, 10);
        var after = VisualiserWidget.Shape(loud, 11);
        // The sample at age a on tick t is at age a+1 on tick t+1: same sign.
        for (var age = 0; age < 10; age++) Assert.Equal(Math.Sign(before[mid + age]), Math.Sign(after[mid + age + 1]));
    }

    [Fact]
    public void Now_youre_listening_lines()
    {
        Assert.Equal(("# Now you're listening #", "Artist • ", "Song"), MediaWidget.ListeningLines(true, " Song ", "Artist", "Spotify"));
        Assert.Equal(("# Now you're listening #", "Spotify • ", "Song"), MediaWidget.ListeningLines(true, "Song", "", "Spotify"));
        Assert.Equal(("# Now you're listening #", "", "Song"), MediaWidget.ListeningLines(true, "Song", " ", ""));
        Assert.Equal("Nothing playing", MediaWidget.ListeningLines(false, "", "", "").Head);
    }

    [Fact]
    public void Every_new_piece_is_in_the_catalog_and_has_a_matching_card()
    {
        foreach (var id in new[] { "battery", "drives", "visualiser", "quote" })
        {
            Assert.NotNull(WidgetCatalog.Find(id));
            Assert.Contains(WidgetCatalog.Companions, c => c.Kind == id);
        }
        Assert.Contains(WidgetCatalog.Companions, c => c.Kind == "media" && c.Variant == "listening");
    }
}

public class DesktopSetTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);
    private static bool Known(string kind) => WidgetCatalog.Find(kind) is not null;
    private static readonly List<string> Glances = IslandWindow.Glances.Select(g => g.Id).ToList();

    [Fact]
    public void Positions_round_trip_and_stay_on_screen()
    {
        var (x, y) = DesktopSets.ToPixels(0.5, 0.5, 400, 200, Work);
        Assert.Equal((760d, 420d), (x, y));
        var (fx, fy) = DesktopSets.ToFraction(x, y, 400, 200, Work);
        Assert.Equal((0.5, 0.5), (fx, fy));

        // A centre at the very corner still keeps the whole widget inside the work area.
        Assert.Equal((0d, 0d), DesktopSets.ToPixels(0, 0, 400, 200, Work));
        Assert.Equal((1520d, 840d), DesktopSets.ToPixels(1, 1, 400, 200, Work));
        Assert.Equal((760d, 420d), DesktopSets.ToPixels(double.NaN, double.PositiveInfinity, 400, 200, Work));
        // On a second display the work area doesn't start at 0.
        var offset = new Rect(0, 48, 1920, 1032);
        Assert.Equal(48d, DesktopSets.ToPixels(0.5, 0, 400, 200, offset).Y);
    }

    [Fact]
    public void A_shared_set_is_cleaned_before_use()
    {
        var w = new WidgetInstance { Kind = "launcher", X = 7, Y = -3, Width = 99999, Height = double.NaN, Opacity = 0 };
        w.Options["items"] = "C:\\Users\\someone\\secret.exe";
        w.Options["style"] = "bare";
        var quote = new WidgetInstance { Kind = "quote", X = 0.3, Y = 0.4, Width = 512, Height = 160 };
        quote.Options["mine"] = "My own words\tMe";
        quote.Options["only"] = "1";
        var set = new DesktopSet
        {
            Id = "../../evil", Name = new string('n', 500), Glance = "not-a-glance", ThemeId = "midnight",
            Widgets = new() { w, quote, new WidgetInstance { Kind = "virus" }, null! },
        };
        var clean = DesktopSets.Clean(set, Known, Glances)!;
        Assert.NotNull(clean);
        Assert.DoesNotContain("/", clean.Id);
        Assert.Equal(60, clean.Name.Length);
        Assert.Equal("", clean.Glance);
        Assert.Equal(2, clean.Widgets.Count);
        var l = clean.Widgets[0];
        Assert.Equal((1d, 0d), (l.X, l.Y));
        Assert.Equal(2000, l.Width);
        Assert.Equal(160, l.Height);
        Assert.Equal(0.2, l.Opacity);
        Assert.False(l.Options.ContainsKey("items"));
        Assert.Equal("bare", l.Options["style"]);
        Assert.Empty(clean.Widgets[1].Options);
    }

    [Fact]
    public void A_set_with_nothing_usable_is_refused()
    {
        Assert.Null(DesktopSets.Clean(null, Known, Glances));
        Assert.Null(DesktopSets.Clean(new DesktopSet { Widgets = new() { new WidgetInstance { Kind = "nope" } } }, Known, Glances));
        var many = new DesktopSet { Widgets = Enumerable.Range(0, 60).Select(_ => new WidgetInstance { Kind = "clock" }).ToList() };
        Assert.Equal(DesktopSets.MaxWidgets, DesktopSets.Clean(many, Known, Glances)!.Widgets.Count);
    }

    [Fact]
    public void Built_in_sets_use_real_widgets_themes_and_glances()
    {
        Assert.Equal(DesktopSets.BuiltIn.Count, DesktopSets.BuiltIn.Select(s => s.Id).Distinct().Count());
        Assert.Equal(3, DesktopSets.Starters.Count());
        foreach (var set in DesktopSets.BuiltIn)
        {
            Assert.True(set.BuiltIn);
            Assert.NotEmpty(set.Name);
            Assert.NotEmpty(set.Description);
            Assert.Contains(Presets.All, t => t.Id == set.ThemeId);
            Assert.Contains(set.Glance, Glances);
            Assert.Contains(set.Widgets, w => w.Kind == "clock");
            foreach (var w in set.Widgets)
            {
                Assert.True(Known(w.Kind), w.Kind);
                Assert.InRange(w.X, 0, 1);
                Assert.InRange(w.Y, 0, 1);
                Assert.Equal("bare", w.Options["style"]);
                if (w.Kind == "clock") Assert.Contains(ClockWidget.Styles, s => s.Id == w.Variant);
            }
            // A set survives its own cleaning unchanged in what it shows.
            Assert.Equal(set.Widgets.Count, DesktopSets.Clean(set, Known, Glances)!.Widgets.Count);
        }
    }

    [Fact]
    public void A_set_file_round_trips_through_json()
    {
        var file = Path.Combine(Path.GetTempPath(), "gw-set-" + Guid.NewGuid().ToString("N") + DesktopSets.Extension);
        try
        {
            Json.Write(file, new DesktopSetFile { Set = DesktopSets.BuiltIn[0] });
            var back = Json.Read<DesktopSetFile>(file)!;
            Assert.Equal(DesktopSets.FileFormat, back.Format);
            Assert.Equal(DesktopSets.BuiltIn[0].Widgets.Count, back.Set!.Widgets.Count);
            Assert.False(back.Set.BuiltIn);   // never trusted from a file
        }
        finally { File.Delete(file); }
    }
}

public class SimpleSettingsAndLiquidToggleTests
{
    [Fact]
    public void The_liquid_glass_switch_turns_the_lens_off_and_keeps_the_theme_amount_when_on()
    {
        Assert.Equal(0, ThemeEngine.EffectiveLiquid(0.8, false));
        Assert.Equal(0.8, ThemeEngine.EffectiveLiquid(0.8, true));
        Assert.Equal(1, ThemeEngine.EffectiveLiquid(3, true));
        Assert.Equal(0, ThemeEngine.EffectiveLiquid(-1, true));
        Assert.Contains(Presets.All, ThemeEngine.HasLiquid);
        Assert.Contains(Presets.All, t => !ThemeEngine.HasLiquid(t));
        Assert.True(ThemeEngine.HasLiquid(Presets.All.First(t => t.Id == ThemeEngine.LiquidFallback)));
    }

    [Fact]
    public void Simple_settings_hide_the_expert_pages_but_never_the_one_open()
    {
        foreach (var id in new[] { "studio", "wallpaper", "tweaks" })
        {
            Assert.False(MainWindow.Listed(id, false, false, "auto", false));
            Assert.True(MainWindow.Listed(id, true, false, "auto", false));
            Assert.True(MainWindow.Listed(id, false, false, "auto", true));
        }
        Assert.True(MainWindow.Listed("home", false, false, "auto", false));
        Assert.True(MainWindow.Listed("widgets", false, false, "auto", false));
        Assert.False(MainWindow.Listed("rainmeter", false, true, "auto", false));
        Assert.True(MainWindow.Listed("rainmeter", true, true, "auto", false));
        Assert.True(MainWindow.Listed("rainmeter", false, false, "always", false));
    }

    [Fact]
    public void The_first_start_flow_stays_within_its_steps()
    {
        Assert.Equal(1, WelcomeFlow.Move(0, true));
        Assert.Equal(0, WelcomeFlow.Move(0, false));
        Assert.Equal(WelcomeFlow.Steps - 1, WelcomeFlow.Move(WelcomeFlow.Steps - 1, true));
    }

    [Fact]
    public void Older_settings_without_the_new_switches_keep_all_settings_and_liquid_glass()
    {
        var s = Json.Read<AppSettings>(WriteTemp("""{"General":{"WelcomeDone":true}}"""))!;
        Assert.Null(s.General.ShowAdvanced);
        Assert.True(s.General.Advanced);
        Assert.True(s.General.LiquidGlass);
        Assert.Equal("", s.ActiveSetId);
    }

    [Fact]
    public void A_new_install_starts_with_simple_settings_and_an_old_one_keeps_all()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-s-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Paths.OverrideRoot(dir);
        try
        {
            var fresh = new Store();
            fresh.Load();
            Assert.True(fresh.IsFirstRun);
            Assert.False(fresh.Settings.General.Advanced);
            fresh.Save();

            File.WriteAllText(Paths.Settings, """{"General":{"WelcomeDone":true}}""");
            var old = new Store();
            old.Load();
            Assert.False(old.IsFirstRun);
            Assert.True(old.Settings.General.Advanced);
            Assert.True(Directory.Exists(Paths.Sets));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static string WriteTemp(string json)
    {
        var f = Path.Combine(Path.GetTempPath(), "gw-j-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(f, json);
        return f;
    }
}
