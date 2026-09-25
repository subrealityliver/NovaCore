using Microsoft.Win32;
using System.Diagnostics;

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

}
