using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinOrbit.Core;
using WinOrbit.Interop;
using WinOrbit.Wallpaper;

namespace WinOrbit.Services;

/// <summary>
/// Live window glass (opt-in, off by default): instead of the wallpaper, glass shows a blurred picture of whatever is
/// on the screen behind it — the windows you have open.
///
/// How: Windows' screen-capture API (Windows.Graphics.Capture) hands over a frame only when something on that screen
/// changed. The frame stays on the graphics card, which shrinks it to an eighth of its size (mipmaps); only that small
/// picture — 240 × 135 for a 1080p screen — comes back to the CPU, where it gets the theme's blur and saturation and
/// replaces the backdrop in place. At most 10 updates a second; nothing at all while the screen is still. Nothing
/// is saved or sent anywhere.
///
/// WinOrbit's own glass windows are kept out of captures (<c>WDA_EXCLUDEFROMCAPTURE</c>), or the glass would see
/// itself. That also leaves them out of screenshots, recordings and screen sharing while this is on — the setting says
/// so. Capture runs only for screens where WinOrbit glass is showing, never during a fullscreen app on that screen or
/// while the screen is off. Needs Windows 11 (earlier versions draw a yellow frame round a captured screen).
/// </summary>
public sealed class LiveGlass
{
    /// <summary>Mipmap level brought back to the CPU: 3 = an eighth of the screen's width and height.</summary>
    internal const int Level = 3;
    /// <summary>Shortest time between two updates of one screen's picture (10 a second).</summary>
    internal const int MinGapMs = 100;

    private readonly Dispatcher _ui;
    private readonly Dictionary<string, Feed> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WriteableBitmap> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IntPtr, Window> _kept = new();      // windows kept out of captures
    private readonly HashSet<IntPtr> _refused = new();              // windows Windows wouldn't keep out: they use the wallpaper
    private Gpu? _gpu;
    private bool _on;

    public LiveGlass(Dispatcher ui)
    {
        _ui = ui;
        Hub.Foreground.FullscreenChanged += () => _ui.BeginInvoke(Reconcile);
        Hub.Power.ScreenChanged += () => _ui.BeginInvoke(Reconcile);
        Hub.Shell.DisplayChanged += () => _ui.BeginInvoke(() => { StopFeeds(); Reconcile(); });
    }

    /// <summary>Windows 11, with screen capture available.</summary>
    public static bool Supported
    {
        get
        {
            try { return Environment.OSVersion.Version.Build >= 22000 && GraphicsCaptureSession.IsSupported(); }
            catch { return false; }
        }
    }

    public bool Running => _on;
    /// <summary>Why it is off although the setting is on (shown in Settings), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Follow the setting. Call after it changes and once at start-up.</summary>
    public void Apply()
    {
        var want = Hub.Settings.General.LiveGlass && Supported;
        if (want == _on) { if (_on) Reconcile(); return; }
        if (want) Start(); else Stop(null);
    }

    private void Start()
    {
        try { _gpu ??= Gpu.Create(); }
        catch (Exception ex) { Problem = "The graphics card could not be used for it."; Log.Warn("Live glass: " + ex.Message); return; }
        _on = true;
        Problem = null;
        Log.Info("Live glass on");
        Hub.Wallpaper.NotifyBackdrops();   // surfaces ask again: their windows are kept out of captures and feeds start
        Reconcile();
    }

    private void Stop(string? problem)
    {
        if (!_on) return;
        _on = false;
        Problem = problem;
        StopFeeds();
        foreach (var (hwnd, w) in _kept)
        {
            Native.SetWindowDisplayAffinity(hwnd, Native.WDA_NONE);
            w.IsVisibleChanged -= OnWindowChanged; w.LocationChanged -= OnWindowMoved;
        }
        _kept.Clear(); _refused.Clear(); _pictures.Clear();
        _gpu?.Dispose(); _gpu = null;
        Log.Info(problem is null ? "Live glass off" : "Live glass stopped: " + problem);
        Hub.Wallpaper.NotifyBackdrops();
    }

