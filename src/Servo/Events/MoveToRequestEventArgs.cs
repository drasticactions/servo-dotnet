namespace Servo;

public sealed class MoveToRequestEventArgs(int x, int y) : EventArgs
{
    public int X { get; } = x;
    public int Y { get; } = y;
}
