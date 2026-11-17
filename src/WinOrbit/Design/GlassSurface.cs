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
        new FrameworkPropertyMetadata(new CornerRadius(22), FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((GlassSurface)d).UpdateShadowClip()));
    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(nameof(Backdrop), typeof(Brush), typeof(GlassSurface));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Brush), typeof(GlassSurface));
    public static readonly DependencyProperty WashOpacityProperty = DependencyProperty.Register(nameof(WashOpacity), typeof(double), typeof(GlassSurface), new PropertyMetadata(1.0));
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
    public bool SampleWallpaper { get => (bool)GetValue(SampleWallpaperProperty); set => SetValue(SampleWallpaperProperty, value); }
    public bool CastShadow { get => (bool)GetValue(CastShadowProperty); set => SetValue(CastShadowProperty, value); }
    public double ShadowSize { get => (double)GetValue(ShadowSizeProperty); set => SetValue(ShadowSizeProperty, value); }

    private Border? _shadow;
    private Window? _window;
    private ImageBrush? _brush;
    private bool _hooked;

    public GlassSurface()
    {
        Focusable = false;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        SizeChanged += (_, _) => { UpdateShadowClip(); UpdateBackdrop(); };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shadow = GetTemplateChild("PART_Shadow") as Border;
        UpdateShadow();
        UpdateShadowClip();
        UpdateBackdrop();
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
        UpdateShadow();
        UpdateBackdrop();
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
    }

    private void OnMoved(object? sender, EventArgs e) => UpdateBackdrop();
    private void OnTheme() { UpdateShadow(); UpdateBackdrop(); }

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
    private void UpdateShadowClip()
    {
        if (_shadow is null || ActualWidth < 1 || ActualHeight < 1) return;
        var r = Math.Min(CornerRadius.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2);
        var outer = new RectangleGeometry(new Rect(-120, -120, ActualWidth + 240, ActualHeight + 240));
        var inner = new RectangleGeometry(new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), r, r);
        var clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        clip.Freeze();
        _shadow.Clip = clip;
    }

    /// <summary>Point the backdrop brush at the slice of blurred wallpaper that lies under this surface.</summary>
    public void UpdateBackdrop()
    {
        if (!SampleWallpaper || !Hub.Ready || !IsLoaded || ActualWidth < 1 || ActualHeight < 1) { if (!SampleWallpaper && Backdrop is not null) Backdrop = null; return; }
        try
        {
            if (PresentationSource.FromVisual(this) is null) return;
            var topLeft = PointToScreen(new Point(0, 0));
            var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            var mon = Monitors.FromPoint((int)((topLeft.X + bottomRight.X) / 2), (int)((topLeft.Y + bottomRight.Y) / 2));
            var image = Hub.Wallpaper.Backdrop(mon.Device);
            if (image is null) { Backdrop = null; return; }

            double mw = mon.Bounds.Width, mh = mon.Bounds.Height;
            var x = (topLeft.X - mon.Bounds.Left) / mw;
            var y = (topLeft.Y - mon.Bounds.Top) / mh;
            var w = (bottomRight.X - topLeft.X) / mw;
            var h = (bottomRight.Y - topLeft.Y) / mh;

            // Refraction: sample a slightly smaller area than we cover, so the backdrop is gently magnified like a lens.
            var refraction = Application.Current.TryFindResource("G.Refraction") as double? ?? 0;
            x += w * refraction / 2; y += h * refraction / 2; w *= 1 - refraction; h *= 1 - refraction;

            if (_brush is null || !ReferenceEquals(_brush.ImageSource, image))
            {
                _brush = new ImageBrush(image) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.Linear);
                Backdrop = _brush;
            }
            _brush.Viewbox = new Rect(x, y, Math.Max(0.0005, w), Math.Max(0.0005, h));
        }
        catch { /* not connected to a presentation source yet */ }
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
