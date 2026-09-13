using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using TinyShell.Interop;

namespace TinyShell.Shell;

public sealed record TaskEntry(IntPtr Hwnd, string Title, ImageSource? Icon, bool IsMinimized);

/// <summary>
/// Tracks top-level, taskbar-worthy windows via SetWinEventHook instead of
/// polling. Fires WindowsChanged whenever the visible set or a title
/// changes - the taskbar updates the same frame a window opens/closes
/// instead of up to 1.2s later.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private readonly List<IntPtr> _hooks = new();
    private readonly Native.WinEventDelegate _callback;
    private readonly string? _selfExePath = Environment.ProcessPath;

    public event Action? WindowsChanged;
    public event Action<IntPtr>? ForegroundChanged;

    public WindowWatcher()
    {
        // Stored as a field: SetWinEventHook only keeps an unmanaged pointer
        // to the delegate. If it's a local, the GC can collect it and the
        // hook silently stops firing with no error.
        _callback = OnWinEvent;

        Hook(Native.EVENT_OBJECT_CREATE);
        Hook(Native.EVENT_OBJECT_DESTROY);
        Hook(Native.EVENT_OBJECT_SHOW);
        Hook(Native.EVENT_OBJECT_HIDE);
        Hook(Native.EVENT_OBJECT_NAMECHANGE);
        Hook(Native.EVENT_SYSTEM_MINIMIZESTART);
        Hook(Native.EVENT_SYSTEM_MINIMIZEEND);
        Hook(Native.EVENT_SYSTEM_FOREGROUND);
    }

    private void Hook(uint eventId)
    {
        var h = Native.SetWinEventHook(eventId, eventId, IntPtr.Zero, _callback, 0, 0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero) _hooks.Add(h);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // OBJID_WINDOW only - filters out the constant stream of events
        // from child controls, scrollbars, carets, etc.
        if (idObject != 0) return;

        if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            ForegroundChanged?.Invoke(hwnd);

        WindowsChanged?.Invoke();
    }

    public List<TaskEntry> GetCurrent()
    {
        var list = new List<TaskEntry>();

        Native.EnumWindows((hwnd, _) =>
        {
            if (!IsTaskWindow(hwnd)) return true;

            int len = Native.GetWindowTextLengthW(hwnd);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            Native.GetWindowTextW(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            string exe;
            try { exe = Process.GetProcessById((int)pid).MainModule?.FileName ?? ""; }
            catch { exe = ""; }

            if (!string.IsNullOrEmpty(_selfExePath) &&
                exe.Equals(_selfExePath, StringComparison.OrdinalIgnoreCase)) return true;

            list.Add(new TaskEntry(hwnd, title, ExtractIcon(exe), Native.IsIconic(hwnd)));
            return true;
        }, IntPtr.Zero);

        return list;
    }

    private static bool IsTaskWindow(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd)) return false;

        int ex = Native.GetWindowLongW(hwnd, Native.GWL_EXSTYLE);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;

        try
        {
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0) return false;
        }
        catch { /* older builds */ }

        IntPtr owner = GetWindow(hwnd, GW_OWNER);
        if (owner != IntPtr.Zero && (ex & Native.WS_EX_APPWINDOW) == 0) return false;

        return true;
    }

    private const int GW_OWNER = 4;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    private static ImageSource? ExtractIcon(string exe) =>
        string.IsNullOrEmpty(exe) ? null : StartMenuScanner.ExtractIcon(exe, 20);

    public static void Activate(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd))
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
        else if (Native.GetForegroundWindow() == hwnd)
            Native.ShowWindow(hwnd, Native.SW_MINIMIZE);

        Native.SetForegroundWindow(hwnd);
    }

    public void Dispose()
    {
        foreach (var h in _hooks) Native.UnhookWinEvent(h);
        _hooks.Clear();
    }
}
