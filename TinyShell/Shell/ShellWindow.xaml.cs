using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TinyShell.FileManager;
using TinyShell.Interop;
using ScreenType = System.Windows.Forms.Screen;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace TinyShell.Shell;

public partial class ShellWindow : Window
{
    private const int MinThickness = 32;
    private const int MaxThickness = 160;
    private const int HotkeyRestoreExplorer = 0xA001;
    private const int HotkeyTaskManager = 0xA002;

    private readonly ScreenType _screen;
    private readonly bool _isPrimary;
    private readonly bool _shellMode;

    private Native.APPBARDATA _abd;
    private uint _appBarMsg;
    private IntPtr _hwnd;
    private int _thickness;
    private BarEdge _edge;

    // Two independent reasons a bar can be hidden - either can be true
    // without the other (a real exclusive-fullscreen game vs. a borderless
    // fullscreen app we detected via heuristic).
    private bool _hiddenExclusiveFullscreen;
    private bool _hiddenBorderlessFullscreen;

    private readonly ObservableCollection<TaskEntry> _tasks = new();
    private List<ShellApp> _allApps = new();
    private DateTime _lastPopupClose = DateTime.MinValue;
    private DispatcherTimer? _clockTimer;

    public ScreenType Screen => _screen;

    public ShellWindow(ScreenType screen, bool isPrimary, bool shellMode)
    {
        _screen = screen;
        _isPrimary = isPrimary;
        _shellMode = shellMode;

        InitializeComponent();
        TaskItems.ItemsSource = _tasks;

        if (_isPrimary)
        {
            StartButton.Visibility = Visibility.Visible;
            FilesButton.Visibility = Visibility.Visible;
            DebloatButton.Visibility = Visibility.Visible;
            MenuButton.Visibility = Visibility.Visible;
            PowerButton.Visibility = Visibility.Visible;
            StartPopup.Closed += (_, _) => _lastPopupClose = DateTime.UtcNow;
        }

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);

        var settings = App.SharedSettings;
        _edge = settings.Edge;
        _thickness = settings.Thickness;

        ApplyEdgeLayout();
        RegisterAppBar();

        if (_isPrimary)
        {
            Native.RegisterHotKey(_hwnd, HotkeyRestoreExplorer,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT, 0x45 /* E */);
            Native.RegisterHotKey(_hwnd, HotkeyTaskManager,
                Native.MOD_CONTROL | Native.MOD_SHIFT, 0x1B /* Esc */);

            _allApps = StartMenuScanner.Scan();
            AppList.ItemsSource = _allApps;
        }

        if (_shellMode) ShellManager.KillExplorer();

        if (App.Watcher is not null)
        {
            App.Watcher.WindowsChanged += OnWindowsChanged;
            App.Watcher.ForegroundChanged += OnForegroundChanged;
        }

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        RefreshTasks();
        UpdateClock();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _clockTimer?.Stop();
        if (App.Watcher is not null)
        {
            App.Watcher.WindowsChanged -= OnWindowsChanged;
            App.Watcher.ForegroundChanged -= OnForegroundChanged;
        }

