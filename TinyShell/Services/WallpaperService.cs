using System.IO;
using System.Runtime.InteropServices;

namespace TinyShell.Services;

/// <summary>
/// Sets the actual Windows wallpaper (not just what DesktopWindow paints),
/// so the choice survives if TinyShell is uninstalled as the shell and
/// explorer.exe takes back over, and so it applies correctly across all
/// monitors on modern (per-monitor wallpaper capable) Windows.
/// DesktopWindow still reads back from the registry afterwards to paint
/// its own background - see DesktopWindow.LoadWallpaper.
/// </summary>
public static class WallpaperService
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam,
        string pvParam, uint fWinIni);

    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    // Windows 8+: per-monitor wallpaper, slideshow, etc.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
                          [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT r);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
        int GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    /// <summary>Sets the wallpaper on every monitor and updates HKCU.</summary>
    public static void Set(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Wallpaper not found", path);

        path = Path.GetFullPath(path);
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
            uint n = dw.GetMonitorDevicePathCount();
            if (n == 0)
            {
                dw.SetWallpaper(null, path);
            }
            else
            {
                for (uint i = 0; i < n; i++)
                    dw.SetWallpaper(dw.GetMonitorDevicePathAt(i), path);
            }
        }
        catch
        {
            // Fallback for older systems / if the COM object is unavailable.
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
    }
}
