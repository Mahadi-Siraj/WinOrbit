using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Services;

namespace WinOrbit.Island;

/// <summary>
/// The calendar page: pick a day on the month grid to see its events; type "9:30 Stand-up" and press Enter to add
/// one. Events stay on this PC. Ten minutes before an event with a time, and again when it starts, the island grows
/// into a reminder card.
/// </summary>
public partial class IslandWindow
{
    private static readonly Color EventRed = Color.FromRgb(0xFF, 0x5A, 0x5F);
    private DateTime _calDay = DateTime.Today;
    private readonly HashSet<string> _reminded = new();

    private static List<CalendarEvent> Events => Hub.Store.Data.Events;

    private void InitCalendar()
    {
        CalView.Marked = d => Events.Any(e => e.Date.Date == d);
        CalView.DayClicked += d => { _calDay = d; CalView.Select(d); UpdateCalendar(); Poke(); };
    }

    // ----- parsing and queries (pure, so they can be tested) -----

    private static readonly Regex TimeFirst = new(@"^\s*(\d{1,2})(?:[:.](\d{2}))?\s*(a\.?m\.?|p\.?m\.?)?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// "9:30 Stand-up", "14:30 Dentist", "7pm Dinner", "9.15am Gym" → a time and a title. A bare number without minutes
    /// or am/pm ("3 books to return") is not a time, so it stays part of the title.
    /// </summary>
    internal static (TimeSpan? Time, string Title) ParseEvent(string text)
    {
        text = text.Trim();
        var m = TimeFirst.Match(text);
        if (!m.Success || (!m.Groups[2].Success && !m.Groups[3].Success)) return (null, text);
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (minute > 59) return (null, text);
        if (m.Groups[3].Success)
        {
            if (hour is < 1 or > 12) return (null, text);
            var pm = m.Groups[3].Value.StartsWith('p') || m.Groups[3].Value.StartsWith('P');
            hour = hour % 12 + (pm ? 12 : 0);
        }
        else if (hour > 23) return (null, text);
        return (new TimeSpan(hour, minute, 0), m.Groups[4].Value.Trim());
    }

    /// <summary>A day's events: all-day ones first, then by time.</summary>
    internal static List<CalendarEvent> EventsOn(IEnumerable<CalendarEvent> all, DateTime day) =>
        all.Where(e => e.Date.Date == day.Date).OrderBy(e => e.Time ?? TimeSpan.MinValue).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>The next timed event that hasn't started yet today, if any.</summary>
    internal static CalendarEvent? NextToday(IEnumerable<CalendarEvent> all, DateTime now) =>
        all.Where(e => e.Start is { } s && s >= now && s.Date == now.Date).OrderBy(e => e.Start).FirstOrDefault();

    /// <summary>Which reminder is due at <paramref name="now"/>: "soon" (≤ 10 minutes away), "now" (started up to 5 minutes ago) or null.</summary>
    internal static string? ReminderDue(CalendarEvent e, DateTime now)
    {
        if (e.Start is not { } s) return null;
        var lead = s - now;
        if (lead > TimeSpan.FromMinutes(1) && lead <= TimeSpan.FromMinutes(10.5)) return "soon";
        if (lead <= TimeSpan.FromMinutes(1) && lead > TimeSpan.FromMinutes(-5)) return "now";
        return null;
    }

    // ----- the page -----

    private void UpdateCalendar()
    {
        var ci = CultureInfo.CurrentCulture;
        var d = _calDay;
        var today = DateTime.Today;
        var prefix = d == today ? "Today · " : d == today.AddDays(1) ? "Tomorrow · " : d == today.AddDays(-1) ? "Yesterday · " : "";
        CalDay.Text = (prefix + d.ToString("dddd", ci)).ToUpper(ci);
        CalNumber.Text = d.Day.ToString(ci);
        CalMonth.Text = d.ToString("MMMM yyyy", ci);
        CalWeek.Text = $"Week {ISOWeek.GetWeekOfYear(d)} · day {d.DayOfYear}";

        CalEvents.Children.Clear();
        var list = EventsOn(Events, d);
        if (list.Count == 0)
            CalEvents.Children.Add(Ui.Text(d == today ? "Nothing planned today" : "Nothing planned", "Text.Caption", 12, brush: "T.Text3").With(t => t.Margin = new Thickness(0, 2, 0, 0)));
        foreach (var ev in list) CalEvents.Children.Add(EventRow(ev));
    }

    private FrameworkElement EventRow(CalendarEvent ev)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 3), Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2, 7, 2), Background = Frozen(new SolidColorBrush(EventRed)) };
        var when = Ui.Text(ev.Start is { } s ? Fmt.Time(s, Hub.Settings.General.Use24Hour) : "All day", "Text.Caption", 11.5, FontWeights.SemiBold, "T.Text2");
        when.Margin = new Thickness(0, 0, 7, 0); when.VerticalAlignment = VerticalAlignment.Center; when.MinWidth = 34;
        var title = Ui.Text(ev.Title, "Text.Body", 12.5);
        title.TextTrimming = TextTrimming.CharacterEllipsis; title.VerticalAlignment = VerticalAlignment.Center; title.ToolTip = ev.Title;
        var remove = new Button { Content = Glyph.Cancel, FontSize = 9, Width = 20, Height = 20, ToolTip = "Remove", Visibility = Visibility.Hidden };
        remove.SetResourceReference(StyleProperty, "Button.Icon");
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + ev.Title);
        remove.Click += (_, _) =>
        {
            Events.Remove(ev);
            Hub.Store.SaveData();
            UpdateCalendar();
            CalView.Build();
        };
        row.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        row.MouseLeave += (_, _) => remove.Visibility = Visibility.Hidden;

        Grid.SetColumn(when, 1); Grid.SetColumn(title, 2); Grid.SetColumn(remove, 3);
        row.Children.Add(bar); row.Children.Add(when); row.Children.Add(title); row.Children.Add(remove);
        if (ev.Start is { } start && start < DateTime.Now) row.Opacity = 0.5;   // already happened
        return row;
    }

    private void OnCalAddChanged(object sender, TextChangedEventArgs e) =>
        CalAddHint.Visibility = CalAdd.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnCalAddKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var (time, title) = ParseEvent(CalAdd.Text);
        if (title.Length == 0) return;
        Events.Add(new CalendarEvent { Date = _calDay.Date, Time = time, Title = title });
        Hub.Store.SaveData();
        CalAdd.Text = "";
        UpdateCalendar();
        CalView.Build();
    }

    // ----- reminders -----

    /// <summary>Runs on the minute: shows a card for an event about to start (or just started).</summary>
    private void CheckReminders(DateTime now)
    {
        if (!S.EventReminders || _expanded || !IsVisible) return;   // shown later (up to 5 minutes) if the island is busy or hidden
        // Anything starting in the next quarter hour or the last few minutes — across midnight too.
        foreach (var ev in Events.Where(e => e.Start is { } s && s > now.AddMinutes(-6) && s < now.AddMinutes(15)).OrderBy(e => e.Start))
        {
            var due = ReminderDue(ev, now);
            if (due is null || _reminded.Contains(ev.Id + due)) continue;
            _reminded.Add(ev.Id + due);
            if (due == "now") _reminded.Add(ev.Id + "soon");
            ShowEventCard(ev, now);
            return;   // one at a time; the next waits for the next minute
        }
    }

    private void ShowEventCard(CalendarEvent ev, DateTime now)
    {
        var start = ev.Start!.Value;
        var at = Fmt.Time(start, Hub.Settings.General.Use24Hour);
        var minutes = (int)Math.Ceiling((start - now).TotalMinutes - 0.05);
        var sub = minutes >= 2 ? $"In {minutes} minutes · {at}" : now < start ? $"In a minute · {at}" : "Now · " + at;
        ShowCard(Glyph.Calendar, ev.Title, sub, EventRed, minutes >= 2 ? 5 : 7);
    }
}
