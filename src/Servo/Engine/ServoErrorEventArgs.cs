namespace Servo;

public sealed class ServoErrorEventArgs(byte errorCode, string message) : EventArgs
{
    public byte ErrorCode { get; } = errorCode;
    public string Message { get; } = message;
}
