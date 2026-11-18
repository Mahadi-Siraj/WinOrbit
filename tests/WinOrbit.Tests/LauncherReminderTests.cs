using WinOrbit.Core;
using WinOrbit.Launcher;
using Xunit;

namespace WinOrbit.Tests;

public class LauncherReminderTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 14, 10, 20);   // a Thursday afternoon

    [Theory]
    [InlineData("remind me in 20 min to stretch", "2026-11-19 14:30", "Stretch")]
    [InlineData("remind me in 1h 30m drink water", "2026-11-19 15:40", "Drink water")]
    [InlineData("Remind me to call mom in 2 hours", "2026-11-19 16:10", "Call mom")]
    [InlineData("remind me at 15:30 to call the bank", "2026-11-19 15:30", "Call the bank")]
    [InlineData("remind me at 9:30 standup", "2026-11-20 09:30", "Standup")]              // passed today → tomorrow
    [InlineData("remind me tomorrow at 9 stand-up", "2026-11-20 09:00", "Stand-up")]
    [InlineData("remind me tomorrow at 3 dentist", "2026-11-20 15:00", "Dentist")]          // 1–6 tomorrow → afternoon
    [InlineData("remind me at 5 take out the bins", "2026-11-19 17:00", "Take out the bins")] // the next 5 o'clock
    [InlineData("remind me at 2 lunch", "2026-11-20 02:00", "Lunch")]                       // 2:00 and 14:00 both passed
    [InlineData("remind me to water the plants at 6pm", "2026-11-19 18:00", "Water the plants")]
    [InlineData("remind 7.45pm pizza", "2026-11-19 19:45", "Pizza")]
    [InlineData("remind me today at 9 film", "2026-11-19 21:00", "Film")]                   // 9:00 has passed → 21:00
    [InlineData("remind me to pay rent tomorrow at 10am", "2026-11-20 10:00", "Pay rent")]
    public void Reminder_phrases(string query, string when, string text)
    {
        var r = ReminderCommand.Parse(query, Now);
        Assert.NotNull(r);
        Assert.Equal(DateTime.Parse(when, System.Globalization.CultureInfo.InvariantCulture), r!.When);
        Assert.Equal(text, r.Text);
    }

    [Theory]
    [InlineData("remind me 3 books to return")]   // a bare number without "at" is not a time
    [InlineData("remind me in 20 min")]           // nothing to be reminded of
    [InlineData("remind me today at 9:00 standup")]  // already past today
    [InlineData("remind me at 25:00 x")]
    [InlineData("remind me in 9 days to x")]      // more than a week
    [InlineData("timer 5 min")]
    [InlineData("reminders")]
    public void Not_reminders(string query) => Assert.Null(ReminderCommand.Parse(query, Now));

    [Fact]
    public void Reminder_time_reads_naturally()
    {
        Assert.Equal("in 20 minutes", ReminderCommand.When(Now.AddMinutes(20), Now));
        Assert.Equal("today", ReminderCommand.When(Now.Date.AddHours(20), Now));
        Assert.Equal("tomorrow", ReminderCommand.When(Now.Date.AddDays(1).AddHours(9), Now));
    }

    [Fact]
    public void A_reminder_card_shows_only_on_its_minute()
    {
        var r = new CalendarEvent { Date = Now.Date, Time = new TimeSpan(14, 30, 0), Title = "Stretch", AtTimeOnly = true };
        Assert.Null(WinOrbit.Island.IslandWindow.ReminderDue(r, Now.Date.AddHours(14).AddMinutes(20)));    // no 10-minute heads-up
        Assert.Null(WinOrbit.Island.IslandWindow.ReminderDue(r, Now.Date.AddHours(14).AddMinutes(29)));    // not a minute early
        Assert.Equal("now", WinOrbit.Island.IslandWindow.ReminderDue(r, Now.Date.AddHours(14).AddMinutes(30).AddMilliseconds(40)));
        Assert.Equal("now", WinOrbit.Island.IslandWindow.ReminderDue(r, Now.Date.AddHours(14).AddMinutes(33)));   // island was busy
        var ev = new CalendarEvent { Date = Now.Date, Time = new TimeSpan(14, 30, 0), Title = "Meeting" };
        Assert.Equal("soon", WinOrbit.Island.IslandWindow.ReminderDue(ev, Now.Date.AddHours(14).AddMinutes(20)));
    }
}
