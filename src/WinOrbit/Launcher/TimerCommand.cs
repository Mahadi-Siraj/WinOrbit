using System.Globalization;
using System.Text.RegularExpressions;
using WinOrbit.Services;

namespace WinOrbit.Launcher;

/// <summary>
/// Timer phrases typed into the launcher: "timer 5 min", "10 minute timer", "timer 1h 30m", "countdown 90s",
/// "pomodoro", "focus 50", "stopwatch". A bare "5m" is left alone — it could be five metres for the unit converter.
/// </summary>
public static class TimerCommand
{
    public sealed record Command(TimerMode Mode, TimeSpan Length);

    private static readonly Regex Part = new(@"(\d+(?:[.,]\d+)?)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static Command? Parse(string query)
    {
        var q = Regex.Replace(query.Trim().ToLowerInvariant(), @"\s+", " ");
        if (q.Length == 0 || q.Length > 40) return null;
        if (q is "stopwatch" or "start stopwatch" or "start the stopwatch") return new(TimerMode.Stopwatch, TimeSpan.Zero);
        if (q is "pomodoro" or "focus" or "start focus") return null;   // the launcher's own "Start a 25-minute focus" covers these

        var focus = Regex.Match(q, @"^(?:focus|pomodoro)(?: for)? (\d{1,3})(?: ?(?:m|min|mins|minutes?))?$");
        if (focus.Success)
        {
            var m = int.Parse(focus.Groups[1].Value, CultureInfo.InvariantCulture);
            return m is >= 1 and <= 240 ? new(TimerMode.Focus, TimeSpan.FromMinutes(m)) : null;
        }

        // A countdown: the word "timer"/"countdown" plus a length, or a length written with a unit word ("5 min").
        var named = Regex.IsMatch(q, @"\b(timer|countdown)\b");
        var rest = Regex.Replace(q, @"\b(set|start|a|an|the|for|timer|countdown)\b", " ");
        rest = Regex.Replace(rest, @"(\d)\s*-\s*(?=[a-z])", "$1 ");          // "10-minute"
        var total = TimeSpan.Zero;
        var spelledUnit = false;
        var consumed = Part.Replace(rest, m =>
        {
            var n = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToLowerInvariant();
            if (unit.Length > 1) spelledUnit = true;
            total += unit[0] switch { 'h' => TimeSpan.FromHours(n), 'm' => TimeSpan.FromMinutes(n), _ => TimeSpan.FromSeconds(n) };
            return " ";
        });
        if (named && total == TimeSpan.Zero && Regex.Match(rest.Trim(), @"^(\d{1,3})$") is { Success: true } bare)
            total = TimeSpan.FromMinutes(int.Parse(bare.Groups[1].Value, CultureInfo.InvariantCulture));   // "timer 5" = 5 minutes
        else if (consumed.Trim().Length > 0) return null;                    // other words: not a timer phrase
        if (total <= TimeSpan.Zero || total > TimeSpan.FromHours(24)) return null;
        // "5m" alone stays with the unit converter; "5 min", "5 minutes" or "timer 5m" are timers.
        if (!named && !spelledUnit) return null;
        return new(TimerMode.Countdown, TimeSpan.FromSeconds(Math.Round(total.TotalSeconds)));
    }

    /// <summary>A length written only with numbers and units ("20 min", "1h 30m", "2 hours"), or null.</summary>
    internal static TimeSpan? Duration(string text)
    {
        var total = TimeSpan.Zero;
        var rest = Part.Replace(text, m =>
        {
            var n = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            total += char.ToLowerInvariant(m.Groups[2].Value[0]) switch { 'h' => TimeSpan.FromHours(n), 'm' => TimeSpan.FromMinutes(n), _ => TimeSpan.FromSeconds(n) };
            return " ";
        });
        return rest.Trim().Length == 0 && total > TimeSpan.Zero ? total : null;
    }

    /// <summary>"5-minute timer", "1 h 30 min timer", "90-second timer".</summary>
    internal static string Describe(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}-second timer";
        if (t.TotalMinutes < 60 && t.Seconds == 0) return $"{(int)t.TotalMinutes}-minute timer";
        var parts = new List<string>();
        if ((int)t.TotalHours > 0) parts.Add($"{(int)t.TotalHours} h");
        if (t.Minutes > 0) parts.Add($"{t.Minutes} min");
        if (t.Seconds > 0) parts.Add($"{t.Seconds} s");
        if (parts.Count == 1 && t.Minutes == 0 && t.Seconds == 0) return $"{(int)t.TotalHours}-hour timer";
        return string.Join(" ", parts) + " timer";
    }

    /// <summary>The launcher result for a timer phrase, or null.</summary>
    internal static LauncherItem? Item(string query, TimerService timers)
    {
        if (Parse(query) is not { } c) return null;
        var replaces = timers.IsActive ? $" · replaces the running {timers.ModeLabel.ToLowerInvariant()}" : "";
        return c.Mode switch
        {
            TimerMode.Stopwatch => new LauncherItem { Kind = ItemKind.Action, Key = "act:stopwatch", Title = "Start the stopwatch", Subtitle = "Shows in the island" + replaces, Glyph = Design.Glyph.Stopwatch, Score = 10_000,
                Run = () => { timers.SetMode(TimerMode.Stopwatch); timers.Start(); } },
            TimerMode.Focus => new LauncherItem { Kind = ItemKind.Action, Key = "act:focus-n", Title = $"Start a {(int)c.Length.TotalMinutes}-minute focus", Subtitle = "Focus timer, then a break" + replaces, Glyph = Design.Glyph.Stopwatch, Score = 10_000,
                Run = () => { timers.SetMode(TimerMode.Focus, (int)c.Length.TotalMinutes); timers.Start(); } },
            _ => new LauncherItem { Kind = ItemKind.Action, Key = "act:countdown", Title = "Start a " + Describe(c.Length), Subtitle = "Counts down in the island; a sound and a card when it's done" + replaces, Glyph = Design.Glyph.Stopwatch, Score = 10_000,
                Run = () => { timers.SetCountdown(c.Length); timers.Start(); } },
        };
    }
}
