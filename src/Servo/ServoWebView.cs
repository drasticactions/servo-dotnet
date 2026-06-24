using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Servo;

public sealed class ServoWebView : IDisposable
{
    private nint _handle;
    private GCHandle _selfHandle;
    private bool _disposed;

    public event EventHandler? NewFrameReady;
    public event EventHandler<LoadStatusChangedEventArgs>? LoadStatusChanged;
    public event EventHandler<UrlChangedEventArgs>? UrlChanged;
    public event EventHandler<TitleChangedEventArgs>? TitleChanged;
    public event EventHandler<CursorChangedEventArgs>? CursorChanged;
    public event EventHandler<EventArgs>? FocusChanged;
    public event EventHandler<EventArgs>? AnimatingChanged;
    public event EventHandler? FaviconChanged;
    public event EventHandler<InputEventHandledEventArgs>? InputEventHandled;
    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;
    public event EventHandler? Closed;
    public event EventHandler<EventArgs>? FullscreenChanged;
    public event EventHandler<CrashedEventArgs>? Crashed;
    public event EventHandler<ConsoleMessageEventArgs>? WebViewConsoleMessage;
    public event EventHandler<AlertRequestEventArgs>? AlertRequested;
    public event EventHandler<ConfirmRequestEventArgs>? ConfirmRequested;
    public event EventHandler<PromptRequestEventArgs>? PromptRequested;
    public event EventHandler<SelectElementRequestEventArgs>? SelectElementRequested;
    public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested;
    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;
    public event EventHandler<PermissionRequestEventArgs>? PermissionRequested;
    public event EventHandler<UnloadRequestEventArgs>? UnloadRequested;
    public event EventHandler<MediaSessionEventArgs>? MediaSessionEvent;
    public event EventHandler<CreateNewWebViewRequestEventArgs>? CreateNewWebViewRequested;
    public event EventHandler<AuthenticationRequestEventArgs>? AuthenticationRequested;
    public event EventHandler? HideEmbedderControlRequested;
    public event EventHandler<WebResourceLoadEventArgs>? WebResourceLoadRequested;
    public event EventHandler<StatusTextChangedEventArgs>? StatusTextChanged;
    public event EventHandler? TraversalCompleted;
    public event EventHandler<MoveToRequestEventArgs>? MoveToRequested;
    public event EventHandler<ResizeToRequestEventArgs>? ResizeToRequested;
    public event EventHandler<ProtocolHandlerRequestEventArgs>? ProtocolHandlerRequested;
    public event EventHandler<NotificationEventArgs>? NotificationRequested;
    public event EventHandler<BluetoothDeviceSelectionEventArgs>? BluetoothDeviceSelectionRequested;
    public event EventHandler<FilePickerRequestEventArgs>? FilePickerRequested;
    public event EventHandler<ColorPickerRequestEventArgs>? ColorPickerRequested;
    public event EventHandler<InputMethodEventArgs>? InputMethodRequested;

    public unsafe ServoWebView(ServoEngine engine, RenderingContext renderingContext, string? initialUrl = null)
        : this(engine, renderingContext.Handle, initialUrl) { }

    private unsafe ServoWebView(ServoEngine engine, nint renderingCtxHandle, string? initialUrl)
    {
        _selfHandle = GCHandle.Alloc(this);
        try
        {
            var callbacks = BuildCallbacks();
            var clipboard = new ClipboardCallbacks();

            if (initialUrl != null)
            {
                var pUrl = Marshal.StringToCoTaskMemUTF8(initialUrl);
                try
                {
                    _handle = (nint)ServoNative.webview_new(
                        (void*)engine.Handle, (void*)renderingCtxHandle,
                        callbacks, clipboard, (byte*)pUrl);
                }
                finally { Marshal.FreeCoTaskMem(pUrl); }
            }
            else
            {
                _handle = (nint)ServoNative.webview_new(
                    (void*)engine.Handle, (void*)renderingCtxHandle,
                    callbacks, clipboard, null);
            }

            if (_handle == 0)
                throw new InvalidOperationException("Failed to create WebView");
        }
        catch
        {
            _selfHandle.Free();
            throw;
        }
    }

