using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Glasswave.Core;
using Glasswave.Design;
using Glasswave.Services;
using Glasswave.WindowsIntegration;
using Microsoft.Win32;

namespace Glasswave.Shell.Pages;

// =================================================================================================
public sealed class ProfilesPage : PageBase
{
    public override string Title => "Profiles";
    public override string Subtitle => "A profile is a complete desktop: theme, Dynamic Island, widget layout and motion. Switch manually, or let simple rules switch for you.";

    protected override void Build()
    {
        var gallery = Kit.Gallery();
        foreach (var p in Hub.Profiles.All) gallery.Children.Add(Card(p));
        Children.Add(Kit.Section("Your profiles", null, gallery,
            Kit.Buttons(
                Kit.Btn("Save current desktop as profile…", () =>
                {
                    if (Dialogs.Prompt("New profile", "Name for a profile that captures your current theme, island and widgets.", "My desktop") is { Length: > 0 } n) { Hub.Profiles.SaveCurrentAs(n); Rebuild(); }
                }, Glyph.Add, true),
                Kit.Btn("Restore previous desktop", () => { Hub.Profiles.RestorePrevious(); Rebuild(); }, Glyph.Undo).With(b => b.IsEnabled = Hub.Profiles.CanRestorePrevious),
                Kit.Btn("Export…", ExportProfile, Glyph.Export),
                Kit.Btn("Import…", ImportProfile, Glyph.Import)).With(b => b.Margin = new Thickness(0, 4, 0, 10))));

        // ---- automation ----
        var a = Hub.Settings.Automation;
        var names = Hub.Profiles.All.Select(p => p.Name).ToList();
        var ids = Hub.Profiles.All.Select(p => p.Id).ToList();
        var body = new StackPanel { IsEnabled = a.Enabled, Opacity = a.Enabled ? 1 : 0.5 };

        body.Children.Add(Kit.ToggleRow("When a fullscreen app or game is running", "Switch to the chosen profile, and back again when it closes.", a.FullscreenRule, v => { a.FullscreenRule = v; Hub.Store.Save(); }, false, Glyph.Game));
        body.Children.Add(Kit.Row("Fullscreen profile", null, Kit.Combo(names, Math.Max(0, ids.IndexOf(a.FullscreenProfileId)), i => { a.FullscreenProfileId = ids[i]; Hub.Store.Save(); })));

        body.Children.Add(Ui.Text("BY TIME OF DAY", "Text.Label").With(t => t.Margin = new Thickness(0, 18, 0, 4)));
        foreach (var rule in a.TimeRules.ToList())
        {
            var time = new TextBox { Text = rule.Time, Width = 70, TextAlignment = TextAlignment.Center };
            time.LostFocus += (_, _) => { if (TimeSpan.TryParse(time.Text, out var ts) && ts < TimeSpan.FromDays(1)) { rule.Time = $"{ts.Hours:00}:{ts.Minutes:00}"; time.Text = rule.Time; Hub.Store.Save(); Hub.Profiles.ArmTimeRules(); } else time.Text = rule.Time; };
            body.Children.Add(Kit.Row("At", null, Kit.Buttons(time,
                Ui.Text("switch to", "Text.Caption").With(t => t.VerticalAlignment = VerticalAlignment.Center),
                Kit.Combo(names, Math.Max(0, ids.IndexOf(rule.ProfileId)), i => { rule.ProfileId = ids[i]; Hub.Store.Save(); }, 170),
                Ui.IconButton(Glyph.Delete, "Remove rule", () => { a.TimeRules.Remove(rule); Hub.Store.Save(); Hub.Profiles.ArmTimeRules(); Rebuild(); }))));
        }
        body.Children.Add(Kit.Btn("Add time rule", () => { a.TimeRules.Add(new TimeRule { Time = "22:00", ProfileId = "night" }); Hub.Store.Save(); Hub.Profiles.ArmTimeRules(); Rebuild(); }, Glyph.Add).With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 6, 0, 0); }));

        body.Children.Add(Ui.Text("WHEN AN APP IS IN FRONT", "Text.Label").With(t => t.Margin = new Thickness(0, 18, 0, 4)));
        foreach (var rule in a.AppRules.ToList())
        {
            var proc = new TextBox { Text = rule.ProcessName, Width = 150, ToolTip = "Process name, e.g. code or photoshop" };
            proc.LostFocus += (_, _) => { rule.ProcessName = proc.Text.Trim(); Hub.Store.Save(); };
            body.Children.Add(Kit.Row("App", null, Kit.Buttons(proc,
                Ui.Text("uses", "Text.Caption").With(t => t.VerticalAlignment = VerticalAlignment.Center),
                Kit.Combo(names, Math.Max(0, ids.IndexOf(rule.ProfileId)), i => { rule.ProfileId = ids[i]; Hub.Store.Save(); }, 170),
                Ui.IconButton(Glyph.Delete, "Remove rule", () => { a.AppRules.Remove(rule); Hub.Store.Save(); Rebuild(); }))));
        }
        body.Children.Add(Kit.Btn("Add app rule", () =>
        {
            a.AppRules.Add(new AppRule { ProcessName = Hub.Foreground.ProcessName is { Length: > 0 } pn && pn != "Glasswave" ? pn : "", ProfileId = "productivity" });
            Hub.Store.Save(); Rebuild();
        }, Glyph.Add).With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 6, 0, 0); }));

        Children.Add(Kit.Section("Automation", "Off by default. Rules only read which window is in front and the time; nothing is logged or sent anywhere.",
            Kit.ToggleRow("Switch profiles automatically", null, a.Enabled, v =>
            {
                a.Enabled = v; Hub.Store.Save(); Hub.Profiles.ArmTimeRules();
                body.IsEnabled = v; body.Opacity = v ? 1 : 0.5;
            }, false),
            body));
    }

    private Border Card(Profile p)
    {
        var active = p.Id == Hub.Settings.ActiveProfileId;
        var card = new Border { Width = 252, Margin = new Thickness(6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(active ? 1.5 : 1), Padding = new Thickness(18, 16, 18, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "T.Card");
        card.SetResourceReference(Border.BorderBrushProperty, active ? "T.Accent" : "T.Stroke");
        var sp = new StackPanel();
        var head = Ui.Grid("auto,*,auto");
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Child = Ui.Icon(p.Glyph, 17, "T.Accent") };
        badge.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
        head.Children.Add(badge);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(p.Name, "Text.Body", 14.5, FontWeights.SemiBold));
        titles.Children.Add(Ui.Text(p.BuiltIn ? "Built-in" : $"{p.Widgets.Count} widget{(p.Widgets.Count == 1 ? "" : "s")}", "Text.Caption", 11.5));
        head.Children.Add(titles.At(0, 1));
        if (active) head.Children.Add(Kit.Chip("Active", "T.Good").At(0, 2));
        sp.Children.Add(head);
        var theme = Hub.Theme.Find(p.ThemeId)?.Name ?? "Theme missing";
        sp.Children.Add(Ui.Text(p.Description.Length > 0 ? p.Description : $"{theme} · {(p.Island.Enabled ? "island on" : "island off")}{(p.ReducedMotion ? " · reduced motion" : "")}", "Text.Caption", 12)
            .With(t => { t.Margin = new Thickness(0, 12, 0, 0); t.Height = 50; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
        sp.Children.Add(Ui.Text("THEME · " + theme.ToUpperInvariant(), "Text.Label", 9.5).With(t => t.Margin = new Thickness(0, 6, 0, 10)));
        var buttons = Ui.Grid("*,auto");
        buttons.Children.Add(Kit.Btn(active ? "Re-apply" : "Apply", () => { Hub.Profiles.Apply(p); Rebuild(); }, null, !active).With(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        var more = Ui.IconButton(Glyph.More, "More actions", () => { });
        more.Click += (_, _) =>
        {
            var m = new ContextMenu();
            MenuItem I(string h, Action a, bool en = true) { var i = new MenuItem { Header = h, IsEnabled = en }; i.Click += (_, _) => a(); return i; }
            m.Items.Add(I("Update with current desktop", () => { if (Dialogs.Confirm("Update profile?", $"“{p.Name}” will be replaced with your current theme, island and widget layout.", "Update")) { Hub.Profiles.UpdateFromCurrent(p); Rebuild(); } }, !p.BuiltIn));
            m.Items.Add(I("Duplicate", () => { Hub.Profiles.Duplicate(p, p.Name + " copy"); Rebuild(); }));
            m.Items.Add(I("Rename…", () => { if (Dialogs.Prompt("Rename profile", "New name.", p.Name) is { Length: > 0 } n) { p.Name = n; Hub.Store.SaveProfile(p); Rebuild(); } }, !p.BuiltIn));
            m.Items.Add(new Separator());
            m.Items.Add(I("Delete", () => { if (Dialogs.Confirm("Delete profile?", $"“{p.Name}” will be removed. Your current desktop is not changed.", "Delete")) { Hub.Profiles.Delete(p); Rebuild(); } }, !p.BuiltIn));
            m.PlacementTarget = more; m.IsOpen = true;
        };
        buttons.Children.Add(more.At(0, 1));
        sp.Children.Add(buttons);
        card.Child = sp;
        return card;
    }

    private static void ExportProfile()
    {
        var all = Hub.Profiles.All.ToList();
        var i = Dialogs.Choose("Export profile", "Choose a profile to export. Notes and tasks are not included.", all.Select(p => p.Name).ToList());
        if (i < 0) return;
        var dlg = new SaveFileDialog { FileName = all[i].Name + ".glassprofile", Filter = "Glasswave profile|*.glassprofile" };
        if (dlg.ShowDialog() != true) return;
        var p = Json.Clone(all[i]);
        p.BuiltIn = false;
        var theme = Hub.Theme.Find(p.ThemeId);
        Json.Write(dlg.FileName, new ProfileBundle { Profile = p, Theme = theme is { BuiltIn: false } ? theme : null });
    }

    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Filter = "Glasswave profile|*.glassprofile;*.json" };
        if (dlg.ShowDialog() != true) return;
        var b = Json.Read<ProfileBundle>(dlg.FileName);
        if (b?.Profile is null) { Dialogs.Message("Not a profile", "That file is not a Glasswave profile."); return; }
        if (b.Theme is not null) { var t = Hub.Theme.SaveAsNew(b.Theme, b.Theme.Name); b.Profile.ThemeId = t.Id; }
        else if (Hub.Theme.Find(b.Profile.ThemeId) is null) b.Profile.ThemeId = Presets.All[0].Id;
        var copy = Hub.Profiles.Duplicate(b.Profile, b.Profile.Name);
        Dialogs.Message("Profile imported", $"“{copy.Name}” is ready. Apply it whenever you like.");
        Rebuild();
    }

    public sealed class ProfileBundle { public Profile? Profile { get; set; } public Theme? Theme { get; set; } }
}

// =================================================================================================
public sealed class SettingsPage : PageBase
{
    public override string Title => "Settings";
    public override string Subtitle => "Start-up, shortcuts, privacy and your data.";
    private static GeneralSettings G => Hub.Settings.General;
    private static PrivacySettings P => Hub.Settings.Privacy;

    protected override void Build()
    {
        Children.Add(Kit.Section("General", null,
            Kit.ToggleRow("Start with Windows", "Adds Glasswave to your sign-in apps (per-user, no admin). Starts quietly in the tray.", Startup.Enabled, v => { try { Startup.Set(v); G.RunAtStartup = v; Hub.Store.Save(); } catch (Exception ex) { Dialogs.Message("Couldn't change start-up", ex.Message); } }, false),
            Kit.ToggleRow("Keep running when the window is closed", "The island and widgets stay on your desktop; reopen from the tray icon.", G.CloseToTray, v => { G.CloseToTray = v; Hub.Store.Save(); }),
            Kit.ToggleRow("24-hour clock", null, G.Use24Hour, v => { G.Use24Hour = v; Hub.Store.Save(); Hub.Island.Apply(); Hub.Widgets.Apply(); foreach (var w in Hub.Widgets.Windows) w.Rebuild(); }),
            Kit.ToggleRow("Pause during fullscreen apps", "Stops widget updates and system sampling while a game or fullscreen video is in front.", G.PauseInFullscreen, v => { G.PauseInFullscreen = v; Hub.Store.Save(); }),
            Kit.ToggleRow("Software rendering for this window", "Only if this window ever appears blank. Glasswave detects that automatically and switches for the session; this makes it permanent. Takes effect when the window is reopened.",
                G.ForceSoftwareRendering, v => { G.ForceSoftwareRendering = v; Hub.Store.Save(); })));

        var failures = App.Current.HotkeyFailures;
        Children.Add(Kit.Section("Keyboard shortcuts", failures.Count > 0 ? $"Already used by another app: {string.Join(", ", failures)}. Choose a different combination." : "Global shortcuts. Use Ctrl, Alt, Shift or Win plus a key, for example Ctrl+Alt+Space.",
            HotkeyRow("Open the Dynamic Island", G.HotkeyIsland, v => G.HotkeyIsland = v, false),
            HotkeyRow("Show or hide widgets", G.HotkeyWidgets, v => G.HotkeyWidgets = v),
            HotkeyRow("Open Glasswave", G.HotkeyMain, v => G.HotkeyMain = v)));

        // ---- privacy ----
        var weatherBody = new StackPanel { IsEnabled = P.NetworkAllowed, Opacity = P.NetworkAllowed ? 1 : 0.5 };
        var place = Ui.Text(P.WeatherPlace.Length > 0 ? P.WeatherPlace : "No place chosen", "Text.Caption", 12.5);
        weatherBody.Children.Add(Kit.ToggleRow("Weather widget", "Uses Open-Meteo (no account, no key). Sends only the coordinates of the place you choose, at most twice an hour while the widget is visible.", P.WeatherEnabled, v => { P.WeatherEnabled = v; Hub.Store.Save(); Hub.Weather.Invalidate(); Hub.Widgets.Apply(); }, false, Glyph.Cloud));
        weatherBody.Children.Add(Kit.Row("Place", null, Kit.Buttons(place, Kit.Btn("Choose…", async () =>
        {
            var q = Dialogs.Prompt("Weather place", "Type a city or town. This search is sent to Open-Meteo's geocoding service.", P.WeatherPlace, "Search");
            if (string.IsNullOrWhiteSpace(q)) return;
            try
            {
                var results = await Hub.Weather.SearchAsync(q);
                if (results.Count == 0) { Dialogs.Message("No match", "No places matched that search."); return; }
                var i = Dialogs.Choose("Choose a place", "Select the right match.", results.Select(r => r.ToString()).ToList());
                if (i < 0) return;
                P.WeatherPlace = results[i].Name; P.WeatherLat = results[i].Lat; P.WeatherLon = results[i].Lon; P.WeatherEnabled = true;
                Hub.Store.Save(); Hub.Weather.Invalidate(); Hub.Widgets.Apply(); Rebuild();
            }
            catch (Exception ex) { Dialogs.Message("Search failed", ex.Message); }
        }))));
        weatherBody.Children.Add(Kit.Row("Temperature unit", null, Kit.Segmented(new[] { "°C", "°F" }, P.WeatherFahrenheit ? 1 : 0, i => { P.WeatherFahrenheit = i == 1; Hub.Store.Save(); Hub.Weather.Invalidate(); Hub.Widgets.Apply(); })));
        Children.Add(Kit.Section("Privacy & network", "Glasswave has no telemetry, analytics, crash uploads, accounts or automatic update checks. This switch is the only way it can ever reach the internet.",
            Kit.ToggleRow("Allow optional internet features", "Off by default. While off, Glasswave cannot open any network connection.", P.NetworkAllowed, v =>
            {
                P.NetworkAllowed = v; Hub.Store.Save(); Hub.Weather.Invalidate(); Hub.Widgets.Apply();
                weatherBody.IsEnabled = v; weatherBody.Opacity = v ? 1 : 0.5;
            }, false, Glyph.Globe),
            weatherBody,
            Kit.Row("Updates", "Glasswave never checks for updates by itself. New versions are installed by running a newer installer.", Kit.Chip("Manual", "T.Accent"))));

        // ---- data ----
        Children.Add(Kit.Section("Your data", $"Everything is stored in {Paths.Root}" + (Paths.Portable ? " (portable mode)." : "."),
            Kit.Row("Open data folder", "Settings, themes, profiles, notes, logs and backups as plain JSON files.", Kit.Btn("Open", () => Process.Start("explorer.exe", $"\"{Paths.Root}\""), Glyph.Folder), false),
            Kit.Row("Back up everything", "One .zip with your settings, themes, profiles, notes and tasks.", Kit.Btn("Export backup…", ExportBackup, Glyph.Export)),
            Kit.Row("Restore a backup", "Replaces current settings with a backup (your current files are saved first).", Kit.Btn("Restore…", RestoreBackup, Glyph.Import)),
            Kit.Row("Reset Glasswave", "Back to first-run defaults. Notes, tasks and Windows settings are kept.", Kit.Btn("Reset…", Reset, Glyph.Refresh))));
    }

    private Grid HotkeyRow(string label, string value, Action<string> set, bool divider = true)
    {
        var box = new TextBox { Text = value, Width = 180, IsReadOnly = true, ToolTip = "Click, then press the new combination. Backspace clears it.", Cursor = Cursors.Hand };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Back or Key.Delete) { box.Text = ""; set(""); Hub.Store.Save(); App.Current.RegisterHotkeys(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab) return;
            var mods = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods.Add("Win");
            if (mods.Count == 0) return;
            var name = key.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
            var gesture = string.Join("+", mods.Append(name));
            box.Text = gesture; set(gesture); Hub.Store.Save();
            App.Current.RegisterHotkeys();
            if (App.Current.HotkeyFailures.Contains(gesture)) Dialogs.Message("Shortcut in use", $"{gesture} is already used by another app. Try another combination.");
        };
        return Kit.Row(label, null, box, divider);
    }

    private static void ExportBackup()
    {
        var dlg = new SaveFileDialog { FileName = $"Glasswave backup {DateTime.Now:yyyy-MM-dd}.zip", Filter = "Zip archive|*.zip" };
        if (dlg.ShowDialog() != true) return;
        Hub.Store.Flush();
        try
        {
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);
            foreach (var f in Directory.EnumerateFiles(Paths.Root, "*.json", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(f, Path.GetRelativePath(Paths.Root, f));
            Dialogs.Message("Backup saved", "Your Glasswave data was saved to " + dlg.FileName);
        }
        catch (Exception ex) { Dialogs.Message("Backup failed", ex.Message); }
    }

    private void RestoreBackup()
    {
        var dlg = new OpenFileDialog { Filter = "Glasswave backup|*.zip" };
        if (dlg.ShowDialog() != true) return;
        if (!Dialogs.Confirm("Restore this backup?", "Your current settings, themes and profiles will be replaced. A copy of them is saved in the backups folder first.", "Restore")) return;
        try
        {
            Hub.Store.Flush();
            var safety = Path.Combine(Paths.Backups, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var z = ZipFile.Open(safety, ZipArchiveMode.Create))
                foreach (var f in Directory.EnumerateFiles(Paths.Root, "*.json", SearchOption.AllDirectories).Where(f => !f.StartsWith(Paths.Backups)))
                    z.CreateEntryFromFile(f, Path.GetRelativePath(Paths.Root, f));
            using (var zip = ZipFile.OpenRead(dlg.FileName))
                foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    var target = Path.GetFullPath(Path.Combine(Paths.Root, e.FullName));
                    if (!target.StartsWith(Path.GetFullPath(Paths.Root), StringComparison.OrdinalIgnoreCase)) continue;   // zip-slip guard
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, true);
                }
            Hub.Store.Load();
            Hub.Theme.Load();
            Hub.Profiles.Load();
            Hub.ApplyAll();
            Owner?.Reload();
            Dialogs.Message("Backup restored", "Glasswave is now using the restored settings.");
        }
        catch (Exception ex) { Dialogs.Message("Restore failed", ex.Message); }
    }

    private void Reset()
    {
        if (!Dialogs.Confirm("Reset Glasswave?", "Settings, widget layout and island configuration return to defaults. Your themes, profiles, notes and tasks are kept. Windows settings are not touched.", "Reset")) return;
        var keepRain = Hub.Settings.Rainmeter;
        var fresh = new AppSettings { Rainmeter = keepRain };
        Json.Write(Paths.Settings, fresh);
        Hub.Store.Load();
        Hub.ApplyAll();
        Owner?.Reload();
    }
}

