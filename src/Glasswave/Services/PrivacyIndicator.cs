using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Glasswave.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Glasswave.Services;

/// <summary>
/// Which apps are using the microphone or camera right now — the same information Windows' own privacy icon in
/// the taskbar uses (Settings › Privacy › Microphone/Camera "currently in use"). Windows records it under the
/// user's CapabilityAccessManager consent store; an app is in use while LastUsedTimeStop is 0. Glasswave asks
/// Windows to signal changes to that key (no polling), reads it locally, and never stores or sends it.
/// </summary>
public sealed class PrivacyIndicator : IDisposable
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public IReadOnlyList<string> Microphone { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Camera { get; private set; } = Array.Empty<string>();
    public bool Active => Microphone.Count > 0 || Camera.Count > 0;

    /// <summary>Raised on the UI thread when the set of apps changes.</summary>
    public event Action? Changed;

    private readonly Dispatcher _ui;
    private Thread? _thread;
    private volatile bool _stop;
    private readonly AutoResetEvent _wake = new(false);

    public PrivacyIndicator(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        Evaluate();
        _thread = new Thread(Watch) { IsBackground = true, Name = "Glasswave privacy indicator", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int filter, SafeWaitHandle evt, bool async);

    private void Watch()
    {
        try
        {
            using var mic = Registry.CurrentUser.OpenSubKey(Root + @"\microphone");
            using var cam = Registry.CurrentUser.OpenSubKey(Root + @"\webcam");
            using var micEvt = new AutoResetEvent(false);
            using var camEvt = new AutoResetEvent(false);
            const int filter = 0x1 | 0x4;   // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET
            while (!_stop)
            {
                if (mic is not null) RegNotifyChangeKeyValue(mic.Handle, true, filter, micEvt.SafeWaitHandle, true);
                if (cam is not null) RegNotifyChangeKeyValue(cam.Handle, true, filter, camEvt.SafeWaitHandle, true);
                WaitHandle.WaitAny(new WaitHandle[] { micEvt, camEvt, _wake });
                if (_stop) break;
                Thread.Sleep(150);   // a start/stop writes several values; read once they settle
                Evaluate();
            }
        }
        catch (Exception ex) { Log.Warn("Privacy indicator stopped: " + ex.Message); }
    }

    private void Evaluate()
    {
        var mic = InUse("microphone");
        var cam = InUse("webcam");
        if (mic.SequenceEqual(Microphone) && cam.SequenceEqual(Camera)) return;
        _ui.BeginInvoke(() =>
        {
            Microphone = mic; Camera = cam;
            Changed?.Invoke();
        });
    }

    internal static List<string> InUse(string capability)
    {
        var apps = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root + "\\" + capability);
            if (key is null) return apps;
            void Scan(RegistryKey parent, bool packaged)
            {
                foreach (var name in parent.GetSubKeyNames())
                {
                    if (name == "NonPackaged") continue;
                    using var app = parent.OpenSubKey(name);
                    if (app?.GetValue("LastUsedTimeStart") is long start && start != 0 && app.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                        apps.Add(Friendly(name, packaged));
                }
            }
            Scan(key, true);
            using var np = key.OpenSubKey("NonPackaged");
            if (np is not null) Scan(np, false);
        }
        catch { }
        return apps.Distinct().OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Friendly(string name, bool packaged)
    {
        if (packaged)
        {
            var s = name.Split('_')[0];
            var dot = s.LastIndexOf('.');
            return dot >= 0 ? s[(dot + 1)..] : s;
        }
        var path = name.Replace('#', '\\');
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } d) return d;
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }

    public void Dispose() { _stop = true; _wake.Set(); }
}
