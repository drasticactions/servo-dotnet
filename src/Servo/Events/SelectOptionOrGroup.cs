namespace Servo;

/// <summary>
/// Represents either a standalone option or an optgroup with child options.
/// </summary>
public sealed class SelectOptionOrGroup
{
    public bool IsGroup { get; }
    public string? GroupLabel { get; }
    public SelectOption? Option { get; }
    public IReadOnlyList<SelectOption>? GroupOptions { get; }

    internal SelectOptionOrGroup(SelectOption option)
    {
        IsGroup = false;
        Option = option;
    }

    internal SelectOptionOrGroup(string groupLabel, IReadOnlyList<SelectOption> options)
    {
        IsGroup = true;
        GroupLabel = groupLabel;
        GroupOptions = options;
    }
}
