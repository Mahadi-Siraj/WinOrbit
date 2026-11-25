using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinOrbit.Core;
using WinOrbit.Shell.Pages;

namespace WinOrbit.Shell;

public sealed record SettingHit(string PageId, string PageName, string Label, string Description, double Score);

/// <summary>
/// Settings search: builds each page once, off screen, and collects every titled section and row (they carry a
/// <see cref="Kit.SearchTag"/>), so the index always matches what the pages really contain. Selecting a hit
/// opens the page, scrolls to the row and briefly highlights it.
/// </summary>
public static class SettingsSearch
{
    private static List<(string PageId, string PageName, string Label, string Description)>? _index;

    public static void Invalidate() => _index = null;

    private static List<(string, string, string, string)> Index(IEnumerable<(string Id, string Name, Func<PageBase> Create)> pages)
    {
        if (_index is not null) return _index;
        var list = new List<(string, string, string, string)>();
        foreach (var (id, name, create) in pages)
        {
            if (id == "home") continue;
            try
            {
                var page = create();
                page.Enter();
                foreach (var tag in Walk(page).Select(e => e.Tag).OfType<Kit.SearchTag>())
                    if (!string.IsNullOrWhiteSpace(tag.Label)) list.Add((id, name, tag.Label, tag.Description));
                page.Leave();
            }
            catch (Exception ex) { Log.Warn($"Settings index: page {id}: {ex.Message}"); }
        }
        return _index = list.Distinct().ToList();
    }

    internal static IEnumerable<FrameworkElement> Walk(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is FrameworkElement fe) yield return fe;
            foreach (var d in Walk(child)) yield return d;
        }
    }

    public static List<SettingHit> Find(string query, IEnumerable<(string Id, string Name, Func<PageBase> Create)> pages, int max = 8)
    {
        var q = query.Trim();
        if (q.Length < 2) return new();
        return Index(pages)
            .Select(e =>
            {
                var s = Launcher.LauncherIndex.Match(q, e.Item3, e.Item4 + " " + e.Item2);
                if (s <= 0 && e.Item4.Contains(q, StringComparison.OrdinalIgnoreCase)) s = 150;   // words in the description
                return new SettingHit(e.Item1, e.Item2, e.Item3, e.Item4, s);
            })
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score).ThenBy(h => h.Label.Length)
            .Take(max).ToList();
    }

    /// <summary>Scroll the row with this label into view and flash it.</summary>
    public static void Reveal(FrameworkElement page, string label)
    {
        var target = Walk(page).FirstOrDefault(e => e.Tag is Kit.SearchTag t && t.Label == label);
        if (target is null) return;
        // With simple settings, an advanced row is hidden: show that one (and the section it sits in) on its own.
        for (DependencyObject? d = target; d is not null && d != page; d = LogicalTreeHelper.GetParent(d))
            if (d is UIElement u && Kit.GetAdvanced(u) && u.Visibility != Visibility.Visible) u.Visibility = Visibility.Visible;
        target.UpdateLayout();
        target.BringIntoView(new Rect(0, -40, target.ActualWidth, target.ActualHeight + 80));
        var color = (page.TryFindResource("T.AccentSoft") as SolidColorBrush)?.Color ?? Color.FromArgb(0x40, 0x5B, 0x9D, 0xFF);
        var brush = new SolidColorBrush(color);
        switch (target)
        {
            case System.Windows.Controls.Panel p: p.Background = brush; break;
            case System.Windows.Controls.Border b: b.BorderBrush = new SolidColorBrush(((page.TryFindResource("T.Accent") as SolidColorBrush)?.Color) ?? color); b.BorderThickness = new Thickness(2); return;
        }
        brush.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.8)) { BeginTime = TimeSpan.FromSeconds(0.6) });
    }
}
