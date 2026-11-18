using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.Wallpaper;

namespace WinOrbit.Island;

/// <summary>
/// The weather page: now (big temperature, condition, feels-like, wind, humidity, the next sunrise or sunset), the
/// next twelve hours and the next three days. While it is open the glass takes on the sky's colours — blue for a
/// clear day, slate for rain, deep indigo at night. Weather needs the internet, so like the widget it stays off until
/// it is switched on (with a place) in Settings › Privacy; it shares the widget's 30-minute cache.
/// </summary>
public partial class IslandWindow
{
    private Brush? _skyWash;
    private string _skyKey = "";

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Color RainBlue = Color.FromRgb(0x5A, 0xA9, 0xFF);

    /// <summary>Rain (or snow) is likely soon while it's dry now: a card, once per spell.</summary>
    private void OnRainSoon(int minutes, int chance, string what)
    {
        if (!S.RainAlerts) return;
        var place = Hub.Settings.Privacy.WeatherPlace;
        var detail = chance >= 0 ? $"{chance}% chance" : "Likely";
        if (!string.IsNullOrWhiteSpace(place)) detail += " · " + place.Split(',')[0];
        ShowCard(Glyph.Cloud, RainAlerts.Title(minutes, what), detail, RainBlue, 6,
            onClick: S.Modules.Contains("weather") ? () => { Expand(false); ShowTab("weather"); } : null);
    }

    private async void ShowWeather(bool force = false)
    {
        if (!Hub.Weather.Enabled)
        {
            WeatherOff();
            SetSky(null);
            return;
        }
        var shown = Hub.Weather.Last;
        if (shown is not null) RenderWeather(shown);
        else WeatherMessage(LineIcons.CloudSun, "Getting the weather…", Hub.Settings.Privacy.WeatherPlace);
        try
        {
            var r = await Hub.Weather.GetAsync(force);
            if (r is null || ReferenceEquals(r, shown) || !_expanded || _tab != "weather") return;
            RenderWeather(r);
        }
        catch (Exception ex)
        {
            if (shown is null && _tab == "weather") WeatherMessage(LineIcons.Cloud, "Weather unavailable", ex.Message);
        }
    }