// =================================================================================================
public sealed class AboutPage : PageBase
{
    public override string Title => "About & Diagnostics";
    public override string Subtitle => "What Glasswave is doing, what it stores and what it costs — measured live.";
    private TextBlock? _ram, _cpu, _threads, _rm, _uptime;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private TimeSpan _lastCpu;
    private DateTime _lastAt;

    protected override void Build()
    {
        var v = typeof(App).Assembly.GetName().Version;
        var head = Ui.Grid("auto,*", "auto");
        head.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/glasswave.ico")), Width = 64, Height = 64 });
        var t = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Ui.Text("Glasswave", "Text.Title", 24));
        t.Children.Add(Ui.Text($"Version {v?.ToString(3)} · .NET {Environment.Version} · Windows build {Environment.OSVersion.Version.Build}" + (Paths.Portable ? " · portable" : ""), "Text.Caption", 12.5));
        t.Children.Add(Ui.Text("A Liquid Glass desktop for Windows. Local-first, no telemetry. Not code-signed, so Windows SmartScreen may warn on first install.", "Text.Caption", 12.5).With(x => x.Margin = new Thickness(0, 4, 0, 0)));
        head.Children.Add(t.At(0, 1));
        Children.Add(Kit.Card(head, new Thickness(22, 18, 22, 18)).With(c => c.Margin = new Thickness(0, 0, 0, 16)));

