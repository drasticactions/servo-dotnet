namespace Servo;

public sealed class PermissionRequestEventArgs : EventArgs
{
    public PermissionFeature Feature { get; }
    private readonly nuint _handle;
    private bool _responded;

    internal PermissionRequestEventArgs(PermissionFeature feature, nuint handle)
    {
        Feature = feature;
        _handle = handle;
    }

    public void Allow()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.permission_request_allow(_handle);
    }

    public void Deny()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.permission_request_deny(_handle);
    }
}
