using System.Diagnostics;
using System.IO;
using System.Windows;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.WindowsIntegration;

namespace WinOrbit.Launcher;

public enum ItemKind { App, Setting, WinOrbit, Action, Recent, Calc, Web }

public sealed class LauncherItem
{
    public ItemKind Kind { get; init; }
    public string Key { get; init; } = "";          // stable id for usage ranking
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string Glyph { get; init; } = Design.Glyph.Launch;
    public string? AppId { get; init; }             // shell:AppsFolder parsing name, for the icon
    public Action Run { get; init; } = () => { };
    /// <summary>Asks for a second Enter before running (sleep, lock …).</summary>
    public bool Confirm { get; init; }
    public double Score { get; set; }
}

/// <summary>
/// Everything the launcher can find. Apps come from Windows' own "All apps" list (shell:AppsFolder — desktop
/// and Store apps, exactly what Start shows), read locally. Usage counts for ranking stay in data.json.
/// Nothing is sent anywhere; web searches open in the user's own browser.
/// </summary>
public static class LauncherIndex
{
    private static List<LauncherItem> _apps = new();
    private static DateTime _appsAt;
    private static Task? _loading;

    /// <summary>Refresh the app list in the background (at most every 5 minutes).</summary>
    public static Task WarmUp()
    {
        if (_loading is { IsCompleted: false }) return _loading;
        if (DateTime.Now - _appsAt < TimeSpan.FromMinutes(5) && _apps.Count > 0) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try { _apps = LoadApps(); _appsAt = DateTime.Now; }
            catch (Exception ex) { Log.Warn("Launcher app index: " + ex.Message); }
            tcs.TrySetResult();
        }) { IsBackground = true, Name = "WinOrbit app index" };
        t.SetApartmentState(ApartmentState.STA);   // the Shell object model is apartment-threaded
        t.Start();
        return _loading = tcs.Task;
    }

    private static List<LauncherItem> LoadApps()
    {
        var list = new List<LauncherItem>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return list;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        foreach (dynamic item in folder.Items())
        {
            string name = item.Name, id = item.Path;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
            var lower = name.ToLowerInvariant();
            if (lower.StartsWith("uninstall") || lower.Contains("readme") || lower.EndsWith(" help") || lower.EndsWith(" documentation")) continue;
            var appId = id;
            list.Add(new LauncherItem
            {
                Kind = ItemKind.App, Key = "app:" + id, Title = name, Subtitle = "App", AppId = appId, Glyph = Design.Glyph.Launch,
                Run = () => Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + appId) { UseShellExecute = true })
            });
        }
        return list.GroupBy(i => i.Title, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    // ------------------------------------------------------------------ fixed entries
    private static readonly (string Title, string Uri, string Keywords)[] WindowsSettings =
    {
        ("Display", "display", "screen resolution scale monitor hdr"), ("Night light", "nightlight", "blue light warm"), ("Sound", "sound", "audio speakers microphone output"),
        ("Notifications", "notifications", "alerts"), ("Focus", "quiethours", "do not disturb dnd"), ("Power & battery", "powersleep", "sleep power mode battery"),
        ("Storage", "storagesense", "disk space cleanup"), ("Multitasking", "multitasking", "snap windows alt tab"), ("Clipboard", "clipboard", "win+v history"),
        ("About this PC", "about", "system specs rename pc"), ("Bluetooth & devices", "bluetooth", "devices pair headphones"), ("Printers & scanners", "printers", "print"),
        ("Mouse", "mousetouchpad", "pointer cursor speed touchpad"), ("Typing", "typing", "keyboard autocorrect"), ("Wi-Fi", "network-wifi", "wireless internet network"),
        ("Ethernet", "network-ethernet", "wired network"), ("VPN", "network-vpn", "proxy"), ("Airplane mode", "network-airplanemode", "flight"),
        ("Background", "personalization-background", "wallpaper"), ("Colours", "colors", "accent dark light theme transparency"), ("Lock screen", "lockscreen", ""),
        ("Themes", "themes", "cursor icons"), ("Fonts", "fonts", ""), ("Start", "personalization-start", "start menu"), ("Taskbar", "taskbar", "tray icons alignment"),
        ("Installed apps", "appsfeatures", "uninstall programs"), ("Default apps", "defaultapps", "browser open with"), ("Startup apps", "startupapps", "sign in boot"),
        ("Sign-in options", "signinoptions", "password pin hello"), ("Date & time", "dateandtime", "clock timezone"), ("Language & region", "regionlanguage", "locale"),
        ("Game Mode", "gaming-gamemode", "games performance"), ("Accessibility: Text size", "easeofaccess-display", "bigger text"), ("Privacy & security", "privacy", "permissions"),
        ("Microphone permissions", "privacy-microphone", "mic"), ("Camera permissions", "privacy-webcam", "webcam"), ("Location", "privacy-location", "gps"),
        ("Windows Update", "windowsupdate", "updates upgrade"), ("Windows Security", "windowsdefender", "antivirus defender firewall"), ("Recovery", "recovery", "reset restore"),
        ("Troubleshoot", "troubleshoot", "fix problems"), ("For developers", "developers", "dev mode")
    };

    private static readonly (string Id, string Title, string Keywords)[] WinOrbitPages =
    {
        ("home", "Home", "overview"), ("appearance", "Appearance", "theme dark light mode accent"), ("studio", "Liquid Glass Editor", "glass blur tint studio"),
        ("wallpaper", "Wallpaper & Gradients", "wallpaper engine colours gradient"), ("island", "Dynamic Island", "notch capsule pill"), ("widgets", "Widgets", "clock calendar weather desktop"),
        ("rainmeter", "Rainmeter", "skins rmskin"), ("tweaks", "Windows Tweaks", "taskbar animations transparency"), ("profiles", "Profiles", "gaming study automation"),
        ("settings", "Settings", "shortcuts privacy network startup"), ("about", "About & Diagnostics", "memory cpu log ledger")
    };

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool LockWorkStation();
    [System.Runtime.InteropServices.DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);

    private static IEnumerable<LauncherItem> Fixed()
    {
        foreach (var (title, uri, kw) in WindowsSettings)
            yield return new LauncherItem { Kind = ItemKind.Setting, Key = "ms:" + uri, Title = title, Subtitle = "Windows Settings", Keywords = kw + " settings", Glyph = Design.Glyph.Settings, Run = () => Island.IslandWindow.OpenUri("ms-settings:" + uri) };
        foreach (var (id, title, kw) in WinOrbitPages)
        {
            if (id == "rainmeter" && !Hub.Rainmeter.PageWanted()) continue;
            yield return new LauncherItem { Kind = ItemKind.WinOrbit, Key = "gw:" + id, Title = title, Subtitle = "WinOrbit", Keywords = kw + " winorbit", Glyph = Design.Glyph.Home, Run = () => App.Current.ShowMain(id) };
        }
        LauncherItem Act(string key, string title, string sub, string kw, string glyph, Action run, bool confirm = false)
            => new() { Kind = ItemKind.Action, Key = "act:" + key, Title = title, Subtitle = sub, Keywords = kw, Glyph = glyph, Run = run, Confirm = confirm };
        var dark = QuickControls.WindowsDark;
        yield return Act("dark", dark ? "Switch to light mode" : "Switch to dark mode", "Windows and WinOrbit", "dark mode light mode theme", Design.Glyph.Color, () => QuickControls.SetWindowsDark(!dark));
        var night = QuickControls.NightLightOn;
        if (night is not null) yield return Act("night", night.Value ? "Turn night light off" : "Turn night light on", "Display", "night light blue warm", Design.Glyph.Moon, () => QuickControls.SetNightLight(!night.Value));
        yield return Act("awake", QuickControls.KeepAwake ? "Stop keeping the PC awake" : "Keep the PC awake", "Until switched off or WinOrbit quits", "awake caffeine sleep screen on", Design.Glyph.Clock, () => QuickControls.SetKeepAwake(!QuickControls.KeepAwake));
        var t = Hub.Timers;
        if (t.Running) yield return Act("timer-stop", $"Stop the {t.ModeLabel.ToLowerInvariant()} ({t.Display} left)", "Timer", "stop timer focus pause", Design.Glyph.Stopwatch, () => t.Stop());
        else yield return Act("focus", $"Start a {t.FocusMinutes}-minute focus", "Focus timer", "focus pomodoro timer start work", Design.Glyph.Stopwatch, () => { t.SetMode(Services.TimerMode.Focus); t.Start(); });
        yield return Act("widgets", Hub.Settings.Layout.WidgetsVisible ? "Hide widgets" : "Show widgets", "WinOrbit", "widgets desktop toggle", Design.Glyph.Tiles, () => Hub.Widgets.ToggleAll());
        yield return Act("centre", "Centre the island", "Dynamic Island", "center centre island notch middle", Design.Glyph.More, () => Hub.Island.Centre());
        yield return Act("preview-charging", "Preview: charging animation", "Dynamic Island", "preview demo charging battery", Design.Glyph.Battery, () => Hub.Island.Demo("charging"));
        yield return Act("preview-low", "Preview: low battery", "Dynamic Island", "preview demo battery low", Design.Glyph.Warning, () => Hub.Island.Demo("low"));
        var micMuted = Services.Audio.GetMicMute();
        if (micMuted is not null) yield return Act("mic", micMuted.Value ? "Unmute microphone" : "Mute microphone", "Default microphone", "microphone mic mute unmute", "", () => Services.Audio.SetMicMute(!micMuted.Value));
        yield return Act("dnd", "Do not disturb", "Opens the notification centre", "dnd do not disturb notifications focus quiet", "", () => Island.IslandWindow.OpenUri("ms-actioncenter:"));
        foreach (var p in Hub.Profiles.All)
            yield return Act("profile:" + p.Id, $"Profile: {p.Name}", p.Description, "profile mode " + p.Name, p.Glyph, () => Hub.Profiles.Apply(p));
        yield return Act("lock", "Lock the PC", "Press Enter again to lock", "lock screen away", Design.Glyph.Lock, () => LockWorkStation(), true);
        yield return Act("sleep", "Sleep", "Press Enter again to put the PC to sleep", "sleep suspend standby", Design.Glyph.Moon, () => SetSuspendState(false, false, false), true);
    }

    private static IEnumerable<LauncherItem> RecentFiles()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        List<FileInfo> files;
        try { files = new DirectoryInfo(dir).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(60).ToList(); }
        catch { yield break; }
        dynamic? wsh = null;
        try { if (Type.GetTypeFromProgID("WScript.Shell") is { } t) wsh = Activator.CreateInstance(t); } catch { }
        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f.Name);
            var path = f.FullName;
            // Windows also records app-internal pseudo documents here; keep only shortcuts to real files and folders.
            string target = "";
            try { if (wsh is not null) target = (string)wsh.CreateShortcut(path).TargetPath; } catch { }
            if (target.Length == 0 || !(File.Exists(target) || Directory.Exists(target))) continue;
            yield return new LauncherItem { Kind = ItemKind.Recent, Key = "recent:" + name, Title = name, Subtitle = "Recent · " + f.LastWriteTime.ToString("d MMM"), Glyph = Design.Glyph.Page,
                Run = () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }) };
        }
    }

    // ------------------------------------------------------------------ search
    /// <summary>Settings, actions and recent files as of now; taken once each time the launcher opens.</summary>
    public static List<LauncherItem> Snapshot() => Fixed().Concat(RecentFiles()).ToList();

    public static List<LauncherItem> Search(string query, IReadOnlyList<LauncherItem> snapshot, int max = 8)
    {
        var q = query.Trim();
        var results = new List<LauncherItem>();
        if (q.Length == 0)
        {
            // Empty box: the things you use most.
            var usage = Hub.Store.Data.LaunchCounts;
            var all = _apps.Concat(snapshot).ToList();
            results.AddRange(usage.OrderByDescending(u => u.Value).Select(u => all.FirstOrDefault(i => i.Key == u.Key)).OfType<LauncherItem>().Take(max));
            return results;
        }

        if (Calculator.TryEvaluate(q, out var value))
        {
            var text = Calculator.Format(value);
            results.Add(new LauncherItem { Kind = ItemKind.Calc, Key = "calc", Title = "= " + text, Subtitle = "Enter copies the result", Glyph = "", Score = 10_000, Run = () => Clipboard.SetText(text) });
        }
        if (Units.TryConvert(q, out var converted))
            results.Add(new LauncherItem { Kind = ItemKind.Calc, Key = "unit", Title = "= " + converted, Subtitle = "Enter copies the result", Glyph = "", Score = 10_000, Run = () => Clipboard.SetText(converted) });

        var counts = Hub.Store.Data.LaunchCounts;
        foreach (var item in _apps.Concat(snapshot))
        {
            var s = Match(q, item.Title, item.Keywords);
            if (s <= 0) continue;
            if (item.Kind == ItemKind.Recent && s < 400) continue;      // recent files need a real name match
            s += item.Kind switch { ItemKind.App => 60, ItemKind.Action => 30, ItemKind.WinOrbit => 20, ItemKind.Setting => 10, ItemKind.Recent => -40, _ => 0 };
            if (counts.TryGetValue(item.Key, out var n)) s += Math.Min(n * 25, 250);
            item.Score = s;
            results.Add(item);
        }
        results = results.OrderByDescending(r => r.Score).ThenBy(r => r.Title.Length).Take(max).ToList();
        var engine = Hub.Settings.General.SearchEngine;
        var url = engine switch
        {
            "google" => "https://www.google.com/search?q=",
            "bing" => "https://www.bing.com/search?q=",
            _ => "https://duckduckgo.com/?q="
        } + Uri.EscapeDataString(q);
        var engineName = engine switch { "google" => "Google", "bing" => "Bing", _ => "DuckDuckGo" };
        if (results.Count < max) results.Add(new LauncherItem { Kind = ItemKind.Web, Key = "web", Title = $"Search {engineName} for “{q}”", Subtitle = "Opens in your browser", Glyph = Design.Glyph.Globe, Run = () => Island.IslandWindow.OpenUri(url) });
        return results;
    }

    /// <summary>
    /// Relevance of a query against a title (and hidden keywords): exact → prefix → word start → initials →
    /// substring → in-order letters. Pure and unit-tested.
    /// </summary>
    internal static double Match(string query, string title, string keywords = "")
    {
        var q = query.ToLowerInvariant();
        var t = title.ToLowerInvariant();
        if (t == q) return 1000;
        if (t.StartsWith(q)) return 800 - Math.Min(t.Length - q.Length, 100);
        var words = t.Split(new[] { ' ', '-', '_', '.', '&', ':', '(' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q))) return 650 - Math.Min(t.Length, 100) / 2;
        var initials = new string(words.Where(w => w.Length > 0).Select(w => w[0]).ToArray());
        if (q.Length >= 2 && initials.StartsWith(q)) return 560;
        if (t.Contains(q)) return 420;
        if (keywords.Length > 0)
        {
            var kw = keywords.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (kw.Any(k => k == q)) return 380;
            if (q.Length >= 3 && kw.Any(k => k.StartsWith(q))) return 320;
        }
        if (q.Length >= 3 && WordJump(q, t)) return 220;   // e.g. "vscode" → Visual Studio Code
        return 0;
    }

    /// <summary>
    /// Fuzzy match that only jumps to the start of a word: each letter continues the current run or starts a
    /// later word. "vscode" fits "visual studio code"; "spot" does not fit "need for speed most wanted".
    /// </summary>
    private static bool WordJump(string q, string t)
    {
        bool IsStart(int i) => i == 0 || !char.IsLetterOrDigit(t[i - 1]);
        var p = -1;
        foreach (var c in q)
        {
            if (p >= 0 && p < t.Length && t[p] == c) { p++; continue; }
            var next = -1;
            for (var i = Math.Max(p, 0); i < t.Length; i++) if (t[i] == c && IsStart(i)) { next = i; break; }
            if (next < 0) return false;
            p = next + 1;
        }
        return true;
    }

    public static void Used(LauncherItem item)
    {
        if (item.Kind is ItemKind.Calc or ItemKind.Web) return;
        var counts = Hub.Store.Data.LaunchCounts;
        counts[item.Key] = counts.TryGetValue(item.Key, out var n) ? n + 1 : 1;
        if (counts.Count > 300) counts.Remove(counts.OrderBy(c => c.Value).First().Key);
        Hub.Store.SaveData();
    }
}
