using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinOrbit.Launcher;

/// <summary>App icons from the Windows shell (what Start shows), fetched lazily for visible rows and cached.</summary>
public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, ref BITMAP bmp);
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    /// <summary>Icon for an AppsFolder id, or null. Call on the UI thread.</summary>
    public static ImageSource? Get(string appId, int size = 32) => Shell("shell:AppsFolder\\" + appId, size, 0x5);   // SIIGBF_ICONONLY | BIGGERSIZEOK

    /// <summary>The icon of a program file (an .exe), as Explorer shows it, or null. Call on the UI thread.</summary>
    public static ImageSource? ForFile(string path, int size = 32) => Shell(path, size, 0x5);

    /// <summary>
    /// A file's real thumbnail — for music files, the embedded album cover Explorer shows — or null when the
    /// file has none (no generic icon is returned).
    /// </summary>
    public static ImageSource? Thumbnail(string path, int size = 256) => Shell(path, size, 0x8 | 0x1);              // SIIGBF_THUMBNAILONLY | BIGGERSIZEOK

    private static ImageSource? Shell(string parsingName, int size, int flags)
    {
        var cacheKey = parsingName + "|" + size + "|" + flags;
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, flags, out var hbmp) == 0 && hbmp != IntPtr.Zero)
            {
                try
                {
                    var bmp = new BITMAP();
                    GetObject(hbmp, Marshal.SizeOf<BITMAP>(), ref bmp);
                    if (bmp.bmBits != IntPtr.Zero && bmp.bmBitsPixel == 32)
                    {
                        // Copy the DIB with its alpha (CreateBitmapSourceFromHBitmap would drop transparency).
                        var src = BitmapSource.Create(bmp.bmWidth, bmp.bmHeight, 96, 96, PixelFormats.Pbgra32, null, bmp.bmBits, bmp.bmWidthBytes * bmp.bmHeight, bmp.bmWidthBytes);
                        // Shell bitmaps are bottom-up DIBs: flip vertically.
                        var flipped = new TransformedBitmap(src, new ScaleTransform(1, -1));
                        var final = new WriteableBitmap(flipped);
                        final.Freeze();
                        result = final;
                    }
                }
                finally { DeleteObject(hbmp); }
            }
            Marshal.ReleaseComObject(factory);
        }
        catch { result = null; }
        if (Cache.Count > 400) Cache.Clear();
        Cache[cacheKey] = result;
        return result;
    }
}
