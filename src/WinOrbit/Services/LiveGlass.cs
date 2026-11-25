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
/// picture — 240 × 135 for a 1080p screen — comes back to the CPU, where it gets the theme's blur and saturation. Each
/// glass window keeps its own copy of it, and a new picture is written only into the part that window's glass reads,
/// and only if that part changed: every write makes WPF redraw the whole window, which costs far more than the
/// capture. At most 10 updates a second; nothing at all while the screen is still. Nearly clear
/// looks (little blur, such as Liquid Clear) take a quarter-size picture instead (480 × 270) up to 20 times a second,
/// so what moves behind the glass stays sharp and smooth. Nothing is saved or sent anywhere.
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
    /// <summary>The level for nearly clear looks: 2 = a quarter of the screen's width and height.</summary>
    internal const int SharpLevel = 2;
    /// <summary>Shortest time between two updates of one screen's picture (10 a second).</summary>
    internal const int MinGapMs = 100;
    /// <summary>The same for nearly clear looks (20 a second), where movement behind the glass is plain to see.</summary>
    internal const int SharpGapMs = 50;

    /// <summary>Which mipmap level to bring back for the theme's blur: sharper for nearly clear looks.</summary>
    internal static int LevelFor(double themeBlur) => themeBlur < WallpaperEngine.SharpBelowBlur ? SharpLevel : Level;
    /// <summary>Shortest time between two updates for the theme's blur.</summary>
    internal static int GapFor(double themeBlur) => themeBlur < WallpaperEngine.SharpBelowBlur ? SharpGapMs : MinGapMs;

    private readonly Dispatcher _ui;
    private readonly Dictionary<string, Feed> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Frame> _latest = new(StringComparer.OrdinalIgnoreCase);   // each screen's newest picture
    private readonly Dictionary<(IntPtr Window, string Device), Picture> _pictures = new();         // each window's copy of it
    private readonly List<Sample> _samples = new();                                                  // what each glass reads from its copy
    private readonly Dictionary<IntPtr, Window> _kept = new();      // windows kept out of captures
    private readonly HashSet<IntPtr> _refused = new();              // windows Windows wouldn't keep out: they use the wallpaper
    private Gpu? _gpu;
    private bool _on;

    /// <summary>The WinOrbit windows Windows currently leaves out of captures (for "Screenshot with WinOrbit").</summary>
    public IReadOnlyCollection<Window> HiddenFromCapture => _kept.Values.ToList();

    public LiveGlass(Dispatcher ui)
    {
        _ui = ui;
        Hub.Foreground.FullscreenChanged += QueueReconcile;
        Hub.Power.ScreenChanged += QueueReconcile;
        Hub.Shell.DisplayChanged += () => _ui.BeginInvoke(() => { StopFeeds(); QueueReconcile(); });
    }

    private bool _reconcileQueued;

    /// <summary>
    /// Reconcile a moment later, on its own turn of the message loop. Starting a capture is a COM call, and Windows
    /// refuses those while WinOrbit is answering an input-synchronous message (a window being moved or shown), which
    /// used to switch live glass off until it was turned off and on again.
    /// </summary>
    private void QueueReconcile()
    {
        if (_reconcileQueued) return;
        _reconcileQueued = true;
        _ui.BeginInvoke(DispatcherPriority.Background, () => { _reconcileQueued = false; Reconcile(); });
    }

    // ----- getting back up after a failure -----
    private readonly List<DateTime> _failures = new();
    private DispatcherTimer? _retry;

    /// <summary>
    /// How long to wait before starting again after a failure, or null to stay off. Failures in the last ten minutes
    /// count: 3, 6, 12 then 24 seconds, and after four of them it stays off (shown in Settings) so a broken driver
    /// can't keep it cycling.
    /// </summary>
    internal static TimeSpan? RetryAfter(IReadOnlyList<DateTime> failures, DateTime now)
    {
        var recent = failures.Count(f => now - f < TimeSpan.FromMinutes(10) && f <= now);
        return recent >= 4 ? null : TimeSpan.FromSeconds(3 * Math.Pow(2, recent));
    }

    /// <summary>Stop after a failure and, unless it keeps failing, start again by itself a few seconds later.</summary>
    private void Recover(string problem)
    {
        var now = DateTime.Now;
        var wait = RetryAfter(_failures, now);
        _failures.Add(now);
        _failures.RemoveAll(f => now - f >= TimeSpan.FromMinutes(10));
        Stop(wait is null ? problem + " It failed several times, so it stays off until you turn it off and on." : problem);
        if (wait is not { } w) return;
        _retry?.Stop();
        _retry = new DispatcherTimer(w, DispatcherPriority.Background, (_, _) =>
        {
            _retry?.Stop(); _retry = null;
            if (_on || !Hub.Settings.General.LiveGlass) return;
            Log.Info("Live glass: trying again");
            Apply();
        }, _ui);
        _retry.Start();
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
        _kept.Clear(); _refused.Clear(); _latest.Clear(); _pictures.Clear(); _samples.Clear();
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
            QueueReconcile();
        }
        if (!_latest.TryGetValue(mon.Device, out var frame)) return null;
        if (!_pictures.TryGetValue((hwnd, mon.Device), out var pic))
        {
            // The window's own copy starts as the newest picture.
            pic = new Picture(new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null), frame.Width, frame.Height);
            pic.Bitmap.WritePixels(pic.All, frame.Pixels, frame.Width * 4, 0);
            pic.Current = pic.All;
            _pictures[(hwnd, mon.Device)] = pic;
        }
        return pic.Bitmap;
    }

    /// <summary>
    /// A glass inside <paramref name="visual"/> now shows <paramref name="view"/> (fractions of the picture) of
    /// <paramref name="image"/>. Called by <see cref="Design.GlassSurface.Slice"/> before the glass draws with it. A window's
    /// copy of the picture is only kept up to date where its glass reads it, so if this glass now reads further, the copy
    /// catches up first: what it draws is always the newest picture.
    /// </summary>
    public void Sampled(Visual visual, ImageSource image, Rect view)
    {
        foreach (var (key, pic) in _pictures)
        {
            if (!ReferenceEquals(pic.Bitmap, image)) continue;
            var area = Area(view, pic.Width, pic.Height);
            var sample = _samples.Find(s => s.Visual.TryGetTarget(out var v) && ReferenceEquals(v, visual));
            if (sample is null) _samples.Add(sample = new Sample(new WeakReference<Visual>(visual)));
            sample.Window = key.Window; sample.Device = key.Device; sample.Area = area;
            if (!Contains(pic.Current, area) && _latest.TryGetValue(key.Device, out var frame) && frame.Width == pic.Width && frame.Height == pic.Height)
            {
                pic.Bitmap.WritePixels(pic.All, frame.Pixels, frame.Width * 4, 0);
                pic.Current = pic.All;
            }
            return;
        }
    }

    private void OnWindowChanged(object? sender, DependencyPropertyChangedEventArgs e) => QueueReconcile();
    private void OnWindowMoved(object? sender, EventArgs e) => QueueReconcile();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // The handle is gone by now, so find the window's entry by the window itself.
        foreach (var hwnd in _kept.Where(k => ReferenceEquals(k.Value, sender)).Select(k => k.Key).ToList())
        {
            _kept.Remove(hwnd);
            foreach (var key in _pictures.Keys.Where(k => k.Window == hwnd).ToList()) _pictures.Remove(key);
            _samples.RemoveAll(s => s.Window == hwnd);
        }
        QueueReconcile();
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
                Log.Warn("Live glass: " + ex.Message);
                if (ex is NotSupportedException) Stop(ex.Message);
                else Recover("Windows didn't allow the screen to be captured.");
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

    /// <summary>Blur radius at the captured size (mipmap <paramref name="level"/>) that matches the wallpaper backdrop's blur.</summary>
    internal static int BlurRadius(double themeBlur, int level = Level) => Math.Max(1, (int)Math.Round(themeBlur / (1 << level) / 1.6));

