using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Design;
using WinOrbit.Services;

namespace WinOrbit.Island;

/// <summary>
/// The microphone pill and sideways swipes.
///
/// While an app uses the microphone (a call in Teams, Zoom, Discord…), the capsule shows a mute button, the app's
/// name and three bars that follow your voice. Mute is Windows' own microphone mute, the same switch as the
/// taskbar's, so it works whatever the app is. The level comes from Windows' peak meter on the microphone,
/// which reports the stream the app already has open and never opens the microphone itself.
///
/// A sideways swipe on the island (two fingers on a touchpad, or tilting the mouse wheel) skips to the next or
/// previous track.
/// </summary>
public partial class IslandWindow
{
    private const string GlyphMicOff = "";
    private static readonly Color CallOrange = Color.FromRgb(0xFF, 0x9F, 0x0A), CallRed = Color.FromRgb(0xFF, 0x45, 0x3A);
    private static readonly MicMeter Mic = new();
    private DispatcherTimer? _callTimer;
    private readonly double[] _callLevels = { 0.3, 0.3, 0.3 };
    private bool _callShown, _callMuted, _demoMuted;
    private int _callTicks;
    private DateTime _callDemoUntil = DateTime.MinValue;

    /// <summary>A real app is using the microphone, or the preview is running.</summary>
    private bool CallDemo => DateTime.UtcNow < _callDemoUntil;
    private bool CallActive => CallDemo || S.CallPill && Hub.Privacy.Microphone.Count > 0;

    private void InitCall()
    {
        _callTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _callTimer.Tick += (_, _) => TickCall();
        // The button acts on the press and keeps it, so pressing it never drags or opens the island, and it
        // doesn't depend on the release reaching it (the glass squashes slightly under a press).
        CCallMute.MouseLeftButtonDown += (_, e) => { e.Handled = true; ToggleCallMute(); };
        CCallMute.MouseLeftButtonUp += (_, e) => e.Handled = true;
        IsVisibleChanged += (_, _) => { UpdateCallDrive(); if (!IsVisible) SetWheelHook(false); };
        Pill.MouseEnter += (_, _) => SetWheelHook(true);
        Pill.MouseLeave += (_, _) => SetWheelHook(false);
        Closed += (_, _) => SetWheelHook(false);
    }

