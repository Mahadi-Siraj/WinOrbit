using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;
using WinOrbit.WindowsIntegration;
using Windows.Devices.Radios;

namespace WinOrbit.Island;

/// <summary>
/// Optional download activity: watches the Downloads folder for browsers' temporary files.
/// Opt-in, local only, and it reads nothing but file names and sizes.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    private static readonly string[] TempExtensions = { ".crdownload", ".part", ".download", ".opdownload", ".partial" };
    private FileSystemWatcher? _watcher;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, DateTime> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _stale;

    public (string Name, long Bytes)? Active { get; private set; }
    public string? JustCompleted { get; private set; }
    /// <summary>The finished file's full path, alongside <see cref="JustCompleted"/>.</summary>
    public string? JustCompletedPath { get; private set; }
    public event Action? Changed;

    public DownloadWatcher(Dispatcher ui)
    {
        _ui = ui;
        _stale = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _stale.Tick += (_, _) => Sweep();
    }

    public void SetEnabled(bool on)
    {
        if (on == (_watcher is not null)) return;
        if (!on) { _watcher?.Dispose(); _watcher = null; _active.Clear(); _stale.Stop(); Active = null; return; }
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true };
            _watcher.Created += (_, e) => _ui.BeginInvoke(() => Touch(e.FullPath));
            _watcher.Changed += (_, e) => _ui.BeginInvoke(() => Touch(e.FullPath));
            _watcher.Deleted += (_, e) => _ui.BeginInvoke(() => Finish(e.FullPath, null));
            _watcher.Renamed += (_, e) => _ui.BeginInvoke(() => Finish(e.OldFullPath, e.FullPath));
        }
        catch (Exception ex) { Log.Warn("Download watcher: " + ex.Message); }
    }

    private static bool IsTemp(string path) => TempExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void Touch(string path)
    {
        if (!IsTemp(path)) return;
        _active[path] = DateTime.UtcNow;
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        var name = Path.GetFileNameWithoutExtension(path);
        var newActive = (name.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase) ? "Downloading…" : name, size);
        var changed = Active?.Name != newActive.Item1;
        Active = newActive;
        JustCompleted = null;
        _stale.Start();
        if (changed) Changed?.Invoke();
    }

    private void Finish(string oldPath, string? newPath)
    {
        if (!_active.Remove(oldPath)) return;
        JustCompletedPath = newPath is not null && !IsTemp(newPath) ? newPath : null;
        JustCompleted = JustCompletedPath is null ? null : Path.GetFileName(JustCompletedPath);
        if (_active.Count == 0) { Active = null; _stale.Stop(); }
        Changed?.Invoke();
        JustCompleted = null; JustCompletedPath = null;
    }

    private void Sweep()
    {
        foreach (var key in _active.Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds > 20 || !File.Exists(kv.Key)).Select(kv => kv.Key).ToList()) _active.Remove(key);
        if (_active.Count == 0) { Active = null; _stale.Stop(); Changed?.Invoke(); }
    }

    public void Dispose() { _watcher?.Dispose(); _stale.Stop(); }
}

/// <summary>Creates one island per target display and keeps them in sync with settings.</summary>
public sealed class IslandController
{
    private readonly List<IslandWindow> _windows = new();
    public DownloadWatcher Downloads { get; }
    public ScreenshotWatcher Screenshots { get; }
    public RainAlerts Rain { get; }
    public KeyboardLanguage Language { get; }
    /// <summary>Cards from your scripts and apps (opt-in local pipe).</summary>
    public IslandPipe Pipe { get; }
    public IReadOnlyList<IslandWindow> Windows => _windows;

    public IslandController(Dispatcher ui)
    {
        Downloads = new DownloadWatcher(ui);
        Screenshots = new ScreenshotWatcher(ui);
        Rain = new RainAlerts(ui);
        Language = new KeyboardLanguage(ui);
        Pipe = new IslandPipe(ui);
        Pipe.Received += ShowAppCard;
        Hub.Shell.DisplayChanged += () => ui.BeginInvoke(DispatcherPriority.Background, Apply);
    }

    public void Apply()
    {
        var s = Hub.Settings.Island;
        Downloads.SetEnabled(s.Enabled && s.WatchDownloads);
        Screenshots.SetEnabled(s.Enabled && s.ScreenshotCards);
        Rain.SetEnabled(s.Enabled && s.RainAlerts);
        Language.SetEnabled(s.Enabled && s.LanguageAlerts);
        Pipe.SetEnabled(s.Enabled && s.AppCards);
        Hub.Devices?.Apply();
        var targets = !s.Enabled ? new List<string>()
            : s.Monitor == "all" ? Monitors.All().Select(m => m.Device).ToList()
            : new List<string> { s.Monitor == "primary" ? Monitors.Primary.Device : Monitors.Find(s.Monitor).Device };

        foreach (var w in _windows.Where(w => !targets.Contains(w.Device)).ToList()) { w.Close(); _windows.Remove(w); }
        foreach (var device in targets.Where(d => _windows.All(w => w.Device != d)))
        {
            var w = new IslandWindow(device, Downloads);
            _windows.Add(w);
            w.Show();
        }
        foreach (var w in _windows) w.ApplySettings();
    }

    public void Toggle(bool keyboard)
    {
        if (_windows.Count == 0) return;
        // Act on the island under the cursor's display when there are several.
        Native.GetCursorPos(out var p);
        var device = Monitors.FromPoint(p.X, p.Y).Device;
        (_windows.FirstOrDefault(w => w.Device == device) ?? _windows[0]).Toggle(keyboard);
    }

    public void Expand(string? tab = null) { if (_windows.Count > 0) _windows[0].Expand(false); }
    public void CollapseAll() { foreach (var w in _windows) w.Collapse(); }
    public void Peek(string glyph, string text) { foreach (var w in _windows) w.Peek(glyph, text); }
    public void Centre() { foreach (var w in _windows) w.Centre(); }
    public void Demo(string what) { foreach (var w in _windows) w.Demo(what); }
    /// <summary>Events changed outside the island (an import): redraw the month marks and the open day.</summary>
    public void RefreshCalendar() { foreach (var w in _windows) w.RefreshCalendar(); }
    public void CloseAll() { Pipe.SetEnabled(false); foreach (var w in _windows.ToList()) w.Close(); _windows.Clear(); }

    /// <summary>
    /// A card from the local pipe. The sending program's name always leads the second line, so a card can't pass
    /// itself off as one of WinOrbit's own; the card has no click action, link or buttons.
    /// </summary>
    private void ShowAppCard(PipeCard card)
    {
        if (!Hub.Settings.Island.AppCards) return;
        var (glyph, color) = AppCardLook(card.Icon);
        var line = card.Text.Length > 0 ? $"{card.Sender} · {card.Text}" : card.Sender;
        foreach (var w in _windows) w.ShowCard(glyph, card.Title, line, color, card.Seconds);
    }

    internal static (string Glyph, Color Color) AppCardLook(string icon) => icon switch
    {
        "done" => ("", Color.FromRgb(0x34, 0xC7, 0x59)),
        "warning" => ("", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        "error" => ("", Color.FromRgb(0xFF, 0x45, 0x3A)),
        "bell" => ("", Color.FromRgb(0x5E, 0x9E, 0xFF)),
        _ => ("", Color.FromRgb(0x5E, 0x9E, 0xFF)),
    };
}
