using WinOrbit.Island;
using Xunit;

namespace WinOrbit.Tests;

public class IslandSplitTests
{
    [Fact]
    public void Fully_inside_the_capsule_nothing_moves()
    {
        var (shift, centre, grown) = IslandWindow.SplitPlacement(-38, 38, 240);
        Assert.Equal(0, shift, 6);
        Assert.Equal(0, grown, 6);
        // The bubble's centre sits just inside the capsule's end.
        Assert.Equal(240 / 2.0 - 38 / 2.0, centre, 6);
    }

    [Fact]
    public void At_rest_the_pair_is_centred_with_the_gap_between()
    {
        const double along = 120, pill = 260, gap = IslandWindow.GapAtRest;
        var (shift, centre, grown) = IslandWindow.SplitPlacement(gap, along, pill);
        Assert.Equal(1, grown, 6);
        var pillLeft = shift - pill / 2;
        var pillRight = shift + pill / 2;
        var satLeft = centre - along / 2;
        var satRight = centre + along / 2;
        Assert.Equal(gap, satLeft - pillRight, 6);
        Assert.Equal(0, (pillLeft + satRight) / 2, 6);   // the pair as a whole stays in the middle
    }

    [Fact]
    public void Words_show_only_when_they_fit_beside_the_capsule()
    {
        Assert.True(IslandWindow.SatelliteWidth(90, 38, 300) > 38);
        Assert.Equal(38, IslandWindow.SatelliteWidth(90, 38, 80));    // no room: a round icon bubble
        Assert.Equal(38, IslandWindow.SatelliteWidth(400, 38, 900));  // never a long banner
        Assert.Equal(0, IslandWindow.SatelliteWidth(90, 38, 300) % 2); // even, so its content centres on whole pixels
    }

    [Fact]
    public void Neck_thins_as_they_part_and_is_gone_at_its_reach()
    {
        var touching = IslandWindow.NeckWaist(0, 19);
        var near = IslandWindow.NeckWaist(7, 19);
        var far = IslandWindow.NeckWaist(IslandWindow.NeckReach, 19);
        Assert.True(touching > near && near > 0);
        Assert.Equal(0, far);
        Assert.True(touching < 19);   // always a waist, never wider than the glass
        Assert.Equal(touching, IslandWindow.NeckWaist(-20, 19));   // overlapping: full neck
    }
}
