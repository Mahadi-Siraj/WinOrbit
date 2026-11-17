using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glasswave.Core;
using Glasswave.Design;
using Glasswave.Rainmeter;
using Glasswave.Services;
using Glasswave.Wallpaper;
using Glasswave.WindowsIntegration;
using Microsoft.Win32;

namespace Glasswave.Shell.Pages;

// =================================================================================================
public sealed class RainmeterPage : PageBase
{
    public override string Title => "Rainmeter";
    public override string Subtitle => "Manage the Rainmeter skins you already have. Rainmeter keeps running them exactly as before — Glasswave only sends it documented commands and never edits skin files.";

    private RainmeterService R => Hub.Rainmeter;
    private string _filter = "all", _search = "";
    private WrapPanel? _library;
    private TextBlock? _count;
    private FileSystemWatcher? _iniWatcher;

    protected override void Build()
    {
        R.Detect();
        if (!R.Installed) { BuildNotInstalled(); return; }
        R.Scan();

        var running = RainmeterService.IsRunning;
        var status = Ui.Grid("auto,*,auto");
        var badge = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Child = Ui.Icon(Glyph.Diagnostic, 20, "T.Accent") };
        badge.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
        status.Children.Add(badge);
        var info = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Ui.Text($"Rainmeter {R.Version}", "Text.Sub", 16));
        line.Children.Add(Kit.Chip(running ? "Running" : "Not running", running ? "T.Good" : "T.Warn").With(c => c.Margin = new Thickness(10, 0, 0, 0)));
        if (R.Portable) line.Children.Add(Kit.Chip("Portable", "T.Accent").With(c => c.Margin = new Thickness(6, 0, 0, 0)));
        info.Children.Add(line);
        info.Children.Add(Ui.Text(R.SkinsPath, "Text.Caption", 12).With(t => { t.Margin = new Thickness(0, 3, 0, 0); t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
        status.Children.Add(info.At(0, 1));
        var actions = Kit.Buttons(
            running ? Kit.Btn("Refresh all", () => { R.RefreshAll(); Later(Rebuild); }, Glyph.Refresh, tooltip: "!RefreshApp — reloads every skin and Rainmeter.ini without restarting Glasswave")
                    : Kit.Btn("Start Rainmeter", () => { R.Start(); Later(Rebuild, 2500); }, Glyph.Play, true),
            Kit.Btn("Import .rmskin", ImportPackage, Glyph.Import),
            Kit.Btn("Skins folder", R.OpenSkinsFolder, Glyph.Folder),
            Kit.Btn("Manage", () => R.Manage(null), Glyph.Settings, tooltip: "Open Rainmeter's own Manage window"));
        status.Children.Add(actions.At(0, 2));
        Children.Add(Kit.Card(status, new Thickness(20, 16, 20, 16)).With(c => c.Margin = new Thickness(0, 0, 0, 16)));

        if (!running) Children.Add(Kit.Notice(Glyph.Info, "Rainmeter is not running", "Skins can only be shown while Rainmeter runs. Start it here; activating a skin will also start it.", "T.Warn"));

        // ---- toolbar ----
        var bar = Ui.Grid("*,auto", "auto");
        bar.Margin = new Thickness(0, 0, 0, 12);
        var search = new TextBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Search skins by name, author or folder" };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search skins");
        var hint = Ui.Text("Search skins…", "Text.Caption", 13, null, "T.Text3").With(t => { t.IsHitTestVisible = false; t.Margin = new Thickness(12, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; });
        search.TextChanged += (_, _) => { _search = search.Text.Trim(); hint.Visibility = _search.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderLibrary(); };
        var searchHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        searchHost.Children.Add(search); searchHost.Children.Add(hint);
        var left = Kit.Buttons(searchHost, Kit.Segmented(new[] { "All", "Active", "Favourites", "Recent" }, 0, i => { _filter = new[] { "all", "active", "fav", "recent" }[i]; RenderLibrary(); }, "Filter"));
        bar.Children.Add(left);
        _count = Ui.Text("", "Text.Caption", 12).With(t => t.VerticalAlignment = VerticalAlignment.Center).At(0, 1);
        bar.Children.Add(_count);
        Children.Add(bar);

        _library = new WrapPanel { Margin = new Thickness(-6, 0, -6, 0) };
        Children.Add(_library);
        RenderLibrary();

        Children.Add(Kit.Section("How the integration works", null,
            Kit.Row("Engine", "Skins are rendered by Rainmeter itself, so every skin keeps working exactly as its author intended. Glasswave cannot draw Rainmeter skins inside its own windows, and doesn't pretend to.", Kit.Chip("Rainmeter.exe", "T.Accent"), false),
            Kit.Row("Commands", "Activate, deactivate, refresh, move, layering and transparency use Rainmeter's documented bangs (!ActivateConfig, !Move, !ZPos …).", Kit.Chip("Documented", "T.Good")),
            Kit.Row("Your files", "Rainmeter.ini and skin files are only read. Packages are installed by Rainmeter's own Skin Installer, which backs up any skin it replaces.", Kit.Chip("Read-only", "T.Good")),
            Kit.Row("Resources", "Rainmeter runs as its own process; its CPU and memory are shown separately under About & Diagnostics.", Kit.Btn("View", () => Owner?.Navigate("about")))));
    }

    private void Later(Action a, int ms = 900)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); a(); };
        t.Start();
    }

    private void BuildNotInstalled()
    {
        Children.Add(Kit.Notice(Glyph.Info, "Rainmeter isn't installed", "Rainmeter is a free, open-source desktop customisation tool with thousands of community skins. When it is installed, this page becomes a full library for your skins.", "T.Accent"));
        var steps = new StackPanel();
        steps.Children.Add(Kit.Row("1. Download from the official site", "rainmeter.net is the only official source. Glasswave never downloads or installs it for you.", Kit.Btn("Open rainmeter.net", () => ThirdParty.Open("https://www.rainmeter.net"), Glyph.Globe), false));
        steps.Children.Add(Kit.Row("2. Install it normally", "The standard installer needs no extra configuration. A portable install also works.", Kit.Chip("Free · open source", "T.Accent")));
        steps.Children.Add(Kit.Row("3. Come back here", "Glasswave detects Rainmeter automatically.", Kit.Btn("Check again", Rebuild, Glyph.Refresh, true)));
        steps.Children.Add(Kit.Row("Already installed somewhere else?", "Point Glasswave at Rainmeter.exe (for example a portable copy).", Kit.Btn("Locate Rainmeter.exe…", Locate, Glyph.Folder)));
        Children.Add(Kit.Section("Get started", null, steps));
        var what = new StackPanel();
        what.Children.Add(Kit.Row("Visual skin library", "Search, filter, favourites, recently used and previews of every installed skin.", Kit.Chip("Ready", "T.Good"), false));
        what.Children.Add(Kit.Row("One-click control", "Activate, deactivate, refresh, position, layer and fade skins.", Kit.Chip("Ready", "T.Good")));
        what.Children.Add(Kit.Row("Safe .rmskin import", "See what a package contains — skins, layouts and any plugin DLLs — before Rainmeter's installer runs.", Kit.Chip("Ready", "T.Good")));
        Children.Add(Kit.Section("What you'll get", null, what));
    }

    private void Locate()
    {
        var dlg = new OpenFileDialog { Title = "Locate Rainmeter.exe", Filter = "Rainmeter|Rainmeter.exe" };
        if (dlg.ShowDialog() != true) return;
        Hub.Settings.Rainmeter.CustomExePath = dlg.FileName;
        Hub.Store.Save();
        Rebuild();
    }

    private void RenderLibrary()
    {
        if (_library is null) return;
        _library.Children.Clear();
        var prefs = Hub.Settings.Rainmeter;
        IEnumerable<RmSkin> skins = R.Skins;
        if (_filter == "active") skins = skins.Where(s => s.Active);
        else if (_filter == "fav") skins = skins.Where(s => prefs.Favourites.Contains(s.Config));
        else if (_filter == "recent") skins = prefs.Recent.Select(c => R.Skins.FirstOrDefault(s => s.Config == c)).OfType<RmSkin>();
        if (_search.Length > 0)
            skins = skins.Where(s => s.Config.Contains(_search, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || s.Author.Contains(_search, StringComparison.OrdinalIgnoreCase));
        var list = skins.ToList();
        if (_count is not null) _count.Text = $"{list.Count} of {R.Skins.Count} skins · {R.Skins.Count(s => s.Active)} active";

        if (list.Count == 0)
        {
            var empty = R.Skins.Count == 0
                ? Kit.Notice(Glyph.Folder, "No skins found", $"There are no skins in {R.SkinsPath}. Import a .rmskin package to add one.", "T.Accent", Kit.Btn("Import .rmskin", ImportPackage))
                : Kit.Notice(Glyph.Search, "Nothing matches", _filter == "fav" ? "Star skins to add them to your favourites." : _filter == "recent" ? "Skins you activate from Glasswave will appear here." : "Try a different search.", "T.Accent");
            empty.Margin = new Thickness(6, 0, 6, 16);
            empty.Width = double.NaN;
            _library.Children.Add(empty);
            return;
        }

        string? lastCategory = null;
        foreach (var s in list.OrderBy(s => _filter == "recent" ? 0 : 1).ThenBy(s => _filter == "recent" ? "" : s.Category, StringComparer.OrdinalIgnoreCase))
        {
            if (_filter != "recent" && s.Category != lastCategory)
            {
                lastCategory = s.Category;
                _library.Children.Add(Ui.Text(s.Category.ToUpperInvariant(), "Text.Label").With(t => { t.Width = 2000; t.Margin = new Thickness(8, _library.Children.Count == 0 ? 0 : 14, 0, 6); }));
            }
            _library.Children.Add(SkinCard(s));
        }
        // Group headers use a fixed huge width so the wrap panel breaks a line before each one.
        _library.SizeChanged -= OnLibrarySize;
        _library.SizeChanged += OnLibrarySize;
        OnLibrarySize(null, null);
    }

    private void OnLibrarySize(object? sender, SizeChangedEventArgs? e)
    {
        if (_library is null) return;
        var width = _library.ActualWidth;
        foreach (var t in _library.Children.OfType<TextBlock>()) t.Width = Math.Max(100, width - 16);
        if (width <= 0) return;
        // Cards share the row evenly (min 260 px each) instead of leaving a gap on the right.
        const double minCard = 260, gap = 12;
        var cols = Math.Max(1, (int)Math.Floor(width / (minCard + gap)));
        var cardWidth = Math.Floor(width / cols - gap);
        foreach (var c in _library.Children.OfType<Border>().Where(b => b.Tag as string == "skin-card")) c.Width = cardWidth;
    }

    private Border SkinCard(RmSkin s)
    {
        var prefs = Hub.Settings.Rainmeter;
        var fav = prefs.Favourites.Contains(s.Config);
        var card = new Border { Tag = "skin-card", Width = 300, Margin = new Thickness(6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(s.Active ? 1.5 : 1), Padding = new Thickness(0) };
        card.SetResourceReference(Border.BackgroundProperty, "T.Card");
        card.SetResourceReference(Border.BorderBrushProperty, s.Active ? "T.Accent" : "T.Stroke");
        var sp = new StackPanel();

        // preview
        var prev = new Grid { Height = 110 };
        prev.SetResourceReference(Panel.BackgroundProperty, "T.Input");
        var hasPreview = false;
        if (s.Preview is not null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 480; bmp.UriSource = new Uri(s.Preview); bmp.EndInit(); bmp.Freeze();
                prev.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
                hasPreview = true;
            }
            catch { }
        }
        if (!hasPreview)
        {
            var ph = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            ph.Children.Add(Ui.Icon(Glyph.Diagnostic, 24, "T.Text3"));
            ph.Children.Add(Ui.Text(s.Variants.Count == 1 ? s.Variants[0] : $"{s.Variants.Count} variants", "Text.Caption", 11, null, "T.Text3").With(t => { t.Margin = new Thickness(0, 6, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center; }));
            prev.Children.Add(ph);
        }
        prev.Clip = new RectangleGeometry(new Rect(0, 0, 300, 110), 15, 15);
        prev.SizeChanged += (_, e) => prev.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, 110), 15, 15);
        var star = Ui.IconButton(fav ? Glyph.StarFill : Glyph.Star, fav ? "Remove from favourites" : "Add to favourites", () =>
        {
            if (!prefs.Favourites.Remove(s.Config)) prefs.Favourites.Add(s.Config);
            Hub.Store.Save();
            RenderLibrary();
        }, 30).With(b => { b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(0, 8, 8, 0); if (fav) b.SetResourceReference(Control.ForegroundProperty, "T.Accent"); });
        prev.Children.Add(star);
        sp.Children.Add(prev);

        // text
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        var head = Ui.Grid("*,auto");
        var titles = new StackPanel();
        titles.Children.Add(Ui.Text(s.DisplayName, "Text.Body", 14, FontWeights.SemiBold).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
        titles.Children.Add(Ui.Text(string.Join(" · ", new[] { s.Author.Length > 0 ? "by " + s.Author : "", s.Version.Length > 0 ? "v" + s.Version : "", s.Config }.Where(x => x.Length > 0)), "Text.Caption", 11)
            .With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }));
        head.Children.Add(titles);
        head.Children.Add(Kit.Switch(s.Active, v => Toggle(s, v), (s.Active ? "Deactivate " : "Activate ") + s.DisplayName).At(0, 1).With(x => x.Margin = new Thickness(10, 0, 0, 0)));
        body.Children.Add(head);
        if (s.Information.Length > 0)
            body.Children.Add(Ui.Text(s.Information, "Text.Caption", 11.5).With(t => { t.Margin = new Thickness(0, 8, 0, 0); t.MaxHeight = 32; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (s.Active) chips.Children.Add(Kit.Chip(s.Variants.Count > 1 ? s.ActiveVariant : "Active", "T.Good").With(c => c.Margin = new Thickness(0, 0, 6, 4)));
        if (s.UsesPlugins) chips.Children.Add(Kit.Chip("Third-party plugin", "T.Warn", Glyph.Warning).With(c => { c.Margin = new Thickness(0, 0, 6, 4); c.ToolTip = $"Loads {string.Join(", ", s.ThirdPartyPlugins)}, which did not come with Rainmeter. Plugins are native code that run inside Rainmeter — only use skins from authors you trust."; }));
        if (s.RunsCommands) chips.Children.Add(Kit.Chip("Runs programs", "T.Warn", Glyph.Warning).With(c => { c.Margin = new Thickness(0, 0, 6, 4); c.ToolTip = "This skin can launch: " + string.Join(", ", s.Launches.Take(4)) + (s.Launches.Count > 4 ? $" and {s.Launches.Count - 4} more" : ""); }));
        if (s.LoadError is not null) chips.Children.Add(Kit.Chip("Read error", "T.Danger", Glyph.Error).With(c => { c.Margin = new Thickness(0, 0, 6, 4); c.ToolTip = s.LoadError; }));
        if (chips.Children.Count > 0) body.Children.Add(chips);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 4, 0, -6) };
        tools.Children.Add(Ui.IconButton(Glyph.Refresh, "Refresh skin", () => R.Refresh(s), 30).With(b => b.IsEnabled = s.Active));
        tools.Children.Add(Ui.IconButton(Glyph.Monitor, "Position and layering", () => PositionDialog(s), 30).With(b => b.IsEnabled = s.Active));
        tools.Children.Add(Ui.IconButton(Glyph.Folder, "Open skin folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{s.Folder}\"") { UseShellExecute = true }), 30));
        tools.Children.Add(Ui.IconButton(Glyph.Settings, "Open in Rainmeter Manage", () => R.Manage(s), 30));
        if (s.Variants.Count > 1)
        {
            var variants = Ui.IconButton(Glyph.List, "Choose variant", () => { }, 30);
            variants.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                foreach (var v in s.Variants)
                {
                    var mi = new MenuItem { Header = v, IsChecked = s.Active && v == s.ActiveVariant };
                    mi.Click += (_, _) => { Remember(s); R.Activate(s, v); Later(() => { R.Scan(); RenderLibrary(); }); };
                    menu.Items.Add(mi);
                }
                menu.PlacementTarget = variants; menu.IsOpen = true;
            };
            tools.Children.Add(variants);
        }
        body.Children.Add(tools);
        sp.Children.Add(body);
        card.Child = sp;
        return card;
    }

    private void Remember(RmSkin s)
    {
        var recent = Hub.Settings.Rainmeter.Recent;
        recent.Remove(s.Config);
        recent.Insert(0, s.Config);
        if (recent.Count > 12) recent.RemoveRange(12, recent.Count - 12);
        Hub.Store.Save();
    }

    private void Toggle(RmSkin s, bool on)
    {
        if (on && (s.UsesPlugins || s.RunsCommands) && !Hub.Settings.Rainmeter.Favourites.Contains(s.Config) && !Hub.Settings.Rainmeter.Recent.Contains(s.Config))
        {
            var plugins = $"loads third-party plugin code ({string.Join(", ", s.ThirdPartyPlugins)})";
            var launches = $"can launch {string.Join(", ", s.Launches.Take(3))}{(s.Launches.Count > 3 ? " and more" : "")}";
            var what = s.UsesPlugins && s.RunsCommands ? $"{plugins} and {launches}" : s.UsesPlugins ? plugins : launches;
            if (!Dialogs.Confirm("Activate this skin?", $"“{s.DisplayName}” {what}. That is common for advanced skins, but only activate skins from sources you trust.", "Activate"))
            { Later(RenderLibrary, 10); return; }
        }
        if (!RainmeterService.IsRunning) R.Start();
        if (on) { Remember(s); R.Activate(s); } else R.Deactivate(s);
        Later(() =>
        {
            R.Scan();
            var now = R.Skins.FirstOrDefault(x => x.Config == s.Config);
            if (on && now is { Active: false } && RainmeterService.IsRunning)
                Dialogs.Message("The skin did not load", $"Rainmeter did not activate “{s.DisplayName}”. The skin may have an error or need a plugin that isn't installed. Open it in Rainmeter's Manage window or check Rainmeter's log (Manage › About › Log) for details.");
            RenderLibrary();
        }, on && !RainmeterService.IsRunning ? 3000 : 1200);
    }

    private void PositionDialog(RmSkin s)
    {
        // Start from where the skin really is; Rainmeter.ini may hold formulas rather than numbers.
        var live = R.LiveBounds(s);
        var curX = live?.Left ?? s.X;
        var curY = live?.Top ?? s.Y;
        var current = curX is not null && curY is not null ? $"{curX},{curY}" : "";
        var x = Dialogs.Prompt("Move skin", $"Position for “{s.DisplayName}” as X,Y in pixels from the top-left of the main screen. Leave it unchanged to keep the skin where it is.", current, "Next");
        if (x is null) return;
        var parts = x.Split(',', ' ', ';').Where(p => p.Length > 0).ToArray();
        if (x.Trim() != current && parts.Length == 2 && int.TryParse(parts[0], out var px) && int.TryParse(parts[1], out var py)) R.Move(s, px, py);
        var layer = Dialogs.Choose("Layering", "Where should the skin sit?", new[] { "On desktop", "Bottom", "Normal", "On top", "Always on top" }, s.AlwaysOnTop + 2);
        if (layer >= 0) R.ZPos(s, layer - 2);
        var alpha = Dialogs.Choose("Transparency", "Skin opacity. Rainmeter applies this until the skin is refreshed or Rainmeter restarts; to keep it permanently, set Transparency in Rainmeter's Manage window.",new[] { "100%", "85%", "70%", "55%", "40%" }, (int)Math.Round((255 - s.Alpha) / 255.0 / 0.15));
        if (alpha >= 0) R.SetAlpha(s, (int)Math.Round(255 * (1 - alpha * 0.15)));
        Later(() => { R.Scan(); RenderLibrary(); });
    }

    private void ImportPackage()
    {
        var dlg = new OpenFileDialog { Title = "Import a Rainmeter skin package", Filter = "Rainmeter skin package|*.rmskin" };
        if (dlg.ShowDialog() != true) return;
        var pkg = RainmeterService.Inspect(dlg.FileName);
        if (!pkg.Valid) { Dialogs.Message("Can't import this file", pkg.Error ?? "Unknown problem."); return; }
        var detail = $"Package: {(pkg.Name.Length > 0 ? pkg.Name : Path.GetFileName(pkg.Path))} {pkg.Version}\nAuthor: {(pkg.Author.Length > 0 ? pkg.Author : "not stated")}\n" +
                     $"Skins: {(pkg.Skins.Count > 0 ? string.Join(", ", pkg.Skins) : "none")}\n" +
                     (pkg.Layouts.Count > 0 ? $"Layouts: {string.Join(", ", pkg.Layouts)}\n" : "") +
                     (pkg.Plugins.Count > 0 ? $"Plugins (native DLLs): {string.Join(", ", pkg.Plugins)}\n" : "") +
                     (pkg.MinimumRainmeter.Length > 0 ? $"Needs Rainmeter {pkg.MinimumRainmeter} or newer\n" : "") +
                     (pkg.HasFooter ? "" : "\nNote: the package has no Rainmeter packager signature footer; Rainmeter's installer may refuse it.");
        var warning = pkg.Plugins.Count > 0
            ? "This package includes plugins — compiled code that runs inside Rainmeter with your permissions. Install it only if you trust where it came from."
            : "Rainmeter's own Skin Installer will open next. It shows the same details and backs up any skin it replaces.";
        if (!Dialogs.Confirm("Install this package?", warning, pkg.Plugins.Count > 0 ? "I trust it — continue" : "Continue", "Cancel", detail)) return;
        if (!R.Install(pkg.Path)) Dialogs.Message("Skin Installer not found", "SkinInstaller.exe was not found beside Rainmeter.exe. Double-click the .rmskin file in Explorer instead.");
        else Later(() => { R.Scan(); RenderLibrary(); }, 6000);
    }

    protected override void OnEnter()
    {
        // Reflect changes made in Rainmeter itself (its own Manage window, tray menu) while this page is open.
        if (!R.Installed || !File.Exists(R.SettingsPath)) return;
        try
        {
            _iniWatcher = new FileSystemWatcher(Path.GetDirectoryName(R.SettingsPath)!, "Rainmeter.ini") { NotifyFilter = NotifyFilters.LastWrite, EnableRaisingEvents = true };
            var debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            debounce.Tick += (_, _) => { debounce.Stop(); R.Scan(); RenderLibrary(); };
            _iniWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(() => { debounce.Stop(); debounce.Start(); });
        }
        catch { }
    }

    protected override void OnLeave() { _iniWatcher?.Dispose(); _iniWatcher = null; }
}

