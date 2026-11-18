using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>What classic Windows Media Player is doing right now.</summary>
public sealed record ClassicTrack(string Title, string Artist, string Album, string? File, bool Playing, TimeSpan Position, TimeSpan Duration);

/// <summary>
/// Classic Windows Media Player (wmplayer.exe) doesn't report to the Windows media session and its window title
/// never changes ("Windows Media Player Legacy"), so neither of WinOrbit's usual routes can see it.
/// Microsoft's documented way in is "remoting": a program hosts the player's own control and declares itself a
/// remote host; the control then mirrors the running player — track, position, play state — and passes
/// play/pause/next/seek to it. Nothing is injected; it is the public interface the player offers to any program.
///
/// The link runs in a small helper (WinOrbit.exe --wmp-link &lt;pid&gt;) rather than in WinOrbit itself, because
/// the player's control keeps per-process state: after one session it refuses a second link in the same process
/// and leaves the player unable to close. A helper per session avoids that and keeps WinOrbit's own memory
/// clean. It is started only while the player shows a window (remoting would otherwise launch the player), and
/// it ends as soon as the player closes, WinOrbit asks, or WinOrbit goes away.
/// </summary>
public sealed class ClassicMediaPlayer : IDisposable
{
    private Process? _helper;
    private volatile ClassicTrack? _last;

    public bool Connected { get { try { return _helper is { HasExited: false }; } catch { return false; } } }

