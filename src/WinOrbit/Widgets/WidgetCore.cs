using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;

namespace WinOrbit.Widgets;

/// <summary>Base class for the content of a widget. Start/Stop bracket every subscription and timer.</summary>
public abstract class WidgetView : Grid
{
    /// <summary>The one inner margin every widget uses, so their contents line up on the desktop.</summary>
    public const double Inset = 16;

    public WidgetInstance Instance { get; }
    public WidgetWindow? Host { get; internal set; }
    protected bool Running { get; private set; }

    protected WidgetView(WidgetInstance instance)
    {
        Instance = instance;
        Margin = new Thickness(Inset);
        // Every widget but the clock takes its text font from the clock's lettering (the clock sets the style).
        if (instance.Kind != "clock" && WidgetLook.Current.Text is { } family) System.Windows.Documents.TextElement.SetFontFamily(this, family);
    }

    public void Start() { if (Running) return; Running = true; OnStart(); }
    public void Stop() { if (!Running) return; Running = false; OnStop(); }
    protected abstract void OnStart();
    protected abstract void OnStop();

    /// <summary>Widget-specific entries for the right-click menu.</summary>
    public virtual void BuildMenu(ItemCollection items) { }

    protected void Save() => Hub.Store.Save();

    protected static MenuItem Item(string header, Action click, bool isChecked = false)
    {
        var m = new MenuItem { Header = header, IsChecked = isChecked };
        m.Click += (_, _) => click();
        return m;
    }

    protected MenuItem Choice(string header, string optionKey, params (string Label, string Value)[] options)
    {
        var parent = new MenuItem { Header = header };
        var current = optionKey == "variant" ? Instance.Variant : Instance.Opt(optionKey);
        foreach (var (label, value) in options)
            parent.Items.Add(Item(label, () =>
            {
                if (optionKey == "variant") Instance.Variant = value; else Instance.Options[optionKey] = value;
                Save();
                Host?.Rebuild();
                if (optionKey == "variant" && Hub.Ready)
                {
                    if (Instance.Kind == "clock") Hub.Widgets.RefreshLettering();   // the other widgets follow the clock's style
                    Hub.Widgets.NotifyChanged();
                }
            }, current == value));
        return parent;
    }
}

public sealed record WidgetKind(string Id, string Name, string Description, string Glyph, string Category,
    double Width, double Height, double MinWidth, double MinHeight, Func<WidgetInstance, WidgetView> Create)
{
    /// <summary>The look a new widget of this kind starts with: "" (glass card) or "bare".</summary>
    public string DefaultStyle { get; init; } = "";
}

/// <summary>
/// The few sizes every widget comes in, like phone widgets: one unit and one gap, so widgets of different kinds line up
/// in a tidy grid. The corner grip still resizes freely; these are what the Size menu and new widgets use.
/// </summary>
public static class WidgetSizes
{
    public const double Unit = 160, Gap = 16;

    public static readonly (string Id, string Name, int Cols, int Rows)[] All =
    {
        ("s", "Small", 1, 1), ("m", "Medium", 2, 1), ("w", "Wide", 3, 1), ("l", "Large", 2, 2)
    };

    internal static Size Of(int cols, int rows) => new(cols * Unit + (cols - 1) * Gap, rows * Unit + (rows - 1) * Gap);

    internal static Size Of(string id)
    {
        foreach (var s in All) if (s.Id == id) return Of(s.Cols, s.Rows);
        return Of(2, 1);
    }

    /// <summary>The size's id when a widget is exactly one of the sizes, or null after a free resize.</summary>
    internal static string? Match(double width, double height)
    {
        foreach (var s in All)
        {
            var size = Of(s.Cols, s.Rows);
            if (Math.Abs(size.Width - width) < 0.5 && Math.Abs(size.Height - height) < 0.5) return s.Id;
        }
        return null;
    }