// =================================================================================================
public sealed class TweaksPage : PageBase
{
    public override string Title => "Windows Tweaks";
    public override string Subtitle => "Bring Windows itself closer to your Glasswave look — using only per-user settings Windows supports. Every change is backed up and can be reverted.";

    protected override void Build()
    {
        Children.Add(Kit.Notice(Glyph.Shield, "What is and isn't possible",
            "Windows has no supported way to reskin every part of the system. Glasswave changes only documented, per-user appearance settings: no administrator rights, no patched system files, no code injected into Explorer.", "T.Accent"));

        // ---- one-click coordination ----
        var accent = Hub.Theme.Accent;
        var sw = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(accent), Margin = new Thickness(0, 0, 10, 0) };
        Children.Add(Kit.Section("Match Windows to Glasswave", null,
            Kit.Row("Apply the Glasswave look", $"Sets Windows to {(Hub.Theme.IsDark ? "dark" : "light")} mode, turns on transparency, and uses your Glasswave accent for Windows. You'll be asked to confirm.",
                Kit.Btn("Apply", ApplyLook, Glyph.Personalize, true), false),
            Kit.Row("Use Glasswave accent for Windows", "Title bars, Start, taskbar highlights and Settings use this colour.",
                Kit.Buttons(sw, Kit.Btn("Set accent", () =>
                {
                    if (!Dialogs.Confirm("Change the Windows accent colour?", $"Windows will use {ColorMath.Hex(accent)} as its accent. Your current accent ({ColorMath.Hex(Tweaks.CurrentSystemAccent())}) is backed up.", "Change")) return;
                    Tweaks.SetSystemAccent(ColorMath.ForMode(accent, false));
                    Rebuild();
                })))));

