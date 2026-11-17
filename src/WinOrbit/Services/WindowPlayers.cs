using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>A player found by its window rather than through the Windows media session.</summary>
public sealed record WindowPlayer(IntPtr Hwnd, int Pid, string App, string Title, string Artist, bool Playing);

/// <summary>
/// Some popular players don't report to the Windows media session (classic VLC 3 is the big one), so the island
/// would say "Nothing playing". For those, WinOrbit reads the track from the player's own window title, which
/// they all keep up to date ("Artist - Title - VLC media player"), and tells playing from paused by whether that
/// app is producing sound (its own level in the volume mixer). Title changes arrive as Windows events; the level
/// is checked once a second only while such a player is open. Controls use a media-key press, and only when it
/// is certain to reach that player (see <see cref="Control"/>).
/// </summary>
public sealed class WindowPlayers : IDisposable
{
    private sealed record Known(string Process, string App, string? Suffix, string? Prefix);

    private static readonly Known[] Players =
    {
        new("vlc", "VLC", " - VLC media player", null),
        new("foobar2000", "foobar2000", " [foobar2000]", null),
        new("winamp", "Winamp", " - Winamp", null),
        new("PotPlayerMini64", "PotPlayer", " - PotPlayer", null), new("PotPlayerMini", "PotPlayer", " - PotPlayer", null),
        new("mpc-hc64", "MPC-HC", null, null), new("mpc-hc", "MPC-HC", null, null), new("mpc-be64", "MPC-BE", null, null), new("mpc-be", "MPC-BE", null, null),
        new("mpv", "mpv", " - mpv", null),
    };

    public WindowPlayer? Current { get; private set; }
    /// <summary>Raised on the UI thread when the detected track or play state changes.</summary>
    public event Action? Changed;

    private readonly Dispatcher _ui;
    private readonly Func<int, double> _peak;
    private readonly Native.WinEventProc _proc;
    private IntPtr _hookName, _hookDestroy;
    private readonly Dictionary<int, Known?> _byPid = new();
    private readonly DispatcherTimer _levelTimer;
    private DateTime _lastSound = DateTime.MinValue;

    public WindowPlayers(Dispatcher ui, Func<int, double> peak)
    {
        _ui = ui;
        _peak = peak;
        _proc = OnEvent;
        _levelTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _levelTimer.Tick += (_, _) => CheckLevel();
    }

