using System.Diagnostics;
using System.Runtime.InteropServices;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// The apps that currently have a window on screen, for picking one in settings (e.g. "always hide the island for
/// this app"). Reads only process names and their file descriptions; never window contents. Nothing is stored or sent.
/// </summary>
public static class RunningApps
{
    public sealed record App(string Process, string Name);

    private const long WsExToolWindow = 0x80;
    private const uint GwOwner = 4;
    private const int DwmwaCloaked = 14;

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);

    public static List<App> List()
    {
        var own = (uint)Environment.ProcessId;
        var pids = new HashSet<uint>();
        Native.EnumWindows((h, _) =>
        {
            // Real app windows only: visible, titled, not owned popups or tool windows, not hidden ("cloaked") UWP frames.
            if (!Native.IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
            if (Native.GetWindow(h, GwOwner) != IntPtr.Zero) return true;
            if ((Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64() & WsExToolWindow) != 0) return true;
            if (DwmGetWindowAttribute(h, DwmwaCloaked, out var cloaked, 4) == 0 && cloaked != 0) return true;
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid != 0 && pid != own) pids.Add(pid);
            return true;
        }, IntPtr.Zero);

        var apps = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                var name = p.ProcessName;
                // Explorer is the desktop itself; ApplicationFrameHost hosts every Store app, so picking it would mean all of them.
                if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) || apps.ContainsKey(name)) continue;
                apps[name] = new App(name, Describe(p) ?? name);
            }
            catch { /* gone, or not ours to read */ }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? Describe(Process p)
    {
        try { return p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } d ? d.Trim() : null; }
        catch { return null; }   // elevated or protected processes don't let us read their module
    }
}