/// <summary>Shortest time between looks while the screen behind only changes a little (about 3 a second).</summary>
    internal const int CalmGapMs = 300;
    /// <summary>A picture is shown when some 8 × 8 patch of it changed by at least this much on average (0–255 per colour).</summary>
    internal const double ChangeThreshold = 3;

    /// <summary>How long until the next frame may be taken.</summary>
    internal static long Due(long lastTaken, long now, bool calm, int gap = MinGapMs) => Math.Max(0, (calm ? CalmGapMs : gap) - (now - lastTaken));

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

    /// <summary>
    /// Whether some 8 × 8 patch changed by at least <paramref name="threshold"/> on average: the same answer as
    /// <c>Change(a, b, w, h) >= threshold</c>, but it stops looking at the first such patch.
    /// </summary>
    internal static bool Changed(byte[] a, byte[] b, int w, int h, double threshold, int block = 8)
    {
        if (threshold <= 0) return true;
        if (a.Length != b.Length || a.Length < w * h * 4) return 255 >= threshold;
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
                if (n > 0 && (double)sum / n >= threshold) return true;
            }
        return false;
    }

    /// <summary>The size of mipmap <paramref name="level"/> for a screen of this size.</summary>
    internal static (int Width, int Height) Shrunk(int width, int height, int level = Level) => (Math.Max(1, width >> level), Math.Max(1, height >> level));

    // ----- each window's copy of the picture -----
    // WPF redraws every window whose picture changes, and redrawing a see-through window is what costs. So each window
    // gets its own copy, updated only where its glass reads it and only when something there changed: a window whose
    // part of the screen stayed the same isn't redrawn, and what every glass draws is still the newest picture.

    /// <summary>Pixels round what a glass shows that smooth scaling also reads (bilinear needs 1; the rest is to spare).</summary>
    internal const int Margin = 3;

    /// <summary>
    /// The pixels of a <paramref name="width"/> × <paramref name="height"/> picture that a brush showing
    /// <paramref name="view"/> (fractions of the picture) can read, with <paramref name="margin"/> more on every side,
    /// within the picture. An unusable view counts as the whole picture.
    /// </summary>
    internal static Int32Rect Area(Rect view, int width, int height, int margin = Margin)
    {
        if (width < 1 || height < 1) return default;
        if (view.IsEmpty || !double.IsFinite(view.X) || !double.IsFinite(view.Y) || !double.IsFinite(view.Width) || !double.IsFinite(view.Height))
            return new Int32Rect(0, 0, width, height);
        var left = (int)Math.Clamp(Math.Floor(view.X * width) - margin, 0, width);
        var top = (int)Math.Clamp(Math.Floor(view.Y * height) - margin, 0, height);
        var right = (int)Math.Clamp(Math.Ceiling(view.Right * width) + margin, 0, width);
        var bottom = (int)Math.Clamp(Math.Ceiling(view.Bottom * height) + margin, 0, height);
        return right > left && bottom > top ? new Int32Rect(left, top, right - left, bottom - top) : default;
    }

    private static bool None(Int32Rect r) => r.Width <= 0 || r.Height <= 0;

    /// <summary>Whether <paramref name="inner"/> lies inside <paramref name="outer"/> (nothing lies inside anything).</summary>
    internal static bool Contains(Int32Rect outer, Int32Rect inner) =>
        None(inner) || (!None(outer) && inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height);

    /// <summary>The smallest rectangle round both.</summary>
    internal static Int32Rect Union(Int32Rect a, Int32Rect b)
    {
        if (None(a)) return None(b) ? default : b;
        if (None(b)) return a;
        int left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        return new Int32Rect(left, top, Math.Max(a.X + a.Width, b.X + b.Width) - left, Math.Max(a.Y + a.Height, b.Y + b.Height) - top);
    }

    /// <summary>Whether two pictures <paramref name="width"/> pixels wide have the same bytes inside <paramref name="r"/>.</summary>
    internal static bool Same(byte[] a, byte[] b, int width, Int32Rect r)
    {
        if (None(r)) return true;
        int stride = width * 4, run = r.Width * 4;
        for (var y = r.Y; y < r.Y + r.Height; y++)
        {
            var at = y * stride + r.X * 4;
            if (!a.AsSpan(at, run).SequenceEqual(b.AsSpan(at, run))) return false;
        }
        return true;
    }

    /// <summary>A screen's newest picture.</summary>
    private sealed class Frame(byte[] pixels, int width, int height)
    {
        public readonly byte[] Pixels = pixels;
        public readonly int Width = width, Height = height;
    }

    /// <summary>One window's copy of a screen's picture.</summary>
    private sealed class Picture(WriteableBitmap bitmap, int width, int height)
    {
        public readonly WriteableBitmap Bitmap = bitmap;
        public readonly int Width = width, Height = height;
        public Int32Rect All => new(0, 0, Width, Height);
        /// <summary>Where this copy is known to hold the newest picture.</summary>
        public Int32Rect Current;
    }

    /// <summary>The part of a window's copy one glass reads (its brush's viewbox, with <see cref="Margin"/>).</summary>
    private sealed class Sample(WeakReference<Visual> visual)
    {
        public readonly WeakReference<Visual> Visual = visual;
        public IntPtr Window;
        public string Device = "";
        public Int32Rect Area;
    }

    // A new picture for one screen, on the UI thread.
    private void Show(string device, byte[] px, int w, int h, Action done)
    {
        try
        {
            if (!_on || !_feeds.ContainsKey(device)) return;
            if (!_latest.TryGetValue(device, out var frame) || frame.Width != w || frame.Height != h)
            {
                // The first picture of this screen, or a new size: every window starts a fresh copy of it.
                _latest[device] = new Frame(px.AsSpan(0, w * h * 4).ToArray(), w, h);
                foreach (var key in _pictures.Keys.Where(k => string.Equals(k.Device, device, StringComparison.OrdinalIgnoreCase)).ToList()) _pictures.Remove(key);
                _samples.RemoveAll(s => string.Equals(s.Device, device, StringComparison.OrdinalIgnoreCase));
                Hub.Wallpaper.NotifyBackdrops();   // glass switches from the wallpaper to this picture
                return;
            }
            _samples.RemoveAll(s => !s.Visual.TryGetTarget(out _));
            foreach (var (key, pic) in _pictures)
            {
                if (!string.Equals(key.Device, device, StringComparison.OrdinalIgnoreCase)) continue;
                // Everything this window's glass reads. Sampled keeps it inside Current, where the copy matches the last
                // picture, so comparing with the last picture there tells whether the window would draw anything new.
                var read = default(Int32Rect);
                foreach (var s in _samples)
                    if (s.Window == key.Window && string.Equals(s.Device, device, StringComparison.OrdinalIgnoreCase)) read = Union(read, s.Area);
                if (!Same(px, frame.Pixels, w, read))
                    pic.Bitmap.WritePixels(read, px, w * 4, read.X, read.Y);
                pic.Current = read;
            }
            Buffer.BlockCopy(px, 0, frame.Pixels, 0, w * h * 4);
        }
        finally { done(); }
    }

    private void Fail(Exception ex)
    {
        Log.Error("Live glass", ex);
        _ui.BeginInvoke(() => { if (_on) Recover("It stopped after a graphics error."); });
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
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(context, in mt, out var multithread));
                try { Native.ComSlot<Native.D3DSetMultithreadProtected>(multithread, 5)(multithread, 1); }
                finally { Marshal.Release(multithread); }
                var iid = Native.IID_IDXGIDevice;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out var dxgi));
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
        private int _w, _h, _sw, _sh, _level = Level;
        private const int Buffers = 1;   // one waiting frame at most: Windows holds the next until it is taken
        private bool _scheduled, _reading, _closed, _calm, _first = true;
        private byte[] _shown = Array.Empty<byte>();
        private long _lastTaken;
        private readonly byte[][] _buffers = new byte[2][];
        private readonly byte[][] _scratch = new byte[2][];   // the blur's working space, one per buffer, kept between pictures
        private readonly bool[] _busy = new bool[2];
        // The last picture read back, before blurring, and the look it was blurred with (see Repeats).
        private byte[] _raw = Array.Empty<byte>();
        private bool _rawValid;
        private double _rawSaturation;
        private int _rawRadius, _deciding;

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
                lock (_gpu.Gate) Allocate(_item.Size.Width, _item.Size.Height, LevelFor(Hub.Wallpaper.Blur));
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
            if (Marshal.QueryInterface(abi, in iid, out var s3) != 0 || s3 == IntPtr.Zero)
                throw new NotSupportedException("Needs Windows 11: this version of Windows marks a captured screen with a yellow frame.");
            try { Marshal.ThrowExceptionForHR(Native.ComSlot<Native.WinRtPutBool>(s3, 7)(s3, 0)); }
            finally { Marshal.Release(s3); }
        }

        // Called under the GPU gate. The mip chain always reaches the softest level, so either level can be read from it.
        private void Allocate(int w, int h, int level)
        {
            Release();
            _w = w; _h = h; _level = level;
            (_sw, _sh) = Shrunk(w, h, level);
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
            _scratch[0] = new byte[_sw * _sh * 4];
            _scratch[1] = new byte[_sw * _sh * 4];
            _shown = new byte[_sw * _sh * 4];
            _raw = new byte[_sw * _sh * 4];
            _rawValid = false;
            _first = true;
        }

        // Called under the GPU gate right after a picture is read back. A picture with the same bytes as the one read
        // before, blurred the same way, comes out of blurring exactly as that one did: the glass already shows it, or it
        // differed too little from what is shown to be shown. So it is treated as a still screen without blurring it
        // again. Only while no other picture is half-way through, so the order of decisions is the same as before.
        private bool Repeats(byte[] px, double saturation, int radius)
        {
            if (px.Length != _raw.Length) { _rawValid = false; return false; }
            if (_rawValid && _deciding == 0 && saturation == _rawSaturation && radius == _rawRadius && px.AsSpan().SequenceEqual(_raw))
            {
                _calm = true;
                return true;
            }
            Buffer.BlockCopy(px, 0, _raw, 0, px.Length);
            _rawValid = true; _rawSaturation = saturation; _rawRadius = radius;
            return false;
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
                wait = Due(_lastTaken, Environment.TickCount64, _calm, GapFor(Hub.Wallpaper.Blur));
            }
            if (wait > 0) _ = Task.Delay((int)wait).ContinueWith(_ => Take());
            else _ = Task.Run(Take);
        }

        private void Retry()
        {
            lock (_gpu.Gate) { if (_closed || _scheduled) return; _scheduled = true; }
            _ = Task.Delay(GapFor(Hub.Wallpaper.Blur)).ContinueWith(_ => Take());
        }

        // Take the waiting frame: shrink it on the card, read the small picture back, blur it, hand it to the UI thread.
        private async Task Take()
        {
            var slot = -1;
            var deciding = false;
            try
            {
                int w, h, level;
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
                        Allocate(size.Width, size.Height, LevelFor(Hub.Wallpaper.Blur));
                        slot = -1;
                        return;
                    }
                    // The look switched between nearly clear and frosted: take this frame at the new picture size.
                    var want = LevelFor(Hub.Wallpaper.Blur);
                    if (want != _level) Allocate(_w, _h, want);
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
                    _gpu.Copy(_gpu.Context, _staging, 0, 0, 0, 0, _mips, (uint)_level, ref small);
                    w = _sw; h = _sh; level = _level;
                }   // the frame goes back to Windows here, ready for the next change

                // Read back once the card has finished, instead of spinning in Map while it works.
                var px = _buffers[slot];
                var scratch = _scratch[slot];
                double saturation = 0;
                int radius = 0;
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
                            saturation = Hub.Wallpaper.Saturation;
                            radius = BlurRadius(Hub.Wallpaper.Blur, level);
                            if (Repeats(px, saturation, radius)) return;   // finally hands the buffer back
                            _deciding++; deciding = true;
                        }
                    }
                    if (hr == 0) break;
                    if (hr != Native.DXGI_ERROR_WAS_STILL_DRAWING || tries > 100) Marshal.ThrowExceptionForHR(hr);
                    await Task.Delay(2).ConfigureAwait(false);
                }
                WallpaperEngine.Adjust(px, saturation);
                WallpaperEngine.BoxBlur(px, w, h, radius, scratch);
                // Skip pictures that barely differ from what the glass already shows (a gently animated wallpaper):
                // no redraw, and the next look comes later. A real change anywhere — a window moving behind — shows.
                lock (_gpu.Gate)
                {
                    _deciding--; deciding = false;
                    var show = _first || Changed(px, _shown, w, h, ChangeThreshold);
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
                if (slot >= 0 || deciding) lock (_gpu.Gate)
                {
                    if (slot >= 0) { _busy[slot] = false; _reading = false; }
                    if (deciding) _deciding--;
                }
            }
        }

        private static Native.DxgiGetInterface? _getInterface;
        private static IntPtr _getInterfaceFn;

        private static IntPtr TextureOf(IDirect3DSurface surface)
        {
            var abi = ((WinRT.IWinRTObject)surface).NativeObject.ThisPtr;
            var iid = Native.IID_IDirect3DDxgiInterfaceAccess;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in iid, out var access));
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
