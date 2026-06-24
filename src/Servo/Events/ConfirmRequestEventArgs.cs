namespace Servo;

public sealed class ConfirmRequestEventArgs : EventArgs
{
    public string Message { get; }
    private readonly nuint _handle;
    private bool _responded;

    internal ConfirmRequestEventArgs(string message, nuint handle)
    {
        Message = message;
        _handle = handle;
    }

    public void Confirm()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.dialog_confirm_respond(_handle, 1);
    }

    public void Cancel()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.dialog_confirm_respond(_handle, 0);
    }
}
