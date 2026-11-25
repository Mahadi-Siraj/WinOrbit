using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WinOrbit.Design;
using WinOrbit.Wallpaper;

namespace WinOrbit.Shell.Pages;

/// <summary>Base for every page in the main window. Enter/Leave bracket any live work so hidden pages cost nothing.</summary>
public abstract class PageBase : StackPanel
{
    public abstract string Title { get; }
    public virtual string Subtitle => "";
    public MainWindow? Owner { get; set; }
    private readonly List<IDisposable> _live = new();
    private readonly List<Action> _unsubscribe = new();
    private bool _built;

    protected PageBase() { }

    public void Enter()
    {
        if (!_built) { _built = true; BuildAll(); }
        OnEnter();
    }

    private void BuildAll()
    {
        Build();
        // Simple settings: say what's hidden on this page and offer to show it, instead of leaving people to wonder.
        var hidden = Kit.HiddenAdvanced(this);
        if (hidden > 0 && Hub.Ready && !Hub.Settings.General.Advanced)
            Children.Add(Kit.Notice(Glyph.Settings, "Simple settings", $"{hidden} more advanced {(hidden == 1 ? "setting is" : "settings are")} hidden on this page. Search still finds every setting.", "T.Accent",
                Kit.Btn("Show advanced settings", () => Kit.SetAdvanced(true, Owner))));
    }

    public void Leave()
    {
        foreach (var d in _live) d.Dispose();
        _live.Clear();
        foreach (var u in _unsubscribe) u();
        _unsubscribe.Clear();
        OnLeave();
    }

    protected abstract void Build();
    protected virtual void OnEnter() { }
    protected virtual void OnLeave() { }
    public virtual void OnKey(KeyEventArgs e) { }

    /// <summary>Keep a subscription only while the page is visible.</summary>
    protected void Live(IDisposable d) => _live.Add(d);
    protected void Live(Action subscribe, Action unsubscribe) { subscribe(); _unsubscribe.Add(unsubscribe); }

    protected void Rebuild() { Children.Clear(); BuildAll(); }
}

/// <summary>Composable building blocks for settings pages.</summary>
public static class Kit
{
    /// <summary>Marks a setting row or section so Settings search can find it and jump to it.</summary>
    public sealed record SearchTag(string Label, string Description);

    // ---- simple and advanced settings ----
    public static readonly DependencyProperty AdvancedProperty = DependencyProperty.RegisterAttached("Advanced", typeof(bool), typeof(Kit), new PropertyMetadata(false));
    public static bool GetAdvanced(DependencyObject d) => (bool)d.GetValue(AdvancedProperty);

    /// <summary>Marks a section or row as advanced: hidden until Settings › Show advanced settings is on (search still finds it).</summary>
    public static T Adv<T>(T element) where T : UIElement
    {
        element.SetValue(AdvancedProperty, true);
        if (Hub.Ready && !Hub.Settings.General.Advanced) element.Visibility = Visibility.Collapsed;
        return element;
    }

