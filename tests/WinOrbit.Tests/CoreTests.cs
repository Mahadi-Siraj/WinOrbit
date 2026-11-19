using System.IO;
using System.Text.Json;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Rainmeter;
using WinOrbit.Services;
using WinOrbit.Wallpaper;
using Xunit;

namespace WinOrbit.Tests;

public class PaletteTests
{
    private static byte[] Fill(int w, int h, Func<int, int, Color> f)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = f(x, y); var i = (y * w + x) * 4;
            px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255;
        }
        return px;
    }

    [Fact]
    public void Monochrome_wallpaper_with_small_colourful_icons_is_neutral()
    {
        // Black/white/grey scene plus a column of small saturated "icons" (~3% of the area), like a real desktop.
        var icons = new[] { Colors.Red, Colors.LimeGreen, Colors.DodgerBlue, Colors.Orange, Colors.Magenta, Color.FromRgb(0x10, 0x7C, 0x41) };
        var px = Fill(480, 270, (x, y) =>
        {
            if (x < 24 && y % 30 < 18) return icons[(y / 30) % icons.Length];
            return y > 180 ? Color.FromRgb(240, 240, 240) : Color.FromRgb((byte)(10 + x / 40), (byte)(10 + x / 40), (byte)(12 + x / 40));
        });
        var p = Palette.FromPixels(px, 480, 270);
        Assert.True(p.IsNeutral);
        Assert.True(ColorMath.ToHsl(p.Accent).S < 0.45, "accent should stay muted on a monochrome wallpaper");
        Assert.All(p.ToGradientStops(), s => Assert.True(ColorMath.ToHsl(ColorMath.Parse(s.Color)).S < 0.2));
    }

    [Fact]
    public void Blue_wallpaper_gives_blue_accent()
    {
        var px = Fill(320, 180, (x, y) => ColorMath.FromHsl(215, 0.7, 0.25 + 0.4 * y / 180.0));
        var p = Palette.FromPixels(px, 320, 180);
        Assert.False(p.IsNeutral);
        var (h, s, _) = ColorMath.ToHsl(p.Accent);
        Assert.InRange(h, 195, 235);
        Assert.True(s > 0.45);
    }

    [Fact]
    public void Two_colour_wallpaper_gives_distinct_secondary()
    {
        var px = Fill(320, 180, (x, _) => x < 160 ? ColorMath.FromHsl(340, 0.75, 0.5) : ColorMath.FromHsl(40, 0.8, 0.55));
        var p = Palette.FromPixels(px, 320, 180);
        Assert.True(ColorMath.HueDistance(ColorMath.ToHsl(p.Accent).H, ColorMath.ToHsl(p.Secondary).H) > 25);
    }

    [Fact]
    public void Colour_spread_over_many_hues_on_dark_background_is_not_neutral()
    {
        // Like Windows' "flow" wallpapers: a near-black indigo background (~80 %) with a ribbon whose colour
        // sweeps from magenta through red and orange to yellow. No single swatch covers 1 %, but the image is
        // clearly colourful, and the accent should come from the ribbon, not the dark background.
        var px = Fill(480, 270, (x, y) =>
        {
            if (x is >= 120 and < 360 && y is >= 60 and < 150)
                return ColorMath.FromHsl(300 + (x - 120) * 0.5, 0.9, 0.45 + 0.25 * (y - 60) / 90.0);
            return Color.FromRgb(0x13, 0x08, 0x26);
        });
        var p = Palette.FromPixels(px, 480, 270);
        Assert.False(p.IsNeutral);
        Assert.True(ColorMath.HueDistance(ColorMath.ToHsl(p.Accent).H, 0) <= 65, $"accent {ColorMath.Hex(p.Accent)} should come from the ribbon");
    }

    [Fact]
    public void Empty_input_falls_back_to_default() => Assert.Same(Palette.Default, Palette.FromPixels(Array.Empty<byte>(), 0, 0));
}

public class ColorMathTests
{
    [Theory]
    [InlineData("#FF8800", 255, 136, 0)]
    [InlineData("0af", 0, 170, 255)]
    [InlineData("#80102030", 16, 32, 48)]
    public void Parses_hex(string hex, byte r, byte g, byte b)
    {
        var c = ColorMath.Parse(hex);
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Fact]
    public void Invalid_hex_returns_fallback() => Assert.Equal(Colors.Teal, ColorMath.Parse("not a colour", Colors.Teal));

