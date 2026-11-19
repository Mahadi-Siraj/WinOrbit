using System.Globalization;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;

namespace WinOrbit.Services;

/// <summary>
/// Desktop profiles: a theme + island configuration + widget layout + motion preference, saved and switched as one.
/// Automation (time of day, fullscreen apps, specific apps) is off until the user turns it on.
/// </summary>
public sealed class ProfileService
{
    public List<Profile> UserProfiles { get; } = new();
    public IEnumerable<Profile> All => BuiltIn.Concat(UserProfiles);
    public string ActiveId => Hub.Settings.ActiveProfileId;
    public event Action? Changed;

    private readonly DispatcherTimer _timeRules = new();
    private string? _beforeAutomaticId;      // profile to return to when a fullscreen/app rule stops matching
    private bool _ruleActive;

    public ProfileService()
    {
        _timeRules.Tick += (_, _) => EvaluateTimeRules();
    }

    public void Load()
    {
        UserProfiles.Clear();
        UserProfiles.AddRange(Hub.Store.LoadUserProfiles().Where(p => !p.BuiltIn));
        Hub.Foreground.FullscreenChanged += EvaluateEventRules;
        Hub.Foreground.ForegroundChanged += EvaluateEventRules;
        ArmTimeRules();
    }

    public Profile? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>Snapshot what is on screen right now as a profile (not yet saved).</summary>
    public static Profile Capture(string name) => new()
    {
        Name = name,
        ThemeId = Hub.Settings.ActiveThemeId,
        Island = Hub.Settings.Island.Clone(),
        Widgets = Hub.Settings.Widgets.Select(w => w.Clone()).ToList(),
        Layout = Hub.Settings.Layout.Clone(),
        ReducedMotion = Hub.Settings.General.ReducedMotion
    };

    public Profile SaveCurrentAs(string name)
    {
        var p = Capture(name);
        UserProfiles.Add(p);
        Hub.Store.SaveProfile(p);
        Hub.Settings.ActiveProfileId = p.Id;
        Hub.Store.Save();
        Changed?.Invoke();
        return p;
    }

    public void UpdateFromCurrent(Profile p)
    {
        if (p.BuiltIn) return;
        var c = Capture(p.Name);
        p.ThemeId = c.ThemeId; p.Island = c.Island; p.Widgets = c.Widgets; p.Layout = c.Layout; p.ReducedMotion = c.ReducedMotion;
        Hub.Store.SaveProfile(p);
        Changed?.Invoke();
    }

    public void Delete(Profile p)
    {
        if (p.BuiltIn) return;
        UserProfiles.RemoveAll(x => x.Id == p.Id);
        Hub.Store.DeleteProfile(p);
        if (Hub.Settings.ActiveProfileId == p.Id) Hub.Settings.ActiveProfileId = "";
        Hub.Store.Save();
        Changed?.Invoke();
    }

    public Profile Duplicate(Profile p, string name)
    {
        var copy = Json.Clone(p);
        copy.Id = Guid.NewGuid().ToString("N")[..10];
        copy.Name = name;
        copy.BuiltIn = false;
        foreach (var w in copy.Widgets) w.Id = Guid.NewGuid().ToString("N")[..10];
        UserProfiles.Add(copy);
        Hub.Store.SaveProfile(copy);
        Changed?.Invoke();
        return copy;
    }

    /// <summary>Switch the whole desktop to a profile. The previous state is kept for "Restore previous".</summary>
    public void Apply(Profile p, bool automatic = false)
    {
        var s = Hub.Settings;
        s.Previous = Capture("Previous desktop");
        s.Previous.Id = s.ActiveProfileId;
        var copy = Json.Clone(p);

        // Built-in profiles describe widgets with placeholder positions; place them on the primary display.
        if (p.BuiltIn) LayoutBuiltIn(copy);
        foreach (var w in copy.Widgets) if (string.IsNullOrEmpty(w.Monitor)) w.Monitor = Monitors.Primary.Device;

        s.ActiveThemeId = copy.ThemeId;
        s.Island = copy.Island;
        s.Widgets = copy.Widgets;
        s.Layout = copy.Layout;
        s.General.ReducedMotion = copy.ReducedMotion;
        s.ActiveProfileId = p.Id;
        Hub.Store.Save();
        Hub.ApplyAll();
        if (copy.Brightness is { } b) _ = Task.Run(() => Brightness.Set(b));
        Log.Info($"Profile applied: {p.Name}{(automatic ? " (automation)" : "")}");
        Changed?.Invoke();
        Hub.Island.Peek(Glyph.Profiles, $"{p.Name} profile");
    }

