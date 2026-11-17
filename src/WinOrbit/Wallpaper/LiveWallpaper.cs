using System.Diagnostics;
using System.Runtime.InteropServices;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Wallpaper;

/// <summary>
/// Support for live-wallpaper apps (Wallpaper Engine, Lively Wallpaper). These draw into a window behind the
/// desktop icons, so the wallpaper Windows reports is not what is on screen. This class snapshots only that
/// wallpaper host window with PrintWindow — never other applications' windows — so glass can frost what the
/// user actually sees. The snapshot stays in memory and is never written to disk.
/// </summary>
public static class LiveWallpaper
{
    private static readonly (string Process, string Name)[] Known =
    {
        ("wallpaper64", "Wallpaper Engine"), ("wallpaper32", "Wallpaper Engine"),
        ("Lively", "Lively Wallpaper"), ("livelywpf", "Lively Wallpaper"), ("Lively.PlayerWebView2", "Lively Wallpaper"),
    };

    /// <summary>Name of the running live-wallpaper app, or null.</summary>
    public static string? DetectApp()
    {
        foreach (var (proc, name) in Known)
        {
            var ps = Process.GetProcessesByName(proc);
            var found = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (found) return name;
        }
        return null;
    }

    /// <summary>
    /// Find the window that hosts the wallpaper. Windows 11 24H2+ parents it under Progman as a WorkerW;
    /// older builds use a top-level WorkerW that sits behind the icon layer.
    /// </summary>
    private static IntPtr FindHost(out bool includesIcons)
    {
        includesIcons = false;
        var progman = Native.FindWindow("Progman", null);
        IntPtr host = IntPtr.Zero;
        if (progman != IntPtr.Zero)
        {
            Native.EnumChildWindows(progman, (h, _) =>
            {
                if (Native.ClassNameOf(h) == "WorkerW" && Native.IsWindowVisible(h) && Native.GetWindowRect(h, out var r) && r.Width > 100) { host = h; return false; }
                return true;
            }, IntPtr.Zero);
        }
        if (host == IntPtr.Zero)
        {
            Native.EnumWindows((h, _) =>
            {
                if (Native.ClassNameOf(h) != "WorkerW" || !Native.IsWindowVisible(h)) return true;
                var hasIcons = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
                if (!hasIcons && Native.GetWindowRect(h, out var r) && r.Width > 100) { host = h; return false; }
                return true;
            }, IntPtr.Zero);
        }
        if (host == IntPtr.Zero && progman != IntPtr.Zero) { host = progman; includesIcons = true; }
        return host;
    }

    /// <summary>Snapshot the wallpaper layer, downscaled. Returns null if nothing usable could be captured.</summary>
    public static WallpaperFrame? Capture(int maxWidth = 960)
    {
        var host = FindHost(out var icons);
        if (host == IntPtr.Zero || !Native.GetWindowRect(host, out var rect) || rect.Width < 32 || rect.Height < 32) return null;
        var full = Snap(host, rect.Width, rect.Height);
        if ((full is null || IsBlank(full)) && !icons)
        {
            // Some builds only render the wallpaper through the parent; fall back to it (icons get blurred away).
            var progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero && Native.GetWindowRect(progman, out rect)) full = Snap(progman, rect.Width, rect.Height);
        }
        if (full is null || IsBlank(full)) return null;

        // Box-downscale to keep palette extraction and blurring cheap.
        var scale = Math.Max(1, (int)Math.Ceiling(rect.Width / (double)maxWidth));
        int w = rect.Width / scale, h = rect.Height / scale;
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            int b = 0, g = 0, r = 0;
            for (var dy = 0; dy < scale; dy++)
            {
                var row = ((y * scale + dy) * rect.Width + x * scale) * 4;
                for (var dx = 0; dx < scale; dx++) { var i = row + dx * 4; b += full[i]; g += full[i + 1]; r += full[i + 2]; }
            }
            var n = scale * scale; var o = (y * w + x) * 4;
            px[o] = (byte)(b / n); px[o + 1] = (byte)(g / n); px[o + 2] = (byte)(r / n); px[o + 3] = 255;
        }
        return new WallpaperFrame { Path = "live:", Pixels = px, Width = w, Height = h, SourceWidth = rect.Width, SourceHeight = rect.Height, Left = rect.Left, Top = rect.Top };
    }

    private static byte[]? Snap(IntPtr hwnd, int width, int height)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        var mem = Native.CreateCompatibleDC(screen);
        var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
        var dib = Native.CreateDIBSection(mem, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero) return null;
            var old = Native.SelectObject(mem, dib);
            var ok = Native.PrintWindow(hwnd, mem, Native.PW_RENDERFULLCONTENT);
            Native.SelectObject(mem, old);
            if (!ok) return null;
            var data = new byte[width * height * 4];
            Marshal.Copy(bits, data, 0, data.Length);
            return data;
        }
        catch (Exception ex) { Log.Warn("Live wallpaper capture: " + ex.Message); return null; }
        finally
        {
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>A capture of an un-rendered window comes back uniformly black or white.</summary>
    internal static bool IsBlank(byte[] px)
    {
        if (px.Length < 16) return true;
        int min = 255, max = 0;
        var step = Math.Max(4, px.Length / 4 / 4000 * 4);
        for (var i = 0; i < px.Length - 3; i += step)
        {
            var v = px[i] + px[i + 1] + px[i + 2];
            v /= 3;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return max - min < 6;
    }
}
