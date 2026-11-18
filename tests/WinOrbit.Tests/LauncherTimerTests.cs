using WinOrbit.Launcher;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class LauncherTimerTests
{
    [Theory]
    [InlineData("timer 5 min", 300)]
    [InlineData("timer 5m", 300)]
    [InlineData("timer 5", 300)]
    [InlineData("5 min timer", 300)]
    [InlineData("10-minute timer", 600)]
    [InlineData("set a timer for 10 minutes", 600)]
    [InlineData("5 minutes", 300)]
    [InlineData("Timer 1h 30m", 5400)]
    [InlineData("timer 1.5 h", 5400)]
    [InlineData("countdown 90s", 90)]
    [InlineData("timer 2 hours", 7200)]
    [InlineData("45 seconds", 45)]
    public void Countdown_phrases_start_a_timer(string query, int seconds)
    {
        var c = TimerCommand.Parse(query);
        Assert.NotNull(c);
        Assert.Equal(TimerMode.Countdown, c!.Mode);
        Assert.Equal(TimeSpan.FromSeconds(seconds), c.Length);
    }

    [Theory]
    [InlineData("5m")]                 // five metres, for the unit converter
    [InlineData("5 min to s")]         // a conversion
    [InlineData("timer")]
    [InlineData("timer 0")]
    [InlineData("timer 25 h")]         // over a day
    [InlineData("timer 1:30")]
    [InlineData("minecraft 5 min")]
    [InlineData("chrome")]
    [InlineData("")]
    public void Other_text_is_not_a_timer(string query) => Assert.Null(TimerCommand.Parse(query));

    [Fact]
    public void Focus_and_stopwatch_phrases()
    {
        Assert.Equal(new TimerCommand.Command(TimerMode.Focus, TimeSpan.FromMinutes(50)), TimerCommand.Parse("focus 50"));
        Assert.Equal(new TimerCommand.Command(TimerMode.Focus, TimeSpan.FromMinutes(45)), TimerCommand.Parse("pomodoro 45 min"));
        Assert.Equal(TimerMode.Stopwatch, TimerCommand.Parse("stopwatch")!.Mode);
        Assert.Null(TimerCommand.Parse("focus"));        // the launcher's own "Start a 25-minute focus" covers it
        Assert.Null(TimerCommand.Parse("focus 500"));
    }

    [Theory]
    [InlineData(45, "45-second timer")]
    [InlineData(300, "5-minute timer")]
    [InlineData(90, "1 min 30 s timer")]
    [InlineData(3600, "1-hour timer")]
    [InlineData(5400, "1 h 30 min timer")]
    public void Timer_lengths_read_naturally(int seconds, string text) =>
        Assert.Equal(text, TimerCommand.Describe(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Done_card_says_what_finished()
    {
        Assert.Equal(("Timer done", "5-minute timer finished"), WinOrbit.Island.IslandWindow.TimerDoneText(TimerMode.Countdown, TimeSpan.FromMinutes(5), 5));
        Assert.Equal(("Focus block complete", "Time for a 5-minute break"), WinOrbit.Island.IslandWindow.TimerDoneText(TimerMode.Focus, TimeSpan.FromMinutes(5), 5));
        Assert.Equal("Break over", WinOrbit.Island.IslandWindow.TimerDoneText(TimerMode.Break, TimeSpan.Zero, 5).Title);
    }
}
