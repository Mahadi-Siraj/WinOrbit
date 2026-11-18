using System.Text.Json.Serialization;

namespace WinOrbit.Core;

// Plain serialisable state. Everything here is stored locally as JSON and nowhere else.

public enum ThemeMode { Dark, Light, Auto, Scheduled }
public enum GradientKind { Linear, Radial }

public sealed class GradientStopDef
{
    public string Color { get; set; } = "#4C8DFF";
    public double Offset { get; set; }
    public GradientStopDef() { }
    public GradientStopDef(string color, double offset) { Color = color; Offset = offset; }
}

public sealed class GradientDef
{
    /// <summary>True: stops are regenerated from the wallpaper palette. False: Stops are user-authored.</summary>
    public bool FromWallpaper { get; set; } = true;
    public GradientKind Kind { get; set; } = GradientKind.Linear;
    public double Angle { get; set; } = 135;
    public double Opacity { get; set; } = 0.55;
    public double Saturation { get; set; } = 1.0;
    public List<GradientStopDef> Stops { get; set; } = new();

    public GradientDef Clone() => new()
    {
        FromWallpaper = FromWallpaper, Kind = Kind, Angle = Angle, Opacity = Opacity, Saturation = Saturation,
        Stops = Stops.Select(s => new GradientStopDef(s.Color, s.Offset)).ToList()
    };
}

/// <summary>Glass material parameters. Dark and light each own a full, independent set.</summary>
public sealed class GlassParams
{
    public string Tint { get; set; } = "#12151C";
    public double TintOpacity { get; set; } = 0.46;
    public double Blur { get; set; } = 40;
    public double BackdropSaturation { get; set; } = 1.3;
    public double BorderIntensity { get; set; } = 0.55;
    public double HighlightIntensity { get; set; } = 0.5;
    public double Glow { get; set; } = 0.2;
    public double ShadowIntensity { get; set; } = 0.45;
    public double Refraction { get; set; } = 0.05;

    public GlassParams Clone() => (GlassParams)MemberwiseClone();
}

public sealed class Theme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public string Description { get; set; } = "";
    public bool BuiltIn { get; set; }
    /// <summary>Built-in theme this one was customised from, used by "Reset to default".</summary>
    public string BasedOn { get; set; } = "";
    public ThemeMode Mode { get; set; } = ThemeMode.Auto;
    public string LightAt { get; set; } = "07:00";
    public string DarkAt { get; set; } = "19:00";
    public bool AccentFromWallpaper { get; set; } = true;
    public string AccentDark { get; set; } = "#7FB5FF";
    public string AccentLight { get; set; } = "#2463EB";
    public GlassParams Dark { get; set; } = new();
    public GlassParams Light { get; set; } = new() { Tint = "#F6F8FC", TintOpacity = 0.58, BorderIntensity = 0.75, HighlightIntensity = 0.7, ShadowIntensity = 0.28, Glow = 0.12, BackdropSaturation = 1.15 };
    public GradientDef Gradient { get; set; } = new();
    public double CornerRadius { get; set; } = 22;
    public string FontFamily { get; set; } = "Segoe UI Variable Display";
    public double FontScale { get; set; } = 1.0;
    public double Animation { get; set; } = 1.0;

    public Theme Clone()
    {
        var t = (Theme)MemberwiseClone();
        t.Dark = Dark.Clone();
        t.Light = Light.Clone();
        t.Gradient = Gradient.Clone();
        return t;
    }
}