    public bool CanRestorePrevious => Hub.Settings.Previous is not null;

    public void RestorePrevious()
    {
        var prev = Hub.Settings.Previous;
        if (prev is null) return;
        var s = Hub.Settings;
        var current = Capture("Previous desktop");
        current.Id = s.ActiveProfileId;
        s.ActiveThemeId = prev.ThemeId;
        s.Island = prev.Island;
        s.Widgets = prev.Widgets;
        s.Layout = prev.Layout;
        s.General.ReducedMotion = prev.ReducedMotion;
        s.ActiveProfileId = prev.Id;
        s.Previous = current;
        Hub.Store.Save();
        Hub.ApplyAll();
        Changed?.Invoke();
    }

    private static void LayoutBuiltIn(Profile p)
    {
        var mon = Monitors.Primary;
        double scale = mon.Scale, margin = p.Layout.Margin * scale, gap = p.Layout.Spacing * scale;
        double right = mon.Work.Right - mon.Bounds.Left - margin, top = mon.Work.Top - mon.Bounds.Top + margin + 46 * scale;
        double y = top;
        foreach (var w in p.Widgets)
        {
            w.Monitor = mon.Device;
            if (w.Opt("place") == "center")
            {
                w.X = Math.Round((mon.Bounds.Width - w.Width * scale) / 2);
                w.Y = Math.Round(mon.Bounds.Height * 0.3);
                continue;
            }
            w.X = Math.Round(right - w.Width * scale);
            w.Y = Math.Round(y);
            y += w.Height * scale + gap;
        }
    }

    // ----- built-in profiles -----
    private static WidgetInstance W(string kind, double w, double h, string variant = "", params (string, string)[] opts)
    {
        var i = new WidgetInstance { Kind = kind, Width = w, Height = h, Variant = variant };
        foreach (var (k, v) in opts) i.Options[k] = v;
        return i;
    }

    public static readonly List<Profile> BuiltIn = new()
    {
        new Profile
        {
            Id = "study", Name = "Study", BuiltIn = true, Glyph = Glyph.Education, ThemeId = "study",
            Description = "Clean desktop, a calm clock, focus timer and calendar. Notifications-free island.",
            Island = new IslandSettings { ShowMedia = false, PeekOnMediaChange = false, Modules = new() { "focus", "calendar", "notes", "media" } },
            Widgets = new() { W("clock", 260, 128, "stacked"), W("focus", 256, 264), W("calendar", 272, 268) },
            Layout = new LayoutSettings { Margin = 36, Spacing = 18 }
        },
        new Profile
        {
            Id = "gaming", Name = "Gaming", BuiltIn = true, Glyph = Glyph.Game, ThemeId = "gaming",
            Description = "Island tucked away and hidden in fullscreen, a compact monitor, minimal motion.",
            Island = new IslandSettings { AutoHide = true, HideInFullscreen = true, ShowDate = false, ShowSystem = true, PeekOnMediaChange = false, Modules = new() { "system", "media", "controls" } },
            Widgets = new() { W("system", 272, 250, "rings") },
            Layout = new LayoutSettings(), ReducedMotion = true
        },
        new Profile
        {
            Id = "productivity", Name = "Productivity", BuiltIn = true, Glyph = Glyph.Work, ThemeId = "productivity",
            Description = "Calendar, tasks, notes, media controls and a system monitor.",
            Island = new IslandSettings { Modules = new() { "media", "calendar", "focus", "notes", "system", "controls" } },
            Widgets = new() { W("calendar", 272, 268), W("tasks", 272, 250), W("media", 336, 128), W("system", 272, 250) },
            Layout = new LayoutSettings { Margin = 24, Spacing = 14 }
        },
        new Profile
        {
            Id = "minimal", Name = "Minimal", BuiltIn = true, Glyph = Glyph.Personalize, ThemeId = "pure-liquid-glass",
            Description = "Just your wallpaper, one elegant clock and a quiet island.",
            Island = new IslandSettings { ShowDate = false, ShowMedia = true, Modules = new() { "media", "calendar", "controls" } },
            Widgets = new() { W("clock", 360, 150, "", ("place", "center")) },
            Layout = new LayoutSettings()
        },
        new Profile
        {
            Id = "night", Name = "Night", BuiltIn = true, Glyph = Glyph.Moon, ThemeId = "midnight",
            Description = "Dark Midnight glass, reduced motion, dimmer display where supported.",
            Island = new IslandSettings { Opacity = 0.9, PeekOnMediaChange = false },
            Widgets = new() { W("clock", 260, 128, "date") },
            Layout = new LayoutSettings(), ReducedMotion = true, Brightness = 35
        },
    };