    [Fact]
    public void Hsl_round_trips()
    {
        foreach (var c in new[] { Colors.Coral, Colors.SteelBlue, Colors.Gold, Color.FromRgb(12, 200, 99) })
        {
            var (h, s, l) = ColorMath.ToHsl(c);
            var back = ColorMath.FromHsl(h, s, l);
            Assert.InRange(Math.Abs(back.R - c.R) + Math.Abs(back.G - c.G) + Math.Abs(back.B - c.B), 0, 3);
        }
    }

    [Fact]
    public void Contrast_black_white_is_21() => Assert.InRange(ColorMath.Contrast(Colors.Black, Colors.White), 20.9, 21.1);

    [Fact]
    public void ForMode_keeps_text_legible()
    {
        var accent = ColorMath.Parse("#202860");
        Assert.True(ColorMath.Contrast(ColorMath.ForMode(accent, true), Color.FromRgb(0x12, 0x15, 0x1C)) > 4.5);
        Assert.True(ColorMath.Contrast(ColorMath.ForMode(Colors.Yellow, false), Colors.White) > 2.5);
    }
}

public class WallpaperComposeTests
{
    [Fact]
    public void Box_blur_preserves_flat_colour()
    {
        var px = new byte[64 * 64 * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = 50; px[i + 1] = 100; px[i + 2] = 150; px[i + 3] = 255; }
        WallpaperEngine.BoxBlur(px, 64, 64, 6);
        Assert.Equal(50, px[2000]); Assert.Equal(100, px[2001]); Assert.Equal(150, px[2002]);
    }

    [Fact]
    public void Live_capture_maps_one_to_one_onto_its_monitor()
    {
        // A 200x100 capture whose left half is red and right half blue, covering a 200x100 monitor at the origin.
        var src = new byte[200 * 100 * 4];
        for (var y = 0; y < 100; y++) for (var x = 0; x < 200; x++) { var i = (y * 200 + x) * 4; if (x < 100) src[i + 2] = 255; else src[i] = 255; src[i + 3] = 255; }
        var frame = new WallpaperFrame { Path = "live:", Pixels = src, Width = 200, Height = 100, SourceWidth = 200, SourceHeight = 100 };
        var mon = new MonitorInfo(IntPtr.Zero, "D1", new Interop.Native.RECT { Right = 200, Bottom = 100 }, new Interop.Native.RECT { Right = 200, Bottom = 100 }, true, 1);
        var outPx = WallpaperEngine.Compose(frame, mon, mon.Bounds, WallpaperFit.Fill, Colors.Black, 50, 25);
        Assert.Equal(255, outPx[(10 * 50 + 5) * 4 + 2]);    // left side is red
        Assert.Equal(255, outPx[(10 * 50 + 45) * 4]);       // right side is blue
    }

    [Fact]
    public void Blank_capture_is_detected()
    {
        Assert.True(LiveWallpaper.IsBlank(new byte[4000]));
        var noisy = new byte[4000];
        for (var i = 0; i < noisy.Length; i++) noisy[i] = (byte)(i * 37 % 251);
        Assert.False(LiveWallpaper.IsBlank(noisy));
    }
}

public class RainmeterTests
{
    [Fact]
    public void Ini_parser_tolerates_comments_bom_and_duplicates()
    {
        var ini = Ini.Parse("\uFEFF; comment\n[Rainmeter]\nSkinPath=C:\\Skins\\\n[illustro\\Clock]\nActive=1\nActive=2\nWindowX=100\n");
        Assert.Equal("C:\\Skins\\", ini.Get("Rainmeter", "SkinPath"));
        Assert.Equal(1, ini.GetInt("illustro\\Clock", "Active", 0));       // first value wins, like Rainmeter
        Assert.Equal(100, ini.TryInt("illustro\\Clock", "WindowX"));
        Assert.Null(ini.TryInt("illustro\\Clock", "WindowY"));
    }

    [Fact]
    public void Scans_skins_and_reads_metadata_without_writing()
    {
        var root = Path.Combine(Path.GetTempPath(), "gw-test-" + Guid.NewGuid().ToString("N"));
        var skins = Path.Combine(root, "Skins");
        Directory.CreateDirectory(Path.Combine(skins, "Suite", "Clock"));
        Directory.CreateDirectory(Path.Combine(skins, "Suite", "@Resources"));
        File.WriteAllText(Path.Combine(skins, "Suite", "Clock", "Clock.ini"), "[Metadata]\nName=Big Clock\nAuthor=Ann\nInformation=Line one|Line two\n[MeasureX]\nMeasure=Plugin\nPlugin=Foo\n");
        File.WriteAllText(Path.Combine(skins, "Suite", "Clock", "Clock2.ini"), "[Metadata]\nName=Alt\n");
        var settings = Path.Combine(root, "Rainmeter.ini");
        File.WriteAllText(settings, $"[Rainmeter]\nSkinPath={skins}\n[Suite\\Clock]\nActive=2\nWindowX=40\nAlphaValue=200\n");
        var before = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);

