using System.Diagnostics;

namespace TinyShell;

public static class PowerActions
{
    public static void Shutdown() =>
        RunShutdownExe("/s /t 0");

    public static void Restart() =>
        RunShutdownExe("/r /t 0");

    public static void SignOut() =>
        RunShutdownExe("/l");

    /// <summary>
    /// Sleep via powrprof.dll's SetSuspendState through rundll32, rather
    /// than P/Invoking SetSuspendState directly - rundll32 already runs
    /// elevated-agnostic and handles the call correctly without us having
    /// to marshal the (hibernate, forceCritical, disableWakeEvent) bools.
    /// </summary>
    public static void Sleep()
    {
        Process.Start(new ProcessStartInfo("rundll32.exe")
        {
            Arguments = "powrprof.dll,SetSuspendState 0,1,0",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void RunShutdownExe(string args)
    {
        Process.Start(new ProcessStartInfo("shutdown.exe")
        {
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