    /// <summary>
    /// Build a WebView from a CreateNewWebViewRequest handle (from a <see cref="CreateNewWebViewRequestEventArgs"/>).
    /// </summary>
    public unsafe ServoWebView(RenderingContext renderingContext, nuint requestHandle)
    {
        _selfHandle = GCHandle.Alloc(this);
        try
        {
            var callbacks = BuildCallbacks();
            var clipboard = new ClipboardCallbacks();

            _handle = (nint)ServoNative.create_new_webview_build(
                requestHandle, (void*)renderingContext.Handle,
                callbacks, clipboard);

            if (_handle == 0)
                throw new InvalidOperationException("Failed to create WebView from CreateNewWebViewRequest");
        }
        catch
        {
            _selfHandle.Free();
            throw;
        }
    }

    private unsafe WebViewCallbacks BuildCallbacks()
    {
        return new WebViewCallbacks
        {
            user_data = (void*)GCHandle.ToIntPtr(_selfHandle),
            on_new_frame_ready = &OnNewFrameReadyImpl,
            on_load_status_changed = &OnLoadStatusChangedImpl,
            on_url_changed = &OnUrlChangedImpl,
            on_title_changed = &OnTitleChangedImpl,
            on_cursor_changed = &OnCursorChangedImpl,
            on_focus_changed = &OnFocusChangedImpl,
            on_animating_changed = &OnAnimatingChangedImpl,
            on_favicon_changed = &OnFaviconChangedImpl,
            on_input_event_handled = &OnInputEventHandledImpl,
            on_history_changed = &OnHistoryChangedImpl,
            on_closed = &OnClosedImpl,
            on_fullscreen_changed = &OnFullscreenChangedImpl,
            on_crashed = &OnCrashedImpl,
            on_console_message = &OnConsoleMessageImpl,
            on_show_alert = &OnShowAlertImpl,
            on_show_confirm = &OnShowConfirmImpl,
            on_show_prompt = &OnShowPromptImpl,
            on_show_select_element = &OnShowSelectElementImpl,
            on_show_context_menu = &OnShowContextMenuImpl,
            on_request_navigation = &OnRequestNavigationImpl,
            on_request_permission = &OnRequestPermissionImpl,
            on_request_unload = &OnRequestUnloadImpl,
            on_media_session_event = &OnMediaSessionEventImpl,
            on_request_create_new_webview = &OnRequestCreateNewWebViewImpl,
            on_request_authentication = &OnRequestAuthenticationImpl,
            on_hide_embedder_control = &OnHideEmbedderControlImpl,
            on_load_web_resource = &OnLoadWebResourceImpl,
            on_status_text_changed = &OnStatusTextChangedImpl,
            on_traversal_complete = &OnTraversalCompleteImpl,
            on_request_move_to = &OnRequestMoveToImpl,
            on_request_resize_to = &OnRequestResizeToImpl,
            on_request_protocol_handler = &OnRequestProtocolHandlerImpl,
            on_show_notification = &OnShowNotificationImpl,
            on_show_bluetooth_device_dialog = &OnShowBluetoothDeviceDialogImpl,
            on_show_file_picker = &OnShowFilePickerImpl,
            on_show_color_picker = &OnShowColorPickerImpl,
            on_show_input_method = &OnShowInputMethodImpl,
        };
    }

    public unsafe void Load(string url)
    {
        ThrowIfDisposed();
        var pUrl = Marshal.StringToCoTaskMemUTF8(url);
        try { ServoNative.webview_load_url((void*)_handle, (byte*)pUrl); }
        finally { Marshal.FreeCoTaskMem(pUrl); }
    }

    public void Load(Uri uri) => Load(uri.AbsoluteUri);

    public unsafe void Reload()
    {
        ThrowIfDisposed();
        ServoNative.webview_reload((void*)_handle);
    }

    public unsafe void GoBack(int steps = 1)
    {
        ThrowIfDisposed();
        ServoNative.webview_go_back((void*)_handle, (nuint)Math.Max(1, steps));
    }

    public unsafe void GoForward(int steps = 1)
    {
        ThrowIfDisposed();
        ServoNative.webview_go_forward((void*)_handle, (nuint)Math.Max(1, steps));
    }

    public unsafe void Paint()
    {
        ThrowIfDisposed();
        ServoNative.webview_paint((void*)_handle);
    }

