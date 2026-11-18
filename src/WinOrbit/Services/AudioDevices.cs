using System.Runtime.InteropServices;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// The playback devices (speakers, headphones, HDMI, Bluetooth…) and which one is the default, for the island's
/// output picker. Listing uses the documented Core Audio API. Changing the default uses <see cref="IPolicyConfig"/>,
/// which Windows doesn't document; if it fails, the picker opens Windows' Sound settings instead.
/// </summary>
public static class AudioDevices
{
    public sealed record Device(string Id, string Name, bool IsDefault);

    private const int Render = 0, Active = 1, Multimedia = 1;
    // PKEY_Device_FriendlyName: "Speakers (Realtek(R) Audio)".
    private static readonly PropertyKey FriendlyName = new() { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };

    /// <summary>Active playback devices, default first, then by name.</summary>
    public static List<Device> List()
    {
        var list = new List<Device>();
        IMMDeviceEnumerator? en = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            string? defaultId = null;
            if (en.GetDefaultAudioEndpoint(Render, Multimedia, out var def) == 0 && def is not null)
            {
                defaultId = IdOf(def);
                Marshal.ReleaseComObject(def);
            }
            if (en.EnumAudioEndpoints(Render, Active, out var ptr) != 0 || ptr == IntPtr.Zero) return list;
            var all = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);
            try
            {
                all.GetCount(out var n);
                for (var i = 0; i < n; i++)
                {
                    if (all.Item(i, out var d) != 0 || d is null) continue;
                    try
                    {
                        if (IdOf(d) is { } id) list.Add(new Device(id, NameOf(d) ?? "Audio device", id == defaultId));
                    }
                    finally { Marshal.ReleaseComObject(d); }
                }
            }
            finally { Marshal.ReleaseComObject(all); }
        }
        catch (Exception ex) { Log.Warn("Audio devices: " + ex.Message); }
        finally { if (en is not null) Marshal.ReleaseComObject(en); }
        return Order(list);
    }

    internal static List<Device> Order(IEnumerable<Device> devices) =>
        devices.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>"Speakers (Realtek(R) Audio)" → ("Speakers", "Realtek(R) Audio"); names without brackets stay whole.</summary>
    internal static (string Name, string Detail) Split(string friendly)
    {
        var open = friendly.IndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || !friendly.EndsWith(')')) return (friendly, "");
        return (friendly[..open], friendly[(open + 2)..^1]);
    }

    /// <summary>Make a device the default for music, games and calls alike — what Windows' Sound settings does.</summary>
    public static bool SetDefault(string id)
    {
        IPolicyConfig? pc = null;
        try
        {
            pc = (IPolicyConfig)new PolicyConfigCom();
            var ok = true;
            for (var role = 0; role <= 2; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;   // console, multimedia, communications
            return ok;
        }
        catch (Exception ex) { Log.Warn("Set default audio device: " + ex.Message); return false; }
        finally { if (pc is not null) Marshal.ReleaseComObject(pc); }
    }

    private static string? IdOf(IMMDevice d)
    {
        if (d.GetId(out var p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    private static string? NameOf(IMMDevice d)
    {
        if (d.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store is null) return null;
        try
        {
            var key = FriendlyName;
            if (store.GetValue(ref key, out var v) != 0) return null;
            try { return v.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(v.Pointer) : null; }
            finally { PropVariantNative.PropVariantClear(ref v); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
}
