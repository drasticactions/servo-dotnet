using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Servo;

public sealed class ProtocolRegistry : IDisposable
{
    private nint _handle;
    private bool _disposed;
    private bool _consumed;
    private readonly List<GCHandle> _handlerHandles = new();
    private readonly HashSet<string> _registeredSchemes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredSchemes => _registeredSchemes;

    public unsafe ProtocolRegistry()
    {
        _handle = (nint)ServoNative.servo_protocol_registry_new();
        if (_handle == 0)
            throw new InvalidOperationException("Failed to create protocol registry");
    }

    public unsafe void Register(string scheme, IProtocolHandler handler)
    {
        ThrowIfDisposed();
        if (_consumed)
            throw new InvalidOperationException("Registry has already been consumed by ServoEngine.");

        var gcHandle = GCHandle.Alloc(handler);
        _handlerHandles.Add(gcHandle);

        var paths = handler.PrivilegedPaths;
        var pathPtrs = new nint[paths.Count];
        for (int i = 0; i < paths.Count; i++)
            pathPtrs[i] = Marshal.StringToCoTaskMemUTF8(paths[i]);

        var pScheme = Marshal.StringToCoTaskMemUTF8(scheme);
        try
        {
            fixed (nint* pPaths = pathPtrs)
            {
                var native = new CProtocolHandler
                {
                    user_data = (void*)GCHandle.ToIntPtr(gcHandle),
                    load = &LoadCallbackImpl,
                    is_fetchable = (byte)(handler.IsFetchable ? 1 : 0),
                    is_secure = (byte)(handler.IsSecure ? 1 : 0),
                    privileged_paths = (byte**)pPaths,
                    privileged_paths_len = (nuint)paths.Count,
                };
                var result = ServoNative.servo_protocol_registry_register(
                    (void*)_handle, (byte*)pScheme, native);
                if (result != 0)
                    throw new ArgumentException($"Failed to register scheme '{scheme}' (error code {result}).");
                _registeredSchemes.Add(scheme);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pScheme);
            foreach (var ptr in pathPtrs)
                Marshal.FreeCoTaskMem(ptr);
        }
    }

    internal nint ConsumeHandle()
    {
        ThrowIfDisposed();
        if (_consumed)
            throw new InvalidOperationException("Registry has already been consumed.");
        _consumed = true;
        var h = _handle;
        _handle = 0;
        return h;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte LoadCallbackImpl(byte* url, void* userData, CProtocolResponse* response)
    {
        var gcHandle = GCHandle.FromIntPtr((nint)userData);
        if (gcHandle.Target is not IProtocolHandler handler)
            return 0;

        var urlStr = Marshal.PtrToStringUTF8((nint)url) ?? "";
        var result = handler.Load(urlStr);
        if (result == null)
            return 0;

        var contentTypeBytes = Encoding.UTF8.GetBytes(result.ContentType + "\0");
        fixed (byte* bodyPtr = result.Body)
        fixed (byte* ctPtr = contentTypeBytes)
        {
            response->body = bodyPtr;
            response->body_len = (nuint)result.Body.Length;
            response->content_type = ctPtr;
            response->status_code = result.StatusCode;
            return 1;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != 0)
        {
            unsafe { ServoNative.servo_protocol_registry_destroy((void*)_handle); }
            _handle = 0;
        }

        foreach (var h in _handlerHandles)
            if (h.IsAllocated) h.Free();
        _handlerHandles.Clear();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}