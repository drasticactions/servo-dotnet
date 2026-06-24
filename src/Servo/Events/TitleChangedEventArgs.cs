namespace Servo;

public sealed class TitleChangedEventArgs(string? title) : EventArgs
{
    public string? Title { get; } = title;
}
