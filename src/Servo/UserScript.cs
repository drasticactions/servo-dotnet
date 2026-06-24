using System.Runtime.InteropServices;

namespace Servo;

public sealed class UserScript : IDisposable
{
    private nint _handle;
    private bool _disposed;

    public unsafe UserScript(string script, string? sourceFile = null)
    {
        var pScript = Marshal.StringToCoTaskMemUTF8(script);
        try
        {
            if (sourceFile != null)
            {
                var pSource = Marshal.StringToCoTaskMemUTF8(sourceFile);
                try { _handle = (nint)ServoNative.user_script_new((byte*)pScript, (byte*)pSource); }
                finally { Marshal.FreeCoTaskMem(pSource); }
            }
            else
            {
                _handle = (nint)ServoNative.user_script_new((byte*)pScript, null);
            }
        }
        finally { Marshal.FreeCoTaskMem(pScript); }

        if (_handle == 0)
            throw new InvalidOperationException("Failed to create UserScript");
    }

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0)
        {
            ServoNative.user_script_destroy((void*)_handle);
            _handle = 0;
        }
    }
}
