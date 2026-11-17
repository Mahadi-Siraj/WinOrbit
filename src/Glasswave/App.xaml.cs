using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Glasswave.Core;
using Glasswave.Design;
using Glasswave.Island;
using Glasswave.Rainmeter;
using Glasswave.Services;
using Glasswave.Shell;
using Glasswave.Wallpaper;
using Glasswave.Widgets;

namespace Glasswave;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    private const string MutexName = "Glasswave.SingleInstance.v1";
    private const string ShowEventName = "Glasswave.ShowMain.v1";
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _main;
    private ContextMenu? _trayMenu;
    public static TimeSpan StartupTime { get; private set; }
    public List<string> HotkeyFailures { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- headless commands used by the installer/uninstaller ----
        if (e.Args.Contains("--revert-tweaks", StringComparer.OrdinalIgnoreCase))
        {
            try { Paths.Ensure(); var n = WindowsIntegration.Tweaks.RevertAll(); Log.Info($"Reverted {n} Windows setting(s) from the command line"); } catch (Exception ex) { Log.Error("Revert", ex); }
            Shutdown();
            return;
        }

        // ---- one instance only: a second launch just opens the existing window ----
        _mutex = new Mutex(true, MutexName, out var first);
        var requestedPage = e.Args.Contains("--quit", StringComparer.OrdinalIgnoreCase) ? "quit" : PageArg(e.Args);
        if (first && requestedPage == "quit") { Shutdown(); return; }
        if (!first)
        {
            // "Glasswave.exe --page widgets" opens that page in the running instance.
            try { File.WriteAllText(ActivationFile, requestedPage ?? ""); } catch { }
            // This launch came from the user (Start menu, shortcut), so it may hand foreground rights to the running copy.
            Interop.Native.AllowSetForegroundWindow(-1);
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                string? page = null;
                try { if (File.Exists(ActivationFile)) { page = File.ReadAllText(ActivationFile).Trim(); File.Delete(ActivationFile); } } catch { }
                if (page == "quit") { Dispatcher.BeginInvoke(Quit); continue; }
                Dispatcher.BeginInvoke(() => ShowMain(string.IsNullOrEmpty(page) ? null : page));
            }
        }) { IsBackground = true, Name = "Glasswave activation" };
        listener.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI exception", ex.Exception);
            ex.Handled = true;   // keep the desktop running; the error is in the local log
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Fatal exception", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("Background task", ex.Exception); ex.SetObserved(); };

        // ---- services, in dependency order ----
        var store = new Store();
        store.Load();
        Hub.Store = store;
        Log.Info($"Glasswave {typeof(App).Assembly.GetName().Version} starting (portable={Paths.Portable})");

        Hub.Shell = new ShellHost();
        Hub.Wallpaper = new WallpaperEngine(Dispatcher);
        Hub.Theme = new ThemeEngine(store, Hub.Wallpaper);
        Hub.Monitor = new SystemMonitor(Dispatcher);
        Hub.Media = new MediaService(Dispatcher);
        Hub.Timers = new TimerService(store);
        Hub.Foreground = new ForegroundWatcher();
        Hub.Net = new NetworkGate(() => Hub.Settings.Privacy);
        Hub.Weather = new WeatherService(Hub.Net, () => Hub.Settings.Privacy);
        Hub.Rainmeter = new RainmeterService(() => Hub.Settings.Rainmeter);
        Hub.Audio = new AudioWatcher(Dispatcher);
        Hub.Lyrics = new LyricsService(Hub.Net, () => Hub.Settings.Privacy);
        Hub.Privacy = new PrivacyIndicator(Dispatcher);
        Hub.Clipboard = new ClipboardHistory(store, () => Hub.Settings.Island);
        Hub.Power = new PowerEvents();
        Hub.Ready = true;

        Hub.Theme.Load();
        Hub.Theme.Apply();                                    // resources exist before any window is created
        Hub.Wallpaper.LiveCaptureAllowed = () => Hub.Settings.LiveWallpaperCapture;
        Hub.Wallpaper.PauseLiveRefresh = () => Hub.Foreground.IsFullscreen;
        Hub.Wallpaper.Watch(Hub.Shell);
        Hub.Wallpaper.PaletteChanged += () => Hub.Theme.Apply();
        Hub.Shell.SettingChanged += area =>
        {
            if (area == "ImmersiveColorSet" && Hub.Theme.Current.Mode == ThemeMode.Auto) Dispatcher.BeginInvoke(Hub.Theme.Apply);
        };

        Hub.Island = new IslandController(Dispatcher);
        Hub.Widgets = new WidgetManager(Dispatcher);
        Hub.Profiles = new ProfileService();
        Hub.Profiles.Load();
        Hub.Media.Start();
        Hub.Audio.Start();
        Hub.Privacy.Start();
        Hub.Clipboard.Apply();
        Hub.Power.Start();

        // First launch: seed a tasteful starter layout so the product is visibly alive.
        if (store.IsFirstRun) SeedFirstRun();

        _ = StartAsync(e.Args.Contains("--startup"), requestedPage);
    }

    private async Task StartAsync(bool fromStartup, string? page)
    {
        await Hub.Wallpaper.RefreshAsync(true);   // palette + backdrops before the glass appears
        Hub.Theme.Apply();
        Hub.Island.Apply();
        Hub.Widgets.Apply();
        RegisterHotkeys();
        Hub.Shell.AddTray("Glasswave");
        Hub.Shell.TrayLeftClick += () => ShowMain(null);
        Hub.Shell.TrayRightClick += ShowTrayMenu;
        if (!fromStartup) ShowMain(page ?? (Hub.Store.IsFirstRun ? "home" : null));
        // Measured from process creation, so it includes .NET runtime start-up, not just our own code.
        using (var self = Process.GetCurrentProcess()) StartupTime = DateTime.Now - self.StartTime;
        Log.Info($"Started in {StartupTime.TotalMilliseconds:0} ms");
        // Trim the start-up working set once everything has settled.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () => { await Task.Delay(4000); TrimMemory(); });
    }

    private static string ActivationFile => Path.Combine(Path.GetTempPath(), "Glasswave.activate");

    private static string? PageArg(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : null;
    }

    public static void TrimMemory()
    {
        GC.Collect(2, GCCollectionMode.Optimized, false, true);
        try { Interop.Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1); } catch { }
    }

    private static void SeedFirstRun()
    {
        var s = Hub.Settings;
        s.General.FirstRunDone = true;
        var mon = Monitors.Primary;
        double scale = mon.Scale, margin = s.Layout.Margin * scale;
        var right = mon.Work.Right - mon.Bounds.Left - margin;
        var top = mon.Work.Top - mon.Bounds.Top + margin + 46 * scale;
        s.Widgets.Add(new WidgetInstance { Kind = "clock", Monitor = mon.Device, Width = 288, Height = 136, Variant = "stacked", X = Math.Round(right - 288 * scale), Y = Math.Round(top) });
        s.Widgets.Add(new WidgetInstance { Kind = "media", Monitor = mon.Device, Width = 336, Height = 128, X = Math.Round(right - 336 * scale), Y = Math.Round(top + (136 + 16) * scale) });
        s.Widgets.Add(new WidgetInstance { Kind = "system", Monitor = mon.Device, Width = 288, Height = 250, X = Math.Round(right - 288 * scale), Y = Math.Round(top + (136 + 128 + 32) * scale) });
        Hub.Store.Save();
    }

    public void RegisterHotkeys()
    {
        var g = Hub.Settings.General;
        Hub.Shell.ClearHotkeys();
        HotkeyFailures.Clear();
        void Reg(string gesture, Action a) { if (!string.IsNullOrWhiteSpace(gesture) && !Hub.Shell.RegisterHotkey(gesture, a)) HotkeyFailures.Add(gesture); }
        // If the island's default shortcut is taken by another app, fall back to a free one instead of leaving it dead.
        if (!string.IsNullOrWhiteSpace(g.HotkeyIsland) && !Hub.Shell.RegisterHotkey(g.HotkeyIsland, () => Hub.Island.Toggle(true)))
        {
            var fallback = new[] { "Ctrl+Alt+I", "Ctrl+Shift+Space", "Win+Alt+I" }.FirstOrDefault(f => f != g.HotkeyIsland && Hub.Shell.RegisterHotkey(f, () => Hub.Island.Toggle(true)));
            if (fallback is not null && g.HotkeyIsland == "Ctrl+Alt+Space")
            {
                Log.Info($"Ctrl+Alt+Space is used by another app; island shortcut moved to {fallback}");
                g.HotkeyIsland = fallback;
                Hub.Store.Save();
            }
            else HotkeyFailures.Add(fallback is null ? g.HotkeyIsland : g.HotkeyIsland);
        }
        // Launcher: Alt+Space by default (the PowerToys Run / Flow Launcher convention). If something else owns it
        // (e.g. Copilot on some PCs), take the first free alternative and remember it.
        if (!string.IsNullOrWhiteSpace(g.HotkeyLauncher) && !Hub.Shell.RegisterHotkey(g.HotkeyLauncher, ToggleLauncher))
        {
            var fallback = new[] { "Win+Alt+Space", "Ctrl+Alt+K", "Ctrl+Shift+K" }.FirstOrDefault(f => f != g.HotkeyLauncher && Hub.Shell.RegisterHotkey(f, ToggleLauncher));
            if (fallback is not null && g.HotkeyLauncher == "Alt+Space")
            {
                Log.Info($"Alt+Space is used by another app; launcher shortcut moved to {fallback}");
                g.HotkeyLauncher = fallback;
                Hub.Store.Save();
            }
            else HotkeyFailures.Add(g.HotkeyLauncher);
        }
        Reg(g.HotkeyWidgets, () => Hub.Widgets.ToggleAll());
        Reg(g.HotkeyMain, () => { if (_main is { IsVisible: true, IsActive: true }) _main.Hide(); else ShowMain(null); });
        if (HotkeyFailures.Count > 0) Log.Warn("Hotkeys already in use by another app: " + string.Join(", ", HotkeyFailures));
    }

    private Launcher.LauncherWindow? _launcher;

    /// <summary>Open the launcher, or close it if it's already open.</summary>
    public void ToggleLauncher()
    {
        _launcher ??= new Launcher.LauncherWindow();
        if (_launcher.IsVisible) _launcher.Dismiss(); else _launcher.Open();
    }

    public void ShowMain(string? page)
    {
        if (_main is null)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => { _main = null; App.TrimMemory(); };
        }
        if (page is not null) _main.Navigate(page);
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        Interop.Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(_main).Handle);
    }

    private void ShowTrayMenu()
    {
        _trayMenu ??= new ContextMenu();
        var m = _trayMenu;
        m.Items.Clear();
        MenuItem Item(string header, Action a, bool check = false) { var i = new MenuItem { Header = header, IsChecked = check }; i.Click += (_, _) => a(); return i; }
        m.Items.Add(Item("Open Glasswave", () => ShowMain(null)));
        m.Items.Add(Item($"Search…   {Hub.Settings.General.HotkeyLauncher}", ToggleLauncher));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Dynamic Island", () => { Hub.Settings.Island.Enabled = !Hub.Settings.Island.Enabled; Hub.Store.Save(); Hub.Island.Apply(); }, Hub.Settings.Island.Enabled));
        m.Items.Add(Item("Desktop widgets", () => Hub.Widgets.ToggleAll(), Hub.Settings.Layout.WidgetsVisible));
        var profiles = new MenuItem { Header = "Profile" };
        foreach (var p in Hub.Profiles.All) profiles.Items.Add(Item(p.Name, () => Hub.Profiles.Apply(p), p.Id == Hub.Settings.ActiveProfileId));
        m.Items.Add(profiles);
        var themes = new MenuItem { Header = "Theme" };
        foreach (var t in Hub.Theme.All) themes.Items.Add(Item(t.Name, () => Hub.Theme.Use(t.Id), t.Id == Hub.Settings.ActiveThemeId));
        m.Items.Add(themes);
        m.Items.Add(new Separator());
        m.Items.Add(Item("Quit Glasswave", Quit));
        m.Placement = PlacementMode.MousePoint;
        m.IsOpen = true;
        // A tray menu must own the foreground or it will not close when clicking elsewhere.
        if (PresentationSource.FromVisual(m) is System.Windows.Interop.HwndSource src) Interop.Native.SetForegroundWindow(src.Handle);
    }

    public void Quit()
    {
        try
        {
            Hub.Store.Flush();
            Hub.Island?.CloseAll();
            Hub.Widgets?.CloseAll();
            Hub.Monitor?.Dispose();
            Hub.Foreground?.Dispose();
            Hub.Island?.Downloads.Dispose();
            Hub.Shell?.Dispose();
        }
        catch (Exception ex) { Log.Error("Shutdown", ex); }
        Log.Info("Glasswave stopped");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Hub.Store?.Flush(); } catch { }
        try { Hub.Shell?.RemoveTray(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
