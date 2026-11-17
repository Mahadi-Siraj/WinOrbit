using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Glasswave.Core;
using Glasswave.Interop;

namespace Glasswave.Services;

// ============================================================================================
// Displays
// ============================================================================================
public sealed record MonitorInfo(IntPtr Handle, string Device, Native.RECT Bounds, Native.RECT Work, bool Primary, double Scale)
{
    public string Label => $"{Device.Replace(@"\\.\", "")} · {Bounds.Width}×{Bounds.Height}{(Primary ? " · primary" : "")}";
}

public static class Monitors
{
    private static List<MonitorInfo>? _cache;

    public static void Invalidate() => _cache = null;

    public static List<MonitorInfo> All()
    {
        if (_cache is { Count: > 0 }) return _cache;
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(h, ref mi))
            {
                double scale = 1;
                try { if (Native.GetDpiForMonitor(h, 0, out var dx, out _) == 0) scale = dx / 96.0; } catch { }
                list.Add(new MonitorInfo(h, mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, scale));
            }
            return true;
        }, IntPtr.Zero);
        if (list.Count == 0)
            list.Add(new MonitorInfo(IntPtr.Zero, "DISPLAY1", new Native.RECT { Right = 1920, Bottom = 1080 }, new Native.RECT { Right = 1920, Bottom = 1040 }, true, 1));
        return _cache = list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ToList();
    }

    public static MonitorInfo Primary => All().FirstOrDefault(m => m.Primary) ?? All()[0];

    /// <summary>Find a display by device name; a missing display falls back to the primary one.</summary>
    public static MonitorInfo Find(string? device)
        => All().FirstOrDefault(m => string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase)) ?? Primary;

    public static MonitorInfo FromWindow(IntPtr hwnd)
    {
        var h = Native.MonitorFromWindow(hwnd, 2);
        return All().FirstOrDefault(m => m.Handle == h) ?? Primary;
    }

    public static MonitorInfo FromPoint(int x, int y)
    {
        var h = Native.MonitorFromPoint(new Native.POINT { X = x, Y = y }, 2);
        return All().FirstOrDefault(m => m.Handle == h) ?? Primary;
    }
}

// ============================================================================================
// System monitor: one shared sampler that only runs while somebody is actually watching.
// ============================================================================================
public sealed class SystemSample
{
    public double Cpu, Ram, RamUsedGb, RamTotalGb, DiskActive, DiskBytes, NetDown, NetUp;
    public double? Gpu, GpuTemp, VramUsedGb, VramTotalGb;
    public int? Battery;
    public bool OnAc = true;
    public TimeSpan Uptime;
}

public sealed class SystemMonitor : IDisposable
{
    public const int HistoryLength = 60;
    public SystemSample Latest { get; private set; } = new();
    public double[] CpuHistory { get; } = new double[HistoryLength];
    public double[] RamHistory { get; } = new double[HistoryLength];
    public double[] GpuHistory { get; } = new double[HistoryLength];
    public double[] NetHistory { get; } = new double[HistoryLength];
    public bool GpuAvailable { get; private set; }
    public bool GpuTempAvailable { get; private set; }
    public string GpuSource { get; private set; } = "none";

    /// <summary>Set while a fullscreen app runs (if the user allows) so sampling stops completely.</summary>
    public bool Paused { get => _paused; set { _paused = value; Reconfigure(); } }

    private readonly Dispatcher _ui;
    private readonly List<Action<SystemSample>> _subscribers = new();
    private Timer? _timer;
    private bool _paused, _pdhReady, _nvmlTried;
    private IntPtr _query, _cDisk, _cDiskIdle, _cNetIn, _cNetOut, _cGpu, _nvml;
    private ulong _lastIdle, _lastKernel, _lastUser;
    private readonly object _gate = new();

    public SystemMonitor(Dispatcher ui) => _ui = ui;

    public int SubscriberCount { get { lock (_gate) return _subscribers.Count; } }

    /// <summary>Subscribe to samples. Dispose the token when the view is hidden: with no subscribers the sampler is off.</summary>
    public IDisposable Subscribe(Action<SystemSample> handler)
    {
        lock (_gate) _subscribers.Add(handler);
        Reconfigure();
        return new Token(() => { lock (_gate) _subscribers.Remove(handler); Reconfigure(); });
    }

