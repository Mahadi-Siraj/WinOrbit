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

    public static string? InstagramUrl => Instagram.Length > 0 ? "https://www.instagram.com/" + Instagram.TrimStart('@') + "/" : null;
    public static string? GitHubUrl => GitHub.Length > 0 ? "https://github.com/" + GitHub : null;
}
