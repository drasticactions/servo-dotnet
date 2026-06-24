namespace Servo;

public sealed class DevtoolsStartedEventArgs(ushort port, string token) : EventArgs
{
    public ushort Port { get; } = port;
    public string Token { get; } = token;
}
