using WinOrbit.WindowsIntegration;
using Windows.Devices.Radios;
using Xunit;

namespace WinOrbit.Tests;

/// <summary>
/// Live tests of the island's quick toggles against this PC's real settings. Opt-in (WINORBIT_QC_LIVE=1)
/// because they change Windows state; each test restores what it found. Only toggles with no visible effect
/// are covered here — Wi-Fi/Bluetooth radios (skip if the radio carries a connection) and power mode.
/// </summary>
public class LiveQuickControlTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("WINORBIT_QC_LIVE") == "1";

    private static async Task<RadioState?> State(RadioKind kind) => (await QuickControls.FindRadio(kind))?.State;

    private static async Task WaitFor(Func<Task<bool>> cond)
    {
        for (var i = 0; i < 40 && !await cond(); i++) await Task.Delay(250);
    }

    [Theory]
    [InlineData(RadioKind.WiFi)]
    [InlineData(RadioKind.Bluetooth)]
    public async Task LiveRadio_turns_off_and_back_on(RadioKind kind)
    {
        if (!Enabled) return;
        var before = await State(kind);
        Assert.NotNull(before);
        var original = before == RadioState.On;

        var r1 = await QuickControls.SetRadio(kind, !original);
        await WaitFor(async () => await State(kind) == (original ? RadioState.Off : RadioState.On));
        Assert.Equal(!original, r1);
        Assert.Equal(original ? RadioState.Off : RadioState.On, await State(kind));

        var r2 = await QuickControls.SetRadio(kind, original);
        await WaitFor(async () => await State(kind) == before);
        Assert.Equal(original, r2);
        Assert.Equal(before, await State(kind));
    }

    /// <summary>Read-only, so it always runs: the real playback devices, through the same COM calls the picker uses.</summary>
    [Fact]
    public void Live_output_devices_are_listed_with_one_default()
    {
        var devices = WinOrbit.Services.AudioDevices.List();
        if (devices.Count == 0) return;   // no sound device (a server, a test runner)
        Assert.All(devices, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        Assert.All(devices, d => Assert.StartsWith("{", d.Id));
        Assert.Equal(1, devices.Count(d => d.IsDefault));
        Assert.True(devices[0].IsDefault);
    }

    [Fact]
    public void LivePowerMode_cycles_and_restores()
    {
        if (!Enabled) return;
        var original = QuickControls.PowerModeIndex();
        Assert.NotNull(original);
        try
        {
            foreach (var i in new[] { 0, 1, 2 })
            {
                Assert.True(QuickControls.SetPowerMode(i));
                Assert.Equal(i, QuickControls.PowerModeIndex());
            }
        }
        finally { QuickControls.SetPowerMode(original!.Value); }
        Assert.Equal(original, QuickControls.PowerModeIndex());
    }
}
