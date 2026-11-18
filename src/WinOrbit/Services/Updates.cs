using WinOrbit.Core;

namespace WinOrbit.Services;

/// <summary>
/// "Check now" for a newer version — only when the user clicks the button, never by itself, and only through the
/// network gate (so it needs the network switch and shows in the ledger). It reads the one-line VERSION file of the
/// public repository; releases are published from that file, so it names the latest release. Nothing is sent but
/// the request itself, and nothing is downloaded or installed.
/// </summary>
public static class Updates
{
    public const string VersionUrl = "https://raw.githubusercontent.com/Mahadi-Siraj/WinOrbit/main/VERSION";
    public static string ReleasesUrl => AppInfo.Repository + "/releases/latest";

    public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>The latest published version, or null if the answer isn't a version number.</summary>
    public static async Task<Version?> LatestAsync(NetworkGate net) => Parse(await net.GetStringAsync(VersionUrl, "Updates: check now"));

    /// <summary>"1.3.0", "v1.3", "1.3.0\n" → a version; anything else → null.</summary>
    internal static Version? Parse(string text)
    {
        var t = text.Trim().TrimStart('v', 'V');
        if (t.Length == 0 || t.Length > 20 || !Version.TryParse(t, out var v)) return null;
        return v;
    }

    /// <summary>Whether <paramref name="latest"/> is newer, comparing major.minor.build only (1.2.0 equals 1.2.0.0).</summary>
    internal static bool IsNewer(Version latest, Version current) => Norm(latest) > Norm(current);

    private static Version Norm(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>What the button reports.</summary>
    internal static string Message(Version? latest, Version current) =>
        latest is null ? "Couldn't read the latest version."
        : IsNewer(latest, current) ? $"WinOrbit {Norm(latest).ToString(3)} is available (you have {Norm(current).ToString(3)})."
        : $"You have the latest version ({Norm(current).ToString(3)}).";
}
