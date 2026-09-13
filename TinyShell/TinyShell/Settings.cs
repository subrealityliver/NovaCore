using System.IO;
using System.Text.Json;

namespace TinyShell;

public enum BarEdge { Top, Bottom, Left, Right }

public sealed class BarSettings
{
    public BarEdge Edge { get; set; } = BarEdge.Bottom;
    public int Thickness { get; set; } = 46;
    public bool DesktopEnabled { get; set; } = true;

    private static string PathOnDisk =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TinyShell", "settings.json");

    public static BarSettings Load()
    {
        try
        {
            var path = PathOnDisk;
            if (!File.Exists(path)) return new BarSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BarSettings>(json) ?? new BarSettings();
        }
        catch
        {
            return new BarSettings();
        }
    }

    public void Save()
    {
        try
        {
            var path = PathOnDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch { /* non-fatal - just means the preference won't persist */ }
    }
}
