namespace Servo;

public enum ProtocolHandlerAction : byte
{
    Register = 0,
    Unregister = 1,
}

public sealed class ProtocolHandlerRequestEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    public string Scheme { get; }
    public string Url { get; }
    public ProtocolHandlerAction Action { get; }

    internal ProtocolHandlerRequestEventArgs(string scheme, string url, ProtocolHandlerAction action, nuint handle)
    {
        Scheme = scheme;
        Url = url;
        Action = action;
        _handle = handle;
    }

    public void Allow()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.allow_or_deny_request_allow(_handle);
    }

    public void Deny()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.allow_or_deny_request_deny(_handle);
    }
}
