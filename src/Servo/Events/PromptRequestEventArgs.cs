

using System.Runtime.InteropServices;

namespace Servo;

public sealed class PromptRequestEventArgs : EventArgs
{
    public string Message { get; }
    public string DefaultValue { get; }
    private readonly nuint _handle;
    private bool _responded;

    internal PromptRequestEventArgs(string message, string defaultValue, nuint handle)
    {
        Message = message;
        DefaultValue = defaultValue;
        _handle = handle;
    }

    public unsafe void Respond(string value)
    {
        if (_responded) return;
        _responded = true;
        var pValue = Marshal.StringToCoTaskMemUTF8(value);
        try { ServoNative.dialog_prompt_respond(_handle, (byte*)pValue); }
        finally { Marshal.FreeCoTaskMem(pValue); }
    }

    public unsafe void Cancel()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.dialog_prompt_respond(_handle, null);
    }
}