    /// <summary>The sizes a kind of widget fits in (its content needs at least its minimum size).</summary>
    internal static IEnumerable<(string Id, string Name)> Fitting(double minWidth, double minHeight)
    {
        foreach (var s in All)
        {
            var size = Of(s.Cols, s.Rows);
            if (size.Width >= minWidth && size.Height >= minHeight) yield return (s.Id, s.Name);
        }
    }
}

/// <summary>
/// One widget = one small layered window parked at the bottom of the z-order (on the desktop).
/// It never activates on click, never appears in Alt-Tab, and only repaints when its content changes.
/// </summary>
public sealed class WidgetWindow : Window
{
    public const double Pad = 26;     // transparent margin that gives the shadow room
    public WidgetInstance Instance { get; }
    public WidgetKind Kind { get; }
    public IntPtr Handle { get; private set; }
    public bool Raised { get; set; }

    /// <summary>What the pointer grabs: holds the glass card, or (Bare style) the content straight on the wallpaper.</summary>
    private readonly Grid _frame = new();
    /// <summary>The widget's content and its resize grip.</summary>
    private readonly Grid _body = new();
    private GlassSurface? _glass;
    private readonly Thumb _grip = new() { Width = 18, Height = 18, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0 };
    private WidgetView _view;

    /// <summary>"Bare" widgets have no card: their text sits on the wallpaper with a soft shadow, like a clock painted on it.</summary>
    internal static bool IsBare(WidgetInstance i) => i.Opt("style") == "bare";

    // Fully transparent pixels of a layered window let clicks fall through to the desktop. Alpha 1 is invisible but
    // keeps the whole Bare widget draggable, not just its letters.
    private static readonly Brush HitBrush = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private bool _pressed, _dragging, _interactive, _closing;
    private Native.POINT _pressCursor;
    private double _pressX, _pressY;
    private static LayoutSettings L => Hub.Settings.Layout;

