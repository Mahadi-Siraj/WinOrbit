using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinOrbit.Launcher;

/// <summary>
/// "time in Tokyo", "dhaka time", "what time is it in New York" → the time there, from Windows' own time-zone
/// data (no network). Cities and countries come from a short table, then from the city names Windows lists in
/// each zone's display name ("(UTC+06:00) Dhaka").
/// </summary>
public static class WorldTime
{
    private static readonly (string Zone, string Names)[] Table =
    {
        ("UTC", "utc,gmt,zulu"),
        ("Hawaiian Standard Time", "honolulu,hawaii"),
        ("Alaskan Standard Time", "anchorage,alaska"),
        ("Pacific Standard Time", "los angeles,la,san francisco,seattle,vancouver,las vegas,california,pacific,pst,pdt"),
        ("US Mountain Standard Time", "phoenix,arizona"),
        ("Mountain Standard Time", "denver,calgary,edmonton,mst,mdt"),
        ("Central Standard Time", "chicago,dallas,houston,austin,texas,cst,cdt"),
        ("Central Standard Time (Mexico)", "mexico city,mexico"),
        ("Eastern Standard Time", "new york,nyc,boston,washington,washington dc,miami,atlanta,toronto,montreal,ottawa,est,edt"),
        ("Atlantic Standard Time", "halifax"),
        ("Newfoundland Standard Time", "st johns,newfoundland"),
        ("SA Pacific Standard Time", "bogota,lima,colombia,peru"),
        ("E. South America Standard Time", "sao paulo,rio de janeiro,rio,brasilia,brazil"),
        ("Argentina Standard Time", "buenos aires,argentina"),
        ("Pacific SA Standard Time", "santiago,chile"),
        ("GMT Standard Time", "london,uk,united kingdom,england,britain,scotland,edinburgh,manchester,dublin,ireland,lisbon,portugal"),
        ("W. Europe Standard Time", "berlin,germany,munich,frankfurt,amsterdam,netherlands,rome,italy,milan,vienna,austria,stockholm,sweden,oslo,norway,zurich,switzerland,cet,cest"),
        ("Romance Standard Time", "paris,france,madrid,spain,barcelona,brussels,belgium,copenhagen,denmark"),
        ("Central European Standard Time", "warsaw,poland,zagreb,croatia"),
        ("Central Europe Standard Time", "prague,czechia,budapest,hungary,belgrade,serbia"),
        ("GTB Standard Time", "athens,greece,bucharest,romania"),
        ("FLE Standard Time", "helsinki,finland,kyiv,kiev,ukraine,riga,tallinn,vilnius,sofia,bulgaria"),
        ("Turkey Standard Time", "istanbul,ankara,turkey"),
        ("Russian Standard Time", "moscow,st petersburg,russia"),
        ("Israel Standard Time", "jerusalem,tel aviv,israel"),
        ("Egypt Standard Time", "cairo,egypt"),
        ("South Africa Standard Time", "johannesburg,cape town,south africa"),
        ("W. Central Africa Standard Time", "lagos,nigeria"),
        ("E. Africa Standard Time", "nairobi,kenya,addis ababa,ethiopia"),
        ("Morocco Standard Time", "casablanca,morocco"),
        ("Arab Standard Time", "riyadh,saudi arabia,jeddah,mecca,kuwait,doha,qatar,bahrain"),
        ("Arabian Standard Time", "dubai,abu dhabi,uae,united arab emirates,muscat,oman"),
        ("Iran Standard Time", "tehran,iran"),
        ("Afghanistan Standard Time", "kabul,afghanistan"),
        ("Pakistan Standard Time", "karachi,lahore,islamabad,pakistan"),
        ("India Standard Time", "mumbai,delhi,new delhi,bangalore,bengaluru,kolkata,chennai,hyderabad,india,ist"),
        ("Sri Lanka Standard Time", "colombo,sri lanka"),
        ("Nepal Standard Time", "kathmandu,nepal"),
        ("Bangladesh Standard Time", "dhaka,chittagong,chattogram,sylhet,bangladesh"),
        ("Myanmar Standard Time", "yangon,myanmar"),
        ("SE Asia Standard Time", "bangkok,thailand,jakarta,indonesia,hanoi,ho chi minh,ho chi minh city,saigon,vietnam"),
        ("Singapore Standard Time", "singapore,kuala lumpur,malaysia,manila,philippines"),
        ("China Standard Time", "beijing,shanghai,shenzhen,guangzhou,china,hong kong"),
        ("Taipei Standard Time", "taipei,taiwan"),
        ("Tokyo Standard Time", "tokyo,osaka,kyoto,japan,jst"),
        ("Korea Standard Time", "seoul,korea,south korea"),
        ("W. Australia Standard Time", "perth"),
        ("Cen. Australia Standard Time", "adelaide"),
        ("E. Australia Standard Time", "brisbane"),
        ("AUS Eastern Standard Time", "sydney,melbourne,canberra,australia,aest,aedt"),
        ("New Zealand Standard Time", "auckland,wellington,new zealand"),
    };

