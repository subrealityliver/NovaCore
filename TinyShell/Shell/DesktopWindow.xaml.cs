using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using TinyShell.FileManager;
using TinyShell.Interop;

namespace TinyShell.Shell;

public sealed class DesktopIcon
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsSpecial { get; init; }
    public ImageSource? Icon { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
}

public partial class DesktopWindow : Window
{
    private const string RecycleBinKey = "::RecycleBin";
    private const double TileWidth = 96, TileHeight = 96, MarginLeft = 12, MarginTop = 12;
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
        Loaded += OnLoaded;
        Closed += (_, _) => { foreach (var w in _watchers) w.Dispose(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        Left = vs.Left; Top = vs.Top; Width = vs.Width; Height = vs.Height;

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
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                WallpaperBrush.ImageSource = bmp;
                return;
            }
        }
        catch { /* fall through to solid color */ }

        RootGrid.Background = new SolidColorBrush(Color.FromRgb(0x05, 0x06, 0x0A));
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

        var ordered = items.OrderBy(i => !i.IsSpecial)
                            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                            .ToList();

        int maxRows = Math.Max(1, (int)((Height - MarginTop) / TileHeight));
        int col = 0, row = 0;

        foreach (var icon in ordered)
        {
            var key = LayoutKey(icon);
            if (_layout.TryGetPosition(key, out var x, out var y))
            {
                icon.X = x; icon.Y = y;
            }
            else
            {
                icon.X = MarginLeft + col * TileWidth;
                icon.Y = MarginTop + row * TileHeight;
                row++;
                if (row >= maxRows) { row = 0; col++; }
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

        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        if (icon.IsSpecial)
        {
            Add("Open", () => OpenIcon(icon));
            Add("Empty Recycle Bin", () =>
            {
                if (MessageBox.Show("Permanently empty the Recycle Bin?", "TinyShell",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    RecycleBin.Empty();
            });
        }
        else
        {
            Add("Open", () => OpenIcon(icon));
            Add("Rename", () => RenameIcon(icon));
            Add("Delete", () => DeleteIcon(icon));
            menu.Items.Add(new Separator());
            Add("Open file location", () =>
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{icon.Path}\"")));
            Add("Copy path", () => Clipboard.SetText(icon.Path));
        }

        menu.IsOpen = true;
    }

    private void RootGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        MenuItem AddSub(string header) { var mi = new MenuItem { Header = header }; menu.Items.Add(mi); return mi; }
        void AddChild(MenuItem parent, string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            parent.Items.Add(mi);
        }

        var newMenu = AddSub("New");
        AddChild(newMenu, "Folder", CreateNewFolder);
        AddChild(newMenu, "Text Document", CreateNewTextFile);

        menu.Items.Add(new Separator());
        Add("Refresh", LoadIcons);
        Add("Arrange icons by name", () => { _layout.Positions.Clear(); _layout.Save(); LoadIcons(); });
        menu.Items.Add(new Separator());
        Add("Open File Manager", () => new FileManagerWindow(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)).Show());
        Add("Change wallpaper...", () =>
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                key.SetValue("WallPaper", dlg.FileName);
                LoadWallpaper();
            }
        });
        Add("Display settings", () =>
            Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }));

        menu.IsOpen = true;
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