public sealed class IslandSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>"primary", "all", or a display device name such as \\.\DISPLAY2.</summary>
    public string Monitor { get; set; } = "primary";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; } = 10;
    /// <summary>While dragging, the island clicks into the horizontal centre when it gets close.</summary>
    public bool SnapToCentre { get; set; } = true;
    /// <summary>Only vertical dragging; the island always stays horizontally centred.</summary>
    public bool KeepCentred { get; set; }
    /// <summary>Show volume and (laptop) brightness changes as an animated meter in the capsule.</summary>
    public bool VolumeHud { get; set; } = true;
    /// <summary>Laptops: announce plugging in/out and warn at 20 % and 10 % battery.</summary>
    public bool BatteryAlerts { get; set; } = true;
    /// <summary>A key cap with its light when Caps Lock, Num Lock or Scroll Lock is switched.</summary>
    public bool LockKeyAlerts { get; set; } = true;
    /// <summary>Wi-Fi or Ethernet connecting and dropping.</summary>
    public bool NetworkAlerts { get; set; } = true;
    /// <summary>Paired Bluetooth devices connecting and disconnecting.</summary>
    public bool BluetoothAlerts { get; set; } = true;
    /// <summary>USB devices plugged in and removed.</summary>
    public bool UsbAlerts { get; set; } = true;
    /// <summary>Island edge: "Glass" (the plain illuminated rim) or a coloured gradient preset.</summary>
    public string Rim { get; set; } = "Glass";
    /// <summary>A soft coloured glow around the island in the rim's colours.</summary>
    public bool RimGlow { get; set; }
    /// <summary>"Capsule", "Rounded", "Square", "Notch" (flush with the top edge) or "Drop" (rounder at the bottom).</summary>
    public string Shape { get; set; } = "Capsule";
    /// <summary>Collapsed height: "Slim", "Regular" or "Tall".</summary>
    public string Thickness { get; set; } = "Regular";
    /// <summary>Pop-ups when dark/light mode, night light, airplane mode or Energy Saver are switched.</summary>
    public bool SystemAlerts { get; set; } = true;
    /// <summary>Equaliser bars follow what is actually playing (Windows' output meter) instead of a loop.</summary>
    public bool AudioReactive { get; set; } = true;
    /// <summary>Show a dot while an app uses the microphone or camera, like phones do.</summary>
    public bool PrivacyDot { get; set; } = true;
    /// <summary>Show the current lyric line in the capsule while music plays (needs Lyrics in Privacy).</summary>
    public bool LyricsInCapsule { get; set; } = true;
    /// <summary>Keep a history of copied text, links and images in memory. Off by default.</summary>
    public bool ClipboardHistory { get; set; }
    /// <summary>Delay before hover-expanding, so passing the cursor over the top of the screen doesn't open it.</summary>
    public int HoverDelayMs { get; set; } = 260;
    /// <summary>One-time migrations of the module list for tabs added in later versions.</summary>
    public int ModulesVersion { get; set; }
    /// <summary>Per-display offsets, keyed by device name, used when Monitor is "all".</summary>
    public Dictionary<string, double[]> PerMonitorOffset { get; set; } = new();
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
    /// <summary>"Glass" (wallpaper-sampled), "Smoked" (dense tint) or "Solid".</summary>
    public string Material { get; set; } = "Glass";
    public bool AutoHide { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool ExpandOnHover { get; set; }
    public int AutoCollapseSeconds { get; set; } = 6;
    public bool PeekOnMediaChange { get; set; } = true;
    public bool ShowTime { get; set; } = true;
    public bool ShowDate { get; set; } = true;
    public bool ShowMedia { get; set; } = true;
    public bool ShowTimer { get; set; } = true;
    public bool ShowSystem { get; set; }
    public bool WatchDownloads { get; set; }
    public List<string> Modules { get; set; } = new() { "media", "system", "controls", "focus", "calendar", "notes" };

    public IslandSettings Clone()
    {
        var c = (IslandSettings)MemberwiseClone();
        c.Modules = new List<string>(Modules);
        c.PerMonitorOffset = PerMonitorOffset.ToDictionary(k => k.Key, v => (double[])v.Value.Clone());
        return c;
    }
}

public sealed class WidgetInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Kind { get; set; } = "clock";
    public string Monitor { get; set; } = "";
    /// <summary>Position in physical pixels relative to the monitor's top-left corner.</summary>
    public double X { get; set; } = 80;
    public double Y { get; set; } = 80;
    /// <summary>Size in device-independent units; WPF scales it for the monitor's DPI.</summary>
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 150;
    public double Opacity { get; set; } = 1.0;
    public bool Visible { get; set; } = true;
    public bool Pinned { get; set; }
    public string Variant { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();

    public WidgetInstance Clone()
    {
        var c = (WidgetInstance)MemberwiseClone();
        c.Options = new Dictionary<string, string>(Options);
        return c;
    }

    public string Opt(string key, string fallback = "") => Options.TryGetValue(key, out var v) ? v : fallback;
}