    /// <summary>How many advanced sections and rows a page is hiding (one count for a hidden section, not for each row in it).</summary>
    internal static int HiddenAdvanced(DependencyObject root)
    {
        var n = 0;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is UIElement u && GetAdvanced(u) && u.Visibility != Visibility.Visible) { n++; continue; }
            n += HiddenAdvanced(child);
        }
        return n;
    }

    /// <summary>Switch between simple and advanced settings and redraw the window to match.</summary>
    public static void SetAdvanced(bool on, MainWindow? window)
    {
        Hub.Settings.General.ShowAdvanced = on;
        Hub.Store.Save();
        window?.UpdateOptionalPages();
        window?.Reload();
    }

    /// <summary>A titled card containing rows.</summary>
    public static Border Section(string title, string? description, params UIElement[] content)
    {
        var card = new Border { Margin = new Thickness(0, 0, 0, 16), Tag = string.IsNullOrEmpty(title) ? null : new SearchTag(title, description ?? "") };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        card.Padding = new Thickness(22, 18, 22, 10);
        var stack = new StackPanel();
        if (!string.IsNullOrEmpty(title)) stack.Children.Add(Ui.Text(title, "Text.Sub"));
        if (!string.IsNullOrEmpty(description)) stack.Children.Add(Ui.Text(description, "Text.Caption", 12.5).With(t => t.Margin = new Thickness(0, 3, 0, 0)));
        if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(description)) stack.Children.Add(new Border { Height = 8 });
        foreach (var c in content) stack.Children.Add(c);
        card.Child = stack;
        return card;
    }

    public static Border Card(UIElement content, Thickness? padding = null)
    {
        var card = new Border { Child = content };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        if (padding is { } p) card.Padding = p;
        return card;
    }

    /// <summary>Label + optional description on the left, a control on the right.</summary>
    public static Grid Row(string label, string? description, UIElement control, bool divider = true, string? glyph = null)
    {
        var g = Ui.Grid(glyph is null ? "*,auto" : "auto,*,auto");
        g.MinHeight = 52;
        g.Tag = new SearchTag(label, description ?? "");
        var col = 0;
        if (glyph is not null) { g.Children.Add(Ui.Icon(glyph, 17, "T.Text2").With(t => { t.Margin = new Thickness(0, 0, 14, 0); t.Width = 20; })); col = 1; }
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 18, 10) };
        left.Children.Add(Ui.Text(label, "Text.Body", 14));
        if (!string.IsNullOrEmpty(description)) left.Children.Add(Ui.Text(description, "Text.Caption", 12).With(t => t.Margin = new Thickness(0, 2, 0, 0)));
        g.Children.Add(left.At(0, col));
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(control.At(0, col + 1));
        if (divider)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
            line.SetResourceReference(Border.BackgroundProperty, "T.Divider");
            Grid.SetColumnSpan(line, 3);
            g.Children.Add(line);
        }
        return g;
    }

    public static ToggleButton Switch(bool value, Action<bool> changed, string? name = null)
    {
        var t = new ToggleButton { IsChecked = value };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        if (name is not null) System.Windows.Automation.AutomationProperties.SetName(t, name);
        t.Checked += (_, _) => changed(true);
        t.Unchecked += (_, _) => changed(false);
        return t;
    }

    public static Grid ToggleRow(string label, string? description, bool value, Action<bool> changed, bool divider = true, string? glyph = null)
        => Row(label, description, Switch(value, changed, label), divider, glyph);

    /// <summary>Slider with a live value read-out.</summary>
    public static StackPanel Slider(double min, double max, double value, Action<double> changed, Func<double, string>? format = null, double width = 220, double step = 0)
    {
        format ??= v => v.ToString("0", CultureInfo.CurrentCulture);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, SmallChange = step > 0 ? step : (max - min) / 100, LargeChange = (max - min) / 10 };
        if (step > 0) { s.TickFrequency = step; s.IsSnapToTickEnabled = true; }
        var label = Ui.Text(format(value), "Text.Caption", 12.5).With(t => { t.Width = 52; t.TextAlignment = TextAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; });
        s.ValueChanged += (_, e) => { label.Text = format(e.NewValue); changed(e.NewValue); };
        sp.Children.Add(s);
        sp.Children.Add(label);
        sp.Tag = s;
        return sp;
    }

    public static Grid SliderRow(string label, string? description, double min, double max, double value, Action<double> changed, Func<double, string>? format = null, bool divider = true, double step = 0)
        => Row(label, description, Slider(min, max, value, changed, format, 220, step), divider);

    /// <summary>Segmented control.</summary>
    public static Border Segmented(IList<string> options, int selected, Action<int> changed, string? name = null)
    {
        var host = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        host.SetResourceReference(Border.BackgroundProperty, "T.Input");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var group = "seg" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var rb = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            rb.Checked += (_, _) => changed(index);
            sp.Children.Add(rb);
        }
        if (name is not null) System.Windows.Automation.AutomationProperties.SetName(sp, name);
        host.Child = sp;
        return host;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> changed, double width = 220)
    {
        var c = new ComboBox { Width = width };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedIndex = selected;
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) changed(c.SelectedIndex); };
        return c;
    }

    /// <summary>
    /// A row whose control sits under the label at full width — for groups (many buttons, wide pickers) that would
    /// not fit beside it. Beside the label they would squeeze the label's column to nothing.
    /// </summary>
    public static Grid WideRow(string label, string? description, UIElement content, bool divider = true)
    {
        var g = new Grid { MinHeight = 52, Tag = new SearchTag(label, description ?? "") };
        var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 10) };
        sp.Children.Add(Ui.Text(label, "Text.Body", 14));
        if (!string.IsNullOrEmpty(description)) sp.Children.Add(Ui.Text(description, "Text.Caption", 12).With(t => t.Margin = new Thickness(0, 2, 0, 0)));
        if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(content);
        g.Children.Add(sp);
        if (divider)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
            line.SetResourceReference(Border.BackgroundProperty, "T.Divider");
            g.Children.Add(line);
        }
        return g;
    }

    /// <summary>Buttons that wrap onto further lines instead of running off the edge.</summary>
    public static WrapPanel WrapButtons(params UIElement[] buttons)
    {
        var wp = new WrapPanel();
        foreach (var b in buttons)
        {
            if (b is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
            wp.Children.Add(b);
        }
        return wp;
    }

    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(buttons[i]);
        }
        return sp;
    }

    public static Button Btn(string text, Action click, string? glyph = null, bool accent = false, string? tooltip = null)
    {
        var b = new Button { ToolTip = tooltip };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "Button.Accent");
        if (glyph is null) b.Content = text;
        else
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("F.Icons"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
        }
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Small rounded label (status, tier, warning).</summary>
    public static Border Chip(string text, string brush = "T.Accent", string? glyph = null)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var bg = new SolidColorBrush();
        b.Background = bg;
        b.Loaded += (_, _) => { if (b.TryFindResource(brush) is SolidColorBrush s) b.Background = new SolidColorBrush(s.Color) { Opacity = 0.16 }; };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null) sp.Children.Add(Ui.Icon(glyph, 11, brush).With(t => t.Margin = new Thickness(0, 0, 5, 0)));
        sp.Children.Add(Ui.Text(text, "Text.Caption", 11.5, FontWeights.SemiBold, brush));
        b.Child = sp;
        return b;
    }

    /// <summary>A wrap grid of equally sized cards.</summary>
    public static WrapPanel Gallery() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 0, -6, 10) };

    /// <summary>Notice banner for warnings, errors and empty states.</summary>
    public static Border Notice(string glyph, string title, string text, string brush = "T.Accent", UIElement? action = null)
    {
        var g = Ui.Grid("auto,*,auto");
        g.Children.Add(Ui.Icon(glyph, 18, brush).With(t => { t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, 2, 14, 0); }));
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(title, "Text.Body", 14, FontWeights.SemiBold));
        sp.Children.Add(Ui.Text(text, "Text.Caption", 12.5).With(t => t.Margin = new Thickness(0, 3, 0, 0)));
        g.Children.Add(sp.At(0, 1));
        if (action is not null) g.Children.Add(action.At(0, 2).With(a => { if (a is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(16, 0, 0, 0); } }));
        var b = Card(g, new Thickness(18, 14, 18, 14));
        b.Margin = new Thickness(0, 0, 0, 16);
        return b;
    }
}

