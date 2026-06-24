using System.Runtime.InteropServices;

namespace Servo;

public sealed class UserContentManager : IDisposable
{
    private nint _handle;
    private bool _disposed;

    public unsafe UserContentManager(ServoEngine engine)
    {
        _handle = (nint)ServoNative.user_content_manager_new((void*)engine.Handle);
        if (_handle == 0)
            throw new InvalidOperationException("Failed to create UserContentManager");
    }

    public unsafe void AddScript(UserScript script)
    {
        ThrowIfDisposed();
        ServoNative.user_content_manager_add_script((void*)_handle, (void*)script.Handle);
    }

    public unsafe void RemoveScript(UserScript script)
    {
        ThrowIfDisposed();
        ServoNative.user_content_manager_remove_script((void*)_handle, (void*)script.Handle);
    }

    public unsafe void AddStyleSheet(UserStyleSheet stylesheet)
    {
        ThrowIfDisposed();
        ServoNative.user_content_manager_add_stylesheet((void*)_handle, (void*)stylesheet.Handle);
    }

    public unsafe void RemoveStyleSheet(UserStyleSheet stylesheet)
    {
        ThrowIfDisposed();
        ServoNative.user_content_manager_remove_stylesheet((void*)_handle, (void*)stylesheet.Handle);
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0)
        {
            ServoNative.user_content_manager_destroy((void*)_handle);
            _handle = 0;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
