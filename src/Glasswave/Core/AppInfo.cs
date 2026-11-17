namespace Glasswave.Core;

/// <summary>
/// Who made Glasswave. Shown on About & Diagnostics as links that open in the user's own browser
/// (Glasswave itself makes no request). Empty values are simply not shown.
/// </summary>
public static class AppInfo
{
    public const string Author = "";
    public const string Instagram = "";   // handle without @, e.g. "yourname"
    public const string GitHub = "";      // user name, e.g. "yourname"

    public static string? InstagramUrl => Instagram.Length > 0 ? "https://www.instagram.com/" + Instagram.TrimStart('@') : null;
    public static string? GitHubUrl => GitHub.Length > 0 ? "https://github.com/" + GitHub : null;
}
