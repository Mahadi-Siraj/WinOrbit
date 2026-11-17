using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using WinOrbit.Core;
using WinOrbit.Rainmeter;
using Xunit;

namespace WinOrbit.Tests;

/// <summary>
/// Integration tests against a real, running Rainmeter. They only run when WINORBIT_RM_LIVE=1 is set
/// (otherwise they return immediately), because they drive whatever Rainmeter is running on the machine.
/// Run them against a throw-away portable copy with stock illustro skins:
///   $env:WINORBIT_RM_LIVE=1; dotnet test --filter LiveRainmeter
/// </summary>
public class LiveRainmeterTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("WINORBIT_RM_LIVE") == "1";

    private static RainmeterService Detected()
    {
        var svc = new RainmeterService(() => new RainmeterPrefs());
        svc.Detect();
        Assert.True(svc.Installed, "Rainmeter must be running for live tests");
        return svc;
    }

    private static Dictionary<string, string> HashTree(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    private static RmSkin WaitFor(RainmeterService svc, string config, Func<RmSkin, bool> cond)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            svc.Scan();
            var s = svc.Skins.Single(k => k.Config == config);
            if (cond(s) || sw.Elapsed > TimeSpan.FromSeconds(10)) return s;
            Thread.Sleep(250);
        }
    }

    [Fact]
    public void LiveRainmeter_DetectsRunningPortableInstall()
    {
        if (!Enabled) return;
        var svc = Detected();
        var running = Process.GetProcessesByName("Rainmeter").Select(p => { using (p) return p.MainModule!.FileName; }).First();
        Assert.Equal(running, svc.ExePath, ignoreCase: true);
        Assert.True(svc.Portable);
        Assert.Equal(Path.Combine(svc.ProgramDir, "Rainmeter.ini"), svc.SettingsPath, ignoreCase: true);
        Assert.Equal(Path.Combine(svc.ProgramDir, "Skins"), svc.SkinsPath, ignoreCase: true);
        Assert.StartsWith("4.", svc.Version);
    }

    [Fact]
    public void LiveRainmeter_ListsStockSkinsWithState()
    {
        if (!Enabled) return;
        var svc = Detected();
        svc.Scan();
        var configs = svc.Skins.Select(s => s.Config).ToList();
        foreach (var c in new[] { @"illustro\Clock", @"illustro\Disk", @"illustro\Google", @"illustro\Network", @"illustro\Recycle Bin", @"illustro\System", @"illustro\Welcome" })
            Assert.Contains(c, configs);
        Assert.DoesNotContain(configs, c => c.Contains('@'));                     // @Vault, @Resources hidden
        var disk = svc.Skins.Single(s => s.Config == @"illustro\Disk");
        Assert.Equal(new[] { "1 Disk.ini", "2 Disks.ini" }, disk.Variants);
        Assert.True(svc.Skins.Single(s => s.Config == @"illustro\Clock").Active);
        Assert.False(string.IsNullOrEmpty(svc.Skins.Single(s => s.Config == @"illustro\Clock").Author));
    }

    [Fact]
    public void LiveRainmeter_ActivateDeactivateRefresh_WithoutTouchingSkinFiles()
    {
        if (!Enabled) return;
        var svc = Detected();
        var before = HashTree(svc.SkinsPath);
        svc.Scan();

        var net = svc.Skins.Single(s => s.Config == @"illustro\Network");
        svc.Activate(net);
        net = WaitFor(svc, net.Config, s => s.Active);
        Assert.True(net.Active, "!ActivateConfig did not take effect");
        Assert.Equal("Network.ini", net.ActiveVariant);

        var disk = svc.Skins.Single(s => s.Config == @"illustro\Disk");
        svc.Activate(disk, "1 Disk.ini");
        disk = WaitFor(svc, disk.Config, s => s.ActiveVariant == "1 Disk.ini");
        Assert.Equal("1 Disk.ini", disk.ActiveVariant);

        svc.Refresh(net);
        svc.RefreshAll();
        Thread.Sleep(1500);
        Assert.True(RainmeterService.IsRunning);

        svc.Deactivate(net);
        net = WaitFor(svc, net.Config, s => !s.Active);
        Assert.False(net.Active, "!DeactivateConfig did not take effect");

        svc.Activate(disk, "2 Disks.ini");
        WaitFor(svc, disk.Config, s => s.ActiveVariant == "2 Disks.ini");

        Assert.Equal(before, HashTree(svc.SkinsPath));
    }
}
