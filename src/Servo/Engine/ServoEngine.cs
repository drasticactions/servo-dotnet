

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Servo;

public sealed class ServoEngine : IDisposable
{
    private nint _handle;
    private GCHandle _wakerHandle;
    private GCHandle _delegateHandle;
    private ProtocolRegistry? _protocolRegistry;
    private bool _disposed;

    public Action? EventLoopWaker { get; set; }

    public event EventHandler<ServoErrorEventArgs>? Error;

    public event EventHandler<DevtoolsStartedEventArgs>? DevtoolsStarted;

    public event EventHandler<ConsoleMessageEventArgs>? ConsoleMessage;

    public event EventHandler<WebResourceLoadEventArgs>? WebResourceLoadRequested;
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public unsafe ServoEngine(string? resourcePath = null, ProtocolRegistry? protocolRegistry = null)
    {
        _protocolRegistry = protocolRegistry;
        _wakerHandle = GCHandle.Alloc(this);
        try
        {
            var waker = new CEventLoopWaker
            {
                user_data = (void*)GCHandle.ToIntPtr(_wakerHandle),
                wake = &WakeCallbackImpl,
            };

            var registryPtr = protocolRegistry?.ConsumeHandle() ?? 0;

            if (resourcePath != null)
            {
                var pResource = Marshal.StringToCoTaskMemUTF8(resourcePath);
                try { _handle = (nint)ServoNative.servo_new(waker, (byte*)pResource, (void*)registryPtr); }
                finally { Marshal.FreeCoTaskMem(pResource); }
            }
            else
            {
                _handle = (nint)ServoNative.servo_new(waker, null, (void*)registryPtr);
            }

            if (_handle == 0)
            {
                var error = GetLastError();
                throw new InvalidOperationException($"Failed to create Servo engine: {error}");
            }

            _delegateHandle = GCHandle.Alloc(this);
            var servoCallbacks = new ServoCallbacks
            {
                user_data = (void*)GCHandle.ToIntPtr(_delegateHandle),
                on_error = &OnErrorImpl,
                on_devtools_started = &OnDevtoolsStartedImpl,
                on_console_message = &OnConsoleMessageImpl,
                on_load_web_resource = &OnLoadWebResourceImpl,
                on_show_notification = &OnShowNotificationImpl,
            };
            ServoNative.servo_set_delegate((void*)_handle, servoCallbacks);
        }
        catch
        {
            if (_handle != 0)
            {
                ServoNative.servo_destroy((void*)_handle);
                _handle = 0;
            }
            if (_delegateHandle.IsAllocated) _delegateHandle.Free();
            _wakerHandle.Free();
            throw;
        }
    }

    public bool IsDisposed => _disposed;

    public unsafe void SpinEventLoop()
    {
        ThrowIfDisposed();
        ServoNative.servo_spin_event_loop((void*)_handle);
    }