        _ram = Ui.Text("—", "Text.Body", 14, FontWeights.SemiBold); _cpu = Ui.Text("—", "Text.Body", 14, FontWeights.SemiBold);
        _threads = Ui.Text("—", "Text.Body", 14, FontWeights.SemiBold); _rm = Ui.Text("—", "Text.Body", 14, FontWeights.SemiBold);
        _uptime = Ui.Text("—", "Text.Body", 14, FontWeights.SemiBold);
        Children.Add(Kit.Section("Resource use (live)", "Measured from Windows' own process counters every 2 seconds while this page is open.",
            Kit.Row("Glasswave memory", "Private bytes (memory committed by this process) and working set (physical RAM in use right now). Closing this window frees most of the UI.", _ram, false),
            Kit.Row("Glasswave CPU", "Average over the last interval, as a share of all cores.", _cpu),
            Kit.Row("Windows", "Island, widgets and this window.", _threads),
            Kit.Row("Rainmeter (separate process)", "Rainmeter's own usage; it is not part of Glasswave.", _rm),
            Kit.Row("Start-up", "Time from launch until the island and widgets were on screen.", Ui.Text($"{App.StartupTime.TotalMilliseconds:0} ms", "Text.Body", 14, FontWeights.SemiBold)),
            Kit.Row("Running for", null, _uptime),
            Kit.Row("Rendering", RenderGuard.FellBack ? "The graphics driver did not present this window, so Glasswave switched it to software rendering for this session. Restarting the PC or updating the GPU driver usually restores hardware rendering."
                    : Hub.Settings.General.ForceSoftwareRendering ? "Software rendering is forced in Settings." : "GPU-accelerated (DirectX).",
                Kit.Chip(RenderGuard.FellBack || Hub.Settings.General.ForceSoftwareRendering ? "Software" : "Hardware", RenderGuard.FellBack ? "T.Warn" : "T.Good")),
            Kit.Row("Sensors", $"GPU: {(Hub.Monitor.GpuAvailable ? Hub.Monitor.GpuSource : "not available until a monitor is shown")}{(Hub.Monitor.GpuTempAvailable ? " (with temperature)" : "")}. CPU temperature is not shown: Windows has no reliable, admin-free source for it.", Kit.Btn("Trim memory", App.TrimMemory))));