    public unsafe void Resize(uint width, uint height)
    {
        ThrowIfDisposed();
        ServoNative.webview_resize((void*)_handle, width, height);
    }

    public unsafe void SetHidpiScale(float scale)
    {
        ThrowIfDisposed();
        ServoNative.webview_set_hidpi_scale((void*)_handle, scale);
    }

    public unsafe void Focus()
    {
        ThrowIfDisposed();
        ServoNative.webview_focus((void*)_handle);
    }

    public unsafe void Blur()
    {
        ThrowIfDisposed();
        ServoNative.webview_blur((void*)_handle);
    }

    public unsafe void Show()
    {
        ThrowIfDisposed();
        ServoNative.webview_show((void*)_handle);
    }

    public unsafe void Hide()
    {
        ThrowIfDisposed();
        ServoNative.webview_hide((void*)_handle);
    }

    public unsafe string? Url
    {
        get
        {
            ThrowIfDisposed();
            var ptr = ServoNative.webview_get_url((void*)_handle);
            if (ptr == null) return null;
            var url = Marshal.PtrToStringUTF8((nint)ptr);
            ServoNative.servo_free_string(ptr);
            return url;
        }
    }

    public unsafe string? Title
    {
        get
        {
            ThrowIfDisposed();
            var ptr = ServoNative.webview_get_title((void*)_handle);
            if (ptr == null) return null;
            var title = Marshal.PtrToStringUTF8((nint)ptr);
            ServoNative.servo_free_string(ptr);
            return title;
        }
    }

    public unsafe LoadStatus Status
    {
        get
        {
            ThrowIfDisposed();
            return (LoadStatus)ServoNative.webview_get_load_status((void*)_handle);
        }
    }

    public unsafe ServoCursor Cursor
    {
        get
        {
            ThrowIfDisposed();
            return (ServoCursor)ServoNative.webview_get_cursor((void*)_handle);
        }
    }

    public unsafe bool IsFocused
    {
        get
        {
            ThrowIfDisposed();
            return ServoNative.webview_is_focused((void*)_handle) != 0;
        }
    }

    public unsafe bool IsAnimating
    {
        get
        {
            ThrowIfDisposed();
            return ServoNative.webview_is_animating((void*)_handle) != 0;
        }
    }

    public unsafe bool CanGoBack
    {
        get
        {
            ThrowIfDisposed();
            return ServoNative.webview_can_go_back((void*)_handle) != 0;
        }
    }

    public unsafe bool CanGoForward
    {
        get
        {
            ThrowIfDisposed();
            return ServoNative.webview_can_go_forward((void*)_handle) != 0;
        }
    }

    public unsafe ulong SendMouseButton(MouseButtonAction action, ServoMouseButton button, float x, float y)
    {
        ThrowIfDisposed();
        return ServoNative.webview_send_mouse_button((void*)_handle, (byte)action, (ushort)button, x, y);
    }

    public unsafe ulong SendMouseMove(float x, float y)
    {
        ThrowIfDisposed();
        return ServoNative.webview_send_mouse_move((void*)_handle, x, y);
    }

    public unsafe ulong SendMouseLeftViewport()
    {
        ThrowIfDisposed();
        return ServoNative.webview_send_mouse_left_viewport((void*)_handle);
    }

    /// <summary>
    /// Send a keyboard event.
    /// </summary>
    /// <param name="down">True for key down, false for key up.</param>
    /// <param name="keyChar">The UTF-32 codepoint of the character, or 0 for non-printable keys.</param>
    /// <param name="keyCode">DOM key code string (e.g. "KeyA", "Enter"), or null.</param>
    /// <param name="modifiers">Active keyboard modifiers.</param>
    public unsafe ulong SendKeyEvent(bool down, uint keyChar, string? keyCode, KeyModifiers modifiers = KeyModifiers.None)
    {
        ThrowIfDisposed();
        if (keyCode != null)
        {
            var pKeyCode = Marshal.StringToCoTaskMemUTF8(keyCode);
            try
            {
                return ServoNative.webview_send_key_event(
                    (void*)_handle, (byte)(down ? 0 : 1), keyChar, (byte*)pKeyCode, (uint)modifiers);
            }
            finally { Marshal.FreeCoTaskMem(pKeyCode); }
        }
        else
        {
            return ServoNative.webview_send_key_event(
                (void*)_handle, (byte)(down ? 0 : 1), keyChar, null, (uint)modifiers);
        }
    }

