using System.IO;
using System.Text.Json;

namespace TinyShell.Shell;

public enum DesktopViewMode { Large, Medium, Small }
public enum DesktopSortMode { Name, Size, ItemType, DateModified }

public sealed class DesktopLayout
{
    public Dictionary<string, double[]> Positions { get; set; } = new();
    public DesktopViewMode ViewMode { get; set; } = DesktopViewMode.Medium;
    public DesktopSortMode SortMode { get; set; } = DesktopSortMode.Name;
    public bool AutoArrange { get; set; } = false;
    public bool AlignToGrid { get; set; } = false;

    private static string PathOnDisk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyShell", "desktop-layout.json");

    public static DesktopLayout Load()
    {
        try
        {
            var p = PathOnDisk;
            if (!File.Exists(p)) return new DesktopLayout();
            return JsonSerializer.Deserialize<DesktopLayout>(File.ReadAllText(p)) ?? new DesktopLayout();
        }
        catch { return new DesktopLayout(); }
    }

    public void Save()
    {
        try
        {
            var p = PathOnDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(this));
        }
        catch { /* non-fatal - icons just re-auto-arrange next load */ }
    }

    public void SetPosition(string key, double x, double y) => Positions[key] = new[] { x, y };

    public bool TryGetPosition(string key, out double x, out double y)
    {
        if (Positions.TryGetValue(key, out var v) && v.Length == 2) { x = v[0]; y = v[1]; return true; }
        x = 0; y = 0;
        return false;
    }
}
