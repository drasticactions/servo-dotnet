namespace Servo;

public sealed class UrlChangedEventArgs(string url) : EventArgs
{
    public string Url { get; } = url;
}
