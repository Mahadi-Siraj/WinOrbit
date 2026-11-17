using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Glasswave.Core;
using Glasswave.Design;
using Glasswave.Interop;
using Glasswave.Services;

namespace Glasswave.Launcher;

/// <summary>
/// The launcher: press the shortcut, type, press Enter. Finds apps (desktop and Store), Windows and Glasswave
/// settings, quick actions, recent files, does maths and unit conversion, and falls back to a web search in
/// your own browser. Liquid Glass like the island; hides the moment it loses focus.
/// </summary>
public sealed class LauncherWindow : Window
{
    private const double Pad = 30, PanelWidth = 640;
    private readonly GlassSurface _glass = new() { SampleWallpaper = true, CornerRadius = new CornerRadius(26), ShadowSize = 30 };
    private readonly TextBox _box = new();
    private readonly TextBlock _hint = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _footer = new();
    private List<LauncherItem> _results = new();
    private List<LauncherItem> _snapshot = new();
    private int _selected;
    private string? _armed;

    public LauncherWindow()
    {
        Title = "Glasswave Launcher";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth + Pad * 2;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "F.Family");
        SetResourceReference(ForegroundProperty, "T.Text");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        // search row
        var search = new Grid { Height = 58, Margin = new Thickness(20, 4, 20, 4) };
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        search.Children.Add(Ui.Icon(Glyph.Search, 18, "T.Accent").With(t => { t.Margin = new Thickness(0, 0, 14, 0); }));
        _box.SetResourceReference(StyleProperty, "TextBox.Bare");
        _box.FontSize = 21;
        _box.VerticalContentAlignment = VerticalAlignment.Center;
        _box.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(_box, "Search apps, settings and actions");
        _box.TextChanged += (_, _) => { _armed = null; Refresh(); };
        _box.PreviewKeyDown += OnKey;
        _hint.Text = "Search apps, settings, files — or type a sum";
        _hint.FontSize = 21;
        _hint.IsHitTestVisible = false;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.Margin = new Thickness(2, 0, 0, 0);
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "T.Text3");
        var boxHost = new Grid { Children = { _hint, _box } };
        Grid.SetColumn(boxHost, 1);
        search.Children.Add(boxHost);

        var divider = new Border { Height = 1, Margin = new Thickness(18, 0, 18, 0) };
        divider.SetResourceReference(Border.BackgroundProperty, "T.Divider");

        _list.Margin = new Thickness(10, 8, 10, 4);
        _footer.FontSize = 11;
        _footer.Margin = new Thickness(22, 2, 22, 12);
        _footer.SetResourceReference(TextBlock.ForegroundProperty, "T.Text3");
        _footer.Text = "↑ ↓ choose   ·   Enter open   ·   Esc close";

        var stack = new StackPanel { Children = { search, divider, _list, _footer } };
        _glass.Content = stack;
        Content = new Grid { Margin = new Thickness(Pad), Children = { _glass } };

        Deactivated += (_, _) => Dismiss();
        LocationChanged += (_, _) => _glass.UpdateBackdrop();
        SizeChanged += (_, _) => _glass.UpdateBackdrop();
    }

    /// <summary>Open on the monitor under the mouse, a quarter of the way down, with the text selected.</summary>
    public void Open()
    {
        _snapshot = LauncherIndex.Snapshot();
        _ = LauncherIndex.WarmUp().ContinueWith(_ => Dispatcher.BeginInvoke(Refresh));
        Native.GetCursorPos(out var cursor);
        var mon = Monitors.All().FirstOrDefault(m => cursor.X >= m.Bounds.Left && cursor.X < m.Bounds.Right && cursor.Y >= m.Bounds.Top && cursor.Y < m.Bounds.Bottom) ?? Monitors.Primary;
        var scale = mon.Scale <= 0 ? 1 : mon.Scale;
        Left = (mon.Bounds.Left + mon.Bounds.Width / 2.0) / scale - Width / 2;
        Top = (mon.Bounds.Top + mon.Bounds.Height * 0.2) / scale - Pad;
        _armed = null;
        Refresh();
        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetForegroundWindow(hwnd);
        _box.Focus();
        _box.SelectAll();
        if (Motion.Intensity > 0.01)
        {
            _glass.Opacity = 0;
            var st = new ScaleTransform(0.97, 0.97);
            _glass.RenderTransformOrigin = new Point(0.5, 0);
            _glass.RenderTransform = st;
            Motion.To(_glass, OpacityProperty, 1, 160);
            Motion.To(st, ScaleTransform.ScaleXProperty, 1, 260, Motion.Spring);
            Motion.To(st, ScaleTransform.ScaleYProperty, 1, 260, Motion.Spring);
        }
        _glass.UpdateBackdrop();
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        Hide();
        _armed = null;
    }

    private void Refresh()
    {
        _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _results = LauncherIndex.Search(_box.Text, _snapshot);
        _selected = 0;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_results.Count == 0)
        {
            _list.Children.Add(Ui.Text(_box.Text.Length == 0 ? "Start typing — the apps you open most will show up here." : "No matches.", "Text.Caption", 12.5).With(t => t.Margin = new Thickness(12, 6, 12, 8)));
            return;
        }
        for (var i = 0; i < _results.Count; i++) _list.Children.Add(Row(_results[i], i));
    }

    private FrameworkElement Row(LauncherItem item, int index)
    {
        var selected = index == _selected;
        var row = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        if (selected) { row.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft"); }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement icon;
        var img = item.AppId is not null ? AppIcons.Get(item.AppId) : null;
        if (img is not null) icon = new Image { Source = img, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left };
        else
        {
            var bead = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Left };
            bead.SetResourceReference(Border.BackgroundProperty, "T.Input");
            bead.Child = Ui.Icon(item.Glyph, 14, item.Kind is ItemKind.Calc ? "T.Accent" : "T.Text2");
            icon = bead;
        }
        g.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(item.Title, "Text.Body", item.Kind == ItemKind.Calc ? 17 : 14, FontWeights.SemiBold).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
        var sub = _armed == item.Key ? "Press Enter again to confirm" : item.Subtitle;
        text.Children.Add(Ui.Text(sub, "Text.Caption", 11.5).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; if (_armed == item.Key) t.SetResourceReference(TextBlock.ForegroundProperty, "T.Warn"); }));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (selected)
        {
            var enter = Ui.Text("↵", "Text.Caption", 13).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(8, 0, 0, 0); });
            Grid.SetColumn(enter, 2);
            g.Children.Add(enter);
        }
        row.Child = g;
        row.MouseEnter += (_, _) => { if (_selected != index) { _selected = index; Render(); } };
        row.MouseLeftButtonUp += (_, _) => { _selected = index; Run(); };
        return row;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_box.Text.Length > 0) _box.Clear(); else Dismiss();
                e.Handled = true; break;
            case Key.Down:
                if (_results.Count > 0) { _selected = (_selected + 1) % _results.Count; _armed = null; Render(); }
                e.Handled = true; break;
            case Key.Up:
                if (_results.Count > 0) { _selected = (_selected - 1 + _results.Count) % _results.Count; _armed = null; Render(); }
                e.Handled = true; break;
            case Key.Enter:
                Run(); e.Handled = true; break;
        }
    }

    private void Run()
    {
        if (_selected < 0 || _selected >= _results.Count) return;
        var item = _results[_selected];
        if (item.Confirm && _armed != item.Key) { _armed = item.Key; Render(); return; }
        _armed = null;
        Dismiss();
        try
        {
            LauncherIndex.Used(item);
            item.Run();
            if (item.Kind == ItemKind.Calc) Hub.Island.Peek(Glyph.Copy, "Result copied");
        }
        catch (Exception ex)
        {
            Log.Warn($"Launcher could not open {item.Title}: {ex.Message}");
            Dialogs.Message("Couldn't open that", ex.Message);
        }
    }
}
