using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// Per-app volume: the same audio sessions and sliders the Windows volume mixer shows, read and set through the
/// documented Core Audio API (IAudioSessionManager2 / ISimpleAudioVolume) on the default output device. Nothing is
/// injected and nothing is stored; an app's volume lives in Windows, exactly as if it were set in the mixer.
/// Apps with several sessions (a browser's tabs) are grouped into one row. Call on the UI thread.
/// </summary>
public static class AppVolume
{
    /// <summary>One row of the mixer: an app (or "System sounds") and its volume 0..1.</summary>
    public sealed record App(string Key, string Name, string? ExePath, double Volume, bool Muted, bool Active, bool System);

    /// <summary>A raw audio session, before grouping (pure data, so grouping can be tested).</summary>
    internal sealed record Session(string Process, string? Name, string? ExePath, double Volume, bool Muted, int State, bool System);

    private const int StateActive = 1, StateExpired = 2;

    /// <summary>The apps with audio on the default output device: playing ones first, System sounds last.</summary>
    public static List<App> List()
    {
        var sessions = new List<Session>();
        Each((control, volume, pid, system) =>
        {
            control.GetState(out var state);
            volume.GetMasterVolume(out var level);
            volume.GetMute(out var muted);
            var (process, exe, description) = system ? ("", null, null) : ProcessOf(pid);
            sessions.Add(new Session(process, system ? null : description ?? DisplayName(control), exe, level, muted, state, system));
        });
        return Group(sessions);
    }

    /// <summary>Set an app's volume (0..1) — every session it has.</summary>
    public static void SetVolume(string key, double level)
    {
        var ctx = Guid.Empty;
        var v = (float)Math.Clamp(level, 0, 1);
        Each((_, volume, pid, system) => { if (Matches(key, pid, system)) volume.SetMasterVolume(v, ref ctx); });
    }

    public static void SetMute(string key, bool mute)
    {
        var ctx = Guid.Empty;
        Each((_, volume, pid, system) => { if (Matches(key, pid, system)) volume.SetMute(mute, ref ctx); });
    }

    /// <summary>
    /// Sessions → one row per app. Expired sessions are dropped; an app is "active" if any session is playing, and
    /// its slider shows the loudest session (they normally match). Pure, so it can be tested.
    /// </summary>
    internal static List<App> Group(IEnumerable<Session> sessions) =>
        sessions.Where(s => s.State != StateExpired && (s.System || s.Process.Length > 0))
            .GroupBy(s => s.System ? "system" : s.Process, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var name = first.System ? "System sounds" : g.Select(s => s.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? first.Process;
                return new App(g.Key, name, g.Select(s => s.ExePath).FirstOrDefault(p => p is not null), g.Max(s => s.Volume), g.All(s => s.Muted), g.Any(s => s.State == StateActive), first.System);
            })
            .OrderBy(a => a.System).ThenByDescending(a => a.Active).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    // ----- Core Audio plumbing -----

    private static bool Matches(string key, uint pid, bool system) =>
        system ? key == "system" : !string.IsNullOrEmpty(key) && string.Equals(ProcessOf(pid).Name, key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Visit every session on the default output device with its volume control; COM objects are released after.</summary>
    private static void Each(Action<IAudioSessionControl2, ISimpleAudioVolume, uint, bool> visit)
    {
        object? manager = null;
        IAudioSessionEnumerator? list = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return;   // eRender, eMultimedia
            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out manager) != 0) return;
            if (((IAudioSessionManager2)manager).GetSessionEnumerator(out list) != 0) return;
            list.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (list.GetSession(i, out var session) != 0 || session is null) continue;
                try
                {
                    if (session is IAudioSessionControl2 control && session is ISimpleAudioVolume volume)
                    {
                        control.GetProcessId(out var pid);
                        visit(control, volume, pid, control.IsSystemSoundsSession() == 0);
                    }
                }
                catch (Exception ex) { Log.Warn("Audio session: " + ex.Message); }
                finally { Marshal.ReleaseComObject(session); }
            }
        }
        catch (Exception ex) { Log.Warn("Per-app volume unavailable: " + ex.Message); }
        finally
        {
            if (list is not null) Marshal.ReleaseComObject(list);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
    }

    private static string? DisplayName(IAudioSessionControl2 control)
    {
        if (control.GetDisplayName(out var p) != 0 || p == IntPtr.Zero) return null;
        try
        {
            var s = Marshal.PtrToStringUni(p);
            return string.IsNullOrWhiteSpace(s) || s.StartsWith('@') ? null : s;   // "@%SystemRoot%\..." is a resource reference, not a name
        }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    private static readonly Dictionary<uint, (string Name, string? Exe, string? Description, DateTime At)> ProcessCache = new();

    /// <summary>
    /// Process name, exe path and friendly name ("Spotify", "Google Chrome") for a session's process — cached for
    /// 30 s, since the list refreshes while the mixer is open and process ids are reused once a process ends.
    /// </summary>
    private static (string Name, string? Exe, string? Description) ProcessOf(uint pid)
    {
        if (pid == 0) return ("", null, null);
        if (ProcessCache.TryGetValue(pid, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return (c.Name, c.Exe, c.Description);
        var name = "";
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { }
        var exe = ImagePath(pid);
        var description = Describe(exe);
        if (ProcessCache.Count > 200) ProcessCache.Clear();
        ProcessCache[pid] = (name, exe, description, DateTime.UtcNow);
        return (name, exe, description);
    }

    private static string? Describe(string? exePath)
    {
        if (exePath is null) return null;
        try { return FileVersionInfo.GetVersionInfo(exePath).FileDescription is { Length: > 0 } d ? d.Trim() : null; }
        catch { return null; }
    }

    // Asking only for "limited information" works for most apps, including many that run elevated.
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private static string? ImagePath(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
