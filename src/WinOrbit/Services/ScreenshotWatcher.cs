using System.IO;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>
/// Notices new screenshots (Win+PrtScn, the Snipping Tool's auto-save, Game Bar captures saved there) by watching
/// the Screenshots folder for new image files. Windows reports each new file; nothing is checked on a timer.
/// Reads only the new file's name and, for the card's thumbnail, the image itself — locally.
/// </summary>
public sealed class ScreenshotWatcher : IDisposable
{
    /// <summary>FOLDERID_Screenshots: Pictures\Screenshots, or wherever the user moved it (OneDrive…).</summary>
    private static readonly Guid ScreenshotsFolder = new("b7bede81-df94-4682-a7d8-57a52620b86f");
    private static readonly string[] Images = { ".png", ".jpg", ".jpeg" };

    private readonly Dispatcher _ui;
    private FileSystemWatcher? _watcher;
    private string _last = "";
    private DateTime _lastAt;

    /// <summary>A finished screenshot, with its full path. Raised on the UI thread.</summary>
    public event Action<string>? Saved;

    public ScreenshotWatcher(Dispatcher ui) => _ui = ui;

    public static string? Folder()
    {
        var known = Native.KnownFolder(ScreenshotsFolder);
        if (!string.IsNullOrEmpty(known) && Directory.Exists(known)) return known;
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public void SetEnabled(bool on)
    {
        if (on == (_watcher is not null)) return;
        if (!on) { _watcher?.Dispose(); _watcher = null; return; }
        try
        {
            if (Folder() is not { } dir) return;   // no Screenshots folder yet: Windows creates it with the first one
            _watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false, EnableRaisingEvents = true };
            _watcher.Created += (_, e) => Arrived(e.FullPath);
            _watcher.Renamed += (_, e) => Arrived(e.FullPath);   // some tools write a temp name, then rename
        }
        catch (Exception ex) { Log.Warn("Screenshot watcher: " + ex.Message); }
    }

    /// <summary>Only finished pictures count: not temp files, not hidden thumbnails.</summary>
    internal static bool IsScreenshot(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name) || name.StartsWith('.') || name.StartsWith('~')) return false;
        return Images.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    private void Arrived(string path)
    {
        if (!IsScreenshot(path)) return;
        // The capture tool is still writing: wait until the file opens for reading (up to about 3 s), off the UI thread.
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 15; i++)
            {
                await Task.Delay(200);
                try
                {
                    using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (f.Length > 0) break;
                }
                catch (FileNotFoundException) { return; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { return; }
            }
            _ui.BeginInvoke(() =>
            {
                // Created and Renamed can both fire for one capture.
                if (string.Equals(path, _last, StringComparison.OrdinalIgnoreCase) && (DateTime.UtcNow - _lastAt).TotalSeconds < 5) return;
                _last = path; _lastAt = DateTime.UtcNow;
                if (File.Exists(path)) Saved?.Invoke(path);
            });
        });
    }

    public void Dispose() { _watcher?.Dispose(); _watcher = null; }
}
