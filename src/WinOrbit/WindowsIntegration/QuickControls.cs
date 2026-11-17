using System.IO;
using System.Runtime.InteropServices;
using WinOrbit.Core;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace WinOrbit.WindowsIntegration;

/// <summary>
/// Direct on/off control of Windows features for the island's quick tiles. Every control reads the real state
/// first and reports "not available" instead of guessing. Nothing here needs administrator rights.
/// </summary>
public static class QuickControls
{
    // ----------------------------------------------------------------- Wi-Fi and Bluetooth (supported API)
    private static IReadOnlyList<Radio>? _radios;
    private static bool _radioHooks;

    /// <summary>Raised (on a background thread) when a radio changes state, including from Windows' own UI.</summary>
    public static event Action? RadiosChanged;

    /// <summary>Windows.Devices.Radios — the documented API behind the Wi-Fi and Bluetooth quick settings.</summary>
    public static async Task<Radio?> FindRadio(RadioKind kind)
    {
        try
        {
            if (_radios is null)
            {
                var access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed) { Log.Warn($"Radio access: {access}"); return null; }
                _radios = await Radio.GetRadiosAsync();
            }
            var radio = _radios.FirstOrDefault(r => r.Kind == kind);
            if (radio is not null && !_radioHooks)
            {
                _radioHooks = true;
                foreach (var r in _radios) r.StateChanged += (_, _) => RadiosChanged?.Invoke();
            }
            return radio;
        }
        catch (Exception ex) { Log.Warn($"Radios unavailable: {ex.Message}"); return null; }
    }

    /// <summary>Returns null when the radio is missing or Windows refuses (e.g. privacy setting or airplane mode policy).</summary>
    public static async Task<bool?> SetRadio(RadioKind kind, bool on)
    {
        var radio = await FindRadio(kind);
        if (radio is null) return null;
        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            Log.Info($"{kind} radio → {(on ? "on" : "off")}: {result}");
            return result == RadioAccessStatus.Allowed ? radio.State == RadioState.On : null;
        }
        catch (Exception ex) { Log.Warn($"Set {kind} radio failed: {ex.Message}"); return null; }
    }

    // ----------------------------------------------------------------- Dark mode (supported registry values)
    public static bool WindowsDark => Tweaks.GetInt(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) == 0;

    /// <summary>Switch apps and Windows between dark and light, through the backed-up tweaks so Revert restores it.</summary>
    public static void SetWindowsDark(bool dark)
    {
        foreach (var id in new[] { "apps-theme", "system-theme" })
            if (Tweaks.All.FirstOrDefault(t => t.Id == id) is { } t) Tweaks.Apply(t, dark ? 0 : 1);
    }

    // ----------------------------------------------------------------- Night light (undocumented, experimental)
    // Windows keeps night-light state in a small CloudStore blob. There is no public API; this flips the same
    // fields Windows' own toggle flips. The blob is validated first and the original is saved once, so a
    // format change in a future Windows makes the tile fall back to opening Settings instead of writing.
    private const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private static byte[]? NightBlob()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(NightKey); return k?.GetValue("Data") as byte[]; } catch { return null; }
    }

    internal static bool? NightLightState(byte[]? d)
    {
        if (d is null || d.Length < 25 || d[0] != 0x43 || d[1] != 0x42 || d[2] != 0x01 || d[3] != 0x00) return null;
        if (d[18] == 0x15 && d[23] == 0x10 && d[24] == 0x00) return true;
        if (d[18] == 0x13) return false;
        return null;
    }

    /// <summary>Pure transform of the blob, unit tested. Returns null if the blob isn't in the expected format.</summary>
    internal static byte[]? NightLightToggled(byte[] d, bool on)
    {
        var state = NightLightState(d);
        if (state is null) return null;
        if (state == on) return d;
        var list = d.ToList();
        if (on) { list[18] = 0x15; list.InsertRange(23, new byte[] { 0x10, 0x00 }); }
        else { list[18] = 0x13; list.RemoveRange(23, 2); }
        // Bump the change counter (bytes 10–14) so Windows notices the new state.
        for (var i = 10; i < 15; i++) { if (list[i] != 0xFF) { list[i]++; break; } }
        return list.ToArray();
    }

    public static bool? NightLightOn => NightLightState(NightBlob());

    public static bool SetNightLight(bool on)
    {
        var d = NightBlob();
        if (d is null) return false;
        var next = NightLightToggled(d, on);
        if (next is null) { Log.Warn("Night light: unrecognised state format; not changing it"); return false; }
        try
        {
            var backup = Path.Combine(Paths.Backups, "nightlight-state.bin");
            if (!File.Exists(backup)) File.WriteAllBytes(backup, d);
            using var k = Registry.CurrentUser.OpenSubKey(NightKey, true);
            k?.SetValue("Data", next, RegistryValueKind.Binary);
            Log.Info($"Night light → {(on ? "on" : "off")}");
            return true;
        }
        catch (Exception ex) { Log.Warn($"Night light failed: {ex.Message}"); return false; }
    }

    // ----------------------------------------------------------------- Keep awake (documented power request)
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;

    /// <summary>While on, Windows won't sleep or turn the screen off. Lasts until switched off or WinOrbit quits. Call on the UI thread.</summary>
    public static bool KeepAwake { get; private set; }

    public static void SetKeepAwake(bool on)
    {
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous);
        KeepAwake = on;
        Log.Info($"Keep awake → {(on ? "on" : "off")}");
    }

    // ----------------------------------------------------------------- Power mode (Windows' power-mode slider)
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public static readonly (string Name, Guid Id)[] PowerModes =
    {
        ("Efficiency", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")),
        ("Balanced", Guid.Empty),
        ("Performance", new Guid("ded574b5-45a0-4f42-8737-46345c09c238")),
    };

    private static readonly Guid BalancedPlan = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    /// <summary>Index into <see cref="PowerModes"/>, or null when the slider isn't available (a non-Balanced plan is active).</summary>
    public static int? PowerModeIndex()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return null;
            var plan = Marshal.PtrToStructure<Guid>(p);
            LocalFree(p);
            if (plan != BalancedPlan) return null;
            if (PowerGetEffectiveOverlayScheme(out var overlay) != 0) return null;
            var i = Array.FindIndex(PowerModes, m => m.Id == overlay);
            return i < 0 ? 1 : i;
        }
        catch { return null; }
    }

    public static bool SetPowerMode(int index)
    {
        try
        {
            var ok = PowerSetActiveOverlayScheme(PowerModes[Math.Clamp(index, 0, PowerModes.Length - 1)].Id) == 0;
            Log.Info($"Power mode → {PowerModes[index].Name}: {(ok ? "ok" : "refused")}");
            return ok;
        }
        catch (Exception ex) { Log.Warn($"Power mode failed: {ex.Message}"); return false; }
    }
}
