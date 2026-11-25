using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;

namespace WinOrbit.Services;

/// <summary>
/// Update checks, always through the network gate (so they need the network switch and show in the ledger). Each check
/// reads the one-line VERSION file of the public repository; releases are published from that file, so it names the
/// latest release. Nothing is sent but the request itself, and nothing is downloaded or installed: when there is a newer
/// version, WinOrbit says so and the download link opens in the user's own browser.
/// <list type="bullet">
/// <item>"Check now" in Settings asks once, when clicked.</item>
/// <item>The automatic check (off by default, asked once) asks at start-up when 48 hours have passed, then every 48 hours.</item>
/// </list>
/// </summary>
public static class Updates
{
    public const string VersionUrl = "https://raw.githubusercontent.com/Mahadi-Siraj/WinOrbit/main/VERSION";
    public static string ReleasesUrl => AppInfo.Repository + "/releases/latest";
    internal static readonly TimeSpan Every = TimeSpan.FromHours(48);

    public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>A newer version found by a check this session (shown on Home until skipped), or null.</summary>
    public static Version? Available { get; private set; }
    public static event Action? AvailableChanged;

    /// <summary>The latest published version, or null if the answer isn't a version number.</summary>
    public static async Task<Version?> LatestAsync(NetworkGate net, string purpose = "Updates: check now") => Parse(await net.GetStringAsync(VersionUrl, purpose));

    /// <summary>"1.3.0", "v1.3", "1.3.0\n" → a version; anything else → null.</summary>
    internal static Version? Parse(string text)
    {
        var t = text.Trim().TrimStart('v', 'V');
        if (t.Length == 0 || t.Length > 20 || !Version.TryParse(t, out var v)) return null;
        return v;
    }

    /// <summary>Whether <paramref name="latest"/> is newer, comparing major.minor.build only (1.2.0 equals 1.2.0.0).</summary>
    internal static bool IsNewer(Version latest, Version current) => Norm(latest) > Norm(current);

    internal static Version Norm(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>What the button reports.</summary>
    internal static string Message(Version? latest, Version current) =>
        latest is null ? "Couldn't read the latest version."
        : IsNewer(latest, current) ? $"WinOrbit {Norm(latest).ToString(3)} is available (you have {Norm(current).ToString(3)})."
        : $"You have the latest version ({Norm(current).ToString(3)}).";

    /// <summary>
    /// The file itself, so nobody has to find their way round GitHub: the installer, or the portable zip for a portable
    /// copy. Release files are named after the version (see tools/build-release.ps1 and the release workflow).
    /// </summary>
    internal static string DownloadUrl(Version v, bool portable)
    {
        var n = Norm(v).ToString(3);
        return $"{AppInfo.Repository}/releases/download/v{n}/" + (portable ? $"WinOrbit-Portable-{n}-win-x64.zip" : $"WinOrbit-Setup-{n}.exe");
    }

    // ----- the automatic check -----

    /// <summary>Whether a check is due: never checked, 48 hours gone, or the clock was set back past the last check.</summary>
    internal static bool Due(DateTime? last, DateTime now) => last is not { } l || now - l >= Every || l > now;

    /// <summary>How long to wait before the next look: a short pause after start-up when due, else until 48 hours are up.</summary>
    internal static TimeSpan Wait(DateTime? last, DateTime now) =>
        Due(last, now) ? TimeSpan.FromSeconds(40) : last!.Value + Every - now + TimeSpan.FromSeconds(5);

    /// <summary>Whether a found version deserves a card: newer than this one and not the version the user skipped.</summary>
    internal static bool ShouldTell(Version? latest, Version current, string skipped) =>
        latest is not null && IsNewer(latest, current) && (Parse(skipped) is not { } s || Norm(latest) > Norm(s));

    private static DispatcherTimer? _timer;
    private static PrivacySettings P => Hub.Settings.Privacy;

    /// <summary>Start, restart or stop the automatic check to match the settings. Costs nothing while it's off.</summary>
    public static void Apply()
    {
        _timer?.Stop();
        if (!P.UpdateCheck || !P.NetworkAllowed) return;
        _timer ??= new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        // A DispatcherTimer can't wait longer than about 24 days; 48 hours is well inside.
        _timer.Interval = Wait(P.UpdateLastCheck, DateTime.Now);
        _timer.Start();
    }

    private static async void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (!P.UpdateCheck || !P.NetworkAllowed) return;
        if (!Due(P.UpdateLastCheck, DateTime.Now)) { Apply(); return; }
        if (Hub.Foreground.IsFullscreen) { _timer!.Interval = TimeSpan.FromMinutes(10); _timer.Start(); return; }   // not during a game
        P.UpdateLastCheck = DateTime.Now;
        Hub.Store.Save();
        try
        {
            var latest = await LatestAsync(Hub.Net, "Updates: automatic check (every 48 hours)");
            if (ShouldTell(latest, Current, P.UpdateSkipped)) Tell(latest!);
        }
        catch (Exception ex) { Log.Info("Update check failed: " + ex.Message); }
        Apply();
    }

    /// <summary>Note the new version for Home and show a card on the island; clicking it opens Home.</summary>
    internal static void Tell(Version latest)
    {
        Available = Norm(latest);
        AvailableChanged?.Invoke();
        Hub.Island.Card(Glyph.Download, $"WinOrbit {Available.ToString(3)} is out", "Click to see what's new and get it", Color.FromRgb(0x5B, 0x9B, 0xFF), 8,
            () => App.Current.ShowMain("home"));
    }

    /// <summary>"Skip this version": no more cards until a newer one.</summary>
    public static void Skip()
    {
        if (Available is null) return;
        P.UpdateSkipped = Available.ToString(3);
        Hub.Store.Save();
        Available = null;
        AvailableChanged?.Invoke();
    }

    /// <summary>The answer to "tell me about updates?". Yes also turns on the network switch, which the check needs.</summary>
    public static void SetAutomatic(bool on)
    {
        P.UpdateAsked = true;
        P.UpdateCheck = on;
        if (on) P.NetworkAllowed = true;
        Hub.Store.Save();
        Apply();
    }
}
