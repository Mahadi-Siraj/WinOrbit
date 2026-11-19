using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace WinOrbit.Design;

/// <summary>
/// Curved glass edges: bends the blurred-wallpaper backdrop inward near the rim of a glass surface, splits the colours
/// there a little, adds a faint sheen round the rim and lights the side that faces the pointer.
/// Source: Design/Shaders/LiquidGlass.fx (compile with tools/compile-shader.ps1). Only ever applied to the backdrop
/// layer, never to text or icons.
/// </summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    private static readonly PixelShader Shader = Load();

    private static PixelShader Load()
    {
        var ps = new PixelShader { UriSource = new Uri("pack://application:,,,/Design/Shaders/LiquidGlass.ps") };
        ps.Freeze();
        return ps;
    }

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(LiquidGlassEffect), 0);
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(Point4D), typeof(LiquidGlassEffect),
        new UIPropertyMetadata(new Point4D(100, 40, 12, 6), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty RadiiProperty = DependencyProperty.Register(nameof(Radii), typeof(Point4D), typeof(LiquidGlassEffect),
        new UIPropertyMetadata(new Point4D(20, 20, 20, 20), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Point4D), typeof(LiquidGlassEffect),
        new UIPropertyMetadata(new Point4D(0, -1, 0, 0.12), PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty RimProperty = DependencyProperty.Register(nameof(Rim), typeof(double), typeof(LiquidGlassEffect),
        new UIPropertyMetadata(1.0, PixelShaderConstantCallback(3)));

    public LiquidGlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ShapeProperty);
        UpdateShaderValue(RadiiProperty);
        UpdateShaderValue(LightProperty);
        UpdateShaderValue(RimProperty);
    }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>Width, height, depth of the bending band, how far it bends (all in pixels).</summary>
    public Point4D Shape { get => (Point4D)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    /// <summary>Corner radii: top-left, top-right, bottom-right, bottom-left.</summary>
    public Point4D Radii { get => (Point4D)GetValue(RadiiProperty); set => SetValue(RadiiProperty, value); }
    /// <summary>Unit direction towards the pointer (x, y), highlight strength, colour split.</summary>
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    /// <summary>Brightness of the faint sheen all round the rim (0 = none).</summary>
    public double Rim { get => (double)GetValue(RimProperty); set => SetValue(RimProperty, value); }

    /// <summary>Whether this window can run the effect well: a GPU that supports pixel shader 2.0 and no software fallback.</summary>
    internal static bool Usable(Visual visual) =>
        RenderCapability.IsPixelShaderVersionSupported(2, 0) && RenderCapability.Tier >> 16 >= 2 && !SoftwareRendered(visual);

    private static bool SoftwareRendered(Visual visual) =>
        PresentationSource.FromVisual(visual) is System.Windows.Interop.HwndSource { CompositionTarget: { } target } && target.RenderMode == System.Windows.Interop.RenderMode.SoftwareOnly;

    /// <summary>
    /// The band and bend for a surface of this size. The backdrop is already heavily blurred, so a bend of a few pixels
    /// would not show: the band is up to 16 px deep and the rim pulls the image 8–20 px inward (more for themes with more
    /// refraction), never more than a third of the short side, so a small chip stays glass rather than a fish-eye.
    /// </summary>
    internal static (double Band, double Bend) Profile(double width, double height, double refraction)
    {
        var shortSide = Math.Max(0, Math.Min(width, height));
        var band = Math.Min(16, shortSide / 2.5);
        var bend = Math.Min(Math.Clamp(8 + refraction * 150, 8, 20), shortSide / 3);
        return (band, bend);
    }

    /// <summary>Unit direction from a surface's centre towards a point (both in pixels); straight up when they coincide.</summary>
    internal static Vector Toward(Point point, double width, double height)
    {
        var v = new Vector(point.X - width / 2, point.Y - height / 2);
        if (v.Length < 0.5) return new Vector(0, -1);
        v.Normalize();
        return v;
    }

    /// <summary>Corner radii clamped to what fits (half the short side), as the shader expects them.</summary>
    internal static Point4D Corners(CornerRadius c, double width, double height)
    {
        var max = Math.Max(0, Math.Min(width, height) / 2);
        return new Point4D(Math.Clamp(c.TopLeft, 0, max), Math.Clamp(c.TopRight, 0, max), Math.Clamp(c.BottomRight, 0, max), Math.Clamp(c.BottomLeft, 0, max));
    }
}
