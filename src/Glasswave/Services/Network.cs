using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Glasswave.Core;

namespace Glasswave.Services;

/// <summary>
/// The ONLY place in Glasswave that can open a network connection.
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

public sealed record WeatherPlace(string Name, string Region, double Lat, double Lon)
{
    public override string ToString() => string.IsNullOrEmpty(Region) ? Name : $"{Name}, {Region}";
}

public sealed record WeatherDay(DateTime Date, double Min, double Max, int Code);

public sealed class WeatherReport
{
    public double Temperature, Feels, Wind;
    public int Code;
    public List<WeatherDay> Days = new();
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
                  "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4" +
                  (p.WeatherFahrenheit ? "&temperature_unit=fahrenheit" : "");
        using var doc = JsonDocument.Parse(await _net.GetStringAsync(url, "Weather: forecast"));
        var report = Parse(doc.RootElement);
        _cache = report;
        return report;
    }

    public void Invalidate() => _cache = null;

    internal static WeatherReport Parse(JsonElement root)
    {
        var cur = root.GetProperty("current");
        var report = new WeatherReport
        {
            Temperature = cur.GetProperty("temperature_2m").GetDouble(),
            Feels = cur.GetProperty("apparent_temperature").GetDouble(),
            Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
            Code = cur.GetProperty("weather_code").GetInt32()
        };
        if (root.TryGetProperty("daily", out var daily))
        {
            var dates = daily.GetProperty("time").EnumerateArray().Select(d => DateTime.Parse(d.GetString()!, CultureInfo.InvariantCulture)).ToArray();
            var max = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(d => d.GetDouble()).ToArray();
            var min = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(d => d.GetDouble()).ToArray();
            var code = daily.GetProperty("weather_code").EnumerateArray().Select(d => d.GetInt32()).ToArray();
            for (var i = 0; i < dates.Length; i++) report.Days.Add(new WeatherDay(dates[i], min[i], max[i], code[i]));
        }
        return report;
    }

    /// <summary>WMO weather code → (description, Segoe Fluent glyph).</summary>
    public static (string Text, string Glyph) Describe(int code) => code switch
    {
        0 => ("Clear", ""),
        1 or 2 => ("Partly cloudy", ""),
        3 => ("Overcast", ""),
        45 or 48 => ("Fog", ""),
        >= 51 and <= 57 => ("Drizzle", ""),
        >= 61 and <= 67 => ("Rain", ""),
        >= 71 and <= 77 => ("Snow", ""),
        >= 80 and <= 82 => ("Showers", ""),
        85 or 86 => ("Snow showers", ""),
        >= 95 => ("Thunderstorm", ""),
        _ => ("Unknown", "")
    };
}
