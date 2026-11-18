using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinOrbit.Core;
using WinOrbit.Design;

namespace WinOrbit.Island;

/// <summary>
/// Cards about files: a screenshot that was just saved (with its thumbnail) and a download that just finished.
/// Click the card to open the file, drag it straight into another app, or use the folder button to see it in
/// File Explorer.
/// </summary>
public partial class IslandWindow
{
    private static readonly Color Shot = Color.FromRgb(0x64, 0xD2, 0xFF);
    private static (string Path, ImageSource Image)? _thumb;

    private void InitFiles() => Hub.Island.Screenshots.Saved += OnScreenshot;
    private void CleanupFiles() => Hub.Island.Screenshots.Saved -= OnScreenshot;

    private void OnScreenshot(string path)
    {
        if (!S.ScreenshotCards) return;
        ShowScreenshotCard(path, Thumbnail(path));
    }

    private void ShowScreenshotCard(string path, ImageSource? thumb, string title = "Screenshot saved")
    {
        ShowCard(Glyph.Photo, title, "Click to open · drag to share", Shot, 6, art: thumb,
            onClick: () => OpenFile(path), file: path,
            actions: new (string, string, Action)[] { (Glyph.Folder, "Show in folder", () => ShowInFolder(path)) });
    }

    private void ShowDownloadCard(string path)
    {
        var name = Path.GetFileName(path);
        var open = SafeToOpen(path);
        // Programs and scripts are never run from a click on the island: the card shows them in their folder instead.
        ShowCard(Glyph.Download, name, open ? "Downloaded · click to open" : "Downloaded · click to show in folder", Connected, 6,
            onClick: open ? () => OpenFile(path) : () => ShowInFolder(path), file: path,
            actions: new (string, string, Action)[] { (Glyph.Folder, "Show in folder", () => ShowInFolder(path)) });
    }

    /// <summary>A small, decoded-once thumbnail; the same one serves every display's island.</summary>
    private static ImageSource? Thumbnail(string path)
    {
        if (_thumb is { } t && string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) return t.Image;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;   // read now, so the file isn't held open
            img.DecodePixelHeight = 96;                    // 44 px tile at up to 2× scaling
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            _thumb = (path, img);
            return img;
        }
        catch (Exception ex) { Log.Warn("Screenshot thumbnail: " + ex.Message); return null; }
    }

    /// <summary>
    /// Whether a click may open this file directly. Anything that runs code (programs, installers, scripts,
    /// shortcuts) is shown in its folder instead, so a stray click on the island can never start a program.
    /// </summary>
    internal static bool SafeToOpen(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        return !Runnable.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] Runnable =
    {
        ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller", ".bat", ".cmd", ".com", ".scr", ".pif", ".cpl",
        ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".url", ".reg", ".jar", ".msc", ".application",
        ".gadget", ".inf", ".scf", ".dll", ".sys", ".iso", ".img", ".vhd", ".vhdx",
    };

    private static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("Open file: " + ex.Message); }
    }

    private static void ShowInFolder(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex) { Log.Warn("Show in folder: " + ex.Message); }
    }

    /// <summary>Preview: the newest picture in the Screenshots folder, or a sample card when there is none.</summary>
    private void DemoScreenshot()
    {
        var latest = ScreenshotLatest();
        if (latest is not null) ShowScreenshotCard(latest, Thumbnail(latest), "Latest screenshot");
        else ShowCard(Glyph.Photo, "Screenshot saved", "Click to open · drag to share", Shot, 4);
    }

    private static string? ScreenshotLatest()
    {
        try
        {
            if (Services.ScreenshotWatcher.Folder() is not { } dir) return null;
            return new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => Services.ScreenshotWatcher.IsScreenshot(f.FullName))
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }
}
