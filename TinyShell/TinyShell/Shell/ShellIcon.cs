using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyShell.Shell;

public static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>
    /// Works for both files and folders, unlike Icon.ExtractAssociatedIcon
    /// which only handles files with an associated executable/icon.
    /// </summary>
    public static ImageSource? GetIcon(string path, bool isDirectory, int size)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | (size <= 20 ? SHGFI_SMALLICON : SHGFI_LARGEICON);

        var result = SHGetFileInfo(path,
            isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL,
            ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { DestroyIcon(info.hIcon); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    /// <summary>
    /// For virtual shell items with no real filesystem path (Recycle Bin,
    /// This PC, etc.) SHGetFileInfo can't resolve an icon from a plain
    /// string. ExtractIconEx pulls a named icon straight out of a system
    /// DLL's icon resource table instead - index 31 in shell32.dll is the
    /// classic Recycle Bin icon.
    /// </summary>
    public static ImageSource? GetIconByIndex(string dllPath, int index, int size)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        if (ExtractIconEx(dllPath, index, large, small, 1) == 0) return null;

        var handle = size <= 20 && small[0] != IntPtr.Zero ? small[0] : large[0];
        if (handle == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
        }
    }
}
