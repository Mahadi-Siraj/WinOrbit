using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinOrbit.Design;

/// <summary>
/// Physics-driven motion: every value is a mass on a damped spring instead of a curve with a fixed duration.
/// That gives three things timed animations can't:
///   • momentum — retargeting a value mid-flight keeps its current velocity, so interrupted motion never jerks;
///   • a real bounce — under-damped springs overshoot and settle the way physical objects do;
///   • kicks — a velocity impulse with no new target makes a surface wobble and come back (a "droplet" bulge).
/// Parameters follow the familiar "response / damping fraction" model: response is the period in seconds,
/// damping 1 means no overshoot, lower values bounce more. The engine runs on the render loop only while
/// something is moving and unhooks itself when every spring has come to rest, so idle cost is zero.
/// Reduced Motion (Motion.Intensity 0) snaps straight to the target.
/// </summary>
public static class Springs
{
    private sealed class State
    {
        public required DependencyObject Target;
        public required DependencyProperty Property;
        public double X, V, To, Omega, Zeta, Rest;
        public Action? Done;
    }

    private sealed class RefKey : IEquatable<RefKey>
    {
        public readonly DependencyObject Target; public readonly DependencyProperty Property;
        public RefKey(DependencyObject t, DependencyProperty p) { Target = t; Property = p; }
        public bool Equals(RefKey? o) => o is not null && ReferenceEquals(o.Target, Target) && o.Property == Property;
        public override bool Equals(object? obj) => Equals(obj as RefKey);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Target), Property.GlobalIndex);
    }

    private static readonly Dictionary<RefKey, State> Active = new();
    private static bool _hooked;
    private static TimeSpan _last = TimeSpan.Zero;
    private static double _frame = 1 / 60.0;   // learned display frame length

    /// <summary>Gentle, no overshoot. Good for sizes that must never dip below their target.</summary>
    public const double Smooth = 1.0;
    /// <summary>A small, natural bounce (about 3 % overshoot).</summary>
    public const double Snappy = 0.74;
    /// <summary>A visible, playful bounce.</summary>
    public const double Bouncy = 0.58;

    /// <summary>Spring <paramref name="property"/> towards <paramref name="to"/>, keeping any velocity it already has.</summary>
    public static void To(DependencyObject target, DependencyProperty property, double to, double response = 0.42, double damping = Snappy, Action? done = null)
    {
        var key = new RefKey(target, property);
        if (Motion.Intensity <= 0.01 || response <= 0)
        {
            Active.Remove(key);
            (target as IAnimatable)?.BeginAnimation(property, null);
            target.SetValue(property, to);
            done?.Invoke();
            return;
        }
        var s = Begin(key);
        s.To = to;
        Tune(s, response, damping);
        s.Done = done;
        Hook();
    }

    /// <summary>Push a value with a velocity impulse (units per second); it wobbles and returns to <paramref name="rest"/>.</summary>
    public static void Kick(DependencyObject target, DependencyProperty property, double velocity, double rest, double response = 0.34, double damping = 0.5)
    {
        if (Motion.Intensity <= 0.01) return;
        var s = Begin(new RefKey(target, property));
        s.To = rest;
        s.V += velocity * Math.Clamp(Motion.Intensity, 0.4, 1.6);
        Tune(s, response, damping);
        Hook();
    }

    /// <summary>The value a running spring is heading to, or null when the property is at rest.</summary>
    public static double? TargetOf(DependencyObject target, DependencyProperty property)
        => Active.TryGetValue(new RefKey(target, property), out var s) ? s.To : null;

    public static bool IsMoving(DependencyObject target, DependencyProperty property) => Active.ContainsKey(new RefKey(target, property));

    /// <summary>Stop a spring where it is (used when a timed animation takes the property over).</summary>
    public static void Stop(DependencyObject target, DependencyProperty property) => Active.Remove(new RefKey(target, property));

    private static State Begin(RefKey key)
    {
        if (Active.TryGetValue(key, out var s)) return s;
        // Start from what is on screen right now, including a timed animation's in-flight value.
        var current = key.Target.GetValue(key.Property) is double d && !double.IsNaN(d) ? d : 0;
        (key.Target as IAnimatable)?.BeginAnimation(key.Property, null);
        key.Target.SetValue(key.Property, current);
        s = new State { Target = key.Target, Property = key.Property, X = current, To = current };
        Active[key] = s;
        return s;
    }

    private static void Tune(State s, double response, double damping)
    {
        // Theme "animation" intensity stretches time like Motion.To does.
        var period = response * Math.Clamp(Motion.Intensity, 0.4, 1.6);
        s.Omega = 2 * Math.PI / Math.Max(0.05, period);
        s.Zeta = Math.Clamp(damping, 0.05, 1.0);
        var span = Math.Max(Math.Abs(s.To), Math.Abs(s.X - s.To));
        s.Rest = span <= 2.5 ? 0.0015 : 0.3;   // opacity/scale settle finer than pixel sizes; sub-pixel tails are invisible
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private static void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= OnFrame;
        ReportFrames();
    }

    // ---- optional frame-pacing probe (set WINORBIT_FRAMES=1): logs how evenly frames arrived during each motion
    private static readonly bool Probe = Environment.GetEnvironmentVariable("WINORBIT_FRAMES") == "1";
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly List<double> Gaps = new();
    private static double _lastWall;

    private static void NoteFrame()
    {
        if (!Probe) return;
        var now = Clock.Elapsed.TotalMilliseconds;
        if (_lastWall > 0) Gaps.Add(now - _lastWall);
        _lastWall = now;
    }

    private static void ReportFrames()
    {
        if (!Probe || Gaps.Count < 3) { Gaps.Clear(); _lastWall = 0; return; }
        var sorted = Gaps.OrderBy(g => g).ToArray();
        Core.Log.Info($"Frames: n={Gaps.Count} mean={Gaps.Average():0.0}ms p50={sorted[sorted.Length / 2]:0.0} p95={sorted[(int)(sorted.Length * 0.95)]:0.0} max={sorted[^1]:0.0} >20ms={Gaps.Count(g => g > 20)} >34ms={Gaps.Count(g => g > 34)}");
        Gaps.Clear(); _lastWall = 0;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
        if (now == _last) return;   // WPF can raise Rendering twice for one frame
        NoteFrame();
        var gap = _last == TimeSpan.Zero ? _frame : (now - _last).TotalSeconds;
        _last = now;
        if (gap <= 0) return;
        // Frame-locked stepping: every rendered frame advances the motion by one display frame. A late frame
        // (the UI thread was busy) then costs a moment of time instead of a visible jump — motion stays even.
        // The frame length is learned from the display, so 60, 120 and 144 Hz all run at true speed.
        if (gap < _frame * 1.5) _frame += (gap - _frame) * 0.1;
        _frame = Math.Clamp(_frame, 1 / 240.0, 1 / 30.0);
        var dt = Math.Min(gap, _frame);

        List<RefKey>? finished = null;
        List<Action>? callbacks = null;
        // A snapshot: setting a value can raise property callbacks that start or retarget other springs.
        foreach (var (key, s) in Active.ToArray())
        {
            Step(s, dt);
            if (Math.Abs(s.X - s.To) < s.Rest && Math.Abs(s.V) < s.Rest * 12)
            {
                s.X = s.To; s.V = 0;
                (finished ??= new()).Add(key);
                if (s.Done is not null) (callbacks ??= new()).Add(s.Done);
            }
            s.Target.SetValue(s.Property, s.X);
        }
        if (finished is not null) foreach (var k in finished) Active.Remove(k);
        if (Active.Count == 0) Unhook();
        if (callbacks is not null) foreach (var c in callbacks) c();
    }

    /// <summary>Advance one spring by dt using the exact solution of the damped oscillator, so it is stable at any frame rate.</summary>
    internal static void Advance(ref double x, ref double v, double to, double omega, double zeta, double dt)
    {
        var d = x - to;
        if (zeta >= 0.999)
        {
            var b = v + omega * d;
            var e = Math.Exp(-omega * dt);
            x = to + (d + b * dt) * e;
            v = (v - omega * b * dt) * e;
            return;
        }
        var wd = omega * Math.Sqrt(1 - zeta * zeta);
        var decay = Math.Exp(-zeta * omega * dt);
        var c = Math.Cos(wd * dt);
        var sn = Math.Sin(wd * dt);
        x = to + decay * (d * c + (v + zeta * omega * d) / wd * sn);
        v = decay * (v * c - (zeta * omega * v + omega * omega * d) / wd * sn);
    }

    private static void Step(State s, double dt) => Advance(ref s.X, ref s.V, s.To, s.Omega, s.Zeta, dt);
}