    public unsafe ulong SendWheel(double deltaX, double deltaY, WheelMode mode, float x, float y, double deltaZ = 0)
    {
        ThrowIfDisposed();
        return ServoNative.webview_send_wheel((void*)_handle, deltaX, deltaY, deltaZ, (byte)mode, x, y);
    }

    public unsafe void SendScroll(float deltaX, float deltaY, float pointX, float pointY)
    {
        ThrowIfDisposed();
        ServoNative.webview_send_scroll((void*)_handle, deltaX, deltaY, pointX, pointY);
    }

    public unsafe ulong SendTouch(TouchEventType eventType, int touchId, float x, float y)
    {
        ThrowIfDisposed();
        return ServoNative.webview_send_touch((void*)_handle, (byte)eventType, touchId, x, y);
    }

    public unsafe ulong SendEditingAction(EditingAction action)
    {
        ThrowIfDisposed();
        return ServoNative.webview_send_editing_action((void*)_handle, (byte)action);
    }

    public unsafe Task<string> EvaluateJavaScriptAsync(string script)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        var pScript = Marshal.StringToCoTaskMemUTF8(script);
        try
        {
            ServoNative.webview_evaluate_javascript(
                (void*)_handle, (byte*)pScript,
                &OnJsCallbackImpl,
                (void*)GCHandle.ToIntPtr(handle));
        }
        finally { Marshal.FreeCoTaskMem(pScript); }
        return tcs.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnJsCallbackImpl(void* ud, byte* result, byte* error)
    {
        var handle = GCHandle.FromIntPtr((nint)ud);
        var tcs = (TaskCompletionSource<string>)handle.Target!;
        handle.Free();

        if (error != null)
            tcs.SetException(new InvalidOperationException(Marshal.PtrToStringUTF8((nint)error)));
        else
            tcs.SetResult(Marshal.PtrToStringUTF8((nint)result) ?? "undefined");
    }

