using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Networking.Connectivity;
using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>
/// Connection and key-state changes for the island: Caps/Num/Scroll Lock, network (Wi-Fi or Ethernet) coming and
/// going, Bluetooth devices connecting and disconnecting, and USB devices being plugged in or removed.
///
/// Privacy and cost:
///   • Lock keys: WinOrbit asks Windows whether the three lock lights are on, five times a second, only while that
///     alert is switched on. It never sees which keys you type.
///   • Network: Windows pushes a NetworkStatusChanged event; WinOrbit reads the connection's name and type only.
///   • Bluetooth and USB: Windows device watchers push added/removed/changed events; only device names are read.
/// Everything stays on this PC. The first state seen after start-up is treated as state, not as an event, so
/// launching WinOrbit doesn't announce every device that was already connected.
/// </summary>
public sealed class DeviceEvents : IDisposable
{
    public enum LockKey { Caps, Num, Scroll }
    public enum NetKind { None, WiFi, Ethernet, Other }

    public event Action<LockKey, bool>? LockChanged;
    /// <summary>(kind, name, connected). Name is the Wi-Fi network or the adapter's connection name.</summary>
    public event Action<NetKind, string, bool>? NetworkChanged;
    /// <summary>(name, kind hint, connected).</summary>
    public event Action<string, string, bool>? BluetoothChanged;
    /// <summary>(name, plugged in).</summary>
    public event Action<string, bool>? UsbChanged;

    private readonly Dispatcher _ui;
    private readonly Func<IslandSettings> _settings;
    private DispatcherTimer? _keys;
    private bool _caps, _num, _scroll;
    private DispatcherTimer? _netDebounce;
    private (NetKind Kind, string Name) _net = (NetKind.None, "");
    private bool _netKnown;
    private DeviceWatcher? _bt, _ble, _usb;
    private readonly Dictionary<string, (string Name, bool Connected, string Kind)> _btDevices = new();
    private readonly Dictionary<string, string> _usbDevices = new();
    private readonly Dictionary<string, DateTime> _recent = new();
    private bool _btReady, _bleReady, _usbReady;

    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    private const int VkCapital = 0x14, VkNumLock = 0x90, VkScroll = 0x91;

    public DeviceEvents(Dispatcher ui, Func<IslandSettings> settings) { _ui = ui; _settings = settings; }

    /// <summary>Start or stop each source to match the island settings (called on start-up and on every change).</summary>
    public void Apply()
    {
        var s = _settings();
        var on = s.Enabled;
        try { SetKeys(on && s.LockKeyAlerts); } catch (Exception ex) { Log.Warn("Lock-key alerts unavailable: " + ex.Message); }
        try { SetNetwork(on && s.NetworkAlerts); } catch (Exception ex) { Log.Warn("Network alerts unavailable: " + ex.Message); }
        try { SetBluetooth(on && s.BluetoothAlerts); } catch (Exception ex) { Log.Warn("Bluetooth alerts unavailable: " + ex.Message); }
        try { SetUsb(on && s.UsbAlerts); } catch (Exception ex) { Log.Warn("USB alerts unavailable: " + ex.Message); }
    }

    // ------------------------------------------------------------------ lock keys
    private static bool Toggled(int vk) => (GetKeyState(vk) & 1) != 0;

