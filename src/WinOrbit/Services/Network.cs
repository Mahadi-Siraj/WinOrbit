using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>
/// The ONLY place in WinOrbit that can open a network connection.
/// It refuses unless the user turned on the master network switch, sends no cookies,
/// no identifiers and no custom headers, and records every request in a ledger the user can read.
/// There is no telemetry, analytics, crash upload or automatic update check anywhere in the app.
/// </summary>
public sealed class NetworkGate
{
    public sealed record Entry(DateTime When, string Host, string Purpose, string Result);

    private readonly Func<PrivacySettings> _privacy;
    private HttpClient? _client;
    private readonly List<Entry> _ledger = new();

    public NetworkGate(Func<PrivacySettings> privacy) => _privacy = privacy;

    public bool Allowed => _privacy().NetworkAllowed;
    public IReadOnlyList<Entry> Ledger { get { lock (_ledger) return _ledger.ToArray(); } }
    public int RequestCount { get { lock (_ledger) return _ledger.Count; } }

    public async Task<string> GetStringAsync(string url, string purpose)
    {
        var uri = new Uri(url);
        if (!Allowed)
        {
            Record(uri.Host, purpose, "BLOCKED (network is off)");
            throw new InvalidOperationException("Network access is turned off in Settings › Privacy.");
        }
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Only HTTPS requests are permitted.");
        _client ??= new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
        try
        {
            var text = await _client.GetStringAsync(uri);
            Record(uri.Host, purpose, "OK");
            return text;
        }
        catch (Exception ex)
        {
            Record(uri.Host, purpose, "FAILED: " + ex.GetType().Name);
            throw;
        }
    }

    private void Record(string host, string purpose, string result)
    {
        lock (_ledger)
        {
            _ledger.Add(new Entry(DateTime.Now, host, purpose, result));
            if (_ledger.Count > 200) _ledger.RemoveAt(0);
        }
    }
}

public sealed record LyricLine(TimeSpan At, string Text);

/// <summary>
/// Optional time-synced lyrics from LRCLIB (free, open, no account or key). Off by default and only active while
/// the network switch is on. Sends the track title, artist, album and length — nothing about the user — and
/// keeps results in memory only. Players that publish no title (some browser videos) get no lookup.
/// </summary>
public sealed class LyricsService
{
    private readonly NetworkGate _net;
    private readonly Func<PrivacySettings> _privacy;
    private readonly Dictionary<string, List<LyricLine>?> _cache = new();

    public LyricsService(NetworkGate net, Func<PrivacySettings> privacy) { _net = net; _privacy = privacy; }

    public bool Enabled => _privacy().NetworkAllowed && _privacy().LyricsEnabled;

