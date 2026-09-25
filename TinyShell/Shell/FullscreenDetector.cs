using TinyShell.Interop;
using ScreenType = System.Windows.Forms.Screen;

namespace TinyShell.Shell;

/// <summary>
/// Windows only tells a shell about *exclusive* fullscreen (old-style
/// DirectX games) via SHAppBarMessage's ABN_FULLSCREENAPP. Modern
/// "fullscreen borderless" games/video players/emulators don't trigger
/// that at all - they're just a normal window sized to the whole monitor
/// with no title bar. There's no official API for this case, so every
/// shell/taskbar-hider (including ExplorerPatcher, Windhawk plugins, etc.)
/// uses the same heuristic this does: a window counts as "fullscreen" if
/// its client rect exactly matches a monitor's bounds AND it has no
/// caption/thickframe chrome. It's a heuristic, not a guarantee - a
/// borderless window with unusual styles could be missed or false-positive.
/// </summary>
public static class FullscreenDetector
{
    public static bool IsBorderlessFullscreen(IntPtr hwnd, ScreenType screen)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (!Native.GetWindowRect(hwnd, out var rect)) return false;

        var b = screen.Bounds;
        // A couple of pixels of tolerance: DWM's invisible resize border/
        // shadow can make GetWindowRect report a rect that's a few pixels
        // off from an exact monitor match even for a genuinely fullscreen
        // borderless window, which would otherwise make this always miss.
        const int tolerance = 2;
        bool coversMonitor = rect.Left <= b.Left + tolerance && rect.Top <= b.Top + tolerance &&
                              rect.Right >= b.Right - tolerance && rect.Bottom >= b.Bottom - tolerance;
        if (!coversMonitor) return false;

        long style = Native.GetWindowLongW(hwnd, Native.GWL_STYLE);
        bool hasChrome = (style & Native.WS_CAPTION) != 0 || (style & Native.WS_THICKFRAME) != 0;
        return !hasChrome;
    }
}
