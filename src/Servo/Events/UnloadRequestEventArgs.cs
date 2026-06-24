namespace Servo;

public sealed class UnloadRequestEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    internal UnloadRequestEventArgs(nuint handle)
    {
        _handle = handle;
    }

    public void Allow()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.unload_request_allow(_handle);
    }

    public void Deny()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.unload_request_deny(_handle);
    }
}
