using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.WindowsIntegration;

namespace WinOrbit.Island;

/// <summary>
/// What the collapsed island shows when nothing is happening ("glance"): the clock (the classic time and date),
/// CPU and memory, a status row (Wi-Fi, Bluetooth, battery), a pair of eyes that follow the pointer, or nothing.
/// Live activities (music, calls, timers, downloads) still take the left side whatever is chosen.
///
/// Cost: the status row listens to Windows' own change events (network status, the Bluetooth radio, the battery)
/// only while it is on screen. The eyes read the pointer position (GetCursorPos) five times a second while they are
/// on screen and at rest, 25 while they move or blink, and stop whenever the island is open, hidden or covered.
/// </summary>
public partial class IslandWindow
{
    public static readonly (string Id, string Name)[] Glances =
    {
        ("clock", "Clock"), ("system", "System"), ("status", "Status"), ("eyes", "Eyes"), ("quiet", "Quiet")
    };

    /// <summary>The glance in force: unknown values (a newer settings file, a typo) fall back to the clock.</summary>
    internal static string GlanceOf(string? setting) => Glances.Any(g => g.Id == setting) ? setting! : "clock";

    private string Glance => GlanceOf(S.Glance);

    /// <summary>
    /// What the right-hand side of the capsule shows: (time, date, cpu, status, eyes). Clock follows the Time, Date and
    /// CPU switches as before; never an empty capsule unless Quiet was chosen on purpose.
    /// </summary>
    internal static (bool Time, bool Date, bool Cpu, bool Status, bool Eyes) GlanceParts(string glance, bool showTime, bool showDate, bool showSystem, bool activity)
    {
        switch (glance)
        {
            case "system": return (false, false, true, false, false);
            case "status": return (false, false, false, true, false);
            case "eyes": return (false, false, false, false, true);
            case "quiet": return (false, false, false, false, false);
            default:
                var any = showTime || showDate || showSystem;
                return (showTime || (!any && !activity), showDate, showSystem, false, false);
        }
    }

    // ------------------------------------------------------------------ status row
    private StackPanel? _status;
    private FrameworkElement? _statusNet, _statusWifi, _statusBt;
    private TextBlock? _statusNetGlyph, _statusPct;
    private Border? _statusBatt;
    private Rectangle? _statusBattFill;
    private bool _statusLive;
    private Radio? _btRadioForStatus;