    /// <summary>Synced lines for a track, or null when lyrics are off, unknown or not time-synced.</summary>
    public async Task<List<LyricLine>?> GetAsync(string title, string artist, string album, TimeSpan duration)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return null;
        var key = $"{title}|{artist}|{(int)duration.TotalSeconds}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;
        List<LyricLine>? result = null;
        try
        {
            var q = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrWhiteSpace(album)) q += "&album_name=" + Uri.EscapeDataString(album);
            if (duration > TimeSpan.Zero) q += "&duration=" + (int)Math.Round(duration.TotalSeconds);
            try
            {
                using var doc = JsonDocument.Parse(await _net.GetStringAsync("https://lrclib.net/api/get?" + q, "Lyrics: look up track"));
                result = FromRecord(doc.RootElement);
            }
            catch (HttpRequestException) { }      // 404: no exact match — try a search below
            if (result is null)
            {
                var s = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                using var doc = JsonDocument.Parse(await _net.GetStringAsync("https://lrclib.net/api/search?" + s, "Lyrics: search track"));
                foreach (var r in doc.RootElement.EnumerateArray())
                {
                    var d = r.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetDouble() : 0;
                    if (duration > TimeSpan.Zero && d > 0 && Math.Abs(d - duration.TotalSeconds) > 4) continue;
                    if ((result = FromRecord(r)) is not null) break;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Lyrics lookup failed: " + ex.GetType().Name); }
        _cache[key] = result;
        if (_cache.Count > 200) _cache.Remove(_cache.Keys.First());
        return result;
    }

    private static List<LyricLine>? FromRecord(JsonElement r)
        => r.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? Parse(s.GetString() ?? "") : null;

    /// <summary>Parse LRC text: "[mm:ss.xx] words", several time tags per line allowed; metadata tags ignored.</summary>
    internal static List<LyricLine>? Parse(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var stamps = new List<TimeSpan>();
            var i = 0;
            while (i < line.Length && line[i] == '[')
            {
                var close = line.IndexOf(']', i);
                if (close < 0) break;
                var tag = line[(i + 1)..close];
                var colon = tag.IndexOf(':');
                if (colon > 0 && int.TryParse(tag[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
                    && double.TryParse(tag[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                    stamps.Add(TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec));
                i = close + 1;
            }
            // "Enhanced" LRC puts a timestamp before every word: <00:19.25>no <00:19.53>strangers — keep the words only.
            var text = System.Text.RegularExpressions.Regex.Replace(line[i..], @"<\d+:\d+(?:[.:]\d+)?>", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ").Trim();
            foreach (var t in stamps) lines.Add(new LyricLine(t, text));
        }
        if (lines.Count == 0) return null;
        lines.Sort((a, b) => a.At.CompareTo(b.At));
        return lines;
    }

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].At <= position) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found;
    }
}

public sealed record WeatherPlace(string Name, string Region, double Lat, double Lon)
{
    public override string ToString() => string.IsNullOrEmpty(Region) ? Name : $"{Name}, {Region}";
}

public sealed record WeatherDay(DateTime Date, double Min, double Max, int Code, int RainChance = -1, DateTime? Sunrise = null, DateTime? Sunset = null);

/// <summary>One hour of the forecast, in the place's own local time.</summary>
public sealed record WeatherHour(DateTime Time, double Temperature, int Code, bool IsDay, int RainChance);

public sealed class WeatherReport
{
    public double Temperature, Feels, Wind;
    public int Code;
    public int Humidity = -1;
    public bool IsDay = true;
    public List<WeatherDay> Days = new();
    public List<WeatherHour> Hours = new();
    public DateTime Fetched = DateTime.Now;
}

/// <summary>
/// Optional weather through Open-Meteo (no account, no API key). Off by default.
/// The only data sent is the coordinates of the place the user typed in.
/// </summary>
public sealed class WeatherService
{
    private readonly NetworkGate _net;
    private readonly Func<PrivacySettings> _privacy;
    private WeatherReport? _cache;

    public WeatherService(NetworkGate net, Func<PrivacySettings> privacy) { _net = net; _privacy = privacy; }

    public bool Enabled => _privacy().NetworkAllowed && _privacy().WeatherEnabled && !string.IsNullOrEmpty(_privacy().WeatherPlace);

    public async Task<List<WeatherPlace>> SearchAsync(string query)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=6&format=json&name=" + Uri.EscapeDataString(query);
        using var doc = JsonDocument.Parse(await _net.GetStringAsync(url, "Weather: find place"));
        var list = new List<WeatherPlace>();
        if (doc.RootElement.TryGetProperty("results", out var results))
            foreach (var r in results.EnumerateArray())
            {
                var region = string.Join(", ", new[] { Str(r, "admin1"), Str(r, "country") }.Where(s => s.Length > 0));
                list.Add(new WeatherPlace(Str(r, "name"), region, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
            }
        return list;
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Current conditions; cached for 30 minutes so a visible widget makes at most two requests an hour.</summary>
    public async Task<WeatherReport?> GetAsync(bool force = false)
    {
        if (!Enabled) return null;
        if (!force && _cache is not null && DateTime.Now - _cache.Fetched < TimeSpan.FromMinutes(30)) return _cache;
        var p = _privacy();
        var ci = CultureInfo.InvariantCulture;
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={p.WeatherLat.ToString("0.###", ci)}&longitude={p.WeatherLon.ToString("0.###", ci)}" +
                  "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day,relative_humidity_2m" +
                  "&hourly=temperature_2m,weather_code,is_day,precipitation_probability&forecast_hours=12" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max&timezone=auto&forecast_days=4" +
                  (p.WeatherFahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
        using var doc = JsonDocument.Parse(await _net.GetStringAsync(url, "Weather: forecast"));
        var report = Parse(doc.RootElement);
        _cache = report;
        return report;
    }

    public void Invalidate() => _cache = null;

    /// <summary>The last report fetched, without asking the network (for the launcher's tile and the island).</summary>
    public WeatherReport? Last => Enabled ? _cache : null;

    internal static WeatherReport Parse(JsonElement root)
    {
        var cur = root.GetProperty("current");
        var report = new WeatherReport
        {
            Temperature = cur.GetProperty("temperature_2m").GetDouble(),
            Feels = cur.GetProperty("apparent_temperature").GetDouble(),
            Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
            Code = cur.GetProperty("weather_code").GetInt32(),
            Humidity = cur.TryGetProperty("relative_humidity_2m", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : -1,
            IsDay = !cur.TryGetProperty("is_day", out var d) || d.ValueKind != JsonValueKind.Number || d.GetInt32() == 1,
        };
        if (root.TryGetProperty("daily", out var daily))
        {
            var dates = Dates(daily, "time");
            var max = Numbers(daily, "temperature_2m_max");
            var min = Numbers(daily, "temperature_2m_min");
            var code = Numbers(daily, "weather_code");
            var rain = Numbers(daily, "precipitation_probability_max");
            var rise = Dates(daily, "sunrise");
            var set = Dates(daily, "sunset");
            for (var i = 0; i < dates.Length; i++)
                report.Days.Add(new WeatherDay(dates[i], min[i], max[i], (int)code[i], i < rain.Length ? (int)rain[i] : -1,
                    i < rise.Length ? rise[i] : null, i < set.Length ? set[i] : null));
        }
        if (root.TryGetProperty("hourly", out var hourly))
        {
            var times = Dates(hourly, "time");
            var temp = Numbers(hourly, "temperature_2m");
            var code = Numbers(hourly, "weather_code");
            var day = Numbers(hourly, "is_day");
            var rain = Numbers(hourly, "precipitation_probability");
            for (var i = 0; i < times.Length && i < temp.Length && i < code.Length; i++)
                report.Hours.Add(new WeatherHour(times[i], temp[i], (int)code[i], i >= day.Length || day[i] >= 1, i < rain.Length ? (int)rain[i] : -1));
        }
        return report;
    }

    // Missing or null values (Open-Meteo sends null for hours it has no model data for) read as 0 rather than failing.
    private static double[] Numbers(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0).ToArray()
            : Array.Empty<double>();

    private static DateTime[] Dates(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(v => DateTime.Parse(v.GetString()!, CultureInfo.InvariantCulture)).ToArray()
            : Array.Empty<DateTime>();

    /// <summary>WMO weather code → (description, line icon path from <see cref="Design.LineIcons"/>); clear nights show the moon.</summary>
    public static (string Text, string Icon) Describe(int code, bool isDay) =>
        !isDay && code is 0 or 1 ? (code == 0 ? "Clear" : "Mostly clear", Design.LineIcons.Moon) : Describe(code);

    /// <summary>WMO weather code → (description, line icon path from <see cref="Design.LineIcons"/>).</summary>
    public static (string Text, string Icon) Describe(int code) => code switch
    {
        0 => ("Clear", Design.LineIcons.Sun),
        1 or 2 => ("Partly cloudy", Design.LineIcons.CloudSun),
        3 => ("Overcast", Design.LineIcons.Cloud),
        45 or 48 => ("Fog", Design.LineIcons.Fog),
        >= 51 and <= 57 => ("Drizzle", Design.LineIcons.Rain),
        >= 61 and <= 67 => ("Rain", Design.LineIcons.Rain),
        >= 71 and <= 77 => ("Snow", Design.LineIcons.Snow),
        >= 80 and <= 82 => ("Showers", Design.LineIcons.Rain),
        85 or 86 => ("Snow showers", Design.LineIcons.Snow),
        >= 95 => ("Thunderstorm", Design.LineIcons.Storm),
        _ => ("Unknown", Design.LineIcons.Cloud)
    };
}
