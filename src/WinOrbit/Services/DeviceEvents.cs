using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using WinOrbit.Core;
using WinOrbit.WindowsIntegration;

namespace WinOrbit.Services;

/// <summary>
/// Connection and key-state changes for the island: Caps/Num/Scroll Lock, network (Wi-Fi or Ethernet) coming and
/// going, Bluetooth devices connecting and disconnecting, and USB devices being plugged in or removed.
///
/// Privacy and cost:
///   • Lock keys: WinOrbit asks Windows whether the three lock lights are on, about eight times a second, only while that
///     alert is switched on. It never sees which keys you type.
///   • Network: the Wi-Fi service (WLAN API) pushes "connected"/"disconnected" the moment the radio associates, and
///     Windows' NetworkStatusChanged covers Ethernet and anything else; only the connection's name and type are read.
///   • Radios: the Wi-Fi and Bluetooth radios' own StateChanged events (switched on or off from anywhere).
///   • Bluetooth and USB: Windows device watchers push added/removed/changed events; only device names are read.
///     Paired Bluetooth devices also report ConnectionStatusChanged, which arrives seconds before the watcher's update.
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
    /// <summary>(Wi-Fi or Bluetooth, switched on).</summary>
    public event Action<RadioKind, bool>? RadioChanged;
    /// <summary>("dark", "night" or "airplane", now on). "dark" off means light mode.</summary>
    public event Action<string, bool>? ModeChanged;

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
        try { SetWlan(on && s.NetworkAlerts); } catch (Exception ex) { Log.Warn("Wi-Fi notifications unavailable: " + ex.Message); }
        try { SetBluetooth(on && s.BluetoothAlerts); } catch (Exception ex) { Log.Warn("Bluetooth alerts unavailable: " + ex.Message); }
        try { SetRadios(on && s.NetworkAlerts, on && s.BluetoothAlerts); } catch (Exception ex) { Log.Warn("Radio alerts unavailable: " + ex.Message); }
        try { SetModes(on && s.SystemAlerts); } catch (Exception ex) { Log.Warn("Mode alerts unavailable: " + ex.Message); }
        try { SetUsb(on && s.UsbAlerts); } catch (Exception ex) { Log.Warn("USB alerts unavailable: " + ex.Message); }
    }

    // ------------------------------------------------------------------ lock keys
    private static bool Toggled(int vk) => (GetKeyState(vk) & 1) != 0;

    private void SetKeys(bool on)
    {
        if (!on) { _keys?.Stop(); _keys = null; return; }
        if (_keys is not null) return;
        _caps = Toggled(VkCapital); _num = Toggled(VkNumLock); _scroll = Toggled(VkScroll);
        // 120 ms: below what a person perceives as a delay after pressing the key; three bit reads per tick.
        _keys = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromMilliseconds(120) };
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
            _netDebounce = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromSeconds(0.5) };
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
            AnnounceNet(before.Kind, before.Name, false);
        if (now.Kind != NetKind.None) AnnounceNet(now.Kind, now.Name, true);
    }

    /// <summary>One way out for both network sources, so the fast Wi-Fi path and the general path never double up.</summary>
    private async void AnnounceNet(NetKind kind, string name, bool connected)
    {
        if (!connected && kind == NetKind.WiFi)
        {
            // Switching the Wi-Fi radio off also drops the connection. Give the radio's own event a moment and
            // let "Wi-Fi off" speak for both, instead of two pop-ups back to back.
            await Task.Delay(450);
            if (_wifiRadio is { } r && r.State != RadioState.On) return;
        }
        if (Bounce($"net|{kind}|{name}|{connected}", 10)) return;
        lock (_recent) _recent.Remove($"net|{kind}|{name}|{!connected}");   // a quick flip the other way still shows
        NetworkChanged?.Invoke(kind, name, connected);
    }

    // ------------------------------------------------------------------ Wi-Fi service notifications (instant)
    [StructLayout(LayoutKind.Sequential)]
    private struct WlanNotification { public uint Source, Code; public Guid Interface; public uint Size; public IntPtr Data; }
    private delegate void WlanCallback(IntPtr data, IntPtr context);
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(IntPtr handle, uint source, bool ignoreDuplicate, WlanCallback? callback, IntPtr context, IntPtr reserved, out uint previous);
    private const uint WlanSourceAcm = 0x8, AcmConnectionComplete = 10, AcmDisconnected = 21;
    private IntPtr _wlan;
    private WlanCallback? _wlanCallback;   // kept alive while registered

    private void SetWlan(bool on)
    {
        if (on == (_wlan != IntPtr.Zero)) return;
        if (!on)
        {
            WlanRegisterNotification(_wlan, 0, true, null, IntPtr.Zero, IntPtr.Zero, out _);
            WlanCloseHandle(_wlan, IntPtr.Zero);
            _wlan = IntPtr.Zero; _wlanCallback = null;
            return;
        }
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return;   // no Wi-Fi service (desktop PC without Wi-Fi)
        _wlanCallback = OnWlan;
        if (WlanRegisterNotification(h, WlanSourceAcm, true, _wlanCallback, IntPtr.Zero, IntPtr.Zero, out _) != 0) { WlanCloseHandle(h, IntPtr.Zero); _wlanCallback = null; return; }
        _wlan = h;
    }

    // Raised on a Wi-Fi service thread. WLAN_CONNECTION_NOTIFICATION_DATA: mode (4 bytes), profile name (256 UTF-16
    // chars), SSID (4 + 32), BSS type (4), security (4), reason code (4) at byte 560.
    private void OnWlan(IntPtr data, IntPtr context)
    {
        try
        {
            var n = Marshal.PtrToStructure<WlanNotification>(data);
            if (n.Source != WlanSourceAcm || (n.Code != AcmConnectionComplete && n.Code != AcmDisconnected) || n.Data == IntPtr.Zero || n.Size < 564) return;
            var connected = n.Code == AcmConnectionComplete;
            if (connected && Marshal.ReadInt32(n.Data, 560) != 0) return;   // a failed attempt
            var name = Marshal.PtrToStringUni(n.Data + 4) ?? "";
            _ui.BeginInvoke(() =>
            {
                if (!_netKnown) return;
                if (connected) _net = (NetKind.WiFi, name);
                else if (_net.Kind == NetKind.WiFi) _net = (NetKind.None, "");
                AnnounceNet(NetKind.WiFi, name, connected);
            });
        }
        catch { }
    }

    // ------------------------------------------------------------------ Wi-Fi and Bluetooth radios
    private Radio? _wifiRadio, _btRadio;
    private bool _radiosHooked, _wifiRadioWanted, _btRadioWanted;
    private readonly Dictionary<RadioKind, bool> _radioOn = new();

    private async void SetRadios(bool wifi, bool bt)
    {
        _wifiRadioWanted = wifi; _btRadioWanted = bt;
        if (_radiosHooked || !(wifi || bt)) return;
        _radiosHooked = true;   // once found, the radios stay subscribed (one cheap event each); the flags above gate them
        try
        {
            _wifiRadio = await QuickControls.FindRadio(RadioKind.WiFi);
            _btRadio = await QuickControls.FindRadio(RadioKind.Bluetooth);
            foreach (var r in new[] { _wifiRadio, _btRadio })
            {
                if (r is null) continue;
                lock (_radioOn) _radioOn[r.Kind] = r.State == RadioState.On;
                r.StateChanged += OnRadioState;
            }
        }
        catch (Exception ex) { Log.Warn("Radios unavailable: " + ex.Message); }
    }

    private void OnRadioState(Radio r, object _)
    {
        var on = r.State == RadioState.On;
        lock (_radioOn)
        {
            if (_radioOn.TryGetValue(r.Kind, out var was) && was == on) return;   // Windows repeats the same state
            _radioOn[r.Kind] = on;
        }
        if (r.Kind == RadioKind.WiFi ? !_wifiRadioWanted : !_btRadioWanted) return;
        _ui.BeginInvoke(async () =>
        {
            // Airplane mode switches every radio at once; its own card says it, so the radios stay quiet.
            await Task.Delay(600);
            if ((DateTime.UtcNow - _airplaneAt).TotalSeconds < 3) return;
            RadioChanged?.Invoke(r.Kind, on);
        });
    }

    // ------------------------------------------------------------------ dark/light, night light, airplane mode
    // Each is a registry value Windows writes when you flip it. RegNotifyChangeKeyValue lets Windows wake us on a
    // write to that one key — no polling. Values are compared, so unrelated writes to the same key stay silent.
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private const string AirplaneKey = @"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState";
    private readonly List<RegistryWatcher> _modeWatchers = new();
    private readonly Dictionary<string, bool?> _modes = new();
    private readonly Dictionary<string, DispatcherTimer> _modeSettle = new();
    private DateTime _airplaneAt = DateTime.MinValue;

    internal static bool? ReadDark() => QuickControls.WindowsDark;
    internal static bool? ReadAirplane()
    {
        try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(AirplaneKey); return k?.GetValue("") is int v ? v == 1 : null; }
        catch { return null; }
    }

    private void SetModes(bool on)
    {
        if (on == (_modeWatchers.Count > 0)) return;
        if (!on) { foreach (var w in _modeWatchers) w.Dispose(); _modeWatchers.Clear(); return; }
        Watch("dark", Microsoft.Win32.Registry.CurrentUser, PersonalizeKey, ReadDark);
        Watch("night", Microsoft.Win32.Registry.CurrentUser, NightKey, () => QuickControls.NightLightOn);
        Watch("airplane", Microsoft.Win32.Registry.LocalMachine, AirplaneKey, ReadAirplane);
    }

    private void Watch(string mode, Microsoft.Win32.RegistryKey root, string path, Func<bool?> read)
    {
        try
        {
            var key = root.OpenSubKey(path);
            if (key is null) return;   // e.g. night light never used on this PC
            _modes[mode] = read();
            _modeWatchers.Add(new RegistryWatcher(key, () =>
            {
                if (mode == "airplane") _airplaneAt = DateTime.UtcNow;
                _ui.BeginInvoke(() => Settle(mode, read));
            }));
        }
        catch (Exception ex) { Log.Warn($"Watching {mode} failed: {ex.Message}"); }
    }

    // Windows often writes a key two or three times for one flip (apps theme, then system theme): wait 200 ms
    // for the writes to finish, then compare with what we saw last.
    private void Settle(string mode, Func<bool?> read)
    {
        if (!_modeSettle.TryGetValue(mode, out var t))
        {
            t = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromMilliseconds(200) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                var now = read();
                _modes.TryGetValue(mode, out var before);
                _modes[mode] = now;
                if (now is { } v && before is { } b && v != b)
                {
                    if (mode == "airplane") _airplaneAt = DateTime.UtcNow;
                    ModeChanged?.Invoke(mode, v);
                }
            };
            _modeSettle[mode] = t;
        }
        t.Stop(); t.Start();
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
            lock (_btLinks) { foreach (var d in _btLinks.Values) try { d.Dispose(); } catch { } _btLinks.Clear(); }
            return;
        }
        _btOn = true;
        _bt = BtWatcher(ClassicBt, false, () => _btReady = true);
        _ble = BtWatcher(LowEnergyBt, true, () => _bleReady = true);
    }

    private DeviceWatcher BtWatcher(string protocol, bool lowEnergy, Action ready)
    {
        var aqs = $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
        var w = DeviceInformation.CreateWatcher(aqs, BtProps, DeviceInformationKind.AssociationEndpoint);
        w.Added += (sender, d) =>
        {
            lock (_btDevices) _btDevices[d.Id] = (d.Name, IsConnected(d.Properties), BtKind(d.Name, d.Properties));
            _ = Link(d.Id, lowEnergy);
        };
        w.Updated += (_, u) => { if (u.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var v) && v is bool c) BtSet(u.Id, c); };
        w.Removed += (_, u) =>
        {
            lock (_btDevices) _btDevices.Remove(u.Id);
            lock (_btLinks) if (_btLinks.Remove(u.Id, out var l)) try { l.Dispose(); } catch { }
        };
        w.EnumerationCompleted += (_, _) => ready();
        w.Start();
        return w;
    }

    // The fast path: the Bluetooth stack raises ConnectionStatusChanged as soon as the link comes up or drops,
    // a few seconds before the device watcher's property update. Holding the object does not connect anything.
    private readonly Dictionary<string, IDisposable> _btLinks = new();
    private volatile bool _btOn;

    private async Task Link(string id, bool lowEnergy)
    {
        try
        {
            IDisposable? link = null;
            if (lowEnergy)
            {
                var d = await BluetoothLEDevice.FromIdAsync(id);
                if (d is not null) { d.ConnectionStatusChanged += (s, _) => BtSet(id, s.ConnectionStatus == BluetoothConnectionStatus.Connected); link = d; }
            }
            else
            {
                var d = await BluetoothDevice.FromIdAsync(id);
                if (d is not null) { d.ConnectionStatusChanged += (s, _) => BtSet(id, s.ConnectionStatus == BluetoothConnectionStatus.Connected); link = d; }
            }
            if (link is null) return;
            lock (_btLinks)
            {
                if (!_btOn) { link.Dispose(); return; }   // switched off meanwhile
                if (_btLinks.Remove(id, out var old)) try { old.Dispose(); } catch { }
                _btLinks[id] = link;
            }
        }
        catch { /* the watcher's updates still cover this device */ }
    }

    private static bool IsConnected(IReadOnlyDictionary<string, object> props)
        => props.TryGetValue("System.Devices.Aep.IsConnected", out var v) && v is bool b && b;

    /// <summary>Both Bluetooth sources end here; the stored state makes the second one to arrive a no-op.</summary>
    private void BtSet(string id, bool connected)
    {
        string name, kind;
        bool was;
        lock (_btDevices)
        {
            if (!_btDevices.TryGetValue(id, out var known)) return;
            (name, was, kind) = known;
            _btDevices[id] = (name, connected, kind);
        }
        if (was == connected || !(_btReady && _bleReady) || string.IsNullOrWhiteSpace(name)) return;
        // Some devices drop and reconnect within a second (headsets switching profiles): announce once.
        if (Bounce("bt|" + name + connected, 4)) return;
        _ui.BeginInvoke(async () =>
        {
            if (!connected)
            {
                // Bluetooth switched off drops every device at once; "Bluetooth off" says it all.
                await Task.Delay(450);
                if (_btRadio is { } r && r.State != RadioState.On) return;
            }
            BluetoothChanged?.Invoke(name, kind, connected);
        });
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
        try { SetWlan(false); } catch { }
        try { SetModes(false); } catch { }
        foreach (var r in new[] { _wifiRadio, _btRadio }) if (r is not null) r.StateChanged -= OnRadioState;
        foreach (var w in new[] { _bt, _ble, _usb }) StopWatcher(w);
        lock (_btLinks) { foreach (var d in _btLinks.Values) try { d.Dispose(); } catch { } _btLinks.Clear(); }
    }
}
