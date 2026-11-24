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
/// The Dynamic Island: a topmost, non-activating layered window. The window itself never changes size;
/// the glass pill inside animates between its collapsed and expanded shapes, and fully transparent
/// pixels are click-through, so nothing outside the pill ever blocks the desktop.
/// </summary>
public partial class IslandWindow : Window
{
    private const double ExpandedWidth = 460, ExpandedHeight = 290, ControlsHeight = 366;
    /// <summary>Capsule thickness (Island › Shape).</summary>
    private static double CollapsedHeight => S.Thickness switch { "Slim" => 32, "Tall" => 46, _ => 38 };
    private static double CollapsedRadius => CollapsedHeight / 2;

    public static readonly string[] Shapes = { "Capsule", "Rounded", "Square", "Notch", "Drop" };
    public static readonly string[] Thicknesses = { "Slim", "Regular", "Tall" };

    /// <summary>
    /// The island's corners for a given "roundness" r (half the height for a capsule). Every shape keeps one
    /// number moving on the spring, so morphing between collapsed, card and panel works the same for all of them.
    /// </summary>
    internal static CornerRadius ShapeCorners(string shape, double r, string edge = "Top") => Orient(shape switch
    {
        "Rounded" => new CornerRadius(Math.Min(r, Math.Max(8, r * 0.5))),
        "Square" => new CornerRadius(Math.Min(r, Math.Max(5, r * 0.26))),
        "Notch" => new CornerRadius(0, 0, r * 0.8, r * 0.8),      // flush with the screen's edge, like a camera notch
        "Drop" => new CornerRadius(r * 0.36, r * 0.36, r, r),     // hangs like a drop: tighter at the edge, full round away from it
        _ => new CornerRadius(r),
    }, edge);

    /// <summary>Corners are described for the top edge; turn them so the "edge side" faces the island's actual edge.</summary>
    internal static CornerRadius Orient(CornerRadius c, string edge) => edge switch
    {
        "Bottom" => new CornerRadius(c.BottomLeft, c.BottomRight, c.TopRight, c.TopLeft),   // flipped
        "Left" => new CornerRadius(c.TopRight, c.BottomRight, c.BottomLeft, c.TopLeft),     // turned a quarter anticlockwise
        "Right" => new CornerRadius(c.BottomLeft, c.TopLeft, c.TopRight, c.BottomRight),    // turned a quarter clockwise
        _ => c,
    };

    public static readonly string[] Edges = { "Top", "Bottom", "Left", "Right" };
    private static string Edge => Edges.Contains(S.Edge) ? S.Edge : "Top";
    private static bool SideEdge => Edge is "Left" or "Right";

    /// <summary>The direction pointing off the screen from the island's edge (used to tuck it away and drop it in).</summary>
    private static (int X, int Y) Outward => Edge switch { "Bottom" => (0, 1), "Left" => (-1, 0), "Right" => (1, 0), _ => (0, -1) };

    /// <summary>Where growth starts from: the middle of the side that touches the screen edge.</summary>
    private static Point EdgeOrigin => Edge switch { "Bottom" => new Point(0.5, 1), "Left" => new Point(0, 0.5), "Right" => new Point(1, 0.5), _ => new Point(0.5, 0) };

    /// <summary>Anchor the glass (and its glow) to the chosen edge, so it grows away from that edge.</summary>
    private void ApplyEdge()
    {
        var (h, v) = Edge switch
        {
            "Bottom" => (HorizontalAlignment.Center, VerticalAlignment.Bottom),
            "Left" => (HorizontalAlignment.Left, VerticalAlignment.Center),
            "Right" => (HorizontalAlignment.Right, VerticalAlignment.Center),
            _ => (HorizontalAlignment.Center, VerticalAlignment.Top),
        };
        foreach (var el in new FrameworkElement[] { Pill, PillGlow }) { el.HorizontalAlignment = h; el.VerticalAlignment = v; el.RenderTransformOrigin = EdgeOrigin; }
        // The panel and cards stay put against the edge while the glass grows out from it, rather than sliding.
        Expanded.VerticalAlignment = v == VerticalAlignment.Center ? VerticalAlignment.Center : v;
        Expanded.HorizontalAlignment = h;
        Card.HorizontalAlignment = h;
    }

    /// <summary>Move the glass off the screen edge by <paramref name="distance"/> (0 = home) on a spring or a timed ease.</summary>
    private void ShiftOut(double distance, bool spring, double response = 0.46, double damping = 0.62, int ms = 300)
    {
        var (ux, uy) = Outward;
        if (spring)
        {
            Springs.To(PillShift, TranslateTransform.XProperty, ux * distance, response, damping);
            Springs.To(PillShift, TranslateTransform.YProperty, uy * distance, response, damping);
        }
        else
        {
            Motion.To(PillShift, TranslateTransform.XProperty, ux * distance, ms, Motion.EaseInOut);
            Motion.To(PillShift, TranslateTransform.YProperty, uy * distance, ms, Motion.EaseInOut);
        }
    }

    /// <summary>Start just off the edge, ready to drop in.</summary>
    private void PlaceOut(double distance)
    {
        var (ux, uy) = Outward;
        PillShift.X = ux * distance;
        PillShift.Y = uy * distance;
    }

    private void ApplyCorners()
    {
        var c = ShapeCorners(S.Shape, Math.Max(0, Radius), Edge);
        Pill.CornerRadius = c; PillRim.CornerRadius = c; PillGlow.CornerRadius = c;
        ArtWashA.CornerRadius = c; ArtWashB.CornerRadius = c;
    }
    private const double MorphResponse = 0.46, MorphDamping = 0.82;

    /// <summary>
    /// The capsule's own motion, shared by every change of size so the island always moves the same way: grows
    /// with a hint of overshoot, shrinks without any (a capsule that dips below its size looks unsure of itself).
    /// </summary>
    internal static class Shape
    {
        // Close 0.9 rather than 1: a fully damped spring crawls over its last few pixels (half a second for the
        // panel), while 0.9 lands in about half the time with an overshoot under half a pixel.
        public const double Response = 0.4, Open = 0.8, Close = 0.9, Fit = 0.88, CloseResponse = 0.36;
    }

