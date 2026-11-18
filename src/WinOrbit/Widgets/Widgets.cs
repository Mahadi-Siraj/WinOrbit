using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;

namespace WinOrbit.Widgets;

public static class WidgetCatalog
{
    public static readonly List<WidgetKind> All = new()
    {
        new("clock", "Clock", "Minimal, stacked or ambient-date clock that scales with its size.", Glyph.Clock, "Time", 260, 128, 130, 80, i => new ClockWidget(i)),
        new("calendar", "Calendar", "Month view with today highlighted.", Glyph.Calendar, "Time", 272, 268, 230, 236, i => new CalendarWidget(i)),
        new("worldclock", "World clock", "Up to five cities with their offset from you.", Glyph.Globe, "Time", 256, 168, 200, 96, i => new WorldClockWidget(i)),
        new("system", "System monitor", "CPU, memory, GPU, network and disk at a glance.", Glyph.Chip, "System", 272, 250, 220, 130, i => new SystemWidget(i)),
        new("media", "Now playing", "Artwork, track details and playback controls.", Glyph.Music, "Media", 336, 128, 260, 112, i => new MediaWidget(i)),
        new("focus", "Focus timer", "Pomodoro, countdown and stopwatch with today's session count.", Glyph.Stopwatch, "Productivity", 256, 264, 220, 240, i => new FocusWidget(i)),
        new("notes", "Notes", "A sticky note that saves as you type. Stored only on this PC.", Glyph.Note, "Productivity", 272, 200, 180, 120, i => new NotesWidget(i)),
        new("tasks", "Tasks", "A checklist for tasks or daily objectives.", Glyph.List, "Productivity", 272, 250, 220, 160, i => new TasksWidget(i)),
        new("launcher", "Launcher", "Pin apps, files and folders. Drop items onto it to add them.", Glyph.Tiles, "Desktop", 288, 128, 150, 96, i => new LauncherWidget(i)),
        new("recyclebin", "Recycle Bin", "Item count and size, with open and empty actions.", Glyph.Delete, "Desktop", 224, 112, 190, 104, i => new RecycleBinWidget(i)),
        new("text", "Text", "A line of your own: a quote, a label or a reminder.", Glyph.Edit, "Aesthetic", 320, 96, 140, 64, i => new TextWidget(i)),
        new("ambient", "Ambient card", "A wallpaper-matched gradient card with a greeting and the date.", Glyph.Color, "Aesthetic", 320, 144, 200, 110, i => new AmbientWidget(i)),
        new("weather", "Weather", "Optional. Uses the internet only if you enable it in Settings › Privacy.", Glyph.Cloud, "Online (optional)", 272, 168, 230, 150, i => new WeatherWidget(i)),
    };

    public static WidgetKind? Find(string id) => All.FirstOrDefault(k => k.Id == id);
}

