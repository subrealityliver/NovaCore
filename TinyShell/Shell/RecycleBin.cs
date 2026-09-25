using System.Runtime.InteropServices;

namespace TinyShell.Shell;

/// <summary>
/// Wraps SHFileOperation's delete verb with FOF_ALLOWUNDO so deletes from
/// the desktop or file manager land in the Recycle Bin instead of being
/// permanent - what any "fully functional" desktop needs before Delete
/// is safe to bind to a keyboard shortcut.
/// </summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    /// <summary>pFrom must be double-null-terminated per the SHFileOperation contract.</summary>
    public static bool Send(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT)
        };
        int result = SHFileOperation(ref op);
        return result == 0 && !op.fAnyOperationsAborted;
    }

    public static void Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, 0);
}