    /// <summary>The Control Centre tab is taller (three rows of tiles); the others share one height.</summary>
    private double HeightFor(string tab) => tab == "controls" ? ControlsHeight : ExpandedHeight;

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(IslandWindow),
        new PropertyMetadata(19.0, (d, _) => ((IslandWindow)d).ApplyCorners()));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public string Device { get; }
    public bool IsExpanded => _expanded;

    private static IslandSettings S => Hub.Settings.Island;
    private IntPtr _hwnd;
    private bool _expanded, _pinned, _interactive, _autoHidden, _dragging, _pressed, _loadingUi, _closing;
    private string _tab = "media";
    private Point _pressPoint;
    private double _pressOffsetX, _pressOffsetY;
    private string _edgeAtPress = "Top";
    private IDisposable? _monitorSub;
    private readonly DispatcherTimer _clock, _collapseTimer, _mediaTick, _hoverPoll, _peekTimer, _hoverIntent;
    private DateTime _leftAt = DateTime.MinValue;
    private readonly Dictionary<string, (TextBlock Value, TextBlock Detail, Sparkline? Spark, ProgressBar? Bar)> _tiles = new();
    private readonly DownloadWatcher _downloads;

    public IslandWindow(string device, DownloadWatcher downloads)
    {
        Device = device;
        _downloads = downloads;
        InitializeComponent();
        Pill.Height = CollapsedHeight;
        Compact.Height = CollapsedHeight;
        Radius = CollapsedRadius;
        ApplyCorners();

        _clock = new DispatcherTimer(DispatcherPriority.Background);
        _clock.Tick += (_, _) => { UpdateClock(); AlignClock(); };
        _collapseTimer = new DispatcherTimer();
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (!BusyInside) Collapse(); else ArmCollapse(); };
        _mediaTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _mediaTick.Tick += (_, _) => UpdateMediaProgress();
        _hoverPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
        _hoverPoll.Tick += (_, _) => PollAutoHide();
        _peekTimer = new DispatcherTimer();
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); CPeek.Visibility = Visibility.Collapsed; CNormal.Visibility = Visibility.Visible; Pop(CNormal, 0.9); RefreshCompact(); };
        _hoverIntent = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _hoverIntent.Tick += (_, _) => { _hoverIntent.Stop(); if (Pill.IsMouseOver && !_expanded && S.ExpandOnHover) Expand(false); };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        Deactivated += (_, _) => { if (_interactive && _expanded && !_pinned && !_closing) Collapse(); };
        PreviewKeyDown += OnKey;

        Pill.MouseLeftButtonDown += OnPillDown;
        Pill.MouseMove += OnPillMove;
        Pill.MouseLeftButtonUp += OnPillUp;
        Pill.MouseEnter += (_, _) => { _collapseTimer.Stop(); if (S.ExpandOnHover && !_expanded) { _hoverIntent.Stop(); _hoverIntent.Start(); } };
        Pill.MouseLeave += (_, _) => { _leftAt = DateTime.UtcNow; ArmCollapse(true); };
        Pill.PreviewMouseMove += (_, e) =>
        {
            Poke();
            if (Pill.ActualWidth < 1) return;
            var p = e.GetPosition(Pill);
            var c = new Point(p.X / Pill.ActualWidth, p.Y / Pill.ActualHeight);
            PillSheenBrush.Center = c; PillSheenBrush.GradientOrigin = c;
            // Narrower light on the compact pill, a broader glow over the open panel.
            PillSheenBrush.RadiusX = _expanded ? 0.42 : 0.3; PillSheenBrush.RadiusY = _expanded ? 0.5 : 1.2;
        };
        Pill.MouseEnter += (_, _) => Motion.To(PillSheen, OpacityProperty, 1, 220);
        Pill.MouseLeave += (_, _) => Motion.To(PillSheen, OpacityProperty, 0, 320);
        // Liquid response to touch: the capsule lifts a little under the pointer, squashes when pressed and
        // springs back on release. Only the compact pill reacts; the open panel stays still to be worked in.
        Pill.MouseEnter += (_, _) => { if (!_expanded) SetPillScale(1.035, 0.32, 0.62); };
        Pill.MouseLeave += (_, _) => { if (!_expanded) SetPillScale(1, 0.4, 0.6); };
        Pill.PreviewMouseLeftButtonDown += (_, _) => { if (!_expanded) SetPillScale(0.95, 0.18, 1); };
        Pill.PreviewMouseLeftButtonUp += (_, _) => { if (!_expanded) SetPillScale(Pill.IsMouseOver ? 1.035 : 1, 0.36, 0.45); };
        Pill.LostMouseCapture += (_, _) => { if (!_expanded && !Pill.IsMouseOver) SetPillScale(1); };
        Pill.PreviewMouseDown += (_, _) => Poke();
        Pill.PreviewMouseWheel += (_, _) => Poke();
        PreviewKeyDown += (_, _) => Poke();
        Pill.MouseWheel += OnWheel;
        Pill.MouseRightButtonUp += (_, e) => { if (!_expanded) { ShowMenu(); e.Handled = true; } };
    }

    // ------------------------------------------------------------------ lifecycle
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowFx.MakeOverlay(this, true);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Never take focus on a click unless the user asked for keyboard interaction.
        if (msg == Native.WM_MOUSEACTIVATE && !_interactive) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
        // WPF has no sideways-wheel event; the high word of wParam is the signed scroll amount.
        if (msg == Native.WM_MOUSEHWHEEL && OnSideScroll((short)((wParam.ToInt64() >> 16) & 0xFFFF))) { handled = true; return IntPtr.Zero; }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Once start-up has settled, draw the hidden panel and card for a single frame at zero opacity, so WPF builds
    /// their visuals now rather than during the first open (which otherwise stalls one frame for ~90 ms).
    /// </summary>
    private void WarmUp()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            await Task.Delay(3500);   // after the entrance animation, never during it
            if (_expanded || _card || !IsLoaded) return;
            Expanded.IsHitTestVisible = false;
            Expanded.Opacity = 0; Card.Opacity = 0;
            Expanded.Visibility = Visibility.Visible; Card.Visibility = Visibility.Visible;
            await Task.Delay(120);   // a few frames
            if (!_expanded) Expanded.Visibility = Visibility.Hidden;
            if (!_card) Card.Visibility = Visibility.Collapsed;
            Expanded.IsHitTestVisible = true;
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildSystemTiles();
        BuildShortcuts();
        BuildFocusModes();
        Hub.Media.Changed += OnMediaChanged;
        Hub.Media.TrackChanged += OnTrackChanged;
        Hub.Timers.Changed += OnTimerChanged;
        Hub.Timers.Completed += OnTimerCompleted;
        Hub.Foreground.FullscreenChanged += ApplyVisibility;
        Hub.Foreground.ForegroundChanged += ApplyVisibility;
        Hub.Theme.Applied += OnThemeApplied;
        _downloads.Changed += OnDownloadsChanged;
        InitLive();
        InitCalendar();
        ApplySettings();
        UpdateClock();
        AlignClock();
        WarmUp();
        if (Motion.Intensity > 0.01)
        {
            Pill.Opacity = 0;
            PlaceOut(30);
            // Drop in once start-up work (widgets, wallpaper, devices) has finished, so the entrance runs on a free thread.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                Motion.To(Pill, OpacityProperty, S.Opacity, 420);
                Motion.To(PillShift, TranslateTransform.XProperty, 0, 520, Motion.Spring);
                Motion.To(PillShift, TranslateTransform.YProperty, 0, 520, Motion.Spring);
            });
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        foreach (var t in new[] { _clock, _collapseTimer, _mediaTick, _hoverPoll, _peekTimer, _hoverIntent }) t.Stop();
        _monitorSub?.Dispose();
        Hub.Media.Changed -= OnMediaChanged;
        Hub.Media.TrackChanged -= OnTrackChanged;
        Hub.Timers.Changed -= OnTimerChanged;
        Hub.Timers.Completed -= OnTimerCompleted;
        Hub.Foreground.FullscreenChanged -= ApplyVisibility;
        Hub.Foreground.ForegroundChanged -= ApplyVisibility;
        Hub.Theme.Applied -= OnThemeApplied;
        _downloads.Changed -= OnDownloadsChanged;
        CleanupLive();
    }

    private void OnThemeApplied() { ApplyMaterial(); }

    /// <summary>Re-read settings: scale, material, position, visible items, tabs.</summary>
    public void ApplySettings()
    {
        var scale = Math.Clamp(S.Scale, 0.8, 1.4);
        RootScale.ScaleX = RootScale.ScaleY = scale;
        Width = 560 * scale;
        Height = 420 * scale;
        ApplyShape();
        ApplyMaterial();
        BuildTabs();
        RefreshCompact();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place);
        if (S.AutoHide) _hoverPoll.Start(); else { _hoverPoll.Stop(); SetAutoHidden(false); }
        _hoverIntent.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.HoverDelayMs, 0, 1500));
        UpdateMonitorSubscription();
        ApplyVisibility();
        UpdatePrivacyDot();
    }

    /// <summary>Shape and thickness changes morph live on the same springs, so you see the new shape take form.</summary>
    private void ApplyShape()
    {
        EndSatellite(true);
        ApplyEdge();
        var m = S.Shape == "Notch" ? 0 : 1;   // a notch sits flush against its edge
        Pill.Margin = Edge switch { "Bottom" => new Thickness(0, 0, 0, m), "Left" => new Thickness(m, 0, 0, 0), "Right" => new Thickness(0, 0, m, 0), _ => new Thickness(0, m, 0, 0) };
        PillGlow.Margin = Pill.Margin;
        Compact.Height = CollapsedHeight;
        ApplyCorners();
        Pill.RefreshShadowClip();
        if (_expanded || _card) return;
        Springs.To(Pill, HeightProperty, CollapsedHeight, 0.45, 0.8);
        Springs.To(this, RadiusProperty, CollapsedRadius, 0.45, 0.8);
    }

    private void ApplyMaterial()
    {
        Pill.Opacity = Math.Clamp(S.Opacity, 0.3, 1);
        switch (S.Material)
        {
            case "Smoked": Pill.SampleWallpaper = false; Pill.SetResourceReference(GlassSurface.TintProperty, "G.TintSmoked"); break;
            case "Solid": Pill.SampleWallpaper = false; Pill.SetResourceReference(GlassSurface.TintProperty, "G.TintSolid"); break;
            case "Black": Pill.SampleWallpaper = false; Pill.Tint = Brushes.Black; break;
            default: Pill.SampleWallpaper = true; Pill.SetResourceReference(GlassSurface.TintProperty, "G.Tint"); break;
        }
        MatchLook(Pill);
        ApplyRim();
    }

    /// <summary>
    /// Black: true black with one fine light edge and nothing else (no wallpaper colour, glow or highlight), and
    /// always light text, in light mode too. The other materials keep the theme's own look.
    /// </summary>
    private void MatchLook(GlassSurface g)
    {
        var black = S.Material == "Black";
        if (!ReferenceEquals(g, Pill)) { g.SampleWallpaper = Pill.SampleWallpaper; g.Tint = Pill.Tint; }
        g.WashOpacity = black ? 0 : 1;
        g.OrnamentOpacity = black ? 0 : 1;
        var merged = g.Resources.MergedDictionaries;
        if (black && !merged.Contains(BlackInk)) merged.Add(BlackInk);
        else if (!black) merged.Remove(BlackInk);
    }

    /// <summary>The dark-mode colours for content on the Black material, whatever the app's own mode.</summary>
    private static readonly ResourceDictionary BlackInk = MakeBlackInk();

    private static ResourceDictionary MakeBlackInk()
    {
        var d = new ResourceDictionary();
        void B(string key, byte a, byte v = 0xFF) { var b = new SolidColorBrush(Color.FromArgb(a, v, v, v)); b.Freeze(); d[key] = b; }
        B("T.Text", 0xFF, 0xF5);
        B("T.Text2", 0xB3, 0xF5);
        B("T.Text3", 0x75, 0xF5);
        B("T.Card", 0x0E);
        B("T.CardHover", 0x18);
        B("T.Input", 0x12);
        B("T.Stroke", 0x17);
        B("T.StrokeStrong", 0x2E);
        B("T.Divider", 0x12);
        B("T.Track", 0x24);
        B("G.Border", 0x24);   // the one fine light edge
        B("G.Stroke", 0x00);   // no dark outline: black needs no extra contrast line
        return d;
    }

    /// <summary>Coloured rim presets. "Glass" keeps the material's own illuminated edge.</summary>
    public static readonly (string Id, string Name, string[] Colors)[] Rims =
    {
        ("Glass", "Glass", Array.Empty<string>()),
        ("Accent", "Accent", Array.Empty<string>()),
        ("Prism", "Prism", new[] { "#FFB340", "#FF4F8B", "#A35BFF", "#3E8BFF" }),
        ("Aurora", "Aurora", new[] { "#34D399", "#22D3EE", "#6366F1" }),
        ("Sunset", "Sunset", new[] { "#FB923C", "#F43F5E", "#C026D3" }),
        ("Ocean", "Ocean", new[] { "#22D3EE", "#3B82F6", "#1E40AF" }),
        ("Mono", "Mono", new[] { "#F4F4F5", "#71717A" }),
        ("Album", "Album", Array.Empty<string>()),   // the playing song's artwork colours; the accent when nothing plays
    };

    private void ApplyRim()
    {
        if (AgentsWaiting || _agentDoneFlash)
        {
            // A coding session is waiting for your answer: an orange rim and glow until it moves on, whatever rim is
            // chosen. A session that just finished: green for a moment.
            var wait = new SolidColorBrush(AgentsWaiting ? AgentWaiting : AgentOk);
            wait.Freeze();
            PillRim.BorderBrush = PillGlow.BorderBrush = wait;
            PillRim.Visibility = PillGlow.Visibility = Visibility.Visible;
            return;
        }
        var rim = Rims.FirstOrDefault(r => r.Id == S.Rim);
        if (rim.Id is null or "Glass") { PillRim.Visibility = PillGlow.Visibility = Visibility.Collapsed; return; }
        Color[] colors;
        if (rim.Id == "Album" && _artPalette is { } art)
            colors = new[] { WinOrbit.Wallpaper.ColorMath.ForMode(art.Accent, true), WinOrbit.Wallpaper.ColorMath.ForMode(art.Secondary, true), art.Dominant };
        else if (rim.Id is "Accent" or "Album")
        {
            var accent = (TryFindResource("T.Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0x7C, 0x5C, 0xFF);
            colors = new[] { WinOrbit.Wallpaper.ColorMath.Lerp(accent, Colors.White, 0.35), accent, WinOrbit.Wallpaper.ColorMath.Lerp(accent, Colors.Black, 0.25) };
        }
        else colors = rim.Colors.Select(c => (Color)ColorConverter.ConvertFromString(c)).ToArray();
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
        brush.Freeze();
        PillRim.BorderBrush = brush;
        PillRim.Visibility = Visibility.Visible;
        PillGlow.BorderBrush = brush;
        PillGlow.Visibility = S.RimGlow ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Place the window so the pill sits at the middle of its edge plus the user's offsets: along the edge from its
    /// centre, and away from the edge. The bottom edge and the sides keep clear of the taskbar.
    /// </summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        var mon = Monitors.Find(Device);
        if (!Native.GetWindowRect(_hwnd, out var r)) return;
        var (x, y) = WindowSpot(Edge, Offset(), S.Shape == "Notch", mon.Bounds, mon.Work, mon.Scale, r.Width, r.Height);
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Pill.UpdateBackdrop();
    }

    /// <summary>Where the island window goes for an edge (pure, so every edge can be tested).</summary>
    internal static (int X, int Y) WindowSpot(string edge, (double Along, double Away) offset, bool flush, Native.RECT bounds, Native.RECT work, double scale, int width, int height)
    {
        var side = edge is "Left" or "Right";
        var length = side ? bounds.Height : bounds.Width;
        var half = Math.Max(0, (length - (side ? 160 : 120) * scale) / 2);
        var along = (int)Math.Clamp(offset.Along * scale, -half, half);
        // A notch sits flush against its edge; the other shapes float at the chosen distance.
        var away = flush ? 0 : (int)Math.Clamp(offset.Away * scale, 0, (side ? bounds.Width : bounds.Height) / 3.0);
        var cx = bounds.Left + (bounds.Width - width) / 2 + along;
        var cy = bounds.Top + (bounds.Height - height) / 2 + along;
        return edge switch
        {
            "Bottom" => (cx, work.Bottom - height - away),
            "Left" => (work.Left + away, cy),
            "Right" => (work.Right - width - away, cy),
            _ => (cx, bounds.Top + away),
        };
    }

    private (double X, double Y) Offset()
        => S.Monitor == "all" && S.PerMonitorOffset.TryGetValue(Device, out var o) && o.Length == 2 ? (o[0], o[1]) : (S.OffsetX, S.OffsetY);

    private void SetOffset(double x, double y)
    {
        if (S.Monitor == "all") S.PerMonitorOffset[Device] = new[] { x, y };
        else { S.OffsetX = x; S.OffsetY = y; }
    }

    private void ApplyVisibility()
    {
        var fg = Hub.Foreground;
        var single = Monitors.All().Count == 1;
        var hide = ShouldHide(S, fg.ProcessName,
            fullscreenHere: fg.IsFullscreen && (fg.FullscreenDevice == Device || single),
            inFrontHere: !fg.DesktopForeground && (fg.ForegroundDevice == Device || single));
        if (hide && IsVisible) { if (_expanded) Collapse(true); EndSatellite(true); Hide(); }
        else if (!hide && !IsVisible && !_closing)
        {
            Show();
            Place();
            if (Motion.Intensity > 0.01 && !_autoHidden)   // an auto-hidden island stays tucked away until the pointer calls it
            {
                // Comes back the way it first arrived: dropping in from its edge with a little bounce.
                Pill.Opacity = 0;
                PlaceOut(24);
                Motion.To(Pill, OpacityProperty, S.Opacity, 260);
                ShiftOut(0, true, 0.42, 0.66);
            }
        }
    }

    /// <summary>
    /// Whether the island steps aside. Apps on the "keep showing" list win over fullscreen hiding; apps on the
    /// "always hide" list hide it whenever they're in front on its screen, fullscreen or not.
    /// </summary>
    internal static bool ShouldHide(IslandSettings s, string process, bool fullscreenHere, bool inFrontHere)
    {
        static string Norm(string p) => (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p).Trim();
        bool On(List<string> list) => process.Length > 0 && list.Any(a => string.Equals(Norm(a), Norm(process), StringComparison.OrdinalIgnoreCase));
        if (inFrontHere && On(s.HideForApps)) return true;
        return s.HideInFullscreen && fullscreenHere && !On(s.ShowOverFullscreenApps);
    }

    // ------------------------------------------------------------------ collapsed content
    private void AlignClock()
    {
        // Wake exactly on the next minute boundary instead of ticking every second.
        var now = DateTime.Now;
        _clock.Interval = TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(40);
        _clock.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CTime.Text = Fmt.Time(now, Hub.Settings.General.Use24Hour);
        CDate.Text = now.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        if (_expanded && _tab == "calendar") UpdateCalendar();
        if (!_expanded) FitCompact();
        CheckReminders(now);
    }

    public void RefreshCompact()
    {
        if (CPeek.Visibility == Visibility.Visible || CHud.Visibility == Visibility.Visible) { FitCompact(); return; }
        var media = Hub.Media;
        var timers = Hub.Timers;
        var showMedia = S.ShowMedia && media.HasTrack;
        var showCall = CallActive;
        var showTimer = S.ShowTimer && timers.IsActive && !showCall;
        var showDownload = S.WatchDownloads && _downloads.Active is not null && !showTimer && !showCall;

        // One live activity at a time keeps the pill calm: microphone > timer > download > media.
        UpdateCall(showCall);
        CTimer.Visibility = showTimer ? Visibility.Visible : Visibility.Collapsed;
        CDownload.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
        CMedia.Visibility = showMedia && !showTimer && !showDownload && !showCall ? Visibility.Visible : Visibility.Collapsed;
        if (showTimer) CTimerText.Text = $"{timers.Display}";
        if (showDownload) CDownloadText.Text = _downloads.Active!.Value.Name;
        if (CMedia.Visibility == Visibility.Visible)
        {
            CTitle.Text = CapsuleTitle();
            if (media.Art is not null) { CArt.Background = new ImageBrush(media.Art) { Stretch = Stretch.UniformToFill }; CArtGlyph.Visibility = Visibility.Collapsed; }
            else { CArt.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft"); CArtGlyph.Visibility = Visibility.Visible; }
        }
        UpdateEqualizerDrive();

        CTime.Visibility = S.ShowTime ? Visibility.Visible : Visibility.Collapsed;
        CDate.Visibility = S.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        CSys.Visibility = S.ShowSystem ? Visibility.Visible : Visibility.Collapsed;
        var activity = showCall || showTimer || showDownload || CMedia.Visibility == Visibility.Visible;
        var trailing = S.ShowTime || S.ShowDate || S.ShowSystem;
        CSep.Visibility = activity && trailing ? Visibility.Visible : Visibility.Collapsed;
        if (!activity && !trailing) { CTime.Visibility = Visibility.Visible; }   // never show an empty pill
        FitCompact();
        UpdateArtWash();
    }

    private bool _refitQueued;

    private void FitCompact()
    {
        if (_expanded) return;
        // Children that just became visible may not have been measured yet; measure fresh, then re-check once layout settles.
        foreach (var fe in new FrameworkElement[] { CTitle, CMedia, CCallText, CCall, CTimer, CDownload, CSys, CTime, CDate, CNormal, CPeek, Compact }) fe.InvalidateMeasure();
        if (!_refitQueued)
        {
            _refitQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { _refitQueued = false; if (!_expanded) FitNow(); });
        }
        FitNow();
    }

    private void FitNow()
    {
        if (_card) return;   // a card owns the capsule's size until it ends
        Compact.Measure(new Size(double.PositiveInfinity, CollapsedHeight));
        var target = EvenWidth(Compact.DesiredSize.Width + 4, 96, 500);
        // Morph the width on a lively spring; retargeting mid-flight keeps the motion continuous.
        var heading = Springs.TargetOf(Pill, WidthProperty) ?? Pill.Width;
        if (Math.Abs(target - heading) > 1.5) Springs.To(Pill, WidthProperty, target, Shape.Response, Shape.Fit);
    }

    /// <summary>Briefly show a message in the collapsed pill (track change, timer finished, download done).</summary>
    public void Peek(string glyph, string text, double seconds = 4, Color? color = null)
    {
        if (_expanded || !IsVisible) return;
        if (PillBusy || SatelliteShowing && !_card) { ShowSatellite(glyph, null, false, text, color, Math.Min(seconds, 4)); return; }   // the capsule keeps its activity
        EndCard();
        var fresh = CPeek.Visibility != Visibility.Visible;
        _hudTimer?.Stop();
        CHud.Visibility = Visibility.Collapsed;
        CPeekKey.Visibility = Visibility.Collapsed;
        CPeekGlyph.Visibility = Visibility.Visible;
        CPeekGlyph.Text = glyph;
        if (color is { } c) CPeekGlyph.Foreground = new SolidColorBrush(c);
        else CPeekGlyph.SetResourceReference(TextBlock.ForegroundProperty, "T.Accent");
        ShowPeek(text, seconds, fresh);
    }

    /// <summary>A lock key changed: a key cap with its indicator light, e.g. [A•] Caps Lock on.</summary>
    public void PeekKey(string cap, string text, bool on, double seconds = 1.6)
    {
        if (_expanded || !IsVisible) return;
        if (PillBusy || SatelliteShowing && !_card) { ShowSatellite(null, cap, on, text, null, seconds); return; }
        EndCard();
        var fresh = CPeek.Visibility != Visibility.Visible;
        _hudTimer?.Stop();
        CHud.Visibility = Visibility.Collapsed;
        CPeekGlyph.Visibility = Visibility.Collapsed;
        CPeekKey.Visibility = Visibility.Visible;
        CPeekKeyText.Text = cap;
        CPeekKeyLed.Fill = on ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) : new SolidColorBrush(Color.FromArgb(0x50, 0x9A, 0x9A, 0x9A));
        CPeekKey.Opacity = on ? 1 : 0.6;
        ShowPeek(text, seconds, fresh);
    }

    private void ShowPeek(string text, double seconds, bool fresh)
    {
        CPeekText.Text = text;
        CNormal.Visibility = Visibility.Collapsed;
        CPeek.Visibility = Visibility.Visible;
        SetAutoHidden(false);
        FitCompact();
        if (fresh) { Pop(CPeek); Droplet(1); } else Droplet(0.45);
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(seconds);
        _peekTimer.Start();
    }

    // ------------------------------------------------------------------ liquid motion
    private double _restScale = 1;

    /// <summary>The scale the pill settles at: 1, or slightly lifted while the pointer is over it.</summary>
    private void SetPillScale(double scale, double response = 0.32, double damping = 0.75)
    {
        _restScale = scale;
        Springs.To(PillScale, ScaleTransform.ScaleXProperty, scale, response, damping);
        Springs.To(PillScale, ScaleTransform.ScaleYProperty, scale, response, damping);
    }

    /// <summary>
    /// Something new arrived: the glass bulges down and narrows a touch, then wobbles back — like a drop of
    /// liquid that just took on more volume. Strength 1 is a new activity; smaller for an update in place.
    /// </summary>
    private void Droplet(double strength)
    {
        if (_expanded) return;
        // It swells away from its edge (down from the top, sideways from a side) and narrows along it.
        var (outwards, along) = SideEdge ? (ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty) : (ScaleTransform.ScaleYProperty, ScaleTransform.ScaleXProperty);
        // One soft swell that settles in about a third of a second, rather than a wobble that rings on.
        Springs.Kick(PillScale, outwards, 0.8 * strength, _restScale, 0.34, 0.58);
        Springs.Kick(PillScale, along, -0.22 * strength, _restScale, 0.34, 0.65);
        Pill.Flare();   // the rim glints as the glass wobbles (curved glass edges; nothing under Reduced Motion)
    }

    /// <summary>Content materialises: grows from slightly small with a soft bounce while fading in.</summary>
    /// <param name="cache">Render the content once as a bitmap while it scales (big panels), then go back to crisp live rendering.</param>
    private static void Pop(FrameworkElement el, double from = 0.88, double originY = 0.5, bool cache = false, double response = 0.34, double damping = 0.74, double originX = 0.5)
    {
        if (Motion.Intensity <= 0.01) { el.Opacity = 1; return; }
        var st = new ScaleTransform(from, from);
        el.RenderTransformOrigin = new Point(originX, originY);
        el.RenderTransform = st;
        el.Opacity = 0;
        if (cache) el.CacheMode = new BitmapCache { RenderAtScale = VisualTreeHelper.GetDpi(el).DpiScaleX, SnapsToDevicePixels = true };
        Springs.To(st, ScaleTransform.ScaleXProperty, 1, response, damping, () => { if (cache) el.CacheMode = null; });
        Springs.To(st, ScaleTransform.ScaleYProperty, 1, response, damping);
        Motion.To(el, OpacityProperty, 1, 170);
    }

    /// <summary>
    /// Blurs <paramref name="el"/> from one radius to another over <paramref name="ms"/>, then takes the blur off again
    /// (it costs a pass on the graphics card only while it moves). Nothing under Reduced Motion.
    /// </summary>
    private static void BlurPass(UIElement el, double from, double to, double ms)
    {
        if (Motion.Intensity <= 0.01) return;
        var fx = new System.Windows.Media.Effects.BlurEffect { Radius = from, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
        el.Effect = fx;
        var a = new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms / Math.Max(0.5, Motion.Intensity)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        a.Completed += (_, _) => { if (ReferenceEquals(el.Effect, fx)) el.Effect = null; };
        fx.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, a);
    }

    /// <summary>
    /// The glass moves in sub-pixel steps (no snapping, so slow motion stays smooth), but it must come to rest on
    /// whole pixels or centred text would sit half a pixel off and look soft. An even width centres exactly.
    /// </summary>
    private static double EvenWidth(double width, double min, double max) => Math.Round(Math.Clamp(width, min, max) / 2) * 2;

    private void OnMediaChanged() { RefreshCompact(); if (_expanded && _tab == "media") UpdateMedia(); }
    private void OnTrackChanged()
    {
        // New song: title and artwork glide in rather than snapping.
        if (_expanded && _tab == "media") { Motion.Enter(MTitle, 8, 300); Motion.Enter(MArtist, 10, 360); Motion.Enter(MArt, 6, 320); }
        else if (CMedia.Visibility == Visibility.Visible) Motion.Enter(CMedia, 6, 280);
        if (S.PeekOnMediaChange && S.ShowMedia && !string.IsNullOrWhiteSpace(Hub.Media.Title))
            ShowMediaCard();
    }
    private void OnTimerChanged() { if (!_expanded) RefreshCompact(); else if (_tab == "focus") UpdateFocus(); }
    private void OnTimerCompleted(TimerMode mode)
    {
        var (title, sub) = TimerDoneText(mode, Hub.Timers.Total, Hub.Timers.BreakMinutes);
        if (_expanded || !IsVisible) Peek(Glyph.Check, $"{title} — {sub}", 7);
        else ShowCard(Glyph.Check, title, sub, Color.FromRgb(0x30, 0xD1, 0x58), 8);
    }

    /// <summary>The "done" card's words. <paramref name="total"/> is the countdown's length (focus/break use their own).</summary>
    internal static (string Title, string Subtitle) TimerDoneText(TimerMode mode, TimeSpan total, int breakMinutes) => mode switch
    {
        TimerMode.Focus => ("Focus block complete", $"Time for a {breakMinutes}-minute break"),
        TimerMode.Break => ("Break over", "Ready for the next focus block"),
        _ => ("Timer done", Launcher.TimerCommand.Describe(total) + " finished"),
    };
    private void OnDownloadsChanged()
    {
        if (!S.WatchDownloads) return;
        if (_downloads.JustCompletedPath is { } path) ShowDownloadCard(path);
        RefreshCompact();
    }

    // ------------------------------------------------------------------ expand / collapse
    public void Toggle(bool keyboard)
    {
        if (_expanded) Collapse(); else Expand(keyboard);
    }

    public void Expand(bool keyboard)
    {
        if (_expanded || !IsVisible) { if (keyboard && _expanded) SetInteractive(true); return; }
        EndSatellite(true);
        _expanded = true;
        _peekTimer.Stop();
        if (_card) { _card = false; _cardTimer?.Stop(); SetCardActions(null, null, null); Card.Visibility = Visibility.Collapsed; Card.Opacity = 0; }
        CPeek.Visibility = Visibility.Collapsed; CNormal.Visibility = Visibility.Visible;
        _hudTimer?.Stop(); CHud.Visibility = Visibility.Collapsed;
        SetAutoHidden(false);
        UpdateEqualizerDrive();
        UpdateCallDrive();
        Pill.Cursor = Cursors.Arrow;
        Compact.IsHitTestVisible = false;
        // Fill and lay out the panel BEFORE anything moves. Doing this mid-motion is what made the first frames
        // of opening late (25–50 ms); now the motion starts only once the heavy work is done.
        Expanded.Opacity = 0;
        Expanded.Visibility = Visibility.Visible;
        if (AgentsWaiting && ActiveTabs().Contains("agents")) _tab = "agents";   // open where the waiting session is
        Expanded.Height = HeightFor(_tab) - 24;
        ShowTab(_tab);
        Expanded.UpdateLayout();
        Motion.To(Compact, OpacityProperty, 0, 110);
        // The capsule swells into the panel on one shared spring, so width, height and corners move as one body
        // and overshoot together a few pixels before settling, like a bubble popping open.
        // Damping 0.82: a ~1.5 % overshoot that reads as weight, without the visible reverse at the end that a
        // bouncier spring gives a panel this large. The content scales on the same response and damping, so glass
        // and content arrive together instead of wobbling out of step.
        SetPillScale(1, 0.4, 0.7);
        Springs.To(Pill, WidthProperty, ExpandedWidth, MorphResponse, MorphDamping);
        Springs.To(Pill, HeightProperty, HeightFor(_tab), MorphResponse, MorphDamping);
        Springs.To(this, RadiusProperty, 30, MorphResponse, MorphDamping);
        Pop(Expanded, 0.94, EdgeOrigin.Y, cache: true, response: MorphResponse, damping: MorphDamping, originX: EdgeOrigin.X);   // the content grows out of the capsule as it opens
        if (Pill.IsLiquid)
        {
            // Liquid glass: light runs round the rim as the drop swells, and the content comes into focus through it.
            Pill.Sweep();
            BlurPass(Expanded, 9, 0, 280);
        }
        if (keyboard) SetInteractive(true);
        ArmCollapse();   // the short count applies only once the pointer has been inside and left
    }

    public void Collapse(bool immediate = false)
    {
        if (!_expanded) return;
        _expanded = false;
        _pinned = false;
        PinButton.SetResourceReference(ForegroundProperty, "T.Text");
        _collapseTimer.Stop();
        _mediaTick.Stop();
        UpdateMonitorSubscription();
        SetInteractive(false);
        Pill.Cursor = Cursors.Hand;
        Compact.IsHitTestVisible = true;
        // Drawing back in: nearly critically damped, so the capsule lands on its size without a visible dip (a dip
        // squeezed the clock for a few frames at the very end) and without crawling over the last pixels.
        var response = immediate ? 0 : Shape.CloseResponse;
        Motion.To(Expanded, OpacityProperty, 0, immediate ? 0 : 120, null, () => { if (!_expanded) Expanded.Visibility = Visibility.Hidden; });   // Hidden keeps it laid out for the next open
        if (Pill.IsLiquid && !immediate)
        {
            Pill.Sweep(0.6);
            BlurPass(Expanded, 0, 6, 120);   // the content melts into the glass as it draws back
        }
        Springs.To(Pill, HeightProperty, CollapsedHeight, response, Shape.Close);
        Springs.To(this, RadiusProperty, CollapsedRadius, response, Shape.Close);
        Motion.To(Compact, OpacityProperty, 1, immediate ? 0 : 260);
        RefreshCompact();
        Compact.Measure(new Size(double.PositiveInfinity, CollapsedHeight));
        Springs.To(Pill, WidthProperty, EvenWidth(Compact.DesiredSize.Width + 4, 96, 500), response, Shape.Close);
        if (!immediate && !Pill.IsMouseOver) SetPillScale(1);
    }

    /// <summary>
    /// Shrink back after a few seconds without interaction (default 6 s), or 2.5 s after the pointer leaves.
    /// Any pointer movement, click, scroll or key press restarts the count.
    /// </summary>
    private void ArmCollapse(bool leaving = false)
    {
        _collapseTimer.Stop();
        if (!_expanded || _pinned || S.AutoCollapseSeconds <= 0) return;
        var seconds = Math.Max(1, S.AutoCollapseSeconds);
        _collapseTimer.Interval = TimeSpan.FromSeconds(leaving ? Math.Min(2.5, seconds) : seconds);
        _collapseTimer.Start();
    }

    private void Poke() { if (_expanded) ArmCollapse(); }

    // A pointer resting on the open island (reading the weather, say) keeps it open; leaving starts the short count.
    private bool BusyInside => _pinned || _seekDragging || Pill.IsMouseOver || NotesBox.IsKeyboardFocusWithin || CalAdd.IsKeyboardFocusWithin;

    /// <summary>Keyboard interaction is opt-in: only then does the island accept focus.</summary>
    private void SetInteractive(bool on)
    {
        if (_interactive == on || _hwnd == IntPtr.Zero) return;
        _interactive = on;
        if (on)
        {
            Native.RemoveExStyle(_hwnd, Native.WS_EX_NOACTIVATE);
            Activate();
            Focus();
            Keyboard.Focus(this);
        }
        else Native.AddExStyle(_hwnd, Native.WS_EX_NOACTIVATE);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_expanded) return;
        var typing = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.Escape) { Collapse(); e.Handled = true; return; }
        if (typing) return;
        var tabs = ActiveTabs();
        var i = Math.Max(0, tabs.IndexOf(_tab));
        if (e.Key is Key.Right or Key.Left && Keyboard.FocusedElement is not Slider)
        {
            var next = tabs[(i + (e.Key == Key.Right ? 1 : tabs.Count - 1)) % tabs.Count];
            ShowTab(next);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && _tab == "media" && Keyboard.FocusedElement is not Button) { Hub.Media.PlayPause(); e.Handled = true; }
        else if (e.Key == Key.Space && _tab == "focus" && Keyboard.FocusedElement is not Button) { Hub.Timers.Toggle(); e.Handled = true; }
        else if (e.Key is >= Key.D1 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            var n = e.Key - Key.D1;   // 1–9 pick a module
            if (n < tabs.Count) { ShowTab(tabs[n]); e.Handled = true; }
        }
    }

    // ------------------------------------------------------------------ pointer: click, drag, wheel
    private void OnPillDown(object sender, MouseButtonEventArgs e)
    {
        if (_expanded) return;
        _pressed = true;
        _dragging = false;
        Native.GetCursorPos(out var p);
        _pressPoint = new Point(p.X, p.Y);
        (_pressOffsetX, _pressOffsetY) = Offset();
        _edgeAtPress = Edge;
        Pill.CaptureMouse();
    }

    private void OnPillMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        // A release that never reached the island (it went to another window) must not leave a drag armed.
        // (OnPillUp calls this too, with the button already released: that is the normal end, not a lost one.)
        if (e is not MouseButtonEventArgs && e.LeftButton != MouseButtonState.Pressed)
        {
            _pressed = false;
            if (_dragging) { _dragging = false; Hub.Store.Save(); }
            Pill.ReleaseMouseCapture();
            return;
        }
        Native.GetCursorPos(out var p);
        double dx = p.X - _pressPoint.X, dy = p.Y - _pressPoint.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 7) return;
        if (!_dragging && _card && _cardFile is { } file && File.Exists(file))
        {
            // Dragging a file card carries the file, not the island.
            _pressed = false;
            Pill.ReleaseMouseCapture();
            DragCardFile(file);
            return;
        }
        _dragging = true;
        var mon = Monitors.Find(Device);
        var scale = mon.Scale;

        // Carried right up to a different edge of the screen: the island moves to that edge and the drag carries on.
        // It has to leave its own edge first, so a corner doesn't flip it back and forth between two edges.
        if (EdgeNear(new Point(p.X, p.Y), mon.Bounds, 70 * scale) is { } edge && edge != Edge && EdgeDistance(new Point(p.X, p.Y), mon.Bounds, Edge) > 110 * scale)
        {
            S.Edge = edge;
            var along = edge is "Left" or "Right" ? p.Y - (mon.Bounds.Top + mon.Bounds.Height / 2.0) : p.X - (mon.Bounds.Left + mon.Bounds.Width / 2.0);
            SetOffset(S.KeepCentred ? 0 : Math.Round(along / scale), 10);
            _pressPoint = new Point(p.X, p.Y);
            (_pressOffsetX, _pressOffsetY) = Offset();
            Radius = CollapsedRadius;
            ApplyShape();
            Place();
            Droplet(0.6);
            return;
        }

        // Along the edge and away from it, whichever edge that is.
        var (dAlong, dAway) = Edge switch
        {
            "Bottom" => (dx, -dy),
            "Left" => (dy, dx),
            "Right" => (dy, -dx),
            _ => (dx, dy),
        };
        var x = S.KeepCentred ? 0 : Math.Round(_pressOffsetX + dAlong / scale);
        // Magnetic centre: within 18 px of the middle it clicks to exactly centred.
        if (S.SnapToCentre && Math.Abs(x) <= 18) x = 0;
        SetOffset(x, Math.Clamp(Math.Round(_pressOffsetY + dAway / scale), 0, 120));
        Place();
    }

    /// <summary>The screen edge the pointer is pressed right up against (within <paramref name="reach"/> px), or null.</summary>
    internal static string? EdgeNear(Point p, Native.RECT b, double reach)
    {
        var d = new (string Edge, double Distance)[]
        {
            ("Top", p.Y - b.Top), ("Bottom", b.Bottom - p.Y), ("Left", p.X - b.Left), ("Right", b.Right - p.X),
        }.OrderBy(e => e.Distance).First();
        return d.Distance <= reach ? d.Edge : null;
    }

    /// <summary>How far the pointer is from one edge of the screen.</summary>
    internal static double EdgeDistance(Point p, Native.RECT b, string edge) => edge switch
    {
        "Bottom" => b.Bottom - p.Y,
        "Left" => p.X - b.Left,
        "Right" => b.Right - p.X,
        _ => p.Y - b.Top,
    };

    /// <summary>Put the island back in the exact horizontal centre (keeps its height from the top).</summary>
    public void Centre()
    {
        var (_, y) = Offset();
        SetOffset(0, y);
        Hub.Store.Save();
        Place();
        Peek(Glyph.Check, "Centred", 1.2);
    }

    private void OnPillUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        if (_dragging) OnPillMove(sender, e);
        _pressed = false;
        Pill.ReleaseMouseCapture();
        if (_dragging)
        {
            _dragging = false;
            Hub.Store.Save();
            if (Edge != _edgeAtPress) Hub.Island.Apply();   // islands on other displays follow to the new edge
            else if (Offset().X == 0 && _pressOffsetX != 0) Peek(Glyph.Check, "Centred", 1.0);
        }
        else if (_card && _cardClick is { } click) { EndCard(); click(); }
        else Expand(false);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e) { if (OnUpDownScroll(e.Delta)) e.Handled = true; }

    /// <summary>The wheel over the collapsed capsule changes the volume. Returns true when it was used.</summary>
    private bool OnUpDownScroll(int delta)
    {
        if (_expanded || delta == 0) return false;
        var v = Audio.GetVolume();
        if (v is null) return false;
        var nv = Math.Clamp(v.Value + (delta > 0 ? 0.02 : -0.02), 0, 1);
        Audio.SetVolume(nv);
        if (!S.VolumeHud) Peek(nv <= 0.001 ? Glyph.Mute : Glyph.Volume, $"Volume {nv * 100:0}%", 1.4);   // otherwise the HUD shows it
        return true;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, bool? check = null)
        {
            var item = new MenuItem { Header = header, IsChecked = check == true };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Open WinOrbit", () => App.Current.ShowMain("island"));
        menu.Items.Add(new Separator());
        Add("Expand on hover", () => { S.ExpandOnHover = !S.ExpandOnHover; Hub.Store.Save(); }, S.ExpandOnHover);
        Add("Auto-hide", () => { S.AutoHide = !S.AutoHide; Hub.Store.Save(); Hub.Island.Apply(); }, S.AutoHide);
        Add("Centre the island", Centre);
        Add("Keep centred", () => { S.KeepCentred = !S.KeepCentred; if (S.KeepCentred) Centre(); Hub.Store.Save(); }, S.KeepCentred);
        Add("Reset to top centre", () => { S.Edge = "Top"; S.OffsetX = 0; S.OffsetY = 10; S.PerMonitorOffset.Clear(); Hub.Store.Save(); Hub.Island.Apply(); });
        var edges = new MenuItem { Header = "Screen edge" };
        foreach (var edge in Edges)
        {
            var item = new MenuItem { Header = edge, IsChecked = Edge == edge };
            item.Click += (_, _) => { S.Edge = edge; S.OffsetX = 0; S.PerMonitorOffset.Clear(); Hub.Store.Save(); Hub.Island.Apply(); };
            edges.Items.Add(item);
        }
        menu.Items.Add(edges);
        menu.Items.Add(new Separator());
        Add("Hide the island", () => { S.Enabled = false; Hub.Store.Save(); Hub.Island.Apply(); });
        menu.PlacementTarget = Pill;
        menu.Placement = Edge switch
        {
            "Bottom" => System.Windows.Controls.Primitives.PlacementMode.Top,
            "Left" => System.Windows.Controls.Primitives.PlacementMode.Right,
            "Right" => System.Windows.Controls.Primitives.PlacementMode.Left,
            _ => System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ auto-hide
    private void PollAutoHide()
    {
        if (!S.AutoHide || _expanded || !IsVisible || CPeek.Visibility == Visibility.Visible || SatelliteShowing) return;
        if (!Native.GetCursorPos(out var p) || !Native.GetWindowRect(_hwnd, out var r)) return;
        var mon = Monitors.Find(Device);
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        var band = 190 * mon.Scale;
        var touch = 3 * mon.Scale;
        // The pointer pushed into the island's edge of the screen, near where it sits.
        var nearEdge = Edge switch
        {
            "Bottom" => p.Y >= r.Bottom - touch && p.Y <= mon.Bounds.Bottom && Math.Abs(p.X - cx) < band,
            "Left" => p.X <= r.Left + touch && p.X >= mon.Bounds.Left && Math.Abs(p.Y - cy) < band,
            "Right" => p.X >= r.Right - touch && p.X <= mon.Bounds.Right && Math.Abs(p.Y - cy) < band,
            _ => p.Y <= r.Top + touch && p.Y >= mon.Bounds.Top && Math.Abs(p.X - cx) < band,
        };
        if (_autoHidden && nearEdge) SetAutoHidden(false);
        else if (!_autoHidden && !Pill.IsMouseOver && !_pressed && (DateTime.UtcNow - _leftAt).TotalSeconds > 1.6) SetAutoHidden(true);
    }

    private void SetAutoHidden(bool hidden)
    {
        if (_autoHidden == hidden) return;
        _autoHidden = hidden;
        if (!hidden) _leftAt = DateTime.UtcNow;
        var (_, oy) = Offset();
        var depth = SideEdge ? Pill.ActualWidth : CollapsedHeight;
        if (hidden) ShiftOut(depth + oy + 8, false);
        else ShiftOut(0, true);   // drops back in with a little bounce
        Pill.IsHitTestVisible = !hidden;
    }

    // ------------------------------------------------------------------ tabs
    private static readonly (string Id, string Glyph, string Name)[] AllTabs =
    {
        ("media", Glyph.Music, "Media"), ("system", Glyph.Chip, "System"), ("controls", Glyph.Settings, "Control Centre"),
        ("focus", Glyph.Stopwatch, "Focus timer"), ("calendar", Glyph.Calendar, "Calendar"), ("weather", Glyph.Cloud, "Weather"), ("notes", Glyph.Note, "Notes"),
        ("clipboard", Glyph.Copy, "Clipboard"), ("shelf", GlyphShelf, "File shelf"), ("agents", GlyphAgents, "Coding agents")
    };

    private List<string> ActiveTabs()
    {
        // Tabs added in later versions appear once for existing users; after that their choice is respected.
        if (S.ModulesVersion < 1)
        {
            foreach (var id in new[] { "clipboard", "shelf" }) if (!S.Modules.Contains(id)) S.Modules.Add(id);
            S.ModulesVersion = 1;
            Hub.Store.Save();
        }
        if (S.ModulesVersion < 2)
        {
            if (S.AutoCollapseSeconds == 8) S.AutoCollapseSeconds = 6;   // the old default felt long
            S.ModulesVersion = 2;
            Hub.Store.Save();
        }
        if (S.ModulesVersion < 3)
        {
            if (!S.Modules.Contains("weather")) S.Modules.Add("weather");
            S.ModulesVersion = 3;
            Hub.Store.Save();
        }
        // The Agents tab exists only while coding agents are on (its own switch, off by default).
        var list = AllTabs.Select(t => t.Id).Where(id => id == "agents" ? S.CodingAgents : S.Modules.Contains(id)).ToList();
        if (list.Count == 0) list.Add("media");
        return list;
    }

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        var active = ActiveTabs();
        if (!active.Contains(_tab)) _tab = active[0];
        // Each button narrows as modules are added so the bar always clears the pin, settings and collapse buttons
        // (about 326 px are free beside them; each button is its ~16 px glyph, its padding and a 2 px margin each side).
        var pad = Math.Clamp((326.0 / active.Count - 20) / 2, 5, 11);
        foreach (var t in AllTabs.Where(t => active.Contains(t.Id)))
        {
            var rb = new RadioButton { GroupName = "IslandTabs" + GetHashCode(), ToolTip = t.Name, Tag = t.Id, Padding = new Thickness(pad, 6, pad, 6), IsChecked = t.Id == _tab };
            rb.SetResourceReference(StyleProperty, "Segment");
            System.Windows.Automation.AutomationProperties.SetName(rb, t.Name);
            var icon = new TextBlock { Text = t.Glyph, FontSize = 14, FontFamily = (FontFamily)FindResource("F.Icons") };
            rb.Content = icon;
            rb.Checked += (_, _) => { if (!_loadingUi) ShowTab((string)rb.Tag); };
            Tabs.Children.Add(rb);
        }
    }

    private void ShowTab(string id)
    {
        if (_expanded && HeightFor(id) != HeightFor(_tab))
        {
            Expanded.Height = HeightFor(id) - 24;
            Motion.To(Pill, HeightProperty, HeightFor(id), 320, Motion.Spring);
        }
        _tab = id;
        UpdateArtWash();
        _loadingUi = true;
        foreach (var rb in Tabs.Children.OfType<RadioButton>()) rb.IsChecked = (string)rb.Tag == id;
        _loadingUi = false;
        TabMedia.Visibility = id == "media" ? Visibility.Visible : Visibility.Collapsed;
        TabSystem.Visibility = id == "system" ? Visibility.Visible : Visibility.Collapsed;
        TabControls.Visibility = id == "controls" ? Visibility.Visible : Visibility.Collapsed;
        TabFocus.Visibility = id == "focus" ? Visibility.Visible : Visibility.Collapsed;
        TabCalendar.Visibility = id == "calendar" ? Visibility.Visible : Visibility.Collapsed;
        TabWeather.Visibility = id == "weather" ? Visibility.Visible : Visibility.Collapsed;
        TabNotes.Visibility = id == "notes" ? Visibility.Visible : Visibility.Collapsed;
        TabClipboard.Visibility = id == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
        TabShelf.Visibility = id == "shelf" ? Visibility.Visible : Visibility.Collapsed;
        TabAgents.Visibility = id == "agents" ? Visibility.Visible : Visibility.Collapsed;
        var view = id switch { "media" => (FrameworkElement)TabMedia, "system" => TabSystem, "controls" => TabControls, "focus" => TabFocus, "calendar" => TabCalendar, "weather" => TabWeather, "clipboard" => TabClipboard, "shelf" => TabShelf, "agents" => TabAgents, _ => TabNotes };
        Motion.Enter(view, 8, 220);
        _mediaTick.Stop();
        UpdateMonitorSubscription();
        switch (id)
        {
            case "media": UpdateMedia(); LoadVolume(); break;
            case "controls": LoadVolume(); LoadBrightness(); RefreshQuick(); break;
            case "focus": UpdateFocus(); break;
            case "calendar": _calDay = DateTime.Today; CalView.Select(_calDay); UpdateCalendar(); break;
            case "weather": ShowWeather(); break;
            case "clipboard": RenderClipboard(); break;
            case "shelf": RenderShelf(); break;
            case "agents": RenderAgents(); break;
            case "notes":
                _loadingUi = true;
                NotesBox.Text = Hub.Store.Data.Notes.TryGetValue(UserData.IslandNoteKey, out var n) ? n : "";
                NotesHint.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                _loadingUi = false;
                break;
        }
    }

    /// <summary>The system sampler runs only while something on the island actually displays it.</summary>
    private void UpdateMonitorSubscription()
    {
        var need = (_expanded && _tab == "system") || (S.ShowSystem && !_expanded && IsVisible);
        if (need && _monitorSub is null) _monitorSub = Hub.Monitor.Subscribe(OnSample);
        else if (!need && _monitorSub is not null) { _monitorSub.Dispose(); _monitorSub = null; }
    }

}
