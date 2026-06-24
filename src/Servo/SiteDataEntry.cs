using System.Text.Json;

namespace Servo;

public sealed class SiteDataEntry(string name, StorageTypes storageTypes)
{
    public string Name { get; } = name;
    public StorageTypes StorageTypes { get; } = storageTypes;

    internal static IReadOnlyList<SiteDataEntry> ParseJson(string json)
    {
        var result = new List<SiteDataEntry>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = element.GetProperty("name").GetString() ?? "";
            var st = (StorageTypes)element.GetProperty("storage_types").GetByte();
            result.Add(new SiteDataEntry(name, st));
        }
        return result;
    }
}