        var stores = new[]
        {
            ("settings.json", "Theme choice, island, widget layout, preferences, automation rules."),
            ("data.json", "Your notes, tasks and focus-session history."),
            ("themes\\, profiles\\", "Themes and profiles you created."),
            ("backups\\", "Original Windows settings before Glasswave changed them."),
            ("logs\\glasswave.log", "Local diagnostic log (max ~1 MB). Never uploaded."),
        };
        var storeRows = stores.Select((s, i) => (UIElement)Kit.Row(s.Item1, s.Item2, new Border(), i > 0)).ToArray();
        Children.Add(Kit.Section("What Glasswave stores", $"All in {Paths.Root}. Nothing is stored anywhere else, except the per-user Windows settings you explicitly change.", storeRows));

        var ledger = Hub.Net.Ledger;
        var net = new StackPanel();
        net.Children.Add(Kit.Row("Network access", Hub.Settings.Privacy.NetworkAllowed ? "Allowed for the optional features you enabled." : "Off — no connections are possible.",
            Kit.Chip(Hub.Settings.Privacy.NetworkAllowed ? "On" : "Off", Hub.Settings.Privacy.NetworkAllowed ? "T.Warn" : "T.Good"), false));
        net.Children.Add(Kit.Row("Requests this session", ledger.Count == 0 ? "None." : "Each request Glasswave made, newest first.", Ui.Text(ledger.Count.ToString(), "Text.Body", 14, FontWeights.SemiBold)));
        foreach (var e in ledger.Reverse().Take(12))
            net.Children.Add(Kit.Row($"{e.When:HH:mm:ss}  {e.Host}", e.Purpose, Kit.Chip(e.Result, e.Result == "OK" ? "T.Good" : "T.Warn")));
        Children.Add(Kit.Section("Network ledger", "There is no telemetry, analytics or crash reporting in Glasswave. Opening a link sends you to your own browser.", net));