        if (_hwnd != IntPtr.Zero)
        {
            if (_isPrimary)
            {
                Native.UnregisterHotKey(_hwnd, HotkeyRestoreExplorer);
                Native.UnregisterHotKey(_hwnd, HotkeyTaskManager);
            }
            _abd.hWnd = _hwnd;
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref _abd);
        }
    }

    private void OnWindowsChanged() => Dispatcher.BeginInvoke(RefreshTasks);

    // ------------------------------------------------------------------
    // Fullscreen auto-hide
    //
    // Two independent signals: ABN_FULLSCREENAPP (true exclusive-fullscreen
    // DirectX apps, handled in WndProc below) and this heuristic check
    // (borderless-fullscreen games/players, which don't send that message
    // at all - see FullscreenDetector for why this can only ever be a
    // heuristic, not a guaranteed detection).
    // ------------------------------------------------------------------

    private void OnForegroundChanged(IntPtr hwnd)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var fgScreen = ScreenType.FromHandle(hwnd);
                if (fgScreen.DeviceName != _screen.DeviceName) return;

                _hiddenBorderlessFullscreen = FullscreenDetector.IsBorderlessFullscreen(hwnd, _screen);
                ApplyVisibility();
            }
            catch { /* foreground window may have closed mid-check */ }
        });
    }

    private void ApplyVisibility()
    {
        bool shouldHide = _hiddenExclusiveFullscreen || _hiddenBorderlessFullscreen;
        if (shouldHide && IsVisible) Hide();
        else if (!shouldHide && !IsVisible) { Show(); PositionAppBar(); }
    }

    // ------------------------------------------------------------------
    // AppBar - configurable edge/thickness, persisted via BarSettings
    // ------------------------------------------------------------------

    /// <summary>
    /// Rotates the bar's content 90°/-90° for a vertical (left/right) edge
    /// instead of maintaining a second XAML layout. WPF's LayoutTransform
    /// (unlike RenderTransform) participates in layout and hit-testing, so
    /// clicks land correctly on the rotated buttons - this is the same
    /// trick used by several rotated-toolbar implementations. Trade-off:
    /// text labels rotate with everything else, so app/window names read
    /// sideways on a vertical bar. Acceptable for a first pass; a true
    /// vertical layout would need a second XAML template.
    /// </summary>
    private void ApplyEdgeLayout()
    {
        var b = _screen.Bounds;

        switch (_edge)
        {
            case BarEdge.Top:
                Width = b.Width; Height = _thickness;
                ContentGrid.LayoutTransform = Transform.Identity;
                EdgeGlow.VerticalAlignment = VerticalAlignment.Bottom;
                ResizeGrip.Height = 6; ResizeGrip.Width = double.NaN;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
                ResizeGrip.HorizontalAlignment = HorizontalAlignment.Stretch;
                ResizeGrip.Cursor = Cursors.SizeNS;
                break;

            case BarEdge.Bottom:
                Width = b.Width; Height = _thickness;
                ContentGrid.LayoutTransform = Transform.Identity;
                EdgeGlow.VerticalAlignment = VerticalAlignment.Top;
                ResizeGrip.Height = 6; ResizeGrip.Width = double.NaN;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Top;
                ResizeGrip.HorizontalAlignment = HorizontalAlignment.Stretch;
                ResizeGrip.Cursor = Cursors.SizeNS;
                break;

            case BarEdge.Left:
                Width = _thickness; Height = b.Height;
                ContentGrid.LayoutTransform = new RotateTransform(90);
                EdgeGlow.VerticalAlignment = VerticalAlignment.Top; // becomes the right-facing edge once rotated
                ResizeGrip.Width = 6; ResizeGrip.Height = double.NaN;
                ResizeGrip.HorizontalAlignment = HorizontalAlignment.Right;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
                ResizeGrip.Cursor = Cursors.SizeWE;
                break;

            case BarEdge.Right:
                Width = _thickness; Height = b.Height;
                ContentGrid.LayoutTransform = new RotateTransform(-90);
                EdgeGlow.VerticalAlignment = VerticalAlignment.Top;
                ResizeGrip.Width = 6; ResizeGrip.Height = double.NaN;
                ResizeGrip.HorizontalAlignment = HorizontalAlignment.Left;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
                ResizeGrip.Cursor = Cursors.SizeWE;
                break;
        }
    }

    public void SetEdge(BarEdge edge)
    {
        _edge = edge;
        ApplyEdgeLayout();
        PositionAppBar();
    }

    private void RegisterAppBar()
    {
        _appBarMsg = Native.WM_APP + 1;
        _abd = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
            hWnd = _hwnd,
            uCallbackMessage = _appBarMsg
        };
        Native.SHAppBarMessage(Native.ABM_NEW, ref _abd);
        PositionAppBar();
    }

    private void PositionAppBar()
    {
        var b = _screen.Bounds;
        _abd.uEdge = _edge switch
        {
            BarEdge.Top => (uint)Native.ABE_TOP,
            BarEdge.Bottom => (uint)Native.ABE_BOTTOM,
            BarEdge.Left => (uint)Native.ABE_LEFT,
            BarEdge.Right => (uint)Native.ABE_RIGHT,
            _ => (uint)Native.ABE_BOTTOM
        };

        _abd.rc = _edge switch
        {
            BarEdge.Top => new Native.RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Top + _thickness },
            BarEdge.Bottom => new Native.RECT { Left = b.Left, Top = b.Bottom - _thickness, Right = b.Right, Bottom = b.Bottom },
            BarEdge.Left => new Native.RECT { Left = b.Left, Top = b.Top, Right = b.Left + _thickness, Bottom = b.Bottom },
            BarEdge.Right => new Native.RECT { Left = b.Right - _thickness, Top = b.Top, Right = b.Right, Bottom = b.Bottom },
            _ => default
        };

        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref _abd);

        // Re-assert our own thickness - QUERYPOS may offer a different
        // size back if another appbar already claims part of the edge.
        switch (_edge)
        {
            case BarEdge.Top: _abd.rc.Bottom = _abd.rc.Top + _thickness; break;
            case BarEdge.Bottom: _abd.rc.Top = _abd.rc.Bottom - _thickness; break;
            case BarEdge.Left: _abd.rc.Right = _abd.rc.Left + _thickness; break;
            case BarEdge.Right: _abd.rc.Left = _abd.rc.Right - _thickness; break;
        }

        Native.SHAppBarMessage(Native.ABM_SETPOS, ref _abd);

        Native.SetWindowPos(_hwnd, IntPtr.Zero,
            _abd.rc.Left, _abd.rc.Top,
            _abd.rc.Right - _abd.rc.Left,
            _abd.rc.Bottom - _abd.rc.Top,
            Native.SWP_NOACTIVATE);
    }

    // ------------------------------------------------------------------
    // Drag-to-resize
    // ------------------------------------------------------------------

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        int delta = _edge switch
        {
            BarEdge.Top => (int)e.VerticalChange,
            BarEdge.Bottom => -(int)e.VerticalChange,
            BarEdge.Left => (int)e.HorizontalChange,
            BarEdge.Right => -(int)e.HorizontalChange,
            _ => 0
        };

        _thickness = Math.Clamp(_thickness + delta, MinThickness, MaxThickness);
        ApplyEdgeLayout();
        PositionAppBar();
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        App.SharedSettings.Thickness = _thickness;
        App.SharedSettings.Save();
        App.BroadcastThickness(_thickness, exclude: this);
    }

    public void ApplyExternalThickness(int thickness)
    {
        _thickness = thickness;
        ApplyEdgeLayout();
        PositionAppBar();
    }

    // ------------------------------------------------------------------
    // WndProc
    // ------------------------------------------------------------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_appBarMsg)
        {
            switch (wParam.ToInt32())
            {
                case Native.ABN_POSCHANGED:
                    PositionAppBar();
                    handled = true;
                    break;
                case Native.ABN_FULLSCREENAPP:
                    _hiddenExclusiveFullscreen = lParam != IntPtr.Zero;
                    ApplyVisibility();
                    handled = true;
                    break;
            }
        }
        else if (msg == Native.WM_HOTKEY && _isPrimary)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyRestoreExplorer)
            {
                ShellManager.UninstallShell();
                ShellManager.RestoreExplorer();
            }
            else if (id == HotkeyTaskManager)
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            handled = true;
        }
        else if (msg == Native.WM_DISPLAYCHANGE)
        {
            PositionAppBar();
        }

        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------
    // Task list
    // ------------------------------------------------------------------

    private void RefreshTasks()
    {
        if (App.Watcher is null) return;

        var current = App.Watcher.GetCurrent()
            .Where(t => ScreenType.FromHandle(t.Hwnd).DeviceName == _screen.DeviceName)
            .ToList();

        var byHandle = _tasks.ToDictionary(t => t.Hwnd);

        for (int i = _tasks.Count - 1; i >= 0; i--)
            if (!current.Any(c => c.Hwnd == _tasks[i].Hwnd))
                _tasks.RemoveAt(i);

        for (int i = 0; i < current.Count; i++)
        {
            var c = current[i];
            if (byHandle.TryGetValue(c.Hwnd, out var existing))
            {
                int idx = _tasks.IndexOf(existing);
                if (existing.Title != c.Title || existing.IsMinimized != c.IsMinimized) _tasks[idx] = c;
            }
            else
            {
                _tasks.Insert(Math.Min(i, _tasks.Count), c);
            }
        }
    }

    private void Task_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TaskEntry entry })
            WindowWatcher.Activate(entry.Hwnd);
    }

    private void Task_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TaskEntry entry }) return;
        e.Handled = true;

        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        Add(entry.IsMinimized ? "Restore" : "Minimize", () =>
        {
            if (entry.IsMinimized) WindowWatcher.Activate(entry.Hwnd);
            else Native.ShowWindow(entry.Hwnd, Native.SW_MINIMIZE);
        });
        Add("Close window", () => Native.PostMessage(entry.Hwnd, (uint)Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        menu.IsOpen = true;
    }

    private void UpdateClock() => Clock.Text = DateTime.Now.ToString("HH:mm:ss");

    // ------------------------------------------------------------------
    // Start menu - single click to launch, right click for options
    // ------------------------------------------------------------------

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartPopup.IsOpen) { StartPopup.IsOpen = false; return; }
        if ((DateTime.UtcNow - _lastPopupClose).TotalMilliseconds < 200) return;

        _allApps = StartMenuScanner.Scan();
        AppList.ItemsSource = _allApps;
        SearchBox.Text = "";
        StartPopup.IsOpen = true;
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        AppList.ItemsSource = string.IsNullOrEmpty(q)
            ? _allApps
            : _allApps.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (AppList.ItemsSource is IEnumerable<ShellApp> apps && apps.FirstOrDefault() is { } first)
            Launch(first);
        e.Handled = true;
    }

    private void AppButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ShellApp app }) Launch(app);
    }

    private void AppButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ShellApp app }) return;
        e.Handled = true;

        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        Add("Open", () => Launch(app));
        Add("Run as administrator", () => Launch(app, asAdmin: true));
        Add("Open file location", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{app.Target}\"")));
        Add("Copy path", () => Clipboard.SetText(app.Target));
        menu.IsOpen = true;
    }

    private void Launch(ShellApp app, bool asAdmin = false)
    {
        StartPopup.IsOpen = false;
        try
        {
            var psi = new ProcessStartInfo(app.Target) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(app.Arguments)) psi.Arguments = app.Arguments;
            if (asAdmin) psi.Verb = "runas";
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not launch {app.Name}\n\n{ex.Message}", "TinyShell",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------
    // Power
    // ------------------------------------------------------------------

    private static bool Confirm(string action) =>
        MessageBox.Show($"{action} now?", "TinyShell", MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    private void Sleep_Click(object sender, RoutedEventArgs e) { if (Confirm("Sleep")) PowerActions.Sleep(); }
    private void Restart_Click(object sender, RoutedEventArgs e) { if (Confirm("Restart")) PowerActions.Restart(); }
    private void Shutdown_Click(object sender, RoutedEventArgs e) { if (Confirm("Shut down")) PowerActions.Shutdown(); }

    private void Power_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        void Add(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        Add("Sleep", () => { if (Confirm("Sleep")) PowerActions.Sleep(); });
        Add("Restart", () => { if (Confirm("Restart")) PowerActions.Restart(); });
        Add("Shut down", () => { if (Confirm("Shut down")) PowerActions.Shutdown(); });
        Add("Sign out", () => { if (Confirm("Sign out")) PowerActions.SignOut(); });
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------
    // Buttons (primary bar only) + taskbar background right-click
    // ------------------------------------------------------------------

    private void Files_Click(object sender, RoutedEventArgs e) => new FileManagerWindow().Show();

    private void Debloat_Click(object sender, RoutedEventArgs e) => new Debloat.DebloatWindow().Show();

    private void Menu_Click(object sender, RoutedEventArgs e) => ShowShellMenu((UIElement)sender);

    private void RootLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e) =>
        ShowShellMenu(RootLayout);

    private void ShowShellMenu(UIElement placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget };

        void Add(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        MenuItem AddSub(string header)
        {
            var mi = new MenuItem { Header = header };
            menu.Items.Add(mi);
            return mi;
        }

        void AddChild(MenuItem parent, string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            parent.Items.Add(mi);
        }

        var moveMenu = AddSub("Move taskbar to");
        AddChild(moveMenu, "Top", () => App.ApplyEdgeToAll(BarEdge.Top));
        AddChild(moveMenu, "Bottom", () => App.ApplyEdgeToAll(BarEdge.Bottom));
        AddChild(moveMenu, "Left", () => App.ApplyEdgeToAll(BarEdge.Left));
        AddChild(moveMenu, "Right", () => App.ApplyEdgeToAll(BarEdge.Right));

        var sizeMenu = AddSub("Taskbar size");
        AddChild(sizeMenu, "Small", () => App.ApplyThicknessToAll(36));
        AddChild(sizeMenu, "Medium", () => App.ApplyThicknessToAll(46));
        AddChild(sizeMenu, "Large", () => App.ApplyThicknessToAll(64));

        menu.Items.Add(new Separator());
        Add("File manager", () => new FileManagerWindow().Show());
        Add("Debloat console", () => new Debloat.DebloatWindow().Show());
        menu.Items.Add(new Separator());

        Add(ShellManager.IsInstalledAsShell()
                ? "Uninstall TinyShell as logon shell"
                : "Install TinyShell as logon shell",
            () =>
            {
                if (ShellManager.IsInstalledAsShell())
                {
                    ShellManager.UninstallShell();
                    MessageBox.Show("TinyShell will no longer start at logon.\n" +
                                    "Run explorer.exe to restore the stock shell now.",
                        "TinyShell", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ShellManager.InstallAsShell();
                    MessageBox.Show("TinyShell registered as your logon shell.\n" +
                                    "Sign out and back in to test it.\n\n" +
                                    "Emergency exit: Ctrl+Alt+Shift+E restores explorer.exe.",
                        "TinyShell", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });

        menu.Items.Add(new Separator());
        Add("Restore explorer.exe", () => ShellManager.RestoreExplorer());
        Add("Open Task Manager", () =>
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }));
        menu.Items.Add(new Separator());

        var powerMenu = AddSub("Power");
        AddChild(powerMenu, "Sleep", () => { if (Confirm("Sleep")) PowerActions.Sleep(); });
        AddChild(powerMenu, "Restart", () => { if (Confirm("Restart")) PowerActions.Restart(); });
        AddChild(powerMenu, "Shut down", () => { if (Confirm("Shut down")) PowerActions.Shutdown(); });
        AddChild(powerMenu, "Sign out", () => { if (Confirm("Sign out")) PowerActions.SignOut(); });

        menu.Items.Add(new Separator());
        Add("Exit TinyShell", () => Application.Current.Shutdown());

        menu.IsOpen = true;
    }
}
