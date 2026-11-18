using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WinOrbit.Design;

namespace WinOrbit.Island;

/// <summary>
/// A Liquid Glass quick-setting tile: frosted base, an accent fill when on, a curved sheen and a lit rim.
/// It reacts like glass under a fingertip: a soft light follows the pointer across it, pressing squeezes it,
/// and a ripple spreads from the exact point you clicked (bright when switching on, dark when switching off).
/// The chevron (or right-click) opens the related settings page.
/// </summary>
public sealed class QuickTile : Grid
{
    private static readonly CornerRadius Radius = new(18);
    private readonly Border _fill, _sheen, _bead;
    private readonly Ellipse _beadFill;
    private readonly TextBlock _icon, _label, _status, _chevron;
    private readonly Path _path = new() { StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
    private readonly Viewbox _pathBox;
    private readonly Border _light;
    private readonly RadialGradientBrush _lightBrush;
    private readonly Canvas _ripples = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _on, _available = true, _down;
    private Point _pressAt;

    public event Action? Toggled;
    public event Action? Secondary;

    public string Label { get => _label.Text; set { _label.Text = value; AutomationProperties.SetName(this, value); } }
    public string Status { get => _status.Text; set => _status.Text = value; }

    /// <summary>A Segoe Fluent glyph, or SVG-style path data in a 24×24 box (starts with "M") for a line icon.</summary>
    public string Glyph
    {
        get => _icon.Text;
        set
        {
            var isPath = value.StartsWith('M');
            _icon.Visibility = isPath ? Visibility.Collapsed : Visibility.Visible;
            _pathBox.Visibility = isPath ? Visibility.Visible : Visibility.Collapsed;
            if (isPath) _path.Data = Geometry.Parse(value); else _icon.Text = value;
        }
    }

    public bool IsOn
    {
        get => _on;
        set
        {
            _on = value;
            Motion.To(_fill, OpacityProperty, value ? 0.9 : 0, 300, Motion.Settle);
            Motion.To(_sheen, OpacityProperty, value ? 0.95 : 0.55, 300);
            var fg = value ? "T.OnAccent" : "T.Text";
            foreach (var t in new[] { _icon, _label, _status, _chevron }) t.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _path.SetResourceReference(Shape.StrokeProperty, fg);
            _beadFill.SetResourceReference(Shape.FillProperty, fg);
            _beadFill.Opacity = value ? 0.2 : 0.1;
            AutomationProperties.SetItemStatus(this, value ? "On" : "Off");
        }
    }

    /// <summary>False when the feature doesn't exist on this PC; clicking then opens Settings instead.</summary>
    public bool IsAvailable
    {
        get => _available;
        set { _available = value; Opacity = value ? 1 : 0.5; }
    }

    public QuickTile()
    {
        Height = 68;
        Margin = new Thickness(3);
        Focusable = true;
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        FocusVisualStyle = null;

        var glass = new Border { CornerRadius = Radius };
        glass.SetResourceReference(Border.BackgroundProperty, "T.Input");
        Children.Add(glass);

        // accent fill (on) with depth towards the bottom edge
        _fill = new Border { CornerRadius = Radius, Opacity = 0 };
        _fill.SetResourceReference(Border.BackgroundProperty, "T.Accent");
        _fill.Child = new Border
        {
            CornerRadius = Radius,
            Background = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.45),
                new(Color.FromArgb(0x00, 0, 0, 0), 0.6), new(Color.FromArgb(0x2A, 0, 0, 0), 1)
            }, 90))
        };
        Children.Add(_fill);

        // the light that follows the pointer — the "liquid" feel
        _lightBrush = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.5), new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1)
        }) { RadiusX = 0.75, RadiusY = 1.1, Center = new Point(0.5, 0.3), GradientOrigin = new Point(0.5, 0.3) };
        _light = new Border { CornerRadius = Radius, Opacity = 0, IsHitTestVisible = false, Background = _lightBrush };
        Children.Add(_light);

        // fixed specular sheen at the top-left, like light on a droplet
        _sheen = new Border
        {
            CornerRadius = Radius, Opacity = 0.55, IsHitTestVisible = false,
            Background = Frozen(new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 0.55), new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1)
            }) { Center = new Point(0.22, -0.15), GradientOrigin = new Point(0.22, -0.15), RadiusX = 0.95, RadiusY = 1.1 })
        };
        Children.Add(_sheen);

        // click ripples, clipped to the tile's rounded shape
        SizeChanged += (_, e) => _ripples.Clip = new RectangleGeometry(new Rect(e.NewSize), Radius.TopLeft, Radius.TopLeft);
        Children.Add(_ripples);

        // lit rim
        Children.Add(new Border
        {
            CornerRadius = Radius, BorderThickness = new Thickness(1), IsHitTestVisible = false,
            BorderBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.5), new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1)
            }, 90))
        });

        // content: icon bead, label, status
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 1), IsHitTestVisible = false };
        _bead = new Border { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center };
        var beadGrid = new Grid();
        _beadFill = new Ellipse { Opacity = 0.1 };
        _beadFill.SetResourceReference(Shape.FillProperty, "T.Text");
        beadGrid.Children.Add(_beadFill);
        beadGrid.Children.Add(new Ellipse { StrokeThickness = 1, Stroke = Frozen(new LinearGradientBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF), 90)) });
        _icon = new TextBlock { FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _icon.SetResourceReference(StyleProperty, "Icon");
        beadGrid.Children.Add(_icon);
        _pathBox = new Viewbox { Width = 17, Height = 17, Visibility = Visibility.Collapsed, Child = new Canvas { Width = 24, Height = 24, Children = { _path } } };
        beadGrid.Children.Add(_pathBox);
        _bead.Child = beadGrid;
        content.Children.Add(_bead);
        _label = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        _status = new TextBlock { FontSize = 10, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(_label);
        content.Children.Add(_status);
        Children.Add(content);

        _chevron = new TextBlock
        {
            Text = Design.Glyph.Right, FontSize = 9, Opacity = 0, Width = 22, Height = 22, TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 6, 0, 0), Margin = new Thickness(0, 3, 3, 0), Background = Brushes.Transparent, ToolTip = "Open settings"
        };
        _chevron.SetResourceReference(StyleProperty, "Icon");
        _chevron.HorizontalAlignment = HorizontalAlignment.Right;
        _chevron.VerticalAlignment = VerticalAlignment.Top;
        _chevron.MouseLeftButtonUp += (_, e) => { e.Handled = true; _down = false; Release(); Secondary?.Invoke(); };
        _chevron.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Children.Add(_chevron);

        IsOn = false;

        MouseEnter += (_, _) => { Motion.To(_chevron, OpacityProperty, 0.8, 140); Motion.To(_light, OpacityProperty, 1, 180); };
        MouseLeave += (_, _) =>
        {
            Motion.To(_chevron, OpacityProperty, 0, 160);
            Motion.To(_light, OpacityProperty, 0, 260);
            if (_down) { _down = false; Release(); }
        };
        MouseMove += (_, e) =>
        {
            if (ActualWidth < 1) return;
            var p = e.GetPosition(this);
            var c = new Point(p.X / ActualWidth, p.Y / ActualHeight);
            _lightBrush.Center = c;
            _lightBrush.GradientOrigin = c;
        };
        MouseLeftButtonDown += (_, e) => { _down = true; _pressAt = e.GetPosition(this); Press(); e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_down) return;
            _down = false; Release(); e.Handled = true;
            Activate(_pressAt);
        };
        MouseRightButtonUp += (_, e) => { e.Handled = true; Secondary?.Invoke(); };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Press(); Release(); Activate(new Point(ActualWidth / 2, ActualHeight / 2)); }
            else if (e.Key == Key.Apps) { e.Handled = true; Secondary?.Invoke(); }
        };
    }

    private void Activate(Point at)
    {
        if (!_available) { Secondary?.Invoke(); return; }
        var wasOn = _on;
        Toggled?.Invoke();
        // Bright ripple when something switches on, a soft dark one when it switches off.
        Ripple(at, _on && !wasOn ? Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF) : !_on && wasOn ? Color.FromArgb(0x50, 0, 0, 0) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    }

    private void Ripple(Point at, Color color)
    {
        if (Motion.Intensity <= 0.01) return;
        var size = Math.Max(ActualWidth, ActualHeight) * 2.4;
        var dot = new Ellipse
        {
            Width = size, Height = size, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(new GradientStopCollection { new(color, 0), new(Color.FromArgb((byte)(color.A / 3), color.R, color.G, color.B), 0.6), new(Color.FromArgb(0, color.R, color.G, color.B), 1) }),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var scale = new ScaleTransform(0.05, 0.05);
        dot.RenderTransform = scale;
        Canvas.SetLeft(dot, at.X - size / 2);
        Canvas.SetTop(dot, at.Y - size / 2);
        _ripples.Children.Add(dot);
        var dur = TimeSpan.FromMilliseconds(560);
        var grow = new DoubleAnimation(0.05, 1, dur) { EasingFunction = Motion.Settle };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimation(1, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = Motion.EaseOut };
        fade.Completed += (_, _) => _ripples.Children.Remove(dot);
        dot.BeginAnimation(OpacityProperty, fade);
    }

    private void Press()
    {
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 0.93, 110);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 0.93, 110);
    }

    private void Release()
    {
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 1, 420, Motion.Spring);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 1, 420, Motion.Spring);
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