public sealed class LayoutSettings
{
    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 16;
    public bool Locked { get; set; }
    public int Margin { get; set; } = 28;
    public int Spacing { get; set; } = 16;
    public bool WidgetsVisible { get; set; } = true;
    public LayoutSettings Clone() => (LayoutSettings)MemberwiseClone();
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "Profile";
    public string Description { get; set; } = "";
    public string Glyph { get; set; } = "\uE771";
    public bool BuiltIn { get; set; }
    public string ThemeId { get; set; } = "pure-liquid-glass";
    public IslandSettings Island { get; set; } = new();
    public List<WidgetInstance> Widgets { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public bool ReducedMotion { get; set; }
    /// <summary>Optional monitor brightness (0-100) applied where DDC/CI is supported. Null leaves it alone.</summary>
    public int? Brightness { get; set; }
}

public sealed class TimeRule
{
    public bool Enabled { get; set; } = true;
    public string Time { get; set; } = "22:00";
    public string ProfileId { get; set; } = "";
}

public sealed class AppRule
{
    public bool Enabled { get; set; } = true;
    public string ProcessName { get; set; } = "";
    public string ProfileId { get; set; } = "";
}

public sealed class AutomationSettings
{
    /// <summary>Master switch. Off by default: nothing switches by itself until the user opts in.</summary>
    public bool Enabled { get; set; }
    public List<TimeRule> TimeRules { get; set; } = new();
    public bool FullscreenRule { get; set; }
    public string FullscreenProfileId { get; set; } = "gaming";
    public List<AppRule> AppRules { get; set; } = new();
}

public sealed class GeneralSettings
{
    public bool RunAtStartup { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool PauseInFullscreen { get; set; } = true;
    public bool Use24Hour { get; set; } = true;
    public string HotkeyIsland { get; set; } = "Ctrl+Alt+Space";
    public string HotkeyWidgets { get; set; } = "Ctrl+Alt+W";
    public string HotkeyMain { get; set; } = "Ctrl+Alt+G";
    /// <summary>Opens the launcher. Alt+Space is what PowerToys Run and Flow Launcher use; a free fallback is chosen if taken.</summary>
    public string HotkeyLauncher { get; set; } = "Alt+Space";
    /// <summary>Web search in the launcher opens this engine in the user's own browser: duckduckgo, google or bing.</summary>
    public string SearchEngine { get; set; } = "duckduckgo";
    /// <summary>The welcome card (choose a starting style) was completed or dismissed.</summary>
    public bool WelcomeDone { get; set; }
    /// <summary>Main window material: "acrylic" (blurs what is behind), "mica" (wallpaper tint) or "glass" (WinOrbit's own).</summary>
    public string WindowMaterial { get; set; } = "acrylic";
    /// <summary>Render the settings window on the CPU. Only needed when a graphics driver misbehaves; WinOrbit also detects that itself.</summary>
    public bool ForceSoftwareRendering { get; set; }
    public bool FirstRunDone { get; set; }
}

public sealed class PrivacySettings
{
    /// <summary>Master network switch. Off by default; while off WinOrbit opens no sockets at all.</summary>
    public bool NetworkAllowed { get; set; }
    public bool WeatherEnabled { get; set; }
    public string WeatherPlace { get; set; } = "";
    public double WeatherLat { get; set; }
    public double WeatherLon { get; set; }
    public bool WeatherFahrenheit { get; set; }
    /// <summary>Synced lyrics from LRCLIB (sends track title, artist, album and length). Off by default; needs the network switch.</summary>
    public bool LyricsEnabled { get; set; }
}

public sealed class RainmeterPrefs
{
    public string CustomExePath { get; set; } = "";
    /// <summary>"auto" (show the Rainmeter page only when Rainmeter is installed or running), "always" or "never".</summary>
    public string PageVisibility { get; set; } = "auto";
    public List<string> Favourites { get; set; } = new();
    public List<string> Recent { get; set; } = new();
}

public sealed class AppSettings
{
    public int Schema { get; set; } = 1;
    public string ActiveThemeId { get; set; } = "pure-liquid-glass";
    public string ActiveProfileId { get; set; } = "";
    public bool WallpaperAutoExtract { get; set; } = true;
    /// <summary>When Wallpaper Engine or Lively draws the desktop, snapshot that layer (in memory only) so glass matches it.</summary>
    public bool LiveWallpaperCapture { get; set; } = true;
    public IslandSettings Island { get; set; } = new();
    public List<WidgetInstance> Widgets { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
    public AutomationSettings Automation { get; set; } = new();
    public RainmeterPrefs Rainmeter { get; set; } = new();
    /// <summary>Snapshot taken before the last profile was applied, for "Restore previous".</summary>
    public Profile? Previous { get; set; }
}

public sealed class TaskItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class FocusSession
{
    public DateTime Start { get; set; }
    public int Minutes { get; set; }
    public string Label { get; set; } = "Focus";
}

/// <summary>User content (notes, tasks, session history). Local only.</summary>
public sealed class UserData
{
    public Dictionary<string, string> Notes { get; set; } = new();
    public Dictionary<string, List<TaskItem>> Tasks { get; set; } = new();
    public List<FocusSession> Sessions { get; set; } = new();
    /// <summary>Clipboard items the user pinned (text only). Unpinned history lives in memory and is never written to disk.</summary>
    public List<string> ClipPins { get; set; } = new();
    /// <summary>How often each launcher result was opened, to rank favourites first. Stays on this PC.</summary>
    public Dictionary<string, int> LaunchCounts { get; set; } = new();

    [JsonIgnore] public const string IslandNoteKey = "island";
}
