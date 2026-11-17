using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Glasswave.Core;
using Glasswave.Interop;
using Glasswave.Services;
using Microsoft.Win32;

namespace Glasswave.Wallpaper;

public enum WallpaperFit { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>A decoded, downscaled copy of one wallpaper image.</summary>
public sealed class WallpaperFrame
{
    public string Path = "";
    public byte[] Pixels = Array.Empty<byte>();   // BGRA
    public int Width, Height;                     // decoded size
    public int SourceWidth, SourceHeight;         // original image size
    public int Left, Top;                         // live captures: virtual-screen position of the captured layer
    public bool IsLive => Path == "live:";
}

/// <summary>
/// Wallpaper integration: finds the current wallpaper (per display), extracts a palette from it and
/// builds a small pre-blurred "backdrop" image for every display. Glass surfaces sample that backdrop
/// at their own screen position, which gives physically plausible frosted glass over the desktop with
/// perfectly anti-aliased corners and no per-frame GPU cost. Everything happens locally, off the UI thread.
/// </summary>
public sealed class WallpaperEngine
{
    public const int Downscale = 4;

    public string CurrentPath { get; private set; } = "";
    public WallpaperFit Fit { get; private set; } = WallpaperFit.Fill;
    public Color BackgroundColor { get; private set; } = Colors.Black;
    public Palette Palette { get; private set; } = Palette.Default;
    public bool HasImage { get; private set; }
    /// <summary>Live-wallpaper app currently drawing the desktop (Wallpaper Engine, Lively), if any.</summary>
    public string? LiveApp { get; private set; }
    /// <summary>True when glass is sampling a snapshot of a live wallpaper rather than the Windows wallpaper file.</summary>
    public bool UsingLiveCapture { get; private set; }
    public DateTime LastCapture { get; private set; }
    /// <summary>Set by the app: whether live-wallpaper snapshots are allowed.</summary>
    public Func<bool> LiveCaptureAllowed { get; set; } = () => true;
    /// <summary>Set by the app: skip periodic re-capture while e.g. a fullscreen game runs.</summary>
    public Func<bool> PauseLiveRefresh { get; set; } = () => false;
    private DispatcherTimer? _liveTimer;

    /// <summary>The wallpaper file or its colours changed.</summary>
    public event Action? PaletteChanged;
    /// <summary>New blurred backdrops are ready.</summary>
    public event Action? BackdropsChanged;

    private readonly Dispatcher _ui;
    private readonly Dictionary<string, WallpaperFrame> _frames = new(StringComparer.OrdinalIgnoreCase);   // by image path
    private readonly Dictionary<string, string> _monitorPath = new(StringComparer.OrdinalIgnoreCase);       // device → image path
    private Dictionary<string, BitmapSource> _blurred = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, BitmapSource> _sharp = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private string _signature = "";
    private double _blur = 40, _saturation = 1.3;
    private int _generation;

    public WallpaperEngine(Dispatcher ui)
    {
        _ui = ui;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshAsync(false); };
    }

    public BitmapSource? Backdrop(string device) => _blurred.TryGetValue(device, out var b) ? b : _blurred.Values.FirstOrDefault();
    public BitmapSource? Preview(string device) => _sharp.TryGetValue(device, out var b) ? b : _sharp.Values.FirstOrDefault();

