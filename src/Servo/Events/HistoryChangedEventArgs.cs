using System.Text.Json;

namespace Servo;

public sealed class HistoryChangedEventArgs(IReadOnlyList<string> urls, int currentIndex, int totalEntries) : EventArgs
{
    public IReadOnlyList<string> Urls { get; } = urls;
    public int CurrentIndex { get; } = currentIndex;
    public int TotalEntries { get; } = totalEntries;

    internal static IReadOnlyList<string> ParseUrlsJson(string json)
    {
        return JsonSerializer.Deserialize(json, ServoJsonContext.Default.ListString) ?? [];
    }
}
