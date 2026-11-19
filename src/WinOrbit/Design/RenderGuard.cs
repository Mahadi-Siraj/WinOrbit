using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Design;

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

    /// <summary>A window was just switched to software rendering (effects that need a GPU turn themselves off).</summary>
    public static event Action<Window>? Switched;

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
            // A window that is still painting (busy start-up) can miss the sentinel once. Only a window that stays
            // blank across three looks, ~0.7 s apart, is treated as not presented — falling back costs smoothness.
            // While the screen is off, no monitor is connected or the PC is locked, DWM hands back a blank picture of
            // every window, so the check stops and waits for Windows to report a screen or display change.
            var misses = 0;
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(600) };
            var waiting = false;
            void Resume()
            {
                if (!waiting) return;
                waiting = false;
                if (Hub.Ready) Hub.Power.ScreenChanged -= OnScreen;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplay;
                if (!window.IsLoaded) return;
                misses = 0;
                timer.Interval = TimeSpan.FromMilliseconds(1500);   // let the GPU settle after the screen comes back
                timer.Start();
            }
            void OnScreen() => window.Dispatcher.BeginInvoke(Resume);
            void OnDisplay(object? s, EventArgs e) => window.Dispatcher.BeginInvoke(Resume);
            window.Closed += (_, _) => { timer.Stop(); Resume(); };
            timer.Tick += (_, _) =>
            {
                try
                {
                    var showing = !Hub.Ready || Hub.Power.IsScreenShowing;
                    var (ok, blank) = showing ? Check(window, dot) : (null, false);
                    var step = Next(showing, ok, blank, misses);
                    switch (step)
                    {
                        case Step.Wait:
                            timer.Stop();
                            Log.Info("Render check: screen off, no monitor or locked; checking again when that changes");
                            waiting = true;
                            if (Hub.Ready) Hub.Power.ScreenChanged += OnScreen;
                            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplay;
                            return;
                        case Step.Look:
                            misses++;
                            timer.Interval = TimeSpan.FromMilliseconds(700);
                            return;
                        case Step.KeepHardware:
                            timer.Stop();
                            if (ok == false) Log.Info("Render check: window has content; keeping hardware rendering");
                            return;
                        default:
                            timer.Stop();
                            UseSoftware(window, onSoftware, "the GPU did not present the window");
                            return;
                    }
                }
                catch (Exception ex) { timer.Stop(); Log.Warn("Render check skipped: " + ex.Message); }
            };
            timer.Start();
        };
    }

    internal enum Step { Wait, Look, KeepHardware, Software }

    /// <summary>
    /// What the render check does next. Screen off or locked: wait (a blank capture proves nothing then).
    /// Sentinel seen, or the check can't judge, or the window has real content: keep hardware rendering.
    /// A flat, blank window: look again, and fall back to software only on the third blank look in a row.
    /// </summary>
    internal static Step Next(bool screenShowing, bool? ok, bool blank, int misses)
    {
        if (!screenShowing) return Step.Wait;
        if (ok != false || !blank) return Step.KeepHardware;
        return misses + 1 >= 3 ? Step.Software : Step.Look;
    }

    public static void UseSoftware(Window window, Action? onSoftware, string reason)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource src || src.CompositionTarget is null) return;
        if (src.CompositionTarget.RenderMode == RenderMode.SoftwareOnly) return;
        src.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        FellBack = true;
        Log.Warn($"Switched '{window.Title}' to software rendering: {reason}");
        onSoftware?.Invoke();
        Switched?.Invoke(window);
        window.InvalidateVisual();
    }

    /// <summary>
    /// Ok: true if the sentinel is on screen, false if not, null if it couldn't be judged.
    /// Blank: the snapshot is (almost) one flat colour — what a window the GPU never presented looks like.
    /// </summary>
    private static (bool? Ok, bool Blank) Check(Window window, FrameworkElement dot)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !window.IsVisible || window.WindowState == WindowState.Minimized) return (null, false);
        if (!Native.GetWindowRect(hwnd, out var wr) || wr.Width < 50 || wr.Height < 50) return (null, false);

        // Where the sentinel is, in window pixels.
        var screen = dot.PointToScreen(new Point(0.5, 0.5));
        int sx = (int)Math.Round(screen.X) - wr.Left, sy = (int)Math.Round(screen.Y) - wr.Top;
        if (sx < 0 || sy < 0 || sx >= wr.Width || sy >= wr.Height) return (null, false);

        var px = Snap(hwnd, wr.Width, wr.Height);
        if (px is null) return (null, false);
        var i = (sy * wr.Width + sx) * 4;
        int b = px[i], g = px[i + 1], r = px[i + 2];
        var match = Math.Abs(r - Sentinel.R) <= 3 && Math.Abs(g - Sentinel.G) <= 3 && Math.Abs(b - Sentinel.B) <= 3;
        if (match) return (true, false);
        var blank = IsFlat(px, wr.Width, wr.Height);
        Log.Warn($"Render check: expected sentinel, found #{r:X2}{g:X2}{b:X2}{(blank ? " (window blank)" : "")}");
        return (false, blank);
    }

    /// <summary>True when 97 % of a 16 × 16 grid of samples share the first sample's colour.</summary>
    internal static bool IsFlat(byte[] px, int width, int height)
    {
        int same = 0, total = 0;
        var first = -1;
        for (var gy = 1; gy <= 16; gy++)
            for (var gx = 1; gx <= 16; gx++)
            {
                var i = ((height * gy / 17) * width + width * gx / 17) * 4;
                var c = px[i] | px[i + 1] << 8 | px[i + 2] << 16;
                if (first < 0) first = c;
                total++;
                if (c == first) same++;
            }
        return same >= total * 0.97;
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
