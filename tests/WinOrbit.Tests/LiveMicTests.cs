using Microsoft.Win32;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

/// <summary>Touches the real default microphone, read-only. Skips itself on a PC without one.</summary>
public class LiveMicTests
{
    private const string NonPackaged = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged";

    [Fact]
    public void Reading_the_microphone_meter_never_counts_as_using_the_microphone()
    {
        var meter = new MicMeter();
        meter.Attach();
        if (!meter.Available) return;   // no microphone on this PC
        var muted = meter.Muted;
        Assert.NotNull(muted);
        for (var i = 0; i < 20; i++) { Assert.InRange(meter.Peak(), 0, 1); Thread.Sleep(10); }

        // Windows records every program that opens the microphone here; reading the meter must not add this one.
        using var key = Registry.CurrentUser.OpenSubKey(NonPackaged);
        var self = Environment.ProcessPath!.Replace('\\', '#');
        var names = key?.GetSubKeyNames() ?? Array.Empty<string>();
        Assert.DoesNotContain(names, n => n.Equals(self, StringComparison.OrdinalIgnoreCase)
            && Registry.CurrentUser.OpenSubKey(NonPackaged + "\\" + n)?.GetValue("LastUsedTimeStop") is long stop && stop == 0);
        Assert.DoesNotContain(PrivacyIndicator.InUse("microphone"), a => a.Contains("testhost", StringComparison.OrdinalIgnoreCase));
    }
}