        var svc = new RainmeterService(() => new RainmeterPrefs());
        typeof(RainmeterService).GetProperty(nameof(RainmeterService.SkinsPath))!.SetValue(svc, skins);
        typeof(RainmeterService).GetProperty(nameof(RainmeterService.SettingsPath))!.SetValue(svc, settings);
        svc.Scan();

        var s = Assert.Single(svc.Skins);
        Assert.Equal("Suite\\Clock", s.Config);
        Assert.True(s.Active);
        Assert.Equal("Clock2.ini", s.ActiveVariant);
        Assert.Equal(40, s.X);
        Assert.Equal(200, s.Alpha);
        Assert.Equal("Alt", s.Name);                       // metadata comes from the active variant
        var after = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);
        Assert.Equal(before, after);                       // nothing was modified or created
        Directory.Delete(root, true);
    }

    [Fact]
    public void Plugin_usage_is_flagged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-p-" + Guid.NewGuid().ToString("N"));
        var skins = Path.Combine(dir, "Skins", "S");
        Directory.CreateDirectory(skins);
        File.WriteAllText(Path.Combine(skins, "S.ini"),
            "[A]\nMeasure=Plugin\nPlugin=WebParser\n" +                       // bundled with Rainmeter
            "[B]\nMeasure=Plugin\nPlugin=Plugins\\InputText.dll\n" +          // bundled, legacy path syntax
            "[C]\nMeasure=Plugin\nPlugin=SpotifyPlugin\n" +                   // third-party
            "[D]\nMeasure=Plugin\nPlugin=RunCommand\nProgram=powershell\n" +  // runs a command
            "[E]\nMeter=String\nLeftMouseUpAction=[\"https://example.com\"][\"C:\\\"]\n" +   // link + folder: harmless
            "[F]\nMeter=String\nLeftMouseUpAction=[!Execute [\"#@#tool.bat\"]]\n");
        var svc = new RainmeterService(() => new RainmeterPrefs());
        typeof(RainmeterService).GetProperty(nameof(RainmeterService.SkinsPath))!.SetValue(svc, Path.Combine(dir, "Skins"));
        typeof(RainmeterService).GetProperty(nameof(RainmeterService.SettingsPath))!.SetValue(svc, Path.Combine(dir, "none.ini"));
        svc.Scan();
        var s = svc.Skins[0];
        Assert.Equal(new[] { "SpotifyPlugin" }, s.ThirdPartyPlugins);
        Assert.True(s.UsesPlugins);
        Assert.Equal(new[] { "powershell", "#@#tool.bat" }, s.Launches);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Links_and_folders_are_not_reported_as_programs()
    {
        Assert.Empty(RainmeterService.LaunchTargets("[\"http://rainmeter.net/discover\"]"));
        Assert.Empty(RainmeterService.LaunchTargets("[\"#disk1#\\\"]"));
        Assert.Empty(RainmeterService.LaunchTargets("[!Refresh][!ToggleConfig \"illustro\\Clock\" \"Clock.ini\"]"));
        Assert.Equal(new[] { "taskmgr.exe" }, RainmeterService.LaunchTargets("[\"taskmgr.exe\"]"));
    }

    [Fact]
    public void Rainmeter_page_appears_only_when_useful()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-rmexe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fakeExe = Path.Combine(dir, "Rainmeter.exe");
        try
        {
            var prefs = new RainmeterPrefs { CustomExePath = Path.Combine(dir, "missing", "Rainmeter.exe") };
            var svc = new RainmeterService(() => prefs);
            if (!RainmeterService.IsRunning && !File.Exists(@"C:\Program Files\Rainmeter\Rainmeter.exe"))
                Assert.False(svc.PageWanted());                               // auto + not installed → hidden

            File.WriteAllText(fakeExe, "");
            prefs.CustomExePath = fakeExe;
            Assert.True(new RainmeterService(() => prefs).PageWanted());       // auto + installed → shown

            prefs.PageVisibility = "never";
            Assert.False(new RainmeterService(() => prefs).PageWanted());
            prefs.PageVisibility = "always";
            prefs.CustomExePath = "";
            Assert.True(new RainmeterService(() => prefs).PageWanted());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Rejects_non_rainmeter_zip()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-" + Guid.NewGuid().ToString("N") + ".rmskin");
        using (var z = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hi");
        var info = RainmeterService.Inspect(path);
        Assert.False(info.Valid);
        Assert.NotNull(info.Error);
        File.Delete(path);
    }

    [Fact]
    public void Inspects_package_contents()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-" + Guid.NewGuid().ToString("N") + ".rmskin");
        using (var z = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("RMSKIN.ini").Open())) w.Write("[rmskin]\nName=Pack\nAuthor=Bo\nVersion=2.0\n");
            using (var w = new StreamWriter(z.CreateEntry("Skins/Pack/Main/Main.ini").Open())) w.Write("[Rainmeter]\n");
            using (var w = new StreamWriter(z.CreateEntry("Plugins/64bit/Thing.dll").Open())) w.Write("x");
        }
        var info = RainmeterService.Inspect(path);
        Assert.True(info.Valid);
        Assert.Equal("Pack", info.Name);
        Assert.Contains("Pack", info.Skins);
        Assert.Contains("Thing.dll", info.Plugins);
        File.Delete(path);
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_round_trip_through_json()
    {
        var s = new AppSettings();
        s.Widgets.Add(new WidgetInstance { Kind = "notes", X = 12, Y = 34, Options = { ["title"] = "IDEAS" } });
        s.Island.Modules.Remove("notes");
        s.Automation.TimeRules.Add(new TimeRule { Time = "21:30", ProfileId = "night" });
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Json.Options), Json.Options)!;
        Assert.Equal("IDEAS", back.Widgets[0].Opt("title"));
        Assert.DoesNotContain("notes", back.Island.Modules);
        Assert.Equal("21:30", back.Automation.TimeRules[0].Time);
        Assert.False(back.Privacy.NetworkAllowed);           // private by default
        Assert.False(back.Automation.Enabled);               // automation opt-in
    }

    [Fact]
    public void Atomic_write_and_corrupt_file_recovery()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-s-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Paths.OverrideRoot(dir);
        var file = Path.Combine(dir, "x.json");
        Json.Write(file, new AppSettings { ActiveThemeId = "midnight" });
        Assert.Equal("midnight", Json.Read<AppSettings>(file)!.ActiveThemeId);
        File.WriteAllText(file, "{ not json");
        Assert.Null(Json.Read<AppSettings>(file));
        Assert.True(File.Exists(file + ".corrupt"));           // bad file preserved, not silently lost
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Built_in_presets_are_complete_and_unique()
    {
        Assert.Equal(Presets.All.Count, Presets.All.Select(t => t.Id).Distinct().Count());
        Assert.True(Presets.All.Count >= 10);
        Assert.All(Presets.All, t => { Assert.True(t.BuiltIn); Assert.NotNull(t.Dark); Assert.NotNull(t.Light); });
        Assert.All(ProfileService.BuiltIn, p => Assert.Contains(Presets.All, t => t.Id == p.ThemeId));
    }

    [Fact]
    public void Scheduled_mode_switches_at_the_right_times()
    {
        var t = new Theme { Mode = ThemeMode.Scheduled, LightAt = "07:00", DarkAt = "19:00" };
        Assert.True(ThemeEngine.ScheduledDark(t, new DateTime(2026, 1, 1, 6, 59, 0), out var n1));
        Assert.Equal(new DateTime(2026, 1, 1, 7, 0, 0), n1);
        Assert.False(ThemeEngine.ScheduledDark(t, new DateTime(2026, 1, 1, 12, 0, 0), out var n2));
        Assert.Equal(new DateTime(2026, 1, 1, 19, 0, 0), n2);
        Assert.True(ThemeEngine.ScheduledDark(t, new DateTime(2026, 1, 1, 23, 0, 0), out _));
    }

    [Fact]
    public void Time_rules_pick_the_most_recent_rule()
    {
        var rules = new[] { new TimeRule { Time = "08:00", ProfileId = "productivity" }, new TimeRule { Time = "22:00", ProfileId = "night" } };
        Assert.Equal("night", ProfileService.CurrentTimeRule(rules, new DateTime(2026, 1, 2, 1, 0, 0))!.ProfileId);
        Assert.Equal("productivity", ProfileService.CurrentTimeRule(rules, new DateTime(2026, 1, 2, 9, 0, 0))!.ProfileId);
        Assert.Equal(new DateTime(2026, 1, 2, 22, 0, 0), ProfileService.NextOccurrence("22:00", new DateTime(2026, 1, 2, 9, 0, 0)));
    }

    [Theory]
    [InlineData("Ctrl+Alt+I", true)]
    [InlineData("Win+Shift+9", true)]
    [InlineData("Space", false)]          // a modifier is required for a global hotkey
    [InlineData("Ctrl+Nope", false)]
    public void Hotkey_parsing(string gesture, bool ok) => Assert.Equal(ok, ShellHost.TryParse(gesture, out _, out _));
}

