namespace Servo;

public sealed class MediaSessionEventArgs(byte eventType, string json) : EventArgs
{
    public byte EventType { get; } = eventType;
    public string Json { get; } = json;
}
