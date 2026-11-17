using System.Runtime.InteropServices;
using System.Windows.Threading;
using Glasswave.Core;
using Glasswave.Interop;

namespace Glasswave.Services;

/// <summary>
/// Event-driven audio state for the island: Windows calls us when the volume or mute changes (keyboard keys,
/// the taskbar flyout, another app) and when the default output device changes. Nothing is polled. The peak
/// meter is read on demand, only while music is playing and the capsule is on screen.
/// </summary>
public sealed class AudioWatcher : IDisposable
{
    /// <summary>Volume 0..1 and mute, raised on the UI thread.</summary>
    public event Action<double, bool>? VolumeChanged;

    private readonly Dispatcher _ui;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;
    private IAudioMeterInformation? _meter;
    private IntPtr _callbackPtr;
    private VolumeCallback? _callback;
    private DeviceClient? _client;
    private DateTime _suppressUntil;

    public AudioWatcher(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            _client = new DeviceClient(this);
            _enumerator.RegisterEndpointNotificationCallback(_client);
            Attach();
        }
        catch (Exception ex) { Log.Warn("Audio events unavailable: " + ex.Message); }
    }

    /// <summary>Don't announce changes Glasswave itself makes for a moment (its own sliders already show the value).</summary>
    public void SuppressBriefly() => _suppressUntil = DateTime.UtcNow.AddMilliseconds(400);

    private void Attach()
    {
        Detach();
        try
        {
            if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out var vol) == 0) _volume = (IAudioEndpointVolume)vol;
            var mid = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref mid, 23, IntPtr.Zero, out var meter) == 0) _meter = (IAudioMeterInformation)meter;
            if (_volume is not null)
            {
                _callback = new VolumeCallback(this);
                _callbackPtr = Marshal.GetComInterfaceForObject(_callback, typeof(IAudioEndpointVolumeCallback));
                _volume.RegisterControlChangeNotify(_callbackPtr);
            }
        }
        catch (Exception ex) { Log.Warn("Audio attach: " + ex.Message); }
    }

    private void Detach()
    {
        try { if (_volume is not null && _callbackPtr != IntPtr.Zero) _volume.UnregisterControlChangeNotify(_callbackPtr); } catch { }
        if (_callbackPtr != IntPtr.Zero) { Marshal.Release(_callbackPtr); _callbackPtr = IntPtr.Zero; }
        _volume = null; _meter = null; _callback = null;
    }

    /// <summary>Current output peak 0..1 (what is audible right now), or 0 when unavailable.</summary>
    public double Peak()
    {
        try { return _meter is not null && _meter.GetPeakValue(out var p) == 0 ? Math.Clamp(p, 0, 1) : 0; }
        catch { return 0; }
    }

    private void OnNotify(IntPtr data)
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: GUID context (16 bytes), BOOL muted, float master volume, ...
        var muted = Marshal.ReadInt32(data, 16) != 0;
        var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        if (DateTime.UtcNow < _suppressUntil) return;
        _ui.BeginInvoke(() => VolumeChanged?.Invoke(Math.Clamp(level, 0, 1), muted));
    }

    public void Dispose()
    {
        Detach();
        try { if (_enumerator is not null && _client is not null) _enumerator.UnregisterEndpointNotificationCallback(_client); } catch { }
    }

    private sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly AudioWatcher _owner;
        public VolumeCallback(AudioWatcher owner) => _owner = owner;
        public int OnNotify(IntPtr data) { try { _owner.OnNotify(data); } catch { } return 0; }
    }

    private sealed class DeviceClient : IMMNotificationClient
    {
        private readonly AudioWatcher _owner;
        public DeviceClient(AudioWatcher owner) => _owner = owner;
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == 0 && role == 1) _owner._ui.BeginInvoke(_owner.Attach);   // eRender, eMultimedia
            return 0;
        }
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }
}
