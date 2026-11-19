using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// The default microphone's level and mute switch, for the island's microphone pill. It reads Windows' own peak
/// meter on the capture device: that never opens the microphone or records anything, it only reports how loud
/// the stream another app already has open is. Read on demand (20 times a second while the pill is on screen).
/// </summary>
public sealed class MicMeter
{
    private IAudioEndpointVolume? _volume;
    private IAudioMeterInformation? _meter;

    /// <summary>Look up the current default microphone again (call when the pill appears).</summary>
    public void Attach()
    {
        _volume = null; _meter = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(1, 0, out var device) != 0) return;   // eCapture, eConsole
            var vid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref vid, 23, IntPtr.Zero, out var vol) == 0) _volume = (IAudioEndpointVolume)vol;
            var mid = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref mid, 23, IntPtr.Zero, out var meter) == 0) _meter = (IAudioMeterInformation)meter;
        }
        catch (Exception ex) { Log.Warn("Microphone meter unavailable: " + ex.Message); }
    }

    public bool Available => _volume is not null;

    /// <summary>Peak level 0..1 of what the microphone hears right now, or 0.</summary>
    public double Peak()
    {
        try { return _meter is not null && _meter.GetPeakValue(out var p) == 0 ? Math.Clamp(p, 0, 1) : 0; }
        catch { return 0; }
    }

    /// <summary>Windows' microphone mute (the same switch as the taskbar and Settings), or null with no microphone.</summary>
    public bool? Muted
    {
        get
        {
            try { return _volume is not null && _volume.GetMute(out var m) == 0 ? m : null; }
            catch { return null; }
        }
    }

    public bool SetMuted(bool mute)
    {
        try
        {
            var ctx = Guid.Empty;
            return _volume is not null && _volume.SetMute(mute, ref ctx) == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Bar height for a peak: speech sits around 0.02–0.3, so a square-root curve lets a normal voice move the bars
    /// well without loud sounds pinning them. Silence keeps a small resting height.
    /// </summary>
    internal static double Level(double peak) => Math.Clamp(0.18 + Math.Sqrt(Math.Clamp(peak, 0, 1)) * 1.1, 0.18, 1);

    /// <summary>The pill's words: the app (and how many others), plus "muted" while Windows' microphone mute is on.</summary>
    internal static string Label(IReadOnlyList<string> apps, bool muted)
    {
        var who = apps.Count switch { 0 => "Microphone", 1 => apps[0], _ => $"{apps[0]} +{apps.Count - 1}" };
        return muted ? who + " · muted" : who;
    }
}