        var log = new TextBox { Text = Log.Tail(40), IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Height = 220, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
        Children.Add(Kit.Section("Diagnostics log", null, log,
            Kit.Buttons(Kit.Btn("Refresh", () => log.Text = Log.Tail(40), Glyph.Refresh), Kit.Btn("Open log folder", () => Process.Start("explorer.exe", $"\"{Paths.Logs}\""), Glyph.Folder),
                Kit.Btn("Copy system summary", CopySummary, Glyph.Copy)).With(b => b.Margin = new Thickness(0, 12, 0, 8))));
    }

    private static void CopySummary()
    {
        var m = Monitors.All();
        var s = $"Glasswave {typeof(App).Assembly.GetName().Version}\nWindows {Environment.OSVersion.Version}\n.NET {Environment.Version}\nDisplays: {string.Join("; ", m.Select(x => $"{x.Device} {x.Bounds.Width}x{x.Bounds.Height} @{x.Scale * 100:0}%"))}\n" +
                $"Rainmeter: {(Hub.Rainmeter.Installed ? Hub.Rainmeter.Version : "not installed")}\nGPU source: {Hub.Monitor.GpuSource}\nWidgets: {Hub.Settings.Widgets.Count}\nTheme: {Hub.Theme.Current.Name}";
        try { Clipboard.SetText(s); } catch { }
    }

