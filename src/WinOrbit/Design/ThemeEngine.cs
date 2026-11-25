using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Wallpaper;
using Microsoft.Win32;

namespace WinOrbit.Design;

/// <summary>
/// Turns a <see cref="Theme"/> plus the wallpaper palette into the live resource set every surface binds to.
/// All visuals reference these keys through DynamicResource, so a change repaints the whole product instantly.
/// </summary>
public sealed class ThemeEngine
{
    public Theme Current { get; private set; } = Presets.All[0].Clone();
    public bool IsDark { get; private set; } = true;
    public Color Accent { get; private set; }
    public GlassParams Glass => IsDark ? Current.Dark : Current.Light;
    public List<Theme> UserThemes { get; } = new();
    public IEnumerable<Theme> All => Presets.All.Concat(UserThemes);

    /// <summary>Resources were rebuilt.</summary>
    public event Action? Applied;

    private readonly Store _store;
    private readonly WallpaperEngine _wallpaper;
    private readonly DispatcherTimer _scheduleTimer;

    public ThemeEngine(Store store, WallpaperEngine wallpaper)
    {
        _store = store;
        _wallpaper = wallpaper;
        _scheduleTimer = new DispatcherTimer();
        _scheduleTimer.Tick += (_, _) => Apply();
    }

    public void Load()
    {
        UserThemes.Clear();
        UserThemes.AddRange(_store.LoadUserThemes().Where(t => !t.BuiltIn));
        var id = _store.Settings.ActiveThemeId;
        Current = (All.FirstOrDefault(t => t.Id == id) ?? Presets.All[0]).Clone();
    }

    public Theme? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>The look liquid glass switches to when it's turned on with a look that has no liquid lens.</summary>
    public const string LiquidFallback = "liquid-frosted";

    /// <summary>
    /// Liquid glass on or off, for every look. Turning it on with a look that has no lens of its own switches to
    /// Liquid Frosted, so the switch always shows; turning it off keeps the look and drops the lens.
    /// </summary>
    /// <summary>Liquid glass is on and this look has a lens to show (what the on/off switches display).</summary>
    public bool LiquidShowing => _store.Settings.General.LiquidGlass && _store.Settings.General.CurvedGlass && HasLiquid(Current);

    public void SetLiquidGlass(bool on)
    {
        _store.Settings.General.LiquidGlass = on;
        if (on) _store.Settings.General.CurvedGlass = true;   // the lens is drawn by the curved-glass shader
        _store.Save();
        if (on && !HasLiquid(Current) && Find(LiquidFallback) is not null) Use(LiquidFallback);
        else Apply();
    }

    public void Use(string id)
    {
        var t = Find(id);
        if (t is null) return;
        if (Current.Id != id) { _undo.Push(Current.Clone()); _redo.Clear(); HistoryChanged?.Invoke(); }
        Current = t.Clone();
        _store.Settings.ActiveThemeId = id;
        _store.Save();
        Apply();
    }

    /// <summary>Swap in an edited copy (live preview, undo/redo) without touching what is saved.</summary>
    public void Preview(Theme theme)
    {
        Current = theme;
        Apply();
    }

