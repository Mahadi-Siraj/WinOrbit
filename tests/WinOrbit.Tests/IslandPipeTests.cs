using WinOrbit.Island;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class IslandPipeTests
{
    [Fact]
    public void A_card_needs_a_title_and_takes_the_rest_as_optional()
    {
        var card = IslandPipe.Parse("""{"title":"Build finished","text":"288 tests passed","icon":"done","seconds":6,"extra":true}""", "pwsh", out _);
        Assert.NotNull(card);
        Assert.Equal("Build finished", card!.Title);
        Assert.Equal("288 tests passed", card.Text);
        Assert.Equal("done", card.Icon);
        Assert.Equal(6, card.Seconds);
        Assert.Equal("pwsh", card.Sender);

        var plain = IslandPipe.Parse("""{"title":"Hi"}""", "python", out _)!;
        Assert.Equal("", plain.Text);
        Assert.Equal("info", plain.Icon);
        Assert.Equal(4, plain.Seconds);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[1,2]", "expected a JSON object")]
    [InlineData("""{"text":"no title"}""", "\"title\" is required")]
    [InlineData("""{"title":"   "}""", "\"title\" is required")]
    [InlineData("""{"title":42}""", "\"title\" is required")]
    public void Bad_messages_are_refused_with_a_reason(string json, string reason)
    {
        Assert.Null(IslandPipe.Parse(json, "app", out var error));
        Assert.Equal(reason, error);
    }

    [Fact]
    public void Unknown_icons_and_extreme_durations_are_tamed()
    {
        var card = IslandPipe.Parse("""{"title":"x","icon":"https://example.com/a.png","seconds":9999}""", "app", out _)!;
        Assert.Equal("info", card.Icon);
        Assert.Equal(10, card.Seconds);
        Assert.Equal(2, IslandPipe.Parse("""{"title":"x","seconds":-5}""", "app", out _)!.Seconds);
    }

    [Fact]
    public void Text_is_plain_single_line_and_cannot_hide_its_direction()
    {
        Assert.Equal("Line one line two", IslandPipe.Clean("Line one\r\n\tline   two", 60));
        // Right-to-left override and zero-width characters are dropped: what you see is what was sent.
        Assert.Equal("invoice.exe", IslandPipe.Clean("invoice‮.exe​", 60));
        Assert.Equal("", IslandPipe.Clean("\u0007\u0000 ", 60));
        var cut = IslandPipe.Clean(new string('a', 200), IslandPipe.MaxTitle);
        Assert.Equal(IslandPipe.MaxTitle, cut.Length);
        Assert.EndsWith("…", cut);
    }

    [Fact]
    public void Each_program_gets_a_short_burst_then_waits()
    {
        var gate = new RateGate();
        var t = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
        Assert.True(gate.Allow("python", t));
        Assert.True(gate.Allow("python", t));
        Assert.True(gate.Allow("python", t));
        Assert.False(gate.Allow("python", t));                  // burst used up
        Assert.True(gate.Allow("pwsh", t));                     // another program has its own allowance
        Assert.True(gate.Allow("python", t.AddSeconds(RateGate.Every + 0.1)));   // one more after a pause
        Assert.False(gate.Allow("python", t.AddSeconds(RateGate.Every + 0.2)));
    }

    [Fact]
    public void All_programs_together_are_held_to_a_budget()
    {
        var gate = new RateGate();
        var t = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
        var allowed = Enumerable.Range(0, 20).Count(i => gate.Allow("app" + i, t));
        Assert.Equal((int)RateGate.TotalBurst, allowed);
    }

    [Fact]
    public void Card_icons_map_to_a_known_look()
    {
        Assert.Equal("", IslandController.AppCardLook("done").Glyph);
        Assert.Equal(IslandController.AppCardLook("info"), IslandController.AppCardLook("anything else"));
    }
}
