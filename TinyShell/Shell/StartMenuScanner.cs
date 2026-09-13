using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace TinyShell.Shell;

public sealed record ShellApp(string Name, string Target, string? Arguments, ImageSource? Icon);

public static class StartMenuScanner
{
    public static List<ShellApp> Scan()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ShellApp>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var lnk in files)
            {
                if (lnk.Contains(@"\Startup\", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (!seen.Add(name)) continue;

                var (target, args) = ResolveShortcut(lnk);
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target)) continue;

                result.Add(new ShellApp(name, target, args, ExtractIcon(target, 24)));
            }
        }

        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static (string? target, string? args) ResolveShortcut(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return (null, null);
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(lnkPath);
            string target = link.TargetPath;
            string args = link.Arguments;
            return (target, string.IsNullOrWhiteSpace(args) ? null : args);
        }
        catch { return (null, null); }
    }

    public static ImageSource? ExtractIcon(string path, int size)
    {
        try
        {
            using var ico = Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico is null) return null;
            using var bmp = ico.ToBitmap();
            var hbm = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hbm, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(size, size));
            }
            finally { Interop.Native.DeleteObject(hbm); }
        }
        catch { return null; }
    }
}
