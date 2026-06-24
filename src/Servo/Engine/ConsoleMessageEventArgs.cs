namespace Servo;

public sealed class ConsoleMessageEventArgs(ConsoleLogLevel level, string message) : EventArgs
{
    public ConsoleLogLevel Level { get; } = level;
    public string Message { get; } = message;
}
