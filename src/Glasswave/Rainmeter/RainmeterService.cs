using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Glasswave.Core;
using Microsoft.Win32;

namespace Glasswave.Rainmeter;

/// <summary>A skin config folder (e.g. "illustro\Clock") and its .ini variants.</summary>
public sealed class RmSkin
{
    public string Config { get; init; } = "";          // relative to Skins, backslash separated
    public string Root => Config.Split('\\')[0];
    public string Folder { get; init; } = "";
    public List<string> Variants { get; init; } = new();
    public bool Active { get; set; }
    public string ActiveVariant { get; set; } = "";
    public int? X { get; set; }
    public int? Y { get; set; }
    public int AlwaysOnTop { get; set; }
    public int Alpha { get; set; } = 255;
    public bool ClickThrough { get; set; }
    public bool Draggable { get; set; } = true;

    // [Metadata]
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Information { get; set; } = "";
    public string Version { get; set; } = "";
    public string License { get; set; } = "";
    public string? Preview { get; set; }
    /// <summary>Plugins that do not ship with Rainmeter (third-party native DLLs).</summary>
    public List<string> ThirdPartyPlugins { get; } = new();
    /// <summary>Programs/scripts the skin can launch (click actions, !Execute, RunCommand).</summary>
    public List<string> Launches { get; } = new();
    public bool UsesPlugins => ThirdPartyPlugins.Count > 0;
    public bool RunsCommands => Launches.Count > 0;
    public string? LoadError { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Config.Split('\\').Last() : Name;
    public string Category => Root;
}

public sealed class RmPackageInfo
{
    public string Path = "";
    public string Name = "", Author = "", Version = "", MinimumRainmeter = "", LoadType = "", Load = "";
    public List<string> Skins = new();
    public List<string> Plugins = new();
    public List<string> Layouts = new();
    public bool HasFooter;
    public bool Valid;
    public string? Error;
}

/// <summary>
/// Integration with an installed Rainmeter. Rainmeter itself runs every skin; Glasswave only:
///   • reads Rainmeter.ini and the Skins folder (read-only),
///   • sends documented bangs to Rainmeter.exe (!ActivateConfig, !DeactivateConfig, !Refresh, !RefreshApp, !Move, !ZPos, !SetTransparency …),
///   • hands .rmskin packages to Rainmeter's own SkinInstaller.exe after showing what is inside.
/// It never edits, moves or deletes skin files.
/// </summary>
public sealed class RainmeterService
{
    public bool Installed => File.Exists(ExePath);
    public string ExePath { get; private set; } = "";
    public string ProgramDir => Path.GetDirectoryName(ExePath) ?? "";
    public string SettingsPath { get; private set; } = "";
    public string SkinsPath { get; private set; } = "";
    public string Version { get; private set; } = "";
    public bool Portable { get; private set; }
    public List<RmSkin> Skins { get; private set; } = new();

    private readonly Func<RainmeterPrefs> _prefs;
    public RainmeterService(Func<RainmeterPrefs> prefs) => _prefs = prefs;

    public static bool IsRunning => Process.GetProcessesByName("Rainmeter").Length > 0;

    /// <summary>
    /// Whether Glasswave should offer its Rainmeter page and Home card. "auto" shows them only when Rainmeter
    /// is installed or running, so people who don't use Rainmeter never see it.
    /// </summary>
    public bool PageWanted()
    {
        switch (_prefs().PageVisibility)
        {
            case "always": return true;
            case "never": return false;
        }
        if (!Installed) Detect();
        return Installed || IsRunning;
    }