    public unsafe Task<PixelData?> TakeScreenshotAsync()
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<PixelData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        ServoNative.webview_take_screenshot(
            (void*)_handle,
            &OnScreenshotImpl,
            (void*)GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnScreenshotImpl(void* ud, byte* pixels, uint width, uint height, nuint len)
    {
        var handle = GCHandle.FromIntPtr((nint)ud);
        var tcs = (TaskCompletionSource<PixelData?>)handle.Target!;
        handle.Free();

        if (pixels == null || len == 0)
        {
            tcs.SetResult(null);
            return;
        }
        var data = new byte[len];
        Marshal.Copy((nint)pixels, data, 0, (int)len);
        tcs.SetResult(new PixelData(data, width, height));
    }

    public unsafe FaviconData? GetFavicon()
    {
        ThrowIfDisposed();
        uint w, h;
        byte fmt;
        nuint len;
        var ptr = ServoNative.webview_get_favicon((void*)_handle, &w, &h, &fmt, &len);
        if (ptr == null || len == 0) return null;
        var data = new byte[len];
        Marshal.Copy((nint)ptr, data, 0, (int)len);
        ServoNative.servo_free_bytes(ptr, len);
        return new FaviconData(data, w, h, (PixelFormat)fmt);
    }

    public unsafe float PageZoom
    {
        get { ThrowIfDisposed(); return ServoNative.webview_get_page_zoom((void*)_handle); }
        set { ThrowIfDisposed(); ServoNative.webview_set_page_zoom((void*)_handle, value); }
    }

    public unsafe void ExitFullscreen()
    {
        ThrowIfDisposed();
        ServoNative.webview_exit_fullscreen((void*)_handle);
    }

    public unsafe void NotifyThemeChange(ServoTheme theme)
    {
        ThrowIfDisposed();
        ServoNative.webview_notify_theme_change((void*)_handle, (byte)theme);
    }

    public unsafe void NotifyMediaSessionAction(MediaSessionAction action)
    {
        ThrowIfDisposed();
        ServoNative.webview_notify_media_session_action((void*)_handle, (byte)action);
    }

    public unsafe void AdjustPinchZoom(float delta, float centerX, float centerY)
    {
        ThrowIfDisposed();
        ServoNative.webview_adjust_pinch_zoom((void*)_handle, delta, centerX, centerY);
    }

    public unsafe float PinchZoom
    {
        get
        {
            ThrowIfDisposed();
            return ServoNative.webview_get_pinch_zoom((void*)_handle);
        }
    }

    public unsafe void SendImeComposition(CompositionState state, string data)
    {
        ThrowIfDisposed();
        var pData = Marshal.StringToCoTaskMemUTF8(data);
        try { ServoNative.webview_send_ime_composition((void*)_handle, (byte)state, (byte*)pData); }
        finally { Marshal.FreeCoTaskMem(pData); }
    }

    public unsafe void SendImeDismissed()
    {
        ThrowIfDisposed();
        ServoNative.webview_send_ime_dismissed((void*)_handle);
    }

    public unsafe void ToggleWebRenderDebugging(WebRenderDebugOption option)
    {
        ThrowIfDisposed();
        ServoNative.webview_toggle_webrender_debugging((void*)_handle, (byte)option);
    }

    public unsafe void CaptureWebRender()
    {
        ThrowIfDisposed();
        ServoNative.webview_capture_webrender((void*)_handle);
    }

    public unsafe void ToggleSamplingProfiler(TimeSpan rate, TimeSpan maxDuration)
    {
        ThrowIfDisposed();
        ServoNative.webview_toggle_sampling_profiler(
            (void*)_handle, (ulong)rate.TotalMilliseconds, (ulong)maxDuration.TotalMilliseconds);
    }

    public unsafe void SetThrottled(bool throttled)
    {
        ThrowIfDisposed();
        ServoNative.webview_set_throttled((void*)_handle, (byte)(throttled ? 1 : 0));
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0)
        {
            ServoNative.webview_destroy((void*)_handle);
            _handle = 0;
        }
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static unsafe bool TryGet(void* ud, out ServoWebView wv)
    {
        var h = GCHandle.FromIntPtr((nint)ud);
        if (h.Target is ServoWebView w) { wv = w; return true; }
        wv = null!; return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNewFrameReadyImpl(void* ud)
    { if (TryGet(ud, out var w)) w.NewFrameReady?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLoadStatusChangedImpl(void* ud, byte status)
    { if (TryGet(ud, out var w)) w.LoadStatusChanged?.Invoke(w, new LoadStatusChangedEventArgs((LoadStatus)status)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnUrlChangedImpl(void* ud, byte* url)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)url);
        if (s != null) w.UrlChanged?.Invoke(w, new UrlChangedEventArgs(s));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnTitleChangedImpl(void* ud, byte* title)
    {
        if (!TryGet(ud, out var w)) return;
        var s = title == null ? null : Marshal.PtrToStringUTF8((nint)title);
        w.TitleChanged?.Invoke(w, new TitleChangedEventArgs(s));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCursorChangedImpl(void* ud, byte cursor)
    { if (TryGet(ud, out var w)) w.CursorChanged?.Invoke(w, new CursorChangedEventArgs((ServoCursor)cursor)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnFocusChangedImpl(void* ud, byte focused)
    { if (TryGet(ud, out var w)) w.FocusChanged?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnAnimatingChangedImpl(void* ud, byte animating)
    { if (TryGet(ud, out var w)) w.AnimatingChanged?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnFaviconChangedImpl(void* ud)
    { if (TryGet(ud, out var w)) w.FaviconChanged?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnInputEventHandledImpl(void* ud, ulong eventId, byte result)
    { if (TryGet(ud, out var w)) w.InputEventHandled?.Invoke(w, new InputEventHandledEventArgs(eventId, result)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnHistoryChangedImpl(void* ud, byte* urlsJson, nuint current, nuint total)
    {
        if (!TryGet(ud, out var w)) return;
        var json = Marshal.PtrToStringUTF8((nint)urlsJson) ?? "[]";
        var urls = HistoryChangedEventArgs.ParseUrlsJson(json);
        w.HistoryChanged?.Invoke(w, new HistoryChangedEventArgs(urls, (int)current, (int)total));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnClosedImpl(void* ud)
    { if (TryGet(ud, out var w)) w.Closed?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnFullscreenChangedImpl(void* ud, byte fullscreen)
    { if (TryGet(ud, out var w)) w.FullscreenChanged?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCrashedImpl(void* ud, byte* reason, byte* backtrace)
    {
        if (!TryGet(ud, out var w)) return;
        var r = Marshal.PtrToStringUTF8((nint)reason) ?? "unknown";
        var bt = backtrace == null ? null : Marshal.PtrToStringUTF8((nint)backtrace);
        w.Crashed?.Invoke(w, new CrashedEventArgs(r, bt));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnConsoleMessageImpl(void* ud, byte level, byte* msg)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)msg) ?? "";
        w.WebViewConsoleMessage?.Invoke(w, new ConsoleMessageEventArgs((ConsoleLogLevel)level, s));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowAlertImpl(void* ud, byte* msg, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)msg) ?? "";
        var args = new AlertRequestEventArgs(s, handle);
        if (w.AlertRequested != null) w.AlertRequested.Invoke(w, args);
        else args.Dismiss();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowConfirmImpl(void* ud, byte* msg, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)msg) ?? "";
        var args = new ConfirmRequestEventArgs(s, handle);
        if (w.ConfirmRequested != null) w.ConfirmRequested.Invoke(w, args);
        else args.Cancel();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowPromptImpl(void* ud, byte* msg, byte* defaultVal, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var m = Marshal.PtrToStringUTF8((nint)msg) ?? "";
        var d = defaultVal == null ? "" : Marshal.PtrToStringUTF8((nint)defaultVal) ?? "";
        var args = new PromptRequestEventArgs(m, d, handle);
        if (w.PromptRequested != null) w.PromptRequested.Invoke(w, args);
        else args.Cancel();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowSelectElementImpl(void* ud, byte* optionsJson, long selectedId,
        int posX, int posY, int posW, int posH, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var json = Marshal.PtrToStringUTF8((nint)optionsJson) ?? "[]";
        var options = SelectElementRequestEventArgs.ParseOptionsJson(json);
        int? selected = selectedId >= 0 ? (int)selectedId : null;
        var args = new SelectElementRequestEventArgs(options, selected, posX, posY, posW, posH, handle);
        var handler = w.SelectElementRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Dismiss(); // no handler subscribed, dismiss immediately
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowContextMenuImpl(void* ud, byte* itemsJson, int posX, int posY, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var json = Marshal.PtrToStringUTF8((nint)itemsJson) ?? "[]";
        var items = ContextMenuRequestEventArgs.ParseItemsJson(json);
        var args = new ContextMenuRequestEventArgs(items, posX, posY, handle);
        var handler = w.ContextMenuRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Dismiss();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestNavigationImpl(void* ud, byte* url, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)url) ?? "";
        var args = new NavigationRequestEventArgs(s, handle);
        w.NavigationRequested?.Invoke(w, args);
        args.Allow(); // default: allow if unhandled
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestPermissionImpl(void* ud, byte feature, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var args = new PermissionRequestEventArgs((PermissionFeature)feature, handle);
        var handler = w.PermissionRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Deny(); // default: deny if unhandled
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestUnloadImpl(void* ud, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var args = new UnloadRequestEventArgs(handle);
        w.UnloadRequested?.Invoke(w, args);
        args.Allow(); // default: allow if unhandled
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnMediaSessionEventImpl(void* ud, byte eventType, byte* json)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)json) ?? "{}";
        w.MediaSessionEvent?.Invoke(w, new MediaSessionEventArgs(eventType, s));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestCreateNewWebViewImpl(void* ud, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var args = new CreateNewWebViewRequestEventArgs(handle);
        var handler = w.CreateNewWebViewRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Dismiss(); // no handler, dismiss to clean up the native request
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestAuthenticationImpl(void* ud, byte* url, byte forProxy, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var urlStr = Marshal.PtrToStringUTF8((nint)url) ?? "";
        var args = new AuthenticationRequestEventArgs(urlStr, forProxy != 0, handle);
        var handler = w.AuthenticationRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Dismiss(); // no handler, dismiss (no credentials)
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnHideEmbedderControlImpl(void* ud)
    {
        if (TryGet(ud, out var w))
            w.HideEmbedderControlRequested?.Invoke(w, EventArgs.Empty);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLoadWebResourceImpl(void* ud, byte* url, byte* method, byte isMainFrame, byte isRedirect, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var urlStr = Marshal.PtrToStringUTF8((nint)url) ?? "";
        var methodStr = Marshal.PtrToStringUTF8((nint)method) ?? "";
        var args = new WebResourceLoadEventArgs(urlStr, methodStr, isMainFrame != 0, isRedirect != 0, handle);
        var handler = w.WebResourceLoadRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Allow();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnStatusTextChangedImpl(void* ud, byte* status)
    {
        if (!TryGet(ud, out var w)) return;
        var s = status == null ? null : Marshal.PtrToStringUTF8((nint)status);
        w.StatusTextChanged?.Invoke(w, new StatusTextChangedEventArgs(s));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnTraversalCompleteImpl(void* ud)
    { if (TryGet(ud, out var w)) w.TraversalCompleted?.Invoke(w, EventArgs.Empty); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestMoveToImpl(void* ud, int x, int y)
    { if (TryGet(ud, out var w)) w.MoveToRequested?.Invoke(w, new MoveToRequestEventArgs(x, y)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestResizeToImpl(void* ud, int width, int height)
    { if (TryGet(ud, out var w)) w.ResizeToRequested?.Invoke(w, new ResizeToRequestEventArgs(width, height)); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnRequestProtocolHandlerImpl(void* ud, byte* scheme, byte* url, byte regType, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var s = Marshal.PtrToStringUTF8((nint)scheme) ?? "";
        var u = Marshal.PtrToStringUTF8((nint)url) ?? "";
        var args = new ProtocolHandlerRequestEventArgs(s, u, (ProtocolHandlerAction)regType, handle);
        var handler = w.ProtocolHandlerRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Deny(); // default: deny
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowNotificationImpl(void* ud, byte* title, byte* body)
    {
        if (!TryGet(ud, out var w)) return;
        var t = Marshal.PtrToStringUTF8((nint)title) ?? "";
        var b = Marshal.PtrToStringUTF8((nint)body) ?? "";
        w.NotificationRequested?.Invoke(w, new NotificationEventArgs(t, b));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowBluetoothDeviceDialogImpl(void* ud, byte* devicesJson, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var json = Marshal.PtrToStringUTF8((nint)devicesJson) ?? "[]";
        var devices = BluetoothDeviceSelectionEventArgs.ParseDevicesJson(json);
        var args = new BluetoothDeviceSelectionEventArgs(devices, handle);
        var handler = w.BluetoothDeviceSelectionRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Cancel();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowFilePickerImpl(void* ud, byte* filterPatternsJson, byte allowMultiple, byte* currentPathsJson, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        var patterns = FilePickerRequestEventArgs.ParseJsonArray(
            Marshal.PtrToStringUTF8((nint)filterPatternsJson) ?? "[]");
        var current = FilePickerRequestEventArgs.ParseJsonArray(
            Marshal.PtrToStringUTF8((nint)currentPathsJson) ?? "[]");
        var args = new FilePickerRequestEventArgs(patterns, allowMultiple != 0, current, handle);
        var handler = w.FilePickerRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Dismiss(); // no handler, dismiss
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowColorPickerImpl(void* ud, byte hasColor, byte r, byte g, byte b,
        int posX, int posY, int posW, int posH, nuint handle)
    {
        if (!TryGet(ud, out var w)) return;
        RgbColor? color = hasColor != 0 ? new RgbColor(r, g, b) : null;
        var args = new ColorPickerRequestEventArgs(color, posX, posY, posW, posH, handle);
        var handler = w.ColorPickerRequested;
        if (handler != null)
            handler.Invoke(w, args);
        else
            args.Dismiss(); // no handler, dismiss (sends current color)
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnShowInputMethodImpl(void* ud, byte imeType, byte* text,
        long insertionPoint, byte multiline, byte allowVirtualKeyboard,
        int posX, int posY, int posW, int posH)
    {
        if (!TryGet(ud, out var w)) return;
        var t = Marshal.PtrToStringUTF8((nint)text) ?? "";
        int? ip = insertionPoint >= 0 ? (int)insertionPoint : null;
        var args = new InputMethodEventArgs(
            (InputMethodType)imeType, t, ip,
            multiline != 0, allowVirtualKeyboard != 0,
            posX, posY, posW, posH);
        w.InputMethodRequested?.Invoke(w, args);
    }
}