// =====================================================================================
internal sealed class ClockWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly TextBlock _a = new(), _b = new(), _c = new();
    private bool Seconds => Instance.Opt("seconds") == "1";

    public ClockWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(18, 12, 18, 12);
        var box = new Viewbox { Stretch = Stretch.Uniform };
        _a.SetResourceReference(TextBlock.ForegroundProperty, "T.Text");
        _b.SetResourceReference(TextBlock.ForegroundProperty, "T.Text2");
        _c.SetResourceReference(TextBlock.ForegroundProperty, "T.Accent");
        switch (Instance.Variant)
        {
            case "stacked":
            {
                var g = Ui.Grid("auto,auto", "auto,auto");
                _a.FontSize = 62; _a.FontWeight = FontWeights.Bold; _a.LineHeight = 58; _a.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                _c.FontSize = 62; _c.FontWeight = FontWeights.Light; _c.LineHeight = 58; _c.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                _b.FontSize = 13; _b.Margin = new Thickness(16, 6, 0, 0); _b.VerticalAlignment = VerticalAlignment.Center; _b.LineHeight = 20;
                g.Children.Add(_a); g.Children.Add(_c.At(1, 0)); g.Children.Add(_b.At(0, 1, 2));
                box.Child = g;
                break;
            }
            case "date":
            {
                var sp = new StackPanel();
                _c.FontSize = 13; _c.FontWeight = FontWeights.SemiBold;
                _a.FontSize = 64; _a.FontWeight = FontWeights.SemiBold; _a.Margin = new Thickness(-3, -10, 0, -8);
                _b.FontSize = 15;
                sp.Children.Add(_c); sp.Children.Add(_a); sp.Children.Add(_b);
                box.Child = sp; box.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            }
            default:
            {
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                _a.FontSize = 58; _a.FontWeight = FontWeights.SemiBold;
                _c.FontSize = 22; _c.FontWeight = FontWeights.SemiBold; _c.VerticalAlignment = VerticalAlignment.Bottom; _c.Margin = new Thickness(6, 0, 0, 11);
                _b.FontSize = 14; _b.HorizontalAlignment = HorizontalAlignment.Center; _b.Margin = new Thickness(0, -4, 0, 0);
                line.Children.Add(_a); line.Children.Add(_c);
                sp.Children.Add(line); sp.Children.Add(_b);
                box.Child = sp;
                break;
            }
        }
        Children.Add(box);
        _timer.Tick += (_, _) => Tick();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        var h24 = Hub.Settings.General.Use24Hour;
        var ci = CultureInfo.CurrentCulture;
        switch (Instance.Variant)
        {
            case "stacked":
                _a.Text = now.ToString(h24 ? "HH" : "hh", ci); _c.Text = now.ToString("mm", ci);
                _b.Text = now.ToString("dddd", ci) + "\n" + now.ToString("d MMMM", ci);
                break;
            case "date":
                _c.Text = now.ToString("dddd", ci).ToUpper(ci); _a.Text = now.Day.ToString(ci);
                _b.Text = now.ToString("MMMM yyyy", ci) + "  ·  " + Fmt.Time(now, h24);
                break;
            default:
                _a.Text = Fmt.Time(now, h24);
                _c.Text = Seconds ? now.ToString("ss", ci) : h24 ? "" : now.ToString("tt", ci);
                _c.Visibility = _c.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                _b.Text = now.ToString("dddd, d MMMM", ci);
                break;
        }
        // Sleep until the next change: one wake-up a minute unless seconds are shown.
        _timer.Interval = Seconds && Instance.Variant is "" or "minimal" ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 5) : TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(30);
    }

    protected override void OnStart() { Tick(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Style", "variant", ("Minimal", ""), ("Stacked", "stacked"), ("Ambient date", "date")));
        items.Add(Item("Show seconds", () => { Instance.Options["seconds"] = Seconds ? "0" : "1"; Save(); Host?.Rebuild(); }, Seconds));
    }
}

// =====================================================================================
internal sealed class CalendarWidget : WidgetView
{
    private readonly MonthView _month = new();
    private readonly DispatcherTimer _midnight = new();

    public CalendarWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(16, 14, 16, 12);
        Children.Add(_month);
        SizeChanged += (_, e) => { _month.CellSize = Math.Clamp(Math.Min((e.NewSize.Width - 8) / 7.4, (e.NewSize.Height - 54) / 6.3), 20, 44); _month.Build(); };
        _midnight.Tick += (_, _) => { _month.ShowToday(); Arm(); };
    }

    private void Arm() { _midnight.Interval = DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(2); _midnight.Start(); }
    protected override void OnStart() { _month.ShowToday(); Arm(); }
    protected override void OnStop() => _midnight.Stop();
    public override void BuildMenu(ItemCollection items) => items.Add(Item("Go to today", _month.ShowToday));
}

