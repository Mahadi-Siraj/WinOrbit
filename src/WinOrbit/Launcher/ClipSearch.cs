using System.IO;
using WinOrbit.Services;

namespace WinOrbit.Launcher;

/// <summary>
/// "clip" or "clip invoice" in the launcher: the clipboard history (opt-in, memory-only), newest first, filtered by
/// the words after "clip". Enter puts the item back on the clipboard. Results only appear after the "clip" prefix,
/// so copied text never shows up in ordinary searches, and nothing about the item is written to the usage counts.
/// </summary>
public static class ClipSearch
{
    /// <summary>The words to look for ("" for everything), or null when the query isn't a clipboard search.</summary>
    internal static string? Term(string query)
    {
        var q = query.TrimStart();
        foreach (var prefix in new[] { "clipboard", "clip" })
        {
            if (!q.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = q[prefix.Length..];
            if (rest.Length > 0 && !char.IsWhiteSpace(rest[0])) return null;   // "clipchamp" is an app
            return rest.Trim();
        }
        return null;
    }

    /// <summary>Items matching every word of <paramref name="term"/>, in the history's order (newest first).</summary>
    internal static List<ClipItem> Find(IEnumerable<ClipItem> items, string term, int max = 6)
    {
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return items.Where(i => words.All(w => Searchable(i).Contains(w, StringComparison.CurrentCultureIgnoreCase))).Take(max).ToList();
    }

    private static string Searchable(ClipItem i) => i.Kind switch
    {
        ClipKind.Files => string.Join(" ", i.Files.Select(Path.GetFileName)),
        ClipKind.Image => "image picture screenshot",
        _ => i.Text
    };

    /// <summary>One line for the result: the first non-empty line, shortened.</summary>
    internal static string Line(ClipItem i)
    {
        var text = i.Kind is ClipKind.Text or ClipKind.Link
            ? i.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? ""
            : i.Preview;
        return text.Length > 90 ? text[..89] + "…" : text;
    }

    internal static string Ago(DateTime at, DateTime now)
    {
        var d = now - at;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min ago";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours} h ago";
        return at.ToString("d MMM");
    }

    internal static IEnumerable<LauncherItem> Items(string query)
    {
        if (Term(query) is not { } term) yield break;
        if (!Hub.Settings.Island.ClipboardHistory)
        {
            yield return new LauncherItem { Kind = ItemKind.Action, Key = "clip-off", Title = "Clipboard history is off", Subtitle = "Turn it on in Dynamic Island › Clipboard history (kept in memory only)", Glyph = Design.Glyph.Copy, Score = 10_000,
                Run = () => App.Current.ShowMain("island") };
            yield break;
        }
        var now = DateTime.Now;
        var found = Find(Hub.Clipboard.Items, term);
        if (found.Count == 0)
        {
            yield return new LauncherItem { Kind = ItemKind.Action, Key = "clip-none", Title = term.Length == 0 ? "Nothing copied yet" : $"Nothing copied matches “{term}”", Subtitle = "Clipboard history", Glyph = Design.Glyph.Copy, Score = 10_000 };
            yield break;
        }
        var rank = 0;
        foreach (var item in found)
        {
            var it = item;
            var kind = it.Kind switch { ClipKind.Link => "Link", ClipKind.Image => "Image", ClipKind.Files => "Files", _ => "Text" };
            yield return new LauncherItem
            {
                Kind = ItemKind.Calc, Key = "clip", Title = Line(it), Glyph = Design.Glyph.Copy, Score = 10_000 - rank++,
                Subtitle = $"{kind} · {(it.Pinned ? "pinned" : Ago(it.At, now))} · Enter copies it again",
                Run = () => Hub.Clipboard.Copy(it)
            };
        }
    }
}
