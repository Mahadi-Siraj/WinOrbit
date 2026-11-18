using System.Globalization;
using System.Windows.Threading;
using Microsoft.Win32;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// Notices the keyboard language switching (Win+Space, Alt+Shift, the taskbar's language button) so the island can
/// show which one is now in use.
///
/// Windows tells only the app you're typing in when its keyboard changes, so this asks Windows which keyboard the
/// app in front is using — four times a second, and only while more than one keyboard is installed. With a single
/// keyboard nothing runs; adding or removing one is noticed through Windows' registry change notification on
/// <c>HKCU\Keyboard Layout\Preload</c>. It reads the layout's ID only — never keystrokes.
/// </summary>
public sealed class KeyboardLanguage : IDisposable
{
    /// <summary>(short code like "EN", language name, layout name — empty when it says nothing more). UI thread.</summary>
    public event Action<string, string, string>? Changed;

    private readonly Dispatcher _ui;
    private DispatcherTimer? _timer;
    private RegistryWatcher? _list;
    private bool _on;
    // The keyboard each window last had. Windows can keep one per app, so a different app in front with a
    // different keyboard is not a switch; the same window with a new keyboard is.
    private readonly Dictionary<IntPtr, IntPtr> _seen = new();
    private static readonly Dictionary<long, string> LayoutNames = new();

    public KeyboardLanguage(Dispatcher ui) => _ui = ui;

    public void SetEnabled(bool on)
    {
        if (on == _on) return;
        _on = on;
        if (!on) { _timer?.Stop(); _timer = null; _list?.Dispose(); _list = null; _seen.Clear(); return; }
        try
        {
            if (Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload") is { } key)
                _list = new RegistryWatcher(key, () => _ui.BeginInvoke(Rearm));
        }
        catch (Exception ex) { Log.Warn("Keyboard list watch: " + ex.Message); }
        Rearm();
    }

    /// <summary>Run the check only while there is more than one keyboard to switch between.</summary>
    private void Rearm()
    {
        if (!_on) return;
        var many = Native.GetKeyboardLayoutList(0, null) > 1;
        if (!many) { _timer?.Stop(); _timer = null; _seen.Clear(); return; }
        if (_timer is not null) return;
        _seen.Clear();
        Check(announce: false);
        _timer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Check(announce: true);
        _timer.Start();
    }

    private void Check(bool announce)
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        var layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(window, out _));
        if (layout == IntPtr.Zero) return;
        var had = _seen.TryGetValue(window, out var before);
        if (_seen.Count > 64) _seen.Clear();
        _seen[window] = layout;
        if (!announce || !had || before == layout) return;
        var (code, language, name) = Describe(layout.ToInt64());
        Changed?.Invoke(code, language, name);
    }

    /// <summary>A layout handle → ("EN", "English (United States)", "US").</summary>
    internal static (string Code, string Language, string Layout) Describe(long hkl)
    {
        var langId = (int)(hkl & 0xFFFF);
        string code, language;
        try
        {
            var ci = CultureInfo.GetCultureInfo(langId);
            code = ci.TwoLetterISOLanguageName.ToUpperInvariant();
            language = ci.DisplayName;
        }
        catch (CultureNotFoundException) { code = langId.ToString("X4"); language = "Keyboard"; }
        var layout = LayoutName(hkl) ?? "";
        // "English (United States)" over "US" reads well; repeating the language as the layout doesn't.
        if (layout.Length > 0 && language.StartsWith(layout, StringComparison.OrdinalIgnoreCase)) layout = "";
        return (code, language, layout);
    }

    /// <summary>
    /// The registry key name (KLID) for a layout handle, when it can be worked out from the handle alone:
    /// 0x04090409 → "00000409", 0x08090409 (UK keys, US English) → "00000809", 0xE0010411 (an IME) → "E0010411".
    /// Handles with a 0xF… high word name their layout by "Layout Id" instead and return null here.
    /// </summary>
    internal static string? Klid(long hkl)
    {
        var hi = (int)((hkl >> 16) & 0xFFFF);
        if ((hi & 0xF000) == 0xF000) return null;
        if ((hi & 0xF000) == 0xE000) return ((uint)hkl).ToString("X8");
        return hi.ToString("X8");
    }

    private static string? LayoutName(long hkl)
    {
        lock (LayoutNames)
            if (LayoutNames.TryGetValue(hkl, out var known)) return known;
        string? name = null;
        try
        {
            using var all = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
            if (all is not null)
            {
                if (Klid(hkl) is { } klid)
                {
                    using var k = all.OpenSubKey(klid);
                    name = k?.GetValue("Layout Text") as string;
                }
                else
                {
                    var id = ((int)((hkl >> 16) & 0x0FFF)).ToString("X4");
                    foreach (var sub in all.GetSubKeyNames())
                    {
                        using var k = all.OpenSubKey(sub);
                        if (string.Equals(k?.GetValue("Layout Id") as string, id, StringComparison.OrdinalIgnoreCase)) { name = k!.GetValue("Layout Text") as string; break; }
                    }
                }
            }
        }
        catch { }
        lock (LayoutNames) LayoutNames[hkl] = name ?? "";
        return name;
    }

    public void Dispose() => SetEnabled(false);
}
