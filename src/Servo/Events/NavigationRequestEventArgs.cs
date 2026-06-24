namespace Servo;

public sealed class NavigationRequestEventArgs : EventArgs
{
    public string Url { get; }
    private readonly nuint _handle;
    private bool _responded;

    internal NavigationRequestEventArgs(string url, nuint handle)
    {
        Url = url;
        _handle = handle;
    }

    public void Allow()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.navigation_request_allow(_handle);
    }

    public void Deny()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.navigation_request_deny(_handle);
    }
}
