using WinOrbit.Design;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class MotionAndDeviceTests
{
    private static (double X, double Peak, double Min) Run(double from, double to, double response, double damping, double seconds, double v0 = 0, double fps = 60)
    {
        double x = from, v = v0, peak = from, min = from;
        var omega = 2 * Math.PI / response;
        for (var t = 0.0; t < seconds; t += 1 / fps)
        {
            Springs.Advance(ref x, ref v, to, omega, damping, 1 / fps);
            peak = Math.Max(peak, x); min = Math.Min(min, x);
        }
        return (x, peak, min);
    }

    [Fact]
    public void Spring_settles_on_its_target()
    {
        var (x, _, _) = Run(38, 290, 0.5, 0.7, 2);
        Assert.InRange(x, 289.9, 290.1);
    }

    [Fact]
    public void Underdamped_spring_overshoots_a_little_critically_damped_does_not()
    {
        var bouncy = Run(0, 100, 0.5, 0.7, 2);
        Assert.InRange(bouncy.Peak, 102, 108);          // about 4.6 % for damping 0.7
        var smooth = Run(0, 100, 0.5, 1.0, 2);
        Assert.True(smooth.Peak <= 100.0001);
    }

    [Fact]
    public void Spring_result_does_not_depend_on_frame_rate()
    {
        var at60 = Run(0, 100, 0.4, 0.74, 0.25, fps: 60);
        var at144 = Run(0, 100, 0.4, 0.74, 0.25, fps: 144);
        Assert.InRange(Math.Abs(at60.X - at144.X), 0, 2.5);   // only the sampling instant differs
    }

    [Fact]
    public void Kick_wobbles_and_returns_to_rest()
    {
        var (x, peak, _) = Run(1, 1, 0.36, 0.42, 3, v0: 1.15);
        Assert.InRange(peak, 1.02, 1.12);
        Assert.InRange(x, 0.999, 1.001);
    }

    [Fact]
    public void Network_kind_from_interface_type()
    {
        Assert.Equal(DeviceEvents.NetKind.WiFi, DeviceEvents.KindOf(71));
        Assert.Equal(DeviceEvents.NetKind.Ethernet, DeviceEvents.KindOf(6));
        Assert.Equal(DeviceEvents.NetKind.Other, DeviceEvents.KindOf(243));
    }

    [Theory]
    [InlineData("WH-1000XM4", "audio")]
    [InlineData("Galaxy Buds2 Pro", "audio")]
    [InlineData("MX Master 3S Mouse", "input")]
    [InlineData("Xbox Wireless Controller", "input")]
    [InlineData("Pixel 8", "phone")]
    [InlineData("Unknown thing", "")]
    public void Bluetooth_kind_from_name(string name, string kind) => Assert.Equal(kind, DeviceEvents.BtKind(name, null));

    [Fact]
    public void Bluetooth_class_of_device_wins_over_the_name()
    {
        var props = new Dictionary<string, object> { ["System.Devices.Aep.Bluetooth.Cod.Major"] = (byte)4 };
        Assert.Equal("audio", DeviceEvents.BtKind("Car", props));
    }

    [Fact]
    public void Collapse_spring_never_dips_below_the_capsule()
    {
        // Critically damped: from the open panel back to 38 px, the height must not undershoot (it used to squeeze the clock).
        var (x, _, min) = Run(290, 38, 0.4, 1.0, 2);
        Assert.True(min >= 37.999, $"dipped to {min}");
        Assert.InRange(x, 37.7, 38.3);
    }

    [Fact]
    public void Island_shapes_set_the_right_corners()
    {
        Assert.Equal(new System.Windows.CornerRadius(19), WinOrbit.Island.IslandWindow.ShapeCorners("Capsule", 19));
        var notch = WinOrbit.Island.IslandWindow.ShapeCorners("Notch", 30);
        Assert.Equal(0, notch.TopLeft); Assert.Equal(0, notch.TopRight); Assert.True(notch.BottomLeft > 0);
        var drop = WinOrbit.Island.IslandWindow.ShapeCorners("Drop", 30);
        Assert.True(drop.TopLeft < drop.BottomLeft);
        var square = WinOrbit.Island.IslandWindow.ShapeCorners("Square", 19);
        Assert.True(square.TopLeft is >= 5 and < 19);
        Assert.Equal(WinOrbit.Island.IslandWindow.ShapeCorners("Capsule", 12), WinOrbit.Island.IslandWindow.ShapeCorners("unknown", 12));
    }

    [Theory]
    [InlineData("9:30 Stand-up", 9, 30, "Stand-up")]
    [InlineData("14:30 Dentist", 14, 30, "Dentist")]
    [InlineData("7pm Dinner with Sam", 19, 0, "Dinner with Sam")]
    [InlineData("9.15am Gym", 9, 15, "Gym")]
    [InlineData("12am Midnight call", 0, 0, "Midnight call")]
    [InlineData("12 PM Lunch", 12, 0, "Lunch")]
    public void Calendar_reads_a_time_in_front_of_the_title(string text, int hour, int minute, string title)
    {
        var (time, parsed) = WinOrbit.Island.IslandWindow.ParseEvent(text);
        Assert.Equal(new TimeSpan(hour, minute, 0), time);
        Assert.Equal(title, parsed);
    }

    [Theory]
    [InlineData("Bins out")]
    [InlineData("3 books to return")]   // a bare number isn't a time
    [InlineData("25:00 Nope")]
    [InlineData("13pm Nope")]
    public void Calendar_keeps_text_without_a_time_as_an_all_day_title(string text)
    {
        var (time, title) = WinOrbit.Island.IslandWindow.ParseEvent(text);
        Assert.Null(time);
        Assert.Equal(text, title);
    }

    [Fact]
    public void Calendar_orders_a_day_and_times_its_reminders()
    {
        var day = new DateTime(2026, 11, 18);
        var all = new List<WinOrbit.Core.CalendarEvent>
        {
            new() { Date = day, Time = new TimeSpan(19, 0, 0), Title = "Dinner" },
            new() { Date = day, Title = "Bins out" },
            new() { Date = day, Time = new TimeSpan(9, 30, 0), Title = "Stand-up" },
            new() { Date = day.AddDays(1), Time = new TimeSpan(8, 0, 0), Title = "Tomorrow" },
        };
        Assert.Equal(new[] { "Bins out", "Stand-up", "Dinner" }, WinOrbit.Island.IslandWindow.EventsOn(all, day).Select(e => e.Title));
        Assert.Equal("Dinner", WinOrbit.Island.IslandWindow.NextToday(all, day.AddHours(12))?.Title);
        Assert.Null(WinOrbit.Island.IslandWindow.NextToday(all, day.AddHours(20)));

        var dinner = all[0];
        var due = (Func<DateTime, string?>)(now => WinOrbit.Island.IslandWindow.ReminderDue(dinner, now));
        Assert.Null(due(day.AddHours(18).AddMinutes(30)));               // 30 minutes early: nothing yet
        Assert.Equal("soon", due(day.AddHours(18).AddMinutes(50)));      // 10 minutes before
        Assert.Equal("now", due(day.AddHours(19)));                       // on the minute
        Assert.Equal("now", due(day.AddHours(19).AddMinutes(4)));         // island was busy: still shown
        Assert.Null(due(day.AddHours(19).AddMinutes(6)));                 // too late to be useful
        Assert.Null(WinOrbit.Island.IslandWindow.ReminderDue(all[1], day.AddHours(9)));   // all-day events don't pop up
    }

    [Fact]
    public void Notch_and_drop_face_whichever_edge_the_island_is_on()
    {
        System.Windows.CornerRadius C(string shape, string edge) => WinOrbit.Island.IslandWindow.ShapeCorners(shape, 20, edge);
        var bottom = C("Notch", "Bottom");
        Assert.Equal(0, bottom.BottomLeft); Assert.Equal(0, bottom.BottomRight); Assert.True(bottom.TopLeft > 0 && bottom.TopRight > 0);
        var left = C("Notch", "Left");
        Assert.Equal(0, left.TopLeft); Assert.Equal(0, left.BottomLeft); Assert.True(left.TopRight > 0 && left.BottomRight > 0);
        var right = C("Notch", "Right");
        Assert.Equal(0, right.TopRight); Assert.Equal(0, right.BottomRight); Assert.True(right.TopLeft > 0 && right.BottomLeft > 0);
        var drop = C("Drop", "Bottom");
        Assert.True(drop.BottomLeft < drop.TopLeft);              // tight at the edge, round away from it
        Assert.Equal(C("Capsule", "Top"), C("Capsule", "Left"));  // symmetric shapes don't change
    }

    [Fact]
    public void Island_window_sits_on_its_edge_clear_of_the_taskbar()
    {
        var b = new WinOrbit.Interop.Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var work = new WinOrbit.Interop.Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1032 };   // 48 px taskbar
        (int X, int Y) Spot(string edge, double along, double away, bool flush = false) =>
            WinOrbit.Island.IslandWindow.WindowSpot(edge, (along, away), flush, b, work, 1.0, 560, 420);

        Assert.Equal((680, 10), Spot("Top", 0, 10));
        Assert.Equal((680, 1032 - 420 - 10), Spot("Bottom", 0, 10));    // above the taskbar
        Assert.Equal((680, 1032 - 420), Spot("Bottom", 0, 10, flush: true));
        Assert.Equal((10, 330), Spot("Left", 0, 10));
        Assert.Equal((1920 - 560 - 10, 330 + 100), Spot("Right", 100, 10)); // along a side is up/down
        Assert.Equal(680 + 900, Spot("Top", 5000, 0).X);                   // never pushed off the screen
    }

    [Fact]
    public void Dragging_to_a_screen_edge_picks_that_edge()
    {
        var b = new WinOrbit.Interop.Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        string? Near(double x, double y) => WinOrbit.Island.IslandWindow.EdgeNear(new System.Windows.Point(x, y), b, 70);
        Assert.Equal("Top", Near(960, 20));
        Assert.Equal("Bottom", Near(960, 1070));
        Assert.Equal("Left", Near(15, 500));
        Assert.Equal("Right", Near(1900, 500));
        Assert.Null(Near(960, 500));   // in the middle of the screen: no edge, just moving away from the current one
        // In a corner it has to leave its own edge first, so it can't flicker between two edges.
        double From(string edge, double x, double y) => WinOrbit.Island.IslandWindow.EdgeDistance(new System.Windows.Point(x, y), b, edge);
        Assert.Equal(20, From("Top", 15, 20));
        Assert.Equal(15, From("Left", 15, 20));
        Assert.Equal(10, From("Bottom", 960, 1070));
        Assert.Equal(20, From("Right", 1900, 500));
    }

    [Theory]
    [InlineData(@"C:\Pics\Screenshots\Screenshot 2026-11-18 231500.png", true)]
    [InlineData(@"C:\Pics\Screenshots\shot.JPG", true)]
    [InlineData(@"C:\Pics\Screenshots\clip.jpeg", true)]
    [InlineData(@"C:\Pics\Screenshots\~tmp1234.png", false)]
    [InlineData(@"C:\Pics\Screenshots\.thumb.png", false)]
    [InlineData(@"C:\Pics\Screenshots\desktop.ini", false)]
    [InlineData(@"C:\Pics\Screenshots\recording.mp4", false)]
    public void Screenshot_watcher_only_counts_finished_pictures(string path, bool expected)
        => Assert.Equal(expected, ScreenshotWatcher.IsScreenshot(path));

    [Theory]
    [InlineData(@"C:\Downloads\report.pdf", true)]
    [InlineData(@"C:\Downloads\photo.JPG", true)]
    [InlineData(@"C:\Downloads\archive.zip", true)]
    [InlineData(@"C:\Downloads\setup.exe", false)]
    [InlineData(@"C:\Downloads\Setup.MSI", false)]
    [InlineData(@"C:\Downloads\run.ps1", false)]
    [InlineData(@"C:\Downloads\invoice.pdf.js", false)]
    [InlineData(@"C:\Downloads\link.lnk", false)]
    [InlineData(@"C:\Downloads\disk.iso", false)]
    [InlineData(@"C:\Downloads\noextension", false)]
    public void A_click_never_runs_a_downloaded_program(string path, bool opens)
        => Assert.Equal(opens, WinOrbit.Island.IslandWindow.SafeToOpen(path));

    [Fact]
    public void Volume_mixer_groups_sessions_by_app()
    {
        AppVolume.Session S(string proc, string? name, double vol, bool muted, int state, bool system = false) => new(proc, name, null, vol, muted, state, system);
        var apps = AppVolume.Group(new[]
        {
            S("chrome", "Google Chrome", 0.5, false, 0),   // a quiet tab
            S("chrome", null, 0.8, false, 1),              // a playing tab
            S("", null, 1, false, 0, system: true),
            S("Discord", "Discord", 0.3, true, 0),
            S("spotify", "Spotify", 0.9, false, 2),        // expired: gone
            S("", null, 1, false, 1),                      // no process: dropped
        });
        Assert.Equal(new[] { "chrome", "Discord", "system" }, apps.Select(a => a.Key));   // playing first, System sounds last
        var chrome = apps[0];
        Assert.Equal("Google Chrome", chrome.Name);
        Assert.True(chrome.Active);
        Assert.Equal(0.8, chrome.Volume);
        Assert.True(apps[1].Muted);
        Assert.Equal("System sounds", apps[2].Name);
    }

    [Fact]
    public void Island_hides_for_fullscreen_and_chosen_apps_but_not_kept_ones()
    {
        var s = new WinOrbit.Core.IslandSettings { ShowOverFullscreenApps = { "POWERPNT.EXE" }, HideForApps = { "obs64" } };
        bool Hide(string app, bool full, bool front = true) => WinOrbit.Island.IslandWindow.ShouldHide(s, app, full, front);

        Assert.True(Hide("chrome", full: true));                 // any fullscreen app
        Assert.False(Hide("chrome", full: false));               // a normal window: stays
        Assert.False(Hide("powerpnt", full: true));              // kept over fullscreen (case and .exe don't matter)
        Assert.True(Hide("OBS64", full: false));                 // always hidden for, even windowed
        Assert.False(Hide("obs64", full: false, front: false));  // …but only while it's in front on the island's screen
        s.HideInFullscreen = false;
        Assert.False(Hide("chrome", full: true));                // fullscreen hiding off
        Assert.True(Hide("obs64", full: true));                  // the always-hide list still applies
        Assert.False(Hide("", full: false));
    }

    [Fact]
    public void Weather_skies_differ_by_condition_and_time_of_day()
    {
        var clearDay = WinOrbit.Island.IslandWindow.SkyColors(0, true);
        Assert.NotEqual(clearDay, WinOrbit.Island.IslandWindow.SkyColors(0, false));
        Assert.NotEqual(clearDay, WinOrbit.Island.IslandWindow.SkyColors(61, true));
        Assert.NotEqual(WinOrbit.Island.IslandWindow.SkyColors(61, true), WinOrbit.Island.IslandWindow.SkyColors(95, true));
        Assert.Equal("Clear", WinOrbit.Services.WeatherService.Describe(0, false).Text);
        Assert.Equal(WinOrbit.Design.LineIcons.Moon, WinOrbit.Services.WeatherService.Describe(0, false).Icon);
        Assert.Equal(WinOrbit.Design.LineIcons.Rain, WinOrbit.Services.WeatherService.Describe(61, false).Icon);
    }

    [Fact]
    public void Render_check_tells_a_blank_window_from_a_drawn_one()
    {
        int w = 64, h = 48;
        var flat = new byte[w * h * 4];
        for (var i = 0; i < flat.Length; i += 4) { flat[i] = 0x36; flat[i + 1] = 0x36; flat[i + 2] = 0x36; }
        Assert.True(WinOrbit.Design.RenderGuard.IsFlat(flat, w, h));
        var drawn = (byte[])flat.Clone();
        for (var i = 0; i < drawn.Length; i += 4 * 7) drawn[i] = (byte)(i % 251);
        Assert.False(WinOrbit.Design.RenderGuard.IsFlat(drawn, w, h));
    }

    [Fact]
    public void Registry_watcher_wakes_on_a_write()
    {
        const string path = @"Software\WinOrbit.Tests\Watch";
        using var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
        try
        {
            using var fired = new ManualResetEventSlim();
            using (var watcher = new RegistryWatcher(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path)!, () => fired.Set()))
            {
                root.SetValue("x", 1);
                Assert.True(fired.Wait(TimeSpan.FromSeconds(3)), "no change notification");
            }
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\WinOrbit.Tests", false); }
    }

    [Theory]
    [InlineData("Generic USB Hub", true)]
    [InlineData("USB Root Hub (USB 3.0)", true)]
    [InlineData("USB Composite Device", true)]
    [InlineData("SanDisk Ultra", false)]
    public void Generic_usb_parts_are_not_announced(string name, bool generic) => Assert.Equal(generic, DeviceEvents.IsGeneric(name));
}
