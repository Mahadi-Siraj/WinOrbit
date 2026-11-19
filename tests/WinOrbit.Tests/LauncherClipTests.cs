using WinOrbit.Launcher;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class LauncherClipTests
{
    [Theory]
    [InlineData("clip", "")]
    [InlineData("clip invoice", "invoice")]
    [InlineData("  Clipboard  my address ", "my address")]
    [InlineData("clipchamp", null)]
    [InlineData("chrome", null)]
    public void Clip_prefix_starts_a_clipboard_search(string query, string? term) => Assert.Equal(term, ClipSearch.Term(query));

    [Fact]
    public void Clipboard_search_matches_every_word_newest_first()
    {
        var items = new[]
        {
            new ClipItem { Kind = ClipKind.Text, Text = "Invoice 2026-114 for Acme" },
            new ClipItem { Kind = ClipKind.Link, Text = "https://example.com/invoice" },
            new ClipItem { Kind = ClipKind.Files, Files = new[] { @"C:\Users\a\Documents\invoice-march.pdf" } },
            new ClipItem { Kind = ClipKind.Image },
            new ClipItem { Kind = ClipKind.Text, Text = "something else" },
        };
        Assert.Equal(3, ClipSearch.Find(items, "invoice").Count);
        Assert.Single(ClipSearch.Find(items, "INVOICE acme"));
        Assert.Single(ClipSearch.Find(items, "screenshot"));          // images answer to "image" / "screenshot"
        Assert.Equal(5, ClipSearch.Find(items, "").Count);
        Assert.Equal(2, ClipSearch.Find(items, "", max: 2).Count);
        Assert.Empty(ClipSearch.Find(items, "nothing like this"));
    }

    [Fact]
    public void Results_show_one_tidy_line()
    {
        Assert.Equal("Dear Sam,", ClipSearch.Line(new ClipItem { Kind = ClipKind.Text, Text = "\n\n  Dear Sam,\nThanks for…" }));
        Assert.Equal(90, ClipSearch.Line(new ClipItem { Kind = ClipKind.Text, Text = new string('a', 200) }).Length);
        Assert.Equal("report.pdf", ClipSearch.Line(new ClipItem { Kind = ClipKind.Files, Files = new[] { @"C:\x\report.pdf" } }));
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        Assert.Equal("just now", ClipSearch.Ago(now.AddSeconds(-20), now));
        Assert.Equal("5 min ago", ClipSearch.Ago(now.AddMinutes(-5), now));
        Assert.Equal("3 h ago", ClipSearch.Ago(now.AddHours(-3), now));
    }
}
