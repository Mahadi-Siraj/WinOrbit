using WinOrbit.Core;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class IcsImportTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0);

    private static string Cal(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//EN\r\n" + string.Join("", events) + "END:VCALENDAR\r\n";

    private static string Ev(params string[] lines) => "BEGIN:VEVENT\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT\r\n";

    [Fact]
    public void Reads_a_timed_event_an_all_day_event_and_escaped_text()
    {
        var r = IcsImport.Parse(Cal(
            Ev("UID:a1", "DTSTART:20261120T093000", "SUMMARY:Stand-up\\, team A"),
            Ev("UID:a2", "DTSTART;VALUE=DATE:20261125", "DTEND;VALUE=DATE:20261126", "SUMMARY:Holiday")), Now, "work.ics");
        Assert.Equal(2, r.Events.Count);
        var standup = r.Events.Single(e => e.Uid == "a1");
        Assert.Equal(new DateTime(2026, 11, 20), standup.Date);
        Assert.Equal(new TimeSpan(9, 30, 0), standup.Time);
        Assert.Equal("Stand-up, team A", standup.Title);
        Assert.Equal("work.ics", standup.Source);
        var holiday = r.Events.Single(e => e.Uid == "a2");
        Assert.Null(holiday.Time);
        Assert.Equal(new DateTime(2026, 11, 25), holiday.Date);
    }

    [Fact]
    public void Utc_and_zoned_times_land_in_local_time()
    {
        var r = IcsImport.Parse(Cal(
            Ev("UID:u", "DTSTART:20261120T150000Z", "SUMMARY:Utc"),
            Ev("UID:z", "DTSTART;TZID=America/New_York:20261120T100000", "SUMMARY:Zoned")), Now, "x.ics");
        var expectedUtc = DateTime.SpecifyKind(new DateTime(2026, 11, 20, 15, 0, 0), DateTimeKind.Utc).ToLocalTime();
        var utc = r.Events.Single(e => e.Uid == "u");
        Assert.Equal(expectedUtc.Date + expectedUtc.TimeOfDay, utc.Start);
        // 10:00 in New York in November (EST, UTC−5) is 15:00 UTC — the same moment.
        Assert.Equal(utc.Start, r.Events.Single(e => e.Uid == "z").Start);
    }

    [Fact]
    public void Folded_lines_alarms_and_cancelled_events_are_handled()
    {
        var r = IcsImport.Parse(Cal(
            "BEGIN:VEVENT\r\nUID:f\r\nDTSTART:20261121T080000\r\nSUMMARY:A very long\r\n  title\r\nBEGIN:VALARM\r\nSUMMARY:Alarm text\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT\r\n",
            Ev("UID:c", "DTSTART:20261122T080000", "STATUS:CANCELLED", "SUMMARY:Called off")), Now, "x.ics");
        Assert.Equal("A very long title", Assert.Single(r.Events).Title);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Weekly_events_repeat_on_their_days_within_the_window()
    {
        // Mondays and Wednesdays since 2025, ten weeks past the window start: only dates from 30 days ago on are kept.
        var r = IcsImport.Parse(Cal(Ev("UID:w", "DTSTART:20250106T090000", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE", "SUMMARY:Gym")), Now, "x.ics");
        Assert.All(r.Events, e => Assert.True(e.Date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday));
        Assert.All(r.Events, e => Assert.InRange(e.Date, Now.Date.AddDays(-30), Now.Date.AddDays(366)));
        Assert.Contains(r.Events, e => e.Date == new DateTime(2026, 11, 18));   // a Wednesday
        Assert.InRange(r.Events.Count, 100, 115);                               // two a week for ~13 months
    }

    [Fact]
    public void Count_until_interval_and_excluded_dates_are_respected()
    {
        var r = IcsImport.Parse(Cal(
            Ev("UID:n", "DTSTART:20261116T100000", "RRULE:FREQ=DAILY;COUNT=5", "EXDATE:20261118T100000", "SUMMARY:Sprint"),
            Ev("UID:u", "DTSTART:20261101T100000", "RRULE:FREQ=WEEKLY;INTERVAL=2;UNTIL=20261231T235959Z", "SUMMARY:Review"),
            Ev("UID:m", "DTSTART;VALUE=DATE:20260131", "RRULE:FREQ=MONTHLY;COUNT=4", "SUMMARY:Rent")), Now, "x.ics");
        Assert.Equal(new[] { 16, 17, 19, 20 }, r.Events.Where(e => e.Uid == "n").Select(e => e.Date.Day).ToArray());
        Assert.Equal(new[] { 1, 15, 29, 13, 27 }, r.Events.Where(e => e.Uid == "u").Select(e => e.Date.Day).ToArray());
        // The 31st repeats only in months that have one; COUNT counts real dates (Jan, Mar, May, Jul) — all before the window.
        Assert.DoesNotContain(r.Events, e => e.Uid == "m");
    }

    [Fact]
    public void A_moved_occurrence_replaces_the_series_date()
    {
        var r = IcsImport.Parse(Cal(
            Ev("UID:s", "DTSTART:20261116T090000", "RRULE:FREQ=DAILY;COUNT=5", "SUMMARY:Daily"),
            Ev("UID:s", "RECURRENCE-ID:20261118T090000", "DTSTART:20261118T140000", "SUMMARY:Daily (moved)")), Now, "x.ics");
        var wed = r.Events.Where(e => e.Date == new DateTime(2026, 11, 18)).ToList();
        Assert.Equal("Daily (moved)", Assert.Single(wed).Title);
        Assert.Equal(new TimeSpan(14, 0, 0), wed[0].Time);
        Assert.Equal(5, r.Events.Count);
    }

    [Fact]
    public void Unsupported_rules_keep_the_first_date_only()
        => Assert.Single(IcsImport.Occurrences(new DateTime(2026, 11, 10), "FREQ=MONTHLY;BYDAY=2TU", Now.AddDays(-30), Now.AddDays(366)));

    [Fact]
    public void Reimporting_replaces_instead_of_doubling()
    {
        var all = new List<CalendarEvent> { new() { Date = Now.Date, Title = "Typed by hand" } };
        var first = IcsImport.Parse(Cal(Ev("UID:r", "DTSTART:20261120T090000", "SUMMARY:Old title")), Now, "x.ics").Events;
        Assert.Equal(1, IcsImport.Merge(all, first));
        var again = IcsImport.Parse(Cal(Ev("UID:r", "DTSTART:20261120T100000", "SUMMARY:New title")), Now, "x.ics").Events;
        IcsImport.Merge(all, again);
        Assert.Equal(2, all.Count);
        Assert.Equal("New title", all.Single(e => e.Uid == "r").Title);
        Assert.Contains(all, e => e.Title == "Typed by hand");
    }

    [Fact]
    public void Garbage_does_not_throw()
    {
        var r = IcsImport.Parse("BEGIN:VEVENT\nDTSTART:notadate\nSUMMARY:x\nEND:VEVENT\nrandom: text\n:\n", Now, "x.ics");
        Assert.Empty(r.Events);
        Assert.Equal(1, r.Skipped);
    }
}
