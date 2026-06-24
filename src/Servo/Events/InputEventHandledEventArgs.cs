namespace Servo;

public sealed class InputEventHandledEventArgs(ulong eventId, byte result) : EventArgs
{
    public ulong EventId { get; } = eventId;
    public byte Result { get; } = result;
}