    private void Reconfigure()
    {
        lock (_gate)
        {
            var shouldRun = _subscribers.Count > 0 && !_paused;
            if (shouldRun && _timer is null) _timer = new Timer(_ => Sample(), null, 0, 1500);
            else if (!shouldRun && _timer is not null) { _timer.Dispose(); _timer = null; }
        }
    }

    private void EnsureCounters()
    {
        if (_pdhReady) return;
        _pdhReady = true;
        try
        {
            if (Native.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
            Native.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Bytes/sec", IntPtr.Zero, out _cDisk);
            Native.PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Idle Time", IntPtr.Zero, out _cDiskIdle);
            Native.PdhAddEnglishCounter(_query, @"\Network Interface(*)\Bytes Received/sec", IntPtr.Zero, out _cNetIn);
            Native.PdhAddEnglishCounter(_query, @"\Network Interface(*)\Bytes Sent/sec", IntPtr.Zero, out _cNetOut);
            Native.PdhCollectQueryData(_query);
        }
        catch (Exception ex) { Log.Warn("Performance counters unavailable: " + ex.Message); }

        // GPU: prefer the vendor's own library (NVML ships with the NVIDIA driver), else the OS counter.
        if (!_nvmlTried)
        {
            _nvmlTried = true;
            try
            {
                if (Native.nvmlInit_v2() == 0 && Native.nvmlDeviceGetHandleByIndex_v2(0, out _nvml) == 0)
                {
                    GpuAvailable = GpuTempAvailable = true;
                    GpuSource = "NVIDIA NVML";
                }
            }
            catch { _nvml = IntPtr.Zero; }
            if (_nvml == IntPtr.Zero && _query != IntPtr.Zero)
            {
                try
                {
                    if (Native.PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _cGpu) == 0)
                    {
                        GpuAvailable = true;
                        GpuSource = "Windows GPU Engine counter";
                    }
                }
                catch { }
            }
        }
    }

    private void Sample()
    {
        try
        {
            EnsureCounters();
            var s = new SystemSample();

            if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var dIdle = idle.Value - _lastIdle;
                var dTotal = (kernel.Value - _lastKernel) + (user.Value - _lastUser);
                if (_lastKernel != 0 && dTotal > 0) s.Cpu = Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
                (_lastIdle, _lastKernel, _lastUser) = (idle.Value, kernel.Value, user.Value);
            }

            var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref mem))
            {
                s.RamTotalGb = mem.ullTotalPhys / 1073741824.0;
                s.RamUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
                s.Ram = mem.dwMemoryLoad;
            }

            if (_query != IntPtr.Zero && Native.PdhCollectQueryData(_query) == 0)
            {
                s.DiskBytes = Single(_cDisk);
                s.DiskActive = Math.Clamp(100 - Single(_cDiskIdle), 0, 100);
                s.NetDown = SumArray(_cNetIn, true);
                s.NetUp = SumArray(_cNetOut, true);
                if (_cGpu != IntPtr.Zero) s.Gpu = Math.Clamp(SumArray(_cGpu, false), 0, 100);
            }

            if (_nvml != IntPtr.Zero)
            {
                try
                {
                    if (Native.nvmlDeviceGetUtilizationRates(_nvml, out var u) == 0) s.Gpu = u.Gpu;
                    if (Native.nvmlDeviceGetTemperature(_nvml, 0, out var t) == 0) s.GpuTemp = t;
                    if (Native.nvmlDeviceGetMemoryInfo(_nvml, out var m) == 0)
                    {
                        s.VramUsedGb = m.Used / 1073741824.0;
                        s.VramTotalGb = m.Total / 1073741824.0;
                    }
                }
                catch { }
            }

            if (Native.GetSystemPowerStatus(out var p))
            {
                s.OnAc = p.ACLineStatus != 0;
                if ((p.BatteryFlag & 128) == 0 && p.BatteryLifePercent <= 100) s.Battery = p.BatteryLifePercent;
            }
            s.Uptime = TimeSpan.FromMilliseconds(Native.GetTickCount64());

            Push(CpuHistory, s.Cpu);
            Push(RamHistory, s.Ram);
            Push(GpuHistory, s.Gpu ?? 0);
            Push(NetHistory, s.NetDown + s.NetUp);
            Latest = s;

            Action<SystemSample>[] targets;
            lock (_gate) targets = _subscribers.ToArray();
            if (targets.Length > 0)
                _ui.BeginInvoke(DispatcherPriority.Background, () => { foreach (var t in targets) { try { t(s); } catch (Exception ex) { Log.Error("Monitor subscriber", ex); } } });
        }
        catch (Exception ex) { Log.Error("Sampling failed", ex); }
    }

    private static void Push(double[] buffer, double value)
    {
        Array.Copy(buffer, 1, buffer, 0, buffer.Length - 1);
        buffer[^1] = value;
    }

    private static double Single(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return 0;
        return Native.PdhGetFormattedCounterValue(counter, Native.PDH_FMT_DOUBLE, out _, out var v) == 0 && v.CStatus == 0 ? v.DoubleValue : 0;
    }

    private static readonly string[] VirtualAdapters = { "loopback", "isatap", "teredo", "vethernet", "virtual", "filter", "qos", "wfp", "pseudo" };

    private static double SumArray(IntPtr counter, bool skipVirtual)
    {
        if (counter == IntPtr.Zero) return 0;
        uint size = 0;
        var rc = Native.PdhGetFormattedCounterArray(counter, Native.PDH_FMT_DOUBLE, ref size, out _, IntPtr.Zero);
        if (rc != Native.PDH_MORE_DATA || size == 0) return 0;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.PdhGetFormattedCounterArray(counter, Native.PDH_FMT_DOUBLE, ref size, out var count, buffer) != 0) return 0;
            double sum = 0;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * 24;                       // { LPWSTR name; PDH_FMT_COUNTERVALUE value }
                if (Marshal.ReadInt32(item, 8) != 0) continue;     // CStatus
                if (skipVirtual)
                {
                    var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item))?.ToLowerInvariant() ?? "";
                    if (VirtualAdapters.Any(name.Contains)) continue;
                }
                sum += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        lock (_gate) { _timer?.Dispose(); _timer = null; }
        if (_query != IntPtr.Zero) { try { Native.PdhCloseQuery(_query); } catch { } _query = IntPtr.Zero; }
        if (_nvml != IntPtr.Zero) { try { Native.nvmlShutdown(); } catch { } _nvml = IntPtr.Zero; }
    }

    private sealed class Token(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}