    // ----- automation -----
    public void ArmTimeRules()
    {
        _timeRules.Stop();
        var a = Hub.Settings.Automation;
        if (!a.Enabled || !a.TimeRules.Any(r => r.Enabled)) return;
        var now = DateTime.Now;
        var next = a.TimeRules.Where(r => r.Enabled).Select(r => NextOccurrence(r.Time, now)).Min();
        _timeRules.Interval = next - now + TimeSpan.FromSeconds(1);
        _timeRules.Start();
    }

    internal static DateTime NextOccurrence(string hhmm, DateTime now)
    {
        if (!TimeSpan.TryParse(hhmm, CultureInfo.InvariantCulture, out var t)) t = TimeSpan.FromHours(22);
        var today = now.Date + t;
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>The time rule whose time most recently passed (today or yesterday).</summary>
    internal static TimeRule? CurrentTimeRule(IEnumerable<TimeRule> rules, DateTime now)
    {
        TimeRule? best = null; var bestAt = DateTime.MinValue;
        foreach (var r in rules.Where(r => r.Enabled))
        {
            if (!TimeSpan.TryParse(r.Time, CultureInfo.InvariantCulture, out var t)) continue;
            var at = now.Date + t;
            if (at > now) at = at.AddDays(-1);
            if (at > bestAt) { best = r; bestAt = at; }
        }
        return best;
    }

    private void EvaluateTimeRules()
    {
        var a = Hub.Settings.Automation;
        if (a.Enabled && !_ruleActive && CurrentTimeRule(a.TimeRules, DateTime.Now) is { } r && Find(r.ProfileId) is { } p && p.Id != ActiveId)
            Apply(p, true);
        ArmTimeRules();
    }

    private void EvaluateEventRules()
    {
        var a = Hub.Settings.Automation;
        if (!a.Enabled) { _ruleActive = false; return; }
        Profile? target = null;
        var proc = Hub.Foreground.ProcessName;
        var app = a.AppRules.FirstOrDefault(r => r.Enabled && r.ProcessName.Length > 0 &&
            string.Equals(r.ProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), proc, StringComparison.OrdinalIgnoreCase));
        if (app is not null) target = Find(app.ProfileId);
        else if (a.FullscreenRule && Hub.Foreground.IsFullscreen) target = Find(a.FullscreenProfileId);

        if (target is not null)
        {
            if (target.Id == ActiveId) return;
            if (!_ruleActive) _beforeAutomaticId = ActiveId;
            _ruleActive = true;
            Apply(target, true);
        }
        else if (_ruleActive && !Hub.Foreground.IsFullscreen)
        {
            _ruleActive = false;
            var back = _beforeAutomaticId is { Length: > 0 } id ? Find(id) : null;
            if (back is not null) Apply(back, true); else RestorePrevious();
        }
    }
}
