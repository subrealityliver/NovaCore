using System.Threading;
using System.Windows;
using TinyShell.Debloat;
using TinyShell.Interop;
using TinyShell.Shell;
using Application = System.Windows.Application;
using Screen = System.Windows.Forms.Screen;

namespace TinyShell;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private static readonly List<ShellWindow> Bars = new();
    private static DesktopWindow? _desktop;
    private static Native.ANIMATIONINFO? _originalAnimation;

    public static WindowWatcher? Watcher { get; private set; }
    public static BarSettings SharedSettings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, @"Global\TinyShell.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("TinyShell is already running.", "TinyShell");
            Shutdown();
            return;
        }

        var args = e.Args;

        if (args.Contains("--debloat", StringComparer.OrdinalIgnoreCase))
        {
            new DebloatWindow().Show();
            return;
        }

        if (args.Contains("--restore-explorer", StringComparer.OrdinalIgnoreCase))
        {
            ShellManager.UninstallShell();
            ShellManager.RestoreExplorer();
            Shutdown();
            return;
        }

        SharedSettings = BarSettings.Load();

        // Explorer normally supplies a taskbar button as the minimize target.
        // TinyShell replaces Explorer, so Windows can animate minimized windows
        // toward the lower-left corner instead. Disable only the system's
        // minimize animation while TinyShell is running and restore the user's
        // original preference on exit.
        try
        {
            var ai = new Native.ANIMATIONINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.ANIMATIONINFO>() };
            if (Native.SystemParametersInfo(Native.SPI_GETANIMATION, ai.cbSize, ref ai, 0))
            {
                _originalAnimation = ai;
                ai.iMinAnimate = 0;
                Native.SystemParametersInfo(Native.SPI_SETANIMATION, ai.cbSize, ref ai, 0);
            }
        }
        catch { }

        bool shellMode = args.Contains("--shell", StringComparer.OrdinalIgnoreCase);

        if (shellMode)
        {
            ShellManager.KillExplorer();
        }

        Watcher = new WindowWatcher();

        // One bar per monitor - the primary screen's bar owns the Start
        // button, launcher, hotkeys and system menu. Secondary bars show
        // a clock and only the windows actually on that monitor.
        foreach (var screen in Screen.AllScreens)
        {
            var bar = new ShellWindow(screen, screen.Primary, false);
            Bars.Add(bar);
            bar.Show();
        }

        if (shellMode && SharedSettings.DesktopEnabled)
        {
            _desktop = new DesktopWindow();
            _desktop.Show();
        }
    }

    /// <summary>Applies a new edge to every bar across every monitor and persists it.</summary>
    public static void ApplyEdgeToAll(BarEdge edge)
    {
        SharedSettings.Edge = edge;
        SharedSettings.Save();
        foreach (var bar in Bars) bar.SetEdge(edge);
    }

    public static void ApplyThicknessToAll(int thickness)
    {
        SharedSettings.Thickness = thickness;
        SharedSettings.Save();
        foreach (var bar in Bars) bar.ApplyExternalThickness(thickness);
    }

    /// <summary>Called after a drag-resize on one bar finishes, so every
    /// other monitor's bar matches the new thickness too.</summary>
    public static void BroadcastThickness(int thickness, ShellWindow exclude)
    {
        foreach (var bar in Bars.Where(b => b != exclude))
            bar.ApplyExternalThickness(thickness);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var b in Bars) b.Close();
        _desktop?.Close();
        Watcher?.Dispose();
        if (_originalAnimation is { } animation)
        {
            try
            {
                var ai = animation;
                Native.SystemParametersInfo(Native.SPI_SETANIMATION, ai.cbSize, ref ai, 0);
            }
            catch { }
        }
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
