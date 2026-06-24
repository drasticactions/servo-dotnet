namespace Servo;

/// <summary>
/// A CPU-signalable GPU timeline semaphore (an <c>MTLSharedEvent</c> on macOS) used to
/// fence frame handoff between a <see cref="RenderingContext"/> and a host compositor.
/// The host imports <see cref="Handle"/>; the producer signals monotonically increasing
/// values once a frame's GPU work has completed
/// (<see cref="RenderingContext.SignalAfterGpuWork"/>), and reads
/// <see cref="SignaledValue"/> to learn when the host has finished consuming a frame.
/// </summary>
public sealed class ServoTimelineSemaphore : IDisposable
{
    private nint _handle;

    private ServoTimelineSemaphore(nint handle) => _handle = handle;

    /// <summary>Create a semaphore, or null when the platform has no implementation.</summary>
    public static unsafe ServoTimelineSemaphore? TryCreate()
    {
        var handle = (nint)ServoNative.timeline_semaphore_new();
        return handle == 0 ? null : new ServoTimelineSemaphore(handle);
    }

    /// <summary>Native shared-event pointer, importable by host compositors.</summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            return _handle;
        }
    }

    /// <summary>The semaphore's current value.</summary>
    public unsafe ulong SignaledValue =>
        _handle == 0 ? 0 : ServoNative.timeline_semaphore_signaled_value((void*)_handle);

    /// <summary>Set the semaphore's value from the CPU.</summary>
    public unsafe void Signal(ulong value)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        ServoNative.timeline_semaphore_signal((void*)_handle, value);
    }

    public unsafe void Dispose()
    {
        if (_handle == 0) return;
        ServoNative.timeline_semaphore_destroy((void*)_handle);
        _handle = 0;
    }
}