    public WidgetWindow(WidgetInstance instance, WidgetKind kind)
    {
        Instance = instance;
        Kind = kind;
        Title = "WinOrbit · " + kind.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "F.Family");
        SetResourceReference(ForegroundProperty, "T.Text");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        _view = kind.Create(instance);
        _view.Host = this;
        var root = new Grid { Margin = new Thickness(Pad) };
        _body.Children.Add(_view);
        _body.Children.Add(_grip);
        _grip.Template = GripTemplate();
        root.Children.Add(_frame);
        Content = root;
        ApplyStyle();
        ApplySize();

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            WindowFx.MakeOverlay(this, true);
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
        };
        Loaded += (_, _) =>
        {
            Place();
            ApplyAppearance();
            _view.Start();
            if (Hub.Ready) Hub.Wallpaper.BackdropsChanged += UpdateBareInk;
            if (Motion.Intensity > 0.01) { _frame.Opacity = 0; Motion.To(_frame, OpacityProperty, Math.Clamp(Instance.Opacity, 0.2, 1), 360); }
        };
        Closed += (_, _) => { _closing = true; _view.Stop(); if (Hub.Ready) Hub.Wallpaper.BackdropsChanged -= UpdateBareInk; };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        Deactivated += (_, _) => SetInteractive(false);

        _frame.MouseLeftButtonDown += OnDown;
        _frame.MouseMove += OnMove;
        _frame.MouseLeftButtonUp += OnUp;
        _frame.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        _frame.MouseEnter += (_, _) => { if (!L.Locked) Motion.To(_grip, OpacityProperty, 0.55, 160); };
        _frame.MouseLeave += (_, _) => Motion.To(_grip, OpacityProperty, 0, 220);
        _grip.DragDelta += OnResize;
        _grip.DragCompleted += (_, _) => { Hub.Store.Save(); Hub.Widgets.NotifyChanged(); };
        // Text input needs focus: grant it only when the user clicks into a text box.
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) => { if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) SetInteractive(true); }), true);
        AllowDrop = true;
    }

    private static ControlTemplate GripTemplate()
    {
        var f = new FrameworkElementFactory(typeof(Grid));
        f.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "");
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));
        text.SetValue(TextBlock.FontSizeProperty, 10.0);
        text.SetResourceReference(TextBlock.ForegroundProperty, "T.Text2");
        text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Bottom);
        text.SetValue(MarginProperty, new Thickness(0, 0, 5, 5));
        f.AppendChild(text);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = f };
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Recreate the content (after a variant or option change).</summary>
    public void Rebuild()
    {
        _view.Stop();
        _body.Children.Remove(_view);
        _view = Kind.Create(Instance);
        _view.Host = this;
        _body.Children.Insert(0, _view);
        ApplyStyle();
        if (IsLoaded) _view.Start();
    }

    /// <summary>Glass card or Bare (straight on the wallpaper), from the widget's "style" option.</summary>
    private void ApplyStyle()
    {
        var bare = IsBare(Instance);
        if (bare)
        {
            if (_glass is not null) { _glass.Content = null; _frame.Children.Remove(_glass); _glass = null; }
            if (!_frame.Children.Contains(_body)) _frame.Children.Add(_body);
            _frame.Background = HitBrush;
            _body.SetResourceReference(EffectProperty, "W.BareShadow");
            UpdateBareInk();
        }
        else
        {
            _frame.Background = null;
            _body.ClearValue(EffectProperty);
            SetInk(null);
            if (_glass is null)
            {
                _frame.Children.Remove(_body);
                _glass = new GlassSurface { SampleWallpaper = true, Content = _body };
                _frame.Children.Add(_glass);
            }
        }
    }

    // ---- Bare ink: a bare widget sits on the wallpaper, so its text takes its colour from the wallpaper under it, not
    // from the theme. A light theme over a dark wallpaper would otherwise put dark text on a dark picture.
    private bool? _ink;   // true = light text, false = dark text, null = the theme's own colours

    /// <summary>Light or dark text for the wallpaper under a bare widget: light when the area's average relative
    /// luminance is below 0.18, where white and black text have the same contrast (WCAG).</summary>
    internal static bool LightInk(double luminance) => luminance < 0.18;

    /// <summary>The average relative luminance of BGRA pixels (sRGB, alpha ignored).</summary>
    internal static double AverageLuminance(byte[] bgra)
    {
        var n = bgra.Length / 4;
        if (n == 0) return 0.5;
        double sum = 0;
        for (var k = 0; k < n; k++) sum += Wallpaper.ColorMath.Luminance(Color.FromRgb(bgra[k * 4 + 2], bgra[k * 4 + 1], bgra[k * 4]));
        return sum / n;
    }

    private void UpdateBareInk()
    {
        if (!IsBare(Instance) || !Hub.Ready) return;
        SetInk(WallpaperLuminanceUnder() is { } lum ? LightInk(lum) : null);
    }

    /// <summary>The blurred wallpaper under the widget, averaged; null when there is no picture of that screen yet.</summary>
    private double? WallpaperLuminanceUnder()
    {
        try
        {
            var mon = Monitors.Find(Instance.Monitor);
            if (Hub.Wallpaper.Backdrop(mon.Device) is not System.Windows.Media.Imaging.BitmapSource img || img.PixelWidth < 1 || mon.Bounds.Width < 1 || mon.Bounds.Height < 1) return null;
            double fx = (double)img.PixelWidth / mon.Bounds.Width, fy = (double)img.PixelHeight / mon.Bounds.Height;
            var x = (int)Math.Clamp(Instance.X * fx, 0, img.PixelWidth - 1);
            var y = (int)Math.Clamp(Instance.Y * fy, 0, img.PixelHeight - 1);
            var w = (int)Math.Clamp(Instance.Width * mon.Scale * fx, 1, img.PixelWidth - x);
            var h = (int)Math.Clamp(Instance.Height * mon.Scale * fy, 1, img.PixelHeight - y);
            System.Windows.Media.Imaging.BitmapSource src = img.Format == PixelFormats.Bgra32 || img.Format == PixelFormats.Pbgra32 || img.Format == PixelFormats.Bgr32
                ? img : new System.Windows.Media.Imaging.FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[w * h * 4];
            src.CopyPixels(new Int32Rect(x, y, w, h), pixels, w * 4, 0);
            return AverageLuminance(pixels);
        }
        catch { return null; }
    }

    private void SetInk(bool? light)
    {
        if (_ink == light) return;
        _ink = light;
        var r = _body.Resources;
        foreach (var key in new[] { "T.Text", "T.Text2", "T.Text3", "T.Track", "W.BareShadow" }) r.Remove(key);
        if (light is not { } l) return;
        var ink = l ? Color.FromRgb(0xF6, 0xF7, 0xFA) : Color.FromRgb(0x14, 0x17, 0x1E);
        SolidColorBrush B(Color c, double a) => Frozen(new SolidColorBrush(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B)));
        r["T.Text"] = B(ink, 1);
        r["T.Text2"] = B(ink, 0.72);
        r["T.Text3"] = B(ink, 0.5);
        r["T.Track"] = l ? B(Colors.White, 0.16) : B(Colors.Black, 0.13);
        r["W.BareShadow"] = ThemeEngine.BareShadow(dark: l);   // a dark shadow under light text, a light one under dark text
    }

    public void SetRunning(bool run) { if (run) _view.Start(); else _view.Stop(); }

    public void ApplySize()
    {
        Width = Math.Max(Kind.MinWidth, Instance.Width) + Pad * 2;
        Height = Math.Max(Kind.MinHeight, Instance.Height) + Pad * 2;
    }

    /// <summary>Switch to one of the standard sizes, keeping the top-left corner where it is.</summary>
    internal void Resize(string sizeId)
    {
        var size = WidgetSizes.Of(sizeId);
        Instance.Width = size.Width;
        Instance.Height = size.Height;
        ApplySize();
        Rebuild();   // some widgets lay out differently at another size (the forecast row, the Small recycle bin)
        Place();
        Hub.Store.Save();
        Hub.Widgets.NotifyChanged();
    }

    public void ApplyAppearance()
    {
        _frame.Opacity = Math.Clamp(Instance.Opacity, 0.2, 1);
        if (Topmost != Instance.Pinned) Topmost = Instance.Pinned;
        if (!Instance.Pinned && Handle != IntPtr.Zero && !Raised) Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Move the window so the glass sits at the instance's stored position on its display.</summary>
    public void Place()
    {
        if (Handle == IntPtr.Zero) return;
        var mon = Monitors.Find(Instance.Monitor);
        var pad = (int)Math.Round(Pad * mon.Scale);
        var maxX = Math.Max(0, mon.Bounds.Width - 60 * mon.Scale);
        var maxY = Math.Max(0, mon.Bounds.Height - 40 * mon.Scale);
        var x = mon.Bounds.Left + (int)Math.Clamp(Instance.X, -Instance.Width * mon.Scale + 60, maxX) - pad;
        var y = mon.Bounds.Top + (int)Math.Clamp(Instance.Y, 0, maxY) - pad;
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        _glass?.UpdateBackdrop();
        UpdateBareInk();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE && !_interactive) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
        if (msg == Native.WM_WINDOWPOSCHANGING && !Instance.Pinned && !Raised && !_closing)
        {
            // Desktop widgets stay beneath every application window, whatever tries to raise them.
            var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((pos.flags & Native.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != Native.HWND_BOTTOM)
            {
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    private void SetInteractive(bool on)
    {
        if (_interactive == on || Handle == IntPtr.Zero) return;
        _interactive = on;
        if (on) { Native.RemoveExStyle(Handle, Native.WS_EX_NOACTIVATE); Activate(); }
        else { Native.AddExStyle(Handle, Native.WS_EX_NOACTIVATE); Keyboard.ClearFocus(); }
    }

    // ----- drag with snapping -----
    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (L.Locked) return;
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null ||
            FindAncestor<Slider>(e.OriginalSource as DependencyObject) is not null || FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null) return;
        _pressed = true; _dragging = false;
        Native.GetCursorPos(out _pressCursor);
        _pressX = Instance.X; _pressY = Instance.Y;
        _frame.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        Native.GetCursorPos(out var p);
        int dx = p.X - _pressCursor.X, dy = p.Y - _pressCursor.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 5) return;
        _dragging = true;

        // Follow the cursor onto another display if it crosses over.
        var current = Monitors.Find(Instance.Monitor);
        var target = Monitors.FromPoint(p.X, p.Y);
        double absX = current.Bounds.Left + _pressX + dx, absY = current.Bounds.Top + _pressY + dy;
        if (target.Device != current.Device)
        {
            Instance.Monitor = target.Device;
            _pressX = absX - target.Bounds.Left - dx; _pressY = absY - target.Bounds.Top - dy;
            current = target;
        }
        double x = absX - current.Bounds.Left, y = absY - current.Bounds.Top;
        (Instance.X, Instance.Y) = WidgetManager.Snap(x, y, Instance, current, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        Place();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        if (_dragging) OnMove(sender, e);     // apply the release point: the last move event can be coalesced away
        _pressed = false;
        _frame.ReleaseMouseCapture();
        if (_dragging) { _dragging = false; Hub.Store.Save(); Hub.Widgets.NotifyChanged(); }
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        if (L.Locked) return;
        var grid = L.SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? Math.Max(4, L.GridSize) : 1;
        var w = Math.Clamp(Instance.Width + e.HorizontalChange, Kind.MinWidth, 1000);
        var h = Math.Clamp(Instance.Height + e.VerticalChange, Kind.MinHeight, 900);
        Instance.Width = Math.Round(w / grid) * grid;
        Instance.Height = Math.Round(h / grid) * grid;
        ApplySize();
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        _view.BuildMenu(menu.Items);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());

        var current = WidgetSizes.Match(Instance.Width, Instance.Height);
        var size = new MenuItem { Header = "Size" };
        foreach (var (id, name) in WidgetSizes.Fitting(Kind.MinWidth, Kind.MinHeight))
        {
            var s = WidgetSizes.Of(id);
            size.Items.Add(MenuItemOf($"{name}  ({s.Width:0} × {s.Height:0})", () => Resize(id), current == id));
        }
        if (size.Items.Count > 0) menu.Items.Add(size);
        var style = new MenuItem { Header = "Look" };
        style.Items.Add(MenuItemOf("Glass card", () => SetStyle(""), !IsBare(Instance)));
        style.Items.Add(MenuItemOf("Bare (on the wallpaper)", () => SetStyle("bare"), IsBare(Instance)));
        menu.Items.Add(style);

        var opacity = new MenuItem { Header = "Opacity" };
        foreach (var v in new[] { 1.0, 0.85, 0.7, 0.55, 0.4 })
        {
            var item = new MenuItem { Header = $"{v * 100:0}%", IsChecked = Math.Abs(Instance.Opacity - v) < 0.03 };
            item.Click += (_, _) => { Instance.Opacity = v; ApplyAppearance(); Hub.Store.Save(); };
            opacity.Items.Add(item);
        }
        menu.Items.Add(opacity);
        menu.Items.Add(MenuItemOf("Always on top", () => { Instance.Pinned = !Instance.Pinned; Raised = false; ApplyAppearance(); Hub.Store.Save(); }, Instance.Pinned));
        menu.Items.Add(MenuItemOf("Lock layout", () => { L.Locked = !L.Locked; Hub.Store.Save(); Hub.Widgets.NotifyChanged(); }, L.Locked));
        menu.Items.Add(MenuItemOf("Snap to grid", () => { L.SnapToGrid = !L.SnapToGrid; Hub.Store.Save(); Hub.Widgets.NotifyChanged(); }, L.SnapToGrid));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemOf("Hide widget", () => Hub.Widgets.SetVisible(Instance, false)));
        menu.Items.Add(MenuItemOf("Remove widget", () => Hub.Widgets.Remove(Instance)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemOf("Manage widgets…", () => App.Current.ShowMain("widgets")));
        menu.PlacementTarget = _frame;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void SetStyle(string style)
    {
        if (style.Length == 0) Instance.Options.Remove("style"); else Instance.Options["style"] = style;
        Hub.Store.Save();
        Rebuild();   // the content may lay itself out differently on the wallpaper
        Place();
    }

    private static MenuItem MenuItemOf(string header, Action click, bool isChecked = false)
    {
        var m = new MenuItem { Header = header, IsChecked = isChecked };
        m.Click += (_, _) => click();
        return m;
    }
}

/// <summary>Owns every widget window: creation, layout, z-order, visibility, pausing.</summary>
public sealed class WidgetManager
{
    private readonly Dictionary<string, WidgetWindow> _windows = new();
    private bool _paused;

    /// <summary>The set of widgets or their layout changed (for the management UI).</summary>
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    public WidgetManager(Dispatcher ui)
    {
        Hub.Foreground.ForegroundChanged += UpdateZOrder;
        Hub.Foreground.FullscreenChanged += OnFullscreen;
        Hub.Shell.DisplayChanged += () => ui.BeginInvoke(DispatcherPriority.Background, () => { foreach (var w in _windows.Values) w.Place(); });
    }

    public IEnumerable<WidgetWindow> Windows => _windows.Values;
    public WidgetWindow? WindowOf(WidgetInstance i) => _windows.TryGetValue(i.Id, out var w) ? w : null;

    /// <summary>
    /// Give every widget the lettering of the clock (or the HUD look when matching is off or there's no clock).
    /// Widgets are rebuilt only when the lettering actually changes; the clocks themselves never are.
    /// </summary>
    public void RefreshLettering()
    {
        var s = Hub.Settings;
        var lettering = WidgetLook.LetteringFor(s.Layout.MatchClockLettering ? WidgetLook.LeadClock(s.Widgets) : null);
        if (ReferenceEquals(lettering, WidgetLook.Current)) return;
        WidgetLook.Current = lettering;
        foreach (var w in _windows.Values.Where(w => w.Kind.Id != "clock")) w.Rebuild();
    }

    /// <summary>Make the open windows match settings.</summary>
    public void Apply()
    {
        RefreshLettering();
        var s = Hub.Settings;
        var wanted = s.Layout.WidgetsVisible ? s.Widgets.Where(w => w.Visible && WidgetCatalog.Find(w.Kind) is not null).ToList() : new List<WidgetInstance>();
        foreach (var id in _windows.Keys.Where(id => wanted.All(w => w.Id != id)).ToList())
        {
            _windows[id].Close();
            _windows.Remove(id);
        }
        foreach (var inst in wanted)
        {
            if (_windows.TryGetValue(inst.Id, out var existing))
            {
                if (!ReferenceEquals(existing.Instance, inst)) { existing.Close(); _windows.Remove(inst.Id); }
                else { existing.ApplySize(); existing.Place(); existing.ApplyAppearance(); continue; }
            }
            var win = new WidgetWindow(inst, WidgetCatalog.Find(inst.Kind)!);
            _windows[inst.Id] = win;
            win.Show();
            if (_paused) win.SetRunning(false);
        }
        Changed?.Invoke();
    }

    public WidgetInstance Add(string kindId, string? monitor = null) => Add(kindId, "", null, monitor);

    /// <summary>Add a widget in a given style (a clock style from the gallery, say), with options such as "style" = "bare".</summary>
    public WidgetInstance Add(string kindId, string variant, IReadOnlyDictionary<string, string>? options, string? monitor = null, string? sizeId = null)
    {
        var kind = WidgetCatalog.Find(kindId) ?? WidgetCatalog.All[0];
        var mon = Monitors.Find(monitor);
        var (w, h) = sizeId is null ? (kind.Width, kind.Height) : (WidgetSizes.Of(sizeId).Width, WidgetSizes.Of(sizeId).Height);
        var inst = new WidgetInstance { Kind = kind.Id, Monitor = mon.Device, Width = w, Height = h, Variant = variant };
        if (kind.DefaultStyle.Length > 0) inst.Options["style"] = kind.DefaultStyle;
        if (options is not null) foreach (var (k, v) in options) inst.Options[k] = v;
        (inst.X, inst.Y) = FindFreeSpot(inst, mon);
        Hub.Settings.Widgets.Add(inst);
        Hub.Settings.Layout.WidgetsVisible = true;
        Hub.Store.Save();
        Apply();
        return inst;
    }

    public void Remove(WidgetInstance inst)
    {
        Hub.Settings.Widgets.RemoveAll(w => w.Id == inst.Id);
        Hub.Store.Data.Notes.Remove(inst.Id);
        Hub.Store.Data.Tasks.Remove(inst.Id);
        Hub.Store.Save();
        Hub.Store.SaveData();
        Apply();
    }

    public void SetVisible(WidgetInstance inst, bool visible)
    {
        inst.Visible = visible;
        Hub.Store.Save();
        Apply();
    }

    public void ToggleAll()
    {
        Hub.Settings.Layout.WidgetsVisible = !Hub.Settings.Layout.WidgetsVisible;
        Hub.Store.Save();
        Apply();
    }

    public void CloseAll()
    {
        foreach (var w in _windows.Values.ToList()) w.Close();
        _windows.Clear();
    }

    // ----- placement -----
    /// <summary>Snap a proposed position (monitor-relative, physical px) to the grid and to the screen margins.</summary>
    public static (double X, double Y) Snap(double x, double y, WidgetInstance inst, MonitorInfo mon, bool bypass)
    {
        var l = Hub.Settings.Layout;
        if (!bypass && l.SnapToGrid)
        {
            var grid = Math.Max(4, l.GridSize) * mon.Scale;
            x = Math.Round(x / grid) * grid;
            y = Math.Round(y / grid) * grid;
            // Magnetic screen margins on the work area (so widgets line up clear of the taskbar).
            double margin = l.Margin * mon.Scale, w = inst.Width * mon.Scale, h = inst.Height * mon.Scale, pull = 14 * mon.Scale;
            double left = mon.Work.Left - mon.Bounds.Left + margin, top = mon.Work.Top - mon.Bounds.Top + margin;
            double right = mon.Work.Right - mon.Bounds.Left - margin - w, bottom = mon.Work.Bottom - mon.Bounds.Top - margin - h;
            if (Math.Abs(x - left) < pull) x = left; else if (Math.Abs(x - right) < pull) x = right;
            if (Math.Abs(y - top) < pull) y = top; else if (Math.Abs(y - bottom) < pull) y = bottom;
        }
        return (Math.Round(x), Math.Round(y));
    }

    private static (double, double) FindFreeSpot(WidgetInstance inst, MonitorInfo mon)
    {
        var l = Hub.Settings.Layout;
        double scale = mon.Scale, margin = l.Margin * scale, gap = l.Spacing * scale;
        double w = inst.Width * scale, h = inst.Height * scale;
        double left = mon.Work.Left - mon.Bounds.Left + margin, top = mon.Work.Top - mon.Bounds.Top + margin + 46 * scale;   // keep clear of the island
        double right = mon.Work.Right - mon.Bounds.Left - margin - w, bottom = mon.Work.Bottom - mon.Bounds.Top - margin - h;
        var others = Hub.Settings.Widgets.Where(o => o.Visible && Monitors.Find(o.Monitor).Device == mon.Device)
            .Select(o => new Rect(o.X - gap, o.Y - gap, o.Width * scale + gap * 2, o.Height * scale + gap * 2)).ToList();
        var step = Math.Max(8, l.GridSize) * scale;
        // Fill columns from the right-hand side, the conventional home of desktop widgets.
        for (var x = right; x >= left; x -= step)
        for (var y = top; y <= bottom; y += step)
        {
            var r = new Rect(x, y, w, h);
            if (!others.Any(o => o.IntersectsWith(r))) return (Math.Round(x), Math.Round(y));
        }
        return (Math.Round(Math.Max(left, right)), Math.Round(top));
    }

    /// <summary>Re-flow all visible widgets on each display into tidy columns using the margin and spacing settings.</summary>
    public void Arrange(bool alignRight)
    {
        var l = Hub.Settings.Layout;
        foreach (var group in Hub.Settings.Widgets.Where(w => w.Visible).GroupBy(w => Monitors.Find(w.Monitor).Device))
        {
            var mon = Monitors.Find(group.Key);
            double scale = mon.Scale, margin = l.Margin * scale, gap = l.Spacing * scale;
            double top = mon.Work.Top - mon.Bounds.Top + margin + 46 * scale, bottom = mon.Work.Bottom - mon.Bounds.Top - margin;
            double edge = alignRight ? mon.Work.Right - mon.Bounds.Left - margin : mon.Work.Left - mon.Bounds.Left + margin;
            double y = top, columnWidth = 0;
            foreach (var w in group.OrderBy(w => alignRight ? -w.X : w.X).ThenBy(w => w.Y))
            {
                double ww = w.Width * scale, hh = w.Height * scale;
                if (y + hh > bottom && y > top)
                {
                    edge += alignRight ? -(columnWidth + gap) : columnWidth + gap;
                    y = top; columnWidth = 0;
                }
                w.X = Math.Round(alignRight ? edge - ww : edge);
                w.Y = Math.Round(y);
                y += hh + gap;
                columnWidth = Math.Max(columnWidth, ww);
            }
        }
        Hub.Store.Save();
        Apply();
    }

    // ----- z-order & pausing -----
    /// <summary>
    /// Widgets live at the bottom of the z-order. When the user invokes "Show desktop" (Win+D) the shell lifts the
    /// desktop above everything, which would bury them, so while that mode is active they are lifted too.
    /// </summary>
    private void UpdateZOrder()
    {
        if (_windows.Count == 0) return;
        var fg = Native.GetForegroundWindow();
        var showDesktop = false;
        if (Hub.Foreground.DesktopForeground)
        {
            var probe = _windows.Values.FirstOrDefault(w => !w.Instance.Pinned && w.Handle != IntPtr.Zero);
            if (probe is not null)
            {
                var h = probe.Handle;
                for (var i = 0; i < 2000 && h != IntPtr.Zero; i++)
                {
                    h = Native.GetWindow(h, Native.GW_HWNDPREV);
                    if (h == fg) { showDesktop = true; break; }
                }
                if (probe.Raised) showDesktop = true;     // already lifted and the desktop is still in front
            }
        }
        foreach (var w in _windows.Values.Where(w => !w.Instance.Pinned && w.Handle != IntPtr.Zero))
        {
            if (showDesktop && !w.Raised)
            {
                w.Raised = true;
                Native.SetWindowPos(w.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            else if (!showDesktop && w.Raised)
            {
                Native.SetWindowPos(w.Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                w.Raised = false;
                Native.SetWindowPos(w.Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }
    }

    private void OnFullscreen()
    {
        var pause = Hub.Foreground.IsFullscreen && Hub.Settings.General.PauseInFullscreen;
        if (pause == _paused) return;
        _paused = pause;
        Hub.Monitor.Paused = pause;
        foreach (var w in _windows.Values)
        {
            w.SetRunning(!pause);
            if (w.Instance.Pinned) { if (pause) w.Hide(); else { w.Show(); w.Place(); } }
        }
    }
}
