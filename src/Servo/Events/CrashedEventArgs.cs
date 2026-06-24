namespace Servo;

public sealed class CrashedEventArgs(string reason, string? backtrace) : EventArgs
{
    public string Reason { get; } = reason;
    public string? Backtrace { get; } = backtrace;
}