// ============================================================================================
// Volume (Core Audio) and brightness (DDC/CI)
// ============================================================================================
public static class Audio
{
    private static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return device.Activate(ref iid, 23, IntPtr.Zero, out var obj) == 0 ? (IAudioEndpointVolume)obj : null;
        }
        catch { return null; }
    }

    /// <summary>Master volume 0..1, or null when there is no output device.</summary>
    public static double? GetVolume()
    {
        var ep = Endpoint();
        return ep is not null && ep.GetMasterVolumeLevelScalar(out var v) == 0 ? v : null;
    }

    public static void SetVolume(double value)
    {
        var ctx = Guid.Empty;
        Endpoint()?.SetMasterVolumeLevelScalar((float)Math.Clamp(value, 0, 1), ref ctx);
    }

    public static bool GetMute() => Endpoint() is { } ep && ep.GetMute(out var m) == 0 && m;

    public static void SetMute(bool mute)
    {
        var ctx = Guid.Empty;
        Endpoint()?.SetMute(mute, ref ctx);
    }
}

public static class Brightness
{
    // DDC/CI is a slow, lossy side channel: monitors often miss a request while they are busy (input switch,
    // OSD open, waking up). One failure must not disable the slider for the whole session, so reads retry,
    // and only a monitor that has failed several times in a row is treated as unsupported — for a few minutes.
    private static int _failures;
    private static DateTime _lastFailure;
    private static readonly object Gate = new();

    private static bool LooksUnsupported => _failures >= 3 && DateTime.UtcNow - _lastFailure < TimeSpan.FromMinutes(5);