    private StackPanel BuildStatus()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // Wi-Fi as the app's own line icon; Ethernet and "no network" as glyphs.
        var wifi = _statusWifi = Ui.LineIcon(LineIcons.WiFi, 16, "T.Text");
        _statusNetGlyph = Ui.Icon("", 13);
        _statusNetGlyph.Visibility = Visibility.Collapsed;
        var net = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { wifi, _statusNetGlyph } };
        _statusNet = net;
        sp.Children.Add(net);
        _statusBt = Ui.LineIcon(LineIcons.Bluetooth, 16, "T.Text");
        _statusBt.Margin = new Thickness(10, 0, 0, 0);
        _statusBt.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(_statusBt);
        // A small battery drawn in shapes (crisp at any scale, no font glyph needed): body, nub, and a fill.
        var batt = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _statusBattFill = new Rectangle { HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1.2, RadiusY = 1.2, Margin = new Thickness(1.6) };
        _statusBatt = new Border { Width = 21, Height = 11, CornerRadius = new CornerRadius(3.2), BorderThickness = new Thickness(1.1), Child = _statusBattFill, VerticalAlignment = VerticalAlignment.Center };
        _statusBatt.SetResourceReference(Border.BorderBrushProperty, "T.Text2");
        var nub = new Border { Width = 1.8, Height = 4.4, CornerRadius = new CornerRadius(0, 1, 1, 0), Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        nub.SetResourceReference(Border.BackgroundProperty, "T.Text2");
        batt.Children.Add(_statusBatt);
        batt.Children.Add(nub);
        _statusPct = new TextBlock { FontSize = 12.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _statusPct.SetResourceReference(TextBlock.ForegroundProperty, "T.Text2");
        batt.Children.Add(_statusPct);
        sp.Children.Add(batt);
        return sp;
    }

    private void SetStatusLive(bool on)
    {
        if (on == _statusLive) return;
        _statusLive = on;
        if (on)
        {
            NetworkInformation.NetworkStatusChanged += OnStatusNetwork;
            Hub.Power.BatteryChanged += OnStatusBattery;
            Hub.Power.PowerSourceChanged += OnStatusPower;
            _ = HookBluetoothRadio();
            UpdateStatus();
        }
        else
        {
            NetworkInformation.NetworkStatusChanged -= OnStatusNetwork;
            Hub.Power.BatteryChanged -= OnStatusBattery;
            Hub.Power.PowerSourceChanged -= OnStatusPower;
            _statusSession++;   // a lookup still running must not hook the radio after this
            if (_btRadioForStatus is not null) { _btRadioForStatus.StateChanged -= OnStatusRadio; _btRadioForStatus = null; }
        }
    }

    /// <summary>Counts status sessions: a radio lookup that finishes after the row was hidden (or shown again) is dropped.</summary>
    private int _statusSession;

    private async Task HookBluetoothRadio()
    {
        var session = ++_statusSession;
        try
        {
            var r = await QuickControls.FindRadio(RadioKind.Bluetooth);
            if (session != _statusSession || !_statusLive) return;   // a newer lookup owns the radio now
            if (r is null) { UpdateStatus(); return; }
            _btRadioForStatus = r;
            r.StateChanged += OnStatusRadio;
            UpdateStatus();
        }
        catch (Exception ex) { Log.Warn("Bluetooth radio unavailable for the status glance: " + ex.Message); }
    }

    // Windows raises these on worker threads.
    private void OnStatusNetwork(object? sender) => Dispatcher.BeginInvoke(UpdateStatus);
    private void OnStatusRadio(Radio r, object _) => Dispatcher.BeginInvoke(UpdateStatus);
    private void OnStatusBattery(int _) => Dispatcher.BeginInvoke(UpdateStatus);
    private void OnStatusPower(bool _) => Dispatcher.BeginInvoke(UpdateStatus);

    /// <summary>"wifi", "ethernet" or "none" for the connection Windows uses for the internet right now.</summary>
    private static string ReadConnection()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p is null || p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) return "none";
            return p.IsWlanConnectionProfile ? "wifi" : "ethernet";
        }
        catch { return "none"; }
    }

    /// <summary>How full the little battery is drawn (0–1) and the colour key for its fill.</summary>
    internal static (double Fill, string Brush) BatteryLook(int percent, bool charging)
    {
        var fill = Math.Clamp(percent, 0, 100) / 100.0;
        return (Math.Max(fill, 0.08), charging ? "T.Good" : percent <= 20 ? "T.Danger" : "T.Text");
    }

    private void UpdateStatus()
    {
        if (_status is null || !_statusLive) return;
        var conn = ReadConnection();
        _statusWifi!.Visibility = conn == "wifi" ? Visibility.Visible : Visibility.Collapsed;
        _statusNetGlyph!.Visibility = conn == "wifi" ? Visibility.Collapsed : Visibility.Visible;
        _statusNetGlyph.Text = conn == "ethernet" ? GlyphEthernet : GlyphWifiOff;
        _statusNetGlyph.SetResourceReference(TextBlock.ForegroundProperty, conn == "none" ? "T.Text3" : "T.Text");
        _statusNet!.ToolTip = conn switch { "wifi" => "Wi-Fi connected", "ethernet" => "Ethernet connected", _ => "No internet connection" };

        var bt = _btRadioForStatus;
        _statusBt!.Visibility = bt is null ? Visibility.Collapsed : Visibility.Visible;
        _statusBt.Opacity = bt?.State == RadioState.On ? 1 : 0.35;
        _statusBt.ToolTip = bt?.State == RadioState.On ? "Bluetooth on" : "Bluetooth off";

        var battery = Hub.Power.Battery;
        var box = (FrameworkElement)_statusBatt!.Parent;
        box.Visibility = battery is null ? Visibility.Collapsed : Visibility.Visible;
        if (battery is { } pct)
        {
            var (fill, brush) = BatteryLook(pct, Hub.Power.OnAc == true);
            _statusBattFill!.Width = Math.Max(1, (21 - 2.2 - 3.2) * fill);
            _statusBattFill.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
            _statusPct!.Text = $"{pct}%";
            box.ToolTip = Hub.Power.OnAc == true ? $"Battery {pct}%, charging" : $"Battery {pct}%";
        }
        FitCompact();
    }

    // ------------------------------------------------------------------ eyes
    private Canvas? _eyes;
    private Border? _eyeL, _eyeR;
    private ScaleTransform? _lidL, _lidR;
    private TranslateTransform? _look;
    private DispatcherTimer? _eyesTimer;
    private Interop.Native.POINT _lastCursor;
    private DateTime _cursorMovedAt = DateTime.UtcNow, _nextBlink = DateTime.UtcNow.AddSeconds(3);
    private DateTime? _blinkStart;
    private double _lookX, _lookY, _lid = 1;
    private readonly Random _blinkDice = new();
    private const double EyesWidth = 46, EyesHeight = 22, BlinkMs = 130;

    private Canvas BuildEyes()
    {
        var c = new Canvas { Width = EyesWidth, Height = EyesHeight, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false, Background = Brushes.Transparent };
        _look = new TranslateTransform();
        Border Eye(double left, out ScaleTransform lid)
        {
            lid = new ScaleTransform(1, 1);
            var e = new Border { Width = 13, Height = 18, CornerRadius = new CornerRadius(6.5), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = lid };
            e.SetResourceReference(Border.BackgroundProperty, "T.Text");
            Canvas.SetLeft(e, left);
            Canvas.SetTop(e, (EyesHeight - 18) / 2);
            c.Children.Add(e);
            return e;
        }
        _eyeL = Eye(6, out _lidL);
        _eyeR = Eye(EyesWidth - 6 - 13, out _lidR);
        c.RenderTransform = _look;
        c.ToolTip = "Glance: eyes. Choose what the island shows in Settings › Dynamic Island.";
        return c;
    }

    /// <summary>
    /// Where the eyes look: towards the pointer, up to <paramref name="maxX"/>/<paramref name="maxY"/> away from the
    /// middle, reaching the edge once the pointer is <paramref name="reach"/> pixels away.
    /// </summary>
    internal static (double X, double Y) LookAt(double dx, double dy, double maxX, double maxY, double reach)
    {
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 0.5 || reach <= 0) return (0, 0);
        var k = Math.Min(d / reach, 1) / d;
        return (dx * k * maxX, dy * k * maxY);
    }

    /// <summary>The lids during a blink, 0 (open) to 1 (shut) back to 0 over <paramref name="ms"/>; 0 outside it.</summary>
    internal static double BlinkAt(double elapsedMs, double ms = 150)
    {
        if (elapsedMs <= 0 || elapsedMs >= ms) return 0;
        var t = elapsedMs / ms;
        return t < 0.45 ? t / 0.45 : 1 - (t - 0.45) / 0.55;
    }

    private void SetEyesLive(bool on)
    {
        if (!on) { _eyesTimer?.Stop(); return; }
        if (_eyesTimer is null)
        {
            _eyesTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _eyesTimer.Tick += (_, _) => TickEyes();
        }
        _cursorMovedAt = DateTime.UtcNow;
        if (!_eyesTimer.IsEnabled) _eyesTimer.Start();
    }

    private void TickEyes()
    {
        if (_eyes is null || _look is null) return;
        if (!_eyes.IsVisible) { _eyesTimer?.Stop(); return; }   // a card or meter covers the capsule; RefreshCompact restarts us
        var now = DateTime.UtcNow;
        Interop.Native.GetCursorPos(out var p);
        var moved = p.X != _lastCursor.X || p.Y != _lastCursor.Y;
        if (moved) { _lastCursor = p; _cursorMovedAt = now; }
        var idle = (now - _cursorMovedAt).TotalSeconds;
        var still = Motion.Intensity <= 0.01;

        // Look towards the pointer, from the eyes' own place on the screen (physical pixels on both sides).
        var scale = PresentationSource.FromVisual(_eyes)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var centre = _eyes.PointToScreen(new Point(EyesWidth / 2, EyesHeight / 2));
        var (tx, ty) = LookAt(p.X - centre.X, p.Y - centre.Y, 4.5, 2.6, 260 * scale);
        // Half-pixel steps: the pointer wandering about the screen only redraws the island when the eyes visibly move.
        tx = Step(tx); ty = Step(ty);
        // Small steps land in one frame; only a big jump (the pointer flung across the screen) glides over a few.
        var ease = still ? 1 : 0.5;
        _lookX = Approach(_lookX, tx, ease, 1.01);
        _lookY = Approach(_lookY, ty, ease, 1.01);

        // Lids: asleep after a minute without the pointer moving; blinks now and then while awake.
        double lid;
        if (idle > 60) lid = 0.16;
        else if (still) lid = 1;
        else
        {
            if (_blinkStart is null && now >= _nextBlink) _blinkStart = now;
            var shut = _blinkStart is { } b ? BlinkAt((now - b).TotalMilliseconds, BlinkMs) : 0;
            if (_blinkStart is { } b2 && (now - b2).TotalMilliseconds >= BlinkMs) { _blinkStart = null; _nextBlink = now.AddMilliseconds(3500 + _blinkDice.Next(5500)); }
            lid = 1 - 0.88 * shut;
        }
        _lid = Approach(_lid, lid, still ? 1 : idle > 60 ? 0.08 : 0.6, 0.01);

        // Only touch the visuals when something changed: an unchanged value would still cost a redraw of the island.
        if (_look.X != _lookX) _look.X = _lookX;
        if (_look.Y != _lookY) _look.Y = _lookY;
        if (_lidL!.ScaleY != _lid) _lidL.ScaleY = _lidR!.ScaleY = _lid;

        // Quick while the eyes are moving, five times a second once they have settled (the pointer may still move).
        var settled = _blinkStart is null && _lookX == tx && _lookY == ty && _lid == lid;
        var want = TimeSpan.FromMilliseconds(settled ? 200 : 40);
        if (_eyesTimer!.Interval != want) _eyesTimer.Interval = want;
    }

    /// <summary>Round to half a pixel.</summary>
    internal static double Step(double v) => Math.Round(v * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>Move part of the way to the target, landing exactly on it once close (so the eyes come to rest).</summary>
    internal static double Approach(double from, double to, double rate, double snap = 0.05)
    {
        var next = from + (to - from) * rate;
        return Math.Abs(to - next) < snap ? to : next;
    }

    // ------------------------------------------------------------------ wiring
    /// <summary>Show the chosen glance's content and run only the work it needs.</summary>
    private void ApplyGlance(bool status, bool eyes)
    {
        if (status && _status is null) { _status = BuildStatus(); CGlance.Children.Add(_status); }
        if (eyes && _eyes is null) { _eyes = BuildEyes(); CGlance.Children.Add(_eyes); }
        if (_status is not null) _status.Visibility = status ? Visibility.Visible : Visibility.Collapsed;
        if (_eyes is not null) _eyes.Visibility = eyes ? Visibility.Visible : Visibility.Collapsed;
        CGlance.Visibility = status || eyes ? Visibility.Visible : Visibility.Collapsed;
        UpdateGlanceLife();
    }

    /// <summary>Start or stop the status listeners and the eyes to match what is on screen right now.</summary>
    private void UpdateGlanceLife()
    {
        var onScreen = IsVisible && !_expanded && CNormal.Visibility == Visibility.Visible && CGlance.Visibility == Visibility.Visible;
        SetStatusLive(onScreen && _status?.Visibility == Visibility.Visible);
        SetEyesLive(onScreen && _eyes?.Visibility == Visibility.Visible);
    }
}
