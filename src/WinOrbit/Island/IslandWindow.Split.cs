using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinOrbit.Design;

namespace WinOrbit.Island;

/// <summary>
/// The side bubble. When the capsule is busy (music, a call, a timer, a download) a short alert — a lock key, the
/// keyboard language, "copied", "microphone muted" — no longer replaces it: a small bubble of the same glass splits
/// off beside it along the edge, and the pair moves aside so it stays centred. While the two are close, a liquid neck
/// joins them, so the bubble looks like a drop pulling away. When the alert ends the bubble flows back in and the
/// capsule wobbles as it takes the drop back.
///
/// Everything is driven by one sprung number, <see cref="SplitGap"/>: the space between capsule and bubble along the
/// edge. At minus the bubble's size it is fully inside the capsule (invisible); at <see cref="GapAtRest"/> it rests
/// beside it. Reduced Motion snaps between the two, without the neck.
/// </summary>
public partial class IslandWindow
{
    internal const double GapAtRest = 8, NeckReach = 14;

    public static readonly DependencyProperty SplitGapProperty = DependencyProperty.Register(nameof(SplitGap), typeof(double), typeof(IslandWindow),
        new PropertyMetadata(-38.0, (d, _) => ((IslandWindow)d).LayoutSplit()));
    public double SplitGap { get => (double)GetValue(SplitGapProperty); set => SetValue(SplitGapProperty, value); }

    private DispatcherTimer? _satTimer;
    private bool _satLeaving;
    private ImageBrush? _neckBrush;

    private bool SatelliteShowing => Satellite.Visibility == Visibility.Visible;

    /// <summary>The capsule is showing a live activity that a short alert shouldn't push aside.</summary>
    private bool PillBusy => S.SplitAlerts && !_expanded && !_card && IsVisible && Pill.ActualWidth > 0
        && CNormal.Visibility == Visibility.Visible && CHud.Visibility != Visibility.Visible && CPeek.Visibility != Visibility.Visible
        && (CMedia.Visibility == Visibility.Visible || CCall.Visibility == Visibility.Visible || CTimer.Visibility == Visibility.Visible || CDownload.Visibility == Visibility.Visible);

    private void InitSplit()
    {
        _satTimer = new DispatcherTimer();
        _satTimer.Tick += (_, _) =>
        {
            _satTimer.Stop();
            if (Satellite.IsMouseOver) { _satTimer.Interval = TimeSpan.FromSeconds(1); _satTimer.Start(); }   // being read
            else EndSatellite();
        };
        // Clicking the bubble opens the island, like clicking the capsule.
        Satellite.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Satellite.MouseLeftButtonUp += (_, e) => { e.Handled = true; EndSatellite(true); Expand(false); };
        Satellite.SizeChanged += (_, _) => LayoutSplit();
        Pill.SizeChanged += (_, _) => LayoutSplit();
        PillShift.Changed += (_, _) => LayoutSplit();
        PillScale.Changed += (_, _) => { if (SatelliteShowing && Bridge.Visibility == Visibility.Visible) DrawNeck(); };
    }

    /// <summary>Match the capsule's edge, margin, shape and material.</summary>
    private void AlignSatellite()
    {
        Satellite.HorizontalAlignment = Pill.HorizontalAlignment;
        Satellite.VerticalAlignment = Pill.VerticalAlignment;
        Satellite.Margin = Pill.Margin;
        Satellite.RenderTransformOrigin = new Point(0.5, 0.5);
        Satellite.SampleWallpaper = Pill.SampleWallpaper;
        Satellite.Tint = Pill.Tint;
        var h = CollapsedHeight;
        Satellite.Height = h;
        Satellite.CornerRadius = ShapeCorners(S.Shape, h / 2, Edge);
    }

