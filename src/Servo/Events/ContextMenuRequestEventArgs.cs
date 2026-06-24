using System.Text.Json;

namespace Servo;

public enum ContextMenuAction : byte
{
    GoBack = 0,
    GoForward = 1,
    Reload = 2,
    CopyLink = 3,
    OpenLinkInNewWebView = 4,
    CopyImageLink = 5,
    OpenImageInNewView = 6,
    Cut = 7,
    Copy = 8,
    Paste = 9,
    SelectAll = 10,
}

public sealed class ContextMenuItem
{
    public string Label { get; }
    public ContextMenuAction Action { get; }
    public bool Enabled { get; }

    public ContextMenuItem(string label, ContextMenuAction action, bool enabled)
    {
        Label = label;
        Action = action;
        Enabled = enabled;
    }
}

public sealed class ContextMenuRequestEventArgs : EventArgs
{
    public IReadOnlyList<ContextMenuItem> Items { get; }
    public int PositionX { get; }
    public int PositionY { get; }

    private readonly nuint _handle;
    private bool _responded;

    internal ContextMenuRequestEventArgs(IReadOnlyList<ContextMenuItem> items,
        int posX, int posY, nuint handle)
    {
        Items = items;
        PositionX = posX;
        PositionY = posY;
        _handle = handle;
    }

    public void Select(ContextMenuAction action)
    {
        if (_responded) return;
        _responded = true;
        ServoNative.context_menu_select(_handle, (byte)action);
    }

    public void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.context_menu_dismiss(_handle);
    }

    internal static IReadOnlyList<ContextMenuItem> ParseItemsJson(string json)
    {
        var result = new List<ContextMenuItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var type = element.GetProperty("type").GetString();
            if (type == "item")
            {
                result.Add(new ContextMenuItem(
                    element.GetProperty("label").GetString() ?? "",
                    (ContextMenuAction)element.GetProperty("action").GetByte(),
                    element.GetProperty("enabled").GetBoolean()));
            }
            // Separators are skipped for now; they can be represented in the UI layer
        }
        return result;
    }
}
