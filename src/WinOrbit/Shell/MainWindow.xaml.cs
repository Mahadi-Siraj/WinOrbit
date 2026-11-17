using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Shell.Pages;

namespace WinOrbit.Shell;

public partial class MainWindow : Window
{
    private readonly List<(string Id, string Glyph, string Name, Func<PageBase> Create)> _pages = new()
    {
        ("home", Glyph.Home, "Home", () => new HomePage()),
        ("appearance", Glyph.Personalize, "Appearance", () => new AppearancePage()),
        ("studio", Glyph.Drop, "Liquid Glass Editor", () => new StudioPage()),
        ("wallpaper", Glyph.Picture, "Wallpaper & Gradients", () => new WallpaperPage()),
        ("island", Glyph.More, "Dynamic Island", () => new IslandPage()),
        ("widgets", Glyph.Tiles, "Widgets", () => new WidgetsPage()),
        ("rainmeter", Glyph.Diagnostic, "Rainmeter", () => new RainmeterPage()),
        ("tweaks", Glyph.System, "Windows Tweaks", () => new TweaksPage()),
        ("profiles", Glyph.Profiles, "Profiles", () => new ProfilesPage()),
        ("settings", Glyph.Settings, "Settings", () => new SettingsPage()),
        ("about", Glyph.Info, "About & Diagnostics", () => new AboutPage()),
    };

