using WinOrbit.Core;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

/// <summary>One real request to LRCLIB. Opt-in (WINORBIT_NET_LIVE=1) because it uses the internet.</summary>
public class LiveLyricsTests
{
    [Fact]
    public async Task LiveLyrics_finds_synced_lines_for_a_known_song()
    {
        if (Environment.GetEnvironmentVariable("WINORBIT_NET_LIVE") != "1") return;
        var privacy = new PrivacySettings { NetworkAllowed = true, LyricsEnabled = true };
        var net = new NetworkGate(() => privacy);
        var lyrics = new LyricsService(net, () => privacy);
        var lines = await lyrics.GetAsync("Never Gonna Give You Up", "Rick Astley", "Whenever You Need Somebody", TimeSpan.FromSeconds(213));
        Assert.NotNull(lines);
        Assert.True(lines!.Count > 20);
        Assert.Contains(lines, l => l.Text.Contains("never gonna", StringComparison.OrdinalIgnoreCase));
        Assert.All(net.Ledger, e => Assert.Equal("lrclib.net", e.Host));
        // Cached: a second ask makes no request.
        var before = net.RequestCount;
        await lyrics.GetAsync("Never Gonna Give You Up", "Rick Astley", "Whenever You Need Somebody", TimeSpan.FromSeconds(213));
        Assert.Equal(before, net.RequestCount);
    }
}