    /// <summary>
    /// Show a short alert in the side bubble: a glyph (or a key cap with its light) and, when there's room, its words.
    /// A second alert while the bubble is out swaps its content in place.
    /// </summary>
    private void ShowSatellite(string? glyph, string? cap, bool ledOn, string text, Color? color, double seconds)
    {
        AlignSatellite();
        SetAutoHidden(false);
        SatKey.Visibility = cap is null ? Visibility.Collapsed : Visibility.Visible;
        SatGlyph.Visibility = cap is null ? Visibility.Visible : Visibility.Collapsed;
        if (cap is not null)
        {
            SatKeyText.Text = cap;
            SatKeyLed.Fill = ledOn ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)) : new SolidColorBrush(Color.FromArgb(0x50, 0x9A, 0x9A, 0x9A));
            SatKey.Opacity = ledOn ? 1 : 0.6;
        }
        else
        {
            SatGlyph.Text = glyph ?? Glyph.Info;
            if (color is { } c) SatGlyph.Foreground = new SolidColorBrush(c);
            else SatGlyph.SetResourceReference(TextBlock.ForegroundProperty, "T.Accent");
        }
        Satellite.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(Satellite, text);

        // Words only when they fit beside the capsule (always on the side edges, where the pair stacks along the edge).
        var h = CollapsedHeight;
        SatText.Text = text;
        SatText.Visibility = Visibility.Visible;
        SatText.MaxWidth = 170;
        SatContent.Measure(new Size(double.PositiveInfinity, h));
        var room = SideEdge ? 300 : 540 - Pill.ActualWidth - GapAtRest;
        var width = SatelliteWidth(SatContent.DesiredSize.Width, h, room);
        if (width <= h) SatText.Visibility = Visibility.Collapsed;

        var fresh = !SatelliteShowing || _satLeaving;
        _satLeaving = false;
        if (!SatelliteShowing)
        {
            Satellite.Width = width;
            SplitGap = -AlongSize(width);   // starts fully inside the capsule
            Satellite.Visibility = Visibility.Visible;
            Springs.Stop(this, SplitGapProperty);
        }
        else Springs.To(Satellite, WidthProperty, width, 0.4, 0.78);

        if (fresh)
        {
            // Pulls out on a lively spring: a little overshoot, like a drop of liquid separating.
            Springs.To(this, SplitGapProperty, GapAtRest, 0.5, 0.6);
            Droplet(0.5);
        }
        else
        {
            Pop(SatContent, 0.8);
            Springs.Kick(SatScale, ScaleTransform.ScaleXProperty, 0.9, 1, 0.3, 0.45);
            Springs.Kick(SatScale, ScaleTransform.ScaleYProperty, 0.9, 1, 0.3, 0.45);
        }
        LayoutSplit();
        _satTimer!.Stop();
        _satTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        _satTimer.Start();
    }

    /// <summary>The bubble flows back into the capsule, which wobbles as it takes the drop back.</summary>
    private void EndSatellite(bool immediate = false)
    {
        if (!SatelliteShowing) return;
        _satTimer?.Stop();
        var inside = -AlongSize(Satellite.Width);
        if (immediate || Motion.Intensity <= 0.01 || !IsVisible)
        {
            Springs.To(this, SplitGapProperty, inside, 0);
            HideSatellite();
            return;
        }
        _satLeaving = true;
        Springs.To(this, SplitGapProperty, inside, 0.42, 0.92, () =>
        {
            if (!_satLeaving) return;   // a new alert took it over on the way back
            HideSatellite();
            Droplet(0.55);
        });
    }

    private void HideSatellite()
    {
        _satLeaving = false;
        Satellite.Visibility = Visibility.Collapsed;
        Bridge.Visibility = Visibility.Collapsed;
        PillSplit.X = PillSplit.Y = 0;
        Pill.UpdateBackdrop();
    }

    /// <summary>The bubble's size along the edge (its width on the top and bottom, its height on the sides).</summary>
    private double AlongSize(double width) => SideEdge ? CollapsedHeight : width;

    /// <summary>Place the capsule and bubble for the current gap; called on every spring step while they move.</summary>
    private void LayoutSplit()
    {
        if (!SatelliteShowing) return;
        var along = AlongSize(Satellite.Width);
        var gap = SplitGap;
        var (pillShift, satCentre, grown) = SplitPlacement(gap, along, SideEdge ? Pill.ActualHeight : Pill.ActualWidth);
        if (SideEdge) { PillSplit.X = 0; PillSplit.Y = pillShift; SatShift.X = PillShift.X; SatShift.Y = satCentre + PillShift.Y; }
        else { PillSplit.X = pillShift; PillSplit.Y = 0; SatShift.X = satCentre + PillShift.X; SatShift.Y = PillShift.Y; }
        var scale = 0.55 + 0.45 * grown;
        if (!Springs.IsMoving(SatScale, ScaleTransform.ScaleXProperty)) { SatScale.ScaleX = scale; SatScale.ScaleY = scale; }
        Satellite.Opacity = Math.Clamp(grown * 2.2, 0, 1) * Math.Clamp(S.Opacity, 0.3, 1);
        Pill.UpdateBackdrop();
        Satellite.UpdateBackdrop();
        DrawNeck();
    }

    /// <summary>
    /// Where things go for a gap: how far the capsule moves aside (so the pair stays centred), where the bubble's centre
    /// sits (from the capsule's own centre line), and how far it has come out (0 inside, 1 once it has fully left).
    /// </summary>
    internal static (double PillShift, double SatCentre, double Grown) SplitPlacement(double gap, double along, double pillAlong)
    {
        var shift = -(gap + along) / 2;
        var centre = shift + pillAlong / 2 + gap + along / 2;
        var grown = along <= 0 ? 1 : Math.Clamp((gap + along) / along, 0, 1);
        return (shift, centre, grown);
    }

    /// <summary>The bubble's width: a circle (icon only) when the words don't fit in <paramref name="room"/>.</summary>
    internal static double SatelliteWidth(double content, double height, double room)
    {
        var withWords = Math.Round((content + height * 0.75) / 2) * 2;   // the icon sits as far from the end as in the capsule
        return withWords <= Math.Min(room, 230) ? Math.Max(height, withWords) : height;
    }

    /// <summary>Half the neck's thickness at its waist: full while they overlap, thinning to nothing at <see cref="NeckReach"/>.</summary>
    internal static double NeckWaist(double gap, double halfHeight)
    {
        if (gap >= NeckReach) return 0;
        var s = Math.Clamp(1 - gap / NeckReach, 0, 1);
        return halfHeight * 0.78 * Math.Pow(s, 0.7);
    }

    private void DrawNeck()
    {
        var gap = SplitGap;
        if (!SatelliteShowing || Motion.Intensity <= 0.01 || gap >= NeckReach || gap <= -AlongSize(Satellite.Width) * 0.7 || Satellite.ActualWidth < 1)
        {
            Bridge.Visibility = Visibility.Collapsed;
            return;
        }
        Rect pill, sat;
        try
        {
            pill = Pill.TransformToAncestor(Bridge).TransformBounds(new Rect(Pill.RenderSize));
            sat = Satellite.TransformToAncestor(Bridge).TransformBounds(new Rect(Satellite.RenderSize));
        }
        catch { return; }   // not in the tree yet

        // Work along the edge (a) and across it (c); the sides swap the two axes.
        var side = SideEdge;
        Point P(double a, double c) => side ? new Point(c, a) : new Point(a, c);
        double pEnd = side ? pill.Bottom : pill.Right, sStart = side ? sat.Top : sat.Left;
        double pMid = side ? (pill.Left + pill.Right) / 2 : (pill.Top + pill.Bottom) / 2;
        double sMid = side ? (sat.Left + sat.Right) / 2 : (sat.Top + sat.Bottom) / 2;
        double pHalf = (side ? pill.Width : pill.Height) / 2, sHalf = (side ? sat.Width : sat.Height) / 2;
        var waist = NeckWaist(gap, Math.Min(pHalf, sHalf));
        if (waist < 0.5) { Bridge.Visibility = Visibility.Collapsed; return; }

        // Anchors sit just inside each rounded end, where the glass hides them; the waist is between the two.
        double ax = pEnd - pHalf * 0.55, ay = pHalf * 0.8, bx = sStart + sHalf * 0.55, by = sHalf * 0.8;
        double mx = (pEnd + sStart) / 2, mc = (pMid + sMid) / 2;
        double k = Math.Max(1, (mx - ax) * 0.55), k2 = Math.Max(1, (bx - mx) * 0.55);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(ax, pMid - ay), true, true);
            ctx.BezierTo(P(ax + k, pMid - ay), P(mx - k * 0.8, mc - waist), P(mx, mc - waist), true, false);
            ctx.BezierTo(P(mx + k2 * 0.8, mc - waist), P(bx - k2, sMid - by), P(bx, sMid - by), true, false);
            ctx.LineTo(P(bx, sMid + by), true, false);
            ctx.BezierTo(P(bx - k2, sMid + by), P(mx + k2 * 0.8, mc + waist), P(mx, mc + waist), true, false);
            ctx.BezierTo(P(mx - k * 0.8, mc + waist), P(ax + k, pMid + ay), P(ax, pMid + ay), true, false);
        }
        g.Freeze();
        BridgeBack.Data = g;
        BridgeTint.Data = g;
        BridgeTint.Fill = Pill.Tint;
        if (Pill.SampleWallpaper && GlassSurface.Slice(Bridge, g.Bounds, true) is { } slice)
        {
            if (_neckBrush is null || !ReferenceEquals(_neckBrush.ImageSource, slice.Image))
            {
                _neckBrush = new ImageBrush(slice.Image) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_neckBrush, BitmapScalingMode.Linear);
            }
            _neckBrush.Viewbox = slice.Viewbox;
            BridgeBack.Fill = _neckBrush;
        }
        else BridgeBack.Fill = null;
        Bridge.Opacity = Math.Clamp(S.Opacity, 0.3, 1);
        Bridge.Visibility = Visibility.Visible;
    }

    /// <summary>Preview: makes the capsule busy (a pretend call) if nothing is showing, then splits off a Caps Lock bubble.</summary>
    private void DemoSplit()
    {
        if (!PillBusy) DemoCall();
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(900);
            if (PillBusy || SatelliteShowing) ShowSatellite(null, "A", true, "Caps Lock on", null, 2.6);
        });
    }
}
