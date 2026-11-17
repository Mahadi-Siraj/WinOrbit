using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Glasswave.Core;
using Glasswave.Design;
using Glasswave.Rainmeter;
using Glasswave.Services;
using Glasswave.Wallpaper;
using Xunit;

namespace Glasswave.Tests;

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
        Assert.False(Glasswave.WindowsIntegration.QuickControls.NightLightState(Off));
        var on = Glasswave.WindowsIntegration.QuickControls.NightLightToggled(Off, true)!;
        Assert.True(Glasswave.WindowsIntegration.QuickControls.NightLightState(on));
        Assert.Equal(Off.Length + 2, on.Length);
        Assert.Equal(0x15, on[18]);
        Assert.Equal(new byte[] { 0x10, 0x00 }, on[23..25]);
        Assert.NotEqual(Off[10..15], on[10..15]);                       // change counter bumped

        var off = Glasswave.WindowsIntegration.QuickControls.NightLightToggled(on, false)!;
        Assert.False(Glasswave.WindowsIntegration.QuickControls.NightLightState(off));
        Assert.Equal(Off.Length, off.Length);
        Assert.Equal(Off[15..], off[15..]);                              // everything after the counter is back as it was
    }

    [Fact]
    public void Night_light_refuses_unknown_format()
    {
        var bad = (byte[])Off.Clone();
        bad[18] = 0x99;
        Assert.Null(Glasswave.WindowsIntegration.QuickControls.NightLightState(bad));
        Assert.Null(Glasswave.WindowsIntegration.QuickControls.NightLightToggled(bad, true));
        Assert.Null(Glasswave.WindowsIntegration.QuickControls.NightLightToggled(new byte[] { 1, 2, 3 }, true));
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
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Glasswave"));
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
    }
}
