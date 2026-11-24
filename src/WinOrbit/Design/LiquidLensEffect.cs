using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace WinOrbit.Design;

/// <summary>
/// The liquid glass lens used by the "Liquid" looks: a thick, rounded rim that gathers in and bends what lies just
/// beyond the glass, a faint prism in the bevel, light on the rim and a readability term in the middle.
/// Source: Design/Shaders/LiquidLens.fx (compile with tools/compile-shader.ps1 -Profile ps_3_0). Only ever applied to
/// the backdrop layer, which must be a plain rectangle showing the backdrop plus <see cref="Pad"/> beyond each edge;
/// the shader cuts the rounded outline itself. Needs pixel shader 3.0 on a GPU; elsewhere glass falls back to
/// <see cref="LiquidGlassEffect"/>.
/// </summary>
public sealed class LiquidLensEffect : ShaderEffect
{
    private static readonly PixelShader Shader = Load();

    private static PixelShader Load()
    {
        var ps = new PixelShader { UriSource = new Uri("pack://application:,,,/Design/Shaders/LiquidLens.ps"), ShaderRenderMode = ShaderRenderMode.HardwareOnly };
        ps.Freeze();
        return ps;
    }

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(LiquidLensEffect), 0);
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(Point4D), typeof(LiquidLensEffect),
        new UIPropertyMetadata(new Point4D(100, 40, 12, 0), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty RadiiProperty = DependencyProperty.Register(nameof(Radii), typeof(Point4D), typeof(LiquidLensEffect),
        new UIPropertyMetadata(new Point4D(20, 20, 20, 20), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Point4D), typeof(LiquidLensEffect),
        new UIPropertyMetadata(new Point4D(-0.5, -0.87, 0, 0.08), PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty LensProperty = DependencyProperty.Register(nameof(Lens), typeof(Point4D), typeof(LiquidLensEffect),
        new UIPropertyMetadata(new Point4D(10, 0, 0.5, 0.4), PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty PadProperty = DependencyProperty.Register(nameof(Pad), typeof(Point4D), typeof(LiquidLensEffect),
        new UIPropertyMetadata(new Point4D(0, 0, 0, 0), PixelShaderConstantCallback(4)));

    public LiquidLensEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ShapeProperty);
        UpdateShaderValue(RadiiProperty);
        UpdateShaderValue(LightProperty);
        UpdateShaderValue(LensProperty);
        UpdateShaderValue(PadProperty);
    }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>Width and height of the glass, bevel width (all in pixels); w unused.</summary>
    public Point4D Shape { get => (Point4D)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    /// <summary>Corner radii: top-left, top-right, bottom-right, bottom-left.</summary>
    public Point4D Radii { get => (Point4D)GetValue(RadiiProperty); set => SetValue(RadiiProperty, value); }
    /// <summary>Unit direction towards the light (x, y), extra shine, colour split.</summary>
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    /// <summary>Reach beyond the edge (px), flex, rim light, readability (+ calm bright backgrounds, − lift dark ones).</summary>
    public Point4D Lens { get => (Point4D)GetValue(LensProperty); set => SetValue(LensProperty, value); }
    /// <summary>How far the input extends beyond each edge of the glass: left, top, right, bottom (px).</summary>
    public Point4D Pad { get => (Point4D)GetValue(PadProperty); set => SetValue(PadProperty, value); }

    /// <summary>Whether this window can run the lens: pixel shader 3.0 on a GPU, never under the software fallback.</summary>
    internal static bool Usable(Visual visual) => RenderCapability.IsPixelShaderVersionSupported(3, 0) && LiquidGlassEffect.Usable(visual);

    /// <summary>Strongest flex the motion gives the lens (see <see cref="GlassSurface"/>); padding allows for it.</summary>
    internal const double MaxFlex = 0.6;

    /// <summary>
    /// The lens for a glass of this size at this liquid strength (0..1): how wide the rounded bevel is, how far the rim
    /// reaches beyond the edge, the prism split and the rim light. A small capsule gets a bevel of about half its
    /// height, so all of it reads as one rounded drop; a big panel keeps a flat, readable middle (bevel at most 64 px).
    /// </summary>
    internal static (double Bevel, double Reach, double Split, double Rim) Profile(double width, double height, double liquid)
    {
        liquid = Math.Clamp(liquid, 0, 1);
        var shortSide = Math.Max(0, Math.Min(width, height));
        var bevel = Math.Clamp(shortSide * (0.3 + 0.18 * liquid), Math.Min(6, shortSide / 2), 64);
        var reach = bevel * (0.4 + 0.55 * liquid);
        return (bevel, reach, 0.04 + 0.08 * liquid, 0.3 + 0.4 * liquid);
    }

    /// <summary>How much backdrop beyond each edge the lens can use (px): its reach at the strongest flex, with the prism.</summary>
    internal static double PadFor(double width, double height, double liquid)
    {
        var (_, reach, split, _) = Profile(width, height, liquid);
        return Math.Ceiling(reach * (1 + MaxFlex) * (1 + split)) + 2;
    }

    /// <summary>Readability term for the shader: positive calms bright backgrounds (dark looks), negative lifts dark ones (light looks).</summary>
    internal static double Readability(double liquid, bool dark) => (dark ? 1 : -1) * 0.6 * Math.Clamp(liquid, 0, 1);

    /// <summary>The resting light: from the upper left, turned by <paramref name="turn"/> radians (a sweep while the glass changes shape).</summary>
    internal static Vector RestingLight(double turn)
    {
        const double baseAngle = -2.1;   // up and to the left (screen y points down)
        var a = baseAngle + turn;
        return new Vector(Math.Cos(a), Math.Sin(a));
    }
}
