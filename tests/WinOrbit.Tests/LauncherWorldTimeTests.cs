using WinOrbit.Launcher;
using Xunit;

namespace WinOrbit.Tests;

public class LauncherWorldTimeTests
{
    [Theory]
    [InlineData("time in Tokyo", "Tokyo")]
    [InlineData("what time is it in New York?", "New York")]
    [InlineData("what's the time in london", "london")]
    [InlineData("dhaka time", "dhaka")]
    [InlineData("time sydney", "sydney")]
    public void Time_questions_name_a_place(string query, string place) => Assert.Equal(place, WorldTime.Place(query));

    [Theory]
    [InlineData("timer 5 min")]
    [InlineData("time")]
    [InlineData("tokyo")]
    public void Other_text_is_not_a_time_question(string query)
    {
        if (WorldTime.Place(query) is { } p) Assert.Null(WorldTime.Zone(p));
    }

    [Theory]
    [InlineData("Tokyo", "Tokyo Standard Time")]
    [InlineData("São Paulo", "E. South America Standard Time")]
    [InlineData("sao paulo", "E. South America Standard Time")]
    [InlineData("Dhaka", "Bangladesh Standard Time")]
    [InlineData("bangladesh", "Bangladesh Standard Time")]
    [InlineData("PST", "Pacific Standard Time")]
    [InlineData("UK", "GMT Standard Time")]
    [InlineData("new york", "Eastern Standard Time")]
    public void Places_map_to_windows_zones(string place, string zone) => Assert.Equal(zone, WorldTime.Zone(place)!.Value.Zone.Id);

    [Fact]
    public void Unknown_places_and_ordinary_words_have_no_zone()
    {
        Assert.Null(WorldTime.Zone("machine"));    // "time machine"
        Assert.Null(WorldTime.Zone("screen"));     // "screen time"
        Assert.Null(WorldTime.Zone("x"));
    }

    /// <summary>Every zone in the table exists in this Windows (catches a misspelt zone ID).</summary>
    [Fact]
    public void Every_table_zone_exists() =>
        Assert.All(WorldTime.TableZones, id => Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(id)));

    [Fact]
    public void Offsets_read_naturally()
    {
        Assert.Equal("3 h ahead of you", WorldTime.Relative(TimeSpan.FromHours(3)));
        Assert.Equal("5 h 30 min behind you", WorldTime.Relative(TimeSpan.FromHours(-5.5)));
        Assert.Equal("30 min ahead of you", WorldTime.Relative(TimeSpan.FromMinutes(30)));
        Assert.Equal("same time as you", WorldTime.Relative(TimeSpan.Zero));
    }
}