    public static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch { return true; }
    }

    public static bool ScheduledDark(Theme t, DateTime now, out DateTime nextSwitch)
    {
        static TimeSpan P(string s, TimeSpan fb) => TimeSpan.TryParseExact(s, @"h\:mm", CultureInfo.InvariantCulture, out var r) || TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out r) ? r : fb;
        var light = P(t.LightAt, TimeSpan.FromHours(7));
        var dark = P(t.DarkAt, TimeSpan.FromHours(19));
        var tod = now.TimeOfDay;
        bool isDark;
        if (light < dark) isDark = tod < light || tod >= dark;
        else isDark = tod >= dark && tod < light;
        var candidates = new[] { now.Date + light, now.Date + dark, now.Date.AddDays(1) + light, now.Date.AddDays(1) + dark };
        nextSwitch = candidates.Where(c => c > now).Min();
        return isDark;
    }

    public void Apply()
    {
        var t = Current;
        _scheduleTimer.Stop();
        IsDark = t.Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            ThemeMode.Scheduled => ScheduledDark(t, DateTime.Now, out _),
            _ => WindowsPrefersDark()
        };
        if (t.Mode == ThemeMode.Scheduled)
        {
            // One timer aimed at the next boundary; no periodic polling.
            ScheduledDark(t, DateTime.Now, out var next);
            _scheduleTimer.Interval = next - DateTime.Now + TimeSpan.FromSeconds(1);
            _scheduleTimer.Start();
        }

        var palette = _store.Settings.WallpaperAutoExtract ? _wallpaper.Palette : Palette.Default;
        var g = Glass;
        var dark = IsDark;

        var accentBase = t.AccentFromWallpaper && _store.Settings.WallpaperAutoExtract
            ? palette.Accent
            : ColorMath.Parse(dark ? t.AccentDark : t.AccentLight, palette.Accent);
        Accent = ColorMath.ForMode(accentBase, dark);
        var onAccent = ColorMath.Contrast(Accent, Colors.White) >= 3.2 ? Colors.White : Color.FromRgb(0x0B, 0x10, 0x18);

        var text = dark ? Color.FromRgb(0xF5, 0xF7, 0xFB) : Color.FromRgb(0x10, 0x16, 0x22);
        var ink = dark ? Colors.White : Colors.Black;

        // Readability guard: if the wallpaper is too bright (dark mode) or too dark (light mode) for the
        // chosen translucency, quietly thicken the tint until text contrast is safe.
        var tint = ColorMath.Parse(g.Tint, dark ? Color.FromRgb(0x12, 0x15, 0x1C) : Color.FromRgb(0xF6, 0xF8, 0xFC));
        var liquid = EffectiveLiquid(g.Liquid, _store.Settings.General.LiquidGlass);
        var tintOpacity = ReadableTintOpacity(_wallpaper.Palette.AverageLuminance, ColorMath.Luminance(tint), g.TintOpacity, dark, liquid);

        var r = Application.Current.Resources;
        void B(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); r[key] = b; }

        B("T.Text", text);
        B("T.Text2", text.Alpha(dark ? 0.70 : 0.68));
        B("T.Text3", text.Alpha(dark ? 0.46 : 0.48));
        B("T.Accent", Accent);
        B("T.AccentSoft", Accent.Alpha(dark ? 0.20 : 0.16));
        B("T.AccentHover", ColorMath.Lerp(Accent, ink, dark ? 0.14 : 0.10));
        B("T.OnAccent", onAccent);
        B("T.Card", dark ? Colors.White.Alpha(0.055) : Colors.White.Alpha(0.62));
        B("T.CardHover", dark ? Colors.White.Alpha(0.095) : Colors.White.Alpha(0.82));
        B("T.CardPressed", dark ? Colors.White.Alpha(0.04) : Colors.White.Alpha(0.5));
        B("T.Input", dark ? Colors.Black.Alpha(0.24) : Colors.White.Alpha(0.72));
        B("T.Stroke", dark ? Colors.White.Alpha(0.09) : Colors.Black.Alpha(0.08));
        B("T.StrokeStrong", dark ? Colors.White.Alpha(0.18) : Colors.Black.Alpha(0.16));
        B("T.Divider", dark ? Colors.White.Alpha(0.07) : Colors.Black.Alpha(0.07));
        B("T.Track", dark ? Colors.White.Alpha(0.14) : Colors.Black.Alpha(0.12));
        B("T.WindowTint", tint.Alpha(dark ? 0.50 : 0.56));
        B("T.Popup", dark ? Color.FromArgb(0xF4, 0x1D, 0x21, 0x2B) : Color.FromArgb(0xF7, 0xFA, 0xFB, 0xFE));
        B("T.Danger", dark ? Color.FromRgb(0xFF, 0x7B, 0x72) : Color.FromRgb(0xC4, 0x2B, 0x1C));
        B("T.Good", dark ? Color.FromRgb(0x6F, 0xDB, 0x9A) : Color.FromRgb(0x10, 0x7C, 0x41));
        B("T.Warn", dark ? Color.FromRgb(0xFF, 0xC8, 0x6B) : Color.FromRgb(0x9A, 0x5B, 0x00));
        B("G.Tint", tint.Alpha(tintOpacity));
        B("G.TintSmoked", tint.Alpha(Math.Max(tintOpacity, 0.86)));
        B("G.TintSolid", tint);
        B("G.Stroke", dark ? Colors.Black.Alpha(0.35) : Colors.Black.Alpha(0.10));

        // Specular highlight: light entering from the top-left and dying out across the surface.
        var hi = g.HighlightIntensity;
        var highlight = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.7, 1) };
        highlight.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.16 : 0.5) * hi), 0));
        highlight.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.045 : 0.16) * hi), 0.32));
        highlight.GradientStops.Add(new GradientStop(Colors.White.Alpha(0), 0.62));
        highlight.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.03 : 0.08) * hi), 1));
        highlight.Freeze();
        r["G.Highlight"] = highlight;

        // Illuminated rim: bright where the light hits, faint on the far edge - the "thin lit border".
        var bi = g.BorderIntensity;
        var border = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        border.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.62 : 0.95) * bi), 0));
        border.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.14 : 0.5) * bi), 0.38));
        border.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.06 : 0.3) * bi), 0.68));
        border.GradientStops.Add(new GradientStop(Colors.White.Alpha((dark ? 0.32 : 0.75) * bi), 1));
        border.Freeze();
        r["G.Border"] = border;

        // Glow: a breath of accent light pooled at the bottom of the glass.
        var glow = new RadialGradientBrush { Center = new Point(0.5, 1.25), GradientOrigin = new Point(0.5, 1.25), RadiusX = 0.85, RadiusY = 0.95 };
        glow.GradientStops.Add(new GradientStop(Accent.Alpha(0.5 * g.Glow), 0));
        glow.GradientStops.Add(new GradientStop(Accent.Alpha(0), 1));
        glow.Freeze();
        r["G.Glow"] = glow;

        r["G.ShadowOpacity"] = Math.Clamp(g.ShadowIntensity, 0, 1) * (dark ? 0.75 : 0.42);
        r["G.Refraction"] = Math.Clamp(g.Refraction, 0, 0.25);
        r["G.Liquid"] = liquid;
        r["W.BareShadow"] = BareShadow(dark);

        // Wallpaper wash
        var stops = t.Gradient.FromWallpaper || t.Gradient.Stops.Count < 2 ? palette.ToGradientStops() : t.Gradient.Stops;
        r["T.Wash"] = BuildGradient(t.Gradient, stops, t.Gradient.Opacity * (dark ? 0.55 : 0.42));
        r["T.WashStrong"] = BuildGradient(t.Gradient, stops, 1);

        var radius = Math.Clamp(t.CornerRadius, 0, 40);
        r["R.Card"] = new CornerRadius(radius);
        r["R.Control"] = new CornerRadius(Math.Clamp(radius * 0.48, 2, 14));
        r["R.Small"] = new CornerRadius(Math.Clamp(radius * 0.3, 2, 8));
        r["R.CardValue"] = radius;

        FontFamily family;
        try { family = new FontFamily(string.IsNullOrWhiteSpace(t.FontFamily) ? "Segoe UI Variable Display" : t.FontFamily + ", Segoe UI Variable Display, Segoe UI"); }
        catch { family = new FontFamily("Segoe UI"); }
        r["F.Family"] = family;
        var fs = Math.Clamp(t.FontScale, 0.85, 1.3);
        r["F.Caption"] = 12.0 * fs;
        r["F.Body"] = 14.0 * fs;
        r["F.Sub"] = 16.0 * fs;
        r["F.Title"] = 22.0 * fs;
        r["F.Display"] = 34.0 * fs;

        Motion.Intensity = _store.Settings.General.ReducedMotion ? 0 : Math.Clamp(t.Animation, 0, 1.6);

        _wallpaper.SetGlass(g.Blur, g.BackdropSaturation);
        Applied?.Invoke();
    }

    /// <summary>
    /// The soft shadow under widgets drawn straight on the wallpaper (the Bare style): a dark shadow under the light
    /// text of dark looks, a pale glow under the dark text of light looks, so the letters read on any picture.
    /// </summary>
    internal static System.Windows.Media.Effects.DropShadowEffect BareShadow(bool dark)
    {
        var fx = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 14, ShadowDepth = 1.5, Direction = 270, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            Color = dark ? Colors.Black : Colors.White, Opacity = dark ? 0.6 : 0.75
        };
        fx.Freeze();
        return fx;
    }

    /// <summary>The liquid lens strength actually used: the look's own, or none when liquid glass is switched off.</summary>
    internal static double EffectiveLiquid(double themeLiquid, bool liquidGlassOn) => liquidGlassOn ? Math.Clamp(themeLiquid, 0, 1) : 0;

    /// <summary>Whether a look has a liquid lens of its own, in dark or light (switching liquid glass on only shows on those).</summary>
    internal static bool HasLiquid(Theme t) => t.Dark.Liquid > 0.01 || t.Light.Liquid > 0.01;

    /// <summary>
    /// Readability guard: if the wallpaper is too bright (dark looks) or too dark (light looks) for the chosen
    /// translucency, thicken the tint in steps until text on it stays readable. The liquid lens (<paramref name="liquid"/>
    /// above 0) calms bright patches (or lifts dark ones) itself, right where the text sits, so it needs less help and
    /// the glass can stay clear.
    /// </summary>
    internal static double ReadableTintOpacity(double wallLum, double tintLum, double opacity, bool dark, double liquid)
    {
        liquid = Math.Clamp(liquid, 0, 1);
        var limit = dark ? 0.16 + 0.12 * liquid : 0.55 - 0.12 * liquid;
        opacity = Math.Clamp(opacity, 0.05, 1);
        for (var i = 0; i < 12; i++)
        {
            var effective = wallLum * (1 - opacity) + tintLum * opacity;
            if (dark ? effective <= limit : effective >= limit) break;
            opacity = Math.Min(1, opacity + 0.05);
        }
        return opacity;
    }

    public static Brush BuildGradient(GradientDef def, IList<GradientStopDef> stops, double opacity)
    {
        var collection = new GradientStopCollection();
        foreach (var s in stops.OrderBy(s => s.Offset))
        {
            var c = ColorMath.Parse(s.Color, Colors.Gray);
            if (Math.Abs(def.Saturation - 1) > 0.01) c = ColorMath.Saturate(c, def.Saturation);
            collection.Add(new GradientStop(c, Math.Clamp(s.Offset, 0, 1)));
        }
        GradientBrush brush;
        if (def.Kind == GradientKind.Radial)
            brush = new RadialGradientBrush(collection) { Center = new Point(0.28, 0.22), GradientOrigin = new Point(0.28, 0.22), RadiusX = 1.0, RadiusY = 1.0 };
        else
        {
            var rad = def.Angle * Math.PI / 180;
            double dx = Math.Cos(rad) / 2, dy = Math.Sin(rad) / 2;
            brush = new LinearGradientBrush(collection, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        }
        brush.Opacity = Math.Clamp(opacity, 0, 1);
        brush.Freeze();
        return brush;
    }

    // ----- editing with undo/redo -----
    private readonly Stack<Theme> _undo = new(), _redo = new();
    private DateTime _lastSnapshot = DateTime.MinValue;
    private DispatcherTimer? _saveTimer;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    /// <summary>A built-in theme was copied into an editable user theme.</summary>
    public event Action<Theme>? Forked;
    public event Action? HistoryChanged;

    /// <summary>
    /// Change the active theme live. Built-in themes are never modified: the first edit copies the theme into
    /// "Name (custom)", which becomes active. Rapid edits (a slider drag) collapse into one undo step.
    /// </summary>
    public void Edit(Action<Theme> change)
    {
        if ((DateTime.UtcNow - _lastSnapshot).TotalMilliseconds > 700)
        {
            _undo.Push(Current.Clone());
            if (_undo.Count > 80) { var keep = _undo.Take(60).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
            _redo.Clear();
        }
        _lastSnapshot = DateTime.UtcNow;
        if (Current.BuiltIn) Fork();
        change(Current);
        Apply();
        ScheduleSave();
        HistoryChanged?.Invoke();
    }

    private void Fork()
    {
        var fork = Current.Clone();
        fork.BasedOn = Current.Id;
        fork.Id = Guid.NewGuid().ToString("N")[..12];
        fork.Name = Current.Name + " (custom)";
        fork.BuiltIn = false;
        UserThemes.Add(fork.Clone());
        Current = fork;
        _store.Settings.ActiveThemeId = fork.Id;
        _store.Save();
        _store.SaveTheme(fork);
        Forked?.Invoke(fork);
    }

    public void Undo() => Step(_undo, _redo);
    public void Redo() => Step(_redo, _undo);

    private void Step(Stack<Theme> from, Stack<Theme> to)
    {
        if (from.Count == 0) return;
        to.Push(Current.Clone());
        var t = from.Pop();
        Current = t.Clone();
        if (!t.BuiltIn && UserThemes.All(u => u.Id != t.Id)) UserThemes.Add(t.Clone());
        _store.Settings.ActiveThemeId = t.Id;
        _store.Save();
        _lastSnapshot = DateTime.MinValue;
        Apply();
        ScheduleSave();
        HistoryChanged?.Invoke();
    }

    /// <summary>Return a customised theme to the built-in it was based on (keeps the custom copy until deleted).</summary>
    public void ResetToBase()
    {
        var baseId = Current.BuiltIn ? Current.Id : Current.BasedOn;
        var b = Presets.All.FirstOrDefault(p => p.Id == baseId) ?? Presets.All[0];
        _undo.Push(Current.Clone());
        _redo.Clear();
        if (Current.BuiltIn) { Use(b.Id); }
        else
        {
            var name = Current.Name; var id = Current.Id;
            Current = b.Clone();
            Current.Id = id; Current.Name = name; Current.BuiltIn = false; Current.BasedOn = b.Id;
            Apply();
            ScheduleSave();
        }
        HistoryChanged?.Invoke();
    }

    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); if (!Current.BuiltIn) SaveExisting(Current); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ClearHistory() { _undo.Clear(); _redo.Clear(); HistoryChanged?.Invoke(); }

    // ----- user themes -----
    public Theme SaveAsNew(Theme source, string name)
    {
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N")[..12];
        copy.Name = name;
        copy.BuiltIn = false;
        UserThemes.Add(copy);
        _store.SaveTheme(copy);
        return copy;
    }

    public void SaveExisting(Theme theme)
    {
        if (theme.BuiltIn) return;
        var idx = UserThemes.FindIndex(t => t.Id == theme.Id);
        var copy = theme.Clone();
        if (idx >= 0) UserThemes[idx] = copy; else UserThemes.Add(copy);
        _store.SaveTheme(copy);
    }

    public void Delete(Theme theme)
    {
        if (theme.BuiltIn) return;
        UserThemes.RemoveAll(t => t.Id == theme.Id);
        _store.DeleteTheme(theme);
    }
}

