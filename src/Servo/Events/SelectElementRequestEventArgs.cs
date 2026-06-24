using System.Text.Json;

namespace Servo;

public sealed class SelectElementRequestEventArgs : EventArgs
{
    public IReadOnlyList<SelectOptionOrGroup> Options { get; }
    public int? SelectedOptionId { get; }

    public int PositionX { get; }

    public int PositionY { get; }

    public int PositionWidth { get; }

    public int PositionHeight { get; }

    private readonly nuint _handle;
    private bool _responded;

    internal SelectElementRequestEventArgs(IReadOnlyList<SelectOptionOrGroup> options, int? selectedOptionId,
        int posX, int posY, int posW, int posH, nuint handle)
    {
        Options = options;
        SelectedOptionId = selectedOptionId;
        PositionX = posX;
        PositionY = posY;
        PositionWidth = posW;
        PositionHeight = posH;
        _handle = handle;
    }

    public void Select(int optionId)
    {
        if (_responded) return;
        _responded = true;
        ServoNative.select_element_respond(_handle, optionId);
    }

    public void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.select_element_respond(_handle, -1);
    }

    internal static IReadOnlyList<SelectOptionOrGroup> ParseOptionsJson(string json)
    {
        var result = new List<SelectOptionOrGroup>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var type = element.GetProperty("type").GetString();
            if (type == "optgroup")
            {
                var label = element.GetProperty("label").GetString() ?? "";
                var options = new List<SelectOption>();
                foreach (var opt in element.GetProperty("options").EnumerateArray())
                {
                    options.Add(new SelectOption(
                        opt.GetProperty("id").GetInt32(),
                        opt.GetProperty("label").GetString() ?? "",
                        opt.GetProperty("disabled").GetBoolean()));
                }
                result.Add(new SelectOptionOrGroup(label, options));
            }
            else
            {
                result.Add(new SelectOptionOrGroup(new SelectOption(
                    element.GetProperty("id").GetInt32(),
                    element.GetProperty("label").GetString() ?? "",
                    element.GetProperty("disabled").GetBoolean())));
            }
        }
        return result;
    }
}
