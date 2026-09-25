using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using TinyShell.FileManager;
using TinyShell.Interop;
using TinyShell.Services;

namespace TinyShell.Shell;

public sealed class DesktopIcon
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsSpecial { get; init; }
    public ImageSource? Icon { get; init; }
    public double IconSize { get; set; } = 40;
    public double TileSize { get; set; } = 88;
    public double X { get; set; }
    public double Y { get; set; }
}

public partial class DesktopWindow : Window
{
    private const string RecycleBinKey = "::RecycleBin";
    private const double MarginLeft = 12, MarginTop = 12;
    private const double DragThreshold = 4;

    private IntPtr _hwnd;
    private readonly DesktopLayout _layout = DesktopLayout.Load();
    private readonly List<FileSystemWatcher> _watchers = new();

    private DesktopIcon? _selectedIcon;
    private Border? _selectedBorder;

    private FrameworkElement? _dragElement;
    private Point _dragStartMouse;
    private Point _dragStartPos;
    private bool _isDragging;

    public DesktopWindow()
    {
        InitializeComponent();
        // The desktop menu is opened explicitly from the mouse handler. This
        // avoids relying on WPF's automatic ContextMenu service for an HWND
        // deliberately kept at the bottom of the Z-order.
        Loaded += OnLoaded;
        Closed += (_, _) => { foreach (var w in _watchers) w.Dispose(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The desktop must stop at TinyShell's appbar work area. Extending a
        // black/transparent WPF window underneath the bottom bar produces the
        // visible black strip that appears when another window reaches the
        // bottom edge.
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        var edge = App.SharedSettings.Edge;
        var thickness = App.SharedSettings.Thickness;
        Left = vs.Left; Top = vs.Top; Width = vs.Width; Height = vs.Height;
        switch (edge)
        {
            case BarEdge.Bottom: Height = Math.Max(1, vs.Height - thickness); break;
            case BarEdge.Top: Top += thickness; Height = Math.Max(1, vs.Height - thickness); break;
            case BarEdge.Left: Left += thickness; Width = Math.Max(1, vs.Width - thickness); break;
            case BarEdge.Right: Width = Math.Max(1, vs.Width - thickness); break;
        }

        LoadWallpaper();
        LoadIcons();
        SetupWatchers();

        // Push to the very bottom of the z-order. Unlike the earlier
        // WS_EX_NOACTIVATE approach, this window CAN be focused (clicking
        // it activates it normally) - that's required for F2/Delete/Enter
        // to work like a real desktop. Activation would normally also
        // raise a window to the top, so every time that happens we
        // immediately reassert HWND_BOTTOM to keep the visual illusion
        // that the desktop always sits behind every other window.
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
            Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE);
        Activated += (_, _) => Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
            Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE);
    }