    /// <summary>Brightness of the primary display 0..100 through DDC/CI, or null when the monitor does not answer.</summary>
    public static int? Get()
    {
        lock (Gate)
        {
            if (LooksUnsupported) return null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                int? result = null;
                WithMonitor(h => { if (Native.GetMonitorBrightness(h, out var min, out var cur, out var max) && max > min) result = (int)Math.Round(100.0 * (cur - min) / (max - min)); });
                if (result is not null) { _failures = 0; return result; }
                Thread.Sleep(60);
            }
            _failures++;
            _lastFailure = DateTime.UtcNow;
            return null;
        }
    }

    public static void Set(int percent)
    {
        lock (Gate)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var ok = false;
                WithMonitor(h =>
                {
                    if (Native.GetMonitorBrightness(h, out var min, out _, out var max) && max > min)
                        ok = Native.SetMonitorBrightness(h, (uint)Math.Round(min + (max - min) * Math.Clamp(percent, 0, 100) / 100.0));
                });
                if (ok) return;
                Thread.Sleep(60);
            }
        }
    }

    private static void WithMonitor(Action<IntPtr> action)
    {
        try
        {
            var hMon = Monitors.Primary.Handle;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out var count) || count == 0) return;
            var arr = new Native.PHYSICAL_MONITOR[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, count, arr)) return;
            try { action(arr[0].hPhysicalMonitor); }
            finally { Native.DestroyPhysicalMonitors(count, arr); }
        }
        catch { }
    }
}

// ============================================================================================
// Foreground / fullscreen awareness (event driven, no per-frame polling)
// ============================================================================================
public sealed class ForegroundWatcher : IDisposable
{
    public bool IsFullscreen { get; private set; }
    public bool DesktopForeground { get; private set; }
    public string ProcessName { get; private set; } = "";
    public string FullscreenDevice { get; private set; } = "";

    public event Action? FullscreenChanged;
    public event Action? ForegroundChanged;

    private readonly Native.WinEventProc _proc;   // kept alive for the lifetime of the hook
    private IntPtr _hook;
    private readonly DispatcherTimer _timer;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public ForegroundWatcher()
    {
        _proc = (_, _, _, _, _, _, _) => Evaluate(true);
        // Out-of-context hook: the callback runs in OUR process. Nothing is injected anywhere.
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        // A window can go fullscreen without a foreground change (F11, video). One cheap check every 2.5 s covers that.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += (_, _) => Evaluate(false);
        _timer.Start();
    }

    private void Evaluate(bool foregroundEvent)
    {
        try
        {
            var fg = Native.GetForegroundWindow();
            var cls = fg == IntPtr.Zero ? "" : Native.ClassNameOf(fg);
            var desktop = cls is "Progman" or "WorkerW";
            var shell = desktop || cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow" or "TopLevelWindowForOverflowXamlIsland";
            Native.GetWindowThreadProcessId(fg, out var pid);

            var full = false;
            var device = "";
            if (fg != IntPtr.Zero && !shell && pid != _ownPid && Native.GetWindowRect(fg, out var r))
            {
                var mon = Monitors.FromWindow(fg);
                var covers = r.Left <= mon.Bounds.Left && r.Top <= mon.Bounds.Top && r.Right >= mon.Bounds.Right && r.Bottom >= mon.Bounds.Bottom;
                if (covers)
                {
                    var style = Native.GetWindowLongPtr(fg, Native.GWL_STYLE).ToInt64();
                    var hasCaption = (style & 0x00C00000) == 0x00C00000;
                    Native.SHQueryUserNotificationState(out var quns);
                    full = !hasCaption || quns is 2 or 3 or 4;   // BUSY, D3D fullscreen, presentation mode
                    device = mon.Device;
                }
            }

            var fgChanged = false;
            if (foregroundEvent || desktop != DesktopForeground)
            {
                string name = ProcessName;
                if (pid != 0)
                {
                    try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); name = p.ProcessName; }
                    catch { name = ""; }
                }
                fgChanged = name != ProcessName || desktop != DesktopForeground;
                ProcessName = name;
                DesktopForeground = desktop;
            }

            if (full != IsFullscreen || device != FullscreenDevice)
            {
                IsFullscreen = full;
                FullscreenDevice = device;
                FullscreenChanged?.Invoke();
            }
            if (fgChanged || foregroundEvent) ForegroundChanged?.Invoke();
        }
        catch (Exception ex) { Log.Error("Foreground evaluation", ex); }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero) { Native.UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
    }
}

// ============================================================================================
// Focus timer / countdown / stopwatch shared by the island and the widgets
// ============================================================================================
public enum TimerMode { Focus, Break, Countdown, Stopwatch }

