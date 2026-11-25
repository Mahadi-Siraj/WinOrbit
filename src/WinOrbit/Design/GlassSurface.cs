using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinOrbit.Interop;
using WinOrbit.Services;

namespace WinOrbit.Design;

/// <summary>
/// The Liquid Glass material. Layers, back to front:
///   shadow (outside only) → wallpaper backdrop slice (blurred, slightly magnified = refraction) →
///   tint → wallpaper colour wash → accent glow → specular highlight → dark hairline → illuminated rim → content.
/// When <see cref="SampleWallpaper"/> is on, the backdrop is the pre-blurred wallpaper sampled at this
/// surface's exact screen position, so the glass appears to frost whatever part of the desktop it sits on.
/// </summary>
public class GlassSurface : ContentControl
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(GlassSurface),
        new FrameworkPropertyMetadata(new CornerRadius(22), FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => { ((GlassSurface)d).UpdateShadowClip(); ((GlassSurface)d).UpdateLens(); }));
    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(nameof(Backdrop), typeof(Brush), typeof(GlassSurface),
        new PropertyMetadata(null, (d, e) => { if (e.OldValue is null != e.NewValue is null) ((GlassSurface)d).UpdateLens(); }));
    /// <summary>How brightly the rim catches the light (0..1). Follows the pointer; <see cref="Flare"/> lifts it briefly.</summary>
    private static readonly DependencyProperty ShineProperty = DependencyProperty.Register("Shine", typeof(double), typeof(GlassSurface),
        new PropertyMetadata(0.0, (d, _) => ((GlassSurface)d).UpdateLight()));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Brush), typeof(GlassSurface));
    public static readonly DependencyProperty WashOpacityProperty = DependencyProperty.Register(nameof(WashOpacity), typeof(double), typeof(GlassSurface), new PropertyMetadata(1.0));
    /// <summary>How much of the glass's light play (accent glow and top highlight) shows: 0 for a plain, solid surface.</summary>
    public static readonly DependencyProperty OrnamentOpacityProperty = DependencyProperty.Register(nameof(OrnamentOpacity), typeof(double), typeof(GlassSurface), new PropertyMetadata(1.0));
    public static readonly DependencyProperty SampleWallpaperProperty = DependencyProperty.Register(nameof(SampleWallpaper), typeof(bool), typeof(GlassSurface),
        new PropertyMetadata(false, (d, _) => ((GlassSurface)d).UpdateBackdrop()));
    public static readonly DependencyProperty CastShadowProperty = DependencyProperty.Register(nameof(CastShadow), typeof(bool), typeof(GlassSurface),
        new PropertyMetadata(true, (d, _) => ((GlassSurface)d).UpdateShadow()));
    public static readonly DependencyProperty ShadowSizeProperty = DependencyProperty.Register(nameof(ShadowSize), typeof(double), typeof(GlassSurface),
        new PropertyMetadata(30.0, (d, _) => ((GlassSurface)d).UpdateShadow()));

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Brush? Backdrop { get => (Brush?)GetValue(BackdropProperty); set => SetValue(BackdropProperty, value); }
    public Brush? Tint { get => (Brush?)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public double WashOpacity { get => (double)GetValue(WashOpacityProperty); set => SetValue(WashOpacityProperty, value); }
    public double OrnamentOpacity { get => (double)GetValue(OrnamentOpacityProperty); set => SetValue(OrnamentOpacityProperty, value); }
    public bool SampleWallpaper { get => (bool)GetValue(SampleWallpaperProperty); set => SetValue(SampleWallpaperProperty, value); }
    public bool CastShadow { get => (bool)GetValue(CastShadowProperty); set => SetValue(CastShadowProperty, value); }
    public double ShadowSize { get => (double)GetValue(ShadowSizeProperty); set => SetValue(ShadowSizeProperty, value); }

    /// <summary>Motion in the liquid lens: 0 at rest; it deepens while the glass changes size and wobbles back after.</summary>
    private static readonly DependencyProperty FlexProperty = DependencyProperty.Register("Flex", typeof(double), typeof(GlassSurface),
        new PropertyMetadata(0.0, (d, _) => ((GlassSurface)d).UpdateLensShape()));
    /// <summary>How far the resting light is turned (radians): a sweep across the rim while the glass changes shape.</summary>
    private static readonly DependencyProperty TurnProperty = DependencyProperty.Register("Turn", typeof(double), typeof(GlassSurface),
        new PropertyMetadata(0.0, (d, _) => ((GlassSurface)d).UpdateLight()));

    private Border? _shadow;
    private Border? _backdrop;
    private LiquidGlassEffect? _lens;     // curved edges (every other look, and the fallback)
    private LiquidLensEffect? _liquid;    // the liquid lens (Liquid looks, pixel shader 3.0)
    private Point? _pointer;   // where the pointer is over the glass, in pixels (null: not over it)
    private bool _over;
    private Window? _window;
    private ImageBrush? _brush;
    private bool _hooked;
    private Size _lastSize;
    private long _lastSizeAt;
    private System.Windows.Threading.DispatcherTimer? _settle;

    public GlassSurface()
    {
        Focusable = false;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        SizeChanged += (_, e) => { UpdateShadowClip(); UpdateBackdrop(); UpdateLensShape(); FlexWithSize(e.NewSize); };
    }

    /// <summary>The liquid lens is on for this surface.</summary>
    public bool IsLiquid => _liquid is not null;
    private bool HasLens => _lens is not null || _liquid is not null;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shadow = GetTemplateChild("PART_Shadow") as Border;
        _backdrop = GetTemplateChild("PART_Backdrop") as Border;
        _lens = null;
        UpdateShadow();
        UpdateShadowClip();
        UpdateBackdrop();
        UpdateLens();
    }

    // ---- Curved glass edges (LiquidGlassEffect on the backdrop layer) ----

    /// <summary>
    /// Puts a lens on the backdrop, or takes it off. Only when there is a backdrop to bend, the curved-edges setting
    /// is on, and the window is drawn by a GPU that can run it (never under the software fallback). Looks with a
    /// "liquid" strength get the liquid lens when pixel shader 3.0 is there; everything else gets the curved edges.
    /// </summary>
    internal void UpdateLens()
    {
        if (_backdrop is null) return;
        var wasLiquid = _liquid is not null;
        var curved = Backdrop is not null && IsLoaded && Hub.Ready && Hub.Settings.General.CurvedGlass && LiquidGlassEffect.Usable(this);
        var liquid = curved && LiquidStrength > 0.01 && LiquidLensEffect.Usable(this);
        if (liquid)
        {
            _lens = null;
            _liquid ??= new LiquidLensEffect();
            if (!ReferenceEquals(_backdrop.Effect, _liquid)) _backdrop.Effect = _liquid;
        }
        else if (curved)
        {
            _liquid = null;
            _lens ??= new LiquidGlassEffect();
            if (!ReferenceEquals(_backdrop.Effect, _lens)) _backdrop.Effect = _lens;
        }
        else
        {
            if (HasLens) { _backdrop.Effect = null; _lens = null; _liquid = null; }
        }
        // The liquid lens cuts its own anti-aliased outline from a plain rectangle that also holds what lies beyond the edge.
        _backdrop.CornerRadius = _liquid is not null ? new CornerRadius(0) : CornerRadius;
        if (wasLiquid != (_liquid is not null) && SampleWallpaper) UpdateBackdrop();   // with or without the margin beyond the edge
        if (!HasLens) return;
        UpdateLensShape();
        UpdateLight();
    }

    /// <summary>The theme's liquid strength (0 = the classic curved edge).</summary>
    private static double LiquidStrength => Application.Current?.TryFindResource("G.Liquid") as double? ?? 0;

    private void UpdateLensShape()
    {
        if (ActualWidth < 1 || ActualHeight < 1) return;
        if (_liquid is not null)
        {
            var liquid = LiquidStrength;
            var (bevel, reach, _, rim) = LiquidLensEffect.Profile(ActualWidth, ActualHeight, liquid);
            var flex = Math.Clamp((double)GetValue(FlexProperty), -0.8, LiquidLensEffect.MaxFlex);
            _liquid.Shape = new System.Windows.Media.Media3D.Point4D(ActualWidth, ActualHeight, bevel, 0);
            _liquid.Radii = LiquidGlassEffect.Corners(CornerRadius, ActualWidth, ActualHeight);
            _liquid.Lens = new System.Windows.Media.Media3D.Point4D(reach, flex, rim, LiquidLensEffect.Readability(liquid, !Hub.Ready || Hub.Theme.IsDark));
            return;
        }
        if (_lens is null) return;
        var refraction = Application.Current?.TryFindResource("G.Refraction") as double? ?? 0.05;
        var (band, bend) = LiquidGlassEffect.Profile(ActualWidth, ActualHeight, refraction);
        _lens.Shape = new System.Windows.Media.Media3D.Point4D(ActualWidth, ActualHeight, band, bend);
        _lens.Radii = LiquidGlassEffect.Corners(CornerRadius, ActualWidth, ActualHeight);
    }

    private void UpdateLight()
    {
        var shine = (double)GetValue(ShineProperty);
        if (_liquid is not null)
        {
            // The light rests at the upper left; the pointer, while it is over the glass, pulls it round to its side.
            var rest = LiquidLensEffect.RestingLight((double)GetValue(TurnProperty));
            var dir = _pointer is { } at ? LiquidGlassEffect.Toward(at, ActualWidth, ActualHeight) : rest;
            var split = LiquidLensEffect.Profile(ActualWidth, ActualHeight, LiquidStrength).Split;
            _liquid.Light = new System.Windows.Media.Media3D.Point4D(dir.X, dir.Y, shine, split);
            return;
        }
        if (_lens is null) return;
        var toward = LiquidGlassEffect.Toward(_pointer ?? new Point(ActualWidth / 2, ActualHeight / 2), ActualWidth, ActualHeight);
        _lens.Light = new System.Windows.Media.Media3D.Point4D(toward.X, toward.Y, shine, 0.12);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!HasLens || Motion.Intensity <= 0 || ActualWidth < 1 || ActualHeight < 1) return;
        _pointer = e.GetPosition(this);
        if (!_over) { _over = true; Glow(0.8, 220); }
        else UpdateLight();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_over) return;
        _over = false;
        _pointer = null;
        Glow(0, 420);
    }

    /// <summary>
    /// A brief brighter glint round the rim — when the glass changes shape (a drop splitting off, merging back). The
    /// liquid lens also flexes and wobbles back, and its light sweeps across, as if the glass were a body of liquid.
    /// </summary>
    public void Flare()
    {
        if (!HasLens || Motion.Intensity <= 0) return;
        var rest = _over ? 0.8 : 0;
        var a = new System.Windows.Media.Animation.DoubleAnimation(1.4, rest, TimeSpan.FromMilliseconds(650 / Math.Max(0.5, Motion.Intensity)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
        a.Completed += (_, _) => SetValue(ShineProperty, rest);
        BeginAnimation(ShineProperty, a);
        Sweep(0.7);
    }

    /// <summary>
    /// Liquid lens only: the light sweeps across the rim and the lens gives a soft wobble — used when the glass opens
    /// into a panel or closes back. <paramref name="strength"/> 1 is a full sweep. Nothing under Reduced Motion.
    /// </summary>
    public void Sweep(double strength = 1)
    {
        if (_liquid is null || Motion.Intensity <= 0.01) return;
        Springs.Kick(this, FlexProperty, 2.6 * strength, 0, 0.42, 0.4);
        var turn = new System.Windows.Media.Animation.DoubleAnimation(-1.1 * strength, 0, TimeSpan.FromMilliseconds(760 / Math.Max(0.5, Motion.Intensity)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
        turn.Completed += (_, _) => SetValue(TurnProperty, 0.0);
        BeginAnimation(TurnProperty, turn);
    }

    /// <summary>
    /// Liquid lens only: while the glass grows or shrinks, the lens deepens with the speed of the change; when it
    /// stops, it springs back with a small wobble, like liquid settling.
    /// </summary>
    private void FlexWithSize(Size size)
    {
        var now = Environment.TickCount64;
        var last = _lastSize; var lastAt = _lastSizeAt;
        _lastSize = size; _lastSizeAt = now;
        if (_liquid is null || Motion.Intensity <= 0.01 || last.Width < 1 || now - lastAt > 120) return;
        var target = FlexForSpeed(Math.Max(Math.Abs(size.Width - last.Width), Math.Abs(size.Height - last.Height)), Math.Max(1, now - lastAt));
        if (target <= 0.01) return;
        Springs.To(this, FlexProperty, target, 0.12, 1);
        _settle ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(90), System.Windows.Threading.DispatcherPriority.Render,
            (_, _) => { _settle!.Stop(); Springs.To(this, FlexProperty, 0, 0.44, 0.36); }, Dispatcher);
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>The lens flex for a size change of <paramref name="pixels"/> in <paramref name="ms"/> milliseconds: 0 when slow, up to <see cref="LiquidLensEffect.MaxFlex"/> when fast.</summary>
    internal static double FlexForSpeed(double pixels, double ms)
    {
        var speed = pixels / Math.Max(1, ms) * 1000;   // pixels per second
        return Math.Clamp((speed - 150) / 2400, 0, LiquidLensEffect.MaxFlex);
    }

    private void Glow(double to, double ms)
    {
        var a = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
        a.Completed += (_, _) => SetValue(ShineProperty, to);
        BeginAnimation(ShineProperty, a);
    }

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _window = Window.GetWindow(this);
        if (_window is not null) _window.LocationChanged += OnMoved;
        if (Hub.Ready)
        {
            Hub.Wallpaper.BackdropsChanged += UpdateBackdrop;
            Hub.Theme.Applied += OnTheme;
        }
        RenderGuard.Switched += OnRenderSwitched;
        UpdateShadow();
        UpdateBackdrop();
        UpdateLens();
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        if (_window is not null) _window.LocationChanged -= OnMoved;
        if (Hub.Ready)
        {
            Hub.Wallpaper.BackdropsChanged -= UpdateBackdrop;
            Hub.Theme.Applied -= OnTheme;
        }
        RenderGuard.Switched -= OnRenderSwitched;
    }

    private void OnMoved(object? sender, EventArgs e) => UpdateBackdrop();
    private void OnTheme() { UpdateShadow(); UpdateBackdrop(); UpdateLens(); }
    private void OnRenderSwitched(Window w) { if (ReferenceEquals(w, _window)) UpdateLens(); }

    private void UpdateShadow()
    {
        if (_shadow is null) return;
        var opacity = Application.Current?.TryFindResource("G.ShadowOpacity") as double? ?? 0.4;
        if (!CastShadow || opacity < 0.02) { _shadow.Visibility = Visibility.Collapsed; _shadow.Effect = null; return; }
        _shadow.Visibility = Visibility.Visible;
        var fx = new DropShadowEffect { BlurRadius = ShadowSize, ShadowDepth = ShadowSize * 0.28, Direction = 270, Color = Colors.Black, Opacity = opacity, RenderingBias = RenderingBias.Performance };
        fx.Freeze();
        _shadow.Effect = fx;
    }

    /// <summary>Keep only the part of the shadow that falls outside the glass, so the pane itself stays clear.</summary>
    public void RefreshShadowClip() => UpdateShadowClip();

    private void UpdateShadowClip()
    {
        if (_shadow is null || ActualWidth < 1 || ActualHeight < 1) return;
        var outer = new RectangleGeometry(new Rect(-120, -120, ActualWidth + 240, ActualHeight + 240));
        var rect = new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1));
        var c = CornerRadius;
        // Uniform corners (every surface but the island's notch/drop shapes) keep the simple, fast rounded rectangle.
        Geometry inner = c.TopLeft == c.TopRight && c.TopLeft == c.BottomLeft && c.TopLeft == c.BottomRight
            ? new RectangleGeometry(rect, Math.Min(c.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2), Math.Min(c.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2))
            : RoundedRect(rect, c);
        var clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        clip.Freeze();
        _shadow.Clip = clip;
    }

    /// <summary>A rectangle with its own radius on each corner (shapes such as the notch: square top, round bottom).</summary>
    internal static Geometry RoundedRect(Rect rect, CornerRadius c)
    {
        var max = Math.Min(rect.Width, rect.Height) / 2;
        double tl = Math.Clamp(c.TopLeft, 0, max), tr = Math.Clamp(c.TopRight, 0, max), br = Math.Clamp(c.BottomRight, 0, max), bl = Math.Clamp(c.BottomLeft, 0, max);
        double l = rect.Left, t = rect.Top, r = rect.Right, b = rect.Bottom;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(l + tl, t), true, true);
            ctx.LineTo(new Point(r - tr, t), true, false);
            if (tr > 0) ctx.ArcTo(new Point(r, t + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(r, b - br), true, false);
            if (br > 0) ctx.ArcTo(new Point(r - br, b), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(l + bl, b), true, false);
            if (bl > 0) ctx.ArcTo(new Point(l, b - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(l, t + tl), true, false);
            if (tl > 0) ctx.ArcTo(new Point(l + tl, t), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Point the backdrop brush at the slice of blurred wallpaper that lies under this surface.</summary>
    public void UpdateBackdrop()
    {
        if (!SampleWallpaper || !Hub.Ready || !IsLoaded || ActualWidth < 1 || ActualHeight < 1)
        {
            // Take back only the slice this surface sampled itself: a backdrop someone else set (the in-app previews
            // hand their glass a slice of the wallpaper picture) stays, or those previews lose their blur and lens.
            if (!SampleWallpaper && _brush is not null && ReferenceEquals(Backdrop, _brush)) { Backdrop = null; _brush = null; }
            return;
        }
        try
        {
            // The liquid lens bends in what lies beyond the edge, so it gets a margin of backdrop all round.
            var pad = _liquid is not null ? LiquidLensEffect.PadFor(ActualWidth, ActualHeight, LiquidStrength) : 0;
            if (Slice(this, new Rect(0, 0, ActualWidth, ActualHeight), true, pad) is not { } slice) { Backdrop = null; return; }
            if (_brush is null || !ReferenceEquals(_brush.ImageSource, slice.Image))
            {
                _brush = new ImageBrush(slice.Image) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.Linear);
                Backdrop = _brush;
            }
            _brush.Viewbox = slice.Viewbox;
            if (_liquid is not null) _liquid.Pad = new System.Windows.Media.Media3D.Point4D(slice.Pad.Left, slice.Pad.Top, slice.Pad.Right, slice.Pad.Bottom);
        }
        catch { /* not connected to a presentation source yet */ }
    }

    /// <summary>
    /// The blurred wallpaper under <paramref name="area"/> (in <paramref name="visual"/>'s coordinates): the image and the
    /// part of it to show. <paramref name="refract"/> samples a slightly smaller area, so it looks gently magnified.
    /// <paramref name="pad"/> widens the part shown by that much on every side (in <paramref name="visual"/>'s units) as
    /// far as the screen goes; <c>Pad</c> says how much each side actually got (less at a screen edge).
    /// </summary>
    internal static (ImageSource Image, Rect Viewbox, Thickness Pad)? Slice(Visual visual, Rect area, bool refract, double pad = 0)
    {
        if (!Hub.Ready || PresentationSource.FromVisual(visual) is null || area.Width < 1 || area.Height < 1) return null;
        var topLeft = visual.PointToScreen(area.TopLeft);
        var bottomRight = visual.PointToScreen(area.BottomRight);
        var mon = Monitors.FromPoint((int)((topLeft.X + bottomRight.X) / 2), (int)((topLeft.Y + bottomRight.Y) / 2));
        // Live window glass (opt-in) when it has a picture of this screen; otherwise the blurred wallpaper.
        if ((Hub.LiveGlass?.ImageFor(visual, mon) ?? Hub.Wallpaper.Backdrop(mon.Device)) is not { } image) return null;
        double mw = mon.Bounds.Width, mh = mon.Bounds.Height;
        var x = (topLeft.X - mon.Bounds.Left) / mw;
        var y = (topLeft.Y - mon.Bounds.Top) / mh;
        var w = (bottomRight.X - topLeft.X) / mw;
        var h = (bottomRight.Y - topLeft.Y) / mh;
        var refraction = refract ? Application.Current.TryFindResource("G.Refraction") as double? ?? 0 : 0;
        var (view, got) = PaddedView(new Rect(x, y, w, h), refraction, pad / area.Width, pad / area.Height);
        return (image, view, new Thickness(got.Left * area.Width, got.Top * area.Height, got.Right * area.Width, got.Bottom * area.Height));
    }

    /// <summary>
    /// The part of the screen's picture to show for a surface at <paramref name="screen"/> (fractions of the screen):
    /// shrunk by <paramref name="refraction"/> about its centre (a gentle magnification), then widened by
    /// <paramref name="padX"/>/<paramref name="padY"/> of the surface's own size on each side, as far as the picture
    /// goes. Returns that view and how much padding each side got, as fractions of the surface's size.
    /// </summary>
    internal static (Rect View, Thickness Got) PaddedView(Rect screen, double refraction, double padX, double padY)
    {
        double x = screen.X, y = screen.Y, w = screen.Width, h = screen.Height;
        x += w * refraction / 2; y += h * refraction / 2; w *= 1 - refraction; h *= 1 - refraction;
        // One surface pixel covers w / width of the picture after magnification, so the padding scales the same way.
        double px = Math.Max(0, padX) * w, py = Math.Max(0, padY) * h;
        double left = Math.Clamp(x, 0, px), top = Math.Clamp(y, 0, py);
        double right = Math.Clamp(1 - (x + w), 0, px), bottom = Math.Clamp(1 - (y + h), 0, py);
        var view = new Rect(x - left, y - top, Math.Max(0.0005, w + left + right), Math.Max(0.0005, h + top + bottom));
        return (view, new Thickness(w > 0 ? left / w : 0, h > 0 ? top / h : 0, w > 0 ? right / w : 0, h > 0 ? bottom / h : 0));
    }
}

/// <summary>Window helpers shared by every WinOrbit window.</summary>
public static class WindowFx
{
    /// <summary>Give a normal (non-layered) window the DWM system backdrop: real blur of what is behind it.</summary>
    public static bool ApplySystemBackdrop(Window window, bool dark, bool acrylic)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        var useDark = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        if (Environment.OSVersion.Version.Build < 22621) return false;   // system backdrops need Windows 11 22H2+
        var type = acrylic ? Native.DWMSBT_TRANSIENTWINDOW : Native.DWMSBT_MAINWINDOW;
        var hr = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        var round = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        return hr == 0;
    }

    /// <summary>Tool-window, never-activating overlay behaviour for widgets and the island.</summary>
    public static void MakeOverlay(Window window, bool noActivate)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | (noActivate ? Native.WS_EX_NOACTIVATE : 0));
    }
}
