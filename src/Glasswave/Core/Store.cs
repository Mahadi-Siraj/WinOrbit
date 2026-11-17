using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Glasswave.Core;

/// <summary>Where Glasswave keeps its files. Portable mode (a "portable.flag" beside the exe) keeps everything in .\Data.</summary>
public static class Paths
{
    public static string ExeDir { get; } = AppContext.BaseDirectory;
    public static bool Portable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));

    public static string Root { get; private set; } = Portable
        ? Path.Combine(AppContext.BaseDirectory, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glasswave");

    /// <summary>Tests redirect storage to a scratch folder.</summary>
    internal static void OverrideRoot(string path) => Root = path;

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Data => Path.Combine(Root, "data.json");
    public static string Themes => Path.Combine(Root, "themes");
    public static string Profiles => Path.Combine(Root, "profiles");
    public static string Backups => Path.Combine(Root, "backups");
    public static string Logs => Path.Combine(Root, "logs");

    public static void Ensure()
    {
        foreach (var d in new[] { Root, Themes, Profiles, Backups, Logs }) Directory.CreateDirectory(d);
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read {Path.GetFileName(path)}: {ex.Message}");
            // Keep the unreadable file so nothing is lost silently.
            try { File.Copy(path, path + ".corrupt", true); } catch { }
            return null;
        }
    }

    /// <summary>Atomic write: a crash mid-save can never leave a half-written settings file.</summary>
    public static void Write<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, true);
        }
        catch (Exception ex) { Log.Error($"Could not write {Path.GetFileName(path)}", ex); }
    }

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

/// <summary>Minimal local log file. Never uploaded anywhere; rotates at 512 KB.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string File_ => Path.Combine(Paths.Logs, "glasswave.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.Logs);
                var f = new FileInfo(File_);
                if (f.Exists && f.Length > 512 * 1024) File.Move(File_, File_ + ".old", true);
                File.AppendAllText(File_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }

    public static string Tail(int lines = 60)
    {
        try { return File.Exists(File_) ? string.Join(Environment.NewLine, File.ReadLines(File_).TakeLast(lines)) : "(log is empty)"; }
        catch { return "(log unavailable)"; }
    }
}

/// <summary>Loads and saves settings and user content. Saves are debounced so slider drags cost one write.</summary>
public sealed class Store
{
    public AppSettings Settings { get; private set; } = new();
    public UserData Data { get; private set; } = new();
    public bool IsFirstRun { get; private set; }

    private DispatcherTimer? _timer;
    private bool _dirtySettings, _dirtyData;

    public void Load()
    {
        Paths.Ensure();
        var s = Json.Read<AppSettings>(Paths.Settings);
        IsFirstRun = s is null;
        Settings = s ?? new AppSettings();
        Data = Json.Read<UserData>(Paths.Data) ?? new UserData();
    }

    public void Save() { _dirtySettings = true; Arm(); }
    public void SaveData() { _dirtyData = true; Arm(); }

    private void Arm()
    {
        if (_timer is null)
        {
            if (Dispatcher.FromThread(Thread.CurrentThread) is null) { Flush(); return; }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _timer.Tick += (_, _) => Flush();
        }
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        _timer?.Stop();
        if (_dirtySettings) { Json.Write(Paths.Settings, Settings); _dirtySettings = false; }
        if (_dirtyData) { Json.Write(Paths.Data, Data); _dirtyData = false; }
    }

    // ----- themes & profiles on disk (one file each, so they are easy to share) -----
    public List<Theme> LoadUserThemes() => LoadAll<Theme>(Paths.Themes);
    public List<Profile> LoadUserProfiles() => LoadAll<Profile>(Paths.Profiles);

    private static List<T> LoadAll<T>(string dir) where T : class
    {
        var list = new List<T>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
            if (Json.Read<T>(f) is { } item) list.Add(item);
        return list;
    }

    public void SaveTheme(Theme t) => Json.Write(Path.Combine(Paths.Themes, Safe(t.Id) + ".json"), t);
    public void DeleteTheme(Theme t) => TryDelete(Path.Combine(Paths.Themes, Safe(t.Id) + ".json"));
    public void SaveProfile(Profile p) => Json.Write(Path.Combine(Paths.Profiles, Safe(p.Id) + ".json"), p);
    public void DeleteProfile(Profile p) => TryDelete(Path.Combine(Paths.Profiles, Safe(p.Id) + ".json"));

    private static string Safe(string id) => string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { Log.Error("Delete failed", ex); } }
}