        foreach (var tier in new[] { TweakTier.Supported, TweakTier.Tweak })
        {
            var rows = new List<UIElement>();
            foreach (var t in Tweaks.All.Where(t => t.Tier == tier))
            {
                int current;
                try { current = t.Read(); } catch { current = 0; }
                UIElement control = t.Options.Length == 2 && t.Options[0] == "Off"
                    ? Kit.Switch(current == 1, v => Change(t, v ? 1 : 0), t.Name)
                    : Kit.Segmented(t.Options, Math.Clamp(current, 0, t.Options.Length - 1), i => Change(t, i), t.Name);
                rows.Add(Kit.Row(t.Name, t.Description + (t.SideEffects.Length > 0 ? "  Effect: " + t.SideEffects : ""), control, rows.Count > 0));
            }
            Children.Add(Kit.Section(tier == TweakTier.Supported ? "Supported settings" : "Taskbar tweaks",
                tier == TweakTier.Supported ? "The same switches as the Settings app, gathered in one place." : "Per-user Explorer options that Windows reads on the fly. Safe and reversible, but not exposed by every Windows version.", rows.ToArray()));
        }

        var backup = Tweaks.LoadBackup();
        var changed = backup.Values.Count + (backup.AccentColor is null ? 0 : 1) + (backup.Wallpaper is null ? 0 : 1);
        Children.Add(Kit.Section("Backup & revert", null,
            Kit.Row("Revert all Windows changes", changed == 0 ? "Glasswave hasn't changed any Windows settings yet." : $"Restores {changed} setting(s) to how they were on {backup.Taken:d MMM yyyy, HH:mm}.",
                Kit.Btn("Revert all", () =>
                {
                    if (!Dialogs.Confirm("Revert Windows settings?", "Every Windows setting Glasswave changed (including accent colour and wallpaper) goes back to its original value.", "Revert")) return;
                    var n = Tweaks.RevertAll();
                    Dialogs.Message("Settings restored", $"{n} setting(s) were restored.");
                    Rebuild();
                }, Glyph.Undo).With(b => b.IsEnabled = changed > 0), false),
            Kit.Row("Backup file", Tweaks.BackupFile, Kit.Btn("Show", () => { if (File.Exists(Tweaks.BackupFile)) Process.Start("explorer.exe", $"/select,\"{Tweaks.BackupFile}\""); }))));

