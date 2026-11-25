namespace WinOrbit.Core;

/// <summary>
/// Who made WinOrbit. Shown on About & Diagnostics as links that open in the user's own browser
/// (WinOrbit itself makes no request). Empty values are simply not shown.
/// </summary>
public static class AppInfo
{
    public const string Author = "mssatalite";
    public const string Instagram = "mssatalite";
    public const string GitHub = "Mahadi-Siraj";
    public const string GitHubName = "Mahadi Siraj";
    // GitHub keeps redirecting this address if the repository is renamed later.
    public const string Repository = "https://github.com/Mahadi-Siraj/WinOrbit";

    /// <summary>For bug reports and ideas from people who don't use GitHub. The owner chose to publish it.</summary>
    public const string Email = "mahadisiraj.06@gmail.com";

    public static string? InstagramUrl => Instagram.Length > 0 ? "https://www.instagram.com/" + Instagram.TrimStart('@') + "/" : null;
    /// <summary>Opens a direct message to the Instagram account (in the app on a phone, in the browser on a PC).</summary>
    public static string? InstagramMessageUrl => Instagram.Length > 0 ? "https://ig.me/m/" + Instagram.TrimStart('@') : null;
    public static string? GitHubUrl => GitHub.Length > 0 ? "https://github.com/" + GitHub : null;
    public static string IssuesUrl => Repository + "/issues/new/choose";

    /// <summary>A <c>mailto:</c> link with the subject and body filled in (CR LF line breaks, as the standard asks).</summary>
    internal static string MailTo(string to, string subject, string body) =>
        $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body.Replace("\r\n", "\n").Replace("\n", "\r\n"))}";
}