    public void Start()
    {
        const uint OutOfContext = 0x0000 | 0x0002;   // WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
        _hookName = Native.SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, _proc, 0, 0, OutOfContext);    // EVENT_OBJECT_NAMECHANGE
        _hookDestroy = Native.SetWinEventHook(0x8001, 0x8001, IntPtr.Zero, _proc, 0, 0, OutOfContext); // EVENT_OBJECT_DESTROY
        ScanAll();
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr l);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);

    private void ScanAll()
    {
        WindowPlayer? found = null;
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && Read(h) is { } p) { found = p; return false; }
            return true;
        }, IntPtr.Zero);
        Set(found);
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;   // window objects only
        if (evt == 0x8001) { if (Current is not null && hwnd == Current.Hwnd) ScanAll(); return; }
        if (GetAncestor(hwnd, 2) != hwnd) return;                           // top-level windows only (GA_ROOT)
        if (Current is not null && hwnd == Current.Hwnd) { Set(Read(hwnd)); return; }
        if (Read(hwnd) is { } p) Set(p);
    }

    private Known? PlayerFor(IntPtr hwnd, out int pid)
    {
        Native.GetWindowThreadProcessId(hwnd, out var upid);
        pid = (int)upid;
        if (_byPid.TryGetValue(pid, out var known)) return known;
        try
        {
            using var proc = Process.GetProcessById(pid);
            var name = proc.ProcessName;
            known = Players.FirstOrDefault(k => k.Process.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch { known = null; }
        if (_byPid.Count > 500) _byPid.Clear();
        _byPid[pid] = known;
        return known;
    }

    private WindowPlayer? Read(IntPtr hwnd)
    {
        var known = PlayerFor(hwnd, out var pid);
        if (known is null) return null;
        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        var parsed = ParseTitle(sb.ToString(), known.Suffix, known.App);
        if (parsed is null) return null;
        return new WindowPlayer(hwnd, pid, known.App, parsed.Value.Title, parsed.Value.Artist, Current?.Hwnd == hwnd && Current.Playing);
    }

    /// <summary>
    /// "Artist - Title - VLC media player" → (Title, Artist). Returns null when the player shows no track
    /// (just its own name). File names lose their extension. Pure and unit-tested.
    /// </summary>
    internal static (string Title, string Artist)? ParseTitle(string window, string? suffix, string app)
    {
        var t = window.Trim();
        if (t.Length == 0) return null;
        if (suffix is not null)
        {
            if (!t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;   // idle: "VLC media player"
            t = t[..^suffix.Length].Trim();
        }
        if (t.Length == 0 || t.Equals(app, StringComparison.OrdinalIgnoreCase)) return null;
        // Winamp numbers the playlist entry: "12. Artist - Title"
        var dot = t.IndexOf(". ", StringComparison.Ordinal);
        if (dot is > 0 and < 5 && t[..dot].All(char.IsDigit)) t = t[(dot + 2)..];
        var ext = Path.GetExtension(t);
        if (ext.Length is >= 3 and <= 5 && ext[1..].All(char.IsLetterOrDigit)) t = t[..^ext.Length];
        var sep = t.IndexOf(" - ", StringComparison.Ordinal);
        return sep > 0 ? (t[(sep + 3)..].Trim(), t[..sep].Trim()) : (t, "");
    }

    private void Set(WindowPlayer? p)
    {
        var old = Current;
        if (p is not null && (old is null || old.Hwnd != p.Hwnd || old.Title != p.Title)) _lastSound = DateTime.MinValue;
        Current = p is null ? null : p with { Playing = Playing(p) };
        if (Current is null) _levelTimer.Stop(); else if (!_levelTimer.IsEnabled) _levelTimer.Start();
        if (!Equals(old, Current)) Changed?.Invoke();
    }

    private bool Playing(WindowPlayer p)
    {
        if (_peak(p.Pid) > 0.0005) _lastSound = DateTime.UtcNow;
        return DateTime.UtcNow - _lastSound < TimeSpan.FromSeconds(2.5);   // brief quiet passages don't count as paused
    }

    private void CheckLevel()
    {
        if (Current is null) { _levelTimer.Stop(); return; }
        var playing = Playing(Current);
        if (playing != Current.Playing) { Current = Current with { Playing = playing }; Changed?.Invoke(); }
    }

    // ------------------------------------------------------------------ controls
    // Never post WM_APPCOMMAND or keystrokes to these windows: VLC 3.0.23 crashes on a posted WM_APPCOMMAND
    // (reproduced twice). The safe route is a real media-key press, but Windows hands media keys to whichever
    // app owns them, so it is only used when it will reach this player.

    /// <summary>
    /// Press a media key for the window player. Returns null when sent, otherwise a short explanation of why
    /// it can't be controlled safely (shown in the island).
    /// </summary>
    public string? Control(byte mediaKey, bool otherSessionExists)
    {
        if (Current is null) return null;
        if (Current.App == "VLC")
        {
            if (!VlcHasGlobalMediaKeys()) return "VLC only takes outside controls with global media keys: in VLC, Tools › Preferences › Hotkeys, set the Global column for Play/Pause, Next and Previous";
        }
        else if (otherSessionExists) return $"{Current.App} can't be controlled from outside while another app is also playing media";
        Native.keybd_event(mediaKey, 0, 1, UIntPtr.Zero);
        Native.keybd_event(mediaKey, 0, 3, UIntPtr.Zero);
        _levelTimer.Stop(); _levelTimer.Start();
        return null;
    }

    private static DateTime _vlcrcStamp;
    private static bool _vlcGlobal;

    /// <summary>VLC claims the media keys system-wide only when its global hotkeys are set (off by default).</summary>
    internal static bool VlcHasGlobalMediaKeys()
    {
        var rc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlcrc");
        try
        {
            var stamp = File.GetLastWriteTimeUtc(rc);
            if (stamp == _vlcrcStamp) return _vlcGlobal;
            _vlcrcStamp = stamp;
            _vlcGlobal = File.Exists(rc) && GlobalKeysSet(File.ReadAllLines(rc));
        }
        catch { _vlcGlobal = false; }
        return _vlcGlobal;
    }

    internal static bool GlobalKeysSet(IEnumerable<string> vlcrc) =>
        vlcrc.Any(l => l.StartsWith("global-key-play-pause=", StringComparison.Ordinal) && l.Contains("Media", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (_hookName != IntPtr.Zero) Native.UnhookWinEvent(_hookName);
        if (_hookDestroy != IntPtr.Zero) Native.UnhookWinEvent(_hookDestroy);
        _levelTimer.Stop();
    }
}