    protected override void OnEnter()
    {
        _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
        _lastAt = DateTime.UtcNow;
        _timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        Sample();
    }

    protected override void OnLeave() => _timer?.Stop();

    private void Sample()
    {
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        var now = DateTime.UtcNow;
        var cpu = (p.TotalProcessorTime - _lastCpu).TotalMilliseconds / Math.Max(1, (now - _lastAt).TotalMilliseconds) / Environment.ProcessorCount * 100;
        _lastCpu = p.TotalProcessorTime; _lastAt = now;
        if (_ram is null) return;
        _ram.Text = $"{p.PrivateMemorySize64 / 1048576.0:0} MB private · {p.WorkingSet64 / 1048576.0:0} MB working set";
        _cpu!.Text = $"{cpu:0.0}%";
        _threads!.Text = $"{Application.Current.Windows.Count} windows · {p.Threads.Count} threads · {p.HandleCount} handles";
        _uptime!.Text = Fmt.Uptime(now - p.StartTime.ToUniversalTime());
        var rm = Process.GetProcessesByName("Rainmeter");
        _rm!.Text = rm.Length == 0 ? "Not running" : $"{rm.Sum(x => { try { return x.PrivateMemorySize64; } catch { return 0; } }) / 1048576.0:0} MB";
        foreach (var x in rm) x.Dispose();
    }
}
