using System.Runtime.InteropServices;
using System.Windows.Interop;
using Glasswave.Core;

namespace Glasswave.Services;

/// <summary>
/// Power and brightness changes pushed by Windows (RegisterPowerSettingNotification): plugging in or out,
/// battery percentage, and the built-in display's brightness (laptop keys, Action Center slider). Nothing is
/// polled. Windows sends the current values right after registering; those are treated as state, not events.
/// </summary>
public sealed class PowerEvents : IDisposable
{
    private static readonly Guid AcDcSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");
    private static readonly Guid BatteryPercent = new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1");
    private static readonly Guid MonitorBrightness = new("8ffee2c6-2d01-46be-adb9-398addc5b4ff");
    private static readonly Guid EnergySaverStatus = new("550e8400-e29b-41d4-a716-446655440000");   // GUID_ENERGY_SAVER_STATUS (Windows 11)

    /// <summary>Windows 11 Energy Saver: 0 off, 1 on, 2 on with extra savings. Null if this Windows doesn't report it.</summary>
    public int? EnergySaver { get; private set; }
    public event Action<int>? EnergySaverChanged;
    private const int WmPowerBroadcast = 0x0218, PbtPowerSettingChange = 0x8013;

    public bool? OnAc { get; private set; }
    public int? Battery { get; private set; }
    public int? Brightness { get; private set; }

    /// <summary>Plugged in (true) or unplugged (false).</summary>
    public event Action<bool>? PowerSourceChanged;
    public event Action<int>? BatteryChanged;
    public event Action<int>? BrightnessChanged;

    private HwndSource? _window;
    private readonly List<IntPtr> _registrations = new();
    private DateTime _quietUntil;

    [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    public void Start()
    {
        try
        {
            _window = new HwndSource(new HwndSourceParameters("Glasswave power") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            _window.AddHook(Hook);
            _quietUntil = DateTime.UtcNow.AddSeconds(3);
            foreach (var g in new[] { AcDcSource, BatteryPercent, MonitorBrightness, EnergySaverStatus })
            {
                var guid = g;
                var h = RegisterPowerSettingNotification(_window.Handle, ref guid, 0);
                if (h != IntPtr.Zero) _registrations.Add(h);
            }
        }
        catch (Exception ex) { Log.Warn("Power notifications unavailable: " + ex.Message); }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg != WmPowerBroadcast || w.ToInt32() != PbtPowerSettingChange || l == IntPtr.Zero) return IntPtr.Zero;
        var setting = Marshal.PtrToStructure<Guid>(l);
        var len = Marshal.ReadInt32(l, 16);
        if (len < 4) return IntPtr.Zero;
        var value = Marshal.ReadInt32(l, 20);
        var quiet = DateTime.UtcNow < _quietUntil;
        if (setting == AcDcSource)
        {
            var ac = value == 0;   // 0 = AC, 1 = DC (battery), 2 = short-term (UPS)
            var changed = OnAc is not null && OnAc != ac;
            OnAc = ac;
            if (changed && !quiet) PowerSourceChanged?.Invoke(ac);
        }
        else if (setting == BatteryPercent)
        {
            var changed = Battery is not null && Battery != value;
            Battery = value;
            if (changed && !quiet) BatteryChanged?.Invoke(value);
        }
        else if (setting == EnergySaverStatus)
        {
            var changed = EnergySaver is not null && EnergySaver != value;
            EnergySaver = value;
            if (changed && !quiet) EnergySaverChanged?.Invoke(value);
        }
        else if (setting == MonitorBrightness)
        {
            var changed = Brightness is not null && Brightness != value;
            Brightness = value;
            if (changed && !quiet) BrightnessChanged?.Invoke(value);
        }
        return IntPtr.Zero;
    }

    /// <summary>The battery thresholds worth a warning, crossed on the way down.</summary>
    internal static int? LowBatteryWarning(int previous, int now) =>
        previous > 10 && now <= 10 ? 10 : previous > 20 && now <= 20 ? 20 : null;

    public void Dispose()
    {
        foreach (var h in _registrations) UnregisterPowerSettingNotification(h);
        _registrations.Clear();
        _window?.Dispose();
    }
}
