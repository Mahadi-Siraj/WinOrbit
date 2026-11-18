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
        Hub.Devices.UsbChanged += OnUsb;
        Hub.Devices.RadioChanged += OnRadio;

        Pill.AllowDrop = true;
        Pill.DragEnter += OnDragEnter;
        Pill.DragOver += OnDragOver;
        Pill.Drop += OnDrop;
        UpdatePrivacyDot();
        OnMediaForLive();
    }

    private void CleanupLive()
    {
        foreach (var t in new[] { _hudTimer, _levelTimer, _lyricTimer }) t?.Stop();
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
        Hub.Devices.UsbChanged -= OnUsb;
        Hub.Devices.RadioChanged -= OnRadio;
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

    private void OnBluetooth(string name, string kind, bool connected)
    {
        if (!S.BluetoothAlerts) return;
        var glyph = kind switch { "audio" => GlyphHeadphones, "input" => GlyphKeyboard, "phone" => GlyphPhone, _ => GlyphBluetooth };
        ShowCard(glyph, name, connected ? "Connected" : "Disconnected", connected ? BtBlue : Dropped, 3);
    }

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
        if (state > 0) Peek(GlyphLeaf, state == 2 ? "Energy saver on · extra savings" : "Energy saver on", 2.5);
        else Peek(GlyphLeaf, "Energy saver off", 2);
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
            case "saver": Peek(GlyphLeaf, "Energy saver on", 2.5); break;
            case "caps": ShowCard("", "Caps Lock", "On", KeyLit, 2.4, toggle: true, cap: "A", switchColor: Connected); break;
            case "wifi": ShowCard(GlyphWifi, "Home Wi-Fi", "Wi-Fi connected", Connected, 3); break;
            case "bluetooth": ShowCard(GlyphHeadphones, "Headphones", "Connected", BtBlue, 3); break;
            case "usb": ShowCard(GlyphUsb, "USB drive", "Connected", AccentColor(), 2.6); break;
            case "wifi-radio": ShowCard(GlyphWifiOff, "Wi-Fi", "Off", Neutral, 2.6, toggle: false, switchColor: BtBlue); break;
            case "bt-radio": ShowCard(GlyphBluetooth, "Bluetooth", "On", BtBlue, 2.6, toggle: true); break;
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
        var show = S.PrivacyDot && p.Active;
        CPrivMic.Visibility = p.Microphone.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CPrivCam.Visibility = p.Camera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CPrivLoc.Visibility = p.Location.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CPriv.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var parts = new List<string>();
        if (p.Microphone.Count > 0) parts.Add("Microphone: " + string.Join(", ", p.Microphone));
        if (p.Camera.Count > 0) parts.Add("Camera: " + string.Join(", ", p.Camera));
        if (p.Location.Count > 0) parts.Add("Location: " + string.Join(", ", p.Location));
        CPriv.ToolTip = parts.Count > 0 ? string.Join("\n", parts) : null;
        if (show && !_privShown)
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
        if (m.Art is BitmapSource art)
        {
            try
            {
                var conv = new FormatConvertedBitmap(art, PixelFormats.Bgra32, null, 0);
                var px = new byte[conv.PixelWidth * conv.PixelHeight * 4];
                conv.CopyPixels(px, conv.PixelWidth * 4, 0);
                var p = Palette.FromPixels(px, conv.PixelWidth, conv.PixelHeight);
                if (!p.IsNeutral) tint = ColorMath.ForMode(p.Accent, Hub.Theme.IsDark);
            }
            catch { }
        }
        CEq.Tint(tint);
    }

    private void OnMediaForLive()
    {
        TintFromArt();
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
        if (!HasFiles(e) || !S.Modules.Contains("shelf")) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        if (!_expanded) Expand(false);
        if (_tab != "shelf") ShowTab("shelf");
        _pinned = true;   // stay open while the user aims for the shelf
        ShelfZone.SetResourceReference(Border.BorderBrushProperty, "T.Accent");
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
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