    private void StopFeeds()
    {
        foreach (var f in _feeds.Values) f.Dispose();
        _feeds.Clear();
    }

    /// <summary>
    /// The live picture for glass inside <paramref name="visual"/> on <paramref name="mon"/>, or null to use the
    /// wallpaper (live glass off, the settings window, a window Windows wouldn't keep out of captures, or no picture yet).
    /// </summary>
    public BitmapSource? ImageFor(Visual visual, MonitorInfo mon)
    {
        // The settings window keeps the wallpaper: leaving it out of screenshots would surprise people.
        // (Not Application.MainWindow: WPF gives that title to the first window opened, usually the island.)
        if (!_on || Window.GetWindow(visual) is not { } w || w is Shell.MainWindow) return null;
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero || _refused.Contains(hwnd)) return null;
        if (!_kept.ContainsKey(hwnd))
        {
            if (!Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE))
            {
                Log.Warn($"Live glass: '{w.Title}' can't be kept out of captures (error {Marshal.GetLastWin32Error()}); it keeps the wallpaper");
                _refused.Add(hwnd);
                return null;
            }
            _kept[hwnd] = w;
            w.IsVisibleChanged += OnWindowChanged;
            w.LocationChanged += OnWindowMoved;
            w.Closed -= OnWindowClosed;   // once per window, however often live glass is turned on
            w.Closed += OnWindowClosed;
            Reconcile();
        }
        return _pictures.TryGetValue(mon.Device, out var pic) ? pic : null;
    }

    private void OnWindowChanged(object? sender, DependencyPropertyChangedEventArgs e) => Reconcile();
    private void OnWindowMoved(object? sender, EventArgs e) => Reconcile();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // The handle is gone by now, so find the window's entry by the window itself.
        foreach (var hwnd in _kept.Where(k => ReferenceEquals(k.Value, sender)).Select(k => k.Key).ToList()) _kept.Remove(hwnd);
        Reconcile();
    }

    /// <summary>Capture exactly the screens where WinOrbit glass is showing; none while the screen is off.</summary>
    private void Reconcile()
    {
        if (!_on || _gpu is null) return;
        var monitors = Monitors.All();
        var showing = _kept.Where(k => k.Value.IsVisible && k.Value.WindowState != WindowState.Minimized)
            .Select(k => Native.MonitorFromWindow(k.Key, 2)).ToList();
        var want = Wanted(monitors, showing, Hub.Power.IsScreenShowing, Hub.Foreground.IsFullscreen ? Hub.Foreground.FullscreenDevice : null);
        foreach (var device in _feeds.Keys.Where(d => !want.Contains(d)).ToList()) { _feeds[device].Dispose(); _feeds.Remove(device); }
        foreach (var mon in monitors.Where(m => want.Contains(m.Device) && !_feeds.ContainsKey(m.Device)))
        {
            try { _feeds[mon.Device] = new Feed(this, _gpu, mon); }
            catch (Exception ex)
            {
                var problem = ex is NotSupportedException ? ex.Message : "Windows didn't allow the screen to be captured.";
                Log.Warn("Live glass: " + ex.Message);
                Stop(problem);
                return;
            }
        }
    }

    /// <summary>Which screens to capture: those showing a WinOrbit glass window, unless the screen is off or a fullscreen app covers it.</summary>
    internal static HashSet<string> Wanted(IReadOnlyList<MonitorInfo> monitors, IEnumerable<IntPtr> showingOn, bool screenOn, string? fullscreenDevice)
    {
        var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!screenOn) return want;
        foreach (var handle in showingOn)
            if (monitors.FirstOrDefault(m => m.Handle == handle) is { } mon && !string.Equals(mon.Device, fullscreenDevice, StringComparison.OrdinalIgnoreCase))
                want.Add(mon.Device);
        return want;
    }

    /// <summary>Blur radius at the captured size that matches the wallpaper backdrop's blur.</summary>
    internal static int BlurRadius(double themeBlur) => Math.Max(1, (int)Math.Round(themeBlur / (1 << Level) / 1.6));