    public unsafe void SetPreference(string name, string value)
    {
        ThrowIfDisposed();
        var pName = Marshal.StringToCoTaskMemUTF8(name);
        var pValue = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            ServoNative.servo_set_preference((void*)_handle, (byte*)pName, (byte*)pValue);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pName);
            Marshal.FreeCoTaskMem(pValue);
        }
    }

    public IReadOnlyCollection<string> RegisteredSchemes =>
        _protocolRegistry?.RegisteredSchemes ?? Array.Empty<string>();

    public unsafe IReadOnlyList<SiteDataEntry> GetSiteData(StorageTypes storageTypes = StorageTypes.All)
    {
        ThrowIfDisposed();
        var ptr = ServoNative.servo_site_data((void*)_handle, (byte)storageTypes);
        if (ptr == null) return [];
        var json = Marshal.PtrToStringUTF8((nint)ptr) ?? "[]";
        ServoNative.servo_free_string(ptr);
        return SiteDataEntry.ParseJson(json);
    }

    public unsafe void ClearSiteData(IReadOnlyList<string> sites, StorageTypes storageTypes = StorageTypes.All)
    {
        ThrowIfDisposed();
        var ptrs = new nint[sites.Count];
        try
        {
            for (int i = 0; i < sites.Count; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(sites[i]);
            fixed (nint* pPtrs = ptrs)
            {
                ServoNative.servo_clear_site_data(
                    (void*)_handle, (byte**)pPtrs, (nuint)sites.Count, (byte)storageTypes);
            }
        }
        finally
        {
            foreach (var p in ptrs)
                if (p != 0) Marshal.FreeCoTaskMem(p);
        }
    }

    public unsafe void ClearCookies()
    {
        ThrowIfDisposed();
        ServoNative.servo_clear_cookies((void*)_handle, null, null);
    }

    /// <summary>Clears all cookies, completing when the operation has finished.</summary>
    public unsafe Task ClearCookiesAsync()
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        ServoNative.servo_clear_cookies(
            (void*)_handle, &OnCookieOpCompleteImpl, (void*)GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    /// <summary>Deletes all session cookies, completing when the operation has finished.</summary>
    public unsafe Task ClearSessionCookiesAsync()
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        ServoNative.servo_clear_session_cookies(
            (void*)_handle, &OnCookieOpCompleteImpl, (void*)GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    /// <summary>
    /// Sets a cookie for the domain associated with <paramref name="url"/>, completing
    /// when the operation has finished. <paramref name="cookie"/> is a Set-Cookie header
    /// value (e.g. <c>name=value; Path=/</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The URL or cookie value could not be parsed.</exception>
    public unsafe Task SetCookieForUrlAsync(string url, string cookie)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        var pUrl = Marshal.StringToCoTaskMemUTF8(url);
        var pCookie = Marshal.StringToCoTaskMemUTF8(cookie);
        int rc;
        try
        {
            rc = ServoNative.servo_set_cookie_for_url(
                (void*)_handle, (byte*)pUrl, (byte*)pCookie,
                &OnCookieOpCompleteImpl, (void*)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pUrl);
            Marshal.FreeCoTaskMem(pCookie);
        }

        if (rc != 0)
        {
            handle.Free();
            throw new ArgumentException($"Failed to set cookie (invalid URL or cookie value): url='{url}'");
        }
        return tcs.Task;
    }

    /// <summary>Returns the cookies for the domain associated with <paramref name="url"/>.</summary>
    public unsafe IReadOnlyList<Cookie> GetCookiesForUrl(string url, CookieSource source = CookieSource.Http)
    {
        ThrowIfDisposed();
        var pUrl = Marshal.StringToCoTaskMemUTF8(url);
        try
        {
            var ptr = ServoNative.servo_cookies_for_url((void*)_handle, (byte*)pUrl, (byte)source);
            if (ptr == null) return [];
            var json = Marshal.PtrToStringUTF8((nint)ptr) ?? "[]";
            ServoNative.servo_free_string(ptr);
            return Cookie.ParseJson(json);
        }
        finally { Marshal.FreeCoTaskMem(pUrl); }
    }

    /// <summary>
    /// Asynchronously returns the cookies for the domain associated with <paramref name="url"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The URL could not be parsed.</exception>
    public unsafe Task<IReadOnlyList<Cookie>> GetCookiesForUrlAsync(string url, CookieSource source = CookieSource.Http)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<IReadOnlyList<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        var pUrl = Marshal.StringToCoTaskMemUTF8(url);
        int rc;
        try
        {
            rc = ServoNative.servo_cookies_for_url_async(
                (void*)_handle, (byte*)pUrl, (byte)source,
                &OnCookiesForUrlImpl, (void*)GCHandle.ToIntPtr(handle));
        }
        finally { Marshal.FreeCoTaskMem(pUrl); }

        if (rc != 0)
        {
            handle.Free();
            throw new ArgumentException($"Failed to query cookies (invalid URL): url='{url}'");
        }
        return tcs.Task;
    }

    public unsafe IReadOnlyList<string> GetCacheEntries()
    {
        ThrowIfDisposed();
        var ptr = ServoNative.servo_cache_entries((void*)_handle);
        if (ptr == null) return [];
        var json = Marshal.PtrToStringUTF8((nint)ptr) ?? "[]";
        ServoNative.servo_free_string(ptr);
        return JsonSerializer.Deserialize(json, ServoJsonContext.Default.ListString) ?? [];
    }

    public unsafe void ClearCache()
    {
        ThrowIfDisposed();
        ServoNative.servo_clear_cache((void*)_handle);
    }

    public static unsafe void InitializeGlAcceleratedMedia(
        GlDisplayType displayType, nuint displayPtr,
        GlApiType api,
        GlContextType contextType, nuint contextPtr)
    {
        ServoNative.servo_initialize_gl_accelerated_media(
            (byte)displayType, displayPtr, (byte)api, (byte)contextType, contextPtr);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCookieOpCompleteImpl(void* ud)
    {
        var handle = GCHandle.FromIntPtr((nint)ud);
        var tcs = (TaskCompletionSource<bool>)handle.Target!;
        handle.Free();
        tcs.SetResult(true);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCookiesForUrlImpl(void* ud, byte* json)
    {
        var handle = GCHandle.FromIntPtr((nint)ud);
        var tcs = (TaskCompletionSource<IReadOnlyList<Cookie>>)handle.Target!;
        handle.Free();
        var str = json == null ? "[]" : Marshal.PtrToStringUTF8((nint)json) ?? "[]";
        tcs.SetResult(Cookie.ParseJson(str));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnMemoryReportImpl(void* ud, byte* json)
    {
        var handle = GCHandle.FromIntPtr((nint)ud);
        var tcs = (TaskCompletionSource<string?>)handle.Target!;
        handle.Free();

        if (json == null)
            tcs.SetResult(null);
        else
            tcs.SetResult(Marshal.PtrToStringUTF8((nint)json));
    }

    internal nint Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle;
        }
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0)
        {
            ServoNative.servo_destroy((void*)_handle);
            _handle = 0;
        }
        _protocolRegistry?.Dispose();
        _protocolRegistry = null;
        if (_wakerHandle.IsAllocated) _wakerHandle.Free();
        if (_delegateHandle.IsAllocated) _delegateHandle.Free();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static unsafe string GetLastError()
    {
        var ptr = ServoNative.servo_last_error();
        return ptr == null ? "unknown error" : Marshal.PtrToStringUTF8((nint)ptr) ?? "unknown error";
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void WakeCallbackImpl(void* userData)
    {
        var handle = GCHandle.FromIntPtr((nint)userData);
        if (handle.Target is ServoEngine engine)
            engine.EventLoopWaker?.Invoke();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnErrorImpl(void* ud, byte code, byte* msg)
    {
        var h = GCHandle.FromIntPtr((nint)ud);
        if (h.Target is ServoEngine e)
            e.Error?.Invoke(e, new ServoErrorEventArgs(code, Marshal.PtrToStringUTF8((nint)msg) ?? ""));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnDevtoolsStartedImpl(void* ud, ushort port, byte* token)
    {
        var h = GCHandle.FromIntPtr((nint)ud);
        if (h.Target is ServoEngine e)
            e.DevtoolsStarted?.Invoke(e, new DevtoolsStartedEventArgs(port, Marshal.PtrToStringUTF8((nint)token) ?? ""));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnConsoleMessageImpl(void* ud, byte level, byte* msg)
    {
        var h = GCHandle.FromIntPtr((nint)ud);
        if (h.Target is ServoEngine e)
            e.ConsoleMessage?.Invoke(e, new ConsoleMessageEventArgs((ConsoleLogLevel)level, Marshal.PtrToStringUTF8((nint)msg) ?? ""));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLoadWebResourceImpl(void* ud, byte* url, byte* method, byte isMainFrame, byte isRedirect, nuint handle)
    {
        var h = GCHandle.FromIntPtr((nint)ud);
        if (h.Target is not ServoEngine e) return;
        var urlStr = Marshal.PtrToStringUTF8((nint)url) ?? "";
        var methodStr = Marshal.PtrToStringUTF8((nint)method) ?? "";
        var args = new WebResourceLoadEventArgs(urlStr, methodStr, isMainFrame != 0, isRedirect != 0, handle);
        var handler = e.WebResourceLoadRequested;
        if (handler != null)
            handler.Invoke(e, args);
        else
            args.Allow();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowNotificationImpl(void* ud, byte* title, byte* body)
    {
        var h = GCHandle.FromIntPtr((nint)ud);
        if (h.Target is not ServoEngine e) return;
        var t = Marshal.PtrToStringUTF8((nint)title) ?? "";
        var b = Marshal.PtrToStringUTF8((nint)body) ?? "";
        e.NotificationRequested?.Invoke(e, new NotificationEventArgs(t, b));
    }
}