/// <summary>Code-driven animation so motion honours the theme's intensity and Reduced Motion.</summary>
public static class Motion
{
    /// <summary>0 disables animation entirely; 1 is the designed speed.</summary>
    public static double Intensity { get; set; } = 1;

    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.32 });
    public static readonly IEasingFunction Settle = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });

    private static T Freeze<T>(T e) where T : Freezable { e.Freeze(); return e; }

    public static void To(IAnimatable target, DependencyProperty property, double to, int ms, IEasingFunction? ease = null, Action? done = null)
    {
        // The "spring" and "settle" curves are real physics now: interruptible, velocity-preserving, and
        // (for Spring) with a natural bounce instead of a canned overshoot curve.
        if (target is DependencyObject obj && ms > 0 && (ReferenceEquals(ease, Spring) || ReferenceEquals(ease, Settle)))
        {
            if (ReferenceEquals(ease, Spring)) Springs.To(obj, property, to, ms / 1000.0, Springs.Snappy, done);
            else Springs.To(obj, property, to, ms / 1000.0 * 0.7, Springs.Smooth, done);
            return;
        }
        if (target is DependencyObject d) Springs.Stop(d, property);
        if (Intensity <= 0.01 || ms <= 0)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            done?.Invoke();
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms * Math.Clamp(Intensity, 0.4, 1.6))) { EasingFunction = ease ?? EaseOut };
        if (done is not null) anim.Completed += (_, _) => done();
        target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Fade and lift an element into place; used when pages and panels appear.</summary>
    public static void Enter(FrameworkElement el, double offsetY = 14, int ms = 320)
    {
        if (Intensity <= 0.01) { el.Opacity = 1; el.RenderTransform = Transform.Identity; return; }
        var tt = new TranslateTransform(0, offsetY);
        el.RenderTransform = tt;
        el.Opacity = 0;
        To(el, UIElement.OpacityProperty, 1, ms);
        To(tt, TranslateTransform.YProperty, 0, ms + 60, Settle);
    }
}

