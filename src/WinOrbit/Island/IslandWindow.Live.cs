using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.Shell.Pages;
using WinOrbit.Wallpaper;

namespace WinOrbit.Island;

/// <summary>
/// Live activities: volume HUD, microphone/camera privacy dot, audio-reactive bars tinted from album art,
/// synced lyrics, clipboard history and the file shelf. Everything is event driven; the only timer that
/// runs continuously is the 20 fps bar meter, and only while music plays in a visible capsule.
/// </summary>
public partial class IslandWindow
{
    private const string GlyphMic = "", GlyphCam = "", GlyphShelf = "";
    private DispatcherTimer? _hudTimer, _levelTimer, _lyricTimer;
    private readonly double[] _levels = { 0.3, 0.3, 0.3 };
    private readonly Random _rng = new();
    private List<LyricLine>? _lyrics;
    private string _lyricKey = "", _artKey = "";
    private int _lyricIndex = -1;
    private bool _privShown;
    private static readonly List<string> Shelf = new();
    private static event Action? ShelfChanged;

    private void InitLive()
    {
        _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); EndHud(); };
        _levelTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _levelTimer.Tick += (_, _) => TickLevels();
        _lyricTimer = new DispatcherTimer();
        _lyricTimer.Tick += (_, _) => { _lyricTimer.Stop(); UpdateLyric(); };

        Hub.Audio.VolumeChanged += OnVolumeEvent;
        Hub.Privacy.Changed += UpdatePrivacyDot;
        Hub.Clipboard.Changed += OnClipboardChanged;
        Hub.Media.Changed += OnMediaForLive;
        ShelfChanged += OnShelfChanged;
        Hub.Power.PowerSourceChanged += OnPowerSource;
        Hub.Power.BatteryChanged += OnBattery;
        Hub.Power.BrightnessChanged += OnBrightness;
        Hub.Power.EnergySaverChanged += OnEnergySaver;
        Hub.Media.Hint += OnMediaHint;
        Hub.Devices.LockChanged += OnLockKey;
        Hub.Devices.NetworkChanged += OnNetwork;
        Hub.Devices.BluetoothChanged += OnBluetooth;
        Hub.Devices.BluetoothBattery += OnBluetoothBattery;
        Hub.Devices.UsbChanged += OnUsb;
        Hub.Devices.RadioChanged += OnRadio;
        Hub.Devices.ModeChanged += OnMode;

        InitFiles();
        InitCall();
        InitSplit();
        Hub.Island.Rain.RainSoon += OnRainSoon;
        Hub.Island.Language.Changed += OnLanguage;
        Pill.AllowDrop = true;
        Pill.DragEnter += OnDragEnter;
        Pill.DragOver += OnDragOver;
        Pill.Drop += OnDrop;
        UpdatePrivacyDot();
        OnMediaForLive();
    }

    private void CleanupLive()
    {
        foreach (var t in new[] { _hudTimer, _levelTimer, _lyricTimer, _callTimer }) t?.Stop();
        Hub.Audio.VolumeChanged -= OnVolumeEvent;
        Hub.Privacy.Changed -= UpdatePrivacyDot;
        Hub.Clipboard.Changed -= OnClipboardChanged;
        Hub.Media.Changed -= OnMediaForLive;
        ShelfChanged -= OnShelfChanged;
        Hub.Power.PowerSourceChanged -= OnPowerSource;
        Hub.Power.BatteryChanged -= OnBattery;
        Hub.Power.BrightnessChanged -= OnBrightness;
        Hub.Power.EnergySaverChanged -= OnEnergySaver;
        Hub.Media.Hint -= OnMediaHint;
        Hub.Devices.LockChanged -= OnLockKey;
        Hub.Devices.NetworkChanged -= OnNetwork;
        Hub.Devices.BluetoothChanged -= OnBluetooth;
        Hub.Devices.BluetoothBattery -= OnBluetoothBattery;
        Hub.Devices.UsbChanged -= OnUsb;
        Hub.Devices.RadioChanged -= OnRadio;
        Hub.Devices.ModeChanged -= OnMode;
        CleanupFiles();
        Hub.Island.Rain.RainSoon -= OnRainSoon;
        Hub.Island.Language.Changed -= OnLanguage;
    }

    /// <summary>Preview with this PC's current keyboard.</summary>
    private void OnLanguageDemo()
    {
        var (code, language, layout) = KeyboardLanguage.Describe(Interop.Native.GetKeyboardLayout(0).ToInt64());
        ShowCard("", language, layout.Length > 0 ? layout + " keyboard" : "Keyboard", AccentColor(), 2.4, cap: code.Length <= 3 ? code : code[..3]);
    }

    /// <summary>The keyboard language switched: a key cap with its code, like the lock keys.</summary>
    private void OnLanguage(string code, string language, string layout)
    {
        if (!S.LanguageAlerts) return;
        if (PillBusy || SatelliteShowing) { ShowSatellite(null, code.Length <= 3 ? code : code[..3], true, language, null, 1.8); return; }
        ShowCard("", language, layout.Length > 0 ? layout + " keyboard" : "Keyboard", AccentColor(), 1.8, cap: code.Length <= 3 ? code : code[..3]);
    }

    // ------------------------------------------------------------------ keys and connections
    private static readonly Color Connected = Color.FromRgb(0x30, 0xD1, 0x58), Dropped = Color.FromRgb(0xFF, 0x9F, 0x0A), BtBlue = Color.FromRgb(0x0A, 0x84, 0xFF);
    private const string GlyphWifi = "", GlyphWifiOff = "", GlyphEthernet = "", GlyphBluetooth = "",
        GlyphHeadphones = "", GlyphKeyboard = "", GlyphPhone = "", GlyphUsb = "";

    private static readonly Color Neutral = Color.FromRgb(0xA1, 0xA1, 0xAA), KeyLit = Color.FromRgb(0xF4, 0xF4, 0xF5);

    private Color AccentColor() => (TryFindResource("T.Accent") as SolidColorBrush)?.Color ?? BtBlue;

    private void OnLockKey(DeviceEvents.LockKey key, bool on)
    {
        if (!S.LockKeyAlerts) return;
        var (cap, name) = key switch
        {
            DeviceEvents.LockKey.Caps => (on ? "A" : "a", "Caps Lock"),
            DeviceEvents.LockKey.Num => ("1", "Num Lock"),
            _ => ("S", "Scroll Lock")
        };
        // While music, a call or a timer is showing, the key splits off in a side bubble instead of covering it.
        if (PillBusy || SatelliteShowing) { ShowSatellite(null, cap, on, $"{name} {(on ? "on" : "off")}", null, 1.8); return; }
        ShowCard("", name, on ? "On" : "Off", on ? KeyLit : Neutral, 1.8, toggle: on, cap: cap, switchColor: Connected);
    }

    private void OnNetwork(DeviceEvents.NetKind kind, string name, bool connected)
    {
        if (!S.NetworkAlerts) return;
        var wifi = kind == DeviceEvents.NetKind.WiFi;
        var label = wifi ? "Wi-Fi" : kind == DeviceEvents.NetKind.Ethernet ? "Ethernet" : "Network";
        var hasName = !string.IsNullOrWhiteSpace(name);
        if (connected)
            ShowCard(wifi ? GlyphWifi : GlyphEthernet, hasName ? name : label, $"{label} connected", Connected, 3);
        else
            ShowCard(wifi ? GlyphWifiOff : GlyphEthernet, $"{label} disconnected", hasName ? name : "", Dropped, 3);
    }

    private void OnRadio(Windows.Devices.Radios.RadioKind kind, bool on)
    {
        var wifi = kind == Windows.Devices.Radios.RadioKind.WiFi;
        if (wifi ? !S.NetworkAlerts : !S.BluetoothAlerts) return;
        var glyph = wifi ? (on ? GlyphWifi : GlyphWifiOff) : GlyphBluetooth;
        ShowCard(glyph, wifi ? "Wi-Fi" : "Bluetooth", on ? "On" : "Off", on ? BtBlue : Neutral, 2.2, toggle: on, switchColor: BtBlue);
    }

    private static readonly Color Indigo = Color.FromRgb(0x8E, 0x8C, 0xFF), Sunny = Color.FromRgb(0xFF, 0xC9, 0x4A),
        Amber = Color.FromRgb(0xFF, 0x9F, 0x43), Flight = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private const string GlyphAirplane = "";

    private void OnMode(string mode, bool on)
    {
        if (!S.SystemAlerts) return;
        switch (mode)
        {
            case "dark":
                if (on) ShowCard(Glyph.Moon, "Dark mode", "Apps and Windows", Indigo, 2.4, toggle: true);
                else ShowCard(Glyph.Brightness, "Light mode", "Apps and Windows", Sunny, 2.4, toggle: true);
                break;
            case "night":
                ShowCard(Glyph.Moon, "Night light", on ? "On · warmer colours" : "Off", on ? Amber : Neutral, 2.4, toggle: on, switchColor: Amber);
                break;
            case "airplane":
                ShowCard(GlyphAirplane, "Airplane mode", on ? "On · radios off" : "Off", on ? Flight : Neutral, 2.6, toggle: on, switchColor: Flight);
                break;
        }
    }

    private (string Name, string Kind)? _btCard;

    private void OnBluetooth(string name, string kind, bool connected) => OnBluetooth(name, kind, connected, false);

    private void OnBluetooth(string name, string kind, bool connected, bool force)
    {
        if (!S.BluetoothAlerts && !force) return;
        ShowCard(BtGlyph(kind), name, connected ? "Connected" : "Disconnected", connected ? BtBlue : Dropped, 3);
        _btCard = connected && _card ? (name, kind) : null;
    }

    private static string BtGlyph(string kind) => kind switch { "audio" => GlyphHeadphones, "input" => GlyphKeyboard, "phone" => GlyphPhone, _ => GlyphBluetooth };

    /// <summary>The battery arrives a moment after "Connected": add it to that card while it is still showing.</summary>
    private void OnBluetoothBattery(string name, int percent)
    {
        if (_btCard is not { } c || c.Name != name || !_card || CardTitle.Text != name) return;
        ShowCard(BtGlyph(c.Kind), name, BatteryLine(percent), percent <= 20 ? Low : BtBlue, 3.2);
    }

    internal static string BatteryLine(int percent) => percent <= 20 ? $"Connected · battery low, {percent}%" : $"Connected · {percent}% battery";

    private void OnUsb(string name, bool pluggedIn)
    {
        if (!S.UsbAlerts) return;
        ShowCard(GlyphUsb, name, pluggedIn ? "Connected" : "Removed", pluggedIn ? AccentColor() : Dropped, 2.6);
    }

    private void OnMediaHint(string message)
    {
        // One short line; the full how-to sits in the tooltip.
        var parts = message.Split(':', 2);
        var shortText = parts[0].Trim();
        if (_expanded && _tab == "media")
        {
            MLyric.Text = shortText;
            MLyric.ToolTip = message;
            MLyric.TextWrapping = TextWrapping.NoWrap;
            MLyric.Visibility = Visibility.Visible;
            return;
        }
        Peek(Glyph.Info, shortText, 5);
    }

    // ------------------------------------------------------------------ power: charging, battery, brightness
    private int? _lastBattery;

    private static readonly Color Charging = Color.FromRgb(0x30, 0xD1, 0x58), Low = Color.FromRgb(0xFF, 0x45, 0x3A), Saver = Color.FromRgb(0xFF, 0xC4, 0x0A);
    private const string GlyphCharging = "", GlyphLeaf = "";

    /// <summary>Plugged in: a green meter fills up to the charge level, like a phone. Unplugged: a quick note.</summary>
    public void OnPowerSource(bool onAc)
    {
        if (!S.BatteryAlerts || _expanded || !IsVisible) return;
        var pct = Hub.Power.Battery ?? 100;
        if (onAc) ShowHud(GlyphCharging, pct / 100.0, $"Charging · {pct}%", Charging, 3.2, fillFromEmpty: true);
        else ShowHud(Glyph.Battery, pct / 100.0, $"On battery · {pct}%", null, 2.2);
    }

    public void OnBattery(int percent)
    {
        var previous = _lastBattery ?? percent + 1;
        _lastBattery = percent;
        if (!S.BatteryAlerts || Hub.Power.OnAc == true || _expanded || !IsVisible) return;
        if (PowerEvents.LowBatteryWarning(previous, percent) is { } level)
            ShowHud(Glyph.Warning, percent / 100.0, level <= 10 ? $"Very low · {percent}%" : $"Battery low · {percent}%", Low, 6);
    }

    private void OnEnergySaver(int state)
    {
        if (!S.BatteryAlerts || _expanded || !IsVisible) return;
        var on = state > 0;
        ShowCard(GlyphLeaf, "Energy saver", on ? (state == 2 ? "On · extra savings" : "On") : "Off", on ? Saver : Neutral, 2.4, toggle: on, switchColor: Saver);
        if (_expanded && _tab == "controls") RefreshQuick();
    }

    /// <summary>Show a live activity on demand (Island settings › Preview), so you can see what each looks like.</summary>
    public void Demo(string what)
    {
        if (_expanded) Collapse(true);
        switch (what)
        {
            case "charging": ShowHud(GlyphCharging, 0.64, "Charging · 64%", Charging, 3.2, fillFromEmpty: true); break;
            case "low": ShowHud(Glyph.Warning, 0.18, "Battery low · 18%", Low, 4); break;
            case "volume": ShowHud(Glyph.Volume, 0.42, "42"); break;
            case "mic": Peek(GlyphMic, "Teams is using your microphone", 3); break;
            case "call": DemoCall(); break;
            case "split": DemoSplit(); break;
            case "saver": ShowCard(GlyphLeaf, "Energy saver", "On", Saver, 2.6, toggle: true); break;
            case "dark": ShowCard(Glyph.Moon, "Dark mode", "Apps and Windows", Indigo, 2.6, toggle: true); break;
            case "song":
                if (Hub.Media.HasTrack) ShowMediaCard();
                else ShowSampleSong();
                break;
            case "event":
                var at = DateTime.Today.AddHours(DateTime.Now.Hour + 1);
                ShowEventCard(new CalendarEvent { Date = at.Date, Time = at.TimeOfDay, Title = "Stand-up" }, at.AddMinutes(-10));
                break;
            case "night": ShowCard(Glyph.Moon, "Night light", "On · warmer colours", Amber, 2.6, toggle: true); break;
            case "airplane": ShowCard(GlyphAirplane, "Airplane mode", "On · radios off", Flight, 2.6, toggle: true); break;
            case "caps": ShowCard("", "Caps Lock", "On", KeyLit, 2.4, toggle: true, cap: "A", switchColor: Connected); break;
            case "wifi": ShowCard(GlyphWifi, "Home Wi-Fi", "Wi-Fi connected", Connected, 3); break;
            case "bluetooth":
                OnBluetooth("Headphones", "audio", true, force: true);
                _ = Dispatcher.InvokeAsync(async () => { await Task.Delay(1500); OnBluetoothBattery("Headphones", 80); });
                break;
            case "usb": ShowCard(GlyphUsb, "USB drive", "Connected", AccentColor(), 2.6); break;
            case "wifi-radio": ShowCard(GlyphWifiOff, "Wi-Fi", "Off", Neutral, 2.6, toggle: false, switchColor: BtBlue); break;
            case "bt-radio": ShowCard(GlyphBluetooth, "Bluetooth", "On", BtBlue, 2.6, toggle: true); break;
            case "screenshot": DemoScreenshot(); break;
            case "language": OnLanguageDemo(); break;
            case "rain": ShowCard(Glyph.Cloud, RainAlerts.Title(40, "Showers"), "70% chance", RainBlue, 4); break;
            case "download": ShowCard(Glyph.Download, "report.pdf", "Downloaded · click to open", Connected, 4,
                actions: new (string, string, Action)[] { (Glyph.Folder, "Show in folder", () => { }) }); break;
        }
    }

    private void OnBrightness(int percent)
    {
        if (!S.VolumeHud || _expanded || !IsVisible) return;
        ShowHud(Glyph.Brightness, percent / 100.0, percent.ToString());
    }

    // ------------------------------------------------------------------ volume HUD
    private void OnVolumeEvent(double level, bool muted)
    {
        if (!S.VolumeHud || _expanded || !IsVisible) return;
        ShowHud(muted || level < 0.005 ? Glyph.Mute : Glyph.Volume, muted ? 0 : level, muted ? "Muted" : $"{level * 100:0}");
    }

    private void ShowHud(string glyph, double fraction, string text, Color? color = null, double seconds = 1.6, bool fillFromEmpty = false)
    {
        EndCard();
        var first = CHud.Visibility != Visibility.Visible;
        var appearing = first;
        CHudGlyph.Text = glyph;
        CHudText.Text = text;
        if (color is { } c)
        {
            CHudFill.Background = new SolidColorBrush(c);
            CHudGlyph.Foreground = new SolidColorBrush(c);
        }
        else
        {
            CHudFill.SetResourceReference(Border.BackgroundProperty, "T.Accent");
            CHudGlyph.SetResourceReference(TextBlock.ForegroundProperty, "T.Accent");
        }
        if (fillFromEmpty) { CHudFill.BeginAnimation(WidthProperty, null); CHudFill.Width = 0; first = false; }
        _hudTimer!.Interval = TimeSpan.FromSeconds(seconds);
        CHud.Visibility = Visibility.Visible;
        CNormal.Visibility = Visibility.Collapsed;
        CPeek.Visibility = Visibility.Collapsed;
        _peekTimer.Stop();
        SetAutoHidden(false);
        // The meter rides a spring too: rapid volume steps chase each other smoothly instead of restarting.
        var fill = 132 * Math.Clamp(fraction, 0, 1);
        if (first) Springs.To(CHudFill, WidthProperty, fill, 0);
        else if (fillFromEmpty) Motion.To(CHudFill, WidthProperty, fill, 900, Motion.Settle);
        else Springs.To(CHudFill, WidthProperty, fill, 0.22, 0.86);
        FitCompact();
        if (appearing) { Pop(CHud); Droplet(1); }
        _hudTimer!.Stop();
        _hudTimer.Start();
    }

    private void EndHud()
    {
        _hudTimer!.Interval = TimeSpan.FromSeconds(1.6);
        if (CHud.Visibility != Visibility.Visible) return;
        CHud.Visibility = Visibility.Collapsed;
        CNormal.Visibility = Visibility.Visible;
        Pop(CNormal, 0.9);
        RefreshCompact();
    }

    // ------------------------------------------------------------------ privacy dot
    private void UpdatePrivacyDot()
    {
        var p = Hub.Privacy;
        // The microphone pill already says the mic is in use, so its orange dot steps aside while the pill shows.
        var micDot = p.Microphone.Count > 0 && !S.CallPill;
        var show = S.PrivacyDot && (micDot || p.Camera.Count > 0 || p.Location.Count > 0);
        CPrivMic.Visibility = micDot ? Visibility.Visible : Visibility.Collapsed;
        CPrivCam.Visibility = p.Camera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CPrivLoc.Visibility = p.Location.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CPriv.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var parts = new List<string>();
        if (p.Microphone.Count > 0) parts.Add("Microphone: " + string.Join(", ", p.Microphone));
        if (p.Camera.Count > 0) parts.Add("Camera: " + string.Join(", ", p.Camera));
        if (p.Location.Count > 0) parts.Add("Location: " + string.Join(", ", p.Location));
        CPriv.ToolTip = parts.Count > 0 ? string.Join("\n", parts) : null;
        // Announce a new use once — except the microphone alone when the pill shows it anyway.
        if (show && !_privShown && (p.Camera.Count > 0 || p.Location.Count > 0 || !S.CallPill))
        {
            var (who, what, glyph) = p.Camera.Count > 0 ? (p.Camera[0], "camera", GlyphCam)
                : p.Microphone.Count > 0 ? (p.Microphone[0], "microphone", GlyphMic)
                : (p.Location[0], "location", "");
            Peek(glyph, $"{who} is using your {what}", 3);
        }
        _privShown = show;
        RefreshCompact();
    }

    // ------------------------------------------------------------------ audio-reactive bars + album-art colour
    private void UpdateEqualizerDrive()
    {
        var run = CMedia.Visibility == Visibility.Visible && Hub.Media.IsPlaying && IsVisible && !_expanded && CNormal.Visibility == Visibility.Visible;
        var reactive = S.AudioReactive && Motion.Intensity > 0.01;
        if (CEq.External != reactive) { CEq.Set(false); CEq.External = reactive; }
        CEq.Set(run);
        if (run && reactive) { if (!_levelTimer!.IsEnabled) _levelTimer.Start(); }
        else _levelTimer?.Stop();
    }

    private void TickLevels()
    {
        var peak = Hub.Audio.Peak();
        // Perceptual curve: quiet passages still move, loud ones don't pin the bars.
        var energy = Math.Sqrt(peak);
        double[] gain = { 1.05, 0.85, 1.2 };
        for (var i = 0; i < 3; i++)
        {
            var target = 0.2 + energy * gain[i] * (0.65 + 0.45 * _rng.NextDouble());
            _levels[i] = target > _levels[i] ? _levels[i] + (target - _levels[i]) * 0.7 : _levels[i] * 0.86 + target * 0.14;
        }
        CEq.Levels(_levels[0], _levels[1], _levels[2]);
    }

    private void TintFromArt()
    {
        var m = Hub.Media;
        var key = m.Title + "|" + m.Artist + "|" + (m.Art?.GetHashCode() ?? 0);
        if (key == _artKey) return;
        _artKey = key;
        Color? tint = null;
        _artPalette = null;
        if (m.Art is BitmapSource art)
        {
            try
            {
                var conv = new FormatConvertedBitmap(art, PixelFormats.Bgra32, null, 0);
                var px = new byte[conv.PixelWidth * conv.PixelHeight * 4];
                conv.CopyPixels(px, conv.PixelWidth * 4, 0);
                var p = Palette.FromPixels(px, conv.PixelWidth, conv.PixelHeight);
                if (!p.IsNeutral) { tint = ColorMath.ForMode(p.Accent, Hub.Theme.IsDark); _artPalette = p; }
            }
            catch { }
        }
        CEq.Tint(tint);
        _washKey = "\0";   // new artwork: cross-fade even if the wash was already showing
        if (S.Rim == "Album") ApplyRim();
        if (_card && _cardIsMedia) RefreshMediaCardArt();
    }

    // ------------------------------------------------------------------ album colours in the glass
    private Palette? _artPalette;
    private string _washKey = "";
    private bool _washOnA;

    /// <summary>The artwork's colours as a soft diagonal light through the glass: strongest where the artwork sits.</summary>
    private static Brush WashBrush(Palette p) => WashBrush(p.Accent, p.Secondary, p.Dominant);

    private static Brush WashBrush(Color first, Color middle, Color last)
    {
        Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.2), EndPoint = new Point(1, 0.8) };
        b.GradientStops.Add(new GradientStop(A(first, 0x70), 0));
        b.GradientStops.Add(new GradientStop(A(middle, 0x40), 0.5));
        b.GradientStops.Add(new GradientStop(A(last, 0x26), 1));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Show the album colours while a song plays where music is on screen (or the sky's colours on the weather
    /// page); fade them out otherwise.
    /// </summary>
    private void UpdateArtWash()
    {
        var m = Hub.Media;
        var sky = _expanded && _tab == "weather" ? _skyWash : null;
        var musicOnScreen = _expanded ? _tab == "media" : _card ? _cardIsMedia : CMedia.Visibility == Visibility.Visible && CNormal.Visibility == Visibility.Visible;
        var want = sky is not null || S.AlbumTint && _artPalette is not null && m.HasTrack && m.IsPlaying && musicOnScreen;
        var key = sky is not null ? _skyKey : want ? _artKey : "";
        if (key == _washKey) return;
        _washKey = key;
        if (!want)
        {
            Motion.To(ArtWashA, OpacityProperty, 0, 600, Motion.EaseInOut);
            Motion.To(ArtWashB, OpacityProperty, 0, 600, Motion.EaseInOut);
            return;
        }
        _washOnA = !_washOnA;
        var (show, hide) = _washOnA ? (ArtWashA, ArtWashB) : (ArtWashB, ArtWashA);
        show.Background = sky ?? WashBrush(_artPalette!);
        Motion.To(show, OpacityProperty, 1, 900, Motion.EaseInOut);
        Motion.To(hide, OpacityProperty, 0, 900, Motion.EaseInOut);
    }

    // ------------------------------------------------------------------ now-playing card
    private bool _cardIsMedia;

    /// <summary>A new song: the island grows into a card with the artwork as its tile, the title and the artist.</summary>
    private void ShowMediaCard(double seconds = 3.4)
    {
        var m = Hub.Media;
        TintFromArt();
        var color = _artPalette is { } p ? ColorMath.ForMode(p.Accent, Hub.Theme.IsDark) : AccentColor();
        ShowCard(Glyph.Music, m.Title, string.IsNullOrWhiteSpace(m.Artist) ? (m.Album ?? "") : m.Artist, color, seconds, art: m.Art);
        _cardIsMedia = true;
        UpdateArtWash();
    }

    /// <summary>Preview with nothing playing: a generated sunset "cover" and its colours, exactly as a real song shows.</summary>
    private void ShowSampleSong()
    {
        const int size = 64;
        var px = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var t = (x + y) / (2.0 * (size - 1));
                var c = t < 0.5 ? ColorMath.Lerp(Color.FromRgb(0xFF, 0x8A, 0x3D), Color.FromRgb(0xE9, 0x2F, 0x6B), t * 2)
                                : ColorMath.Lerp(Color.FromRgb(0xE9, 0x2F, 0x6B), Color.FromRgb(0x5B, 0x2A, 0xC9), (t - 0.5) * 2);
                var i = (y * size + x) * 4;
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255;
            }
        var art = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
        art.Freeze();
        var palette = Palette.FromPixels(px, size, size);
        ShowCard(Glyph.Music, "Golden Hour", "Sample artist · preview", ColorMath.ForMode(palette.Accent, Hub.Theme.IsDark), 3.4, art: art);
        _cardIsMedia = true;
        if (!S.AlbumTint) return;
        _washKey = "preview";   // the next real update fades this out again
        ArtWashA.Background = WashBrush(palette);
        Motion.To(ArtWashA, OpacityProperty, 1, 900, Motion.EaseInOut);
        Motion.To(ArtWashB, OpacityProperty, 0, 900, Motion.EaseInOut);
        _washOnA = true;
    }

    private void RefreshMediaCardArt()
    {
        if (Hub.Media.Art is not { } art) return;
        SetCardArt(art);
        UpdateArtWash();
    }

    private void OnMediaForLive()
    {
        TintFromArt();
        UpdateArtWash();
        UpdateEqualizerDrive();
        _ = LoadLyricsAsync();
        UpdateLyric();
    }

    // ------------------------------------------------------------------ synced lyrics
    private async Task LoadLyricsAsync()
    {
        var m = Hub.Media;
        var key = m.Title + "|" + m.Artist;
        if (key == _lyricKey) return;
        _lyricKey = key;
        _lyrics = null; _lyricIndex = -1;
        if (!Hub.Lyrics.Enabled || !m.HasTrack) { UpdateLyric(); return; }
        var lines = await Hub.Lyrics.GetAsync(m.Title, m.Artist, m.Album, m.Duration);
        if (key != _lyricKey) return;
        _lyrics = lines;
        UpdateLyric();
    }

    /// <summary>Show the current line and schedule exactly one wake-up for the next line (no polling).</summary>
    private void UpdateLyric()
    {
        _lyricTimer?.Stop();
        var m = Hub.Media;
        string? line = null;
        if (_lyrics is { Count: > 0 } lines && m.HasTrack)
        {
            var pos = m.Position;
            var i = LyricsService.IndexAt(lines, pos);
            line = i >= 0 ? lines[i].Text : null;
            if (string.IsNullOrWhiteSpace(line)) line = null;
            if (i != _lyricIndex)
            {
                _lyricIndex = i;
                if (line is not null) { CTitle.Opacity = 0.25; Motion.To(CTitle, OpacityProperty, 1, 220); }
            }
            if (m.IsPlaying && i + 1 < lines.Count)
            {
                var wait = lines[i + 1].At - pos;
                _lyricTimer!.Interval = wait < TimeSpan.FromMilliseconds(40) ? TimeSpan.FromMilliseconds(40) : wait;
                _lyricTimer.Start();
            }
        }
        MLyric.Text = line ?? "";
        MLyric.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        var capsule = line is not null && S.LyricsInCapsule && m.IsPlaying;
        CTitle.Width = capsule ? 250 : double.NaN;          // a steady width so the pill doesn't jump every line
        CTitle.MaxWidth = capsule ? 250 : 170;
        if (CMedia.Visibility == Visibility.Visible) CTitle.Text = capsule ? line! : m.DisplayTitle;
        if (!_expanded) FitCompact();
    }

    /// <summary>Text for the capsule title: the current lyric line when lyrics are showing.</summary>
    private string CapsuleTitle()
    {
        var m = Hub.Media;
        if (S.LyricsInCapsule && m.IsPlaying && _lyrics is { Count: > 0 } lines && _lyricIndex >= 0 && _lyricIndex < lines.Count && !string.IsNullOrWhiteSpace(lines[_lyricIndex].Text))
            return lines[_lyricIndex].Text;
        return m.DisplayTitle;
    }

    // ------------------------------------------------------------------ clipboard tab
    private void OnClipboardChanged() { if (_expanded && _tab == "clipboard") RenderClipboard(); }

    private void RenderClipboard()
    {
        ClipList.Children.Clear();
        var clip = Hub.Clipboard;
        ClipClear.Visibility = S.ClipboardHistory ? Visibility.Visible : Visibility.Collapsed;
        if (!S.ClipboardHistory)
        {
            ClipHeader.Text = "Clipboard history is off";
            var box = new StackPanel { Margin = new Thickness(4, 10, 4, 0) };
            box.Children.Add(Ui.Text("Keep the last 40 things you copied — text, links, images and files — one click away.", "Text.Sub", 13).With(t => t.TextWrapping = TextWrapping.Wrap));
            box.Children.Add(Ui.Text("History stays in memory only and is gone when WinOrbit quits; only items you pin are saved. Copies that password managers mark as private are never recorded.", "Text.Caption", 11.5).With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 6, 0, 12); }));
            box.Children.Add(Kit.Btn("Turn on clipboard history", () => { S.ClipboardHistory = true; Hub.Store.Save(); Hub.Clipboard.Apply(); RenderClipboard(); }, Glyph.Copy, true).With(b => b.HorizontalAlignment = HorizontalAlignment.Left));
            ClipList.Children.Add(box);
            return;
        }
        ClipHeader.Text = clip.Items.Count == 0 ? "Copy something and it appears here" : $"{clip.Items.Count} item{(clip.Items.Count == 1 ? "" : "s")} · click to copy again";
        foreach (var item in clip.Items) ClipList.Children.Add(ClipRow(item));
    }

    private FrameworkElement ClipRow(ClipItem item)
    {
        var row = new Border { Margin = new Thickness(0, 0, 4, 6), Padding = new Thickness(10, 7, 6, 7), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(12) };
        row.SetResourceReference(Border.BackgroundProperty, "T.Input");
        var g = Ui.Grid("auto,*,auto,auto", "auto");
        FrameworkElement lead = item.Kind == ClipKind.Image && item.Thumb is not null
            ? new Border { Width = 46, Height = 34, CornerRadius = new CornerRadius(6), Background = new ImageBrush(item.Thumb) { Stretch = Stretch.UniformToFill } }
            : Ui.Icon(item.Kind switch { ClipKind.Link => Glyph.Link, ClipKind.Files => Glyph.Folder, _ => Glyph.Page }, 14, "T.Text2").With(t => t.Width = 22);
        g.Children.Add(lead);
        var text = Ui.Text(item.Preview.Replace('\n', ' ').Replace('\r', ' '), "Text.Body", 12.5).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; t.Margin = new Thickness(10, 0, 6, 0); t.VerticalAlignment = VerticalAlignment.Center; });
        g.Children.Add(text.At(0, 1));
        if (item.Kind is ClipKind.Text or ClipKind.Link)
            g.Children.Add(Ui.IconButton(item.Pinned ? Glyph.StarFill : Glyph.Star, item.Pinned ? "Unpin" : "Pin (kept after restart)", () => Hub.Clipboard.TogglePin(item), 26)
                .With(b => { if (item.Pinned) b.SetResourceReference(Control.ForegroundProperty, "T.Accent"); }).At(0, 2));
        g.Children.Add(Ui.IconButton(Glyph.Cancel, "Remove", () => Hub.Clipboard.Remove(item), 26).At(0, 3));
        row.Child = g;
        row.ToolTip = item.Kind == ClipKind.Files ? string.Join("\n", item.Files) : item.Kind == ClipKind.Image ? "Image" : item.Text.Length > 600 ? item.Text[..600] + "…" : item.Text;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) is not null) return;
            Hub.Clipboard.Copy(item);
            Collapse();
            Peek(Glyph.Copy, "Copied — press Ctrl+V to paste", 1.6);
        };
        return row;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = d; p is not null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p)) if (p is T t) return t;
        return null;
    }

    private void OnClipClear(object sender, RoutedEventArgs e) => Hub.Clipboard.ClearUnpinned();

    // ------------------------------------------------------------------ file shelf
    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (_dragOut || !HasFiles(e) || !S.Modules.Contains("shelf")) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        if (!_expanded) Expand(false);
        if (_tab != "shelf") ShowTab("shelf");
        _pinned = true;   // stay open while the user aims for the shelf
        ShelfZone.SetResourceReference(Border.BorderBrushProperty, "T.Accent");
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) && !_dragOut ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dragOut) { e.Handled = true; return; }
        ShelfZone.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
        _pinned = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files.Where(f => !Shelf.Contains(f, StringComparer.OrdinalIgnoreCase))) Shelf.Add(f);
            ShelfChanged?.Invoke();
        }
        e.Handled = true;
        ArmCollapse();
    }

    private void OnShelfChanged() { if (_expanded && _tab == "shelf") RenderShelf(); }

    private void OnShelfClear(object sender, RoutedEventArgs e) { Shelf.Clear(); ShelfChanged?.Invoke(); }

    private void RenderShelf()
    {
        ShelfList.Children.Clear();
        Shelf.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        ShelfEmpty.Visibility = Shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfHeader.Text = Shelf.Count == 0 ? "File shelf" : $"{Shelf.Count} on the shelf · drag out to use, double-click to open";
        foreach (var path in Shelf.ToList()) ShelfList.Children.Add(ShelfChip(path));
    }

    private FrameworkElement ShelfChip(string path)
    {
        var isDir = Directory.Exists(path);
        var chip = new Border { Width = 96, Height = 84, Margin = new Thickness(4), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, ToolTip = path, Padding = new Thickness(6) };
        chip.SetResourceReference(Border.BackgroundProperty, "T.Card");
        var g = new Grid();
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var glyph = isDir ? Glyph.Folder : ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" ? Glyph.Photo : ext is ".mp3" or ".wav" or ".flac" or ".m4a" ? Glyph.Music : Glyph.Page;
        sp.Children.Add(Ui.Icon(glyph, 22, "T.Accent"));
        sp.Children.Add(Ui.Text(Path.GetFileName(path.TrimEnd('\\')), "Text.Caption", 11).With(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; t.MaxHeight = 30; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 6, 0, 0); }));
        g.Children.Add(sp);
        var remove = Ui.IconButton(Glyph.Cancel, "Remove from shelf (the file stays)", () => { Shelf.Remove(path); ShelfChanged?.Invoke(); }, 20)
            .With(b => { b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(0, -4, -4, 0); b.FontSize = 8; });
        g.Children.Add(remove);
        chip.Child = g;

        Point? down = null;
        chip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } return; }
            down = e.GetPosition(chip);
        };
        chip.MouseMove += (_, e) =>
        {
            if (down is null || e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
            var p = e.GetPosition(chip);
            if (Math.Abs(p.X - down.Value.X) + Math.Abs(p.Y - down.Value.Y) < 6) return;
            down = null;
            _pinned = true;
            try { DragDrop.DoDragDrop(chip, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            finally { _pinned = false; ArmCollapse(); }
        };
        return chip;
    }
}