    internal static IEnumerable<string> TableZones => Table.Select(t => t.Zone);

    private static readonly Regex[] Forms =
    {
        new(@"^(?:what(?:'s| is)? )?(?:the )?(?:current )?time(?: is it)? in (?<p>.+?)\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^(?<p>.+?) time$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^time (?<p>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    /// <summary>The place asked about, or null when the text isn't a time question.</summary>
    internal static string? Place(string query)
    {
        var q = Regex.Replace(query.Trim(), @"\s+", " ");
        if (q.Length is < 6 or > 60) return null;
        foreach (var re in Forms)
            if (re.Match(q) is { Success: true } m) return m.Groups["p"].Value.Trim();
        return null;
    }

    private static string Key(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return Regex.Replace(b.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>The zone for a city, country or abbreviation ("São Paulo", "sao paulo", "Japan", "PST"), or null.</summary>
    internal static (TimeZoneInfo Zone, string Name)? Zone(string place)
    {
        var key = Key(place);
        if (key.Length < 2) return null;
        foreach (var (zone, names) in Table)
            if (names.Split(',').Any(n => n == key))
                try { return (TimeZoneInfo.FindSystemTimeZoneById(zone), Title(place, key)); } catch { }
        // Windows' own list: "(UTC+06:00) Dhaka", "(UTC+03:00) Kuwait, Riyadh".
        if (key.Length < 4) return null;
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            var close = tz.DisplayName.IndexOf(')');
            var cities = close >= 0 ? tz.DisplayName[(close + 1)..] : tz.DisplayName;
            foreach (var c in cities.Split(','))
                if (Key(c) == key) return (tz, c.Trim());
        }
        return null;
    }

    private static string Title(string typed, string key) =>
        key.Length <= 4 && !key.Contains(' ') ? typed.Trim().ToUpperInvariant() : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(typed.Trim().ToLower(CultureInfo.CurrentCulture));

    /// <summary>"3 h ahead of you", "5 h 30 min behind you", "same time as you".</summary>
    internal static string Relative(TimeSpan diff)
    {
        if (diff == TimeSpan.Zero) return "same time as you";
        var a = diff.Duration();
        var text = a.Minutes == 0 ? $"{(int)a.TotalHours} h" : (int)a.TotalHours == 0 ? $"{a.Minutes} min" : $"{(int)a.TotalHours} h {a.Minutes} min";
        return text + (diff > TimeSpan.Zero ? " ahead of you" : " behind you");
    }

    internal static LauncherItem? Item(string query)
    {
        if (Place(query) is not { } place || Zone(place) is not { } found) return null;
        var utc = DateTime.UtcNow;
        var there = TimeZoneInfo.ConvertTimeFromUtc(utc, found.Zone);
        var diff = found.Zone.GetUtcOffset(utc) - TimeZoneInfo.Local.GetUtcOffset(utc);
        var time = Services.Fmt.Time(there, Hub.Settings.General.Use24Hour);
        var day = there.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        var copy = $"{time} in {found.Name} ({day})";
        return new LauncherItem
        {
            Kind = ItemKind.Calc, Key = "time", Title = $"{time} in {found.Name}", Subtitle = $"{day} · {Relative(diff)} · Enter copies", Glyph = Design.Glyph.Globe, Score = 10_000,
            Run = () => System.Windows.Clipboard.SetText(copy)
        };
    }
}