    private void LoadWallpaper()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var path = key?.GetValue("WallPaper") as string;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                // Bypass WPF's per-Uri image cache: reloading the *same*
                // path after the user changes wallpaper to something else
                // and back would otherwise show a stale cached bitmap.
                SetWallpaperImage(path);
                return;
            }
        }
        catch { /* fall through to no image - the window's own Background shows through */ }

        // IMPORTANT: never reassign RootGrid.Background here. WallpaperBrush
        // is the ImageBrush declared as RootGrid.Background in XAML; setting
        // RootGrid.Background to a *new* brush (e.g. a SolidColorBrush)
        // would permanently detach that named element from the visual tree.
        // Every later "Change wallpaper" call would then set
        // WallpaperBrush.ImageSource on an orphaned object with nothing
        // showing it - the picker would appear to succeed but nothing
        // would ever change on screen. Clearing the image source and
        // letting the Window's own Background (Black, in the XAML) show
        // through gives the same visual fallback without that trap.
        WallpaperBrush.ImageSource = null;
    }

    // ------------------------------------------------------------------
    // Icon list + auto-arrange for anything without a saved position
    // ------------------------------------------------------------------

    public void LoadIcons()
    {
        DeselectIcon();

        var items = new List<DesktopIcon>
        {
            new DesktopIcon
            {
                Name = "Recycle Bin",
                Path = RecycleBinKey,
                IsDirectory = false,
                IsSpecial = true,
                Icon = ShellIcon.GetIconByIndex(
                    System.IO.Path.Combine(Environment.SystemDirectory, "shell32.dll"), 31, 40)
            }
        };

        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var dir in dirs.Distinct())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                    items.Add(new DesktopIcon
                    {
                        Name = System.IO.Path.GetFileName(d),
                        Path = d,
                        IsDirectory = true,
                        Icon = ShellIcon.GetIcon(d, true, 40)
                    });

                foreach (var f in Directory.EnumerateFiles(dir))
                    items.Add(new DesktopIcon
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(f),
                        Path = f,
                        IsDirectory = false,
                        Icon = ShellIcon.GetIcon(f, false, 40)
                    });
            }
            catch { /* permission issues on some folders - skip */ }
        }

        ApplyIconVisuals(items);
        var ordered = SortIcons(items);

        int maxRows = Math.Max(1, (int)((ActualHeight - MarginTop) / GetTileHeight()));
        int col = 0, row = 0;

        foreach (var icon in ordered)
        {
            var key = LayoutKey(icon);
            if (_layout.AutoArrange || !_layout.TryGetPosition(key, out var x, out var y))
            {
                icon.X = MarginLeft + col * GetTileWidth();
                icon.Y = MarginTop + row * GetTileHeight();
                row++;
                if (row >= maxRows) { row = 0; col++; }
                if (_layout.AlignToGrid)
                    SnapIconToGrid(icon);
            }
            else
            {
                icon.X = x; icon.Y = y;
                if (_layout.AlignToGrid) SnapIconToGrid(icon);
            }
        }

        IconGrid.ItemsSource = ordered;
    }

    private static string LayoutKey(DesktopIcon icon) => icon.IsSpecial ? icon.Path : icon.Path;

    private void SetupWatchers()
    {
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var dir in dirs.Distinct())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var w = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                w.Created += (_, _) => Dispatcher.BeginInvoke(LoadIcons);
                w.Deleted += (_, _) => Dispatcher.BeginInvoke(LoadIcons);
                w.Renamed += (_, _) => Dispatcher.BeginInvoke(LoadIcons);
                _watchers.Add(w);
            }
            catch { /* some folders may not support watching - non-fatal */ }
        }
    }

    // ------------------------------------------------------------------
    // Select / open / drag - the actual desktop interaction model
    // ------------------------------------------------------------------

    private void SelectIcon(DesktopIcon icon, Border border)
    {
        if (_selectedBorder is not null) _selectedBorder.Background = Brushes.Transparent;
        _selectedIcon = icon;
        _selectedBorder = border;
        border.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0xF0, 0xFF));
        Keyboard.Focus(this);
    }

    private void DeselectIcon()
    {
        if (_selectedBorder is not null) _selectedBorder.Background = Brushes.Transparent;
        _selectedBorder = null;
        _selectedIcon = null;
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DeselectIcon();
        Keyboard.Focus(this);
    }

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var border = (Border)sender;
        var icon = (DesktopIcon)border.DataContext;

        if (e.ClickCount == 2)
        {
            OpenIcon(icon);
            e.Handled = true;
            return;
        }

        SelectIcon(icon, border);

        _dragElement = border;
        _dragStartMouse = e.GetPosition(RootGrid);
        _dragStartPos = new Point(Canvas.GetLeft(border), Canvas.GetTop(border));
        _isDragging = false;
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragElement is null || e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(RootGrid);
        var delta = current - _dragStartMouse;

        if (!_isDragging && (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold))
            _isDragging = true;

        if (_isDragging)
        {
            Canvas.SetLeft(_dragElement, Math.Max(0, _dragStartPos.X + delta.X));
            Canvas.SetTop(_dragElement, Math.Max(0, _dragStartPos.Y + delta.Y));
        }
    }

    private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragElement is null) return;
        _dragElement.ReleaseMouseCapture();

        if (_isDragging && _dragElement.DataContext is DesktopIcon icon)
        {
            var x = Canvas.GetLeft(_dragElement);
            var y = Canvas.GetTop(_dragElement);
            icon.X = x; icon.Y = y;
            _layout.SetPosition(LayoutKey(icon), x, y);
            _layout.Save();
        }

        _dragElement = null;
        _isDragging = false;
    }

    private void OpenIcon(DesktopIcon icon)
    {
        try
        {
            if (icon.IsSpecial)
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
            else if (icon.IsDirectory)
                new FileManagerWindow(icon.Path).Show();
            else
                Process.Start(new ProcessStartInfo(icon.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open {icon.Name}\n\n{ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------
    // Keyboard: F2 rename, Delete (to Recycle Bin), Enter to open
    // ------------------------------------------------------------------

    private void DesktopWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (_selectedIcon is null || _selectedIcon.IsSpecial) return;

        if (e.Key == Key.Delete) { DeleteIcon(_selectedIcon); e.Handled = true; }
        else if (e.Key == Key.F2) { RenameIcon(_selectedIcon); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenIcon(_selectedIcon); e.Handled = true; }
    }

    private void DeleteIcon(DesktopIcon icon)
    {
        var confirm = MessageBox.Show($"Move \"{icon.Name}\" to the Recycle Bin?", "TinyShell",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        if (RecycleBin.Send(icon.Path)) { DeselectIcon(); LoadIcons(); }
        else MessageBox.Show("Could not delete that item.", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RenameIcon(DesktopIcon icon)
    {
        var input = SimpleInputBox.Show("Rename", "New name:", icon.Name);
        if (string.IsNullOrWhiteSpace(input) || input == icon.Name) return;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(icon.Path)!;
            var ext = icon.IsDirectory ? "" : System.IO.Path.GetExtension(icon.Path);
            var newPath = System.IO.Path.Combine(dir, input + ext);
            if (icon.IsDirectory) Directory.Move(icon.Path, newPath);
            else File.Move(icon.Path, newPath);
            LoadIcons();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Rename failed: {ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------
    // Right-click menus
    // ------------------------------------------------------------------

    private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var border = (Border)sender;
        var icon = (DesktopIcon)border.DataContext;
        SelectIcon(icon, border);
        e.Handled = true;

        var pt = PointToScreen(e.GetPosition(this));

        // Ask the shell itself for the real context menu - Cut/Copy/Paste,
        // Send to, Create shortcut, real Recycle-Bin Delete (honoring the
        // user's own confirmation setting), Properties, and any installed
        // shell extensions, instead of a hand-picked handful of commands.
        var shellPath = icon.IsSpecial ? ShellContextMenu.RecycleBinParsingName : icon.Path;

        var extras = icon.IsSpecial
            ? new (string, Action)[]
            {
                ("Empty Recycle Bin", () =>
                {
                    if (MessageBox.Show("Permanently empty the Recycle Bin?", "TinyShell",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        RecycleBin.Empty();
                })
            }
            : new (string, Action)[]
            {
                ("Copy path", () => Clipboard.SetText(icon.Path))
            };

        ShellContextMenu.Show(_hwnd, shellPath, (int)pt.X, (int)pt.Y, extras);

        // The shell menu can rename/delete/move the item behind our back
        // (real filesystem operations), so refresh afterwards.
        Dispatcher.BeginInvoke(LoadIcons);
    }

    private void RootGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Do not depend on WPF's automatic ContextMenuOpening event. The
        // desktop window is intentionally HWND_BOTTOM, so opening the menu
        // explicitly is much more reliable when Explorer is not running.
        //
        // Only bail out here if the click actually landed on an icon tile -
        // checked by name ("Tile", the x:Name given to the icon Border in
        // the DataTemplate), not just "any Border anywhere in the ancestor
        // chain". Matching on type alone is too broad: it silently eats
        // every right-click on the empty desktop if any unrelated Border
        // ever ends up above the click point in the visual tree (e.g. from
        // a MenuItem/Popup template, an Adorner, or anything else that
        // happens to use a Border), and there is no visible error when
        // that happens - the menu just never opens.
        if (IsDesktopIconTile(e.OriginalSource as DependencyObject))
            return;

        e.Handled = true;
        DeselectIcon();

        var menu = BuildDesktopContextMenu();
        menu.PlacementTarget = RootGrid;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static bool IsDesktopIconTile(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Border { Name: "Tile" })
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private ContextMenu BuildDesktopContextMenu()
    {
        var menu = new ContextMenu();

        var view = new MenuItem { Header = "View" };
        view.Items.Add(CreateCheckItem("Large icons", _layout.ViewMode == DesktopViewMode.Large, () => SetViewMode(DesktopViewMode.Large)));
        view.Items.Add(CreateCheckItem("Medium icons", _layout.ViewMode == DesktopViewMode.Medium, () => SetViewMode(DesktopViewMode.Medium)));
        view.Items.Add(CreateCheckItem("Small icons", _layout.ViewMode == DesktopViewMode.Small, () => SetViewMode(DesktopViewMode.Small)));
        menu.Items.Add(view);

        var sort = new MenuItem { Header = "Sort by" };
        sort.Items.Add(CreateCheckItem("Name", _layout.SortMode == DesktopSortMode.Name, () => SetSortMode(DesktopSortMode.Name)));
        sort.Items.Add(CreateCheckItem("Size", _layout.SortMode == DesktopSortMode.Size, () => SetSortMode(DesktopSortMode.Size)));
        sort.Items.Add(CreateCheckItem("Item type", _layout.SortMode == DesktopSortMode.ItemType, () => SetSortMode(DesktopSortMode.ItemType)));
        sort.Items.Add(CreateCheckItem("Date modified", _layout.SortMode == DesktopSortMode.DateModified, () => SetSortMode(DesktopSortMode.DateModified)));
        menu.Items.Add(sort);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateActionItem("Refresh", LoadIcons));

        var @new = new MenuItem { Header = "New" };
        @new.Items.Add(CreateActionItem("Folder", CreateNewFolder));
        @new.Items.Add(CreateActionItem("Shortcut", CreateNewShortcut));
        @new.Items.Add(CreateActionItem("Text document", CreateNewTextFile));
        menu.Items.Add(@new);

        if (CanPasteFiles())
        {
            menu.Items.Add(CreateActionItem("Paste", PasteFiles));
            menu.Items.Add(CreateActionItem("Paste shortcut", PasteShortcuts));
        }

        menu.Items.Add(new Separator());

        menu.Items.Add(CreateActionItem("Arrange icons", ArrangeIcons));
        menu.Items.Add(CreateCheckItem("Auto arrange", _layout.AutoArrange, ToggleAutoArrange));
        menu.Items.Add(CreateCheckItem("Align to grid", _layout.AlignToGrid, ToggleAlignToGrid));

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateActionItem("Change wallpaper...", ChangeWallpaper));
        menu.Items.Add(CreateActionItem("Display settings", () => OpenSettingsUri("ms-settings:display")));
        menu.Items.Add(CreateActionItem("Personalize", () => OpenSettingsUri("ms-settings:personalization")));
        menu.Items.Add(CreateActionItem("Open desktop folder", OpenDesktopFolder));

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateActionItem("TinyShell options...", OpenTinyShellOptions));

        return menu;
    }

    private static MenuItem CreateActionItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem CreateCheckItem(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetViewMode(DesktopViewMode mode)
    {
        _layout.ViewMode = mode;
        _layout.Save();
        LoadIcons();
    }

    private void SetSortMode(DesktopSortMode mode)
    {
        _layout.SortMode = mode;
        _layout.Save();
        LoadIcons();
    }

    private void ToggleAutoArrange()
    {
        _layout.AutoArrange = !_layout.AutoArrange;
        _layout.Save();
        LoadIcons();
    }

    private void ToggleAlignToGrid()
    {
        _layout.AlignToGrid = !_layout.AlignToGrid;
        _layout.Save();
        LoadIcons();
    }

    private void ArrangeIcons()
    {
        _layout.Positions.Clear();
        _layout.AutoArrange = false;
        _layout.Save();
        LoadIcons();
    }

    private List<DesktopIcon> SortIcons(IEnumerable<DesktopIcon> items)
    {
        var normal = items.Where(i => !i.IsSpecial);
        IEnumerable<DesktopIcon> sorted = _layout.SortMode switch
        {
            DesktopSortMode.Size => normal.OrderBy(GetIconSize).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            DesktopSortMode.ItemType => normal.OrderBy(GetItemType, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            DesktopSortMode.DateModified => normal.OrderBy(GetLastWriteTime).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => normal.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        return new[] { items.First(i => i.IsSpecial) }.Concat(sorted).ToList();
    }

    private static long GetIconSize(DesktopIcon icon)
    {
        try { return icon.IsDirectory ? 0 : new FileInfo(icon.Path).Length; }
        catch { return 0; }
    }

    private static string GetItemType(DesktopIcon icon)
    {
        if (icon.IsDirectory) return "File folder";
        try { return new FileInfo(icon.Path).Extension.TrimStart('.'); }
        catch { return string.Empty; }
    }

    private static DateTime GetLastWriteTime(DesktopIcon icon)
    {
        try { return File.GetLastWriteTime(icon.Path); }
        catch { return DateTime.MinValue; }
    }

    private void ApplyIconVisuals(IEnumerable<DesktopIcon> icons)
    {
        var size = _layout.ViewMode switch
        {
            DesktopViewMode.Large => 48,
            DesktopViewMode.Small => 24,
            _ => 40
        };
        var tile = _layout.ViewMode switch
        {
            DesktopViewMode.Large => 104,
            DesktopViewMode.Small => 72,
            _ => 88
        };

        foreach (var icon in icons)
        {
            icon.IconSize = size;
            icon.TileSize = tile;
        }
    }

    private double GetTileWidth() => _layout.ViewMode == DesktopViewMode.Large ? 104 : _layout.ViewMode == DesktopViewMode.Small ? 72 : 88;
    private double GetTileHeight() => _layout.ViewMode == DesktopViewMode.Large ? 112 : _layout.ViewMode == DesktopViewMode.Small ? 72 : 96;

    private void SnapIconToGrid(DesktopIcon icon)
    {
        const double grid = 8;
        icon.X = Math.Round(icon.X / grid) * grid;
        icon.Y = Math.Round(icon.Y / grid) * grid;
    }

    private void OpenDesktopFolder()
    {
        new FileManagerWindow(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)).Show();
    }

    private void OpenTinyShellOptions()
    {
        // Never depend on explorer.exe for TinyShell's own desktop menu.
        // The FileManagerWindow is part of TinyShell and remains available in
        // shell mode.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyShell");
        try
        {
            Directory.CreateDirectory(folder);
            new FileManagerWindow(folder).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open TinyShell options.\n\n{ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenSettingsUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Could not open Windows Settings.\n\n{ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private bool CanPasteFiles()
    {
        try { return Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count > 0; }
        catch { return false; }
    }

    private List<string> GetClipboardFiles()
    {
        try { return Clipboard.GetFileDropList().Cast<string>().Where(p => File.Exists(p) || Directory.Exists(p)).ToList(); }
        catch { return new List<string>(); }
    }

    private void PasteFiles()
    {
        var files = GetClipboardFiles();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var source in files)
        {
            try
            {
                var destination = GetUniquePath(desktop, Path.GetFileName(source));
                if (Directory.Exists(source)) CopyDirectory(source, destination);
                else File.Copy(source, destination, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not paste {Path.GetFileName(source)}.\n\n{ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            }
        }
        LoadIcons();
    }

    private void PasteShortcuts()
    {
        var files = GetClipboardFiles();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var source in files)
        {
            try { CreateShortcut(desktop, source); }
            catch (Exception ex) { MessageBox.Show($"Could not create shortcut.\n\n{ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning); break; }
        }
        LoadIcons();
    }

    private void CreateNewShortcut()
    {
        var target = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var input = SimpleInputBox.Show("New shortcut", "Target path:", string.Empty);
        if (string.IsNullOrWhiteSpace(input)) return;

        try { CreateShortcut(target, input); LoadIcons(); }
        catch (Exception ex) { MessageBox.Show($"Could not create shortcut.\n\n{ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void CreateShortcut(string folder, string target)
    {
        if (!File.Exists(target) && !Directory.Exists(target))
            throw new FileNotFoundException("The target does not exist.", target);

        var baseName = Path.GetFileNameWithoutExtension(target);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Shortcut";
        var shortcut = GetUniquePath(folder, baseName + " - Shortcut.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcut);
            try
            {
                link.TargetPath = target;
                link.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
                link.Save();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static string GetUniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; ; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var image = files.FirstOrDefault(IsWallpaperFile);
        if (image is null) return;

        try
        {
            WallpaperService.Set(image);
            Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop")?.SetValue("WallPaper", image);
            LoadWallpaper();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not set wallpaper\n\n{ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }

    private static bool IsWallpaperFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private void ChangeWallpaper()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff",
            Title = "Choose desktop wallpaper"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            // Paint TinyShell first. This makes the result immediate even if
            // the Windows shell APIs are unavailable while Explorer is gone.
            SetWallpaperImage(dlg.FileName);
            WallpaperService.Set(dlg.FileName);

            using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
            key?.SetValue("WallPaper", Path.GetFullPath(dlg.FileName), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not set wallpaper.\n\n{ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetWallpaperImage(string path)
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(Path.GetFullPath(path));
        bmp.EndInit();
        bmp.Freeze();
        WallpaperBrush.ImageSource = bmp;
    }

    private void CreateNewFolder()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var basePath = System.IO.Path.Combine(dir, "New folder");
        var path = basePath;
        int i = 2;
        while (Directory.Exists(path)) path = $"{basePath} ({i++})";

        try
        {
            Directory.CreateDirectory(path);
            var name = SimpleInputBox.Show("New folder", "Name:", System.IO.Path.GetFileName(path));
            if (!string.IsNullOrWhiteSpace(name) && name != System.IO.Path.GetFileName(path))
                Directory.Move(path, System.IO.Path.Combine(dir, name));
            LoadIcons();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder: {ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CreateNewTextFile()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = System.IO.Path.Combine(dir, "New Text Document.txt");
        int i = 2;
        while (File.Exists(path)) path = System.IO.Path.Combine(dir, $"New Text Document ({i++}).txt");

        try
        {
            File.WriteAllText(path, "");
            var name = SimpleInputBox.Show("New text document", "Name:", System.IO.Path.GetFileNameWithoutExtension(path));
            if (!string.IsNullOrWhiteSpace(name))
            {
                var renamed = System.IO.Path.Combine(dir, name + ".txt");
                if (renamed != path) File.Move(path, renamed);
            }
            LoadIcons();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create file: {ex.Message}", "TinyShell", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
