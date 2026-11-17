using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Glasswave.Design;

namespace Glasswave.Island;

/// <summary>
/// A Liquid Glass quick-setting tile: frosted base, an accent fill that fades in when the setting is on,
/// a curved specular sheen, a lit rim and an icon bead. Pressing springs it; the chevron (or right-click)
/// opens the related settings page.
/// </summary>
public sealed class QuickTile : Grid
{
    private readonly Border _fill, _sheen, _bead;
    private readonly Ellipse _beadFill;
    private readonly TextBlock _icon, _label, _status, _chevron;
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _on, _available = true, _down;

    public event Action? Toggled;
    public event Action? Secondary;

    public string Label { get => _label.Text; set { _label.Text = value; AutomationProperties.SetName(this, value); } }
    public string Status { get => _status.Text; set => _status.Text = value; }
    public string Glyph { get => _icon.Text; set => _icon.Text = value; }

    public bool IsOn
    {
        get => _on;
        set
        {
            _on = value;
            Motion.To(_fill, OpacityProperty, value ? 0.88 : 0, 260, Motion.EaseOut);   // a touch of wallpaper shows through
            Motion.To(_sheen, OpacityProperty, value ? 0.95 : 0.55, 260);
            var fg = value ? "T.OnAccent" : "T.Text";
            _icon.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _label.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _status.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _chevron.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _beadFill.SetResourceReference(Shape.FillProperty, value ? "T.OnAccent" : "T.Text");
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
        var radius = new CornerRadius(18);

        // frosted base
        var glass = new Border { CornerRadius = radius };
        glass.SetResourceReference(Border.BackgroundProperty, "T.Input");
        Children.Add(glass);

        // accent fill (on state) with depth towards the bottom edge
        _fill = new Border { CornerRadius = radius, Opacity = 0 };
        _fill.SetResourceReference(Border.BackgroundProperty, "T.Accent");
        _fill.Child = new Border
        {
            CornerRadius = radius,
            Background = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.45),
                new(Color.FromArgb(0x00, 0, 0, 0), 0.6), new(Color.FromArgb(0x2A, 0, 0, 0), 1)
            }, 90))
        };
        Children.Add(_fill);

        // curved specular sheen, brightest at the top-left like light on a glass droplet
        _sheen = new Border
        {
            CornerRadius = radius, Opacity = 0.55, IsHitTestVisible = false,
            Background = Frozen(new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 0.55), new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1)
            }) { Center = new Point(0.22, -0.15), GradientOrigin = new Point(0.22, -0.15), RadiusX = 0.95, RadiusY = 1.1 })
        };
        Children.Add(_sheen);

        // lit rim: bright top edge, fading down, faint return at the bottom
        Children.Add(new Border
        {
            CornerRadius = radius, BorderThickness = new Thickness(1), IsHitTestVisible = false,
            BorderBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.5), new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1)
            }, 90))
        });

        // content: bead on top, label and status centred beneath
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 1) };

        _bead = new Border { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center };
        var beadGrid = new Grid();
        _beadFill = new Ellipse { Opacity = 0.1 };
        _beadFill.SetResourceReference(Shape.FillProperty, "T.Text");
        beadGrid.Children.Add(_beadFill);
        beadGrid.Children.Add(new Ellipse
        {
            StrokeThickness = 1,
            Stroke = Frozen(new LinearGradientBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF), 90))
        });
        _icon = new TextBlock { FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _icon.SetResourceReference(StyleProperty, "Icon");
        beadGrid.Children.Add(_icon);
        _bead.Child = beadGrid;
        content.Children.Add(_bead);

        _label = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        _status = new TextBlock { FontSize = 10, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(_label);
        content.Children.Add(_status);
        Children.Add(content);

        // settings chevron in the top-right corner, shown on hover
        _chevron = new TextBlock
        {
            Text = Design.Glyph.Right, FontSize = 9, Opacity = 0, Width = 22, Height = 22, TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0), Background = Brushes.Transparent, ToolTip = "Open settings"
        };
        _chevron.SetResourceReference(StyleProperty, "Icon");
        _chevron.HorizontalAlignment = HorizontalAlignment.Right;
        _chevron.VerticalAlignment = VerticalAlignment.Top;
        _chevron.MouseLeftButtonUp += (_, e) => { e.Handled = true; _down = false; Release(); Secondary?.Invoke(); };
        _chevron.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Children.Add(_chevron);

        IsOn = false;

        MouseEnter += (_, _) => { Motion.To(_chevron, OpacityProperty, 0.8, 140); if (!_on) Motion.To(_sheen, OpacityProperty, 0.8, 160); };
        MouseLeave += (_, _) => { Motion.To(_chevron, OpacityProperty, 0, 160); if (!_on) Motion.To(_sheen, OpacityProperty, 0.55, 200); if (_down) { _down = false; Release(); } };
        MouseLeftButtonDown += (_, e) => { _down = true; Press(); e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_down) return;
            _down = false; Release(); e.Handled = true;
            Activate();
        };
        MouseRightButtonUp += (_, e) => { e.Handled = true; Secondary?.Invoke(); };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Press(); Release(); Activate(); }
            else if (e.Key == Key.Apps) { e.Handled = true; Secondary?.Invoke(); }
        };
    }

    private void Activate()
    {
        if (_available) Toggled?.Invoke(); else Secondary?.Invoke();
    }

    private void Press()
    {
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 0.94, 110);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 0.94, 110);
    }

    private void Release()
    {
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 1, 380, Motion.Spring);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 1, 380, Motion.Spring);
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
