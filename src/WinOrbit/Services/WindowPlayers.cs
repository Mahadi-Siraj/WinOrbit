using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>A player found by its window rather than through the Windows media session.</summary>
public sealed record WindowPlayer(IntPtr Hwnd, int Pid, string App, string Title, string Artist, bool Playing)
{
    /// <summary>The file being played, when the player tells (classic Media Player) — used for album art.</summary>
    public string? File { get; init; }
    public string Album { get; init; } = "";
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public bool CanSeek { get; init; }
}

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
        new("wmplayer", ClassicName, null, null),   // read through its remoting interface, not its title
    };

    public const string ClassicName = "Windows Media Player";

    public WindowPlayer? Current { get; private set; }
    /// <summary>Raised on the UI thread when the detected track or play state changes.</summary>
    public event Action? Changed;

    private readonly Dispatcher _ui;
    private readonly Func<int, double> _peak;
    private readonly Native.WinEventProc _proc;
    private IntPtr _hookName, _hookDestroy, _hookShow;
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
        _hookShow = Native.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, _proc, 0, 0, OutOfContext);    // EVENT_OBJECT_SHOW (classic Media Player opening)
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
        if (evt == 0x8001) { if (Current is not null && hwnd == Current.Hwnd) { if (Current.App == ClassicName) _wmpHwnd = IntPtr.Zero; ScanAll(); } return; }
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
        if (known.App == ClassicName) return ReadClassic(hwnd, pid);
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
        Current = p is null ? null : p.App == ClassicName ? p : p with { Playing = Playing(p) };
        if (Current is null && _wmpPid == 0) _levelTimer.Stop(); else if (!_levelTimer.IsEnabled) _levelTimer.Start();
        if (!Same(old, Current)) Changed?.Invoke();
    }

    /// <summary>Equal apart from the playhead moving on as expected (a jump — a seek — counts as a change).</summary>
    internal static bool Same(WindowPlayer? a, WindowPlayer? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a with { Position = default } != b with { Position = default }) return false;
        if (a.Duration <= TimeSpan.Zero) return true;
        var expected = a.Playing ? a.Position + TimeSpan.FromSeconds(1) : a.Position;
        return Math.Abs((b.Position - expected).TotalSeconds) < 2.5;
    }

    // ------------------------------------------------------------------ classic Windows Media Player
    private ClassicMediaPlayer? _wmp;
    private int _wmpPid;
    private IntPtr _wmpHwnd;
    private bool _wmpShown;
    private DateTime _wmpSince;
    private DateTime _wmpRetryAt;

    private WindowPlayer? ReadClassic(IntPtr hwnd, int pid)
    {
        if (_wmpPid != pid)
        {
            _wmp?.Dispose(); _wmp = new ClassicMediaPlayer(); _wmpPid = pid; _wmpRetryAt = default;
            _wmpShown = false; _wmpSince = DateTime.UtcNow;
        }
        if (!_levelTimer.IsEnabled) _levelTimer.Start();   // keeps checking while it starts up and while it's open
        if (IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0) _wmpHwnd = hwnd;
        if (!HasVisibleWindow(pid)) return null;            // still starting (or closing): never link to a windowless player
        _wmpShown = true;
        if (_wmp is null) return null;
        if (!_wmp.Connected)
        {
            if (DateTime.UtcNow < _wmpRetryAt) return null;
            _wmpRetryAt = DateTime.UtcNow.AddSeconds(30);   // if the link helper fails, don't keep restarting it
            if (!_wmp.Connect(pid)) return null;
            Log.Info("Linking to classic Windows Media Player");
        }
        if (_wmp.Read() is not { } t) return null;
        return new WindowPlayer(_wmpHwnd != IntPtr.Zero ? _wmpHwnd : hwnd, pid, ClassicName, t.Title, t.Artist, t.Playing)
        {
            File = t.File, Album = t.Album, Position = t.Position, Duration = t.Duration, CanSeek = t.Duration > TimeSpan.Zero,
        };
    }

    /// <summary>
    /// The player counts as open while it shows a window. Once the user closes it, the link is released at
    /// once — otherwise our connection would keep wmplayer.exe running invisibly in the background.
    /// </summary>
    private bool ClassicStillOpen()
    {
        if (_wmpPid == 0) return false;
        var alive = false;
        try { using var p = Process.GetProcessById(_wmpPid); alive = !p.HasExited; } catch { }
        if (alive && HasVisibleWindow(_wmpPid)) return true;
        if (alive && !_wmpShown && DateTime.UtcNow - _wmpSince < TimeSpan.FromSeconds(20)) return true;   // window not up yet
        _wmp?.Dispose(); _wmp = null; _wmpPid = 0; _wmpHwnd = IntPtr.Zero;
        return false;
    }

    internal static bool HasVisibleWindow(int pid)
    {
        var found = false;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            Native.GetWindowThreadProcessId(h, out var p);
            if ((int)p != pid || GetWindowTextLength(h) == 0) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);

    public bool Seek(TimeSpan position)
    {
        if (Current?.App != ClassicName || _wmp is null) return false;
        var ok = _wmp.Seek(position);
        Soon();
        return ok;
    }

    private DispatcherTimer? _soon;
    /// <summary>Re-read shortly after a control so the island reflects it without waiting for the next tick.</summary>
    private void Soon()
    {
        _soon ??= new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(350) };
        _soon.Tick -= SoonTick; _soon.Tick += SoonTick;
        _soon.Stop(); _soon.Start();
    }
    private void SoonTick(object? s, EventArgs e) { _soon?.Stop(); CheckLevel(); }

    private bool Playing(WindowPlayer p)
    {
        if (_peak(p.Pid) > 0.0005) _lastSound = DateTime.UtcNow;
        return DateTime.UtcNow - _lastSound < TimeSpan.FromSeconds(2.5);   // brief quiet passages don't count as paused
    }

    private void CheckLevel()
    {
        if (_wmpPid != 0)
        {
            if (!ClassicStillOpen()) { if (Current?.App == ClassicName) ScanAll(); else if (Current is null) _levelTimer.Stop(); return; }
            // Classic Media Player has no change events we can receive here: read it once a second while open.
            if (Current is null || Current.App == ClassicName) { Set(ReadClassic(_wmpHwnd, _wmpPid)); return; }
        }
        if (Current is null) { _levelTimer.Stop(); return; }
        var playing = Playing(Current);
        if (playing != Current.Playing) { Current = Current with { Playing = playing }; Changed?.Invoke(); }
    }

    // ------------------------------------------------------------------ album art for VLC
    /// <summary>
    /// The file VLC is playing, from its own recent-files list (newest first), matched against the title VLC
    /// shows so a stale entry is never used. Null when it can't be matched.
    /// </summary>
    public static string? VlcFile(string title)
    {
        try
        {
            var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlc-qt-interface.ini");
            if (!File.Exists(ini)) return null;
            var line = File.ReadLines(ini).SkipWhile(l => l.Trim() != "[RecentsMRL]").Skip(1).FirstOrDefault(l => l.StartsWith("list=", StringComparison.Ordinal));
            if (line is null) return null;
            foreach (var mrl in line[5..].Split(", ").Take(5))
            {
                if (!Uri.TryCreate(mrl.Trim(), UriKind.Absolute, out var uri) || !uri.IsFile) continue;
                var path = uri.LocalPath;
                if (File.Exists(path) && NameMatches(Path.GetFileNameWithoutExtension(path), title)) return path;
            }
        }
        catch { }
        return null;
    }

    internal static bool NameMatches(string fileName, string title)
    {
        static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var f = Norm(fileName); var t = Norm(title);
        return t.Length >= 3 && f.Contains(t) || t.Length > 0 && t.Contains(f) && f.Length >= 3;
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
        if (Current.App == ClassicName && _wmp is not null)
        {
            if (mediaKey == Native.VK_MEDIA_PLAY_PAUSE) _wmp.PlayPause();
            else if (mediaKey == Native.VK_MEDIA_NEXT_TRACK) _wmp.Next();
            else if (mediaKey == Native.VK_MEDIA_PREV_TRACK) _wmp.Previous();
            Soon();
            return null;
        }
        if (Current.App == "VLC")
        {
            if (!VlcHasGlobalMediaKeys()) return "Turn on VLC's global media keys to control it from here: in VLC, Tools › Preferences › Hotkeys, set the Global column for Play/Pause, Next and Previous to your media keys, then Save";
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
        if (_hookShow != IntPtr.Zero) Native.UnhookWinEvent(_hookShow);
        _wmp?.Dispose();
        _levelTimer.Stop();
    }
}