public class QuickControlTests
{
    // Night-light state blob as Windows 11 (build 26300) stores it with night light off.
    private static readonly byte[] Off = Convert.FromHexString("434201000A0201002A06D4D5ABCD062A2B0E1343420100D00A02C61482B0F69CCDCAF8ED0100000000");

    [Fact]
    public void Night_light_blob_toggles_on_and_back_off()
    {
        Assert.False(WinOrbit.WindowsIntegration.QuickControls.NightLightState(Off));
        var on = WinOrbit.WindowsIntegration.QuickControls.NightLightToggled(Off, true)!;
        Assert.True(WinOrbit.WindowsIntegration.QuickControls.NightLightState(on));
        Assert.Equal(Off.Length + 2, on.Length);
        Assert.Equal(0x15, on[18]);
        Assert.Equal(new byte[] { 0x10, 0x00 }, on[23..25]);
        Assert.NotEqual(Off[10..15], on[10..15]);                       // change counter bumped

        var off = WinOrbit.WindowsIntegration.QuickControls.NightLightToggled(on, false)!;
        Assert.False(WinOrbit.WindowsIntegration.QuickControls.NightLightState(off));
        Assert.Equal(Off.Length, off.Length);
        Assert.Equal(Off[15..], off[15..]);                              // everything after the counter is back as it was
    }