    private PageBase? _current;
    private string _currentId = "";
    private bool _systemBackdrop, _software;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var p in _pages)
        {
            var item = new ListBoxItem { Content = p.Name, Tag = p.Glyph, DataContext = p.Id };
            System.Windows.Automation.AutomationProperties.SetName(item, p.Name);
            Nav.Items.Add(item);
        }
        SourceInitialized += (_, _) => ApplyBackdrop();
        // If the GPU path fails to present this window, fall back to software rendering with an opaque material
        // (DWM backdrops need a transparent hardware surface).
        RenderGuard.Attach(this, Frame, () => { _software = true; ApplyBackdrop(); });
        Hub.Theme.Applied += OnTheme;
        Hub.Profiles.Changed += UpdateProfileChip;
        Closed += (_, _) =>
        {
            Hub.Theme.Applied -= OnTheme;
            Hub.Profiles.Changed -= UpdateProfileChip;
            _current?.Leave();
        };
        StateChanged += (_, _) =>
        {
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            // A maximised chrome-less window overhangs the screen by the resize border; pad it back in.
            Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        };
        SizeChanged += (_, e) => SideColumn.Width = new GridLength(e.NewSize.Width < 1000 ? 212 : 248);
        IsVisibleChanged += (_, _) =>
        {
            // Pages with live data stop all work while the window is hidden.
            if (!IsVisible) _current?.Leave(); else _current?.Enter();
        };
        PreviewKeyDown += OnKeys;
        IsVisibleChanged += (_, _) => { if (IsVisible) UpdateOptionalPages(); };
        UpdateProfileChip();
        UpdateOptionalPages();
        Navigate("home");
    }

    // ------------------------------------------------------------------ settings search
    private List<SettingHit> _hits = new();
    private int _hitIndex;

    private IEnumerable<(string Id, string Name, Func<PageBase> Create)> SearchablePages()
        => _pages.Where(p => p.Id != "rainmeter" || Hub.Rainmeter.PageWanted()).Select(p => (p.Id, p.Name, p.Create));

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _hits = SettingsSearch.Find(SearchBox.Text, SearchablePages());
        _hitIndex = 0;
        RenderHits();
    }

    private void RenderHits()
    {
        SearchResults.Children.Clear();
        if (SearchBox.Text.Trim().Length < 2) { SearchPopup.IsOpen = false; return; }
        if (_hits.Count == 0)
            SearchResults.Children.Add(Ui.Text("No settings match.", "Text.Caption", 12.5).With(t => t.Margin = new Thickness(10, 8, 10, 8)));
        for (var i = 0; i < _hits.Count; i++)
        {
            var hit = _hits[i];
            var index = i;
            var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            if (i == _hitIndex) row.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(hit.Label, "Text.Body", 13, FontWeights.SemiBold).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
            sp.Children.Add(Ui.Text(hit.PageName + (hit.Description.Length > 0 ? " · " + hit.Description : ""), "Text.Caption", 11).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
            row.Child = sp;
            row.MouseLeftButtonUp += (_, _) => { _hitIndex = index; OpenHit(); };
            SearchResults.Children.Add(row);
        }
        SearchPopup.IsOpen = true;
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _hits.Count > 0: _hitIndex = (_hitIndex + 1) % _hits.Count; RenderHits(); e.Handled = true; break;
            case Key.Up when _hits.Count > 0: _hitIndex = (_hitIndex - 1 + _hits.Count) % _hits.Count; RenderHits(); e.Handled = true; break;
            case Key.Enter: OpenHit(); e.Handled = true; break;
            case Key.Escape: SearchBox.Clear(); SearchPopup.IsOpen = false; Frame.Focus(); e.Handled = true; break;
        }
    }

    private void OnSearchLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Let a click on a result land before the list closes.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => { if (!SearchBox.IsKeyboardFocusWithin && !SearchPopup.IsMouseOver) SearchPopup.IsOpen = false; });
    }

    private void OpenHit()
    {
        if (_hitIndex < 0 || _hitIndex >= _hits.Count) return;
        var hit = _hits[_hitIndex];
        SearchPopup.IsOpen = false;
        OpenSetting(hit.PageId, hit.Label);
    }

    /// <summary>Open a page and reveal one setting on it (used by search and the launcher).</summary>
    public void OpenSetting(string pageId, string label)
    {
        Navigate(pageId);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { if (_current is not null) SettingsSearch.Reveal(_current, label); });
    }

    /// <summary>Show the Rainmeter entry only when it's useful (see Settings › Rainmeter page).</summary>
    public void UpdateOptionalPages()
    {
        var index = _pages.FindIndex(p => p.Id == "rainmeter");
        if (index >= 0 && Nav.Items[index] is ListBoxItem item)
            item.Visibility = Hub.Rainmeter.PageWanted() || _currentId == "rainmeter" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeys(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D9)
        {
            // Ctrl+N picks the Nth page that is actually listed.
            var visible = Nav.Items.OfType<ListBoxItem>().Select((it, i) => (it, i)).Where(x => x.it.Visibility == Visibility.Visible).ToList();
            var n = e.Key - Key.D1;
            if (n < visible.Count) Nav.SelectedIndex = visible[n].i;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) { Close(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.K) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else _current?.OnKey(e);
    }

    private void OnTheme()
    {
        ApplyBackdrop();
        UpdateProfileChip();
    }

    private void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var source = HwndSource.FromHwnd(hwnd);
        var material = _software ? "glass" : Hub.Settings.General.WindowMaterial;
        _systemBackdrop = material != "glass" && WindowFx.ApplySystemBackdrop(this, Hub.Theme.IsDark, material == "acrylic");
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = _systemBackdrop ? Colors.Transparent : Colors.Black;
        // Without a system backdrop (Windows 10, or "Glass" chosen) use WinOrbit's own wallpaper-sampled glass.
        FallbackGlass.Visibility = _systemBackdrop ? Visibility.Collapsed : Visibility.Visible;
        FallbackGlass.SampleWallpaper = !_systemBackdrop;
        Tint.Opacity = _systemBackdrop ? 1 : 0.6;
    }

    public void RefreshBackdrop() => ApplyBackdrop();

    public void Navigate(string id)
    {
        var index = _pages.FindIndex(p => p.Id == id);
        if (index < 0) index = 0;
        if (Nav.SelectedIndex == index && _current is not null) { _current.Enter(); return; }
        Nav.SelectedIndex = index;
    }

    private void OnNav(object sender, SelectionChangedEventArgs e) => Show(false);

    private void Show(bool force)
    {
        if (Nav.SelectedIndex < 0) return;
        var p = _pages[Nav.SelectedIndex];
        if (p.Id == _currentId && !force) return;
        _current?.Leave();
        _currentId = p.Id;
        try
        {
            _current = p.Create();
            _current.Owner = this;
            PageTitle.Text = _current.Title;
            PageSubtitle.Text = _current.Subtitle;
            PageSubtitle.Visibility = string.IsNullOrEmpty(_current.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
            PageHost.Content = _current;
            Scroller.ScrollToTop();
            _current.Enter();
            Motion.Enter(Header, 8, 260);
            Motion.Enter(_current, 16, 340);
        }
        catch (Exception ex)
        {
            Log.Error($"Page {p.Id} failed", ex);
            PageHost.Content = Ui.Text("This page could not be shown. Details are in the log (About & Diagnostics).\n\n" + ex.Message, "Text.Muted");
        }
    }

    /// <summary>Rebuild the current page (after an import or a profile switch changed what it shows).</summary>
    public void Reload()
    {
        var offset = Scroller.VerticalOffset;
        Show(true);
        Scroller.ScrollToVerticalOffset(offset);
    }

    private void UpdateProfileChip()
    {
        var p = Hub.Profiles.Find(Hub.Settings.ActiveProfileId);
        ProfileName.Text = p?.Name ?? "Custom";
        ProfileGlyph.Text = p?.Glyph ?? Glyph.Personalize;
        SideSubtitle.Text = Hub.Theme.Current.Name;
    }

    private void OnProfileChip(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var p in Hub.Profiles.All)
        {
            var item = new MenuItem { Header = p.Name, IsChecked = p.Id == Hub.Settings.ActiveProfileId };
            item.Click += (_, _) => Hub.Profiles.Apply(p);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "Manage profiles…" };
        manage.Click += (_, _) => Navigate("profiles");
        menu.Items.Add(manage);
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void OnMinimize(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object s, RoutedEventArgs e)
    {
        if (Hub.Settings.General.CloseToTray) Close();      // the desktop experience keeps running from the tray
        else App.Current.Quit();
    }
}