    /// <summary>Start the helper for the player with this process id. The first details arrive within a second.</summary>
    public bool Connect(int playerPid)
    {
        if (Connected) return true;
        Dispose();
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return false;
            var psi = new ProcessStartInfo(exe, "--wmp-link " + playerPid.ToString(CultureInfo.InvariantCulture))
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            if (p is null) return false;
            p.OutputDataReceived += (_, e) => { if (e.Data is { } line) _last = Decode(line); };
            p.BeginOutputReadLine();
            _helper = p;
            return true;
        }
        catch (Exception ex) { Log.Warn("Classic Media Player link failed to start: " + ex.Message); return false; }
    }

    /// <summary>The latest details from the helper, or null when nothing is loaded or there's no link yet.</summary>
    public ClassicTrack? Read() => Connected ? _last : null;

    public void PlayPause() => Send("pp");
    public void Next() => Send("next");
    public void Previous() => Send("prev");
    public bool Seek(TimeSpan position) => Send("seek " + position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));

    private bool Send(string command)
    {
        try { if (!Connected) return false; _helper!.StandardInput.WriteLine(command); _helper.StandardInput.Flush(); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        var p = _helper;
        _helper = null;
        _last = null;
        if (p is null) return;
        try { p.StandardInput.WriteLine("quit"); p.StandardInput.Flush(); } catch { }
        // Give it a moment to release the player cleanly; never block the UI for it.
        _ = Task.Run(() =>
        {
            try { if (!p.WaitForExit(3000)) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        });
    }

    // ------------------------------------------------------------------ wire format (one line per update)
    private const char Sep = '\u001f';

    internal static string Encode(ClassicTrack? t)
    {
        if (t is null) return "-";
        static string Clean(string? s) => (s ?? "").Replace(Sep, ' ').Replace('\r', ' ').Replace('\n', ' ');
        return string.Join(Sep, "T", t.Playing ? "1" : "0", Clean(t.Title), Clean(t.Artist), Clean(t.Album), Clean(t.File),
            t.Position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), t.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
    }

    internal static ClassicTrack? Decode(string line)
    {
        var f = line.Split(Sep);
        if (f.Length != 8 || f[0] != "T") return null;
        double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var pos);
        double.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur);
        return new ClassicTrack(f[2], f[3], f[4], f[5].Length > 0 ? f[5] : null, f[1] == "1",
            TimeSpan.FromSeconds(Math.Max(0, pos)), TimeSpan.FromSeconds(Math.Max(0, dur)));
    }

    // ------------------------------------------------------------------ the helper process

    /// <summary>
    /// Entry point of "WinOrbit.exe --wmp-link &lt;pid&gt;" (called from App startup, on the UI thread).
    /// Writes the player's state to standard output twice a second and obeys commands from standard input.
    /// Calls <paramref name="exit"/> when the player closes, on "quit", or when WinOrbit closes the pipe.
    /// </summary>
    public static void RunHelper(int playerPid, Action exit)
    {
        var session = new LinkSession();
        if (!session.Connect()) { exit(); return; }
        var ui = SynchronizationContext.Current;
        var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        var stopped = false;
        var misses = 0;

        void Stop()
        {
            if (stopped) return;
            stopped = true;
            timer.Stop();
            session.Dispose();
            exit();
        }

        timer.Tick += (_, _) =>
        {
            if (!PlayerOpen(playerPid)) { if (++misses >= 3) Stop(); return; }   // closed (a brief gap while it switches mode is fine)
            misses = 0;
            try { output.WriteLine(Encode(session.Read())); }
            catch { Stop(); }                                                       // WinOrbit is gone
        };
        timer.Start();

        var reader = new Thread(() =>
        {
            try
            {
                using var input = new StreamReader(Console.OpenStandardInput());
                while (input.ReadLine() is { } line)
                {
                    var cmd = line.Trim();
                    if (cmd == "quit") break;
                    ui?.Post(_ => { if (!stopped) session.Command(cmd); }, null);
                }
            }
            catch { }
            ui?.Post(_ => Stop(), null);
        }) { IsBackground = true, Name = "wmp-link input" };
        reader.Start();
    }

    private static bool PlayerOpen(int pid)
    {
        try { using var p = Process.GetProcessById(pid); if (p.HasExited) return false; } catch { return false; }
        return WindowPlayers.HasVisibleWindow(pid);
    }

    /// <summary>The in-process link itself (used only inside the helper).</summary>
    private sealed class LinkSession : IDisposable
    {
        private HiddenForm? _form;
        private RemoteHost? _host;
        private dynamic? _player;

        public bool Connect()
        {
            try
            {
                _form = new HiddenForm
                {
                    ShowInTaskbar = false,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-32000, -32000),
                    Size = new System.Drawing.Size(1, 1),
                    Text = "WinOrbit Media Player link",
                };
                _host = new RemoteHost();
                ((System.ComponentModel.ISupportInitialize)_host).BeginInit();
                _form.Controls.Add(_host);
                ((System.ComponentModel.ISupportInitialize)_host).EndInit();
                _form.Show();   // stays shown (off-screen, tool window, never activated): the control must stay in-place active
                _player = _host.Player;
                if (_player is null) { Log.Warn("Classic Media Player link: control not ready (" + (_host.LastError ?? "no site") + ")"); return false; }
                return true;
            }
            catch (Exception ex) { Log.Warn("Classic Media Player link failed: " + ex.Message); return false; }
        }

        public ClassicTrack? Read()
        {
            if (_player is null) return null;
            try
            {
                int state = _player.playState;            // 1 stopped, 2 paused, 3 playing, 6 buffering, 9 transitioning
                dynamic? media = _player.currentMedia;
                if (media is null) return null;
                string name = media.name ?? "";
                string title = Info(media, "Title");
                string artist = Info(media, "Author");
                if (artist.Length == 0) artist = Info(media, "WM/AlbumArtist");
                string album = Info(media, "WM/AlbumTitle");
                if (title.Length == 0) title = name;
                if (artist.Length == 0 && WindowPlayers.ParseTitle(title, null, "") is { } parsed) { title = parsed.Title; artist = parsed.Artist; }
                if (title.Length == 0) return null;
                string url = media.sourceURL ?? "";
                string? file = url.Length > 0 && System.IO.File.Exists(url) ? url : null;
                double dur = media.duration;
                double pos = _player.controls.currentPosition;
                return new ClassicTrack(title, artist, album, file, state is 3 or 6 or 9,
                    TimeSpan.FromSeconds(Math.Max(0, pos)), TimeSpan.FromSeconds(Math.Max(0, dur)));
            }
            catch { return null; }
        }

        private static string Info(dynamic media, string key)
        {
            try { return (string?)media.getItemInfo(key) ?? ""; } catch { return ""; }
        }

        public void Command(string cmd)
        {
            if (_player is null) return;
            try
            {
                if (cmd == "pp") { if ((int)_player.playState == 3) _player.controls.pause(); else _player.controls.play(); }
                else if (cmd == "next") _player.controls.next();
                else if (cmd == "prev") _player.controls.previous();
                else if (cmd.StartsWith("seek ", StringComparison.Ordinal)
                         && double.TryParse(cmd[5..], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                    _player.controls.currentPosition = Math.Max(0, s);
            }
            catch (Exception ex) { Log.Warn("Media Player command '" + cmd + "': " + ex.Message); }
        }

        public void Dispose()
        {
            var ocx = _host?.Player;
            _player = null;
            try { _host?.Dispose(); } catch { }
            try { _form?.Close(); _form?.Dispose(); } catch { }
            _host = null; _form = null;
            if (ocx is not null && Marshal.IsComObject(ocx)) { try { Marshal.FinalReleaseComObject(ocx); } catch { } }
        }
    }

    /// <summary>A 1×1 off-screen tool window: never in the taskbar or Alt+Tab, never takes focus.</summary>
    private sealed class HiddenForm : System.Windows.Forms.Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x80 | 0x08000000; return p; }   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        }
    }

    // ------------------------------------------------------------------ COM plumbing

    [ComImport, Guid("CBB92747-741F-44FE-AB5B-F1A48F3B2A59"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IWMPRemoteMediaServices
    {
        [PreserveSig] int GetServiceType([MarshalAs(UnmanagedType.BStr)] out string type);
        [PreserveSig] int GetApplicationName([MarshalAs(UnmanagedType.BStr)] out string name);
        [PreserveSig] int GetScriptableObject([MarshalAs(UnmanagedType.BStr)] out string? name, out IntPtr dispatch);
        [PreserveSig] int GetCustomUIMode([MarshalAs(UnmanagedType.BStr)] out string? file);
    }

    [ComImport, Guid("00000118-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleClientSite
    {
        [PreserveSig] int SaveObject();
        [PreserveSig] int GetMoniker(uint assign, uint which, out IntPtr moniker);
        [PreserveSig] int GetContainer(out IntPtr container);
        [PreserveSig] int ShowObject();
        [PreserveSig] int OnShowWindow(int show);
        [PreserveSig] int RequestNewObjectLayout();
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr obj);
    }

    [ComImport, Guid("00000112-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleObjectSetSite
    {
        [PreserveSig] int SetClientSite(IOleClientSite? site);   // first method of IOleObject
    }

    /// <summary>Hosts the player control (it must be in-place active) and answers its request for a remote host.</summary>
    [ComVisible(true)]
    public sealed class RemoteHost : System.Windows.Forms.AxHost, IOleClientSite, IServiceProvider, IWMPRemoteMediaServices
    {
        private const int E_NOTIMPL = unchecked((int)0x80004001), E_NOINTERFACE = unchecked((int)0x80004002);
        public object? Player { get; private set; }
        public string? LastError { get; private set; }

        public RemoteHost() : base("6bf52a52-394a-11d3-b153-00c04f79faa6") { }   // CLSID_WindowsMediaPlayer

        protected override void AttachInterfaces()
        {
            try
            {
                var ocx = GetOcx();
                if (ocx is IOleObjectSetSite ole)
                {
                    var hr = ole.SetClientSite(this);
                    if (hr == 0) Player = ocx; else LastError = "SetClientSite 0x" + hr.ToString("X8");
                }
                else LastError = "no IOleObject";
            }
            catch (Exception ex) { LastError = ex.Message; }
        }

        int IOleClientSite.SaveObject() => E_NOTIMPL;
        int IOleClientSite.GetMoniker(uint assign, uint which, out IntPtr moniker) { moniker = IntPtr.Zero; return E_NOTIMPL; }
        int IOleClientSite.GetContainer(out IntPtr container) { container = IntPtr.Zero; return E_NOINTERFACE; }
        int IOleClientSite.ShowObject() => 0;
        int IOleClientSite.OnShowWindow(int show) => 0;
        int IOleClientSite.RequestNewObjectLayout() => E_NOTIMPL;

        int IServiceProvider.QueryService(ref Guid service, ref Guid iid, out IntPtr obj)
        {
            obj = IntPtr.Zero;
            if (iid != typeof(IWMPRemoteMediaServices).GUID) return E_NOINTERFACE;
            var unk = Marshal.GetIUnknownForObject(this);
            try { var g = iid; return Marshal.QueryInterface(unk, in g, out obj); }
            finally { Marshal.Release(unk); }
        }

        int IWMPRemoteMediaServices.GetServiceType(out string type) { type = "Remote"; return 0; }
        int IWMPRemoteMediaServices.GetApplicationName(out string name) { name = "WinOrbit"; return 0; }
        int IWMPRemoteMediaServices.GetScriptableObject(out string? name, out IntPtr dispatch) { name = null; dispatch = IntPtr.Zero; return E_NOTIMPL; }
        int IWMPRemoteMediaServices.GetCustomUIMode(out string? file) { file = null; return E_NOTIMPL; }
    }
}