    [Fact]
    public void Night_light_refuses_unknown_format()
    {
        var bad = (byte[])Off.Clone();
        bad[18] = 0x99;
        Assert.Null(WinOrbit.WindowsIntegration.QuickControls.NightLightState(bad));
        Assert.Null(WinOrbit.WindowsIntegration.QuickControls.NightLightToggled(bad, true));
        Assert.Null(WinOrbit.WindowsIntegration.QuickControls.NightLightToggled(new byte[] { 1, 2, 3 }, true));
    }
}

public class LiveActivityTests
{
    [Fact]
    public void Lrc_parses_multi_stamps_and_skips_metadata()
    {
        var lines = LyricsService.Parse("[ar:Someone]\n[00:12.50]First line\n[00:05.00][01:00.25]Chorus\n[00:20.00]\n")!;
        Assert.Equal(new[] { "Chorus", "First line", "", "Chorus" }, lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromSeconds(5), lines[0].At);
        Assert.Equal(TimeSpan.FromMilliseconds(60250), lines[3].At);
        Assert.Null(LyricsService.Parse("plain text, no timestamps"));
    }

    [Fact]
    public void Enhanced_lrc_word_stamps_are_removed()
    {
        var lines = LyricsService.Parse("[00:18.89] <00:18.89>We're <00:19.25>no <00:19.53>strangers <00:20.07>to <00:20.33>love")!;
        Assert.Equal("We're no strangers to love", lines.Single().Text);
    }

    [Fact]
    public void Lyric_index_follows_position()
    {
        var lines = LyricsService.Parse("[00:01.00]a\n[00:03.00]b\n[00:05.00]c")!;
        Assert.Equal(-1, LyricsService.IndexAt(lines, TimeSpan.FromSeconds(0.5)));
        Assert.Equal(0, LyricsService.IndexAt(lines, TimeSpan.FromSeconds(1)));
        Assert.Equal(1, LyricsService.IndexAt(lines, TimeSpan.FromSeconds(4.9)));
        Assert.Equal(2, LyricsService.IndexAt(lines, TimeSpan.FromMinutes(9)));
    }

    [Theory]
    [InlineData("Blinding Lights (Official Video)", "Blinding Lights")]
    [InlineData("Blinding Lights [Official Music Video] (4K)", "Blinding Lights")]
    [InlineData("Shape of You (Lyrics)", "Shape of You")]
    [InlineData("Bohemian Rhapsody - Remastered 2011", "Bohemian Rhapsody")]
    [InlineData("Heroes - 2017 Remaster", "Heroes")]
    [InlineData("Stay (feat. Justin Bieber)", "Stay")]
    [InlineData("Stay ft. Justin Bieber", "Stay")]
    [InlineData("Song Name | Official Lyric Video", "Song Name")]
    [InlineData("Hurt (Live)", "Hurt (Live)")]
    [InlineData("Titanium (David Guetta Remix)", "Titanium (David Guetta Remix)")]
    [InlineData("Left Behind", "Left Behind")]
    [InlineData("(Official Video)", "(Official Video)")]
    public void Lyrics_titles_lose_video_page_noise(string raw, string clean) =>
        Assert.Equal(clean, LyricsService.CleanTitle(raw));

