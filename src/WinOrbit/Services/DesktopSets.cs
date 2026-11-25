using System.IO;
using System.Windows;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// Desktop sets: a whole look in one click (a clock skin, its matching pieces, the island's glance, the theme and
/// liquid glass), and the same as a small file to share. Applying one replaces the widgets on the desktop; the
/// desktop before it is kept for Profiles › Restore previous desktop.
/// </summary>
public static class DesktopSets
{
    public const string Extension = ".winorbitset";
    public const string FileFormat = "winorbit-desktop-set";
    internal const int MaxWidgets = 24;

    /// <summary>Widget options that hold your own things (pinned apps and their paths, a greeting name, quotes you typed). Never put in a shared file.</summary>
    internal static readonly string[] PrivateOptions = { "items", "name", "mine", "only", "offset" };

    public static event Action? Changed;
    public static List<DesktopSet> UserSets { get; } = new();
    public static IEnumerable<DesktopSet> All => BuiltIn.Concat(UserSets);
    public static DesktopSet? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    private static WidgetInstance P(string kind, string variant, double w, double h, double cx, double cy, params (string, string)[] opts)
    {
        var i = new WidgetInstance { Kind = kind, Variant = variant, Width = w, Height = h, X = cx, Y = cy };
        i.Options["style"] = "bare";
        foreach (var (k, v) in opts) i.Options[k] = v;
        return i;
    }

    /// <summary>The built-in sets, each drawn from a clock skin and the pieces made for it.</summary>
    public static readonly List<DesktopSet> BuiltIn = new()
    {
        new DesktopSet
        {
            Id = "set-orbit", Name = "Orbit", BuiltIn = true, ThemeId = "liquid-frosted", LiquidGlass = true, Glance = "status",
            Description = "Airy Orbit letters in the middle, a music line and what you're listening to under them, system slashes in the corner. Liquid glass.",
            Widgets = new()
            {
                P("clock", "orbit", 512, 160, 0.5, 0.36), P("visualiser", "", 512, 96, 0.5, 0.53), P("media", "listening", 512, 112, 0.5, 0.65),
                P("system", "slashes", 336, 160, 0.87, 0.14), P("recyclebin", "label", 300, 96, 0.9, 0.92),
            }
        },
        new DesktopSet
        {
            Id = "set-poster", Name = "Poster", BuiltIn = true, ThemeId = "pure-liquid-glass", LiquidGlass = false, Glance = "clock",
            Description = "A big condensed poster date on the left, a quote a day under it, your music and drive space on the right. Classic glass.",
            Widgets = new()
            {
                P("clock", "poster", 336, 336, 0.2, 0.42), P("quote", "", 512, 160, 0.24, 0.8), P("media", "listening", 512, 112, 0.76, 0.86),
                P("drives", "", 336, 160, 0.86, 0.16), P("battery", "", 160, 96, 0.92, 0.36),
            }
        },
        new DesktopSet
        {
            Id = "set-terminal", Name = "Terminal", BuiltIn = true, ThemeId = "minimal-dark", LiquidGlass = false, Glance = "system",
            Description = "Monospaced time, a slashes readout of CPU, memory and graphics, the song as a caption. Everything in terminal letters.",
            Widgets = new()
            {
                P("clock", "mono", 336, 160, 0.5, 0.38), P("system", "slashes", 336, 160, 0.5, 0.56), P("media", "caption", 336, 160, 0.5, 0.74),
                P("drives", "", 336, 160, 0.14, 0.16), P("battery", "", 160, 96, 0.92, 0.13),
            }
        },
        new DesktopSet
        {
            Id = "set-heavy", Name = "Heavy night", BuiltIn = true, ThemeId = "midnight", Glance = "quiet",
            Description = "Heavy time over the city, drive diamonds beside it, a music line, what's playing and a quote. Midnight glass.",
            Widgets = new()
            {
                P("clock", "heavy", 336, 160, 0.5, 0.28), P("drives", "", 336, 160, 0.8, 0.16), P("visualiser", "", 512, 96, 0.5, 0.46),
                P("media", "listening", 512, 112, 0.5, 0.58), P("quote", "", 512, 160, 0.2, 0.8),
            }
        },
        new DesktopSet
        {
            Id = "set-hairline", Name = "Hairline", BuiltIn = true, ThemeId = "liquid-clear", LiquidGlass = true, Glance = "clock",
            Description = "A tall hairline stack with the greeting, the song under it and a quote on the right. Nearly clear liquid glass.",
            Widgets = new()
            {
                P("clock", "hairline", 336, 336, 0.22, 0.42), P("media", "listening", 512, 112, 0.25, 0.76), P("quote", "", 512, 160, 0.76, 0.84),
                P("battery", "", 160, 96, 0.92, 0.13),
            }
        },
    };

