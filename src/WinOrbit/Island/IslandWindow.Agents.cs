using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Design;
using WinOrbit.Services;
using WinOrbit.Shell.Pages;

namespace WinOrbit.Island;

/// <summary>
/// The Agents tab (opt-in): what each AI coding session on this PC is doing — needs you, working or done — as reported
/// by the tools' own hooks through the local pipe. Plus a card when a session needs you and a short alert when one
/// finishes.
/// </summary>
public partial class IslandWindow
{
    private const string GlyphAgents = "", GlyphBell = "", GlyphWorking = "", GlyphDone = "";
    private static readonly Color AgentWaiting = Color.FromRgb(0xFF, 0xA9, 0x3A), AgentBusy = Color.FromRgb(0x5E, 0x9E, 0xFF), AgentOk = Color.FromRgb(0x34, 0xC7, 0x59);
    private DispatcherTimer? _agentClock;

    private static (string Glyph, Color Color, string Label) AgentLook(AgentState s) => s switch
    {
        AgentState.NeedsYou => (GlyphBell, AgentWaiting, "Needs you"),
        AgentState.Working => (GlyphWorking, AgentBusy, "Working"),
        _ => (GlyphDone, AgentOk, "Done"),
    };

    public void OnAgentsChanged()
    {
        if (_expanded && _tab == "agents") RenderAgents();
    }

    public void AgentNeedsYou(AgentSession s)
    {
        if (!S.CodingAgents) return;
        if (_expanded) { if (_tab == "agents") RenderAgents(); return; }
        var line = s.Text.Length > 0 ? s.Text : "Waiting for your answer";
        ShowCard(GlyphBell, $"{s.AgentName} needs you", s.Folder.Length > 0 ? $"{line} · {s.Folder}" : line, AgentWaiting, 6,
            onClick: () => { Expand(false); ShowTab("agents"); });
    }

    public void AgentFinished(AgentSession s)
    {
        if (!S.CodingAgents || _expanded) return;
        Peek(GlyphDone, s.Folder.Length > 0 ? $"{s.AgentName} finished · {s.Folder}" : $"{s.AgentName} finished", 3, AgentOk);
    }

    private void RenderAgents()
    {
        TabAgents.Children.Clear();
        var board = Hub.Island.Agents;
        board.Prune();
        var root = Ui.Grid("*", "auto,*");

        // Header: counts on the left, Clear on the right.
        var head = new Grid { Margin = new Thickness(2, 0, 0, 8) };
        var counts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var state in new[] { AgentState.NeedsYou, AgentState.Working, AgentState.Done })
        {
            var n = board.Sessions.Count(x => x.State == state);
            if (n == 0) continue;
            var (_, color, label) = AgentLook(state);
            counts.Children.Add(new Border
            {
                Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(color),
                Margin = new Thickness(counts.Children.Count == 0 ? 0 : 12, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            counts.Children.Add(Ui.Text($"{n} {(state == AgentState.NeedsYou ? "need" + (n == 1 ? "s" : "") + " you" : label.ToLowerInvariant())}", "Text.Caption", 12).With(t => t.VerticalAlignment = VerticalAlignment.Center));
        }
        if (counts.Children.Count == 0) counts.Children.Add(Ui.Text("Coding agents", "Text.Caption", 12));
        head.Children.Add(counts);
        if (board.Sessions.Any(x => x.State == AgentState.Done))
            head.Children.Add(new Button { Content = "Clear", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 4, 12, 4), FontSize = 12, ToolTip = "Remove finished sessions" }
                .With(b => b.Click += (_, _) => board.ClearFinished()));
        root.Children.Add(head);

        var list = new StackPanel();
        if (board.Sessions.Count == 0)
        {
            list.Margin = new Thickness(4, 6, 4, 0);
            list.Children.Add(Ui.Text("No coding sessions yet", "Text.Sub", 13));
            list.Children.Add(Ui.Text("When an AI coding tool on this PC (Claude Code, Codex or your own) is connected, each session shows here: when it needs your OK, while it works, and when it's done. Connect a tool in Settings › Dynamic Island › Coding agents.", "Text.Caption", 11.5)
                .With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 6, 0, 12); }));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Kit.Btn("Set up", () => { Collapse(); App.Current.ShowMain("island"); }, Glyph.Settings, true));
            buttons.Children.Add(Kit.Btn("Turn off", () => { S.CodingAgents = false; Hub.Store.Save(); Hub.Island.Apply(); }).With(b => b.Margin = new Thickness(8, 0, 0, 0)));
            list.Children.Add(buttons);
        }
        foreach (var s in board.Sessions) list.Children.Add(AgentRow(s));
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list }.At(1, 0));
        TabAgents.Children.Add(root);

        // "4 min" keeps counting while the tab is open, and only then.
        _agentClock ??= new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) =>
        {
            if (_expanded && _tab == "agents") RenderAgents(); else _agentClock!.Stop();
        }, Dispatcher);
        if (board.Sessions.Count > 0) _agentClock.Start(); else _agentClock.Stop();
    }

    private FrameworkElement AgentRow(AgentSession s)
    {
        var (glyph, color, label) = AgentLook(s.State);
        var ink = new SolidColorBrush(color);
        var row = new Border { Margin = new Thickness(0, 0, 4, 6), Padding = new Thickness(8, 7, 6, 7), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        row.SetResourceReference(Border.BackgroundProperty, "T.Input");
        row.BorderBrush = s.State == AgentState.NeedsYou ? new SolidColorBrush(Color.FromArgb(0x90, color.R, color.G, color.B)) : Brushes.Transparent;

        var g = Ui.Grid("auto,*,auto,auto", "auto,auto");
        var tile = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
            Child = Ui.Icon(glyph, 15, "T.Text").With(t => { t.Foreground = ink; t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }),
        };
        Grid.SetRowSpan(tile, 2);
        g.Children.Add(tile);
        g.Children.Add(Ui.Text(s.Heading, "Text.Body", 13).With(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; }).At(0, 1));
        var status = s.State == AgentState.Working && s.Text.Length > 0 ? s.Text : s.State == AgentState.NeedsYou && s.Text.Length > 0 ? s.Text : label;
        g.Children.Add(Ui.Text(status, "Text.Caption", 11.5).With(t => { t.Foreground = ink; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; t.Margin = new Thickness(0, 2, 6, 0); }).At(1, 1));
        g.Children.Add(Ui.Text(AgentBoard.Ago(DateTime.Now - s.Since), "Text.Caption", 11).With(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(6, 0, 4, 0); }).At(0, 2));
        var where = s.Folder.Length > 0 ? $"{s.AgentName} · {s.Folder}" : s.AgentName;
        g.Children.Add(Ui.Text(where, "Text.Caption", 11).With(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap; t.MaxWidth = 170; t.Margin = new Thickness(6, 2, 4, 0); }).At(1, 2));
        var remove = Ui.IconButton(Glyph.Cancel, "Remove from the list", () => Hub.Island.Agents.Remove(s), 24).At(0, 3);
        Grid.SetRowSpan(remove, 2);
        g.Children.Add(remove);
        row.Child = g;
        row.ToolTip = $"{s.AgentName} · {label}{(s.Folder.Length > 0 ? " · " + s.Folder : "")}";
        return row;
    }

    /// <summary>Preview (Island settings › Preview): the card a session shows when it needs you. Leaves the real list alone.</summary>
    private void DemoAgents() =>
        ShowCard(GlyphBell, "Claude Code needs you", "Claude needs your permission to use Bash · storefront", AgentWaiting, 5);
}