// =====================================================================================
internal sealed class WorldClockWidget : WidgetView
{
    public static readonly (string Id, string City)[] Cities =
    {
        ("UTC", "UTC"), ("Pacific Standard Time", "Los Angeles"), ("Eastern Standard Time", "New York"), ("E. South America Standard Time", "São Paulo"),
        ("GMT Standard Time", "London"), ("W. Europe Standard Time", "Berlin"), ("Arabian Standard Time", "Dubai"), ("India Standard Time", "Mumbai"),
        ("Singapore Standard Time", "Singapore"), ("China Standard Time", "Shanghai"), ("Tokyo Standard Time", "Tokyo"), ("AUS Eastern Standard Time", "Sydney")
    };

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly StackPanel _rows = new() { VerticalAlignment = VerticalAlignment.Center };
    private List<string> Zones => Instance.Opt("zones", "Eastern Standard Time;GMT Standard Time;Tokyo Standard Time").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    public WorldClockWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(18, 12, 18, 12);
        Children.Add(_rows);
        _timer.Tick += (_, _) => Tick();
    }

    private void Tick()
    {
        _rows.Children.Clear();
        var now = DateTime.UtcNow;
        var local = TimeZoneInfo.Local.GetUtcOffset(now);
        foreach (var id in Zones.Take(5))
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { continue; }
            var there = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
            var diff = tz.GetUtcOffset(now) - local;
            var city = Cities.FirstOrDefault(c => c.Id == id).City ?? id;
            var row = Ui.Grid("*,auto");
            row.Margin = new Thickness(0, 5, 0, 5);
            var left = new StackPanel();
            left.Children.Add(Ui.Text(city, "Text.Body", 14, FontWeights.SemiBold));
            var rel = diff == TimeSpan.Zero ? "Same time" : $"{(diff > TimeSpan.Zero ? "+" : "−")}{Math.Abs(diff.TotalHours):0.#} h";
            if (there.Date != DateTime.Now.Date) rel += there.Date > DateTime.Now.Date ? " · tomorrow" : " · yesterday";
            left.Children.Add(Ui.Text(rel, "Text.Caption", 11));
            row.Children.Add(left);
            row.Children.Add(Ui.Text(Fmt.Time(there, Hub.Settings.General.Use24Hour), "Text.Sub", 22, FontWeights.SemiBold).With(t => t.VerticalAlignment = VerticalAlignment.Center).At(0, 1));
            _rows.Children.Add(row);
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(Ui.Text("Right-click to choose cities.", "Text.Caption"));
        _timer.Interval = TimeSpan.FromSeconds(60 - DateTime.Now.Second) + TimeSpan.FromMilliseconds(30);
    }

    protected override void OnStart() { Tick(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();

    public override void BuildMenu(ItemCollection items)
    {
        var cities = new MenuItem { Header = "Cities" };
        var zones = Zones;
        foreach (var (id, city) in Cities)
            cities.Items.Add(Item(city, () =>
            {
                if (!zones.Remove(id)) zones.Add(id);
                Instance.Options["zones"] = string.Join(';', zones);
                Save(); Tick();
            }, zones.Contains(id)));
        items.Add(cities);
    }
}

// =====================================================================================
internal sealed class SystemWidget : WidgetView
{
    private IDisposable? _sub;
    private readonly Dictionary<string, (TextBlock Value, ProgressBar? Bar, Ring? Ring)> _rows = new();
    private readonly Sparkline _spark = new() { Height = 34 };
    private readonly TextBlock _footer = Ui.Text("", "Text.Caption", 11);

    public SystemWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(18, 14, 18, 14);
        if (Instance.Variant == "rings")
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var (id, label) in new[] { ("cpu", "CPU"), ("ram", "RAM"), ("gpu", "GPU") })
            {
                var cell = new Grid { Margin = new Thickness(5) };
                var ring = new Ring { Thickness = 6 };
                var box = new Viewbox { Margin = new Thickness(14) };
                var sp = new StackPanel();
                var value = Ui.Text("—", "Text.Sub", 20).With(t => t.HorizontalAlignment = HorizontalAlignment.Center);
                sp.Children.Add(value);
                sp.Children.Add(Ui.Text(label, "Text.Label", 10).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
                box.Child = sp;
                cell.Children.Add(ring); cell.Children.Add(box);
                grid.Children.Add(cell);
                _rows[id] = (value, null, ring);
            }
            Children.Add(grid);
            return;
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (Instance.Variant == "graph") stack.Children.Add(_spark.With(s => s.Margin = new Thickness(0, 0, 0, 10)));
        foreach (var (id, label) in new[] { ("cpu", "CPU"), ("ram", "Memory"), ("gpu", "GPU"), ("net", "Network") })
        {
            var head = Ui.Grid("auto,*,auto");
            head.Children.Add(Ui.LineIcon(Ui.MetricIcon(id), 15, Ui.MetricBrush(id)).With(v => { v.Margin = new Thickness(0, 0, 8, 0); v.VerticalAlignment = VerticalAlignment.Center; }));
            head.Children.Add(Ui.Text(label, "Text.Caption", 12).With(t => t.VerticalAlignment = VerticalAlignment.Center).At(0, 1));
            var value = Ui.Text("—", "Text.Body", 12.5, FontWeights.SemiBold).At(0, 2);
            head.Children.Add(value);
            stack.Children.Add(head);
            ProgressBar? bar = null;
            if (id != "net") { bar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 5, 0, 11) }; stack.Children.Add(bar); }
            else head.Margin = new Thickness(0, 0, 0, 9);
            _rows[id] = (value, bar, null);
        }
        stack.Children.Add(_footer);
        Children.Add(stack);
    }

    private void OnSample(SystemSample s)
    {
        void Set(string id, double? pct, string text)
        {
            if (!_rows.TryGetValue(id, out var r)) return;
            r.Value.Text = text;
            if (r.Bar is not null) r.Bar.Value = pct ?? 0;
            if (r.Ring is not null) r.Ring.Value = (pct ?? 0) / 100;
        }
        Set("cpu", s.Cpu, $"{s.Cpu:0}%");
        Set("ram", s.Ram, Instance.Variant == "rings" ? $"{s.Ram:0}%" : $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB");
        Set("gpu", s.Gpu, s.Gpu is { } g ? (s.GpuTemp is { } t && Instance.Variant != "rings" ? $"{g:0}%  ·  {t:0}°C" : $"{g:0}%") : "n/a");
        Set("net", null, $"↓ {Fmt.Rate(s.NetDown)}   ↑ {Fmt.Rate(s.NetUp)}");
        _footer.Text = $"Disk {s.DiskActive:0}%  ·  Up {Fmt.Uptime(s.Uptime)}" + (s.Battery is { } b ? $"  ·  Battery {b}%" : "");
        if (Instance.Variant == "graph") _spark.Update(Hub.Monitor.CpuHistory);
    }

    protected override void OnStart() => _sub = Hub.Monitor.Subscribe(OnSample);
    protected override void OnStop() { _sub?.Dispose(); _sub = null; }
    public override void BuildMenu(ItemCollection items) => items.Add(Choice("Style", "variant", ("Bars", ""), ("Bars + CPU graph", "graph"), ("Rings", "rings")));
}

