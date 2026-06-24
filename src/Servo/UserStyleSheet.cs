using System.Runtime.InteropServices;

namespace Servo;

public sealed class UserStyleSheet : IDisposable
{
    private nint _handle;
    private bool _disposed;

    public unsafe UserStyleSheet(string source, string url)
    {
        var pSource = Marshal.StringToCoTaskMemUTF8(source);
        var pUrl = Marshal.StringToCoTaskMemUTF8(url);
        try { _handle = (nint)ServoNative.user_stylesheet_new((byte*)pSource, (byte*)pUrl); }
        finally
        {
            Marshal.FreeCoTaskMem(pSource);
            Marshal.FreeCoTaskMem(pUrl);
        }

        if (_handle == 0)
            throw new InvalidOperationException("Failed to create UserStyleSheet");
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
            ServoNative.user_stylesheet_destroy((void*)_handle);
            _handle = 0;
        }
    }
}
