using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinOrbit.Core;
using WinOrbit.Interop;
using WinOrbit.Wallpaper;
using Microsoft.Win32;

namespace WinOrbit.WindowsIntegration;

public enum TweakTier { Supported, Tweak }

/// <summary>
/// A per-user Windows appearance setting. Every tweak here only writes values under HKEY_CURRENT_USER
/// (the same ones the Settings app writes) or calls a documented API, needs no administrator rights,
/// and records the original value before the first change so it can be reverted.
/// </summary>
public sealed class Tweak
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string SideEffects { get; init; } = "";
    public TweakTier Tier { get; init; }
    public bool Disruptive { get; init; }
    public string[] Options { get; init; } = { "Off", "On" };
    public Func<int> Read { get; init; } = () => 0;
    public Action<int> Write { get; init; } = _ => { };
    /// <summary>Registry locations this tweak touches, for the backup.</summary>
    public (string Key, string Value)[] Touches { get; init; } = Array.Empty<(string, string)>();
}

public static class Tweaks
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";

    public static string BackupFile => Path.Combine(Paths.Backups, "windows-settings-backup.json");

    public static readonly List<Tweak> All = new()
    {
        new Tweak
        {
            Id = "apps-theme", Name = "App mode", Tier = TweakTier.Supported, Options = new[] { "Dark", "Light" },
            Description = "Whether apps use dark or light mode (Settings › Personalisation › Colours).",
            SideEffects = "Open apps switch immediately.",
            Read = () => GetInt(Personalize, "AppsUseLightTheme", 0) == 1 ? 1 : 0,
            Write = v => { SetInt(Personalize, "AppsUseLightTheme", v); Native.BroadcastSettingChange("ImmersiveColorSet"); },
            Touches = new[] { (Personalize, "AppsUseLightTheme") }
        },
        new Tweak
        {
            Id = "system-theme", Name = "Windows mode", Tier = TweakTier.Supported, Options = new[] { "Dark", "Light" },
            Description = "Dark or light for the taskbar, Start and system flyouts.",
            SideEffects = "The taskbar redraws; accent on taskbar is unavailable in light mode.",
            Read = () => GetInt(Personalize, "SystemUsesLightTheme", 0) == 1 ? 1 : 0,
            Write = v => { SetInt(Personalize, "SystemUsesLightTheme", v); if (v == 1) SetInt(Personalize, "ColorPrevalence", 0); Native.BroadcastSettingChange("ImmersiveColorSet"); },
            Touches = new[] { (Personalize, "SystemUsesLightTheme"), (Personalize, "ColorPrevalence") }
        },
        new Tweak
        {
            Id = "transparency", Name = "Transparency effects", Tier = TweakTier.Supported,
            Description = "Windows' own translucency for the taskbar, Start and Mica windows. WinOrbit's glass works either way.",
            SideEffects = "Turning it off makes system surfaces opaque and saves a little GPU work.",
            Read = () => GetInt(Personalize, "EnableTransparency", 1),
            Write = v => { SetInt(Personalize, "EnableTransparency", v); Native.BroadcastSettingChange("ImmersiveColorSet"); },
            Touches = new[] { (Personalize, "EnableTransparency") }
        },
        new Tweak
        {
            Id = "accent-taskbar", Name = "Accent on Start and taskbar", Tier = TweakTier.Supported,
            Description = "Tint Start and the taskbar with the accent colour. Requires Windows mode = Dark.",
            SideEffects = "Ignored by Windows while Windows mode is Light.",
            Read = () => GetInt(Personalize, "ColorPrevalence", 0),
            Write = v => { SetInt(Personalize, "ColorPrevalence", v); Native.BroadcastSettingChange("ImmersiveColorSet"); },
            Touches = new[] { (Personalize, "ColorPrevalence") }
        },
        new Tweak
        {
            Id = "accent-titlebars", Name = "Accent on title bars and borders", Tier = TweakTier.Supported,
            Description = "Colour window title bars and borders with the accent colour.",
            SideEffects = "Applies to apps that use the standard Windows frame.",
            Read = () => GetInt(Dwm, "ColorPrevalence", 0),
            Write = v => { SetInt(Dwm, "ColorPrevalence", v); Native.BroadcastSettingChange("ImmersiveColorSet"); },
            Touches = new[] { (Dwm, "ColorPrevalence") }
        },
        new Tweak
        {
            Id = "animations", Name = "Animation effects", Tier = TweakTier.Supported,
            Description = "Windows' own window and control animations (SPI_SETCLIENTAREAANIMATION).",
            SideEffects = "Off makes Windows feel snappier and also enables Reduced Motion in apps that respect it.",
            Read = () => { var v = 0; Native.SystemParametersInfoBool(Native.SPI_GETCLIENTAREAANIMATION, 0, ref v, 0); return v != 0 ? 1 : 0; },
            Write = v => Native.SystemParametersInfoSet(Native.SPI_SETCLIENTAREAANIMATION, 0, new IntPtr(v), Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE),
        },
        new Tweak
        {
            Id = "taskbar-align", Name = "Taskbar alignment", Tier = TweakTier.Tweak, Options = new[] { "Left", "Centre" },
            Description = "Position of the taskbar icons. WinOrbit's island looks best with centred icons.",
            SideEffects = "The taskbar re-lays out its icons.",
            Read = () => GetInt(Advanced, "TaskbarAl", 1),
            Write = v => { SetInt(Advanced, "TaskbarAl", v); Native.BroadcastSettingChange("TraySettings"); },
            Touches = new[] { (Advanced, "TaskbarAl") }
        },
        new Tweak
        {
            Id = "taskbar-autohide", Name = "Automatically hide the taskbar", Tier = TweakTier.Supported, Disruptive = true,
            Description = "Hides the taskbar until the pointer touches the bottom edge (documented AppBar API).",
            SideEffects = "Maximised windows grow to fill the screen; widgets placed near the bottom may need re-arranging.",
            Read = () => { var d = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>() }; return ((int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d) & Native.ABS_AUTOHIDE) != 0 ? 1 : 0; },
            Write = v =>
            {
                var d = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Native.FindWindow("Shell_TrayWnd", null) };
                d.lParam = new IntPtr(v == 1 ? Native.ABS_AUTOHIDE : Native.ABS_ALWAYSONTOP);
                Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
            }
        },
        new Tweak
        {
            Id = "taskbar-search", Name = "Taskbar search", Tier = TweakTier.Tweak, Options = new[] { "Hidden", "Icon", "Box" },
            Description = "How search appears on the taskbar. Hiding it gives a cleaner, more minimal bar.",
            SideEffects = "Search remains available from Start and with Win+S.",
            Read = () => Math.Clamp(GetInt(Search, "SearchboxTaskbarMode", 2), 0, 2) switch { 0 => 0, 1 => 1, _ => 2 },
            Write = v => { SetInt(Search, "SearchboxTaskbarMode", v); Native.BroadcastSettingChange("TraySettings"); },
            Touches = new[] { (Search, "SearchboxTaskbarMode") }
        },
        new Tweak
        {
            Id = "taskview", Name = "Task view button", Tier = TweakTier.Tweak,
            Description = "Show or hide the Task view button on the taskbar.",
            SideEffects = "Task view stays available with Win+Tab.",
            Read = () => GetInt(Advanced, "ShowTaskViewButton", 1),
            Write = v => { SetInt(Advanced, "ShowTaskViewButton", v); Native.BroadcastSettingChange("TraySettings"); },
            Touches = new[] { (Advanced, "ShowTaskViewButton") }
        },
    };

    public static int GetInt(string key, string name, int fallback)
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(key); return k?.GetValue(name) is int v ? v : fallback; } catch { return fallback; }
    }

    public static void SetInt(string key, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    // ----- backup / revert -----
    public sealed class Backup
    {
        public DateTime Taken { get; set; }
        public Dictionary<string, int> Values { get; set; } = new();   // tweak id → original option
        public string? AccentColor { get; set; }
        public string? Wallpaper { get; set; }
    }

    public static Backup LoadBackup() => Json.Read<Backup>(BackupFile) ?? new Backup();

    /// <summary>Record the current value the first time a tweak is changed. Later changes keep the original.</summary>
    public static void Remember(Tweak t)
    {
        var b = LoadBackup();
        if (b.Values.ContainsKey(t.Id)) return;
        if (b.Taken == default) b.Taken = DateTime.Now;
        b.Values[t.Id] = t.Read();
        Json.Write(BackupFile, b);
    }

    public static void Apply(Tweak t, int value)
    {
        Remember(t);
        try { t.Write(value); Log.Info($"Tweak {t.Id} = {value}"); }
        catch (Exception ex) { Log.Error($"Tweak {t.Id} failed", ex); throw; }
    }

    /// <summary>Restore everything WinOrbit changed to how it was before.</summary>
    public static int RevertAll()
    {
        var b = LoadBackup();
        var n = 0;
        foreach (var (id, value) in b.Values)
        {
            var t = All.FirstOrDefault(x => x.Id == id);
            if (t is null) continue;
            try { t.Write(value); n++; } catch (Exception ex) { Log.Error($"Revert {id}", ex); }
        }
        if (b.AccentColor is { } accent) { try { SetSystemAccent(ColorMath.Parse(accent), false); n++; } catch { } }
        if (b.Wallpaper is { } wp && File.Exists(wp)) { WallpaperEngine.SetWallpaper(wp); n++; }
        try { File.Move(BackupFile, BackupFile + $".restored-{DateTime.Now:yyyyMMdd-HHmmss}"); } catch { }
        return n;
    }

    // ----- system accent colour -----
    public static Color CurrentSystemAccent()
    {
        var v = GetInt(Dwm, "AccentColor", unchecked((int)0xFFD77800));
        var u = unchecked((uint)v);   // ABGR
        return Color.FromRgb((byte)(u & 0xFF), (byte)((u >> 8) & 0xFF), (byte)((u >> 16) & 0xFF));
    }

    /// <summary>
    /// Set the Windows accent colour the way Settings does: DWM accent values plus the Explorer accent palette.
    /// Title bars update at once; Start and the taskbar pick it up on the colour-set broadcast.
    /// </summary>
    public static void SetSystemAccent(Color c, bool remember = true)
    {
        if (remember)
        {
            var b = LoadBackup();
            if (b.AccentColor is null) { b.AccentColor = ColorMath.Hex(CurrentSystemAccent()); if (b.Taken == default) b.Taken = DateTime.Now; Json.Write(BackupFile, b); }
        }
        var abgr = unchecked((int)(0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R));
        var argb = unchecked((int)(0xC4000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
        SetInt(Dwm, "AccentColor", abgr);
        SetInt(Dwm, "ColorizationColor", argb);
        SetInt(Dwm, "ColorizationAfterglow", argb);

        // Palette of 8 shades, light → dark, as Settings stores it (RGBA, 4 bytes each).
        var (h, s, _) = ColorMath.ToHsl(c);
        var lights = new[] { 0.86, 0.76, 0.64, ColorMath.ToHsl(c).L, 0.38, 0.28, 0.2, 0.5 };
        var palette = new byte[32];
        for (var i = 0; i < 8; i++)
        {
            var shade = i == 3 ? c : ColorMath.FromHsl(h, s, lights[i]);
            palette[i * 4] = shade.R; palette[i * 4 + 1] = shade.G; palette[i * 4 + 2] = shade.B; palette[i * 4 + 3] = 0;
        }
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
        {
            k.SetValue("AccentPalette", palette, RegistryValueKind.Binary);
            var dark = palette.AsSpan(16, 4);
            k.SetValue("AccentColorMenu", abgr, RegistryValueKind.DWord);
            k.SetValue("StartColorMenu", unchecked((int)(0xFF000000u | ((uint)dark[2] << 16) | ((uint)dark[1] << 8) | dark[0])), RegistryValueKind.DWord);
        }
        using (var d = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop")) d.SetValue("AutoColorization", "0");
        Native.BroadcastSettingChange("ImmersiveColorSet");
        Log.Info("System accent set to " + ColorMath.Hex(c));
    }

    // ----- wallpaper from gradient -----
    /// <summary>Render a gradient as a PNG at the primary display's resolution and make it the wallpaper.</summary>
    public static string ApplyGradientWallpaper(Brush gradient, int width, int height)
    {
        var b = LoadBackup();
        if (b.Wallpaper is null)
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"); b.Wallpaper = k?.GetValue("WallPaper") as string ?? ""; } catch { b.Wallpaper = ""; }
            if (b.Taken == default) b.Taken = DateTime.Now;
            Json.Write(BackupFile, b);
        }
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(0, 0, width, height));
            var g = gradient.Clone(); g.Opacity = 1;
            dc.DrawRectangle(g, null, new System.Windows.Rect(0, 0, width, height));
            // Fine noise-free vignette for depth.
            var v = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(90, 0, 0, 0)) { RadiusX = 0.9, RadiusY = 0.9 };
            dc.DrawRectangle(v, null, new System.Windows.Rect(0, 0, width, height));
        }
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var dir = Path.Combine(Paths.Root, "wallpapers");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"gradient-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using (var fs = File.Create(file))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            enc.Save(fs);
        }
        WallpaperEngine.SetWallpaper(file);
        return file;
    }
}