// =====================================================================================
internal sealed class MediaWidget : WidgetView
{
    private readonly Border _art = new() { CornerRadius = new CornerRadius(14) };
    private readonly TextBlock _artGlyph = Ui.Icon(Glyph.Music, 26, "T.Accent");
    private readonly TextBlock _title = Ui.Text("", "Text.Sub", 15), _artist = Ui.Text("", "Text.Caption", 12.5);
    private readonly Button _play;
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 6, 0, 0) };

    public MediaWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(14);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _art.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
        _art.Child = _artGlyph;
        _art.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualHeight") { Source = _art });
        Children.Add(_art);
        _title.TextTrimming = TextTrimming.CharacterEllipsis; _title.TextWrapping = TextWrapping.NoWrap;
        _artist.TextTrimming = TextTrimming.CharacterEllipsis; _artist.TextWrapping = TextWrapping.NoWrap;
        var sp = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(_title); sp.Children.Add(_artist);
        _controls.Children.Add(Ui.IconButton(Glyph.Previous, "Previous track", () => Hub.Media.Previous()));
        _play = Ui.IconButton(Glyph.Play, "Play or pause", () => Hub.Media.PlayPause(), 36, "Button.IconAccent");
        _play.Margin = new Thickness(4, 0, 4, 0);
        _controls.Children.Add(_play);
        _controls.Children.Add(Ui.IconButton(Glyph.Next, "Next track", () => Hub.Media.Next()));
        sp.Children.Add(_controls);
        Children.Add(sp.At(0, 1));
    }

    private void Update()
    {
        var m = Hub.Media;
        var has = m.HasTrack;
        _title.Text = has ? m.DisplayTitle : "Nothing playing";
        _artist.Text = has ? (string.IsNullOrWhiteSpace(m.Artist) ? m.Source : m.Artist) : "Start music in any app";
        _play.Content = m.IsPlaying ? Glyph.Pause : Glyph.Play;
        _controls.Opacity = has ? 1 : 0.45;
        if (has && m.Art is not null) { _art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; _artGlyph.Visibility = Visibility.Collapsed; }
        else { _art.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft"); _artGlyph.Visibility = Visibility.Visible; }
    }

    protected override void OnStart() { Hub.Media.Changed += Update; Update(); }
    protected override void OnStop() => Hub.Media.Changed -= Update;
    public override void BuildMenu(ItemCollection items) => items.Add(Item("Open sound output settings", () => Island.IslandWindow.OpenUri("ms-settings:sound")));
}

// =====================================================================================
internal sealed class FocusWidget : WidgetView
{
    private readonly Ring _ring = new() { Thickness = 8 };
    private readonly TextBlock _time = Ui.Text("25:00", "Text.Display", 34), _mode = Ui.Text("Focus", "Text.Caption", 12), _stats = Ui.Text("", "Text.Caption", 11.5);
    private readonly Button _start;

