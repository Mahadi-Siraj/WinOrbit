using System.Globalization;
using System.Text;
using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>
/// Reads an iCalendar (.ics) file — what Outlook, Google Calendar and Apple Calendar export — into island calendar
/// events. A one-off copy: nothing is synced and nothing goes online. Repeating events are expanded from 30 days ago
/// to a year ahead (daily, weekly on chosen days, monthly on a date, yearly); cancelled events and excluded dates are
/// skipped. Each event keeps its UID, so importing the same file again replaces rather than duplicates.
/// </summary>
public static class IcsImport
{
    private const int MaxEvents = 2000, MaxPerSeries = 400, MaxDaysPerEvent = 14;

    public sealed record Result(List<CalendarEvent> Events, int Skipped);

    public static Result Parse(string text, DateTime now, string source)
    {
        var events = new List<CalendarEvent>();
        var skipped = 0;
        var from = now.Date.AddDays(-30);
        var until = now.Date.AddDays(366);
        Dictionary<string, (string Value, Dictionary<string, string> Params)>? ev = null;
        var exdates = new List<(string Value, Dictionary<string, string> Params)>();
        // A changed (moved, renamed or cancelled) occurrence of a repeating event comes as its own VEVENT with the
        // series' UID and a RECURRENCE-ID naming the date it replaces; the series' own copy of that date is dropped.
        var replaced = new HashSet<(string Uid, DateTime Day)>();
        var overrides = new HashSet<CalendarEvent>();
        var nested = 0;

        foreach (var line in Unfold(text))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = new(StringComparer.OrdinalIgnoreCase); exdates.Clear(); nested = 0; continue; }
            if (ev is null) continue;
            // Skip the insides of alarms and other sub-blocks: their properties are not the event's.
            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase)) { nested++; continue; }
            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase) && nested > 0) { nested--; continue; }
            if (nested > 0) continue;
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (ev.TryGetValue("RECURRENCE-ID", out var rid) && ev.TryGetValue("UID", out var ruid) && When(rid.Value, rid.Params) is { } original)
                {
                    replaced.Add((ruid.Value.Trim(), original.Date));
                    ev.Remove("RRULE");
                }
                var added = Expand(ev, exdates, from, until, source);
                if (added is null) skipped++;
                else
                {
                    events.AddRange(added);
                    if (ev.ContainsKey("RECURRENCE-ID")) overrides.UnionWith(added);
                }
                ev = null;
                if (events.Count >= MaxEvents) break;
                continue;
            }
            if (Property(line) is not { } p) continue;
            if (p.Name.Equals("EXDATE", StringComparison.OrdinalIgnoreCase))
                foreach (var v in p.Value.Split(',')) exdates.Add((v, p.Params));
            else if (!ev.ContainsKey(p.Name)) ev[p.Name] = (p.Value, p.Params);
        }
        if (replaced.Count > 0)
            events.RemoveAll(e => e.Uid is not null && !overrides.Contains(e) && replaced.Contains((e.Uid, e.Date)));
        return new Result(events.Take(MaxEvents).ToList(), skipped);
    }

    /// <summary>Long lines are folded onto following lines that start with a space or tab.</summary>
    internal static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { sb.Append(raw, 1, raw.Length - 1); continue; }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear().Append(raw);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>"DTSTART;TZID=Europe/London:20261118T093000" → name, parameters, value.</summary>
    internal static (string Name, Dictionary<string, string> Params, string Value)? Property(string line)
    {
        // The value starts after the first colon that is not inside a quoted parameter.
        var quoted = false; var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) { colon = i; break; }
        }
        if (colon <= 0) return null;
        var head = line[..colon].Split(';');
        var ps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in head.Skip(1))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) ps[part[..eq]] = part[(eq + 1)..].Trim('"');
        }
        return (head[0].Trim(), ps, line[(colon + 1)..]);
    }

    internal static string Unescape(string s) =>
        s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    /// <summary>An ICS date or date-time in this PC's local time; Time is null for an all-day date.</summary>
    internal static (DateTime Date, TimeSpan? Time)? When(string value, Dictionary<string, string> ps)
    {
        value = value.Trim();
        var ci = CultureInfo.InvariantCulture;
        if (value.Length == 8 || (ps.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase)))
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", ci, DateTimeStyles.None, out var d) ? (d.Date, null) : null;

        var utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, ci, DateTimeStyles.None, out var t)) return null;
        DateTime local;
        if (utc) local = DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
        else if (ps.TryGetValue("TZID", out var tzid) && Zone(tzid) is { } zone)
        {
            try { local = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local); }
            catch (ArgumentException) { local = t; }   // a time skipped by a clock change in that zone
        }
        else local = t;   // "floating": the same clock time wherever you are
        return (local.Date, local.TimeOfDay);
    }

    /// <summary>A TZID as Windows or IANA ("Europe/London", "W. Europe Standard Time", "/mozilla.org/…/Europe/Berlin").</summary>
    private static TimeZoneInfo? Zone(string tzid)
    {
        foreach (var id in new[] { tzid, tzid.Contains('/') ? string.Join('/', tzid.Split('/').TakeLast(2)) : tzid })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win))
                try { return TimeZoneInfo.FindSystemTimeZoneById(win); } catch { }
        }
        return null;
    }

    private static List<CalendarEvent>? Expand(Dictionary<string, (string Value, Dictionary<string, string> Params)> ev,
        List<(string Value, Dictionary<string, string> Params)> exdates, DateTime from, DateTime until, string source)
    {
        if (ev.TryGetValue("STATUS", out var status) && status.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return null;
        if (!ev.TryGetValue("DTSTART", out var dt) || When(dt.Value, dt.Params) is not { } start) return null;
        var title = ev.TryGetValue("SUMMARY", out var sum) ? Unescape(sum.Value) : "";
        if (title.Length == 0) title = "Event";
        if (title.Length > 200) title = title[..200];
        var uid = ev.TryGetValue("UID", out var u) ? u.Value.Trim() : null;

        // All-day events that span several days get one entry per day (up to two weeks).
        var days = 1;
        if (start.Time is null && ev.TryGetValue("DTEND", out var end) && When(end.Value, end.Params) is { Time: null } e)
            days = Math.Clamp((int)(e.Date - start.Date).TotalDays, 1, MaxDaysPerEvent);

        var excluded = new HashSet<DateTime>(exdates.Select(x => When(x.Value, x.Params)).Where(x => x is not null).Select(x => x!.Value.Date));
        var starts = ev.TryGetValue("RRULE", out var rule) ? Occurrences(start.Date, rule.Value, from, until) : new List<DateTime> { start.Date };

        var list = new List<CalendarEvent>();
        foreach (var day in starts)
        {
            if (excluded.Contains(day)) continue;
            for (var i = 0; i < days; i++)
            {
                var d = day.AddDays(i);
                if (d < from || d > until) continue;
                list.Add(new CalendarEvent { Date = d, Time = start.Time, Title = title, Uid = uid, Source = source });
            }
        }
        return list;
    }

    /// <summary>
    /// The start dates of a repeating event between <paramref name="from"/> and <paramref name="until"/>.
    /// FREQ DAILY / WEEKLY (with BYDAY) / MONTHLY (same date) / YEARLY, with INTERVAL, COUNT and UNTIL.
    /// Rules it can't follow exactly (e.g. "the second Tuesday") keep just the first date.
    /// </summary>
    internal static List<DateTime> Occurrences(DateTime first, string rrule, DateTime from, DateTime until)
    {
        var r = rrule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First()[1].Trim());
        var freq = r.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        var interval = int.TryParse(r.GetValueOrDefault("INTERVAL"), out var n) && n > 0 ? n : 1;
        int? count = int.TryParse(r.GetValueOrDefault("COUNT"), out var c) && c > 0 ? c : null;
        var last = until;
        if (r.TryGetValue("UNTIL", out var u) && When(u, new Dictionary<string, string>()) is { } ud && ud.Date < last) last = ud.Date;

        var byDay = r.GetValueOrDefault("BYDAY", "");
        var simpleDays = byDay.Length == 0 ? null : byDay.Split(',').Select(DayOf).ToList();
        if (simpleDays is not null && simpleDays.Any(d => d is null) || freq is "MONTHLY" or "YEARLY" && byDay.Length > 0
            || freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY"))
            return new List<DateTime> { first };

        var result = new List<DateTime>();
        var made = 0;
        if (freq == "WEEKLY")
        {
            var days = simpleDays?.Select(d => d!.Value).ToHashSet() ?? new HashSet<DayOfWeek> { first.DayOfWeek };
            // Weeks start on Monday (the iCalendar default); walk week by week, then the chosen days in each.
            var weekStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
            for (var w = weekStart; w <= last && result.Count < MaxPerSeries; w = w.AddDays(7 * interval))
                for (var i = 0; i < 7; i++)
                {
                    var d = w.AddDays(i);
                    if (d < first || d > last || !days.Contains(d.DayOfWeek)) continue;
                    if (count is { } max && made >= max) return result;
                    made++;
                    if (d >= from && result.Count < MaxPerSeries) result.Add(d);
                }
            return result;
        }
        for (var k = 0; made < 100000; k++)
        {
            var d = freq switch
            {
                "DAILY" => first.AddDays((double)k * interval),
                "MONTHLY" => first.AddMonths(k * interval),
                _ => first.AddYears(k * interval),
            };
            if (d > last || result.Count >= MaxPerSeries) break;
            // 31st of a month that has no 31st: that month is skipped, as calendars do.
            if (freq == "MONTHLY" && d.Day != first.Day || freq == "YEARLY" && d.Day != first.Day) { continue; }
            if (count is { } max && made >= max) break;
            made++;
            if (d >= from) result.Add(d);
        }
        return result;
    }

    private static DayOfWeek? DayOf(string code) => code.Trim().ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
    };

    /// <summary>
    /// Merge imported events into the list: events from the same file with the same UID are replaced, so importing an
    /// updated export doesn't duplicate anything. Returns how many were added.
    /// </summary>
    public static int Merge(List<CalendarEvent> all, IReadOnlyList<CalendarEvent> imported)
    {
        var uids = imported.Where(e => e.Uid is not null).Select(e => e.Uid!).ToHashSet(StringComparer.Ordinal);
        all.RemoveAll(e => e.Uid is not null && uids.Contains(e.Uid));
        // Events without a UID: skip exact repeats of what is already there.
        var added = 0;
        foreach (var e in imported)
        {
            if (e.Uid is null && all.Any(x => x.Date == e.Date && x.Time == e.Time && x.Title == e.Title)) continue;
            all.Add(e);
            added++;
        }
        return added;
    }
}
