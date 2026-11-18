using System.Globalization;
using System.Text.RegularExpressions;
using WinOrbit.Core;

namespace WinOrbit.Launcher;

/// <summary>
/// Reminders typed into the launcher: "remind me in 20 min to stretch", "remind me at 15:30 to call the bank",
/// "remind me tomorrow at 9 stand-up", "remind me to water the plants at 6pm". They are saved as calendar events
/// that show one card in the island at that minute (<see cref="CalendarEvent.AtTimeOnly"/>). Stays on this PC.
/// </summary>
public static class ReminderCommand
{
    private const string Dur = @"(?:\d+(?:[.,]\d+)?\s*(?:hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)\b\s*)+";
    private const string Time = @"\d{1,2}(?:[:.]\d{2})?\s*(?:a\.?m\.?|p\.?m\.?)?";
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex InFirst = new($@"^remind(?: me)? in (?<dur>{Dur})(?:to )?(?<text>.+)$", Opt);
    private static readonly Regex AtFirst = new($@"^remind(?: me)? (?:(?<day>today|tomorrow) )?(?:at )?(?<time>{Time})(?: (?<day2>today|tomorrow))? (?:to )?(?<text>.+)$", Opt);
    private static readonly Regex InLast = new($@"^remind(?: me)? (?:to )?(?<text>.+?) in (?<dur>{Dur})$", Opt);
    private static readonly Regex AtLast = new($@"^remind(?: me)? (?:to )?(?<text>.+?) (?:(?<day>today|tomorrow) )?at (?<time>{Time})(?: (?<day2>today|tomorrow))?$", Opt);
    private static readonly Regex TimeParts = new(@"^(\d{1,2})(?:[:.](\d{2}))?\s*(a\.?m\.?|p\.?m\.?)?$", Opt);

    public sealed record Reminder(DateTime When, string Text);

    internal static Reminder? Parse(string query, DateTime now)
    {
        var q = Regex.Replace(query.Trim(), @"\s+", " ");
        if (q.Length > 160 || !q.StartsWith("remind", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var re in new[] { InFirst, InLast })
        {
            var m = re.Match(q);
            if (!m.Success || TimerCommand.Duration(m.Groups["dur"].Value) is not { } d) continue;
            if (d > TimeSpan.FromDays(7)) return null;
            var when = RoundToMinute(now + d);
            if (when <= now) when = when.AddMinutes(1);
            return Make(when, m.Groups["text"].Value);
        }
        foreach (var re in new[] { AtFirst, AtLast })
        {
            var m = re.Match(q);
            if (!m.Success) continue;
            var timeText = m.Groups["time"].Value.Trim();
            var explicitAt = re == AtLast || Regex.IsMatch(q, @"\bat " + Regex.Escape(timeText), Opt);
            var day = (m.Groups["day"].Success ? m.Groups["day"].Value : m.Groups["day2"].Value).ToLowerInvariant();
            if (Resolve(timeText, day, explicitAt, now) is not { } when) continue;
            return Make(when, m.Groups["text"].Value);
        }
        return null;
    }

    private static Reminder? Make(DateTime when, string text)
    {
        text = text.Trim().TrimEnd('.', '!', ',');
        if (text.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) text = text[3..].Trim();
        if (text.Length == 0 || text.Equals("me", StringComparison.OrdinalIgnoreCase)) return null;   // "remind me in 20 min" — of what?
        return new(when, char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..]);
    }

    private static DateTime RoundToMinute(DateTime t) =>
        new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind).AddMinutes(t.Second >= 30 ? 1 : 0);

    /// <summary>
    /// "15:30", "9.15am", "6pm"; a bare hour ("at 5") needs the word "at" and means the next 5 o'clock — 5:00 or
    /// 17:00, whichever comes first (tomorrow: 1–6 are afternoon, 7–12 morning). A time already past today is tomorrow.
    /// </summary>
    internal static DateTime? Resolve(string timeText, string day, bool explicitAt, DateTime now)
    {
        var m = TimeParts.Match(timeText.Trim());
        if (!m.Success) return null;
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (minute > 59) return null;
        var today = now.Date;
        var start = day == "tomorrow" ? today.AddDays(1) : today;

        if (m.Groups[3].Success)
        {
            if (hour is < 1 or > 12) return null;
            hour = hour % 12 + (char.ToLowerInvariant(m.Groups[3].Value[0]) == 'p' ? 12 : 0);
        }
        else if (!m.Groups[2].Success && hour is >= 1 and <= 12)
        {
            if (!explicitAt) return null;                               // "remind me 3 books" is not a time
            if (day == "tomorrow") return start.AddHours(hour <= 6 ? hour + 12 : hour);
            var morning = today.AddHours(hour % 12);
            var evening = morning.AddHours(12);
            if (hour == 12) { morning = today.AddHours(12); evening = today.AddDays(1); }
            if (day != "today" && morning <= now && evening <= now) return morning.AddDays(1);
            return morning > now ? morning : evening > now ? evening : null;
        }
        else if (hour > 23) return null;

        var when = start.AddHours(hour).AddMinutes(minute);
        if (when <= now)
        {
            if (day.Length > 0) return null;                            // "today at 9" when it's already 10
            when = when.AddDays(1);
        }
        return when;
    }

    /// <summary>"in 20 minutes", "today", "tomorrow", "Fri 21 Nov".</summary>
    internal static string When(DateTime when, DateTime now)
    {
        var minutes = (int)Math.Round((when - now).TotalMinutes);
        if (minutes < 60 && when.Date == now.Date) return minutes <= 1 ? "in a minute" : $"in {minutes} minutes";
        if (when.Date == now.Date) return "today";
        if (when.Date == now.Date.AddDays(1)) return "tomorrow";
        return when.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    }

    internal static LauncherItem? Item(string query)
    {
        var now = DateTime.Now;
        if (Parse(query, now) is not { } r) return null;
        var at = Services.Fmt.Time(r.When, Hub.Settings.General.Use24Hour);
        var sub = char.ToUpper(When(r.When, now)[0]) + When(r.When, now)[1..] + " · a card in the island at that time";
        if (!Hub.Settings.Island.EventReminders) sub = "Event reminders are off (Dynamic Island settings), so no card will show";
        return new LauncherItem
        {
            Kind = ItemKind.Action, Key = "act:remind", Title = $"Remind me at {at}: {r.Text}", Subtitle = sub, Glyph = Design.Glyph.Clock, Score = 10_000,
            Run = () =>
            {
                Hub.Store.Data.Events.Add(new CalendarEvent { Date = r.When.Date, Time = r.When.TimeOfDay, Title = r.Text, AtTimeOnly = true });
                Hub.Store.SaveData();
                Hub.Island.RefreshCalendar();
                Hub.Island.Peek(Design.Glyph.Clock, $"Reminder set for {at}");
            }
        };
    }
}
