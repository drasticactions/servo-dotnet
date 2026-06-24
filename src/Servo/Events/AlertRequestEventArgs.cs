namespace Servo;

public sealed class AlertRequestEventArgs : EventArgs
{
    public string Message { get; }
    private readonly nuint _handle;
    private bool _responded;

    internal AlertRequestEventArgs(string message, nuint handle)
    {
        Message = message;
        _handle = handle;
    }

    public void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.dialog_alert_dismiss(_handle);
    }
}
