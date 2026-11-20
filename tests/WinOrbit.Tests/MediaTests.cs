using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class MediaTests
{
    [Fact]
    public void A_timeline_or_play_pause_update_keeps_a_cover_that_is_still_loading()
    {
        var fetch = MediaService.NextDetailsVersion(0, writesDetails: true);   // title and cover start loading
        var now = MediaService.NextDetailsVersion(fetch, writesDetails: false); // the player reports "playing" meanwhile
        now = MediaService.NextDetailsVersion(now, writesDetails: false);       // and a timeline tick
        Assert.Equal(fetch, now);                                              // the fetch still applies
    }

    [Fact]
    public void Newer_details_replace_a_fetch_still_in_flight()
    {
        var oldApp = MediaService.NextDetailsVersion(0, writesDetails: true);     // the paused browser's details
        var newApp = MediaService.NextDetailsVersion(oldApp, writesDetails: true); // a player starts: its details
        Assert.NotEqual(oldApp, newApp);                                         // the browser's late answer is dropped
    }
}