public sealed class TimerService
{
    public TimerMode Mode { get; private set; } = TimerMode.Focus;
    public bool Running { get; private set; }
    public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Elapsed => _banked + (Running ? DateTime.UtcNow - _startedAt : TimeSpan.Zero);
    public TimeSpan Remaining => Mode == TimerMode.Stopwatch ? Elapsed : (Total - Elapsed < TimeSpan.Zero ? TimeSpan.Zero : Total - Elapsed);
    public double Progress => Mode == TimerMode.Stopwatch || Total.TotalSeconds <= 0 ? 0 : Math.Clamp(Elapsed.TotalSeconds / Total.TotalSeconds, 0, 1);
    public bool IsActive => Running || _banked > TimeSpan.Zero;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int CompletedToday => _store.Data.Sessions.Count(s => s.Start.Date == DateTime.Today);
    public int MinutesToday => _store.Data.Sessions.Where(s => s.Start.Date == DateTime.Today).Sum(s => s.Minutes);

    /// <summary>Raised once a second while running, and on every state change.</summary>
    public event Action? Changed;
    /// <summary>Raised when a focus block, break or countdown reaches zero.</summary>
    public event Action<TimerMode>? Completed;

    private readonly Store _store;
    private readonly DispatcherTimer _tick;
    private TimeSpan _banked;
    private DateTime _startedAt;

    public TimerService(Store store)
    {
        _store = store;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();
    }

    public void SetMode(TimerMode mode, int? minutes = null)
    {
        Stop(false);
        Mode = mode;
        _banked = TimeSpan.Zero;
        Total = mode switch
        {
            TimerMode.Focus => TimeSpan.FromMinutes(minutes ?? FocusMinutes),
            TimerMode.Break => TimeSpan.FromMinutes(minutes ?? BreakMinutes),
            TimerMode.Countdown => TimeSpan.FromMinutes(minutes ?? 10),
            _ => TimeSpan.Zero
        };
        if (mode == TimerMode.Focus && minutes is { } m) FocusMinutes = m;
        Changed?.Invoke();
    }

    public void Toggle() { if (Running) Stop(false); else Start(); }

    public void Start()
    {
        if (Running) return;
        if (Mode != TimerMode.Stopwatch && Remaining <= TimeSpan.Zero) _banked = TimeSpan.Zero;
        _startedAt = DateTime.UtcNow;
        Running = true;
        _tick.Start();          // the 1 s tick exists only while a timer is running
        Changed?.Invoke();
    }

    public void Stop(bool notify = true)
    {
        if (Running) { _banked += DateTime.UtcNow - _startedAt; Running = false; }
        _tick.Stop();
        if (notify) Changed?.Invoke();
    }

    public void Reset()
    {
        Stop(false);
        _banked = TimeSpan.Zero;
        Changed?.Invoke();
    }

    private void OnTick()
    {
        if (Mode != TimerMode.Stopwatch && Elapsed >= Total)
        {
            var finished = Mode;
            Stop(false);
            _banked = TimeSpan.Zero;
            if (finished == TimerMode.Focus)
            {
                _store.Data.Sessions.Add(new FocusSession { Start = DateTime.Now - Total, Minutes = (int)Math.Round(Total.TotalMinutes) });
                if (_store.Data.Sessions.Count > 2000) _store.Data.Sessions.RemoveRange(0, 500);
                _store.SaveData();
                Mode = TimerMode.Break;
                Total = TimeSpan.FromMinutes(BreakMinutes);
            }
            else if (finished == TimerMode.Break)
            {
                Mode = TimerMode.Focus;
                Total = TimeSpan.FromMinutes(FocusMinutes);
            }
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            Completed?.Invoke(finished);
        }
        Changed?.Invoke();
    }

    public string Display
    {
        get
        {
            var t = Remaining;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }
    }

    public string ModeLabel => Mode switch { TimerMode.Focus => "Focus", TimerMode.Break => "Break", TimerMode.Countdown => "Countdown", _ => "Stopwatch" };
}

/// <summary>Formatting helpers shared by widgets and the island.</summary>
public static class Fmt
{
    public static string Rate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1048576) return $"{bytesPerSecond / 1048576:0.0} MB/s";
        if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    public static string Size(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:0.0} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }

    public static string Uptime(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : $"{t.Hours}h {t.Minutes}m";

    public static string Time(DateTime t, bool h24) => t.ToString(h24 ? "HH:mm" : "h:mm");
}