    public FocusWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(16, 16, 16, 12);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dial = new Grid { MaxWidth = 190, MaxHeight = 190 };
        dial.Children.Add(_ring);
        var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _time.HorizontalAlignment = HorizontalAlignment.Center; _mode.HorizontalAlignment = HorizontalAlignment.Center;
        centre.Children.Add(_time); centre.Children.Add(_mode);
        dial.Children.Add(new Viewbox { Child = centre, Margin = new Thickness(26) });
        Children.Add(dial);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        _start = Ui.Button("Start", () => Hub.Timers.Toggle(), "Button.Accent");
        _start.MinWidth = 84;
        buttons.Children.Add(_start);
        buttons.Children.Add(Ui.IconButton(Glyph.Refresh, "Reset", () => Hub.Timers.Reset()).With(b => b.Margin = new Thickness(8, 0, 0, 0)));
        Children.Add(buttons.At(1, 0));
        _stats.HorizontalAlignment = HorizontalAlignment.Center; _stats.Margin = new Thickness(0, 9, 0, 0); _stats.TextAlignment = TextAlignment.Center;
        Children.Add(_stats.At(2, 0));
    }

    private void Update()
    {
        var t = Hub.Timers;
        _time.Text = t.Display;
        _mode.Text = t.ModeLabel;
        _ring.Value = t.Mode == TimerMode.Stopwatch ? (t.Elapsed.TotalSeconds % 60) / 60.0 : 1 - t.Progress;
        _start.Content = t.Running ? "Pause" : t.IsActive ? "Resume" : "Start";
        _stats.Text = t.CompletedToday == 0 ? "No sessions yet today" : $"Today: {t.CompletedToday} session{(t.CompletedToday == 1 ? "" : "s")} · {t.MinutesToday} min";
    }

    protected override void OnStart() { Hub.Timers.Changed += Update; Update(); }
    protected override void OnStop() => Hub.Timers.Changed -= Update;

    public override void BuildMenu(ItemCollection items)
    {
        var t = Hub.Timers;
        var mode = new MenuItem { Header = "Mode" };
        mode.Items.Add(Item("Focus (Pomodoro)", () => t.SetMode(TimerMode.Focus), t.Mode is TimerMode.Focus or TimerMode.Break));
        mode.Items.Add(Item("Countdown", () => t.SetMode(TimerMode.Countdown), t.Mode == TimerMode.Countdown));
        mode.Items.Add(Item("Stopwatch", () => t.SetMode(TimerMode.Stopwatch), t.Mode == TimerMode.Stopwatch));
        items.Add(mode);
        var len = new MenuItem { Header = "Length" };
        foreach (var m in new[] { 5, 10, 15, 25, 45, 50, 60, 90 })
            len.Items.Add(Item($"{m} minutes", () => t.SetMode(t.Mode is TimerMode.Stopwatch or TimerMode.Break ? TimerMode.Focus : t.Mode, m), Math.Abs(t.Total.TotalMinutes - m) < 0.01 && t.Mode != TimerMode.Stopwatch));
        items.Add(len);
        var brk = new MenuItem { Header = "Break length" };
        foreach (var m in new[] { 3, 5, 10, 15 }) brk.Items.Add(Item($"{m} minutes", () => t.BreakMinutes = m, t.BreakMinutes == m));
        items.Add(brk);
    }
}

// =====================================================================================
internal sealed class NotesWidget : WidgetView
{
    private readonly TextBox _box = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13.5 };
    private readonly TextBlock _hint = Ui.Text("Write a note…", "Text.Caption", 13.5, null, "T.Text3");
    private bool _loading;

    public NotesWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(16, 12, 12, 12);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(Ui.Text(Instance.Opt("title", "NOTES"), "Text.Label").With(t => t.Margin = new Thickness(0, 0, 0, 8)));
        _box.SetResourceReference(StyleProperty, "TextBox.Bare");
        System.Windows.Automation.AutomationProperties.SetName(_box, "Note text");
        _hint.IsHitTestVisible = false;
        Children.Add(_hint.At(1, 0));
        Children.Add(_box.At(1, 0));
        _box.TextChanged += (_, _) =>
        {
            _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_loading) return;
            Hub.Store.Data.Notes[Instance.Id] = _box.Text;
            Hub.Store.SaveData();
        };
    }

    protected override void OnStart()
    {
        _loading = true;
        _box.Text = Hub.Store.Data.Notes.TryGetValue(Instance.Id, out var t) ? t : "";
        _loading = false;
    }

    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items) => items.Add(Item("Rename…", () =>
    {
        if (Dialogs.Prompt("Rename note", "Title shown at the top of this note.", Instance.Opt("title", "NOTES")) is { Length: > 0 } t) { Instance.Options["title"] = t.ToUpperInvariant(); Save(); Host?.Rebuild(); }
    }));
}

// =====================================================================================
internal sealed class TasksWidget : WidgetView
{
    private readonly StackPanel _list = new();
    private readonly TextBox _input = new() { FontSize = 13 };
    private readonly TextBlock _count = Ui.Text("", "Text.Caption", 11);
    private List<TaskItem> Items
    {
        get
        {
            if (!Hub.Store.Data.Tasks.TryGetValue(Instance.Id, out var l)) Hub.Store.Data.Tasks[Instance.Id] = l = new List<TaskItem>();
            return l;
        }
    }