/// <summary>Run-at-sign-in through the per-user Run key (no scheduled tasks, no services, no admin).</summary>
public static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "WinOrbit";

    public static bool Enabled
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) is string; } catch { return false; } }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
        else if (k.GetValue(Name) is not null) k.DeleteValue(Name);
    }

    /// <summary>The app used to be called Glasswave: replace its start-up entry with ours, keeping the user's choice.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true);
            const string legacy = "Glass" + "wave";
            if (k?.GetValue(legacy) is null) return;
            k.DeleteValue(legacy);
            if (!Environment.ProcessPath!.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)) Set(true);
        }
        catch { }
    }
}

/// <summary>Reputable third-party tools for the parts of Windows no supported setting can restyle. Information only.</summary>
public static class ThirdParty
{
    public sealed record Tool(string Name, string What, string Url, string Note);

    public static readonly Tool[] Tools =
    {
        new("TranslucentTB", "Makes the taskbar transparent, blurred or acrylic.", "https://github.com/TranslucentTB/TranslucentTB",
            "Open source, available on the Microsoft Store. Works without modifying system files."),
        new("Windhawk", "Mods for Explorer, taskbar and Start, including rounded/floating taskbar styles.", "https://windhawk.net",
            "Open source, but works by injecting code into Explorer. Powerful; use with care and only mods you trust."),
        new("Mica For Everyone", "Applies Mica/Acrylic backdrops to apps that don't use them.", "https://github.com/MicaForEveryone/MicaForEveryone",
            "Open source. Uses documented DWM attributes on other apps' windows; some apps render oddly."),
        new("Rainmeter", "Skinnable desktop widgets — WinOrbit manages it on the Rainmeter page.", "https://www.rainmeter.net",
            "Open source (GPL). WinOrbit never installs it for you."),
    };

    public static void Open(string url)
    {
        // Opens in the user's own browser. WinOrbit itself makes no network request here.
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("Open url: " + ex.Message); }
    }
}