        var shortcuts = new WrapPanel();
        foreach (var (label, uri) in new[] { ("Colours", "ms-settings:colors"), ("Background", "ms-settings:personalization-background"), ("Themes", "ms-settings:themes"), ("Taskbar", "ms-settings:taskbar"),
                     ("Start", "ms-settings:personalization-start"), ("Display", "ms-settings:display"), ("Night light", "ms-settings:nightlight"), ("Visual effects", "ms-settings:easeofaccess-visualeffects") })
            shortcuts.Children.Add(Kit.Btn(label, () => Island.IslandWindow.OpenUri(uri), Glyph.Launch).With(b => b.Margin = new Thickness(0, 0, 8, 8)));
        Children.Add(Kit.Section("Windows Settings shortcuts", null, shortcuts));

        var third = new List<UIElement>();
        foreach (var tool in ThirdParty.Tools)
            third.Add(Kit.Row(tool.Name, tool.What + " " + tool.Note, Kit.Btn("Website", () => ThirdParty.Open(tool.Url), Glyph.Globe), third.Count > 0));
        Children.Add(Kit.Section("Optional third-party tools", "For surfaces no supported setting can reach — taskbar blur, Mica for every app, Explorer styling. Glasswave never installs these; links open in your browser.", third.ToArray()));

