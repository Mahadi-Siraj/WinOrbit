using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;
using WinOrbit.WindowsIntegration;
using Windows.Devices.Radios;

namespace WinOrbit.Island;

/// <summary>
/// The expanded pages: media, the Control Centre (volume, brightness and quick tiles), system monitor, focus timer, quick note, and the header buttons.
/// </summary>
public partial class IslandWindow
{
    // ----- media -----
    private void UpdateMedia()
    {
        var m = Hub.Media;
        var has = m.HasTrack;
        MediaEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        MediaBody.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) { _mediaTick.Stop(); return; }
        MTitle.Text = m.DisplayTitle;
        MArtist.Text = string.IsNullOrWhiteSpace(m.Album) ? m.Artist : $"{m.Artist} · {m.Album}";
        MSource.Text = m.Source.ToUpperInvariant();
        MPlay.Content = m.IsPlaying ? Glyph.Pause : Glyph.Play;
        if (m.Art is not null) { MArt.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; MArtGlyph.Visibility = Visibility.Collapsed; }
        else { MArt.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft"); MArtGlyph.Visibility = Visibility.Visible; }
        UpdateMediaProgress();
        if (m.IsPlaying && _expanded) _mediaTick.Start(); else _mediaTick.Stop();
    }

    private static string T(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    private bool _seekDragging;

    private void UpdateMediaProgress()
    {
        var m = Hub.Media;
        var known = m.Duration > TimeSpan.Zero;
        var canSeek = m.CanSeek;
        if (!_seekDragging)
        {
            _loadingUi = true;
            MSeek.Value = known ? Math.Clamp(m.Position.TotalSeconds / m.Duration.TotalSeconds, 0, 1) : 0;
            _loadingUi = false;
            MPos.Text = known ? T(m.Position) : "";
        }
        MDur.Text = known ? T(m.Duration) : "";
        MSeek.Visibility = MPos.Visibility = MDur.Visibility = known ? Visibility.Visible : Visibility.Hidden;   // nothing to show for players without a timeline
        MSeek.IsEnabled = canSeek;
        MSeek.Opacity = known ? 1 : 0.35;
        MSeek.ToolTip = canSeek ? null : known ? $"{m.Source} doesn't let other apps change the position" : null;
        MBack.Visibility = MFwd.Visibility = canSeek ? Visibility.Visible : Visibility.Collapsed;
    }

    // Clicking the bar jumps there; dragging previews the time and jumps when released.
    private void OnSeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi) return;
        var target = TimeSpan.FromSeconds(Hub.Media.Duration.TotalSeconds * e.NewValue);
        MPos.Text = T(target);
        if (!_seekDragging) _ = Hub.Media.SeekAsync(target);
    }

    private void OnSeekDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _seekDragging = true;

    private void OnSeekDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _seekDragging = false;
        _ = Hub.Media.SeekAsync(TimeSpan.FromSeconds(Hub.Media.Duration.TotalSeconds * MSeek.Value));
    }

    private void OnSkipBack(object s, RoutedEventArgs e) => _ = Hub.Media.SeekAsync(Hub.Media.Position - TimeSpan.FromSeconds(10));
    private void OnSkipForward(object s, RoutedEventArgs e) => _ = Hub.Media.SeekAsync(Hub.Media.Position + TimeSpan.FromSeconds(10));

    private void OnPrev(object s, RoutedEventArgs e) => Hub.Media.Previous();
    private void OnNext(object s, RoutedEventArgs e) => Hub.Media.Next();
    private void OnPlay(object s, RoutedEventArgs e) => Hub.Media.PlayPause();

    // ----- volume / brightness -----
    private void LoadVolume()
    {
        _loadingUi = true;
        var v = Audio.GetVolume();
        var pct = (v ?? 0) * 100;
        MVolume.Value = QVolume.Value = pct;
        MVolume.IsEnabled = QVolume.IsEnabled = QMute.IsEnabled = v is not null;
        QVolumeText.Text = v is null ? "n/a" : $"{pct:0}%";
        QMute.Content = Audio.GetMute() ? Glyph.Mute : Glyph.Volume;
        _loadingUi = false;
    }

    private void OnVolumeSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi) return;
        Audio.SetVolume(e.NewValue / 100);
        QVolumeText.Text = $"{e.NewValue:0}%";
        _loadingUi = true;
        if (!ReferenceEquals(sender, MVolume)) MVolume.Value = e.NewValue;
        if (!ReferenceEquals(sender, QVolume)) QVolume.Value = e.NewValue;
        _loadingUi = false;
    }

    private void OnMute(object s, RoutedEventArgs e)
    {
        var mute = !Audio.GetMute();
        Audio.SetMute(mute);
        QMute.Content = mute ? Glyph.Mute : Glyph.Volume;
    }

    private async void LoadBrightness()
    {
        // DDC/CI calls can take ~50 ms, so they run off the UI thread.
        var value = await Task.Run(Brightness.Get);
        _loadingUi = true;
        QBrightnessRow.IsEnabled = value is not null;
        QBrightnessRow.Opacity = value is null ? 0.45 : 1;
        QBrightness.Value = value ?? 0;
        QBrightnessText.Text = value is null ? "n/a" : $"{value}%";
        QBrightnessRow.ToolTip = value is null ? "This display does not expose brightness control (DDC/CI)." : null;
        _loadingUi = false;
    }

    private CancellationTokenSource? _brightnessCts;
    private async void OnBrightnessSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi) return;
        QBrightnessText.Text = $"{e.NewValue:0}%";
        _brightnessCts?.Cancel();
        var cts = _brightnessCts = new CancellationTokenSource();
        var target = (int)e.NewValue;
        try { await Task.Delay(120, cts.Token); await Task.Run(() => Brightness.Set(target)); } catch (TaskCanceledException) { }
    }

    // ----- quick tiles: each one switches the real setting; the chevron or right-click opens its settings page -----
    private readonly Dictionary<string, QuickTile> _quick = new();
    private bool _quickHooked;

    private void BuildShortcuts()
    {
        QShortcuts.Children.Clear();
        _quick.Clear();
        void Add(string id, string glyph, string label, Action toggle, Action secondary)
        {
            var tile = new QuickTile { Glyph = glyph, Label = label, Status = "…" };
            tile.Toggled += toggle;
            tile.Secondary += () => { secondary(); if (id != "focus") Collapse(); };
            _quick[id] = tile;
            QShortcuts.Children.Add(tile);
        }
        Add("wifi", LineIcons.WiFi, "Wi-Fi", () => ToggleRadio("wifi", RadioKind.WiFi), () => OpenUri("ms-settings:network-wifi"));
        Add("bt", LineIcons.Bluetooth, "Bluetooth", () => ToggleRadio("bt", RadioKind.Bluetooth), () => OpenUri("ms-settings:bluetooth"));
        // Windows offers apps no way to switch these; the tile shows what it can and opens the right place.
        Add("airplane", LineIcons.Airplane, "Airplane mode", () => { OpenUri("ms-settings:network-airplanemode"); Collapse(); }, () => OpenUri("ms-settings:network-airplanemode"));
        Add("dnd", LineIcons.Moon, "Do not disturb", () => { OpenUri("ms-actioncenter:"); Collapse(); }, () => OpenUri("ms-settings:notifications"));
        Add("night", LineIcons.Sunset, "Night light", () =>
        {
            var on = !(_quick["night"].IsOn);
            if (QuickControls.SetNightLight(on)) SetTile("night", on, on ? "On" : "Off");
            else OpenUri("ms-settings:nightlight");
        }, () => OpenUri("ms-settings:nightlight"));
        Add("dark", LineIcons.Contrast, "Dark mode", () =>
        {
            var dark = !QuickControls.WindowsDark;
            QuickControls.SetWindowsDark(dark);
            SetTile("dark", dark, dark ? "Dark" : "Light");
        }, () => OpenUri("ms-settings:colors"));
        Add("saver", LineIcons.Leaf, "Energy saver", () => { OpenUri("ms-settings:batterysaver"); Collapse(); }, () => OpenUri("ms-settings:batterysaver"));
        Add("power", LineIcons.Bolt, "Power mode", () =>
        {
            // Balanced → Performance → Efficiency → Balanced
            var i = QuickControls.PowerModeIndex() ?? 1;
            var next = i == 1 ? 2 : i == 2 ? 0 : 1;
            if (QuickControls.SetPowerMode(next)) RefreshQuick();
        }, () => OpenUri("ms-settings:powersleep"));
        Add("mic", LineIcons.Mic, "Microphone", () =>
        {
            var muted = Audio.GetMicMute();
            if (muted is null) { OpenUri("ms-settings:sound"); return; }
            Audio.SetMicMute(!muted.Value);
            RefreshQuick();
            Peek(muted.Value ? "" : "", muted.Value ? "Microphone on" : "Microphone muted", 1.5);
        }, () => OpenUri("ms-settings:sound"));
        Add("awake", LineIcons.Cup, "Keep awake", () =>
        {
            QuickControls.SetKeepAwake(!QuickControls.KeepAwake);
            RefreshQuick();
        }, () => OpenUri("ms-settings:powersleep"));
        Add("focus", LineIcons.Target, "Focus", () =>
        {
            var t = Hub.Timers;
            if (t.Running && t.Mode == TimerMode.Focus) t.Stop();
            else { t.SetMode(TimerMode.Focus); t.Start(); }
            RefreshQuick();
        }, () => ShowTab("focus"));
        Add("widgets", LineIcons.Grid, "Widgets", () =>
        {
            Hub.Settings.Layout.WidgetsVisible = !Hub.Settings.Layout.WidgetsVisible;
            Hub.Store.Save();
            Hub.Widgets.Apply();
            RefreshQuick();
        }, () => App.Current.ShowMain("widgets"));

        if (!_quickHooked)
        {
            _quickHooked = true;
            QuickControls.RadiosChanged += () => Dispatcher.BeginInvoke(RefreshQuick);
            Hub.Timers.Changed += () => { if (_expanded && _tab == "controls") UpdateFocusTile(); };
        }
    }

    private void SetTile(string id, bool on, string status, bool available = true)
    {
        if (!_quick.TryGetValue(id, out var t)) return;
        if (t.IsOn != on) t.IsOn = on;
        t.Status = status;
        t.IsAvailable = available;
    }

    private async void ToggleRadio(string id, RadioKind kind)
    {
        var tile = _quick[id];
        var want = !tile.IsOn;
        SetTile(id, want, "…");
        var result = await QuickControls.SetRadio(kind, want);
        if (result is null) { RefreshQuick(); Peek(Glyph.Warning, "Windows didn't allow that change", 1.6); }
        else RefreshQuick();
    }

    private void UpdateFocusTile()
    {
        var t = Hub.Timers;
        var on = t.Running && t.Mode == TimerMode.Focus;
        SetTile("focus", on, on ? $"{t.Display} left" : t.Running ? t.ModeLabel : $"{t.FocusMinutes} min");
    }

    private async void RefreshQuick()
    {
        if (_quick.Count == 0) return;
        var wifi = await QuickControls.FindRadio(RadioKind.WiFi);
        var bt = await QuickControls.FindRadio(RadioKind.Bluetooth);
        if (wifi is null) SetTile("wifi", false, "Not available", false);
        else
        {
            var on = wifi.State == RadioState.On;
            var connected = on && System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Any(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 && n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
            SetTile("wifi", on, !on ? "Off" : connected ? "Connected" : "On");
        }
        if (bt is null) SetTile("bt", false, "Not available", false);
        else SetTile("bt", bt.State == RadioState.On, bt.State == RadioState.On ? "On" : "Off");

        var night = QuickControls.NightLightOn;
        if (night is null) SetTile("night", false, "Open settings", false);
        else SetTile("night", night.Value, night.Value ? "On" : "Off");

        var dark = QuickControls.WindowsDark;
        SetTile("dark", dark, dark ? "Dark" : "Light");

        var power = QuickControls.PowerModeIndex();
        if (power is null) SetTile("power", false, "Custom plan", false);
        else SetTile("power", power.Value == 2, QuickControls.PowerModes[power.Value].Name);

        SetTile("awake", QuickControls.KeepAwake, QuickControls.KeepAwake ? "Screen stays on" : "Off");
        var mic = Audio.GetMicMute();
        if (mic is null) SetTile("mic", false, "No microphone", false);
        else SetTile("mic", !mic.Value, mic.Value ? "Muted" : "On");
        var saver = Hub.Power.EnergySaver;
        SetTile("saver", saver > 0, saver switch { null => "Settings", 0 => "Off", 2 => "Extra savings", _ => "On" });
        var radiosOff = (wifi is null || wifi.State != RadioState.On) && (bt is null || bt.State != RadioState.On);
        SetTile("airplane", false, radiosOff && (wifi is not null || bt is not null) ? "Radios off" : "Settings");
        SetTile("dnd", false, "Notifications");
        UpdateFocusTile();
        var widgets = Hub.Settings.Layout.WidgetsVisible;
        SetTile("widgets", widgets, widgets ? "Shown" : "Hidden");
    }

    public static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Could not open {uri}: {ex.Message}"); }
    }

    // ----- system -----
    private void BuildSystemTiles()
    {
        TabSystem.Children.Clear();
        _tiles.Clear();
        foreach (var (id, label, spark) in new[] { ("cpu", "CPU", true), ("ram", "MEMORY", true), ("gpu", "GPU", true), ("net", "NETWORK", true), ("disk", "DISK", false), ("up", "UPTIME", false) })
        {
            var border = new Border { Margin = new Thickness(3) };
            border.SetResourceReference(StyleProperty, "Card.Inset");
            border.Padding = new Thickness(11, 9, 11, 9);
            var grid = Ui.Grid("*", "auto,auto,*");
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Ui.LineIcon(Ui.MetricIcon(id), 13, Ui.MetricBrush(id), 1.8).With(v => { v.Margin = new Thickness(0, 0, 6, 0); v.VerticalAlignment = VerticalAlignment.Center; }));
            head.Children.Add(Ui.Text(label, "Text.Label", 10).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            grid.Children.Add(head);
            var value = Ui.Text("—", "Text.Sub", 18).With(t => t.Margin = new Thickness(0, 2, 0, 0)).At(1, 0);
            grid.Children.Add(value);
            var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom }.At(2, 0);
            var detail = Ui.Text("", "Text.Caption", 10.5).With(t => { t.TextWrapping = TextWrapping.NoWrap; t.VerticalAlignment = VerticalAlignment.Bottom; });
            Sparkline? sl = null; ProgressBar? bar = null;
            if (spark) { sl = new Sparkline { Height = 22, Opacity = 0.9 }; bottom.Children.Add(sl); detail.HorizontalAlignment = HorizontalAlignment.Right; detail.VerticalAlignment = VerticalAlignment.Top; detail.Margin = new Thickness(0, -20, 0, 0); }
            else { bar = new ProgressBar { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) }; bottom.Children.Add(bar); detail.Margin = new Thickness(0, 0, 0, 10); }
            bottom.Children.Add(detail);
            grid.Children.Add(bottom);
            border.Child = grid;
            TabSystem.Children.Add(border);
            _tiles[id] = (value, detail, sl, bar);
        }
    }

    private void OnSample(SystemSample s)
    {
        if (S.ShowSystem && !_expanded) { CSys.Text = $"CPU {s.Cpu:0}%  ·  RAM {s.Ram:0}%"; FitCompact(); }
        if (!_expanded || _tab != "system") return;
        var mon = Hub.Monitor;
        _tiles["cpu"].Value.Text = $"{s.Cpu:0}%"; _tiles["cpu"].Spark!.Update(mon.CpuHistory);
        _tiles["ram"].Value.Text = $"{s.Ram:0}%"; _tiles["ram"].Detail.Text = $"{s.RamUsedGb:0.0}/{s.RamTotalGb:0} GB"; _tiles["ram"].Spark!.Update(mon.RamHistory);
        _tiles["gpu"].Value.Text = s.Gpu is { } g ? $"{g:0}%" : "n/a";
        _tiles["gpu"].Detail.Text = s.GpuTemp is { } t ? $"{t:0}°C" : "";
        _tiles["gpu"].Spark!.Update(mon.GpuHistory);
        _tiles["net"].Value.Text = Fmt.Rate(s.NetDown).Replace("/s", "");
        _tiles["net"].Detail.Text = "↑ " + Fmt.Rate(s.NetUp);
        _tiles["net"].Spark!.Update(mon.NetHistory, 0);
        _tiles["disk"].Value.Text = $"{s.DiskActive:0}%"; _tiles["disk"].Detail.Text = Fmt.Rate(s.DiskBytes); _tiles["disk"].Bar!.Value = s.DiskActive;
        _tiles["up"].Value.Text = Fmt.Uptime(s.Uptime);
        _tiles["up"].Detail.Text = s.Battery is { } b ? $"Battery {b}%{(s.OnAc ? " · charging" : "")}" : "On mains power";
        _tiles["up"].Bar!.Value = s.Battery ?? 100;
    }

    // ----- focus -----
    private void BuildFocusModes()
    {
        FModes.Children.Clear();
        foreach (var (mode, label) in new[] { (TimerMode.Focus, "Focus"), (TimerMode.Countdown, "Countdown"), (TimerMode.Stopwatch, "Stopwatch") })
        {
            var rb = new RadioButton { Content = label, GroupName = "IslandFocus" + GetHashCode(), Tag = mode, Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
            rb.SetResourceReference(StyleProperty, "Segment");
            rb.Checked += (_, _) => { if (!_loadingUi) Hub.Timers.SetMode((TimerMode)rb.Tag); };
            FModes.Children.Add(rb);
        }
    }

    private void UpdateFocus()
    {
        var t = Hub.Timers;
        FTime.Text = t.Display;
        FMode.Text = t.ModeLabel;
        FRing.Value = t.Mode == TimerMode.Stopwatch ? (t.Elapsed.TotalSeconds % 60) / 60.0 : 1 - t.Progress;
        FStart.Content = t.Running ? "Pause" : t.IsActive ? "Resume" : "Start";
        FStats.Text = t.CompletedToday == 0 ? "No focus blocks completed today yet." : $"{t.CompletedToday} focus block{(t.CompletedToday == 1 ? "" : "s")} · {t.MinutesToday} min today";
        _loadingUi = true;
        var shown = t.Mode == TimerMode.Break ? TimerMode.Focus : t.Mode;
        foreach (var rb in FModes.Children.OfType<RadioButton>()) rb.IsChecked = (TimerMode)rb.Tag == shown;
        _loadingUi = false;
    }

    private void OnFocusToggle(object s, RoutedEventArgs e) => Hub.Timers.Toggle();
    private void OnFocusReset(object s, RoutedEventArgs e) => Hub.Timers.Reset();
    private void OnFocusLess(object s, RoutedEventArgs e) => Adjust(-5);
    private void OnFocusMore(object s, RoutedEventArgs e) => Adjust(5);
    private static void Adjust(int delta)
    {
        var t = Hub.Timers;
        if (t.Mode == TimerMode.Stopwatch || t.Running) return;
        t.SetMode(t.Mode, Math.Clamp((int)Math.Round(t.Total.TotalMinutes) + delta, 5, 180));
    }

    // ----- notes -----
    private void OnNotesMouseDown(object sender, MouseButtonEventArgs e) => SetInteractive(true);

    private void OnNotesChanged(object sender, TextChangedEventArgs e)
    {
        NotesHint.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_loadingUi) return;
        Hub.Store.Data.Notes[UserData.IslandNoteKey] = NotesBox.Text;
        Hub.Store.SaveData();
    }

    // ----- header buttons -----
    private void OnPin(object s, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        PinButton.SetResourceReference(ForegroundProperty, _pinned ? "T.Accent" : "T.Text");
        if (_pinned) _collapseTimer.Stop();
    }

    private void OnSettings(object s, RoutedEventArgs e) { Collapse(); App.Current.ShowMain("island"); }
    private void OnCollapseClick(object s, RoutedEventArgs e) => Collapse();
}
