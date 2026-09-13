using System.Text.Json.Serialization;

namespace TinyShell.Debloat;

public sealed class DebloatItem
{
    [JsonPropertyName("Id")]       public string Id { get; set; } = "";
    [JsonPropertyName("Name")]     public string Name { get; set; } = "";
    [JsonPropertyName("Desc")]     public string Desc { get; set; } = "";
    [JsonPropertyName("Category")] public string Category { get; set; } = "";
    [JsonPropertyName("Risk")]     public string Risk { get; set; } = "Safe";
    [JsonPropertyName("Default")]  public bool Default { get; set; }

    [JsonIgnore] public bool Selected { get; set; }
}
