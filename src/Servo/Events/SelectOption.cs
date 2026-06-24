namespace Servo;

/// <summary>
/// Represents a single option in a select element.
/// </summary>
public sealed class SelectOption
{
    public int Id { get; }
    public string Label { get; }
    public bool IsDisabled { get; }

    internal SelectOption(int id, string label, bool isDisabled)
    {
        Id = id;
        Label = label;
        IsDisabled = isDisabled;
    }
}
