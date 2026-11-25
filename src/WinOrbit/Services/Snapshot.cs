using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// "Screenshot with WinOrbit": a picture of every screen that includes the island and widgets even while Live glass
/// keeps them out of normal screenshots. Windows captures the screens as usual (without WinOrbit), then WinOrbit draws
/// its own hidden windows on top, each exactly where it sits. Saved to the Screenshots folder and copied to the
/// clipboard; nothing is sent anywhere.
/// </summary>
public static class Snapshot
{
    /// <summary>The file name for a snapshot taken at <paramref name="at"/>.</summary>
    internal static string FileName(DateTime at) => $"WinOrbit {at:yyyy-MM-dd HHmmss}.png";

    /// <summary>Where a window sits inside the captured picture, from its screen rectangle and the picture's origin.</summary>
    internal static Int32Rect Place(Native.RECT window, int originX, int originY) =>
        new(window.Left - originX, window.Top - originY, Math.Max(0, window.Right - window.Left), Math.Max(0, window.Bottom - window.Top));

    /// <summary>Take the picture, save it and copy it. Returns the saved file, or null if it failed.</summary>
    public static string? Take(string? folder = null)
    {
        try
        {
            int x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int w = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), h = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) return null;
            var screens = CaptureScreens(x, y, w, h);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(screens, new Rect(0, 0, w, h));
                // Widgets first, then the island, so the island stays on top as it does on screen.
                var hidden = Hub.LiveGlass?.HiddenFromCapture ?? Array.Empty<Window>();
                foreach (var win in hidden.OrderBy(v => v is Island.IslandWindow ? 1 : 0))
                {
                    if (!win.IsVisible || win.Opacity <= 0) continue;
                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) continue;
                    var at = Place(r, x, y);
                    if (at.Width == 0 || at.Height == 0) continue;
                    var dpi = VisualTreeHelper.GetDpi(win);
                    var pic = new RenderTargetBitmap(at.Width, at.Height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    pic.Render(win);
                    dc.DrawImage(pic, new Rect(at.X, at.Y, at.Width, at.Height));
                }
            }
            var all = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            all.Render(dv);
            all.Freeze();

            var dir = folder ?? ScreenshotWatcher.Folder() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, FileName(DateTime.Now));
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(all));
            using (var fs = File.Create(file)) png.Save(fs);
            try { Clipboard.SetImage(all); } catch { }   // the clipboard can be busy; the file is what matters
            Log.Info("Screenshot with WinOrbit saved");
            return file;
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot with WinOrbit", ex);
            return null;
        }
    }

    /// <summary>All screens as Windows shows them (WinOrbit's hidden windows are not in it).</summary>
    private static BitmapSource CaptureScreens(int x, int y, int w, int h)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        var mem = Native.CreateCompatibleDC(screen);
        var bmi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = Native.CreateDIBSection(mem, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            var old = Native.SelectObject(mem, dib);
            Native.BitBlt(mem, 0, 0, w, h, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT);
            Native.SelectObject(mem, old);
            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bits, w * h * 4, w * 4);
            src.Freeze();
            return src;
        }
        finally
        {
            Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