    public TasksWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(16, 12, 12, 12);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = Ui.Grid("*,auto");
        head.Margin = new Thickness(0, 0, 4, 6);
        head.Children.Add(Ui.Text(Instance.Variant == "objectives" ? "TODAY'S OBJECTIVES" : Instance.Opt("title", "TASKS"), "Text.Label"));
        head.Children.Add(_count.At(0, 1));
        Children.Add(head);
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Focusable = false }.At(1, 0));
        _input.Margin = new Thickness(0, 8, 4, 0);
        _input.ToolTip = "Type a task and press Enter";
        System.Windows.Automation.AutomationProperties.SetName(_input, "New task");
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_input.Text)) return;
            Items.Add(new TaskItem { Text = _input.Text.Trim() });
            _input.Clear();
            Hub.Store.SaveData();
            Render();
        };
        Children.Add(_input.At(2, 0));
    }

    private void Render()
    {
        _list.Children.Clear();
        var items = Items;
        foreach (var task in items)
        {
            var row = Ui.Grid("*,auto");
            row.Margin = new Thickness(0, 3, 0, 3);
            var text = new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Opacity = task.Done ? 0.5 : 1, TextDecorations = task.Done ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { IsChecked = task.Done, Content = text };
            check.Click += (_, _) => { task.Done = check.IsChecked == true; Hub.Store.SaveData(); Render(); };
            row.Children.Add(check);
            var del = Ui.IconButton(Glyph.Cancel, "Delete task", () => { items.Remove(task); Hub.Store.SaveData(); Render(); }, 24).At(0, 1);
            del.Opacity = 0;
            row.MouseEnter += (_, _) => del.Opacity = 0.8;
            row.MouseLeave += (_, _) => del.Opacity = 0;
            row.Background = Brushes.Transparent;
            row.Children.Add(del);
            _list.Children.Add(row);
        }
        if (items.Count == 0) _list.Children.Add(Ui.Text("Nothing here yet. Add your first item below.", "Text.Caption", 12, null, "T.Text3"));
        _count.Text = items.Count == 0 ? "" : $"{items.Count(t => t.Done)}/{items.Count}";
    }

    protected override void OnStart() => Render();
    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Choice("Kind", "variant", ("Tasks", ""), ("Daily objectives", "objectives")));
        items.Add(Item("Clear completed", () => { Items.RemoveAll(t => t.Done); Hub.Store.SaveData(); Render(); }));
    }
}

// =====================================================================================
internal sealed class LauncherWidget : WidgetView
{
    private readonly WrapPanel _panel = new() { VerticalAlignment = VerticalAlignment.Center };
    private List<string> Paths => Instance.Opt("items").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    public LauncherWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(12, 10, 12, 10);
        Background = Brushes.Transparent;
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _panel, Focusable = false });
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { var p = Paths; p.AddRange(files.Where(f => !p.Contains(f, StringComparer.OrdinalIgnoreCase))); Store(p); }
        };
    }

    private void Store(List<string> paths) { Instance.Options["items"] = string.Join('|', paths.Take(24)); Save(); Render(); }

    private void Render()
    {
        _panel.Children.Clear();
        var paths = Paths;
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name)) name = path;
            var b = new Button { Width = 76, Padding = new Thickness(4, 8, 4, 6), Margin = new Thickness(2), ToolTip = path };
            b.SetResourceReference(StyleProperty, "Button.Ghost");
            var sp = new StackPanel();
            var icon = ShellIcon(path);
            sp.Children.Add(icon is not null ? new Image { Source = icon, Width = 32, Height = 32 } : Ui.Icon(Glyph.Page, 26));
            sp.Children.Add(Ui.Text(name, "Text.Caption", 11).With(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 5, 0, 0); }));
            b.Content = sp;
            b.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { Dialogs.Message("Could not open item", ex.Message, path); }
            };
            var menu = new ContextMenu();
            menu.Items.Add(Item("Remove from launcher", () => { var p = Paths; p.Remove(path); Store(p); }));
            b.ContextMenu = menu;
            _panel.Children.Add(b);
        }
        if (paths.Count == 0)
        {
            var hint = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            hint.Children.Add(Ui.Text("Drop apps, files or folders here", "Text.Body", 13, FontWeights.SemiBold));
            hint.Children.Add(Ui.Text("or right-click › Add shortcut…", "Text.Caption", 12));
            _panel.Children.Add(hint);
        }
    }

    internal static ImageSource? ShellIcon(string path)
    {
        try
        {
            var info = new Native.SHFILEINFO();
            var res = Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Native.DestroyIcon(info.hIcon); }
        }
        catch { return null; }
    }

    protected override void OnStart() => Render();
    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Item("Add shortcut…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose an app or file to pin", Multiselect = true, DereferenceLinks = false, Filter = "Apps and shortcuts|*.exe;*.lnk;*.url|All files|*.*" };
            if (dlg.ShowDialog() == true) { var p = Paths; p.AddRange(dlg.FileNames.Where(f => !p.Contains(f, StringComparer.OrdinalIgnoreCase))); Store(p); }
        }));
        items.Add(Item("Add folder…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to pin" };
            if (dlg.ShowDialog() == true) { var p = Paths; if (!p.Contains(dlg.FolderName)) p.Add(dlg.FolderName); Store(p); }
        }));
    }
}

