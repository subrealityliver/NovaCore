using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TinyShell.Interop;
using TinyShell.Shell;

namespace TinyShell.FileManager;

public sealed class FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public ImageSource? Icon { get; init; }
    public string TypeLabel => IsDirectory ? "Folder" : "File";
    public string SizeLabel { get; init; } = "";
    public string ModifiedLabel { get; init; } = "";
}

public partial class FileManagerWindow : Window
{
    private string _currentPath;

    public FileManagerWindow(string? startPath = null)
    {
        InitializeComponent();
        _currentPath = startPath is not null && Directory.Exists(startPath)
            ? startPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Loaded += (_, _) => Navigate(_currentPath);
    }

    private void Navigate(string path)
    {
        if (!Directory.Exists(path))
        {
            StatusText.Text = $"Cannot open {path}";
            return;
        }

        _currentPath = path;
        PathBox.Text = path;

        var entries = new List<FileEntry>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var di = new DirectoryInfo(dir);
                entries.Add(new FileEntry
                {
                    Name = di.Name,
                    FullPath = dir,
                    IsDirectory = true,
                    Icon = ShellIcon.GetIcon(dir, true, 18),
                    ModifiedLabel = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                var fi = new FileInfo(file);
                entries.Add(new FileEntry
                {
                    Name = fi.Name,
                    FullPath = file,
                    IsDirectory = false,
                    Icon = ShellIcon.GetIcon(file, false, 18),
                    SizeLabel = FormatSize(fi.Length),
                    ModifiedLabel = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }

        FileList.ItemsSource = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        StatusText.Text = $"{entries.Count} item(s)";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    private void Open(FileEntry entry)
    {
        try
        {
            if (entry.IsDirectory)
            {
                Navigate(entry.FullPath);
            }
            else
            {
                Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open {entry.Name}\n\n{ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is FileEntry entry) Open(entry);
    }

    private void FileList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileEntry entry) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        var pt = PointToScreen(e.GetPosition(this));

        // Real shell context menu (Cut/Copy/Paste, real Recycle-Bin
        // Delete, Properties, shell extensions, ...) instead of a
        // hand-picked handful of commands.
        var extras = new (string, Action)[] { ("Copy path", () => Clipboard.SetText(entry.FullPath)) };
        ShellContextMenu.Show(hwnd, entry.FullPath, (int)pt.X, (int)pt.Y, extras);

        // The shell menu can rename/delete/move the item behind our back.
        Dispatcher.BeginInvoke(() => Navigate(_currentPath));
    }

    private void Rename(FileEntry entry)
    {
        var input = SimpleInputBox.Show("Rename", "New name:", entry.Name);
        if (string.IsNullOrWhiteSpace(input) || input == entry.Name) return;

        try
        {
            var newPath = Path.Combine(_currentPath, input);
            if (entry.IsDirectory) Directory.Move(entry.FullPath, newPath);
            else File.Move(entry.FullPath, newPath);
            Navigate(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Rename failed: {ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete(FileEntry entry)
    {
        var confirm = MessageBox.Show(
            $"Move \"{entry.Name}\" to the Recycle Bin?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        if (RecycleBin.Send(entry.FullPath))
        {
            Navigate(_currentPath);
        }
        else
        {
            MessageBox.Show("Could not delete that item.", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null) Navigate(parent.FullName);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Navigate(_currentPath);

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Navigate(PathBox.Text.Trim());
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = "New folder";
        var path = Path.Combine(_currentPath, name);
        int i = 2;
        while (Directory.Exists(path)) path = Path.Combine(_currentPath, $"{name} ({i++})");

        try
        {
            Directory.CreateDirectory(path);
            Navigate(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder: {ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