    [Fact]
    public void Lyrics_queries_split_artist_and_song_from_video_titles()
    {
        // A VEVO / Topic channel: the artist comes from the channel, the song from the title.
        Assert.Equal(("Blinding Lights", "TheWeeknd"), LyricsService.Queries("Blinding Lights (Official Video)", "TheWeekndVEVO")[0]);
        Assert.Equal(("Shape of You", "Ed Sheeran"), LyricsService.Queries("Shape of You", "Ed Sheeran - Topic")[0]);

        // "Artist - Song" on a channel that isn't the artist: the split goes first.
        var q2 = LyricsService.Queries("Daft Punk - Get Lucky (Official Audio) ft. Pharrell Williams", "Some Music Channel");
        Assert.Equal(("Get Lucky", "Daft Punk"), q2[0]);

        // A music app with a dash inside the real title: the name as given goes first.
        var q3 = LyricsService.Queries("Lose Yourself - From 8 Mile", "Eminem");
        Assert.Equal(("Lose Yourself - From 8 Mile", "Eminem"), q3[0]);
        Assert.True(q3.Count <= 3);

        // No artist at all, but the title carries one.
        Assert.Equal(("Halo", "Beyoncé"), LyricsService.Queries("Beyoncé - Halo", "")[0]);
        Assert.Empty(LyricsService.Queries("Just a title", ""));
    }

    [Fact]
    public void Lyrics_search_picks_the_closest_length()
    {
        Assert.Equal(1, LyricsService.Pick(new[] { 250.0, 201.0, 199.5 }, 200.8));
        Assert.Equal(-1, LyricsService.Pick(new[] { 250.0, 230.0 }, 200));       // nothing within 4 s
        Assert.Equal(1, LyricsService.Pick(new[] { 250.0, 0 }, 200));            // unknown length as a last resort
        Assert.Equal(0, LyricsService.Pick(new[] { 250.0, 200.0 }, 0));          // track length unknown: server order
        Assert.Equal(-1, LyricsService.Pick(Array.Empty<double>(), 200));
    }

    [Fact]
    public async Task Lyrics_need_both_switches_and_never_touch_the_network_when_off()
    {
        var privacy = new PrivacySettings { NetworkAllowed = true, LyricsEnabled = false };
        var net = new NetworkGate(() => privacy);
        var lyrics = new LyricsService(net, () => privacy);
        Assert.False(lyrics.Enabled);
        Assert.Null(await lyrics.GetAsync("Song", "Artist", "", TimeSpan.FromMinutes(3)));
        Assert.Equal(0, net.RequestCount);
        privacy.LyricsEnabled = true; privacy.NetworkAllowed = false;
        Assert.False(lyrics.Enabled);
    }

    [Fact]
    public void Links_are_detected_conservatively()
    {
        Assert.True(ClipboardHistory.IsLink("https://example.com/a?b=c"));
        Assert.False(ClipboardHistory.IsLink("see https://example.com"));
        Assert.False(ClipboardHistory.IsLink("file:///C:/x.txt"));
    }

    [Fact]
    public void Privacy_indicator_reads_without_throwing()
    {
        Assert.NotNull(PrivacyIndicator.InUse("microphone"));
        Assert.NotNull(PrivacyIndicator.InUse("webcam"));
        Assert.Empty(PrivacyIndicator.InUse("no-such-capability"));
    }
}

public class WindowPlayerTests
{
    [Theory]
    [InlineData("Joznez, 2WEI & Kataem - Ready for War - VLC media player", " - VLC media player", "VLC", "Ready for War", "Joznez, 2WEI & Kataem")]
    [InlineData("holiday video.mp4 - VLC media player", " - VLC media player", "VLC", "holiday video", "")]
    [InlineData("Daft Punk - One More Time [foobar2000]", " [foobar2000]", "foobar2000", "One More Time", "Daft Punk")]
    [InlineData("12. Queen - Bohemian Rhapsody - Winamp", " - Winamp", "Winamp", "Bohemian Rhapsody", "Queen")]
    [InlineData("Interstellar.2014.1080p.mkv", null, "MPC-HC", "Interstellar.2014.1080p", "")]
    public void Player_titles_parse(string window, string? suffix, string app, string title, string artist)
    {
        var r = WinOrbit.Services.WindowPlayers.ParseTitle(window, suffix, app);
        Assert.NotNull(r);
        Assert.Equal(title, r!.Value.Title);
        Assert.Equal(artist, r.Value.Artist);
    }

    [Theory]
    [InlineData("VLC media player", " - VLC media player", "VLC")]
    [InlineData("foobar2000", " [foobar2000]", "foobar2000")]
    [InlineData("", null, "MPC-HC")]
    public void Idle_player_windows_are_not_tracks(string window, string? suffix, string app)
        => Assert.Null(WinOrbit.Services.WindowPlayers.ParseTitle(window, suffix, app));