/// <summary>The built-in themes. Each one is a complete, hand-tuned set for both dark and light.</summary>
public static class Presets
{
    private static Theme T(string id, string name, string desc, ThemeMode mode, Action<Theme> tune)
    {
        var t = new Theme { Id = id, Name = name, Description = desc, BuiltIn = true, Mode = mode };
        tune(t);
        return t;
    }

    public static readonly List<Theme> All = new()
    {
        T("pure-liquid-glass", "Pure Liquid Glass", "The signature look: deep blur, lit rims and wallpaper-matched colour.", ThemeMode.Auto, t =>
        {
            t.Dark = new GlassParams { Tint = "#11141B", TintOpacity = 0.44, Blur = 44, BackdropSaturation = 1.35, BorderIntensity = 0.6, HighlightIntensity = 0.55, Glow = 0.22, ShadowIntensity = 0.45, Refraction = 0.06 };
            t.Light = new GlassParams { Tint = "#F7F9FD", TintOpacity = 0.52, Blur = 44, BackdropSaturation = 1.25, BorderIntensity = 0.8, HighlightIntensity = 0.75, Glow = 0.12, ShadowIntensity = 0.3, Refraction = 0.06 };
        }),
        T("liquid-clear", "Liquid Clear", "Nearly clear liquid glass: a thick rounded rim that bends the wallpaper round its edges, and moves when the glass does.", ThemeMode.Auto, t =>
        {
            t.Dark = new GlassParams { Tint = "#0C0F15", TintOpacity = 0.12, Blur = 4, BackdropSaturation = 1.2, BorderIntensity = 0.22, HighlightIntensity = 0.2, Glow = 0.06, ShadowIntensity = 0.38, Refraction = 0.02, Liquid = 1 };
            t.Light = new GlassParams { Tint = "#FFFFFF", TintOpacity = 0.16, Blur = 4, BackdropSaturation = 1.15, BorderIntensity = 0.35, HighlightIntensity = 0.3, Glow = 0.04, ShadowIntensity = 0.22, Refraction = 0.02, Liquid = 1 };
            t.Gradient.Opacity = 0.2;
        }),
        T("liquid-frosted", "Liquid Frosted", "Liquid glass with a soft frost: the same living rim, with calmer, easier-to-read middles.", ThemeMode.Auto, t =>
        {
            t.Dark = new GlassParams { Tint = "#10131A", TintOpacity = 0.3, Blur = 18, BackdropSaturation = 1.3, BorderIntensity = 0.3, HighlightIntensity = 0.3, Glow = 0.12, ShadowIntensity = 0.42, Refraction = 0.03, Liquid = 0.8 };
            t.Light = new GlassParams { Tint = "#F8FAFE", TintOpacity = 0.38, Blur = 18, BackdropSaturation = 1.2, BorderIntensity = 0.45, HighlightIntensity = 0.45, Glow = 0.08, ShadowIntensity = 0.26, Refraction = 0.03, Liquid = 0.8 };
            t.Gradient.Opacity = 0.35;
        }),
        T("wallpaper-adaptive", "Wallpaper Adaptive", "Lets the wallpaper lead: thinner tint, stronger wash and vivid extracted accents.", ThemeMode.Auto, t =>
        {
            t.Dark = new GlassParams { Tint = "#0E1118", TintOpacity = 0.32, Blur = 52, BackdropSaturation = 1.6, BorderIntensity = 0.55, HighlightIntensity = 0.5, Glow = 0.4, ShadowIntensity = 0.4, Refraction = 0.08 };
            t.Light = new GlassParams { Tint = "#FAFBFF", TintOpacity = 0.42, Blur = 52, BackdropSaturation = 1.45, BorderIntensity = 0.8, HighlightIntensity = 0.7, Glow = 0.25, ShadowIntensity = 0.28, Refraction = 0.08 };
            t.Gradient.Opacity = 0.85; t.Gradient.Saturation = 1.2;
        }),
        T("frosted-glass", "Frosted Glass", "Heavier frost and softer highlights for maximum legibility.", ThemeMode.Auto, t =>
        {
            t.Dark = new GlassParams { Tint = "#1A1E27", TintOpacity = 0.62, Blur = 64, BackdropSaturation = 1.1, BorderIntensity = 0.45, HighlightIntensity = 0.35, Glow = 0.1, ShadowIntensity = 0.4, Refraction = 0.02 };
            t.Light = new GlassParams { Tint = "#FFFFFF", TintOpacity = 0.68, Blur = 64, BackdropSaturation = 1.05, BorderIntensity = 0.7, HighlightIntensity = 0.55, Glow = 0.06, ShadowIntensity = 0.26, Refraction = 0.02 };
            t.Gradient.Opacity = 0.35;
        }),
        T("minimal-dark", "Minimal Dark", "Quiet charcoal surfaces, one restrained accent, almost no ornament.", ThemeMode.Dark, t =>
        {
            t.AccentFromWallpaper = false; t.AccentDark = "#9DB4D6"; t.AccentLight = "#3B5B8C";
            t.Dark = new GlassParams { Tint = "#15171C", TintOpacity = 0.78, Blur = 30, BackdropSaturation = 0.9, BorderIntensity = 0.3, HighlightIntensity = 0.2, Glow = 0, ShadowIntensity = 0.35, Refraction = 0 };
            t.Gradient.Opacity = 0.18; t.Gradient.Saturation = 0.6; t.CornerRadius = 16; t.Animation = 0.8;
        }),
        T("soft-light", "Soft Light", "Airy, warm-white glass with gentle shadows. Designed for daylight.", ThemeMode.Light, t =>
        {
            t.Light = new GlassParams { Tint = "#FFFDF9", TintOpacity = 0.6, Blur = 48, BackdropSaturation = 1.15, BorderIntensity = 0.85, HighlightIntensity = 0.8, Glow = 0.1, ShadowIntensity = 0.22, Refraction = 0.04 };
            t.Gradient.Opacity = 0.5; t.CornerRadius = 24;
        }),
        T("midnight", "Midnight", "Inky blue-black glass with a cool indigo accent for late hours.", ThemeMode.Dark, t =>
        {
            t.AccentFromWallpaper = false; t.AccentDark = "#8C9BFF"; t.AccentLight = "#3F4FC4";
            t.Dark = new GlassParams { Tint = "#080B16", TintOpacity = 0.66, Blur = 46, BackdropSaturation = 0.95, BorderIntensity = 0.4, HighlightIntensity = 0.3, Glow = 0.3, ShadowIntensity = 0.55, Refraction = 0.04 };
            t.Gradient.FromWallpaper = false; t.Gradient.Angle = 140; t.Gradient.Opacity = 0.6;
            t.Gradient.Stops = new() { new("#4B5BFF", 0), new("#27307A", 0.5), new("#0B1030", 1) };
        }),
        T("amoled-black", "AMOLED Black", "True black, zero translucency. The lowest-cost, highest-contrast option.", ThemeMode.Dark, t =>
        {
            t.Dark = new GlassParams { Tint = "#000000", TintOpacity = 1.0, Blur = 0, BackdropSaturation = 1, BorderIntensity = 0.28, HighlightIntensity = 0.08, Glow = 0.12, ShadowIntensity = 0.2, Refraction = 0 };
            t.Gradient.Opacity = 0.12; t.CornerRadius = 18; t.Animation = 0.7;
        }),
        T("productivity", "Productivity", "Neutral, dense and calm. High contrast text, dependable blue accent.", ThemeMode.Auto, t =>
        {
            t.AccentFromWallpaper = false; t.AccentDark = "#6AA9FF"; t.AccentLight = "#1F5FD0";
            t.Dark = new GlassParams { Tint = "#161920", TintOpacity = 0.7, Blur = 36, BackdropSaturation = 1.0, BorderIntensity = 0.4, HighlightIntensity = 0.3, Glow = 0.08, ShadowIntensity = 0.4, Refraction = 0.02 };
            t.Light = new GlassParams { Tint = "#FBFCFE", TintOpacity = 0.74, Blur = 36, BackdropSaturation = 1.0, BorderIntensity = 0.7, HighlightIntensity = 0.5, Glow = 0.05, ShadowIntensity = 0.24, Refraction = 0.02 };
            t.Gradient.Opacity = 0.25; t.CornerRadius = 14; t.Animation = 0.75;
        }),
        T("gaming", "Gaming", "Dark smoked glass with a sharp accent. Minimal motion, minimal cost.", ThemeMode.Dark, t =>
        {
            t.AccentFromWallpaper = false; t.AccentDark = "#5FE3C0"; t.AccentLight = "#0B8C6E";
            t.Dark = new GlassParams { Tint = "#0B0D11", TintOpacity = 0.82, Blur = 24, BackdropSaturation = 1.1, BorderIntensity = 0.35, HighlightIntensity = 0.2, Glow = 0.28, ShadowIntensity = 0.35, Refraction = 0 };
            t.Gradient.FromWallpaper = false; t.Gradient.Opacity = 0.3;
            t.Gradient.Stops = new() { new("#19C2A0", 0), new("#1B3A4B", 0.55), new("#0B0D11", 1) };
            t.CornerRadius = 12; t.Animation = 0.5;
        }),
        T("study", "Study", "Warm, low-stimulation paper tones that stay out of the way.", ThemeMode.Auto, t =>
        {
            t.AccentFromWallpaper = false; t.AccentDark = "#E6B47A"; t.AccentLight = "#9A5F1E";
            t.Dark = new GlassParams { Tint = "#1B1814", TintOpacity = 0.72, Blur = 44, BackdropSaturation = 0.8, BorderIntensity = 0.35, HighlightIntensity = 0.25, Glow = 0.1, ShadowIntensity = 0.38, Refraction = 0.02 };
            t.Light = new GlassParams { Tint = "#FBF6EC", TintOpacity = 0.76, Blur = 44, BackdropSaturation = 0.85, BorderIntensity = 0.7, HighlightIntensity = 0.5, Glow = 0.05, ShadowIntensity = 0.22, Refraction = 0.02 };
            t.Gradient.Opacity = 0.22; t.Gradient.Saturation = 0.7; t.CornerRadius = 20; t.Animation = 0.7;
        }),
    };
}