// =====================================================================================
internal sealed class RecycleBinWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(90) };
    private readonly TextBlock _value = Ui.Text("—", "Text.Sub", 17), _detail = Ui.Text("", "Text.Caption", 12);
    private readonly Button _empty;

    public RecycleBinWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(16, 12, 14, 12);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Icon(Glyph.Delete, 19, "T.Accent") };
        badge.SetResourceReference(Border.BackgroundProperty, "T.AccentSoft");
        Children.Add(badge);
        var sp = new StackPanel { Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(_value); sp.Children.Add(_detail);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        var open = Ui.Button("Open", () => Island.IslandWindow.OpenUri("shell:RecycleBinFolder"));
        open.Padding = new Thickness(10, 4, 10, 4); open.FontSize = 12;
        _empty = Ui.Button("Empty", Empty);
        _empty.Padding = new Thickness(10, 4, 10, 4); _empty.FontSize = 12; _empty.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(open); actions.Children.Add(_empty);
        sp.Children.Add(actions);
        Children.Add(sp.At(0, 1));
        _timer.Tick += (_, _) => Refresh();
        MouseEnter += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var info = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        if (Native.SHQueryRecycleBin(null, ref info) != 0) { _value.Text = "Recycle Bin"; _detail.Text = "Unavailable"; return; }
        _value.Text = info.i64NumItems == 0 ? "Empty" : $"{info.i64NumItems:N0} item{(info.i64NumItems == 1 ? "" : "s")}";
        _detail.Text = info.i64NumItems == 0 ? "Recycle Bin" : Fmt.Size(info.i64Size) + " in Recycle Bin";
        _empty.IsEnabled = info.i64NumItems > 0;
    }

    private void Empty()
    {
        if (!Dialogs.Confirm("Empty the Recycle Bin?", "Everything in the Recycle Bin will be permanently deleted. This cannot be undone.", "Empty")) return;
        Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION);
        Refresh();
    }

    protected override void OnStart() { Refresh(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();
}

// =====================================================================================
internal sealed class TextWidget : WidgetView
{
    public TextWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(20, 12, 20, 12);
        var text = Instance.Opt("text", "Make it simple, but significant.");
        var size = double.TryParse(Instance.Opt("size", "20"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 20;
        var t = new TextBlock
        {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Instance.Variant == "bold" ? FontWeights.Bold : Instance.Variant == "light" ? FontWeights.Light : FontWeights.SemiBold,
            TextAlignment = Instance.Opt("align") switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left },
            LineHeight = size * 1.3
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, Instance.Opt("accent") == "1" ? "T.Accent" : "T.Text");
        Children.Add(t);
        Background = Brushes.Transparent;
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Edit(); e.Handled = true; } };
    }

    private void Edit()
    {
        if (Dialogs.Prompt("Edit text", "What should this widget say?", Instance.Opt("text", "Make it simple, but significant.")) is { Length: > 0 } t)
        {
            Instance.Options["text"] = t; Save(); Host?.Rebuild();
        }
    }

    protected override void OnStart() { }
    protected override void OnStop() { }

    public override void BuildMenu(ItemCollection items)
    {
        items.Add(Item("Edit text…", Edit));
        items.Add(Choice("Size", "size", ("Small", "14"), ("Medium", "20"), ("Large", "28"), ("Display", "40")));
        items.Add(Choice("Weight", "variant", ("Light", "light"), ("Semibold", ""), ("Bold", "bold")));
        items.Add(Choice("Align", "align", ("Left", ""), ("Centre", "center"), ("Right", "right")));
        items.Add(Item("Use accent colour", () => { Instance.Options["accent"] = Instance.Opt("accent") == "1" ? "0" : "1"; Save(); Host?.Rebuild(); }, Instance.Opt("accent") == "1"));
    }
}

