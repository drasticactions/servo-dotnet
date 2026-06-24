using System.Runtime.InteropServices;

namespace Servo;

public abstract class RenderingContext : IDisposable
{
    private nint _handle;
    private bool _disposed;

    protected RenderingContext(nint handle, string contextName)
    {
        _handle = handle;
        if (_handle == 0)
        {
            throw new InvalidOperationException($"Failed to create {contextName}");
        }
    }
    
    public bool IsDisposed => _disposed;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>
    /// Resize the rendering surface.
    /// </summary>
    public unsafe void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ServoNative.rendering_context_resize((void*)_handle, width, height);
    }

    /// <summary>
    /// Present the rendered frame.
    /// </summary>
    public unsafe void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ServoNative.rendering_context_present((void*)_handle);
    }

    /// <summary>
    /// Make this rendering context current on the calling thread.
    /// </summary>
    public unsafe bool MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ServoNative.rendering_context_make_current((void*)_handle) == 0;
    }

    /// <summary>
    /// Read the rendered pixels as RGBA8 data.
    /// Returns null if no frame has been rendered yet.
    /// </summary>
    public unsafe PixelData? ReadPixels()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint width, height;
        nuint len;
        var ptr = ServoNative.rendering_context_read_pixels(
            (void*)_handle, &width, &height, &len);

        if (ptr == null || len == 0)
            return null;

        var data = new byte[len];
        Marshal.Copy((nint)ptr, data, 0, (int)len);
        ServoNative.servo_free_bytes(ptr, len);

        return new PixelData(data, width, height);
    }

    /// <summary>
    /// How this context exports rendered frames as native GPU surfaces, if at all.
    /// </summary>
    public unsafe ServoFrameExportKind FrameExportKind
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (ServoFrameExportKind)ServoNative.rendering_context_frame_export_kind((void*)_handle);
        }
    }

    /// <summary>
    /// Take the most recently presented frame out of the swap chain as a native GPU
    /// surface handle. Call after <see cref="Present"/>; returns false when no new frame
    /// is pending or the context doesn't support export (<see cref="FrameExportKind"/>).
    /// The frame must be returned with <see cref="ReleaseFrame"/>.
    /// </summary>
    public unsafe bool TryAcquireFrame(out ServoFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ServoFrameInfo info;
        if (ServoNative.rendering_context_acquire_frame((void*)_handle, &info) == 0)
        {
            frame = default;
            return false;
        }
        frame = new ServoFrame(info.frame_id, (nint)info.native_handle, info.width, info.height);
        return true;
    }

    /// <summary>
    /// Return a frame acquired with <see cref="TryAcquireFrame"/> to the swap chain.
    /// Safe to call after disposal (no-op), since releases can arrive from async
    /// presentation continuations during teardown.
    /// </summary>
    public unsafe void ReleaseFrame(ulong frameId)
    {
        if (_disposed || _handle == 0) return;
        ServoNative.rendering_context_release_frame((void*)_handle, frameId);
    }

    /// <summary>
    /// Block until this context's GPU work has completed, then signal
    /// <paramref name="semaphore"/> to <paramref name="value"/>. Call after
    /// <see cref="TryAcquireFrame"/> so a compositor waiting on
    /// (semaphore &gt;= value) is guaranteed the frame finished rendering.
    /// </summary>
    public unsafe void SignalAfterGpuWork(ServoTimelineSemaphore semaphore, ulong value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ServoNative.rendering_context_signal_after_gpu_work(
            (void*)_handle, (void*)semaphore.Handle, value);
    }

    public unsafe bool ReadPixelsInto(nint destination, nuint destinationLength, out uint width, out uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint w, h;
        var result = ServoNative.rendering_context_read_pixels_into(
            (void*)_handle, (byte*)destination, destinationLength, &w, &h);
        width = w;
        height = h;
        return result != 0;
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0)
        {
            ServoNative.rendering_context_destroy((void*)_handle);
            _handle = 0;
        }
    }
}
