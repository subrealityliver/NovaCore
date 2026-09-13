using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TinyShell;

public static class ShellManager
{
    private const string WinlogonKey = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TinyShell";

    public static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;

    public static void KillExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { /* ignore */ }
        }
    }

    public static void RestoreExplorer()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    public static void InstallAsShell()
    {
        using var key = Registry.CurrentUser.CreateSubKey(WinlogonKey, true);
        key.SetValue("Shell", $"\"{ExePath}\" --shell", RegistryValueKind.String);
    }

    public static void UninstallShell()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WinlogonKey, true);
        if (key?.GetValue("Shell") is string s && s.Contains("TinyShell"))
            key.DeleteValue("Shell", false);
    }

    public static void SetRunAtLogon(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(AppName, $"\"{ExePath}\" --shell", RegistryValueKind.String);
        else key.DeleteValue(AppName, false);
    }

    public static bool IsInstalledAsShell()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WinlogonKey);
        return key?.GetValue("Shell") is string s && s.Contains("TinyShell");
    }

    // ------------------------------------------------------------------
    // Minimize animation
    //
    // Without a real Explorer taskbar, Windows still animates a minimize
    // as a "fly toward a target rect" - but since no taskbar button is
    // registered to give it a target, it defaults to flying toward a
    // corner of the screen, which looks broken. There's no API to tell
    // Windows "target my taskbar button" without implementing the full
    // ITaskbarList COM interface, so the pragmatic fix used here (and by
    // most minimal shells) is to just turn the animation off entirely:
    // minimized windows disappear instantly instead of flying anywhere.
    // Restored on exit so a normal Explorer session isn't left changed.
    // ------------------------------------------------------------------

    private static bool? _previousMinimizeAnimation;

    public static void DisableMinimizeAnimation()
    {
        var info = new Interop.Native.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<Interop.Native.ANIMATIONINFO>() };
        if (Interop.Native.SystemParametersInfo(Interop.Native.SPI_GETANIMATION, info.cbSize, ref info, 0))
            _previousMinimizeAnimation = info.iMinAnimate != 0;

        info.iMinAnimate = 0;
        Interop.Native.SystemParametersInfo(Interop.Native.SPI_SETANIMATION, info.cbSize, ref info, 0);
    }

    public static void RestoreMinimizeAnimation()
    {
        if (_previousMinimizeAnimation is not bool prev) return;
        var info = new Interop.Native.ANIMATIONINFO
        {
            cbSize = (uint)Marshal.SizeOf<Interop.Native.ANIMATIONINFO>(),
            iMinAnimate = prev ? 1 : 0
        };
        Interop.Native.SystemParametersInfo(Interop.Native.SPI_SETANIMATION, info.cbSize, ref info, 0);
    }
}
