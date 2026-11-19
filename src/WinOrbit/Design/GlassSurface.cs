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

    private Border? _shadow;
    private Border? _backdrop;
    private LiquidGlassEffect? _lens;
    private Point? _pointer;   // where the pointer is over the glass, in pixels (null: not over it)
    private bool _over;
    private Window? _window;
    private ImageBrush? _brush;
    private bool _hooked;

    public GlassSurface()
    {
        Focusable = false;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        SizeChanged += (_, _) => { UpdateShadowClip(); UpdateBackdrop(); UpdateLensShape(); };
    }

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
    /// Puts the curved-edge shader on the backdrop, or takes it off: only when there is a wallpaper backdrop to bend,
    /// the setting is on, and the window is drawn by a GPU that can run it (never under the software fallback).
    /// </summary>
    internal void UpdateLens()
    {
        if (_backdrop is null) return;
        var want = Backdrop is not null && IsLoaded && Hub.Ready && Hub.Settings.General.CurvedGlass && LiquidGlassEffect.Usable(this);
        if (!want)
        {
            if (_lens is not null) { _backdrop.Effect = null; _lens = null; }
            return;
        }
        _lens ??= new LiquidGlassEffect();
        if (!ReferenceEquals(_backdrop.Effect, _lens)) _backdrop.Effect = _lens;
        UpdateLensShape();
        UpdateLight();
    }

    private void UpdateLensShape()
    {
        if (_lens is null || ActualWidth < 1 || ActualHeight < 1) return;
        var refraction = Application.Current?.TryFindResource("G.Refraction") as double? ?? 0.05;
        var (band, bend) = LiquidGlassEffect.Profile(ActualWidth, ActualHeight, refraction);
        _lens.Shape = new System.Windows.Media.Media3D.Point4D(ActualWidth, ActualHeight, band, bend);
        _lens.Radii = LiquidGlassEffect.Corners(CornerRadius, ActualWidth, ActualHeight);
    }

    private void UpdateLight()
    {
        if (_lens is null) return;
        var shine = (double)GetValue(ShineProperty);
        var toward = LiquidGlassEffect.Toward(_pointer ?? new Point(ActualWidth / 2, ActualHeight / 2), ActualWidth, ActualHeight);
        _lens.Light = new System.Windows.Media.Media3D.Point4D(toward.X, toward.Y, shine, 0.12);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_lens is null || Motion.Intensity <= 0 || ActualWidth < 1 || ActualHeight < 1) return;
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

    /// <summary>A brief brighter glint round the rim — when the glass changes shape (a drop splitting off, merging back).</summary>
    public void Flare()
    {
        if (_lens is null || Motion.Intensity <= 0) return;
        var rest = _over ? 0.8 : 0;
        var a = new System.Windows.Media.Animation.DoubleAnimation(1.4, rest, TimeSpan.FromMilliseconds(650 / Math.Max(0.5, Motion.Intensity)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
        a.Completed += (_, _) => SetValue(ShineProperty, rest);
        BeginAnimation(ShineProperty, a);
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
        if (!SampleWallpaper || !Hub.Ready || !IsLoaded || ActualWidth < 1 || ActualHeight < 1) { if (!SampleWallpaper && Backdrop is not null) Backdrop = null; return; }
        try
        {
            if (Slice(this, new Rect(0, 0, ActualWidth, ActualHeight), true) is not { } slice) { Backdrop = null; return; }
            if (_brush is null || !ReferenceEquals(_brush.ImageSource, slice.Image))
            {
                _brush = new ImageBrush(slice.Image) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.Linear);
                Backdrop = _brush;
            }
            _brush.Viewbox = slice.Viewbox;
        }
        catch { /* not connected to a presentation source yet */ }
    }

    /// <summary>
    /// The blurred wallpaper under <paramref name="area"/> (in <paramref name="visual"/>'s coordinates): the image and the
    /// part of it to show. <paramref name="refract"/> samples a slightly smaller area, so it looks gently magnified.
    /// </summary>
    internal static (ImageSource Image, Rect Viewbox)? Slice(Visual visual, Rect area, bool refract)
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
        if (refract)
        {
            var refraction = Application.Current.TryFindResource("G.Refraction") as double? ?? 0;
            x += w * refraction / 2; y += h * refraction / 2; w *= 1 - refraction; h *= 1 - refraction;
        }
        return (image, new Rect(x, y, Math.Max(0.0005, w), Math.Max(0.0005, h)));
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
