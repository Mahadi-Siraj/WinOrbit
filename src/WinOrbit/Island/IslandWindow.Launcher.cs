using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;

namespace WinOrbit.Island;

/// <summary>
/// The launcher: every module as an app-style tile — a glossy squircle in the module's own colours, its name, and
/// one live line (the song playing, the focus countdown, today's date…). Tiles arrive one after another on springs,
/// lift under the pointer and squash when pressed; clicking one opens that module.
/// </summary>
public partial class IslandWindow
{
    public const string HomeTab = "home";
    private readonly Dictionary<string, (TextBlock Sub, Border Icon, TextBlock Glyph)> _homeTiles = new();

    /// <summary>Each module's tile colours (top → bottom of the gradient).</summary>
    internal static (Color Top, Color Bottom) TileColors(string id) => id switch
    {
        "media" => (Rgb(0xFF, 0x6B, 0x8B), Rgb(0xE5, 0x1E, 0x55)),
        "system" => (Rgb(0x3E, 0xE0, 0xB0), Rgb(0x0E, 0x9F, 0xD8)),
        "controls" => (Rgb(0x81, 0x8C, 0xF8), Rgb(0x43, 0x38, 0xCA)),
        "focus" => (Rgb(0xFF, 0xB2, 0x3E), Rgb(0xFF, 0x6A, 0x00)),
        "calendar" => (Rgb(0xFF, 0x6B, 0x61), Rgb(0xD7, 0x16, 0x2A)),
        "notes" => (Rgb(0xFF, 0xE0, 0x5C), Rgb(0xF5, 0xA6, 0x00)),
        "clipboard" => (Rgb(0xD0, 0x7B, 0xFF), Rgb(0x8E, 0x3F, 0xD8)),
        "weather" => (Rgb(0x7D, 0xD8, 0xFF), Rgb(0x0A, 0x7C, 0xF0)),
        "shelf" => (Rgb(0x6E, 0xE7, 0x9B), Rgb(0x16, 0xA3, 0x4A)),
        _ => (Rgb(0x9C, 0xA3, 0xAF), Rgb(0x4B, 0x55, 0x63)),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static string ShortName(string id) => id switch { "controls" => "Controls", "focus" => "Focus", "shelf" => "Shelf", _ => AllTabs.First(t => t.Id == id).Name };

    private void BuildHome(List<string> modules)
    {
        TabHome.Children.Clear();
        _homeTiles.Clear();
        // Never more than two rows, so the grid always fits the panel.
        TabHome.Columns = modules.Count <= 3 ? Math.Max(1, modules.Count) : modules.Count <= 6 ? 3 : (modules.Count + 1) / 2;
        foreach (var id in modules) TabHome.Children.Add(HomeTile(id));
    }

    private FrameworkElement HomeTile(string id)
    {
        var (top, bottom) = TileColors(id);
        var fill = new LinearGradientBrush(top, bottom, 90); fill.Freeze();
        var icon = new Border { CornerRadius = new CornerRadius(16), Background = fill };
        // A soft glow in the tile's own colour beneath it, like light passing through tinted glass.
        icon.Effect = new DropShadowEffect { Color = bottom, BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.45, RenderingBias = RenderingBias.Performance };
        var gloss = new Border
        {
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), IsHitTestVisible = false,
            BorderBrush = Frozen(new LinearGradientBrush(Color.FromArgb(0x80, 255, 255, 255), Color.FromArgb(0x10, 255, 255, 255), 90)),
            Background = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x38, 255, 255, 255), 0), new(Color.FromArgb(0x00, 255, 255, 255), 0.55)
            }, 90)),
        };
        var glyph = new TextBlock
        {
            Text = AllTabs.First(t => t.Id == id).Glyph, FontFamily = (FontFamily)FindResource("F.Icons"), FontSize = 22,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new Grid { Width = 54, Height = 54, HorizontalAlignment = HorizontalAlignment.Center };
        face.Children.Add(icon); face.Children.Add(glyph); face.Children.Add(gloss);

        var name = new TextBlock { Text = ShortName(id), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var sub = new TextBlock { FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 98, Margin = new Thickness(0, 1, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "T.Text2");

        var stack = new StackPanel();
        stack.Children.Add(face); stack.Children.Add(name); stack.Children.Add(sub);

        var scale = new ScaleTransform(1, 1);
        var button = new Button { Content = stack, Style = (Style)FindResource("TileButton"), Tag = id, ToolTip = AllTabs.First(t => t.Id == id).Name };
        System.Windows.Automation.AutomationProperties.SetName(button, ShortName(id));
        face.RenderTransformOrigin = new Point(0.5, 0.5);
        face.RenderTransform = scale;
        // Lift under the pointer, squash on press, spring back — the same physical feel as the island itself.
        button.MouseEnter += (_, _) => { Springs.To(scale, ScaleTransform.ScaleXProperty, 1.08, 0.3, 0.6); Springs.To(scale, ScaleTransform.ScaleYProperty, 1.08, 0.3, 0.6); };
        button.MouseLeave += (_, _) => { Springs.To(scale, ScaleTransform.ScaleXProperty, 1, 0.34, 0.65); Springs.To(scale, ScaleTransform.ScaleYProperty, 1, 0.34, 0.65); };
        button.PreviewMouseLeftButtonDown += (_, _) => { Springs.To(scale, ScaleTransform.ScaleXProperty, 0.9, 0.16, 1); Springs.To(scale, ScaleTransform.ScaleYProperty, 0.9, 0.16, 1); };
        button.Click += (_, _) => { Poke(); ShowTab(id); };
        _homeTiles[id] = (sub, icon, glyph);
        return button;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Fill in each tile's live line (and the calendar's date, the media tile's artwork).</summary>
    private void RefreshHome()
    {
        foreach (var (id, (sub, icon, glyph)) in _homeTiles)
        {
            switch (id)
            {
                case "media":
                    var m = Hub.Media;
                    sub.Text = m.HasTrack ? m.DisplayTitle : "Nothing playing";
                    if (m.Art is { } art)
                    {
                        icon.Background = Frozen(new ImageBrush(art) { Stretch = Stretch.UniformToFill });
                        glyph.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        var (t, b) = TileColors(id);
                        icon.Background = Frozen(new LinearGradientBrush(t, b, 90));
                        glyph.Visibility = Visibility.Visible;
                    }
                    break;
                case "system": sub.Text = "CPU · memory · GPU"; break;
                case "controls": sub.Text = "Wi-Fi, sound, light"; break;
                case "focus": sub.Text = Hub.Timers.IsActive ? Hub.Timers.Display : "Start a focus block"; break;
                case "calendar":
                    // The tile itself shows today's date, like a desk calendar.
                    glyph.FontFamily = (FontFamily)FindResource("F.Family");
                    glyph.FontWeight = FontWeights.Bold;
                    glyph.FontSize = 24;
                    glyph.Text = DateTime.Now.Day.ToString();
                    sub.Text = NextToday(Events, DateTime.Now) is { Start: { } at } next
                        ? $"{Fmt.Time(at, Hub.Settings.General.Use24Hour)} {next.Title}"
                        : DateTime.Now.ToString("dddd");
                    break;
                case "weather":
                    if (!Hub.Weather.Enabled) { sub.Text = "Off · set up"; ResetGlyph(glyph, Glyph.Cloud); break; }
                    if (Hub.Weather.Last is { } wx)
                    {
                        // The tile shows the temperature, like the calendar shows the date.
                        glyph.FontFamily = (FontFamily)FindResource("F.Family");
                        glyph.FontWeight = FontWeights.Bold;
                        glyph.FontSize = 20;
                        glyph.Text = $"{wx.Temperature:0}°";
                        sub.Text = WeatherService.Describe(wx.Code, wx.IsDay).Text;
                    }
                    else
                    {
                        sub.Text = Hub.Settings.Privacy.WeatherPlace;
                        ResetGlyph(glyph, Glyph.Cloud);
                        _ = LoadWeatherForTile();
                    }
                    break;
                case "notes":
                    var note = Hub.Store.Data.Notes.TryGetValue(UserData.IslandNoteKey, out var n) ? n : "";
                    var first = note.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                    sub.Text = first ?? "Empty";
                    break;
                case "clipboard":
                    var count = Hub.Clipboard.Items.Count;
                    sub.Text = !S.ClipboardHistory ? "History off" : count == 0 ? "Nothing copied" : count == 1 ? "1 item" : $"{count} items";
                    break;
                case "shelf": sub.Text = Shelf.Count == 0 ? "Drop files here" : Shelf.Count == 1 ? "1 file" : $"{Shelf.Count} files"; break;
            }
        }
    }

    private void ResetGlyph(TextBlock glyph, string icon)
    {
        glyph.FontFamily = (FontFamily)FindResource("F.Icons");
        glyph.FontWeight = FontWeights.Normal;
        glyph.FontSize = 22;
        glyph.Text = icon;
    }

    /// <summary>First look at the launcher since starting: fetch once (the shared 30-minute cache) and fill the tile in.</summary>
    private async Task LoadWeatherForTile()
    {
        try
        {
            if (await Hub.Weather.GetAsync() is not null && _expanded && _tab == HomeTab) RefreshHome();
        }
        catch { /* the tile keeps the place name; the weather page explains any failure */ }
    }

    /// <summary>Tiles arrive one after another, each popping in on a spring — a short ripple across the grid.</summary>
    private async void PopTiles()
    {
        var tiles = TabHome.Children.OfType<FrameworkElement>().ToList();
        foreach (var t in tiles) t.Opacity = Motion.Intensity > 0.01 ? 0 : 1;
        if (Motion.Intensity <= 0.01) return;
        foreach (var t in tiles)
        {
            if (!_expanded || _tab != HomeTab) { t.Opacity = 1; continue; }
            Pop(t, 0.7, 0.5, response: 0.4, damping: 0.62);
            await Task.Delay(28);
        }
    }

    /// <summary>Open the launcher (Home or 0 on the keyboard, or the grid button).</summary>
    private void ShowHome() => ShowTab(HomeTab);
}