/// <summary>Shortest time between looks while the screen behind only changes a little (about 3 a second).</summary>
    internal const int CalmGapMs = 300;
    /// <summary>A picture is shown when some 8 × 8 patch of it changed by at least this much on average (0–255 per colour).</summary>
    internal const double ChangeThreshold = 3;

    /// <summary>How long until the next frame may be taken.</summary>
    internal static long Due(long lastTaken, long now, bool calm) => Math.Max(0, (calm ? CalmGapMs : MinGapMs) - (now - lastTaken));

    /// <summary>
    /// How much two pictures differ where they differ most: the largest average colour change over any 8 × 8 patch.
    /// A whole-picture average would hide a window moving behind a small island.
    /// </summary>
    internal static double Change(byte[] a, byte[] b, int w, int h, int block = 8)
    {
        if (a.Length != b.Length || a.Length < w * h * 4) return 255;
        double worst = 0;
        for (var by = 0; by < h; by += block)
            for (var bx = 0; bx < w; bx += block)
            {
                long sum = 0; var n = 0;
                for (var y = by; y < Math.Min(by + block, h); y++)
                    for (int x = bx, i = (y * w + bx) * 4; x < Math.Min(bx + block, w); x++, i += 4)
                    {
                        sum += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                        n += 3;
                    }
                if (n > 0) worst = Math.Max(worst, (double)sum / n);
            }
        return worst;
    }

    /// <summary>The size of mipmap level <see cref="Level"/> for a screen of this size.</summary>
    internal static (int Width, int Height) Shrunk(int width, int height) => (Math.Max(1, width >> Level), Math.Max(1, height >> Level));

    // A new picture for one screen, on the UI thread.
    private void Show(string device, byte[] px, int w, int h, Action done)
    {
        try
        {
            if (!_on || !_feeds.ContainsKey(device)) return;
            var fresh = false;
            if (!_pictures.TryGetValue(device, out var pic) || pic.PixelWidth != w || pic.PixelHeight != h)
            {
                pic = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                _pictures[device] = pic;
                fresh = true;
            }
            pic.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
            if (fresh) Hub.Wallpaper.NotifyBackdrops();   // glass switches from the wallpaper to this picture
        }
        finally { done(); }
    }

    private void Fail(Exception ex)
    {
        Log.Error("Live glass", ex);
        _ui.BeginInvoke(() => Stop("It stopped after a graphics error; turn it off and on to try again."));
    }

    // ------------------------------------------------------------------ the graphics card

    /// <summary>One Direct3D 11 device shared by every screen's capture. Its context is used under <see cref="Gate"/>.</summary>
    private sealed class Gpu : IDisposable
    {
        public readonly object Gate = new();
        public IntPtr Device, Context;
        public IDirect3DDevice WinRt = null!;
        public Native.D3DCreateTexture2D CreateTexture2D = null!;
        public Native.D3DCreateShaderResourceView CreateView = null!;
        public Native.D3DCopySubresourceRegion Copy = null!;
        public Native.D3DGenerateMips GenerateMips = null!;
        public Native.D3DMap Map = null!;
        public Native.D3DUnmap Unmap = null!;

        public static Gpu Create()
        {
            var hr = Native.D3D11CreateDevice(IntPtr.Zero, Native.D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, Native.D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, Native.D3D11_SDK_VERSION, out var device, out _, out var context);
            Marshal.ThrowExceptionForHR(hr);
            var gpu = new Gpu { Device = device, Context = context };
            try
            {
                var mt = Native.IID_ID3D10Multithread;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(context, ref mt, out var multithread));
                try { Native.ComSlot<Native.D3DSetMultithreadProtected>(multithread, 5)(multithread, 1); }
                finally { Marshal.Release(multithread); }
                var iid = Native.IID_IDXGIDevice;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, ref iid, out var dxgi));
                try
                {
                    Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                    gpu.WinRt = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                    Marshal.Release(inspectable);
                }
                finally { Marshal.Release(dxgi); }
                gpu.CreateTexture2D = Native.ComSlot<Native.D3DCreateTexture2D>(device, 5);
                gpu.CreateView = Native.ComSlot<Native.D3DCreateShaderResourceView>(device, 7);
                gpu.Map = Native.ComSlot<Native.D3DMap>(context, 14);
                gpu.Unmap = Native.ComSlot<Native.D3DUnmap>(context, 15);
                gpu.Copy = Native.ComSlot<Native.D3DCopySubresourceRegion>(context, 46);
                gpu.GenerateMips = Native.ComSlot<Native.D3DGenerateMips>(context, 54);
                return gpu;
            }
            catch { gpu.Dispose(); throw; }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                (WinRt as IDisposable)?.Dispose();
                if (Context != IntPtr.Zero) { Marshal.Release(Context); Context = IntPtr.Zero; }
                if (Device != IntPtr.Zero) { Marshal.Release(Device); Device = IntPtr.Zero; }
            }
        }
    }

    // ------------------------------------------------------------------ one screen

    private sealed class Feed : IDisposable
    {
        private readonly LiveGlass _owner;
        private readonly Gpu _gpu;
        private readonly string _device;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _pool;
        private readonly GraphicsCaptureSession _session;
        private IntPtr _mips, _view, _staging;
        private int _w, _h, _sw, _sh;
        private const int Buffers = 1;   // one waiting frame at most: Windows holds the next until it is taken
        private bool _scheduled, _reading, _closed, _calm, _first = true;
        private byte[] _shown = Array.Empty<byte>();
        private long _lastTaken;
        private readonly byte[][] _buffers = new byte[2][];
        private readonly bool[] _busy = new bool[2];

        public Feed(LiveGlass owner, Gpu gpu, MonitorInfo mon)
        {
            _owner = owner; _gpu = gpu; _device = mon.Device;
            _item = ItemFor(mon.Handle);
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(gpu.WinRt, DirectXPixelFormat.B8G8R8A8UIntNormalized, Buffers, _item.Size);
            _session = _pool.CreateCaptureSession(_item);
            try
            {
                _session.IsCursorCaptureEnabled = false;
                NoBorder(_session);
                lock (_gpu.Gate) Allocate(_item.Size.Width, _item.Size.Height);
                _pool.FrameArrived += OnFrame;
                _session.StartCapture();
            }
            catch { Dispose(); throw; }
        }

        private static GraphicsCaptureItem ItemFor(IntPtr monitor)
        {
            const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(Native.WindowsCreateString(name, name.Length, out var hstring));
            IntPtr interop;
            try
            {
                var iid = Native.IID_IGraphicsCaptureItemInterop;
                Marshal.ThrowExceptionForHR(Native.RoGetActivationFactory(hstring, ref iid, out interop));
            }
            finally { Native.WindowsDeleteString(hstring); }
            try
            {
                var item = Native.IID_IGraphicsCaptureItem;
                Marshal.ThrowExceptionForHR(Native.ComSlot<Native.CaptureItemForMonitor>(interop, 4)(interop, monitor, ref item, out var abi));
                try { return GraphicsCaptureItem.FromAbi(abi); }
                finally { Marshal.Release(abi); }
            }
            finally { Marshal.Release(interop); }
        }

        /// <summary>Windows 11 lets a capture run without the yellow frame round the screen; without that, no live glass.</summary>
        private static void NoBorder(GraphicsCaptureSession session)
        {
            var iid = Native.IID_IGraphicsCaptureSession3;
            var abi = ((WinRT.IWinRTObject)session).NativeObject.ThisPtr;
            if (Marshal.QueryInterface(abi, ref iid, out var s3) != 0 || s3 == IntPtr.Zero)
                throw new NotSupportedException("Needs Windows 11: this version of Windows marks a captured screen with a yellow frame.");
            try { Marshal.ThrowExceptionForHR(Native.ComSlot<Native.WinRtPutBool>(s3, 7)(s3, 0)); }
            finally { Marshal.Release(s3); }
        }

        // Called under the GPU gate.
        private void Allocate(int w, int h)
        {
            Release();
            _w = w; _h = h;
            (_sw, _sh) = Shrunk(w, h);
            var desc = new Native.D3D11_TEXTURE2D_DESC
            {
                Width = (uint)w, Height = (uint)h, MipLevels = Level + 1, ArraySize = 1, Format = Native.DXGI_FORMAT_B8G8R8A8_UNORM, SampleCount = 1,
                Usage = Native.D3D11_USAGE_DEFAULT, BindFlags = Native.D3D11_BIND_SHADER_RESOURCE | Native.D3D11_BIND_RENDER_TARGET, MiscFlags = Native.D3D11_RESOURCE_MISC_GENERATE_MIPS
            };
            Marshal.ThrowExceptionForHR(_gpu.CreateTexture2D(_gpu.Device, ref desc, IntPtr.Zero, out _mips));
            Marshal.ThrowExceptionForHR(_gpu.CreateView(_gpu.Device, _mips, IntPtr.Zero, out _view));
            var staging = new Native.D3D11_TEXTURE2D_DESC
            {
                Width = (uint)_sw, Height = (uint)_sh, MipLevels = 1, ArraySize = 1, Format = Native.DXGI_FORMAT_B8G8R8A8_UNORM, SampleCount = 1,
                Usage = Native.D3D11_USAGE_STAGING, CPUAccessFlags = Native.D3D11_CPU_ACCESS_READ
            };
            Marshal.ThrowExceptionForHR(_gpu.CreateTexture2D(_gpu.Device, ref staging, IntPtr.Zero, out _staging));
            _buffers[0] = new byte[_sw * _sh * 4];
            _buffers[1] = new byte[_sw * _sh * 4];
            _shown = new byte[_sw * _sh * 4];
            _first = true;
        }

        private void Release()
        {
            if (_staging != IntPtr.Zero) { Marshal.Release(_staging); _staging = IntPtr.Zero; }
            if (_view != IntPtr.Zero) { Marshal.Release(_view); _view = IntPtr.Zero; }
            if (_mips != IntPtr.Zero) { Marshal.Release(_mips); _mips = IntPtr.Zero; }
        }

        // Windows has a changed frame waiting (any thread). It is left in the pool until it is due: with a one-frame
        // pool, Windows captures nothing more until it's taken, so a screen that changes 60 times a second (a video,
        // an animated wallpaper) costs no more than MinGapMs allows.
        private void OnFrame(Direct3D11CaptureFramePool pool, object _)
        {
            long wait;
            lock (_gpu.Gate)
            {
                if (_closed || _scheduled) return;
                _scheduled = true;
                wait = Due(_lastTaken, Environment.TickCount64, _calm);
            }
            if (wait > 0) _ = Task.Delay((int)wait).ContinueWith(_ => Take());
            else _ = Task.Run(Take);
        }

        private void Retry()
        {
            lock (_gpu.Gate) { if (_closed || _scheduled) return; _scheduled = true; }
            _ = Task.Delay(MinGapMs).ContinueWith(_ => Take());
        }

        // Take the waiting frame: shrink it on the card, read the small picture back, blur it, hand it to the UI thread.
        private async Task Take()
        {
            var slot = -1;
            try
            {
                int w, h;
                lock (_gpu.Gate)
                {
                    _scheduled = false;
                    if (_closed) return;
                    slot = _reading ? -1 : _busy[0] ? (_busy[1] ? -1 : 1) : 0;
                    if (slot < 0) { Retry(); return; }   // still busy with the last picture; the frame waits in the pool
                    using var frame = _pool.TryGetNextFrame();
                    if (frame is null) { slot = -1; return; }
                    _lastTaken = Environment.TickCount64;
                    var size = frame.ContentSize;
                    if (size.Width != _w || size.Height != _h)
                    {
                        // The screen's resolution changed: resize everything and wait for the next frame.
                        _pool.Recreate(_gpu.WinRt, DirectXPixelFormat.B8G8R8A8UIntNormalized, Buffers, size);
                        Allocate(size.Width, size.Height);
                        slot = -1;
                        return;
                    }
                    _busy[slot] = true;
                    _reading = true;
                    var texture = TextureOf(frame.Surface);
                    try
                    {
                        var full = new Native.D3D11_BOX { Right = (uint)_w, Bottom = (uint)_h, Back = 1 };
                        _gpu.Copy(_gpu.Context, _mips, 0, 0, 0, 0, texture, 0, ref full);
                    }
                    finally { Marshal.Release(texture); }
                    _gpu.GenerateMips(_gpu.Context, _view);
                    var small = new Native.D3D11_BOX { Right = (uint)_sw, Bottom = (uint)_sh, Back = 1 };
                    _gpu.Copy(_gpu.Context, _staging, 0, 0, 0, 0, _mips, Level, ref small);
                    w = _sw; h = _sh;
                }   // the frame goes back to Windows here, ready for the next change

                // Read back once the card has finished, instead of spinning in Map while it works.
                var px = _buffers[slot];
                for (var tries = 0; ; tries++)
                {
                    int hr;
                    lock (_gpu.Gate)
                    {
                        if (_closed) return;
                        hr = _gpu.Map(_gpu.Context, _staging, 0, Native.D3D11_MAP_READ, Native.D3D11_MAP_FLAG_DO_NOT_WAIT, out var mapped);
                        if (hr == 0)
                        {
                            try
                            {
                                for (var y = 0; y < h; y++)
                                    Marshal.Copy(mapped.pData + y * (int)mapped.RowPitch, px, y * w * 4, w * 4);
                            }
                            finally { _gpu.Unmap(_gpu.Context, _staging, 0); _reading = false; }
                        }
                    }
                    if (hr == 0) break;
                    if (hr != Native.DXGI_ERROR_WAS_STILL_DRAWING || tries > 100) Marshal.ThrowExceptionForHR(hr);
                    await Task.Delay(2).ConfigureAwait(false);
                }
                WallpaperEngine.Adjust(px, Hub.Wallpaper.Saturation);
                WallpaperEngine.BoxBlur(px, w, h, BlurRadius(Hub.Wallpaper.Blur));
                // Skip pictures that barely differ from what the glass already shows (a gently animated wallpaper):
                // no redraw, and the next look comes later. A real change anywhere — a window moving behind — shows.
                lock (_gpu.Gate)
                {
                    var show = _first || Change(px, _shown, w, h) >= ChangeThreshold;
                    _calm = !show;
                    if (!show) return;   // finally hands the buffer back
                    Buffer.BlockCopy(px, 0, _shown, 0, px.Length);
                    _first = false;
                }
                var mine = slot;
                slot = -1;   // the UI thread hands it back
                _ = _owner._ui.BeginInvoke(() => _owner.Show(_device, px, w, h, () => { lock (_gpu.Gate) _busy[mine] = false; }));
            }
            catch (Exception ex) { _owner.Fail(ex); }
            finally
            {
                if (slot >= 0) lock (_gpu.Gate) { _busy[slot] = false; _reading = false; }
            }
        }

        private static Native.DxgiGetInterface? _getInterface;
        private static IntPtr _getInterfaceFn;

        private static IntPtr TextureOf(IDirect3DSurface surface)
        {
            var abi = ((WinRT.IWinRTObject)surface).NativeObject.ThisPtr;
            var iid = Native.IID_IDirect3DDxgiInterfaceAccess;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, ref iid, out var access));
            try
            {
                var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(access), 3 * IntPtr.Size);
                if (fn != _getInterfaceFn) { _getInterface = Marshal.GetDelegateForFunctionPointer<Native.DxgiGetInterface>(fn); _getInterfaceFn = fn; }
                var tex = Native.IID_ID3D11Texture2D;
                Marshal.ThrowExceptionForHR(_getInterface!(access, ref tex, out var texture));
                return texture;
            }
            finally { Marshal.Release(access); }
        }
        public void Dispose()
        {
            // Mark closed under the gate (Take checks it before touching the pool), but close the capture outside it:
            // a frame callback may be waiting for the gate, and closing the session can wait for that callback.
            lock (_gpu.Gate)
            {
                if (_closed) return;
                _closed = true;
            }
            _pool.FrameArrived -= OnFrame;
            _session.Dispose();
            _pool.Dispose();
            lock (_gpu.Gate) Release();
        }
    }
}
