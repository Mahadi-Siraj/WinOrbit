using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// One hidden top-level window that receives everything the shell tells applications:
/// global hotkeys, tray icon clicks, and the WM_SETTINGCHANGE / WM_DISPLAYCHANGE broadcasts
/// that tell us the wallpaper, theme or monitor layout changed. No polling required.
/// </summary>
public sealed class ShellHost : IDisposable
{
    private const int TrayMessage = Native.WM_APP + 1;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _hotkeys = new();
    private readonly uint _taskbarCreated;
    private Native.NOTIFYICONDATA _tray;
    private bool _trayAdded;
    private IntPtr _icon;
    private int _nextId = 1;

    public IntPtr Handle => _source.Handle;
    public event Action<string>? SettingChanged;
    public event Action? DisplayChanged;
    public event Action? TrayLeftClick;
    public event Action? TrayRightClick;

    public ShellHost()
    {
        var p = new HwndSourceParameters("WinOrbitShellHost")
        {
            Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = Native.WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY:
                if (_hotkeys.TryGetValue(wParam.ToInt32(), out var action)) { try { action(); } catch (Exception ex) { Log.Error("Hotkey", ex); } }
                handled = true;
                break;
            case Native.WM_SETTINGCHANGE:
                SettingChanged?.Invoke(lParam == IntPtr.Zero ? "" : Marshal.PtrToStringUni(lParam) ?? "");
                break;
            case Native.WM_DISPLAYCHANGE:
                Monitors.Invalidate();
                DisplayChanged?.Invoke();
                break;
            case TrayMessage:
                var m = lParam.ToInt32() & 0xFFFF;
                if (m is Native.WM_LBUTTONUP or Native.WM_LBUTTONDBLCLK) TrayLeftClick?.Invoke();
                else if (m is Native.WM_RBUTTONUP or Native.WM_CONTEXTMENU) TrayRightClick?.Invoke();
                handled = true;
                break;
            default:
                // Explorer restarted: the tray forgot us, so add the icon again.
                if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated && _trayAdded) { _trayAdded = false; AddTray(_tray.szTip); }
                break;
        }
        return IntPtr.Zero;
    }

    // ----- hotkeys -----
    /// <summary>Registers a gesture such as "Ctrl+Alt+Space". Returns false if another app owns it or it cannot be parsed.</summary>
    public bool RegisterHotkey(string gesture, Action action)
    {
        if (!TryParse(gesture, out var mods, out var vk)) return false;
        var id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk)) return false;
        _hotkeys[id] = action;
        return true;
    }

    public void ClearHotkeys()
    {
        foreach (var id in _hotkeys.Keys) Native.UnregisterHotKey(Handle, id);
        _hotkeys.Clear();
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= Native.MOD_WIN; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse<Key>(name, true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0 && modifiers != 0;
    }

    // ----- tray icon -----
    public void AddTray(string tooltip)
    {
        if (_icon == IntPtr.Zero)
        {
            var info = new Native.SHFILEINFO();
            var exe = Environment.ProcessPath ?? "";
            Native.SHGetFileInfo(exe, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), Native.SHGFI_ICON | 0x1 /*small*/);
            _icon = info.hIcon;
        }
        _tray = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = Handle,
            uID = 1,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = TrayMessage,
            hIcon = _icon,
            szTip = tooltip,
            szInfo = "",
            szInfoTitle = ""
        };
        _trayAdded = Native.Shell_NotifyIcon(Native.NIM_ADD, ref _tray);
    }

    public void RemoveTray()
    {
        if (_trayAdded) { Native.Shell_NotifyIcon(Native.NIM_DELETE, ref _tray); _trayAdded = false; }
    }

    public void Dispose()
    {
        ClearHotkeys();
        RemoveTray();
        if (_icon != IntPtr.Zero) { Native.DestroyIcon(_icon); _icon = IntPtr.Zero; }
        _source.Dispose();
    }
}