    public void Detect()
    {
        ExePath = "";
        var candidates = new List<string>();
        var custom = _prefs().CustomExePath;
        if (!string.IsNullOrWhiteSpace(custom)) candidates.Add(custom);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var key in new[] { @"SOFTWARE\Rainmeter", @"SOFTWARE\WOW6432Node\Rainmeter" })
        {
            try { if (hive.OpenSubKey(key)?.GetValue(null) is string dir) candidates.Add(Path.Combine(dir, "Rainmeter.exe")); } catch { }
        }
        try
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var u = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Rainmeter");
                if (u?.GetValue("InstallLocation") is string loc) candidates.Add(Path.Combine(loc, "Rainmeter.exe"));
                if (u?.GetValue("DisplayIcon") is string icon) candidates.Add(icon.Trim('"').Split(',')[0]);
            }
        }
        catch { }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rainmeter", "Rainmeter.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Rainmeter", "Rainmeter.exe"));
        try
        {
            foreach (var p in Process.GetProcessesByName("Rainmeter"))
                using (p) { try { if (p.MainModule?.FileName is { } f) candidates.Add(f); } catch { } }
        }
        catch { }

        ExePath = candidates.FirstOrDefault(File.Exists) ?? "";
        if (!Installed) { SettingsPath = SkinsPath = Version = ""; Skins = new(); return; }
        try { Version = FileVersionInfo.GetVersionInfo(ExePath).ProductVersion ?? ""; } catch { }

        // Portable installs keep Rainmeter.ini beside the exe; normal ones under %APPDATA%\Rainmeter.
        var portableIni = Path.Combine(ProgramDir, "Rainmeter.ini");
        Portable = File.Exists(portableIni);
        SettingsPath = Portable ? portableIni : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rainmeter", "Rainmeter.ini");

        var ini = Ini.Load(SettingsPath);
        var skinPath = ini.Get("Rainmeter", "SkinPath");
        SkinsPath = !string.IsNullOrWhiteSpace(skinPath) ? Environment.ExpandEnvironmentVariables(skinPath)
            : Portable ? Path.Combine(ProgramDir, "Skins")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rainmeter", "Skins");
    }

    /// <summary>Scan the Skins folder and merge state from Rainmeter.ini. Read-only.</summary>
    public void Scan()
    {
        var list = new List<RmSkin>();
        if (!Directory.Exists(SkinsPath)) { Skins = list; return; }
        var ini = Ini.Load(SettingsPath);
        foreach (var dir in SafeDirs(SkinsPath, 0))
        {
            var rel = Path.GetRelativePath(SkinsPath, dir);
            if (rel.StartsWith("@") || rel.Split('\\').Any(p => p.StartsWith("@"))) continue;    // @Resources etc.
            var inis = SafeFiles(dir, "*.ini");
            if (inis.Count == 0) continue;
            var skin = new RmSkin { Config = rel, Folder = dir, Variants = inis.Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() };

            var active = ini.GetInt(rel, "Active", 0);
            skin.Active = active > 0 && active <= skin.Variants.Count;
            if (skin.Active) skin.ActiveVariant = skin.Variants[active - 1];
            skin.X = ini.TryInt(rel, "WindowX");
            skin.Y = ini.TryInt(rel, "WindowY");
            skin.AlwaysOnTop = ini.GetInt(rel, "AlwaysOnTop", 0);
            skin.Alpha = ini.GetInt(rel, "AlphaValue", 255);
            skin.ClickThrough = ini.GetInt(rel, "ClickThrough", 0) == 1;
            skin.Draggable = ini.GetInt(rel, "Draggable", 1) == 1;

            ReadMetadata(skin, Path.Combine(dir, skin.Active ? skin.ActiveVariant : skin.Variants[0]));
            skin.Preview = FindPreview(dir);
            list.Add(skin);
        }
        Skins = list.OrderBy(s => s.Config, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> SafeDirs(string root, int depth)
    {
        if (depth > 5) yield break;
        string[] dirs;
        try { dirs = Directory.GetDirectories(root); } catch { yield break; }
        foreach (var d in dirs)
        {
            yield return d;
            foreach (var sub in SafeDirs(d, depth + 1)) yield return sub;
        }
    }

    private static List<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).ToList(); } catch { return new(); }
    }

    private static void ReadMetadata(RmSkin skin, string iniPath)
    {
        try
        {
            var ini = Ini.Load(iniPath);
            skin.Name = ini.Get("Metadata", "Name");
            skin.Author = ini.Get("Metadata", "Author");
            skin.Information = ini.Get("Metadata", "Information").Replace("|", "\n");
            skin.Version = ini.Get("Metadata", "Version");
            skin.License = ini.Get("Metadata", "License");
            foreach (var section in ini.Sections)
            {
                var plugin = PluginName(ini.Get(section, "Plugin"));
                if (plugin.Length > 0)
                {
                    if (plugin.Equals("RunCommand", StringComparison.OrdinalIgnoreCase))
                        AddOnce(skin.Launches, ini.Get(section, "Program") is { Length: > 0 } prog ? prog : "a command (RunCommand)");
                    else if (!BundledPlugins.Contains(plugin)) AddOnce(skin.ThirdPartyPlugins, plugin);
                }
                foreach (var v in ini.Values(section))
                    foreach (var target in LaunchTargets(v)) AddOnce(skin.Launches, target);
            }
        }
        catch (Exception ex) { skin.LoadError = "Could not read skin file: " + ex.Message; }
    }

    /// <summary>Plugins installed with Rainmeter 4.5 itself; these are not third-party code.</summary>
    internal static readonly HashSet<string> BundledPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "ActionTimer", "AdvancedCPU", "AudioLevel", "CoreTemp", "FileView", "FolderInfo", "InputText", "iTunesPlugin",
        "PerfMon", "PingPlugin", "PowerPlugin", "QuotePlugin", "ResMon", "RunCommand", "SpeedFanPlugin", "UsageMonitor",
        "Win7AudioPlugin", "WindowMessagePlugin", "WebParser", "NowPlaying", "SysInfo", "WifiStatus", "MediaKey", "RecycleManager",
    };

    private static readonly string[] ProgramExtensions = { ".exe", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".msi", ".lnk", ".scr", ".pif", ".hta" };

    internal static string PluginName(string value)
    {
        var v = value.Trim().Trim('"');
        if (v.Length == 0) return "";
        return Path.GetFileNameWithoutExtension(v.Replace('/', '\\').Split('\\').Last());
    }

    /// <summary>
    /// Targets of ["..."] actions and !Execute that start a program or script. Links (http/https/mailto) and
    /// folders (e.g. illustro's ["C:\"]) only open in Explorer or the browser, so they are not reported.
    /// </summary>
    internal static IEnumerable<string> LaunchTargets(string value)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(value, "\\[\"([^\"]+)\""))
        {
            var t = m.Groups[1].Value.Trim();
            if (t.Length == 0 || t.StartsWith('!')) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, "^(https?|mailto|ftp):", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            var file = t.Split(' ')[0];
            var lower = t.ToLowerInvariant();
            if (ProgramExtensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase) || lower.Contains(e + " ") || lower.EndsWith(e)) ||
                lower.StartsWith("cmd") || lower.StartsWith("powershell") || lower.StartsWith("pwsh") || lower.StartsWith("wscript") || lower.StartsWith("cscript") || lower.StartsWith("rundll32"))
                yield return t;
        }
    }

    private static void AddOnce(List<string> list, string item)
    {
        if (!list.Contains(item, StringComparer.OrdinalIgnoreCase)) list.Add(item);
    }

    private static string? FindPreview(string dir)
    {
        string[] names = { "preview.png", "preview.jpg", "screenshot.png", "screenshot.jpg", "thumb.png", "thumbnail.png" };
        foreach (var n in names) { var p = Path.Combine(dir, n); if (File.Exists(p)) return p; }
        var root = dir;
        for (var i = 0; i < 4 && root is not null; i++)
        {
            foreach (var n in names) { var p = Path.Combine(root, "@Resources", n); if (File.Exists(p)) return p; }
            foreach (var n in names) { var p = Path.Combine(root, "@Resources", "Images", n); if (File.Exists(p)) return p; }
            root = Path.GetDirectoryName(root);
        }
        return null;
    }

    // ----- bangs -----
    /// <summary>Run a documented Rainmeter bang through Rainmeter.exe's command line.</summary>
    public bool Bang(string bang, params string[] args)
    {
        if (!Installed) return false;
        try
        {
            var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(bang);
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            Log.Info($"Rainmeter {bang} {string.Join(' ', args)}");
            return true;
        }
        catch (Exception ex) { Log.Error("Rainmeter bang failed", ex); return false; }
    }

    public void Activate(RmSkin s, string? variant = null) => Bang("!ActivateConfig", s.Config, variant ?? (s.ActiveVariant.Length > 0 ? s.ActiveVariant : s.Variants[0]));
    public void Deactivate(RmSkin s) => Bang("!DeactivateConfig", s.Config);
    public void Refresh(RmSkin s) => Bang("!Refresh", s.Config);
    public void RefreshAll() => Bang("!RefreshApp");
    public void Move(RmSkin s, int x, int y) => Bang("!Move", x.ToString(), y.ToString(), s.Config);
    public void ZPos(RmSkin s, int pos) => Bang("!ZPos", pos.ToString(), s.Config);
    public void SetAlpha(RmSkin s, int alpha) => Bang("!SetTransparency", Math.Clamp(alpha, 0, 255).ToString(), s.Config);
    public void SetClickThrough(RmSkin s, bool on) => Bang("!ClickThrough", on ? "1" : "0", s.Config);
    public void SetDraggable(RmSkin s, bool on) => Bang("!Draggable", on ? "1" : "0", s.Config);
    public void Manage(RmSkin? s) { if (s is null) Bang("!Manage"); else Bang("!Manage", "Skins", s.Config, s.ActiveVariant.Length > 0 ? s.ActiveVariant : s.Variants[0]); }

    /// <summary>
    /// The skin's real on-screen rectangle (physical pixels), read from Rainmeter's own window. Rainmeter.ini often
    /// stores positions as formulas such as (#SCREENAREAWIDTH#-200), so the saved values can't be used directly.
    /// Rainmeter titles each skin window with the full path of the loaded .ini file.
    /// </summary>
    public Interop.Native.RECT? LiveBounds(RmSkin s)
    {
        if (!s.Active) return null;
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("Rainmeter")) using (p) pids.Add((uint)p.Id);
        if (pids.Count == 0) return null;
        var prefix = s.Folder.TrimEnd('\\') + "\\";
        Interop.Native.RECT? found = null;
        Interop.Native.EnumWindows((h, _) =>
        {
            Interop.Native.GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            var cls = new StringBuilder(64);
            Interop.Native.GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() != "RainmeterMeterWindow") return true;
            var title = new StringBuilder(1024);
            Interop.Native.GetWindowText(h, title, title.Capacity);
            var t = title.ToString();
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || t.IndexOf('\\', prefix.Length) >= 0) return true;
            if (Interop.Native.GetWindowRect(h, out var r)) { found = r; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public void Start()
    {
        if (!Installed || IsRunning) return;
        try { Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true }); } catch (Exception ex) { Log.Error("Start Rainmeter", ex); }
    }

    public void OpenSkinsFolder()
    {
        if (Directory.Exists(SkinsPath)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SkinsPath}\"") { UseShellExecute = true });
    }

    // ----- .rmskin -----
    /// <summary>
    /// Inspect a package without installing it. An .rmskin is a zip with an RMSKIN.ini and an 16-byte
    /// footer (size + "RMSKIN" magic) appended; .NET's zip reader handles the trailing footer.
    /// </summary>
    public static RmPackageInfo Inspect(string path)
    {
        var info = new RmPackageInfo { Path = path };
        try
        {
            using (var fs = File.OpenRead(path))
            {
                if (fs.Length > 16)
                {
                    fs.Seek(-16, SeekOrigin.End);
                    var tail = new byte[16];
                    fs.ReadExactly(tail);
                    info.HasFooter = Encoding.ASCII.GetString(tail, 9, 6) == "RMSKIN";
                }
            }
            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Select(e => e.FullName.Replace('/', '\\')).ToList();
            var manifest = zip.Entries.FirstOrDefault(e => e.FullName.Equals("RMSKIN.ini", StringComparison.OrdinalIgnoreCase));
            if (manifest is not null)
            {
                using var reader = new StreamReader(manifest.Open());
                var ini = Ini.Parse(reader.ReadToEnd());
                info.Name = ini.Get("rmskin", "Name");
                info.Author = ini.Get("rmskin", "Author");
                info.Version = ini.Get("rmskin", "Version");
                info.MinimumRainmeter = ini.Get("rmskin", "MinimumRainmeter");
                info.LoadType = ini.Get("rmskin", "LoadType");
                info.Load = ini.Get("rmskin", "Load");
            }
            info.Skins = entries.Where(e => e.StartsWith("Skins\\", StringComparison.OrdinalIgnoreCase)).Select(e => e.Split('\\')).Where(p => p.Length > 2).Select(p => p[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            info.Plugins = entries.Where(e => e.StartsWith("Plugins\\", StringComparison.OrdinalIgnoreCase) && e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            info.Layouts = entries.Where(e => e.StartsWith("Layouts\\", StringComparison.OrdinalIgnoreCase)).Select(e => e.Split('\\')).Where(p => p.Length > 2).Select(p => p[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            info.Valid = manifest is not null && (info.Skins.Count > 0 || info.Layouts.Count > 0);
            if (!info.Valid) info.Error = manifest is null ? "This file has no RMSKIN.ini, so it is not a Rainmeter skin package." : "The package contains no skins or layouts.";
        }
        catch (Exception ex) { info.Error = "The file could not be read as a Rainmeter package: " + ex.Message; }
        return info;
    }

    /// <summary>Hand the package to Rainmeter's own installer, which shows its own confirmation and backs up replaced skins.</summary>
    public bool Install(string rmskinPath)
    {
        var installer = Path.Combine(ProgramDir, "SkinInstaller.exe");
        if (!File.Exists(installer)) return false;
        try { Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, ArgumentList = { rmskinPath } }); return true; }
        catch (Exception ex) { Log.Error("SkinInstaller", ex); return false; }
    }
}

/// <summary>Tolerant INI reader (Rainmeter allows duplicate keys, comments and BOMs). Never writes.</summary>
public sealed class Ini
{
    private readonly Dictionary<string, Dictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<string> Sections => _data.Keys;

    public static Ini Load(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : new Ini(); }
        catch { return new Ini(); }
    }

    public static Ini Parse(string text)
    {
        var ini = new Ini();
        Dictionary<string, string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!ini._data.TryGetValue(name, out current)) ini._data[name] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null) continue;
            var key = line[..eq].Trim();
            if (!current.ContainsKey(key)) current[key] = line[(eq + 1)..].Trim().Trim('"');
        }
        return ini;
    }

    public string Get(string section, string key) => _data.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : "";
    public IEnumerable<string> Values(string section) => _data.TryGetValue(section, out var s) ? s.Values : Enumerable.Empty<string>();
    public int GetInt(string section, string key, int fallback) => int.TryParse(Get(section, key), out var v) ? v : fallback;
    public int? TryInt(string section, string key) => int.TryParse(Get(section, key), out var v) ? v : null;
}
