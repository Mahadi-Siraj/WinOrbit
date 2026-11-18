using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>
/// The ONLY place in WinOrbit that can open a network connection.
/// It refuses unless the user turned on the master network switch, sends no cookies,
/// no identifiers and no custom headers, and records every request in a ledger the user can read.
/// There is no telemetry, analytics, crash upload or automatic update check anywhere in the app
/// (Settings' "Check now" asks for the latest version number only when clicked — see Services/Updates.cs).
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
    /// <remarks>
    /// Players often publish messy names — "Artist - Song (Official Video) [4K]" from a YouTube channel called
    /// "ArtistVEVO". The name is cleaned first (<see cref="Queries"/>); at most three requests are made per track:
    /// one exact lookup and up to two searches.
    /// </remarks>
    public async Task<List<LyricLine>?> GetAsync(string title, string artist, string album, TimeSpan duration)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(title)) return null;
        var queries = Queries(title, artist);
        if (queries.Count == 0) return null;
        var key = $"{title}|{artist}|{(int)duration.TotalSeconds}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;
        List<LyricLine>? result = null;
        try
        {
            var (t0, a0) = queries[0];
            var q = $"track_name={Uri.EscapeDataString(t0)}&artist_name={Uri.EscapeDataString(a0)}";
            // The album only helps when the name came through untouched (a music app, not a video page).
            if (!string.IsNullOrWhiteSpace(album) && t0 == title.Trim()) q += "&album_name=" + Uri.EscapeDataString(album);
            if (duration > TimeSpan.Zero) q += "&duration=" + (int)Math.Round(duration.TotalSeconds);
            try
            {
                using var doc = JsonDocument.Parse(await _net.GetStringAsync("https://lrclib.net/api/get?" + q, "Lyrics: look up track"));
                result = FromRecord(doc.RootElement);
            }
            catch (HttpRequestException) { }      // 404: no exact match — try a search below
            foreach (var (t, a) in queries.Take(2))
            {
                if (result is not null) break;
                var s = $"track_name={Uri.EscapeDataString(t)}&artist_name={Uri.EscapeDataString(a)}";
                using var doc = JsonDocument.Parse(await _net.GetStringAsync("https://lrclib.net/api/search?" + s, "Lyrics: search track"));
                var found = new List<(double Duration, List<LyricLine> Lines)>();
                foreach (var r in doc.RootElement.EnumerateArray())
                {
                    var d = r.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetDouble() : 0;
                    if (FromRecord(r) is { } lines) found.Add((d, lines));
                }
                var pick = Pick(found.Select(f => f.Duration).ToList(), duration.TotalSeconds);
                if (pick >= 0) result = found[pick].Lines;
            }
        }
        catch (Exception ex) { Log.Warn("Lyrics lookup failed: " + ex.GetType().Name); }
        _cache[key] = result;
        if (_cache.Count > 200) _cache.Remove(_cache.Keys.First());
        return result;
    }

    private static readonly System.Text.RegularExpressions.Regex Noise = new(
        @"\s*[\(\[【]([^\)\]】]*\b(official|video|audio|lyrics?|visuali[sz]er|hd|hq|4k|8k|1080p|720p|mv|m/v|remaster(ed)?|explicit|clean|colou?r coded|full song|music video|performance video)\b[^\)\]】]*)[\)\]】]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex Featuring = new(
        @"\s*(?:[\(\[]\s*(?:feat\.?|ft\.?|featuring)\s[^\)\]]*[\)\]]|\s(?:feat\.?|ft\.?|featuring)\s.*$)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex RemasterTail = new(
        @"\s+[-–—]\s+(?:\d{4}\s+)?remaster(?:ed)?(?:\s+\d{4})?(?:\s+version)?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A track name without the video-page noise: "(Official Video)", "[4K]", "(Lyrics)", "- 2011 Remaster",
    /// "feat. Someone", and anything after " | ". "(Live)", "(Acoustic)" and "(Remix)" stay — they are different recordings.
    /// </summary>
    internal static string CleanTitle(string title)
    {
        var t = title.Trim();
        var bar = t.IndexOf(" | ", StringComparison.Ordinal);
        if (bar > 0) t = t[..bar];
        for (var i = 0; i < 3; i++) t = Noise.Replace(t, "");
        t = Featuring.Replace(t, "");
        t = RemasterTail.Replace(t, "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s{2,}", " ").Trim().TrimEnd('-', '–', '—', ' ');
        return t.Length > 0 ? t : title.Trim();
    }

    /// <summary>An artist name without YouTube channel decorations ("ArtistVEVO", "Artist - Topic") or featured artists.</summary>
    internal static string CleanArtist(string artist)
    {
        var a = artist.Trim();
        if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) a = a[..^8];
        if (a.Length > 4 && a.EndsWith("VEVO", StringComparison.Ordinal)) a = a[..^4];
        if (a.EndsWith(" Official", StringComparison.OrdinalIgnoreCase)) a = a[..^9];
        a = Featuring.Replace(a, "");
        return a.Trim();
    }

    private static bool LooksLikeChannel(string artist) =>
        artist.EndsWith("VEVO", StringComparison.Ordinal) || artist.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase) ||
        artist.EndsWith("Official", StringComparison.OrdinalIgnoreCase) || artist.Contains("Records", StringComparison.OrdinalIgnoreCase) ||
        artist.Contains("Music", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The (title, artist) pairs worth asking for, best first, at most three. A video titled "Artist - Song" on a
    /// channel that isn't the artist's own name is split into its parts.
    /// </summary>
    internal static List<(string Title, string Artist)> Queries(string title, string artist)
    {
        var list = new List<(string, string)>();
        void Add(string t, string a)
        {
            t = t.Trim(); a = a.Trim();
            if (t.Length == 0 || a.Length == 0 || list.Count >= 3) return;
            if (list.Any(x => string.Equals(x.Item1, t, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, a, StringComparison.OrdinalIgnoreCase))) return;
            list.Add((t, a));
        }
        var cleanTitle = CleanTitle(title);
        var cleanArtist = CleanArtist(artist);

        // "Artist - Song": split on the first spaced dash.
        (string Left, string Right)? split = null;
        var m = System.Text.RegularExpressions.Regex.Match(cleanTitle, @"^(.+?)\s+[-–—]\s+(.+)$");
        if (m.Success) split = (CleanArtist(m.Groups[1].Value), CleanTitle(m.Groups[2].Value));

        var splitFirst = split is { } s && (cleanArtist.Length == 0 || LooksLikeChannel(artist.Trim())
            || s.Left.Contains(cleanArtist, StringComparison.OrdinalIgnoreCase) || cleanArtist.Contains(s.Left, StringComparison.OrdinalIgnoreCase));
        if (splitFirst) Add(split!.Value.Right, split.Value.Left);
        Add(cleanTitle, cleanArtist);
        if (split is { } later) Add(later.Right, later.Left);
        Add(title, artist);
        return list;
    }

    /// <summary>
    /// Which search result to use: the synced one whose length is closest to the track's, within 4 s.
    /// Results without a length are a last resort. -1 when nothing fits.
    /// </summary>
    internal static int Pick(IReadOnlyList<double> durations, double trackSeconds)
    {
        int best = -1;
        var bestScore = double.MaxValue;
        for (var i = 0; i < durations.Count; i++)
        {
            var d = durations[i];
            double score;
            if (trackSeconds <= 0) score = i;                                  // nothing to compare: keep the server's order
            else if (d <= 0) score = 100 + i;                                  // unknown length
            else if (Math.Abs(d - trackSeconds) <= 4) score = Math.Abs(d - trackSeconds);
            else continue;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
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
            Humidity = cur.TryGetProperty("relative_humidity_2m", out var h) && h.ValueKind == JsonValueKind.Number ? (int)Math.Round(h.GetDouble()) : -1,
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
            for (var i = 0; i < dates.Length && i < min.Length && i < max.Length && i < code.Length; i++)
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
            ? a.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue).ToArray()
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