/// <summary>
/// Colour field: a swatch that opens a picker with hue/saturation/lightness sliders, hex entry and
/// suggested swatches (the wallpaper palette first).
/// </summary>
public sealed class ColorField : Button
{
    private Color _color;
    private readonly Border _swatch = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
    private readonly TextBlock _hex = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 2, 0), FontSize = 12.5, Width = 62 };
    public event Action<Color>? Changed;

    public Color Color
    {
        get => _color;
        set { _color = value; _swatch.Background = new SolidColorBrush(value); _hex.Text = ColorMath.Hex(value); }
    }

    public ColorField(Color initial, string name = "Colour")
    {
        SetResourceReference(StyleProperty, typeof(Button));   // implicit styles don't reach subclasses
        Padding = new Thickness(7, 5, 10, 5);
        _swatch.SetResourceReference(Border.BorderBrushProperty, "T.StrokeStrong");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(_swatch);
        sp.Children.Add(_hex);
        Content = sp;
        Color = initial;
        System.Windows.Automation.AutomationProperties.SetName(this, name);
        Click += (_, _) => Open();
    }

    private void Open()
    {
        var (h0, s0, l0) = ColorMath.ToHsl(_color);
        double h = h0, s = s0, l = l0;
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 4 };
        var card = new Border { Width = 292, Padding = new Thickness(16), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(12) };
        card.SetResourceReference(Border.BackgroundProperty, "T.Popup");
        card.SetResourceReference(Border.BorderBrushProperty, "T.StrokeStrong");
        card.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.32 };
        System.Windows.Documents.TextElement.SetForeground(card, (Brush)FindResource("T.Text"));
        var stack = new StackPanel();
        var preview = new Border { Height = 46, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(preview);

        Rectangle Track(Brush fill) => new() { Height = 8, RadiusX = 4, RadiusY = 4, Fill = fill, Margin = new Thickness(0, 0, 0, -15), IsHitTestVisible = false };
        var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(ColorMath.FromHsl(i * 60, 1, 0.5), i / 6.0));
        var satBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var litBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        var hex = new TextBox { Margin = new Thickness(0, 12, 0, 0) };
        var updating = false;

        var opening = true;
        void Refresh(bool fromHex = false)
        {
            var c = ColorMath.FromHsl(h, s, l);
            preview.Background = new SolidColorBrush(c);
            satBrush.GradientStops.Clear();
            satBrush.GradientStops.Add(new GradientStop(ColorMath.FromHsl(h, 0, l), 0));
            satBrush.GradientStops.Add(new GradientStop(ColorMath.FromHsl(h, 1, l), 1));
            litBrush.GradientStops.Clear();
            litBrush.GradientStops.Add(new GradientStop(Colors.Black, 0));
            litBrush.GradientStops.Add(new GradientStop(ColorMath.FromHsl(h, s, 0.5), 0.5));
            litBrush.GradientStops.Add(new GradientStop(Colors.White, 1));
            if (!fromHex) { updating = true; hex.Text = ColorMath.Hex(c); updating = false; }
            Color = c;
            if (!opening) Changed?.Invoke(c);
        }

        Slider Make(double max, double value, Action<double> set)
        {
            var sl = new Slider { Minimum = 0, Maximum = max, Value = value, Margin = new Thickness(0, 0, 0, 8) };
            sl.Resources.Add("T.Accent", Brushes.Transparent);
            sl.Resources.Add("T.Track", Brushes.Transparent);
            sl.ValueChanged += (_, e) => { if (updating) return; set(e.NewValue); Refresh(); };
            return sl;
        }
        Slider hs = Make(360, h, v => h = v), ss = Make(1, s, v => s = v), ls = Make(1, l, v => l = v);
        foreach (var (label, track, slider) in new[] { ("Hue", Track(hueBrush), hs), ("Saturation", Track(satBrush), ss), ("Lightness", Track(litBrush), ls) })
        {
            stack.Children.Add(Ui.Text(label, "Text.Label").With(t => t.Margin = new Thickness(0, 0, 0, 4)));
            stack.Children.Add(track);
            stack.Children.Add(slider);
        }
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            var c = ColorMath.Parse(hex.Text, Colors.Transparent);
            if (c.A == 0 || hex.Text.Trim().TrimStart('#').Length is not (3 or 6)) return;
            (h, s, l) = ColorMath.ToHsl(c);
            updating = true; hs.Value = h; ss.Value = s; ls.Value = l; updating = false;
            Refresh(true);
        };
        stack.Children.Add(hex);

        stack.Children.Add(Ui.Text("FROM YOUR WALLPAPER", "Text.Label").With(t => t.Margin = new Thickness(0, 14, 0, 6)));
        var swatches = new WrapPanel();
        var p = Hub.Wallpaper.Palette;
        var suggestions = new[] { p.Accent, p.Secondary, p.Complement }.Concat(p.Swatches.Take(5))
            .Concat(new[] { "#4C8DFF", "#8C7CFF", "#FF6B8B", "#FFB454", "#36C9A0", "#5BC0EB", "#E6E8EE", "#1C1F26" }.Select(x => ColorMath.Parse(x)));
        foreach (var c in suggestions.Distinct())
        {
            var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6), Background = new SolidColorBrush(c), Cursor = Cursors.Hand, ToolTip = ColorMath.Hex(c), BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BorderBrushProperty, "T.Stroke");
            b.MouseLeftButtonUp += (_, _) => { (h, s, l) = ColorMath.ToHsl(c); updating = true; hs.Value = h; ss.Value = s; ls.Value = l; updating = false; Refresh(); };
            swatches.Children.Add(b);
        }
        stack.Children.Add(swatches);
        card.Child = stack;
        popup.Child = card;
        Refresh();
        opening = false;
        popup.IsOpen = true;
    }
}