    private void SetKeys(bool on)
    {
        if (!on) { _keys?.Stop(); _keys = null; return; }
        if (_keys is not null) return;
        _caps = Toggled(VkCapital); _num = Toggled(VkNumLock); _scroll = Toggled(VkScroll);
        _keys = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(200) };
        _keys.Tick += (_, _) => CheckKeys();
        _keys.Start();
    }

    private void CheckKeys()
    {
        bool c = Toggled(VkCapital), n = Toggled(VkNumLock), s = Toggled(VkScroll);
        if (c != _caps) { _caps = c; LockChanged?.Invoke(LockKey.Caps, c); }
        if (n != _num) { _num = n; LockChanged?.Invoke(LockKey.Num, n); }
        if (s != _scroll) { _scroll = s; LockChanged?.Invoke(LockKey.Scroll, s); }
    }

    // ------------------------------------------------------------------ network
    private bool _netOn;

    private void SetNetwork(bool on)
    {
        if (on == _netOn) return;
        _netOn = on;
        if (on)
        {
            _net = ReadNetwork();
            _netKnown = true;
            _netDebounce = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(1.5) };
            _netDebounce.Tick += (_, _) => { _netDebounce!.Stop(); CheckNetwork(); };
            NetworkInformation.NetworkStatusChanged += OnNetworkStatus;
        }
        else
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatus;
            _netDebounce?.Stop();
            _netKnown = false;
        }
    }

    // Raised on a worker thread, often several times for one change: wait for it to settle, then compare.
    private void OnNetworkStatus(object? sender) => _ui.BeginInvoke(() => { if (_netDebounce is null) return; _netDebounce.Stop(); _netDebounce.Start(); });

    private void CheckNetwork()
    {
        var now = ReadNetwork();
        var before = _net;
        _net = now;
        if (!_netKnown || now == before) return;
        if (before.Kind != NetKind.None && (now.Kind != before.Kind || now.Name != before.Name))
            NetworkChanged?.Invoke(before.Kind, before.Name, false);
        if (now.Kind != NetKind.None) NetworkChanged?.Invoke(now.Kind, now.Name, true);
    }

    internal static NetKind KindOf(uint ianaType) => ianaType switch { 71 => NetKind.WiFi, 6 => NetKind.Ethernet, _ => NetKind.Other };

    private static (NetKind Kind, string Name) ReadNetwork()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p is null || p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) return (NetKind.None, "");
            var kind = p.IsWlanConnectionProfile ? NetKind.WiFi : KindOf(p.NetworkAdapter?.IanaInterfaceType ?? 0);
            return (kind, p.ProfileName ?? "");
        }
        catch { return (NetKind.None, ""); }
    }

    // ------------------------------------------------------------------ Bluetooth
    private const string ClassicBt = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}", LowEnergyBt = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private static readonly string[] BtProps = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.Category", "System.Devices.Aep.Bluetooth.Cod.Major" };

    private void SetBluetooth(bool on)
    {
        if (on == (_bt is not null)) return;
        if (!on)
        {
            foreach (var w in new[] { _bt, _ble }) StopWatcher(w);
            _bt = _ble = null; _btReady = _bleReady = false;
            lock (_btDevices) _btDevices.Clear();
            return;
        }
        _bt = BtWatcher(ClassicBt, () => _btReady = true);
        _ble = BtWatcher(LowEnergyBt, () => _bleReady = true);
    }

    private DeviceWatcher BtWatcher(string protocol, Action ready)
    {
        var aqs = $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
        var w = DeviceInformation.CreateWatcher(aqs, BtProps, DeviceInformationKind.AssociationEndpoint);
        w.Added += (_, d) => { lock (_btDevices) _btDevices[d.Id] = (d.Name, IsConnected(d.Properties), BtKind(d.Name, d.Properties)); };
        w.Updated += (_, u) => OnBtUpdate(u);
        w.Removed += (_, u) => { lock (_btDevices) _btDevices.Remove(u.Id); };
        w.EnumerationCompleted += (_, _) => ready();
        w.Start();
        return w;
    }

    private static bool IsConnected(IReadOnlyDictionary<string, object> props)
        => props.TryGetValue("System.Devices.Aep.IsConnected", out var v) && v is bool b && b;

    private void OnBtUpdate(DeviceInformationUpdate u)
    {
        if (!u.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var v) || v is not bool connected) return;
        string name, kind;
        bool was;
        lock (_btDevices)
        {
            if (!_btDevices.TryGetValue(u.Id, out var known)) return;
            (name, was, kind) = known;
            _btDevices[u.Id] = (name, connected, kind);
        }
        if (was == connected || !(_btReady && _bleReady) || string.IsNullOrWhiteSpace(name)) return;
        // Some devices drop and reconnect within a second (headsets switching profiles): announce once.
        if (Bounce("bt|" + name + connected, 4)) return;
        _ui.BeginInvoke(() => BluetoothChanged?.Invoke(name, kind, connected));
    }

    /// <summary>"audio", "input", "phone" or "" — from the Bluetooth class of device, or the name as a fallback.</summary>
    internal static string BtKind(string name, IReadOnlyDictionary<string, object>? props)
    {
        if (props is not null && props.TryGetValue("System.Devices.Aep.Bluetooth.Cod.Major", out var m) && m is not null)
        {
            var major = Convert.ToInt32(m);
            if (major == 4) return "audio";
            if (major == 5) return "input";
            if (major == 2) return "phone";
        }
        var n = name.ToLowerInvariant();
        if (n.Contains("bud") || n.Contains("pods") || n.Contains("headphone") || n.Contains("headset") || n.Contains("speaker") || n.Contains("wh-") || n.Contains("wf-")) return "audio";
        if (n.Contains("mouse") || n.Contains("keyboard") || n.Contains("trackpad") || n.Contains("controller") || n.Contains("gamepad")) return "input";
        if (n.Contains("phone") || n.Contains("galaxy") || n.Contains("pixel")) return "phone";
        return "";
    }

    // ------------------------------------------------------------------ USB
    private const string UsbDeviceInterface = "{A5DCBF10-6530-11D2-901F-00C04FB951ED}";
    private static readonly Guid ThisPcContainer = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    private void SetUsb(bool on)
    {
        if (on == (_usb is not null)) return;
        if (!on) { StopWatcher(_usb); _usb = null; _usbReady = false; lock (_usbDevices) _usbDevices.Clear(); return; }
        var aqs = $"System.Devices.InterfaceClassGuid:=\"{UsbDeviceInterface}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        _usb = DeviceInformation.CreateWatcher(aqs, new[] { "System.Devices.ContainerId" }, DeviceInformationKind.DeviceInterface);
        _usb.Added += (w, d) => { _ = OnUsbAdded(d); };
        _usb.Removed += (_, u) => OnUsbRemoved(u.Id);
        _usb.Updated += (_, _) => { };   // Windows only raises Removed when all three handlers are attached
        _usb.EnumerationCompleted += (_, _) => _usbReady = true;
        _usb.Start();
    }

    private async Task OnUsbAdded(DeviceInformation d)
    {
        var ready = _usbReady;
        var name = await UsbName(d);
        if (name is null) return;   // hubs, controllers and other parts of the PC itself
        lock (_usbDevices) _usbDevices[d.Id] = name;
        if (!ready || Bounce("usb+" + name, 3)) return;
        _ = _ui.BeginInvoke(() => UsbChanged?.Invoke(name, true));
    }

    private void OnUsbRemoved(string id)
    {
        string? name;
        lock (_usbDevices) { if (_usbDevices.Remove(id, out name) is false) return; }
        if (name is null || Bounce("usb-" + name, 3)) return;
        _ui.BeginInvoke(() => UsbChanged?.Invoke(name, false));
    }

    /// <summary>A friendly name from the device's container ("SanDisk Ultra", "G502 HERO"), or null for the PC's own parts.</summary>
    private static async Task<string?> UsbName(DeviceInformation d)
    {
        try
        {
            if (d.Properties.TryGetValue("System.Devices.ContainerId", out var c) && c is Guid container)
            {
                if (container == ThisPcContainer) return null;
                var info = await DeviceInformation.CreateFromIdAsync("{" + container + "}", Array.Empty<string>(), DeviceInformationKind.DeviceContainer);
                if (!string.IsNullOrWhiteSpace(info?.Name)) return Clean(info.Name);
            }
        }
        catch { }
        return string.IsNullOrWhiteSpace(d.Name) || IsGeneric(d.Name) ? null : Clean(d.Name);
    }

    internal static bool IsGeneric(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("hub") || n.Contains("host controller") || n.Contains("composite device") || n == "usb input device";
    }

    private static string Clean(string name) => name.Length > 40 ? name[..40].TrimEnd() + "…" : name.Trim();

    // ------------------------------------------------------------------ shared
    /// <summary>True when the same thing was announced within the last <paramref name="seconds"/>.</summary>
    private bool Bounce(string key, double seconds)
    {
        lock (_recent)
        {
            var now = DateTime.UtcNow;
            if (_recent.TryGetValue(key, out var at) && (now - at).TotalSeconds < seconds) return true;
            _recent[key] = now;
            if (_recent.Count > 64) _recent.Clear();
            return false;
        }
    }

    private static void StopWatcher(DeviceWatcher? w)
    {
        try { if (w is not null && w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop(); } catch { }
    }

    public void Dispose()
    {
        _keys?.Stop();
        if (_netOn) NetworkInformation.NetworkStatusChanged -= OnNetworkStatus;
        foreach (var w in new[] { _bt, _ble, _usb }) StopWatcher(w);
    }
}
