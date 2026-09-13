using System.Windows;

namespace TinyShell.Debloat;

public partial class DebloatWindow : Window
{
    private List<DebloatItem> _items = new();

    public DebloatWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        Status.Text = "loading catalog...";
        RunButton.IsEnabled = false;
        try
        {
            _items = await Debloater.GetCatalogAsync();
            foreach (var i in _items) i.Selected = i.Default;
            Render();
            Status.Text = $"{_items.Count} tweaks available.";
        }
        catch (Exception ex)
        {
            Status.Text = "catalog load FAILED — see console";
            Append("[ERR] " + ex.Message);
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void Render()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var visible = string.IsNullOrEmpty(query)
            ? _items
            : _items.Where(i =>
                i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                i.Desc.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var groups = visible
            .GroupBy(i => i.Category)
            .OrderBy(g => g.Key)
            .Select(g => new GroupVM(g.Key, g.ToList()))
            .ToList();

        GroupsHost.ItemsSource = groups;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int n = _items.Count(i => i.Selected);
        int risky = _items.Count(i => i.Selected && i.Risk == "Aggressive");
        Summary.Text = risky > 0 ? $"{n} selected ({risky} aggressive)" : $"{n} selected";
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Render();

    // ---------------- Presets ----------------

    private void ApplyPreset(Func<DebloatItem, bool> predicate)
    {
        foreach (var i in _items) i.Selected = predicate(i);
        Render();
    }

    private void PresetNone_Click(object sender, RoutedEventArgs e) => ApplyPreset(_ => false);
    private void PresetSafe_Click(object sender, RoutedEventArgs e) => ApplyPreset(i => i.Risk == "Safe");
    private void PresetBalanced_Click(object sender, RoutedEventArgs e) => ApplyPreset(i => i.Risk is "Safe" or "Moderate");
    private void PresetAggressive_Click(object sender, RoutedEventArgs e) => ApplyPreset(_ => true);

    // ---------------- Execution ----------------

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Nothing selected.", "TinyShell Debloater",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int aggressive = selected.Count(i => i.Risk == "Aggressive");
        string warn = aggressive > 0
            ? $"\n\n{aggressive} aggressive tweak(s) are selected. These can break features."
            : "";

        var confirm = MessageBox.Show(
            $"Apply {selected.Count} tweak(s)?\n\n" +
            $"This will request administrator rights and modify AppX packages, " +
            $"registry policies, services and scheduled tasks.{warn}",
            "Confirm debloat", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        RunButton.IsEnabled = false;
        Log.Clear();

        if (RestorePointCheck.IsChecked == true)
        {
            Status.Text = "creating restore point — approve the UAC prompt...";
            Append("[SYS] requesting System Restore checkpoint...");
            var ok = await Debloater.CreateRestorePointAsync(Append);
            Append(ok ? "[SYS] restore point OK" : "[SYS] restore point failed — continuing anyway");
            Append("");
        }

        Status.Text = "running — approve the UAC prompt...";
        try
        {
            await Debloater.ApplyAsync(selected, Append);
            Status.Text = "finished.";
        }
        catch (Exception ex)
        {
            Status.Text = "FAILED.";
            Append("[ERR] " + ex.Message);
        }
        finally
        {
            RunButton.IsEnabled = true;
            UpdateSummary();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

    private void Append(string line) => Dispatcher.Invoke(() =>
    {
        Log.AppendText(line + Environment.NewLine);
        Log.ScrollToEnd();
    });

    private sealed record GroupVM(string Name, List<DebloatItem> Items);
}