// =====================================================================================
internal sealed class AmbientWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly TextBlock _greeting = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _date = new() { FontSize = 13.5, Foreground = Brushes.White, Opacity = 0.86, Margin = new Thickness(0, 4, 0, 0) };

    public AmbientWidget(WidgetInstance i) : base(i)
    {
        // The wallpaper gradient at full strength, softened by a dark veil so white text always reads.
        var wash = new Border { Margin = new Thickness(6) };
        wash.SetResourceReference(Border.BackgroundProperty, "T.WashStrong");
        wash.SetResourceReference(Border.CornerRadiusProperty, "R.Control");
        var veil = new Border { Margin = new Thickness(6), Background = new LinearGradientBrush(Color.FromArgb(40, 0, 0, 0), Color.FromArgb(120, 0, 0, 0), 90) };
        veil.SetResourceReference(Border.CornerRadiusProperty, "R.Control");
        Children.Add(wash); Children.Add(veil);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(22, 0, 22, 20) };
        sp.Children.Add(_greeting); sp.Children.Add(_date);
        Children.Add(sp);
        _timer.Tick += (_, _) => Tick();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        var name = Instance.Opt("name");
        var hello = now.Hour switch { < 5 => "Good night", < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };
        _greeting.Text = string.IsNullOrWhiteSpace(name) ? hello : $"{hello}, {name}";
        _date.Text = now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        _timer.Interval = TimeSpan.FromMinutes(60 - now.Minute);
    }

    protected override void OnStart() { Tick(); _timer.Start(); }
    protected override void OnStop() => _timer.Stop();

    public override void BuildMenu(ItemCollection items) => items.Add(Item("Set name…", () =>
    {
        if (Dialogs.Prompt("Greeting name", "Name to greet (leave empty for none). Stored only on this PC.", Instance.Opt("name")) is { } n) { Instance.Options["name"] = n; Save(); Tick(); }
    }));
}

// =====================================================================================
internal sealed class WeatherWidget : WidgetView
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(31) };
    private readonly StackPanel _body = new() { VerticalAlignment = VerticalAlignment.Center };

    public WeatherWidget(WidgetInstance i) : base(i)
    {
        Margin = new Thickness(18, 14, 18, 14);
        Children.Add(_body);
        _timer.Tick += async (_, _) => await Refresh(false);
    }

    private async Task Refresh(bool force)
    {
        _body.Children.Clear();
        var p = Hub.Settings.Privacy;
        if (!Hub.Weather.Enabled)
        {
            _body.Children.Add(Ui.Icon(Glyph.Cloud, 24, "T.Text3").With(t => t.HorizontalAlignment = HorizontalAlignment.Left));
            _body.Children.Add(Ui.Text("Weather is off", "Text.Sub", 15).With(t => t.Margin = new Thickness(0, 8, 0, 2)));
            _body.Children.Add(Ui.Text("It needs the internet, so it stays off until you enable it and choose a place.", "Text.Caption", 12));
            _body.Children.Add(Ui.Button("Open privacy settings", () => App.Current.ShowMain("settings")).With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); b.FontSize = 12; }));
            return;
        }
        try
        {
            var r = await Hub.Weather.GetAsync(force);
            if (r is null || !Running) return;
            _body.Children.Clear();
            var unit = p.WeatherFahrenheit ? "°F" : "°C";
            var (text, icon) = WeatherService.Describe(r.Code);
            var top = Ui.Grid("auto,*");
            top.Children.Add(Ui.LineIcon(icon, 36, "T.Accent", 1.5).With(v => v.VerticalAlignment = VerticalAlignment.Center));
            var sp = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            sp.Children.Add(Ui.Text($"{r.Temperature:0}{unit}", "Text.Display", 32));
            sp.Children.Add(Ui.Text($"{text} · feels {r.Feels:0}{unit}", "Text.Caption", 12));
            top.Children.Add(sp.At(0, 1));
            _body.Children.Add(top);
            _body.Children.Add(Ui.Text(p.WeatherPlace.ToUpperInvariant(), "Text.Label").With(t => t.Margin = new Thickness(0, 10, 0, 6)));
            var days = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var d in r.Days.Skip(1).Take(3))
            {
                var cell = new StackPanel();
                cell.Children.Add(Ui.Text(d.Date.ToString("ddd", CultureInfo.CurrentCulture), "Text.Caption", 11));
                cell.Children.Add(Ui.LineIcon(WeatherService.Describe(d.Code).Icon, 17, "T.Text").With(t => { t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 4, 0, 4); }));
                cell.Children.Add(Ui.Text($"{d.Max:0}° / {d.Min:0}°", "Text.Body", 12));
                days.Children.Add(cell);
            }
            _body.Children.Add(days);
        }
        catch (Exception ex)
        {
            _body.Children.Clear();
            _body.Children.Add(Ui.Text("Weather unavailable", "Text.Sub", 15));
            _body.Children.Add(Ui.Text(ex.Message, "Text.Caption", 12));
        }
    }

    protected override async void OnStart() { _timer.Start(); await Refresh(false); }
    protected override void OnStop() => _timer.Stop();
    public override void BuildMenu(ItemCollection items) => items.Add(Item("Refresh now", async () => await Refresh(true)));
}