    private void WeatherOff()
    {
        var sp = WeatherMessage(LineIcons.CloudSun, "Weather is off",
            "It needs the internet, so it stays off until you turn it on and choose a place. Only that place's coordinates are sent, to Open-Meteo (no account).");
        var b = Ui.Button("Set up weather", () => { Collapse(); App.Current.ShowMain("settings"); });
        b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 12, 0, 0); b.FontSize = 12;
        sp.Children.Add(b);
    }

    private StackPanel WeatherMessage(string icon, string title, string detail)
    {
        TabWeather.Children.Clear();
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 330 };
        sp.Children.Add(Ui.LineIcon(icon, 34, "T.Text3", 1.5));
        sp.Children.Add(Ui.Text(title, "Text.Sub", 15).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 8, 0, 3); }));
        sp.Children.Add(Ui.Text(detail, "Text.Caption", 12).With(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; }));
        TabWeather.Children.Add(sp);
        return sp;
    }

    private void RenderWeather(WeatherReport r)
    {
        var p = Hub.Settings.Privacy;
        var h24 = Hub.Settings.General.Use24Hour;
        var ci = CultureInfo.CurrentCulture;
        var wind = p.WeatherFahrenheit ? "mph" : "km/h";
        var (text, icon) = WeatherService.Describe(r.Code, r.IsDay);
        TabWeather.Children.Clear();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- now
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.LineIcon(icon, 50, Ink(r.Code, r.IsDay), 1.4).With(v => { v.VerticalAlignment = VerticalAlignment.Center; v.Margin = new Thickness(2, 0, 14, 0); }));

        var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        now.Children.Add(Ui.Text(p.WeatherPlace.ToUpper(ci), "Text.Label", 11).With(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        now.Children.Add(new TextBlock { Text = $"{r.Temperature:0}°", FontSize = 40, FontWeight = FontWeights.SemiBold, Margin = new Thickness(-2, -4, 0, -4) });
        now.Children.Add(Ui.Text($"{text} · feels {r.Feels:0}°", "Text.Body", 12.5));
        var details = new List<string>();
        if (r.Days.Count > 0) details.Add($"H {r.Days[0].Max:0}°  L {r.Days[0].Min:0}°");
        details.Add($"{r.Wind:0} {wind}");
        if (r.Humidity >= 0) details.Add($"{r.Humidity}%");
        if (SunEvent(r) is { } sun) details.Add(sun);
        now.Children.Add(Ui.Text(string.Join(" · ", details), "Text.Caption", 11.5).With(t => { t.Margin = new Thickness(0, 2, 0, 0); t.TextTrimming = TextTrimming.CharacterEllipsis; }));
        Grid.SetColumn(now, 1);
        top.Children.Add(now);

        // ---- next three days
        var days = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var d in r.Days.Skip(1).Take(3))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Ui.Text(d.Date.ToString("ddd", ci), "Text.Caption", 11.5, FontWeights.SemiBold, "T.Text2").With(t => t.VerticalAlignment = VerticalAlignment.Center));
            row.Children.Add(Ui.LineIcon(WeatherService.Describe(d.Code).Icon, 16, Ink(d.Code, true), 1.7).With(v => { Grid.SetColumn(v, 1); v.HorizontalAlignment = HorizontalAlignment.Left; }));
            var temps = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            temps.Inlines.Add(new Run($"{d.Max:0}°") { FontWeight = FontWeights.SemiBold });
            temps.Inlines.Add(new Run($"  {d.Min:0}°").With(run => run.SetResourceReference(TextElement.ForegroundProperty, "T.Text3")));
            Grid.SetColumn(temps, 2);
            row.Children.Add(temps);
            days.Children.Add(row);
        }
        Grid.SetColumn(days, 2);
        top.Children.Add(days);
        root.Children.Add(top);

        // ---- the next twelve hours, every other hour
        var hours = r.Hours.Skip(1).Where((_, i) => i % 2 == 0).Take(6).ToList();
        if (hours.Count > 0)
        {
            var strip = new UniformGrid { Rows = 1, Columns = hours.Count };
            foreach (var h in hours)
            {
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                cell.Children.Add(Ui.Text(h.Time.ToString(h24 ? "HH:mm" : "h tt", ci), "Text.Caption", 11).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
                cell.Children.Add(Ui.LineIcon(WeatherService.Describe(h.Code, h.IsDay).Icon, 18, Ink(h.Code, h.IsDay), 1.7).With(v => { v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = new Thickness(0, 4, 0, 3); }));
                cell.Children.Add(new TextBlock { Text = $"{h.Temperature:0}°", FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                // Rain chance only where it matters, in rain blue; an empty line keeps the cells level.
                var rain = new TextBlock { Text = h.RainChance >= 30 ? $"{h.RainChance}%" : " ", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
                rain.Foreground = Frozen(new SolidColorBrush(ColorMath.ForMode(Color.FromRgb(0x5A, 0xA9, 0xFF), Hub.Theme.IsDark)));
                cell.Children.Add(rain);
                strip.Children.Add(cell);
            }
            var card = new Border { Child = strip, Padding = new Thickness(6, 7, 6, 4), Margin = new Thickness(0, 10, 0, 0) };
            card.SetResourceReference(StyleProperty, "Card.Inset");
            Grid.SetRow(card, 2);
            root.Children.Add(card);
        }
        TabWeather.Children.Add(root);
        if (Motion.Intensity > 0.01) Motion.Enter(root, 6, 200);

        SetSky(r);
    }

    /// <summary>"Sunset 16:07" by day, "Sunrise 07:25" by night (in the place's own time).</summary>
    private static string? SunEvent(WeatherReport r)
    {
        var h24 = Hub.Settings.General.Use24Hour;
        var placeNow = r.Hours.Count > 0 ? r.Hours[0].Time : DateTime.Now;
        if (r.IsDay) return r.Days.FirstOrDefault()?.Sunset is { } set ? "Sunset " + Fmt.Time(set, h24) : null;
        var rise = r.Days.Select(d => d.Sunrise).FirstOrDefault(s => s is { } v && v > placeNow);
        return rise is { } v2 ? "Sunrise " + Fmt.Time(v2, h24) : null;
    }

    /// <summary>Each condition's icon colour: warm sun, pale moon, grey cloud, blue rain, icy snow, violet storm.</summary>
    private static Brush Ink(int code, bool day)
    {
        var c = code switch
        {
            0 or 1 => day ? Color.FromRgb(0xFF, 0xC2, 0x3D) : Color.FromRgb(0xC7, 0xD2, 0xFE),
            2 => day ? Color.FromRgb(0xFF, 0xCE, 0x6B) : Color.FromRgb(0xB4, 0xC0, 0xE0),
            >= 51 and <= 67 or >= 80 and <= 82 => Color.FromRgb(0x5A, 0xA9, 0xFF),
            >= 71 and <= 77 or 85 or 86 => Color.FromRgb(0x9F, 0xD8, 0xFF),
            >= 95 => Color.FromRgb(0xB7, 0x94, 0xF6),
            _ => Color.FromRgb(0xA9, 0xB8, 0xCC),
        };
        return Frozen(new SolidColorBrush(ColorMath.ForMode(c, Hub.Theme.IsDark)));
    }

    /// <summary>The sky behind the glass for a condition: (top-left light, middle, far corner).</summary>
    internal static (Color A, Color B, Color C) SkyColors(int code, bool day) => (code, day) switch
    {
        (0 or 1, true) => (Color.FromRgb(0x3B, 0x9E, 0xFF), Color.FromRgb(0x7F, 0xD4, 0xFF), Color.FromRgb(0xFF, 0xD5, 0x80)),
        (2, true) => (Color.FromRgb(0x4A, 0x90, 0xE2), Color.FromRgb(0x9F, 0xC5, 0xE8), Color.FromRgb(0xD6, 0xE2, 0xEE)),
        (>= 95, _) => (Color.FromRgb(0x7E, 0x5B, 0xC9), Color.FromRgb(0x3E, 0x36, 0x7A), Color.FromRgb(0x5B, 0x3F, 0x9E)),
        (>= 51 and <= 67 or >= 80 and <= 82, _) => (Color.FromRgb(0x3A, 0x6E, 0xA5), Color.FromRgb(0x5D, 0x8A, 0xB8), Color.FromRgb(0x2C, 0x4A, 0x6E)),
        (>= 71 and <= 77 or 85 or 86, _) => (Color.FromRgb(0xB8, 0xD8, 0xF5), Color.FromRgb(0xE6, 0xF0, 0xFA), Color.FromRgb(0x9E, 0xC1, 0xE6)),
        (0 or 1, false) => (Color.FromRgb(0x3B, 0x2F, 0x8F), Color.FromRgb(0x1E, 0x2A, 0x78), Color.FromRgb(0x0F, 0x17, 0x4A)),
        (_, false) => (Color.FromRgb(0x3D, 0x45, 0x66), Color.FromRgb(0x2A, 0x33, 0x55), Color.FromRgb(0x1A, 0x20, 0x38)),
        _ => (Color.FromRgb(0x8A, 0x9B, 0xB0), Color.FromRgb(0xA0, 0xAE, 0xC0), Color.FromRgb(0x6B, 0x7A, 0x8F)),
    };

    private void SetSky(WeatherReport? r)
    {
        if (r is null) { _skyWash = null; _skyKey = ""; }
        else
        {
            var (a, b, c) = SkyColors(r.Code, r.IsDay);
            _skyWash = WashBrush(a, b, c);
            _skyKey = $"sky:{r.Code}:{r.IsDay}";
        }
        UpdateArtWash();
    }
}
