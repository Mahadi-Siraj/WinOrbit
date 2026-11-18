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
    private const double ExpandedWidth = 460, ExpandedHeight = 290, ControlsHeight = 366, CollapsedHeight = 38;

    /// <summary>The Control Centre tab is taller (three rows of tiles); the others share one height.</summary>
    private double HeightFor(string tab) => tab == "controls" ? ControlsHeight : ExpandedHeight;

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(IslandWindow),
        new PropertyMetadata(19.0, (d, e) => ((IslandWindow)d).Pill.CornerRadius = new CornerRadius((double)e.NewValue)));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public string Device { get; }
    public bool IsExpanded => _expanded;

    private static IslandSettings S => Hub.Settings.Island;
    private IntPtr _hwnd;
    private bool _expanded, _pinned, _interactive, _autoHidden, _dragging, _pressed, _loadingUi, _closing;
    private string _tab = "media";
    private Point _pressPoint;
    private double _pressOffsetX, _pressOffsetY;
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
        Pill.CornerRadius = new CornerRadius(19);

        _clock = new DispatcherTimer(DispatcherPriority.Background);
        _clock.Tick += (_, _) => { UpdateClock(); AlignClock(); };
        _collapseTimer = new DispatcherTimer();
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (!BusyInside) Collapse(); else ArmCollapse(); };
        _mediaTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _mediaTick.Tick += (_, _) => UpdateMediaProgress();
        _hoverPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
        _hoverPoll.Tick += (_, _) => PollAutoHide();
        _peekTimer = new DispatcherTimer();
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); CPeek.Visibility = Visibility.Collapsed; CNormal.Visibility = Visibility.Visible; RefreshCompact(); };
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
        return IntPtr.Zero;
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
        Hub.Theme.Applied += OnThemeApplied;
        _downloads.Changed += OnDownloadsChanged;
        InitLive();
        ApplySettings();
        UpdateClock();
        AlignClock();
        if (Motion.Intensity > 0.01)
        {
            Pill.Opacity = 0;
            PillShift.Y = -30;
            Motion.To(Pill, OpacityProperty, S.Opacity, 420);
            Motion.To(PillShift, TranslateTransform.YProperty, 0, 520, Motion.Spring);
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

    private void ApplyMaterial()
    {
        Pill.Opacity = Math.Clamp(S.Opacity, 0.3, 1);
        switch (S.Material)
        {
            case "Smoked": Pill.SampleWallpaper = false; Pill.SetResourceReference(GlassSurface.TintProperty, "G.TintSmoked"); break;
            case "Solid": Pill.SampleWallpaper = false; Pill.SetResourceReference(GlassSurface.TintProperty, "G.TintSolid"); break;
            default: Pill.SampleWallpaper = true; Pill.SetResourceReference(GlassSurface.TintProperty, "G.Tint"); break;
        }
    }

    /// <summary>Place the window so the pill sits at top-centre of its display plus the user's offset.</summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        var mon = Monitors.Find(Device);
        if (!Native.GetWindowRect(_hwnd, out var r)) return;
        var (ox, oy) = Offset();
        var half = (mon.Bounds.Width - 120 * mon.Scale) / 2;
        var dx = Math.Clamp(ox * mon.Scale, -half, half);
        var dy = Math.Clamp(oy * mon.Scale, 0, mon.Bounds.Height - 60 * mon.Scale);
        var x = mon.Bounds.Left + (mon.Bounds.Width - r.Width) / 2 + (int)dx;
        var y = mon.Bounds.Top + (int)dy;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Pill.UpdateBackdrop();
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
        var hide = S.HideInFullscreen && Hub.Foreground.IsFullscreen && (Hub.Foreground.FullscreenDevice == Device || Monitors.All().Count == 1);
        if (hide && IsVisible) { if (_expanded) Collapse(true); Hide(); }
        else if (!hide && !IsVisible && !_closing) { Show(); Place(); }
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
    }

    public void RefreshCompact()
    {
        if (CPeek.Visibility == Visibility.Visible || CHud.Visibility == Visibility.Visible) { FitCompact(); return; }
        var media = Hub.Media;
        var timers = Hub.Timers;
        var showMedia = S.ShowMedia && media.HasTrack;
        var showTimer = S.ShowTimer && timers.IsActive;
        var showDownload = S.WatchDownloads && _downloads.Active is not null && !showTimer;

        // One live activity at a time keeps the pill calm: timer > download > media.
        CTimer.Visibility = showTimer ? Visibility.Visible : Visibility.Collapsed;
        CDownload.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
        CMedia.Visibility = showMedia && !showTimer && !showDownload ? Visibility.Visible : Visibility.Collapsed;
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
        var activity = showTimer || showDownload || CMedia.Visibility == Visibility.Visible;
        var trailing = S.ShowTime || S.ShowDate || S.ShowSystem;
        CSep.Visibility = activity && trailing ? Visibility.Visible : Visibility.Collapsed;
        if (!activity && !trailing) { CTime.Visibility = Visibility.Visible; }   // never show an empty pill
        FitCompact();
    }

    private bool _refitQueued;

    private void FitCompact()
    {
        if (_expanded) return;
        // Children that just became visible may not have been measured yet; measure fresh, then re-check once layout settles.
        foreach (var fe in new FrameworkElement[] { CTitle, CMedia, CTimer, CDownload, CSys, CTime, CDate, CNormal, CPeek, Compact }) fe.InvalidateMeasure();
        if (!_refitQueued)
        {
            _refitQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { _refitQueued = false; if (!_expanded) FitNow(); });
        }
        FitNow();
    }

    private void FitNow()
    {
        Compact.Measure(new Size(double.PositiveInfinity, CollapsedHeight));
        var target = Math.Clamp(Compact.DesiredSize.Width + 4, 96, 500);
        if (Math.Abs(target - Pill.Width) > 1.5 || Pill.HasAnimatedProperties) Motion.To(Pill, WidthProperty, target, 280, Motion.Settle);
    }

    /// <summary>Briefly show a message in the collapsed pill (track change, timer finished, download done).</summary>
    public void Peek(string glyph, string text, double seconds = 4)
    {
        if (_expanded || !IsVisible) return;
        _hudTimer?.Stop();
        CHud.Visibility = Visibility.Collapsed;
        CPeekGlyph.Text = glyph;
        CPeekText.Text = text;
        CNormal.Visibility = Visibility.Collapsed;
        CPeek.Visibility = Visibility.Visible;
        SetAutoHidden(false);
        FitCompact();
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(seconds);
        _peekTimer.Start();
    }

    private void OnMediaChanged() { RefreshCompact(); if (_expanded && _tab == "media") UpdateMedia(); }
    private void OnTrackChanged()
    {
        // New song: title and artwork glide in rather than snapping.
        if (_expanded && _tab == "media") { Motion.Enter(MTitle, 8, 300); Motion.Enter(MArtist, 10, 360); Motion.Enter(MArt, 6, 320); }
        else if (CMedia.Visibility == Visibility.Visible) Motion.Enter(CMedia, 6, 280);
        if (S.PeekOnMediaChange && S.ShowMedia && !string.IsNullOrWhiteSpace(Hub.Media.Title))
            Peek(Glyph.Music, string.IsNullOrWhiteSpace(Hub.Media.Artist) ? Hub.Media.Title : $"{Hub.Media.Title} — {Hub.Media.Artist}");
    }
    private void OnTimerChanged() { if (!_expanded) RefreshCompact(); else if (_tab == "focus") UpdateFocus(); }
    private void OnTimerCompleted(TimerMode mode) => Peek(Glyph.Check, mode switch { TimerMode.Focus => "Focus block complete — take a break", TimerMode.Break => "Break over — ready to focus", _ => "Countdown finished" }, 7);
    private void OnDownloadsChanged()
    {
        if (!S.WatchDownloads) return;
        if (_downloads.JustCompleted is { } name) Peek(Glyph.Download, "Downloaded " + name, 5);
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
        _expanded = true;
        _peekTimer.Stop();
        CPeek.Visibility = Visibility.Collapsed; CNormal.Visibility = Visibility.Visible;
        _hudTimer?.Stop(); CHud.Visibility = Visibility.Collapsed;
        SetAutoHidden(false);
        UpdateEqualizerDrive();
        Expanded.Visibility = Visibility.Visible;
        Pill.Cursor = Cursors.Arrow;
        Motion.To(Compact, OpacityProperty, 0, 110);
        Motion.To(Pill, WidthProperty, ExpandedWidth, 430, Motion.Spring);
        Expanded.Height = HeightFor(_tab) - 24;
        Motion.To(Pill, HeightProperty, HeightFor(_tab), 430, Motion.Spring);
        Motion.To(this, RadiusProperty, 30, 320);
        Motion.To(Expanded, OpacityProperty, 1, 300);
        Compact.IsHitTestVisible = false;
        ShowTab(_tab);
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
        var ms = immediate ? 0 : 340;
        Motion.To(Expanded, OpacityProperty, 0, immediate ? 0 : 120, null, () => { if (!_expanded) Expanded.Visibility = Visibility.Collapsed; });
        Motion.To(Pill, HeightProperty, CollapsedHeight, ms, Motion.Settle);
        Motion.To(this, RadiusProperty, 19, ms);
        Motion.To(Compact, OpacityProperty, 1, immediate ? 0 : 260);
        Pill.BeginAnimation(WidthProperty, null);
        RefreshCompact();
        Compact.Measure(new Size(double.PositiveInfinity, CollapsedHeight));
        Motion.To(Pill, WidthProperty, Math.Clamp(Compact.DesiredSize.Width + 4, 96, 500), ms, Motion.Settle);
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

    private bool BusyInside => _pinned || _seekDragging || NotesBox.IsKeyboardFocusWithin || Mouse.LeftButton == MouseButtonState.Pressed && Pill.IsMouseOver;

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
        var i = tabs.IndexOf(_tab);
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
            var n = e.Key - Key.D1;
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
        Pill.CaptureMouse();
    }

    private void OnPillMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        Native.GetCursorPos(out var p);
        double dx = p.X - _pressPoint.X, dy = p.Y - _pressPoint.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 7) return;
        _dragging = true;
        var scale = Monitors.Find(Device).Scale;
        var x = S.KeepCentred ? 0 : Math.Round(_pressOffsetX + dx / scale);
        // Magnetic centre: within 18 px of the middle it clicks to exactly centred.
        if (S.SnapToCentre && Math.Abs(x) <= 18) x = 0;
        SetOffset(x, Math.Max(0, Math.Round(_pressOffsetY + dy / scale)));
        Place();
    }

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
            if (Offset().X == 0 && _pressOffsetX != 0) Peek(Glyph.Check, "Centred", 1.0);
        }
        else Expand(false);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_expanded) return;
        var v = Audio.GetVolume();
        if (v is null) return;
        var nv = Math.Clamp(v.Value + (e.Delta > 0 ? 0.02 : -0.02), 0, 1);
        Audio.SetVolume(nv);
        if (!S.VolumeHud) Peek(nv <= 0.001 ? Glyph.Mute : Glyph.Volume, $"Volume {nv * 100:0}%", 1.4);   // otherwise the HUD shows it
        e.Handled = true;
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
        Add("Reset to top centre", () => { S.OffsetX = 0; S.OffsetY = 10; S.PerMonitorOffset.Clear(); Hub.Store.Save(); Hub.Island.Apply(); });
        menu.Items.Add(new Separator());
        Add("Hide the island", () => { S.Enabled = false; Hub.Store.Save(); Hub.Island.Apply(); });
        menu.PlacementTarget = Pill;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ auto-hide
    private void PollAutoHide()
    {
        if (!S.AutoHide || _expanded || !IsVisible || CPeek.Visibility == Visibility.Visible) return;
        if (!Native.GetCursorPos(out var p) || !Native.GetWindowRect(_hwnd, out var r)) return;
        var mon = Monitors.Find(Device);
        var centre = (r.Left + r.Right) / 2;
        var band = 190 * mon.Scale;
        var nearTop = p.Y <= r.Top + 3 * mon.Scale && Math.Abs(p.X - centre) < band && p.Y >= mon.Bounds.Top;
        if (_autoHidden && nearTop) SetAutoHidden(false);
        else if (!_autoHidden && !Pill.IsMouseOver && !_pressed && (DateTime.UtcNow - _leftAt).TotalSeconds > 1.6) SetAutoHidden(true);
    }

    private void SetAutoHidden(bool hidden)
    {
        if (_autoHidden == hidden) return;
        _autoHidden = hidden;
        if (!hidden) _leftAt = DateTime.UtcNow;
        var (_, oy) = Offset();
        Motion.To(PillShift, TranslateTransform.YProperty, hidden ? -(CollapsedHeight + oy + 8) : 0, hidden ? 300 : 380, hidden ? Motion.EaseInOut : Motion.Spring);
        Pill.IsHitTestVisible = !hidden;
    }

    // ------------------------------------------------------------------ tabs
    private static readonly (string Id, string Glyph, string Name)[] AllTabs =
    {
        ("media", Glyph.Music, "Media"), ("system", Glyph.Chip, "System"), ("controls", Glyph.Settings, "Control Centre"),
        ("focus", Glyph.Stopwatch, "Focus timer"), ("calendar", Glyph.Calendar, "Calendar"), ("notes", Glyph.Note, "Notes"),
        ("clipboard", Glyph.Copy, "Clipboard"), ("shelf", GlyphShelf, "File shelf")
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
        var list = AllTabs.Select(t => t.Id).Where(id => S.Modules.Contains(id)).ToList();
        if (list.Count == 0) list.Add("media");
        return list;
    }

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        var active = ActiveTabs();
        if (!active.Contains(_tab)) _tab = active[0];
        foreach (var t in AllTabs.Where(t => active.Contains(t.Id)))
        {
            var rb = new RadioButton { GroupName = "IslandTabs" + GetHashCode(), ToolTip = t.Name, Tag = t.Id, Padding = new Thickness(11, 6, 11, 6), IsChecked = t.Id == _tab };
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
        _loadingUi = true;
        foreach (var rb in Tabs.Children.OfType<RadioButton>()) rb.IsChecked = (string)rb.Tag == id;
        _loadingUi = false;
        TabMedia.Visibility = id == "media" ? Visibility.Visible : Visibility.Collapsed;
        TabSystem.Visibility = id == "system" ? Visibility.Visible : Visibility.Collapsed;
        TabControls.Visibility = id == "controls" ? Visibility.Visible : Visibility.Collapsed;
        TabFocus.Visibility = id == "focus" ? Visibility.Visible : Visibility.Collapsed;
        TabCalendar.Visibility = id == "calendar" ? Visibility.Visible : Visibility.Collapsed;
        TabNotes.Visibility = id == "notes" ? Visibility.Visible : Visibility.Collapsed;
        TabClipboard.Visibility = id == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
        TabShelf.Visibility = id == "shelf" ? Visibility.Visible : Visibility.Collapsed;
        var view = id switch { "media" => (FrameworkElement)TabMedia, "system" => TabSystem, "controls" => TabControls, "focus" => TabFocus, "calendar" => TabCalendar, "clipboard" => TabClipboard, "shelf" => TabShelf, _ => TabNotes };
        Motion.Enter(view, 8, 220);
        _mediaTick.Stop();
        UpdateMonitorSubscription();
        switch (id)
        {
            case "media": UpdateMedia(); LoadVolume(); break;
            case "controls": LoadVolume(); LoadBrightness(); RefreshQuick(); break;
            case "focus": UpdateFocus(); break;
            case "calendar": CalView.ShowToday(); UpdateCalendar(); break;
            case "clipboard": RenderClipboard(); break;
            case "shelf": RenderShelf(); break;
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
            grid.Children.Add(Ui.Text(label, "Text.Label", 10));
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

    // ----- calendar / notes -----
    private void UpdateCalendar()
    {
        var now = DateTime.Now;
        CalDay.Text = now.ToString("dddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        CalNumber.Text = now.Day.ToString(CultureInfo.CurrentCulture);
        CalMonth.Text = now.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        CalWeek.Text = $"Week {ISOWeek.GetWeekOfYear(now)} · day {now.DayOfYear}";
    }

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

/// <summary>
/// Optional download activity: watches the Downloads folder for browsers' temporary files.
/// Opt-in, local only, and it reads nothing but file names and sizes.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    private static readonly string[] TempExtensions = { ".crdownload", ".part", ".download", ".opdownload", ".partial" };
    private FileSystemWatcher? _watcher;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, DateTime> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _stale;

    public (string Name, long Bytes)? Active { get; private set; }
    public string? JustCompleted { get; private set; }
    public event Action? Changed;

    public DownloadWatcher(Dispatcher ui)
    {
        _ui = ui;
        _stale = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _stale.Tick += (_, _) => Sweep();
    }

    public void SetEnabled(bool on)
    {
        if (on == (_watcher is not null)) return;
        if (!on) { _watcher?.Dispose(); _watcher = null; _active.Clear(); _stale.Stop(); Active = null; return; }
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true };
            _watcher.Created += (_, e) => _ui.BeginInvoke(() => Touch(e.FullPath));
            _watcher.Changed += (_, e) => _ui.BeginInvoke(() => Touch(e.FullPath));
            _watcher.Deleted += (_, e) => _ui.BeginInvoke(() => Finish(e.FullPath, null));
            _watcher.Renamed += (_, e) => _ui.BeginInvoke(() => Finish(e.OldFullPath, e.FullPath));
        }
        catch (Exception ex) { Log.Warn("Download watcher: " + ex.Message); }
    }

    private static bool IsTemp(string path) => TempExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void Touch(string path)
    {
        if (!IsTemp(path)) return;
        _active[path] = DateTime.UtcNow;
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        var name = Path.GetFileNameWithoutExtension(path);
        var newActive = (name.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase) ? "Downloading…" : name, size);
        var changed = Active?.Name != newActive.Item1;
        Active = newActive;
        JustCompleted = null;
        _stale.Start();
        if (changed) Changed?.Invoke();
    }

    private void Finish(string oldPath, string? newPath)
    {
        if (!_active.Remove(oldPath)) return;
        JustCompleted = newPath is not null && !IsTemp(newPath) ? Path.GetFileName(newPath) : null;
        if (_active.Count == 0) { Active = null; _stale.Stop(); }
        Changed?.Invoke();
        JustCompleted = null;
    }

    private void Sweep()
    {
        foreach (var key in _active.Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds > 20 || !File.Exists(kv.Key)).Select(kv => kv.Key).ToList()) _active.Remove(key);
        if (_active.Count == 0) { Active = null; _stale.Stop(); Changed?.Invoke(); }
    }

    public void Dispose() { _watcher?.Dispose(); _stale.Stop(); }
}