    /// <summary>Called from RefreshCompact: show or hide the pill and keep its words and colours current.</summary>
    private void UpdateCall(bool show)
    {
        CCall.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && !_callShown)
        {
            if (!CallDemo) Mic.Attach();
            _callMuted = CallDemo ? _demoMuted : Mic.Muted ?? false;
            if (CNormal.Visibility == Visibility.Visible && !_expanded) { Pop(CCall); Droplet(1); }
        }
        _callShown = show;
        if (show) PaintCall();
        UpdateCallDrive();
    }

    private void PaintCall()
    {
        var apps = CallDemo ? new[] { "Teams" } : (IReadOnlyList<string>)Hub.Privacy.Microphone;
        CCallText.Text = MicMeter.Label(apps, _callMuted);
        CCallText.ToolTip = apps.Count > 0 ? "Using your microphone: " + string.Join(", ", apps) : null;
        var c = _callMuted ? CallRed : CallOrange;
        CCallGlyph.Text = _callMuted ? GlyphMicOff : GlyphMic;
        CCallGlyph.Foreground = new SolidColorBrush(c);
        CCallMute.Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
        CCallMute.ToolTip = _callMuted ? "Turn the microphone back on" : "Mute the microphone";
        System.Windows.Automation.AutomationProperties.SetName(CCallMute, (string)CCallMute.ToolTip);
        CCallEq.Tint(_callMuted ? Color.FromArgb(0x80, 0xA1, 0xA1, 0xAA) : CallOrange);
        CCallEq.Visibility = _callMuted ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The level timer runs only while the pill is actually on screen.</summary>
    private void UpdateCallDrive()
    {
        if (_callTimer is null) return;
        var run = _callShown && IsVisible && !_expanded && CNormal.Visibility == Visibility.Visible && !_card;
        if (!run) { _callTimer.Stop(); return; }
        // With Reduced Motion the bars rest still; the mute state is still kept current once a second.
        _callTimer.Interval = _callMuted || Motion.Intensity <= 0.01 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(50);
        if (!_callTimer.IsEnabled) _callTimer.Start();
    }

    private void TickCall()
    {
        if (!_callShown || !IsVisible || _expanded || _card) { _callTimer!.Stop(); return; }
        // Muting from the taskbar, the keyboard or Settings shows here within half a second.
        if (_callTicks++ % 10 == 0 || _callTimer!.Interval.TotalSeconds >= 1)
        {
            var muted = CallDemo ? _demoMuted : Mic.Muted ?? false;
            if (muted != _callMuted) { _callMuted = muted; PaintCall(); UpdateCallDrive(); FitCompact(); }
        }
        if (_callMuted) return;
        if (Motion.Intensity <= 0.01) { CCallEq.Levels(0.5, 0.7, 0.5); return; }
        var peak = CallDemo ? Math.Pow(_rng.NextDouble(), 2.2) * 0.35 : Mic.Peak();
        var level = MicMeter.Level(peak);
        double[] gain = { 0.85, 1.1, 0.75 };
        for (var i = 0; i < 3; i++)
        {
            var target = 0.18 + (level - 0.18) * gain[i] * (0.8 + 0.3 * _rng.NextDouble());
            _callLevels[i] = target > _callLevels[i] ? _callLevels[i] + (target - _callLevels[i]) * 0.75 : _callLevels[i] * 0.8 + target * 0.2;
        }
        CCallEq.Levels(_callLevels[0], _callLevels[1], _callLevels[2]);
    }

    private void ToggleCallMute()
    {
        var mute = !_callMuted;
        if (CallDemo) _demoMuted = mute;   // the preview never touches the real microphone
        else if (!Mic.SetMuted(mute)) { Peek(Glyph.Warning, "Windows didn't allow that change", 1.6); return; }
        _callMuted = mute;
        PaintCall();
        UpdateCallDrive();
        FitCompact();
        Pop(CCallGlyph, 0.6);
        Droplet(0.45);
    }

    /// <summary>Preview: a pretend call for eight seconds, with a working (pretend) mute button.</summary>
    private void DemoCall()
    {
        _callDemoUntil = DateTime.UtcNow.AddSeconds(8);
        _demoMuted = false;
        _callShown = false;   // appear with the usual pop
        RefreshCompact();
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(8100);
            if (!CallDemo) RefreshCompact();
        });
    }

    // ------------------------------------------------------------------ scroll wheel over the island
    // Windows sends the scroll wheel to the window under the pointer only when "Scroll inactive windows when I
    // hover over them" is on; with it off, scrolling over the island would scroll the app in front instead. So
    // while the pointer is over the island — and only then — WinOrbit watches the wheel with a low-level mouse
    // hook in its own process (nothing is injected into other apps), uses the steps meant for the island and
    // passes everything else straight on. Leaving the island removes the hook.
    private IntPtr _wheelHook;
    private Interop.Native.LowLevelMouseProc? _wheelProc;

    private void SetWheelHook(bool on)
    {
        if (on == (_wheelHook != IntPtr.Zero)) return;
        if (on)
        {
            _wheelProc ??= WheelHookProc;   // kept in a field so the delegate outlives the hook
            _wheelHook = Interop.Native.SetWindowsHookEx(Interop.Native.WH_MOUSE_LL, _wheelProc, Interop.Native.GetModuleHandle(null), 0);
        }
        else
        {
            Interop.Native.UnhookWindowsHookEx(_wheelHook);
            _wheelHook = IntPtr.Zero;
        }
    }

    private IntPtr WheelHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        var msg = (int)wParam;
        if (code >= 0 && msg is Interop.Native.WM_MOUSEWHEEL or Interop.Native.WM_MOUSEHWHEEL)
        {
            try
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Interop.Native.MSLLHOOKSTRUCT>(lParam);
                var delta = (short)((data.MouseData >> 16) & 0xFFFF);
                if (OverPill(data.Pt))
                {
                    var used = msg == Interop.Native.WM_MOUSEHWHEEL ? OnSideScroll(delta) : OnUpDownScroll(delta);
                    if (used) return new IntPtr(1);   // ours: the app in front doesn't scroll too
                }
                else Dispatcher.BeginInvoke(() => { if (!Pill.IsMouseOver) SetWheelHook(false); });
            }
            catch { }
        }
        return Interop.Native.CallNextHookEx(_wheelHook, code, wParam, lParam);
    }

    private bool OverPill(Interop.Native.POINT pt)
    {
        if (!IsVisible || Pill.ActualWidth < 1) return false;
        var p = Pill.PointFromScreen(new Point(pt.X, pt.Y));
        return p.X >= 0 && p.Y >= 0 && p.X <= Pill.ActualWidth && p.Y <= Pill.ActualHeight;
    }

    // ------------------------------------------------------------------ sideways swipe
    private readonly SwipeGesture _swipe = new();

    /// <summary>A sideways scroll arrived over the island. Returns true when it was used.</summary>
    private bool OnSideScroll(int delta)
    {
        if (!S.SwipeToSkip || !Hub.Media.HasTrack || (_expanded && _tab != "media") || _autoHidden) return false;
        var step = _swipe.Feed(delta, Environment.TickCount64);
        if (step == 0) return true;
        if (step > 0) Hub.Media.Next(); else Hub.Media.Previous();
        // The glass gives a little in the direction of the swipe and springs back.
        if (!_expanded) Springs.Kick(PillShift, SideEdge ? TranslateTransform.YProperty : TranslateTransform.XProperty, -step * 260, 0, 0.34, 0.5);
        if (!_expanded && !S.PeekOnMediaChange) Peek(step > 0 ? "" : "", step > 0 ? "Next track" : "Previous track", 1.2);
        return true;
    }
}

/// <summary>
/// Turns a stream of sideways scroll steps into at most one skip per swipe. A touchpad sends many small steps;
/// a tilting wheel sends 120 per notch and repeats while held. The steps add up until they pass a threshold,
/// which fires once; the swipe ends after a short pause, and only then can the next one fire.
/// </summary>
internal sealed class SwipeGesture
{
    internal const int Threshold = 200;
    internal const int PauseMs = 350;
    private int _sum;
    private long _last = long.MinValue;
    private bool _fired;

    /// <summary>+1 (next) for a swipe to the right, -1 (previous) to the left, 0 while nothing has been decided.</summary>
    public int Feed(int delta, long nowMs)
    {
        if (_last == long.MinValue || nowMs - _last > PauseMs) { _sum = 0; _fired = false; }
        _last = nowMs;
        // A change of direction mid-swipe starts the count again.
        if (Math.Sign(delta) != Math.Sign(_sum) && _sum != 0) _sum = 0;
        _sum += delta;
        if (_fired || Math.Abs(_sum) < Threshold) return 0;
        _fired = true;
        return Math.Sign(_sum);
    }
}