    /// <summary>Start watching. Windows broadcasts a setting change and rewrites TranscodedWallpaper when the wallpaper changes.</summary>
    public void Watch(ShellHost shell)
    {
        shell.SettingChanged += _ => Schedule();
        shell.DisplayChanged += Schedule;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes");
            if (Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
                FileSystemEventHandler h = (_, _) => _ui.BeginInvoke(Schedule);
                _watcher.Changed += h; _watcher.Created += h;
                _watcher.Renamed += (_, _) => _ui.BeginInvoke(Schedule);
            }
        }
        catch (Exception ex) { Log.Warn("Wallpaper watcher unavailable: " + ex.Message); }
    }

    public void Schedule() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>Change how strongly backdrops are blurred/saturated (driven by the active theme).</summary>
    public void SetGlass(double blur, double saturation)
    {
        if (Math.Abs(blur - _blur) < 0.5 && Math.Abs(saturation - _saturation) < 0.01) return;
        _blur = blur; _saturation = saturation;
        _ = RebuildBackdropsAsync();
    }

    public async Task RefreshAsync(bool force)
    {
        try
        {
            Detect();
            LiveApp = LiveWallpaper.DetectApp();
            var live = LiveApp is not null && LiveCaptureAllowed();
            var signature = BuildSignature() + (live ? "|live" : "");
            if (!force && !live && signature == _signature) return;
            _signature = signature;

            Dictionary<string, WallpaperFrame> frames;
            WallpaperFrame? liveFrame = live ? await Task.Run(() => LiveWallpaper.Capture()) : null;
            UsingLiveCapture = liveFrame is not null;
            if (liveFrame is not null)
            {
                LastCapture = DateTime.Now;
                frames = new(StringComparer.OrdinalIgnoreCase) { [liveFrame.Path] = liveFrame };
                foreach (var m in Monitors.All()) _monitorPath[m.Device] = liveFrame.Path;
            }
            else
            {
                var files = _monitorPath.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                frames = await Task.Run(() => files.Select(Decode).Where(f => f is not null).ToDictionary(f => f!.Path, f => f!, StringComparer.OrdinalIgnoreCase));
            }
            ArmLiveTimer(live);
            var paths = _monitorPath.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _frames.Clear();
            foreach (var kv in frames) _frames[kv.Key] = kv.Value;
            HasImage = _frames.Count > 0;

            var primaryPath = _monitorPath.TryGetValue(Monitors.Primary.Device, out var pp) ? pp : paths.FirstOrDefault() ?? "";
            CurrentPath = primaryPath;
            var palette = _frames.TryGetValue(primaryPath, out var pf)
                ? await Task.Run(() => Palette.FromPixels(pf.Pixels, pf.Width, pf.Height))
                : SolidPalette(BackgroundColor);
            var paletteChanged = ColorMath.Hex(palette.Accent) != ColorMath.Hex(Palette.Accent) || ColorMath.Hex(palette.Dominant) != ColorMath.Hex(Palette.Dominant)
                                 || Math.Abs(palette.AverageLuminance - Palette.AverageLuminance) > 0.02;
            Palette = palette;
            if (paletteChanged || force) PaletteChanged?.Invoke();
            await RebuildBackdropsAsync();
        }
        catch (Exception ex) { Log.Error("Wallpaper refresh", ex); }
    }

    /// <summary>Live wallpapers change without telling Windows, so re-snapshot every few minutes (never during fullscreen apps).</summary>
    private void ArmLiveTimer(bool live)
    {
        if (!live) { _liveTimer?.Stop(); return; }
        if (_liveTimer is null)
        {
            _liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(3) };
            _liveTimer.Tick += (_, _) => { if (!PauseLiveRefresh()) _ = RefreshAsync(false); };
        }
        if (!_liveTimer.IsEnabled) _liveTimer.Start();
    }

    private static Palette SolidPalette(Color c)
    {
        var px = new byte[4 * 16];
        for (var i = 0; i < 16; i++) { px[i * 4] = c.B; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.R; px[i * 4 + 3] = 255; }
        return Palette.FromPixels(px, 4, 4);
    }

    private string BuildSignature()
    {
        var parts = new List<string> { Fit.ToString(), BackgroundColor.ToString() };
        foreach (var m in Monitors.All())
        {
            _monitorPath.TryGetValue(m.Device, out var p);
            long stamp = 0;
            try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) stamp = File.GetLastWriteTimeUtc(p).Ticks; } catch { }
            parts.Add($"{m.Device}|{m.Bounds.Width}x{m.Bounds.Height}|{p}|{stamp}");
        }
        return string.Join(";", parts);
    }

    // ----- detection -----
    private void Detect()
    {
        _monitorPath.Clear();
        var monitors = Monitors.All();
        var fit = WallpaperFit.Fill;
        var bg = Colors.Black;
        var gotCom = false;
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperCom();
            if (dw.GetPosition(out var pos) == 0) fit = (WallpaperFit)pos;
            if (dw.GetBackgroundColor(out var cr) == 0) bg = Color.FromRgb((byte)(cr & 0xFF), (byte)((cr >> 8) & 0xFF), (byte)((cr >> 16) & 0xFF));
            if (dw.GetMonitorDevicePathCount(out var n) == 0)
            {
                for (uint i = 0; i < n; i++)
                {
                    if (dw.GetMonitorDevicePathAt(i, out var id) != 0 || string.IsNullOrEmpty(id)) continue;
                    if (dw.GetMonitorRECT(id, out var rect) != 0) continue;     // detached display
                    if (dw.GetWallpaper(id, out var path) != 0) continue;
                    var mon = monitors.FirstOrDefault(m => m.Bounds.Left == rect.Left && m.Bounds.Top == rect.Top);
                    if (mon is not null && !string.IsNullOrEmpty(path) && File.Exists(path)) { _monitorPath[mon.Device] = path; gotCom = true; }
                }
            }
        }
        catch (Exception ex) { Log.Warn("IDesktopWallpaper unavailable, using registry: " + ex.Message); }

        if (!gotCom)
        {
            // Registry fallback (also covers Windows 10 edge cases).
            var path = "";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                path = key?.GetValue("WallPaper") as string ?? "";
                var style = key?.GetValue("WallpaperStyle") as string ?? "10";
                var tile = key?.GetValue("TileWallpaper") as string ?? "0";
                fit = tile == "1" ? WallpaperFit.Tile : style switch { "0" => WallpaperFit.Center, "2" => WallpaperFit.Stretch, "6" => WallpaperFit.Fit, "22" => WallpaperFit.Span, _ => WallpaperFit.Fill };
            }
            catch { }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                path = File.Exists(transcoded) ? transcoded : "";
            }
            if (!string.IsNullOrEmpty(path)) foreach (var m in monitors) _monitorPath[m.Device] = path;
        }
        Fit = fit;
        BackgroundColor = bg;
    }

    private static WallpaperFrame? Decode(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            int sw = frame.PixelWidth, sh = frame.PixelHeight;
            if (sw < 2 || sh < 2) return null;
            var scale = Math.Min(1.0, 720.0 / sw);
            BitmapSource src = frame;
            if (scale < 1) src = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            return new WallpaperFrame { Path = path, Pixels = px, Width = w, Height = h, SourceWidth = sw, SourceHeight = sh };
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not decode wallpaper '{Path.GetFileName(path)}': {ex.Message}");
            return null;
        }
    }

    // ----- backdrops -----
    private async Task RebuildBackdropsAsync()
    {
        var gen = ++_generation;
        var monitors = Monitors.All();
        var jobs = monitors.Select(m => (Monitor: m, Frame: _monitorPath.TryGetValue(m.Device, out var p) && _frames.TryGetValue(p, out var f) ? f : null)).ToList();
        var fit = Fit; var bg = BackgroundColor; var blur = _blur; var sat = _saturation;
        var virtualBounds = new Native.RECT
        {
            Left = monitors.Min(m => m.Bounds.Left), Top = monitors.Min(m => m.Bounds.Top),
            Right = monitors.Max(m => m.Bounds.Right), Bottom = monitors.Max(m => m.Bounds.Bottom)
        };

        var result = await Task.Run(() =>
        {
            var blurred = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            var sharp = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mon, frame) in jobs)
            {
                var tw = Math.Max(32, mon.Bounds.Width / Downscale);
                var th = Math.Max(32, mon.Bounds.Height / Downscale);
                var px = Compose(frame, mon, virtualBounds, fit, bg, tw, th);
                sharp[mon.Device] = ToBitmap((byte[])px.Clone(), tw, th);
                Adjust(px, sat);
                BoxBlur(px, tw, th, (int)Math.Round(blur / Downscale / 1.6));
                blurred[mon.Device] = ToBitmap(px, tw, th);
            }
            return (blurred, sharp);
        });

        if (gen != _generation) return;     // a newer rebuild superseded this one
        _blurred = result.blurred;
        _sharp = result.sharp;
        BackdropsChanged?.Invoke();
    }

    private static BitmapSource ToBitmap(byte[] px, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Render the wallpaper as Windows would place it on one monitor, at reduced resolution.</summary>
    internal static byte[] Compose(WallpaperFrame? frame, MonitorInfo mon, Native.RECT virtualBounds, WallpaperFit fit, Color bg, int tw, int th)
    {
        var dst = new byte[tw * th * 4];
        for (var i = 0; i < dst.Length; i += 4) { dst[i] = bg.B; dst[i + 1] = bg.G; dst[i + 2] = bg.R; dst[i + 3] = 255; }
        if (frame is null) return dst;

        // Work in monitor pixels: find the rectangle (ox, oy, dw, dh) the full image occupies, then sample.
        double mw = mon.Bounds.Width, mh = mon.Bounds.Height;
        double iw = frame.SourceWidth, ih = frame.SourceHeight;
        double ox, oy, dw, dh;
        var tile = false;
        if (frame.IsLive) fit = (WallpaperFit)(-1);   // a live capture maps 1:1 onto the virtual screen
        switch (fit)
        {
            case (WallpaperFit)(-1): dw = frame.SourceWidth; dh = frame.SourceHeight; ox = frame.Left - mon.Bounds.Left; oy = frame.Top - mon.Bounds.Top; break;
            case WallpaperFit.Stretch: ox = 0; oy = 0; dw = mw; dh = mh; break;
            case WallpaperFit.Fit: { var s = Math.Min(mw / iw, mh / ih); dw = iw * s; dh = ih * s; ox = (mw - dw) / 2; oy = (mh - dh) / 2; break; }
            case WallpaperFit.Center: dw = iw; dh = ih; ox = (mw - dw) / 2; oy = (mh - dh) / 2; break;
            case WallpaperFit.Tile: dw = iw; dh = ih; ox = -(mon.Bounds.Left - virtualBounds.Left); oy = -(mon.Bounds.Top - virtualBounds.Top); tile = true; break;
            case WallpaperFit.Span:
            {
                double vw = virtualBounds.Width, vh = virtualBounds.Height;
                var s = Math.Max(vw / iw, vh / ih);
                dw = iw * s; dh = ih * s;
                ox = (vw - dw) / 2 - (mon.Bounds.Left - virtualBounds.Left);
                oy = (vh - dh) / 2 - (mon.Bounds.Top - virtualBounds.Top);
                break;
            }
            default: { var s = Math.Max(mw / iw, mh / ih); dw = iw * s; dh = ih * s; ox = (mw - dw) / 2; oy = (mh - dh) / 2; break; }   // Fill
        }

        double sxScale = mw / tw, syScale = mh / th;
        var src = frame.Pixels; int fw = frame.Width, fh = frame.Height;
        for (var y = 0; y < th; y++)
        {
            var my = (y + 0.5) * syScale;
            var v = (my - oy) / dh;
            if (tile) v -= Math.Floor(v);
            if (v < 0 || v >= 1) continue;
            var fy = v * fh - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(fy), 0, fh - 1); var y1 = Math.Min(y0 + 1, fh - 1); var wy = Math.Clamp(fy - y0, 0, 1);
            for (var x = 0; x < tw; x++)
            {
                var mx = (x + 0.5) * sxScale;
                var u = (mx - ox) / dw;
                if (tile) u -= Math.Floor(u);
                if (u < 0 || u >= 1) continue;
                var fx = u * fw - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(fx), 0, fw - 1); var x1 = Math.Min(x0 + 1, fw - 1); var wx = Math.Clamp(fx - x0, 0, 1);
                int a = (y0 * fw + x0) * 4, b = (y0 * fw + x1) * 4, c = (y1 * fw + x0) * 4, d = (y1 * fw + x1) * 4, o = (y * tw + x) * 4;
                for (var ch = 0; ch < 3; ch++)
                {
                    var top = src[a + ch] + (src[b + ch] - src[a + ch]) * wx;
                    var bottom = src[c + ch] + (src[d + ch] - src[c + ch]) * wx;
                    dst[o + ch] = (byte)(top + (bottom - top) * wy);
                }
            }
        }
        return dst;
    }

    internal static void Adjust(byte[] px, double saturation)
    {
        if (Math.Abs(saturation - 1) < 0.01) return;
        for (var i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            var grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            px[i] = (byte)Math.Clamp(grey + (b - grey) * saturation, 0, 255);
            px[i + 1] = (byte)Math.Clamp(grey + (g - grey) * saturation, 0, 255);
            px[i + 2] = (byte)Math.Clamp(grey + (r - grey) * saturation, 0, 255);
        }
    }

    /// <summary>Three box-blur passes approximate a Gaussian; O(pixels) regardless of radius.</summary>
    internal static void BoxBlur(byte[] px, int w, int h, int radius)
    {
        if (radius < 1) return;
        radius = Math.Min(radius, Math.Min(w, h) / 2 - 1);
        if (radius < 1) return;
        var tmp = new byte[px.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BlurLine(px, tmp, w, h, radius, true);
            BlurLine(tmp, px, w, h, radius, false);
        }
    }

    private static void BlurLine(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        var win = r * 2 + 1;
        int outer = horizontal ? h : w, inner = horizontal ? w : h;
        int stepInner = horizontal ? 4 : w * 4, stepOuter = horizontal ? w * 4 : 4;
        for (var o = 0; o < outer; o++)
        {
            var line = o * stepOuter;
            int sb = 0, sg = 0, sr = 0;
            for (var i = -r; i <= r; i++)
            {
                var idx = line + Math.Clamp(i, 0, inner - 1) * stepInner;
                sb += src[idx]; sg += src[idx + 1]; sr += src[idx + 2];
            }
            for (var i = 0; i < inner; i++)
            {
                var d = line + i * stepInner;
                dst[d] = (byte)(sb / win); dst[d + 1] = (byte)(sg / win); dst[d + 2] = (byte)(sr / win); dst[d + 3] = 255;
                var add = line + Math.Min(i + r + 1, inner - 1) * stepInner;
                var rem = line + Math.Max(i - r, 0) * stepInner;
                sb += src[add] - src[rem]; sg += src[add + 1] - src[rem + 1]; sr += src[add + 2] - src[rem + 2];
            }
        }
    }

    /// <summary>Set the desktop wallpaper through the documented shell API.</summary>
    public static bool SetWallpaper(string path)
    {
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperCom();
            return dw.SetWallpaper(null, path) == 0;
        }
        catch
        {
            return Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, path, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }
    }
}