        Children.Add(Kit.Section("Deliberately not included", null,
            Kit.Row("Explorer / DWM code injection", "Hooking or injecting DLLs into Explorer, DWM or other processes can crash the shell, break with updates and is flagged by security software.", Kit.Chip("Excluded", "T.Danger"), false),
            Kit.Row("Patching system files", "Replacing uxtheme, msstyles signatures or system binaries weakens Windows security and is undone by updates.", Kit.Chip("Excluded", "T.Danger")),
            Kit.Row("Disabling protections", "Nothing in Glasswave turns off Defender, UAC, SmartScreen or driver signing.", Kit.Chip("Excluded", "T.Danger"))));
    }

    private void Change(Tweak t, int value)
    {
        if (t.Disruptive && !Dialogs.Confirm($"Change “{t.Name}”?", t.Description + "\n\n" + t.SideEffects, "Change")) { Rebuild(); return; }
        try { Tweaks.Apply(t, value); }
        catch (Exception ex) { Dialogs.Message("Couldn't change setting", ex.Message); Rebuild(); }
    }

    private void ApplyLook()
    {
        var dark = Hub.Theme.IsDark;
        var accent = ColorMath.ForMode(Hub.Theme.Accent, false);
        if (!Dialogs.Confirm("Apply the Glasswave look to Windows?", "These Windows settings will change. Every original value is backed up and “Revert all” restores them.", "Apply",
                detail: $"• App mode → {(dark ? "Dark" : "Light")}\n• Windows mode → {(dark ? "Dark" : "Light")}\n• Transparency effects → On\n• Accent colour → {ColorMath.Hex(accent)}\n• Accent on title bars → On")) return;
        foreach (var (id, v) in new[] { ("apps-theme", dark ? 0 : 1), ("system-theme", dark ? 0 : 1), ("transparency", 1), ("accent-titlebars", 1) })
            Tweaks.Apply(Tweaks.All.First(t => t.Id == id), v);
        Tweaks.SetSystemAccent(accent);
        Rebuild();
    }
}
