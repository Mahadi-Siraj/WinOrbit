using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Glasswave.Core;
using Glasswave.Interop;

namespace Glasswave.Design;

/// <summary>
/// Self-check for GPU presentation. After some display-driver events (mode re-enumeration, driver resets)
/// WPF's hardware path can render a window that never reaches the screen — the controls exist but nothing
/// is drawn. RenderGuard places an invisible sentinel pixel in the window, reads the window back through
/// DWM (PrintWindow), and if the sentinel is missing switches that window to software rendering.
/// </summary>
public static class RenderGuard
{
    private static readonly Color Sentinel = Color.FromRgb(0x0B, 0x0D, 0x13);

    /// <summary>Whether any window had to fall back this session (shown in Diagnostics).</summary>
    public static bool FellBack { get; private set; }

    /// <summary>Adds the sentinel to a window's root grid and schedules the check.</summary>
    public static void Attach(Window window, System.Windows.Controls.Panel root, Action? onSoftware = null)
    {
        if (Hub.Ready && Hub.Settings.General.ForceSoftwareRendering)
        {
            window.SourceInitialized += (_, _) => UseSoftware(window, onSoftware, "set in Settings");
            return;
        }
        var dot = new Rectangle { Width = 2, Height = 2, Fill = new SolidColorBrush(Sentinel), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        System.Windows.Controls.Panel.SetZIndex(dot, 10000);
        root.Children.Add(dot);
        window.ContentRendered += (_, _) =>
        {
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var ok = Check(window, dot);
                    if (ok == false) UseSoftware(window, onSoftware, "the GPU did not present the window");
                }
                catch (Exception ex) { Log.Warn("Render check skipped: " + ex.Message); }
            };
            timer.Start();
        };
    }

    public static void UseSoftware(Window window, Action? onSoftware, string reason)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource src || src.CompositionTarget is null) return;
        if (src.CompositionTarget.RenderMode == RenderMode.SoftwareOnly) return;
        src.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        FellBack = true;
        Log.Warn($"Switched '{window.Title}' to software rendering: {reason}");
        onSoftware?.Invoke();
        window.InvalidateVisual();
    }

    /// <summary>True if the sentinel is on screen, false if the window is blank, null if it couldn't be judged.</summary>
    private static bool? Check(Window window, FrameworkElement dot)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !window.IsVisible || window.WindowState == WindowState.Minimized) return null;
        if (!Native.GetWindowRect(hwnd, out var wr) || wr.Width < 50 || wr.Height < 50) return null;

        // Where the sentinel is, in window pixels.
        var screen = dot.PointToScreen(new Point(0.5, 0.5));
        int sx = (int)Math.Round(screen.X) - wr.Left, sy = (int)Math.Round(screen.Y) - wr.Top;
        if (sx < 0 || sy < 0 || sx >= wr.Width || sy >= wr.Height) return null;

        var px = Snap(hwnd, wr.Width, wr.Height);
        if (px is null) return null;
        var i = (sy * wr.Width + sx) * 4;
        int b = px[i], g = px[i + 1], r = px[i + 2];
        var match = Math.Abs(r - Sentinel.R) <= 3 && Math.Abs(g - Sentinel.G) <= 3 && Math.Abs(b - Sentinel.B) <= 3;
        if (!match) Log.Warn($"Render check: expected sentinel, found #{r:X2}{g:X2}{b:X2}");
        return match;
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
        finally
        {
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