/// <summary>Creates one island per target display and keeps them in sync with settings.</summary>
public sealed class IslandController
{
    private readonly List<IslandWindow> _windows = new();
    public DownloadWatcher Downloads { get; }
    public IReadOnlyList<IslandWindow> Windows => _windows;

    public IslandController(Dispatcher ui)
    {
        Downloads = new DownloadWatcher(ui);
        Hub.Shell.DisplayChanged += () => ui.BeginInvoke(DispatcherPriority.Background, Apply);
    }

    public void Apply()
    {
        var s = Hub.Settings.Island;
        Downloads.SetEnabled(s.Enabled && s.WatchDownloads);
        var targets = !s.Enabled ? new List<string>()
            : s.Monitor == "all" ? Monitors.All().Select(m => m.Device).ToList()
            : new List<string> { s.Monitor == "primary" ? Monitors.Primary.Device : Monitors.Find(s.Monitor).Device };

        foreach (var w in _windows.Where(w => !targets.Contains(w.Device)).ToList()) { w.Close(); _windows.Remove(w); }
        foreach (var device in targets.Where(d => _windows.All(w => w.Device != d)))
        {
            var w = new IslandWindow(device, Downloads);
            _windows.Add(w);
            w.Show();
        }
        foreach (var w in _windows) w.ApplySettings();
    }

    public void Toggle(bool keyboard)
    {
        if (_windows.Count == 0) return;
        // Act on the island under the cursor's display when there are several.
        Native.GetCursorPos(out var p);
        var device = Monitors.FromPoint(p.X, p.Y).Device;
        (_windows.FirstOrDefault(w => w.Device == device) ?? _windows[0]).Toggle(keyboard);
    }

    public void Expand(string? tab = null) { if (_windows.Count > 0) _windows[0].Expand(false); }
    public void CollapseAll() { foreach (var w in _windows) w.Collapse(); }
    public void Peek(string glyph, string text) { foreach (var w in _windows) w.Peek(glyph, text); }
    public void Centre() { foreach (var w in _windows) w.Centre(); }
    public void Demo(string what) { foreach (var w in _windows) w.Demo(what); }
    public void CloseAll() { foreach (var w in _windows.ToList()) w.Close(); _windows.Clear(); }
}