    [Theory]
    [InlineData("y2mate.com - VØJ Narvent  Memory Reboot", "Memory Reboot", true)]
    [InlineData("Memory Reboot", "Memory Reboot", true)]
    [InlineData("y2mate.com - Wuys  GOAT Ultra slowed  Reverb", "Memory Reboot", false)]
    [InlineData("ab", "ab", false)]
    public void Vlc_recent_file_matches_the_shown_title(string file, string title, bool expected)
        => Assert.Equal(expected, WinOrbit.Services.WindowPlayers.NameMatches(file, title));

    [Fact]
    public void Classic_player_link_messages_round_trip()
    {
        var t = new WinOrbit.Services.ClassicTrack("Wedding Nasheed", "Artist, feat. X", "Album", @"C:\Music\a b.mp3", true, TimeSpan.FromSeconds(12.5), TimeSpan.FromSeconds(201));
        Assert.Equal(t, WinOrbit.Services.ClassicMediaPlayer.Decode(WinOrbit.Services.ClassicMediaPlayer.Encode(t)));
        var noFile = t with { File = null, Title = "Line\nbreak" };
        var back = WinOrbit.Services.ClassicMediaPlayer.Decode(WinOrbit.Services.ClassicMediaPlayer.Encode(noFile));
        Assert.Null(back!.File);
        Assert.Equal("Line break", back.Title);
        Assert.Null(WinOrbit.Services.ClassicMediaPlayer.Decode(WinOrbit.Services.ClassicMediaPlayer.Encode(null)));
        Assert.Null(WinOrbit.Services.ClassicMediaPlayer.Decode("garbage"));
    }

    [Fact]
    public void Classic_player_ticking_on_is_not_a_change_but_a_seek_or_pause_is()
    {
        var a = new WinOrbit.Services.WindowPlayer(IntPtr.Zero, 1, "Windows Media Player", "Song", "Artist", true)
        { Position = TimeSpan.FromSeconds(10), Duration = TimeSpan.FromSeconds(200), CanSeek = true };
        Assert.True(WinOrbit.Services.WindowPlayers.Same(a, a with { Position = TimeSpan.FromSeconds(11) }));
        Assert.False(WinOrbit.Services.WindowPlayers.Same(a, a with { Position = TimeSpan.FromSeconds(60) }));
        Assert.False(WinOrbit.Services.WindowPlayers.Same(a, a with { Playing = false }));
        Assert.False(WinOrbit.Services.WindowPlayers.Same(a, a with { Title = "Next song" }));
        Assert.False(WinOrbit.Services.WindowPlayers.Same(a, null));
        Assert.True(WinOrbit.Services.WindowPlayers.Same(null, null));
    }

    [Fact]
    public void Low_battery_warnings_fire_once_per_threshold()
    {
        Assert.Equal(20, WinOrbit.Services.PowerEvents.LowBatteryWarning(21, 20));
        Assert.Null(WinOrbit.Services.PowerEvents.LowBatteryWarning(20, 19));
        Assert.Equal(10, WinOrbit.Services.PowerEvents.LowBatteryWarning(11, 10));
        Assert.Null(WinOrbit.Services.PowerEvents.LowBatteryWarning(40, 39));
    }
}