    /// <summary>The three shown when WinOrbit starts for the first time.</summary>
    public static IEnumerable<DesktopSet> Starters => new[] { "set-orbit", "set-poster", "set-terminal" }.Select(id => BuiltIn.First(s => s.Id == id));

    // ----- placement -----

    /// <summary>A display's work area (clear of the taskbar) in physical pixels, relative to the display's top-left corner.</summary>
    internal static Rect WorkArea(MonitorInfo mon) =>
        new(mon.Work.Left - mon.Bounds.Left, mon.Work.Top - mon.Bounds.Top, Math.Max(1, mon.Work.Right - mon.Work.Left), Math.Max(1, mon.Work.Bottom - mon.Work.Top));

    /// <summary>Where a widget of this size (physical px) goes for a centre given as a share of the work area: kept wholly inside it.</summary>
    internal static (double X, double Y) ToPixels(double fx, double fy, double width, double height, Rect work)
    {
        fx = double.IsFinite(fx) ? Math.Clamp(fx, 0, 1) : 0.5;
        fy = double.IsFinite(fy) ? Math.Clamp(fy, 0, 1) : 0.5;
        var x = work.X + fx * work.Width - width / 2;
        var y = work.Y + fy * work.Height - height / 2;
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height));
        return (Math.Round(x), Math.Round(y));
    }

    /// <summary>The other way: a widget's centre as a share (0..1) of the work area.</summary>
    internal static (double FX, double FY) ToFraction(double x, double y, double width, double height, Rect work) =>
        (Math.Round(Math.Clamp((x + width / 2 - work.X) / work.Width, 0, 1), 4), Math.Round(Math.Clamp((y + height / 2 - work.Y) / work.Height, 0, 1), 4));

    // ----- safety for shared files -----

    /// <summary>
    /// Make a set safe to use: known widget kinds only, at most <see cref="MaxWidgets"/>, positions within the screen,
    /// sensible sizes, a short name, a known glance, and no private options. Null when it isn't a usable set.
    /// </summary>
    internal static DesktopSet? Clean(DesktopSet? set, Func<string, bool> knownKind, IReadOnlyCollection<string> glances)
    {
        if (set is null) return null;
        var clean = new DesktopSet
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Name = Trim(set.Name, 60) is { Length: > 0 } n ? n : "Shared set",
            Description = Trim(set.Description, 300),
            ThemeId = Trim(set.ThemeId, 80),
            Glance = glances.Contains(set.Glance) ? set.Glance : "",
            LiquidGlass = set.LiquidGlass,
        };
        foreach (var w in set.Widgets ?? new())
        {
            if (w is null || !knownKind(w.Kind ?? "") || clean.Widgets.Count >= MaxWidgets) continue;
            var c = new WidgetInstance
            {
                Kind = w.Kind!, Variant = Trim(w.Variant, 40),
                X = Finite(w.X, 0.5, 0, 1), Y = Finite(w.Y, 0.5, 0, 1),
                Width = Finite(w.Width, 336, 48, 2000), Height = Finite(w.Height, 160, 48, 2000),
                Opacity = Finite(w.Opacity, 1, 0.2, 1),
            };
            foreach (var (k, v) in (w.Options ?? new()).Take(16))
                if (k is { Length: > 0 and <= 40 } && !PrivateOptions.Contains(k)) c.Options[k] = Trim(v, 2000);
            clean.Widgets.Add(c);
        }
        return clean.Widgets.Count > 0 ? clean : null;
    }

    private static string Trim(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static double Finite(double v, double fallback, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    // ----- applying, saving, sharing -----

    internal static bool HasBattery() => Native.GetSystemPowerStatus(out var p) && (p.BatteryFlag & 128) == 0 && p.BatteryFlag != 255 && p.BatteryLifePercent <= 100;

    /// <summary>Put a set on the desktop: its widgets on the main display, its theme, glance and liquid glass.</summary>
    public static void Apply(DesktopSet set)
    {
        var s = Hub.Settings;
        Hub.Profiles.RememberCurrent();
        var mon = Monitors.Primary;
        var work = WorkArea(mon);
        var battery = HasBattery();
        var widgets = new List<WidgetInstance>();
        foreach (var w in set.Widgets)
        {
            if (Widgets.WidgetCatalog.Find(w.Kind) is null) continue;
            if (w.Kind == "battery" && !battery) continue;   // a desktop PC has no battery to show
            var c = w.Clone();
            c.Id = Guid.NewGuid().ToString("N")[..10];
            c.Monitor = mon.Device;
            c.Visible = true;
            (c.X, c.Y) = ToPixels(w.X, w.Y, w.Width * mon.Scale, w.Height * mon.Scale, work);
            widgets.Add(c);
        }
        s.Widgets = widgets;
        if (set.ThemeId.Length > 0 && Hub.Theme.Find(set.ThemeId) is not null) s.ActiveThemeId = set.ThemeId;
        if (set.Glance.Length > 0) s.Island.Glance = set.Glance;
        if (set.LiquidGlass is { } liquid) s.General.LiquidGlass = liquid;
        s.Layout.MatchClockLettering = true;
        s.Layout.WidgetsVisible = true;
        s.ActiveProfileId = "";
        s.ActiveSetId = set.Id;
        Hub.Store.Save();
        Hub.ApplyAll();
        Log.Info($"Desktop set applied: {set.Name}");
        Hub.Profiles.NotifyChanged();
        Changed?.Invoke();
        Hub.Island.Peek(Glyph.Personalize, $"{set.Name} set");
    }

    /// <summary>The desktop as it is now, as a set (positions as shares of each display, private options left out).</summary>
    public static DesktopSet Capture(string name)
    {
        var s = Hub.Settings;
        var set = new DesktopSet { Name = name, ThemeId = s.ActiveThemeId, Glance = s.Island.Glance, LiquidGlass = s.General.LiquidGlass, Description = "Saved from my desktop." };
        foreach (var w in s.Widgets.Where(w => w.Visible && Widgets.WidgetCatalog.Find(w.Kind) is not null).Take(MaxWidgets))
        {
            var mon = Monitors.Find(w.Monitor);
            var c = w.Clone();
            c.Id = Guid.NewGuid().ToString("N")[..10];
            c.Monitor = "";
            c.Pinned = false;
            foreach (var key in PrivateOptions) c.Options.Remove(key);
            (c.X, c.Y) = ToFraction(w.X, w.Y, w.Width * mon.Scale, w.Height * mon.Scale, WorkArea(mon));
            set.Widgets.Add(c);
        }
        return set;
    }

    public static void Load()
    {
        UserSets.Clear();
        if (!Directory.Exists(Paths.Sets)) return;
        foreach (var f in Directory.EnumerateFiles(Paths.Sets, "*.json"))
            if (Json.Read<DesktopSet>(f) is { } set && Clean(set, k => Widgets.WidgetCatalog.Find(k) is not null, Island.IslandWindow.Glances.Select(g => g.Id).ToList()) is { } clean)
            {
                clean.Id = set.Id is { Length: > 0 } id ? id : clean.Id;   // keep its file name
                UserSets.Add(clean);
            }
    }

    private static string FileOf(DesktopSet set) => Path.Combine(Paths.Sets, string.Concat(set.Id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')) + ".json");

    public static DesktopSet SaveCurrentAs(string name)
    {
        var set = Capture(name);
        UserSets.Add(set);
        Json.Write(FileOf(set), set);
        Hub.Settings.ActiveSetId = set.Id;
        Hub.Store.Save();
        Changed?.Invoke();
        return set;
    }

    public static void Delete(DesktopSet set)
    {
        if (set.BuiltIn) return;
        UserSets.RemoveAll(s => s.Id == set.Id);
        try { if (File.Exists(FileOf(set))) File.Delete(FileOf(set)); } catch (Exception ex) { Log.Error("Delete set failed", ex); }
        Changed?.Invoke();
    }

    /// <summary>Write a set to a file to share. Your own theme goes with it; built-in themes are named only.</summary>
    public static void Export(DesktopSet set, string path)
    {
        var copy = Json.Clone(set);
        foreach (var w in copy.Widgets) foreach (var key in PrivateOptions) w.Options.Remove(key);
        var theme = Hub.Theme.Find(copy.ThemeId);
        Json.Write(path, new DesktopSetFile { Set = copy, Theme = theme is { BuiltIn: false } ? theme : null });
    }

    /// <summary>Read a shared set file, keep it with your sets and return it; null when the file isn't a usable set.</summary>
    public static DesktopSet? Import(string path)
    {
        if (new FileInfo(path).Length > 512 * 1024) return null;
        var file = Json.Read<DesktopSetFile>(path);
        if (file is null || file.Format != FileFormat) return null;
        var set = Clean(file.Set, k => Widgets.WidgetCatalog.Find(k) is not null, Island.IslandWindow.Glances.Select(g => g.Id).ToList());
        if (set is null) return null;
        if (file.Theme is not null) { var t = Hub.Theme.SaveAsNew(file.Theme, Trim(file.Theme.Name, 60)); set.ThemeId = t.Id; }
        else if (set.ThemeId.Length > 0 && Hub.Theme.Find(set.ThemeId) is null) set.ThemeId = "";
        UserSets.Add(set);
        Json.Write(FileOf(set), set);
        Changed?.Invoke();
        return set;
    }
}
