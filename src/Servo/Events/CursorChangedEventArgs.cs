namespace Servo;

public sealed class CursorChangedEventArgs(ServoCursor cursor) : EventArgs
{
    public ServoCursor Cursor { get; } = cursor;
}