public class LauncherTests
{
    [Theory]
    [InlineData("2+2", 4)]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2^10", 1024)]
    [InlineData("2^3^2", 512)]
    [InlineData("-5+2", -3)]
    [InlineData("50%", 0.5)]
    [InlineData("200*15%", 30)]
    [InlineData("10%3", 1)]
    [InlineData("sqrt(16)+1", 5)]
    [InlineData("1,5*2", 3)]
    [InlineData("12×3÷4", 9)]
    public void Calculator_evaluates(string input, double expected)
    {
        Assert.True(WinOrbit.Launcher.Calculator.TryEvaluate(input, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("spotify")]
    [InlineData("42")]
    [InlineData("2+")]
    [InlineData("1/0")]
    [InlineData("e-mail")]
    public void Calculator_ignores_non_sums(string input) => Assert.False(WinOrbit.Launcher.Calculator.TryEvaluate(input, out _));

    [Theory]
    [InlineData("10 km to mi", "6.213712 mi")]
    [InlineData("10km to mi", "6.213712 mi")]
    [InlineData("212 f to c", "100 °C")]
    [InlineData("0 c in f", "32 °F")]
    [InlineData("1 gb to mb", "1,024 mb")]
    [InlineData("2 hours to min", "120 min")]
    public void Units_convert(string input, string expected)
    {
        Assert.True(WinOrbit.Launcher.Units.TryConvert(input, out var r));
        Assert.Equal(expected, r.Replace(' ', ' ').Replace(" ", " "), ignoreCase: true);
    }

    [Fact]
    public void Units_refuse_mismatched_groups() => Assert.False(WinOrbit.Launcher.Units.TryConvert("10 kg to km", out _));

    [Fact]
    public void Ranking_prefers_exact_then_prefix_then_words_then_initials()
    {
        double M(string q, string t, string k = "") => WinOrbit.Launcher.LauncherIndex.Match(q, t, k);
        Assert.True(M("chrome", "Chrome") > M("chrome", "Chrome Remote Desktop"));
        Assert.True(M("chr", "Chrome") > M("chr", "Google Chrome"));
        Assert.True(M("code", "Visual Studio Code") > 0);
        Assert.True(M("vsc", "Visual Studio Code") >= 560);
        Assert.True(M("bluetooth", "Bluetooth & devices") > M("blue", "Night light", "blue light warm"));
        Assert.True(M("dnd", "Focus", "do not disturb dnd") > 0);
        Assert.Equal(0, M("zzz", "Calculator"));
        Assert.True(M("vscode", "Visual Studio Code") > 0);
        Assert.Equal(0, M("spot", "Need For Speed Most Wanted Black Edition"));
        Assert.Equal(0, M("blue", "Where You Are 1 HOUR VERSION Nasheed Background Vocals"));
    }
}

public class PrivacyTests
{
    [Fact]
    public async Task Network_gate_refuses_when_switched_off()
    {
        var privacy = new PrivacySettings { NetworkAllowed = false };
        var gate = new NetworkGate(() => privacy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.GetStringAsync("https://example.org/", "test"));
        Assert.Contains(gate.Ledger, e => e.Result.StartsWith("BLOCKED"));
    }

    [Fact]
    public async Task Network_gate_refuses_plain_http()
    {
        var gate = new NetworkGate(() => new PrivacySettings { NetworkAllowed = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.GetStringAsync("http://example.org/", "test"));
    }

    [Fact]
    public void Only_the_network_gate_creates_http_clients()
    {
        // Static audit: no other source file may construct an HttpClient, WebClient, socket or WebRequest.
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "WinOrbit"));
        Assert.True(Directory.Exists(src), src);
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("Network.cs"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("new HttpClient") || t.Contains("WebClient") || t.Contains("new Socket(") || t.Contains("WebRequest.Create") || t.Contains("TcpClient")))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Weather_parser_reads_open_meteo_shape()
    {
        var json = """{"current":{"temperature_2m":11.4,"apparent_temperature":9.8,"weather_code":3,"wind_speed_10m":12.0},"daily":{"time":["2026-11-17","2026-11-18"],"weather_code":[3,61],"temperature_2m_max":[12,10],"temperature_2m_min":[5,4]}}""";
        using var doc = JsonDocument.Parse(json);
        var r = WeatherService.Parse(doc.RootElement);
        Assert.Equal(11.4, r.Temperature);
        Assert.Equal(2, r.Days.Count);
        Assert.Equal("Rain", WeatherService.Describe(r.Days[1].Code).Text);
        Assert.True(r.IsDay);          // fields the old shape lacks fall back quietly
        Assert.Empty(r.Hours);
    }

    [Fact]
    public void Weather_parser_reads_hours_sun_times_and_night()
    {
        var json = """
        {"current":{"temperature_2m":28.1,"apparent_temperature":33,"weather_code":0,"wind_speed_10m":4,"is_day":0,"relative_humidity_2m":79},
         "hourly":{"time":["2026-11-18T18:00","2026-11-18T19:00"],"temperature_2m":[28.4,null],"weather_code":[0,1],"is_day":[0,0],"precipitation_probability":[0,45]},
         "daily":{"time":["2026-11-18"],"weather_code":[1],"temperature_2m_max":[34],"temperature_2m_min":[27],"sunrise":["2026-11-18T06:36"],"sunset":["2026-11-18T17:38"],"precipitation_probability_max":[5]}}
        """;
        using var doc = JsonDocument.Parse(json);
        var r = WeatherService.Parse(doc.RootElement);
        Assert.False(r.IsDay);
        Assert.Equal(79, r.Humidity);
        Assert.Equal(2, r.Hours.Count);
        Assert.Equal(0, r.Hours[1].Temperature);   // a null hour reads as 0 instead of failing the whole report
        Assert.Equal(45, r.Hours[1].RainChance);
        Assert.Equal(new DateTime(2026, 11, 18, 17, 38, 0), r.Days[0].Sunset);
        Assert.Equal(5, r.Days[0].RainChance);
    }
}
