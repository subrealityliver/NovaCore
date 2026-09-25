using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Windows.Interop;

namespace TinyShell.Interop;

/// <summary>
/// Shows the *real* Windows shell context menu for a file/folder, or for
/// the desktop background, instead of a hand-rolled WPF menu. This is what
/// gives us Cut/Copy/Paste, "Send to", "Create shortcut", real Delete
/// (actual Recycle Bin, with the user's configured confirmation dialog),
/// "Properties", any installed shell extensions, etc. - all for free,
/// because it asks the shell to build the menu instead of us guessing at
/// what belongs in it.
///
/// <paramref name="path"/> == null means "desktop background" menu
/// (New, Sort by, Refresh, Personalize, Display settings, ...).
///
/// <paramref name="extraItems"/> lets a caller append TinyShell-specific
/// commands (e.g. "Change wallpaper...") to the bottom of the *same*
/// native popup, so the user gets one right-click menu instead of two.
/// </summary>
public static class ShellContextMenu
{
    // ---- COM interop ----
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name,
            out uint eaten, out IntPtr pidl, ref uint attrs);
        void EnumObjects(IntPtr hwnd, uint flags, out IntPtr penum);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint attrs);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, IntPtr reserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint flags, out IntPtr name);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name,
            uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint iMenu, uint idCmdFirst,
            uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        void GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved,
            [MarshalAs(UnmanagedType.LPStr)] string name, uint cchMax);
    }


    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);

        // IShellView
        [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
        void EnableModeless([MarshalAs(UnmanagedType.Bool)] bool fEnable);
        void UIActivate(uint uState);
        void Refresh();
        void CreateViewWindow(IShellView? psvPrevious, IntPtr pfs, out IntPtr psv, IntPtr prcView);
        void DestroyViewWindow();
        void GetCurrentInfo(IntPtr pfs);
        void AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lParam);
        void SaveViewState();
        void SelectItem(IntPtr pidlItem, uint uFlags);
        void GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint iMenu, uint idCmdFirst,
            uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        void GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved,
            [MarshalAs(UnmanagedType.LPStr)] string name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("000214F5-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint iMenu, uint idCmdFirst,
            uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        void GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved,
            [MarshalAs(UnmanagedType.LPStr)] string name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    // ---- Win32 ----
    [DllImport("shell32.dll")] private static extern int SHGetDesktopFolder(out IShellFolder folder);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl,
        uint attrs, out uint eat);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr hMenu,
        uint flags, int x, int y, IntPtr hwnd, IntPtr tp);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellView = new("000214E3-0000-0000-C000-000000000046");
    private const uint SVGIO_BACKGROUND = 0x00000000;

    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXTENDEDVERBS = 0x00000100;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    // Below this, an id is a shell verb offset (idCmdFirst..idCmdLast).
    // At/above this, it's one of our own extraItems.
    private const uint CustomIdBase = 0x8000;

    // Well-known parsing name for the Recycle Bin virtual folder - it has
    // no real filesystem path, so SHParseDisplayName needs the CLSID form.
    public const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    /// <summary>
    /// Show the shell context menu for a path (file/folder/virtual item),
    /// or for the desktop background when <paramref name="path"/> is null.
    /// </summary>
    public static void Show(IntPtr ownerHwnd, string? path, int x, int y,
        IReadOnlyList<(string Header, Action Action)>? extraItems = null)
    {
        var hMenu = CreatePopupMenu();
        IContextMenu? ctxMenu = null;
        try
        {
            if (SHGetDesktopFolder(out var desktop) != 0) return;

            if (string.IsNullOrEmpty(path))
            {
                // There are two different Shell implementations in the wild:
                // some Windows builds expose the desktop background menu through
                // IShellView::GetItemObject(SVGIO_BACKGROUND), while others expose
                // it directly from the desktop IShellFolder. Explorer normally
                // hides this distinction from callers. TinyShell cannot assume
                // either path because Explorer may have been replaced/killed, so
                // try BOTH mechanisms before falling back to our functional
                // managed desktop menu.
                try
                {
                    var viewIid = IID_IShellView;
                    desktop.CreateViewObject(ownerHwnd, ref viewIid, out var viewPtr);
                    if (viewPtr != IntPtr.Zero)
                    {
                        try
                        {
                            var view = (IShellView)Marshal.GetObjectForIUnknown(viewPtr);
                            var menuIid = IID_IContextMenu;
                            view.GetItemObject(SVGIO_BACKGROUND, ref menuIid, out var menuPtr);
                            if (menuPtr != IntPtr.Zero)
                            {
                                try { ctxMenu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr); }
                                finally { Marshal.Release(menuPtr); }
                            }
                        }
                        finally { Marshal.Release(viewPtr); }
                    }
                }
                catch (Exception) { /* try direct folder context below */ }

                if (ctxMenu == null)
                {
                    try
                    {
                        var menuIid = IID_IContextMenu;
                        // cidl == 0 is explicitly a folder-background request on
                        // shell folder implementations that support it.
                        desktop.GetUIObjectOf(ownerHwnd, 0, Array.Empty<IntPtr>(),
                            ref menuIid, IntPtr.Zero, out var menuPtr);
                        if (menuPtr != IntPtr.Zero)
                        {
                            try { ctxMenu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr); }
                            finally { Marshal.Release(menuPtr); }
                        }
                    }
                    catch (Exception) { /* use managed fallback */ }
                }
            }
            else
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0) return;
                try
                {
                    var arr = new[] { pidl };
                    var iid = IID_IContextMenu;
                    desktop.GetUIObjectOf(ownerHwnd, 1, arr, ref iid, IntPtr.Zero, out var ptr);
                    if (ptr == IntPtr.Zero) return;
                    ctxMenu = (IContextMenu)Marshal.GetObjectForIUnknown(ptr);
                    Marshal.Release(ptr);
                }
                finally { ILFree(pidl); }
            }

            if (ctxMenu == null)
            {
                if (string.IsNullOrEmpty(path))
                    ShowManagedDesktopMenu(ownerHwnd, x, y, extraItems);
                return;
            }

            // Real shell verbs get ids 1..0x7FFF; ours start at CustomIdBase,
            // safely above that range.
            int hr;
            try
            {
                hr = ctxMenu.QueryContextMenu(hMenu, 0, 1, CustomIdBase - 1, CMF_NORMAL | CMF_EXTENDEDVERBS);
            }
            catch (Exception)
            {
                if (string.IsNullOrEmpty(path))
                    ShowManagedDesktopMenu(ownerHwnd, x, y, extraItems);
                return;
            }
            if (hr < 0)
            {
                if (string.IsNullOrEmpty(path))
                    ShowManagedDesktopMenu(ownerHwnd, x, y, extraItems);
                return;
            }

            if (extraItems is { Count: > 0 })
            {
                AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
                for (int i = 0; i < extraItems.Count; i++)
                    AppendMenu(hMenu, MF_STRING, (UIntPtr)(CustomIdBase + (uint)i), extraItems[i].Header);
            }

            var activeMenu2 = ctxMenu as IContextMenu2;
            var hookedSource = HwndSource.FromHwnd(ownerHwnd);
            IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (activeMenu2 == null) return IntPtr.Zero;

                const int WM_INITMENUPOPUP = 0x0117;
                const int WM_DRAWITEM = 0x002B;
                const int WM_MEASUREITEM = 0x002C;
                const int WM_MENUCHAR = 0x0120;
                const int WM_MENUSELECT = 0x011F;

                if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR or WM_MENUSELECT)
                {
                    if (activeMenu2 is IContextMenu3 cm3) cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out _);
                    else activeMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
                    handled = true;
                }
                return IntPtr.Zero;
            }
            hookedSource?.AddHook(WndProc);

            SetForegroundWindow(ownerHwnd);
            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, ownerHwnd, IntPtr.Zero);

            hookedSource?.RemoveHook(WndProc);

            if (cmd == 0) return;

            if (cmd >= CustomIdBase)
            {
                int idx = (int)(cmd - CustomIdBase);
                if (extraItems is not null && idx < extraItems.Count)
                    extraItems[idx].Action();
                return;
            }

            var info = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                hwnd = ownerHwnd,
                lpVerb = (IntPtr)(cmd - 1),
                nShow = 1
            };
            ctxMenu.InvokeCommand(ref info);
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }
    // Explorer normally supplies this menu. When Explorer is not running,
    // however, some Windows builds do not expose the background IContextMenu
    // through either COM route above. Never leave the user with a dead right
    // click: this fallback implements the core desktop operations directly.
    private static void ShowManagedDesktopMenu(IntPtr ownerHwnd, int x, int y,
        IReadOnlyList<(string Header, Action Action)>? extraItems)
    {
        const uint MF_POPUP = 0x00000010;
        const uint TPM_RETURNCMD = 0x0100;
        const uint TPM_RIGHTBUTTON = 0x0002;
        const uint MF_SEPARATOR = 0x00000800;

        var menu = CreatePopupMenu();
        var newMenu = CreatePopupMenu();
        const uint CMD_REFRESH = 0x9001;
        const uint CMD_PASTE = 0x9002;
        const uint CMD_DISPLAY = 0x9003;
        const uint CMD_PERSONALIZE = 0x9004;
        const uint CMD_TERMINAL = 0x9005;
        const uint CMD_FILEMANAGER = 0x9006;
        const uint CMD_WALLPAPER = 0x9007;
        const uint CMD_ARRANGE = 0x9008;
        const uint CMD_NEW_FOLDER = 0x9101;
        const uint CMD_NEW_TEXT = 0x9102;

        try
        {
            AppendMenu(newMenu, MF_STRING, (UIntPtr)CMD_NEW_FOLDER, "Folder");
            AppendMenu(newMenu, MF_STRING, (UIntPtr)CMD_NEW_TEXT, "Text Document");
            AppendMenu(menu, MF_POPUP, (UIntPtr)newMenu, "New");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_REFRESH, "Refresh");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_ARRANGE, "Arrange icons by name");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_PASTE, "Paste");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_FILEMANAGER, "Open File Manager");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_TERMINAL, "Open Terminal here");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_WALLPAPER, "Change wallpaper...");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_DISPLAY, "Display settings");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_PERSONALIZE, "Personalize");

            if (extraItems is { Count: > 0 })
            {
                AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                for (int i = 0; i < extraItems.Count; i++)
                    AppendMenu(menu, MF_STRING, (UIntPtr)(CustomIdBase + (uint)i), extraItems[i].Header);
            }

            SetForegroundWindow(ownerHwnd);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, ownerHwnd, IntPtr.Zero);
            if (cmd == 0) return;

            switch (cmd)
            {
                case CMD_REFRESH:
                    SendKeysToOwner(ownerHwnd, "{F5}");
                    break;
                case CMD_PASTE:
                    SendKeysToOwner(ownerHwnd, "^v");
                    break;
                case CMD_DISPLAY:
                    Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
                    break;
                case CMD_PERSONALIZE:
                    Process.Start(new ProcessStartInfo("ms-settings:personalization") { UseShellExecute = true });
                    break;
                case CMD_TERMINAL:
                    Process.Start(new ProcessStartInfo("wt.exe") {
                        UseShellExecute = true,
                        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    });
                    break;
                case CMD_FILEMANAGER:
                    Process.Start(new ProcessStartInfo("explorer.exe",
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)) { UseShellExecute = true });
                    break;
                case CMD_WALLPAPER:
                    extraItems?.FirstOrDefault(x => x.Header.Equals("Change wallpaper...", StringComparison.Ordinal)).Action?.Invoke();
                    break;
                case CMD_ARRANGE:
                    extraItems?.FirstOrDefault(x => x.Header.Equals("Arrange icons by name", StringComparison.Ordinal)).Action?.Invoke();
                    break;
                case CMD_NEW_FOLDER:
                    CreateDesktopFolder();
                    break;
                case CMD_NEW_TEXT:
                    CreateDesktopTextFile();
                    break;
                default:
                    if (cmd >= CustomIdBase)
                    {
                        var idx = (int)(cmd - CustomIdBase);
                        if (extraItems is not null && idx < extraItems.Count) extraItems[idx].Action();
                    }
                    break;
            }
        }
        finally
        {
            DestroyMenu(newMenu);
            DestroyMenu(menu);
        }
    }

    private static void SendKeysToOwner(IntPtr hwnd, string keys)
    {
        try
        {
            SetForegroundWindow(hwnd);
            System.Windows.Forms.SendKeys.SendWait(keys);
        }
        catch { }
    }

    private static void CreateDesktopFolder()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var baseName = "New folder";
        var path = System.IO.Path.Combine(dir, baseName);
        for (int i = 2; Directory.Exists(path); i++)
            path = System.IO.Path.Combine(dir, $"{baseName} ({i})");
        try { Directory.CreateDirectory(path); } catch { }
    }

    private static void CreateDesktopTextFile()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var baseName = "New Text Document";
        var path = System.IO.Path.Combine(dir, baseName + ".txt");
        for (int i = 2; File.Exists(path); i++)
            path = System.IO.Path.Combine(dir, $"{baseName} ({i}).txt");
        try { File.WriteAllText(path, string.Empty); } catch { }
    }

}
