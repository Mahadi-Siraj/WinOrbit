using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinOrbit.Core;

namespace WinOrbit.Services;

public enum ClipKind { Text, Link, Image, Files }

public sealed class ClipItem
{
    public ClipKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string[] Files { get; init; } = Array.Empty<string>();
    public byte[]? Png { get; init; }              // full image, compressed
    public BitmapSource? Thumb { get; init; }      // small preview
    public DateTime At { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }

    public string Preview => Kind switch
    {
        ClipKind.Files => Files.Length == 1 ? Path.GetFileName(Files[0]) : $"{Files.Length} files",
        ClipKind.Image => "Image",
        _ => Text.Length > 300 ? Text[..300] : Text
    };
}

/// <summary>
/// Opt-in clipboard history for the island. Windows tells us when the clipboard changes (no polling).
/// History is kept in memory only and disappears when WinOrbit quits; only items the user pins are saved
/// (text only). Content that apps mark as private — password managers set ExcludeClipboardContentFromMonitorProcessing
/// or CanIncludeInClipboardHistory = 0, the same flags Windows' own history honours — is never recorded.
/// </summary>
public sealed class ClipboardHistory
{
    private const int MaxItems = 40, MaxImages = 6, WmClipboardUpdate = 0x031D;

    private readonly Store _store;
    private readonly Func<IslandSettings> _settings;
    private readonly List<ClipItem> _items = new();
    private HwndSource? _window;
    private bool _selfWrite;
    private static readonly uint ExcludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HistoryFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint IgnoreFormat = RegisterClipboardFormat("Clipboard Viewer Ignore");

    public event Action? Changed;
    public IReadOnlyList<ClipItem> Items => _items;
    public bool Listening => _window is not null;

    public ClipboardHistory(Store store, Func<IslandSettings> settings)
    {
        _store = store;
        _settings = settings;
        foreach (var pin in store.Data.ClipPins) _items.Add(new ClipItem { Kind = IsLink(pin) ? ClipKind.Link : ClipKind.Text, Text = pin, Pinned = true });
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);

    /// <summary>Start or stop listening to match the setting.</summary>
    public void Apply()
    {
        var want = _settings().ClipboardHistory;
        if (want && _window is null)
        {
            _window = new HwndSource(new HwndSourceParameters("WinOrbit clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });   // message-only window
            _window.AddHook(Hook);
            AddClipboardFormatListener(_window.Handle);
            Log.Info("Clipboard history on");
        }
        else if (!want && _window is not null)
        {
            RemoveClipboardFormatListener(_window.Handle);
            _window.Dispose();
            _window = null;
            _items.RemoveAll(i => !i.Pinned);   // turning it off forgets everything that wasn't pinned
            Changed?.Invoke();
            Log.Info("Clipboard history off; unpinned items cleared");
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            if (_selfWrite) { _selfWrite = false; return IntPtr.Zero; }
            // Read slightly later: the copying app may still hold the clipboard open.
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, Capture);
        }
        return IntPtr.Zero;
    }

    private bool IsPrivate()
    {
        if (IsClipboardFormatAvailable(ExcludeFormat) || IsClipboardFormatAvailable(IgnoreFormat)) return true;
        if (IsClipboardFormatAvailable(HistoryFormat))
        {
            try
            {
                if (Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream ms && ms.Length >= 4)
                {
                    var b = ms.ToArray();
                    if (BitConverter.ToInt32(b, 0) == 0) return true;
                }
            }
            catch { return true; }   // can't tell → treat as private
        }
        return false;
    }

    private void Capture()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (IsPrivate()) return;
                ClipItem? item = null;
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                    if (files.Length > 0) item = new ClipItem { Kind = ClipKind.Files, Files = files };
                }
                else if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img is not null) item = FromImage(img);
                }
                else if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 100_000)
                        item = new ClipItem { Kind = IsLink(text) ? ClipKind.Link : ClipKind.Text, Text = text };
                }
                if (item is not null) Add(item);
                return;
            }
            catch (COMException) { Thread.Sleep(60); }   // clipboard busy
            catch (Exception ex) { Log.Warn("Clipboard read: " + ex.Message); return; }
        }
    }

    private static ClipItem FromImage(BitmapSource img)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var scale = Math.Min(1.0, 220.0 / Math.Max(1, img.PixelWidth));
        BitmapSource thumb = scale < 1 ? new TransformedBitmap(img, new ScaleTransform(scale, scale)) : img;
        thumb = new WriteableBitmap(thumb);
        thumb.Freeze();
        return new ClipItem { Kind = ClipKind.Image, Png = ms.ToArray(), Thumb = thumb };
    }

    internal static bool IsLink(string text)
    {
        var t = text.Trim();
        return !t.Contains(' ') && !t.Contains('\n') && Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
    }

    private void Add(ClipItem item)
    {
        // The same text again moves to the top instead of duplicating.
        if (item.Kind is ClipKind.Text or ClipKind.Link)
        {
            var existing = _items.FirstOrDefault(i => i.Kind is ClipKind.Text or ClipKind.Link && i.Text == item.Text);
            if (existing is not null) { _items.Remove(existing); existing.At = DateTime.Now; _items.Insert(0, existing); Changed?.Invoke(); return; }
        }
        _items.Insert(0, item);
        var images = _items.Where(i => i.Kind == ClipKind.Image && !i.Pinned).Skip(MaxImages).ToList();
        foreach (var i in images) _items.Remove(i);
        while (_items.Count(i => !i.Pinned) > MaxItems) _items.Remove(_items.Last(i => !i.Pinned));
        Changed?.Invoke();
    }

    /// <summary>Put an item back on the clipboard (it then pastes anywhere with Ctrl+V).</summary>
    public void Copy(ClipItem item)
    {
        try
        {
            _selfWrite = _window is not null;
            switch (item.Kind)
            {
                case ClipKind.Files:
                    var list = new StringCollection(); list.AddRange(item.Files.Where(File.Exists).ToArray());
                    Clipboard.SetFileDropList(list); break;
                case ClipKind.Image when item.Png is not null:
                    using (var ms = new MemoryStream(item.Png))
                    {
                        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        Clipboard.SetImage(frame);
                    }
                    break;
                default: Clipboard.SetText(item.Text); break;
            }
            _items.Remove(item); item.At = DateTime.Now; _items.Insert(0, item);
            Changed?.Invoke();
        }
        catch (Exception ex) { _selfWrite = false; Log.Warn("Clipboard write: " + ex.Message); }
    }

    public void TogglePin(ClipItem item)
    {
        if (item.Kind is not (ClipKind.Text or ClipKind.Link)) return;   // only text is ever written to disk
        item.Pinned = !item.Pinned;
        _store.Data.ClipPins = _items.Where(i => i.Pinned && i.Kind is ClipKind.Text or ClipKind.Link).Select(i => i.Text).ToList();
        _store.SaveData();
        Changed?.Invoke();
    }

    public void Remove(ClipItem item)
    {
        _items.Remove(item);
        if (item.Pinned) { _store.Data.ClipPins.Remove(item.Text); _store.SaveData(); }
        Changed?.Invoke();
    }

    public void ClearUnpinned() { _items.RemoveAll(i => !i.Pinned); Changed?.Invoke(); }
}
