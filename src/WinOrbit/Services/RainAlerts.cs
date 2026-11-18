using System.Windows.Threading;
using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>
/// Optional "rain soon" alert. Off by default, and it only runs while weather is turned on in Privacy: every
/// 30 minutes it asks for the forecast of the user's place (the same request, and the same 30-minute cache, as
/// the weather widget and tab; each one shows in the network ledger) and raises <see cref="RainSoon"/> once when
/// rain or snow is likely within the next two hours while it is dry now.
/// </summary>
public sealed class RainAlerts
{
    private readonly DispatcherTimer _timer;
    private DateTime _alertedFor = DateTime.MinValue;

    /// <summary>(minutes until it starts, chance in % or -1, what: "Rain", "Showers", "Snow"…). On the UI thread.</summary>
    public event Action<int, int, string>? RainSoon;

    public RainAlerts(Dispatcher ui)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public void SetEnabled(bool on)
    {
        if (on == _timer.IsEnabled) return;
        if (!on) { _timer.Stop(); return; }
        _timer.Start();
        // First look a minute after start-up (or after switching it on), not in the middle of it.
        var first = new DispatcherTimer(DispatcherPriority.Background, _timer.Dispatcher) { Interval = TimeSpan.FromMinutes(1) };
        first.Tick += async (_, _) => { first.Stop(); if (_timer.IsEnabled) await CheckAsync(); };
        first.Start();
    }

    private async Task CheckAsync()
    {
        if (!Hub.Weather.Enabled) return;
        try
        {
            var report = await Hub.Weather.GetAsync();
            if (report is null || Soon(report, DateTime.Now.Minute) is not { } soon) return;
            // One alert per spell of rain: the hour it starts, not again for three hours after that.
            var starts = DateTime.Now.AddMinutes(soon.Minutes);
            if (Math.Abs((starts - _alertedFor).TotalHours) < 3) return;
            _alertedFor = starts;
            RainSoon?.Invoke(soon.Minutes, soon.Chance, soon.What);
        }
        catch (Exception ex) { Log.Warn("Rain alert: " + ex.Message); }
    }

    /// <summary>Rain, drizzle, showers, snow or thunder (WMO codes).</summary>
    internal static bool IsWet(int code) => code is >= 51 and <= 67 or >= 71 and <= 77 or >= 80 and <= 86 or >= 95;

    /// <summary>
    /// Whether rain is likely within about two hours while it is dry now. <paramref name="minuteOfHour"/> is how far
    /// into the current hour we are; the first forecast hour is the current one.
    /// </summary>
    internal static (int Minutes, int Chance, string What)? Soon(WeatherReport r, int minuteOfHour)
    {
        if (IsWet(r.Code) || r.Hours.Count < 2) return null;
        if (r.Hours[0].RainChance >= 70 || IsWet(r.Hours[0].Code)) return null;   // already as good as raining: nothing new to say
        for (var i = 1; i <= 2 && i < r.Hours.Count; i++)
        {
            var h = r.Hours[i];
            var likely = IsWet(h.Code) ? h.RainChance is < 0 or >= 40 : h.RainChance >= 70;
            if (!likely) continue;
            var what = IsWet(h.Code) ? WeatherService.Describe(h.Code).Text : "Rain";
            return (Math.Max(5, i * 60 - minuteOfHour), h.RainChance, what);
        }
        return null;
    }

    /// <summary>"Rain in about 40 min", "Snow in about an hour"…</summary>
    internal static string Title(int minutes, string what) =>
        what + " " + (minutes <= 10 ? "any minute now" : minutes < 50 ? $"in about {(int)Math.Round(minutes / 5.0) * 5} min" : minutes < 75 ? "in about an hour" : "in under two hours");
}
