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
