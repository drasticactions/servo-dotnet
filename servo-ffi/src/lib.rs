#[cfg(target_os = "macos")]
mod fence_worker;
mod hardware_rendering;
mod resources;
mod shared_event;
mod types;
mod waker;

use std::ffi::{CStr, CString, c_char, c_void};
use std::future;
use std::panic;
use std::pin::Pin;
use std::rc::Rc;
use std::cell::RefCell;

use servo::{
    AllowOrDenyRequest, AuthenticationRequest, ClipboardDelegate, ConsoleLogLevel,
    CreateNewWebViewRequest, Cursor, InputEvent, InputEventId, InputEventResult,
    JSValue, LoadStatus, MediaSessionEvent, NavigationRequest, PermissionFeature, PermissionRequest,
    PrefValue, RenderingContext, Scroll, ScreenGeometry, Servo, ServoBuilder,
    ServoDelegate, ServoError, SoftwareRenderingContext, StringRequest, WebView,
    WebViewBuilder, WebViewDelegate, DevicePoint, DeviceIntSize, DeviceIntRect,
    DeviceIntPoint, DeviceVector2D, WebViewPoint, EmbedderControl, SimpleDialog,
    SelectElement, ContextMenu, ContextMenuAction,
    UserContentManager, UserScript, StorageType,
    SiteData, ColorPicker, FilePicker, RgbColor,
    Theme, MediaSessionActionType, InputMethodType,
    CompositionEvent, CompositionState, ImeEvent,
    WebRenderDebugOption,
    MediaGlApi, MediaGlContext, MediaNativeDisplay,
};
use servo::EmbedderControlId;
use servo::CookieSource;
use servo::user_contents::UserStyleSheet;
use servo::{WebResourceLoad, WebResourceResponse};
use servo::{Notification, RegisterOrUnregister, TraversalId, BluetoothDeviceSelectionRequest};
use servo::protocol_handler::ProtocolHandlerRegistration;
use servo::protocol_handler::{
    ProtocolHandler, ProtocolRegistry, Request as NetRequest,
    Response as NetResponse, ResponseBody, ResourceFetchTiming,
    DoneChannel, FetchContext, NetworkError,
};
use servo::SelectElementOptionOrOptgroup;
use servo::ContextMenuItem;
use servo::input_events::{
    EditingActionEvent, KeyboardEvent, MouseButton, MouseButtonAction, MouseButtonEvent,
    MouseLeftViewportEvent, MouseMoveEvent, TouchEvent, TouchEventType, TouchId,
    TouchPointerType, WheelDelta, WheelEvent, WheelMode,
};
use std::path::PathBuf;
use cookie::Cookie;
use url::Url;

use crate::types::*;
use crate::waker::FfiEventLoopWaker;

thread_local! {
    static LAST_ERROR: RefCell<Option<CString>> = const { RefCell::new(None) };
}

fn set_last_error(msg: String) {
    LAST_ERROR.with(|e| {
        *e.borrow_mut() = CString::new(msg).ok();
    });
}

fn ffi_catch<F, T>(f: F) -> Option<T>
where
    F: FnOnce() -> T + panic::UnwindSafe,
{
    match panic::catch_unwind(f) {
        Ok(val) => Some(val),
        Err(e) => {
            let msg = if let Some(s) = e.downcast_ref::<&str>() {
                s.to_string()
            } else if let Some(s) = e.downcast_ref::<String>() {
                s.clone()
            } else {
                "unknown panic".to_string()
            };
            set_last_error(msg);
            None
        },
    }
}

#[inline]
fn wv_ref(handle: *mut c_void) -> Option<&'static WebViewHandle> {
    if handle.is_null() { None } else { Some(unsafe { &*(handle as *mut WebViewHandle) }) }
}


#[unsafe(no_mangle)]
pub extern "C" fn servo_last_error() -> *const c_char {
    LAST_ERROR.with(|e| {
        match e.borrow().as_ref() {
            Some(s) => s.as_ptr(),
            None => std::ptr::null(),
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        unsafe { drop(CString::from_raw(ptr)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_free_bytes(ptr: *mut u8, len: usize) {
    if !ptr.is_null() {
        unsafe { drop(Vec::from_raw_parts(ptr, len, len)); }
    }
}

struct FfiProtocolHandler {
    callbacks: CProtocolHandler,
    privileged: &'static [&'static str],
}

impl ProtocolHandler for FfiProtocolHandler {
    fn privileged_paths(&self) -> &'static [&'static str] {
        self.privileged
    }

    fn load<'a>(
        &'a self,
        request: &'a mut NetRequest,
        _done_chan: &mut DoneChannel,
        _context: &FetchContext,
    ) -> Pin<Box<dyn std::future::Future<Output = NetResponse> + Send + 'a>> {
        let url = request.current_url();
        let url_str = match CString::new(url.as_str()) {
            Ok(s) => s,
            Err(_) => {
                return Box::pin(future::ready(NetResponse::network_error(
                    NetworkError::ResourceLoadError("Invalid URL".into()),
                )));
            }
        };

        let load_fn = match self.callbacks.load {
            Some(f) => f,
            None => {
                return Box::pin(future::ready(NetResponse::network_error(
                    NetworkError::ResourceLoadError("No load callback".into()),
                )));
            }
        };

        let mut c_response = CProtocolResponse {
            body: std::ptr::null(),
            body_len: 0,
            content_type: std::ptr::null(),
            status_code: 200,
        };

        let ok = load_fn(url_str.as_ptr(), self.callbacks.user_data, &mut c_response);
        if ok == 0 || c_response.body.is_null() {
            return Box::pin(future::ready(NetResponse::network_error(
                NetworkError::ResourceLoadError("Protocol handler returned error".into()),
            )));
        }

        // Reject unreasonably large responses (256 MB) to prevent OOM from malformed data
        // TODO: Memo to me later, is there a usecase for larger responses?

        const MAX_BODY_LEN: usize = 256 * 1024 * 1024;
        if c_response.body_len > MAX_BODY_LEN {
            return Box::pin(future::ready(NetResponse::network_error(
                NetworkError::ResourceLoadError("Protocol handler response too large".into()),
            )));
        }

        let body = unsafe { std::slice::from_raw_parts(c_response.body, c_response.body_len) }.to_vec();
        let content_type = if !c_response.content_type.is_null() {
            unsafe { CStr::from_ptr(c_response.content_type) }
                .to_str()
                .unwrap_or("application/octet-stream")
                .to_string()
        } else {
            "application/octet-stream".to_string()
        };

        let mut response = NetResponse::new(
            url,
            ResourceFetchTiming::new(request.timing_type()),
        );
        *response.body.lock() = ResponseBody::Done(body);
        if let Ok(val) = http::HeaderValue::from_str(&content_type) {
            response.headers.insert(http::header::CONTENT_TYPE, val);
        }
        response.status = http::StatusCode::from_u16(c_response.status_code)
            .unwrap_or(http::StatusCode::OK)
            .into();

        Box::pin(future::ready(response))
    }

    fn is_fetchable(&self) -> bool {
        self.callbacks.is_fetchable != 0
    }

    fn is_secure(&self) -> bool {
        self.callbacks.is_secure != 0
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_protocol_registry_new() -> *mut c_void {
    Box::into_raw(Box::new(ProtocolRegistry::default())) as *mut c_void
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_protocol_registry_register(
    registry: *mut c_void,
    scheme: *const c_char,
    handler: CProtocolHandler,
) -> u8 {
    if registry.is_null() || scheme.is_null() { return 3; }
    let registry = unsafe { &mut *(registry as *mut ProtocolRegistry) };
    let scheme_str = match unsafe { CStr::from_ptr(scheme) }.to_str() {
        Ok(s) => s,
        Err(_) => return 3,
    };
    // Box::leak is intentional: the ProtocolHandler trait requires &'static [&'static str]
    // for privileged_paths(). Protocol handlers are registered once at startup, so these
    // allocations live for the program's lifetime.
    // TODO: Is that right?
    let mut privileged_strs = Vec::new();
    if !handler.privileged_paths.is_null() && handler.privileged_paths_len > 0 {
        let ptrs = unsafe {
            std::slice::from_raw_parts(handler.privileged_paths, handler.privileged_paths_len)
        };
        for &ptr in ptrs {
            if let Ok(s) = unsafe { CStr::from_ptr(ptr) }.to_str() {
                privileged_strs.push(Box::leak(s.to_string().into_boxed_str()) as &'static str);
            }
        }
    }
    let privileged: &'static [&'static str] = Box::leak(privileged_strs.into_boxed_slice());
    let ffi_handler = FfiProtocolHandler { callbacks: handler, privileged };
    match registry.register(scheme_str, ffi_handler) {
        Ok(()) => 0,
        Err(_) => {
            1
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_protocol_registry_destroy(registry: *mut c_void) {
    if !registry.is_null() {
        unsafe { drop(Box::from_raw(registry as *mut ProtocolRegistry)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_new(
    waker: CEventLoopWaker,
    resource_path: *const c_char,
    protocol_registry: *mut c_void,
) -> *mut c_void {
    let result = ffi_catch(std::panic::AssertUnwindSafe(|| {
        if resource_path.is_null() {
            resources::RESOURCE_READER.init_from_exe_dir();
        } else {
            let path = unsafe { CStr::from_ptr(resource_path) }
                .to_str()
                .expect("resource_path must be valid UTF-8");
            resources::RESOURCE_READER.init_from_path(std::path::PathBuf::from(path));
        };

        let ffi_waker = FfiEventLoopWaker::new(waker);
        let mut builder = ServoBuilder::default()
            .event_loop_waker(Box::new(ffi_waker));

        if !protocol_registry.is_null() {
            let registry = unsafe { *Box::from_raw(protocol_registry as *mut ProtocolRegistry) };
            builder = builder.protocol_registry(registry);
        }

        let servo = builder.build();
        servo.setup_logging();

        Box::into_raw(Box::new(servo)) as *mut c_void
    }));
    result.unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut Servo)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_spin_event_loop(handle: *mut c_void) {
    if !handle.is_null() {
        let servo = unsafe { &*(handle as *mut Servo) };
        servo.spin_event_loop();
    }
}

struct FfiServoDelegate {
    callbacks: ServoCallbacks,
}

impl FfiServoDelegate {
    fn ud(&self) -> *mut c_void { self.callbacks.user_data }
}

impl ServoDelegate for FfiServoDelegate {
    fn notify_error(&self, error: ServoError) {
        if let Some(cb) = self.callbacks.on_error {
            let (code, msg) = match &error {
                ServoError::LostConnectionWithBackend => (0u8, "Lost connection with backend".to_string()),
                ServoError::DevtoolsFailedToStart => (1, "DevTools failed to start".to_string()),
                ServoError::ResponseFailedToSend(e) => (2, format!("Response failed to send: {e:?}")),
            };
            if let Ok(c) = CString::new(msg) { cb(self.ud(), code, c.as_ptr()); }
        }
    }

    fn notify_devtools_server_started(&self, port: u16, token: String) {
        if let Some(cb) = self.callbacks.on_devtools_started {
            if let Ok(c) = CString::new(token) { cb(self.ud(), port, c.as_ptr()); }
        }
    }

    fn show_console_message(&self, level: ConsoleLogLevel, message: String) {
        if let Some(cb) = self.callbacks.on_console_message {
            let level_u8 = console_level_to_u8(level);
            if let Ok(c) = CString::new(message) { cb(self.ud(), level_u8, c.as_ptr()); }
        }
    }

    fn request_devtools_connection(&self, request: AllowOrDenyRequest) {
        if let Some(cb) = self.callbacks.on_request_devtools_connection {
            let result = cb(self.ud());
            if result == 0 { request.allow(); } else { request.deny(); }
        }
    }

    fn load_web_resource(&self, load: WebResourceLoad) {
        if let Some(cb) = self.callbacks.on_load_web_resource {
            fire_web_resource_callback(cb, self.ud(), load);
        }
    }

    fn show_notification(&self, notification: Notification) {
        if let Some(cb) = self.callbacks.on_show_notification {
            let title_ok = CString::new(notification.title);
            let body_ok = CString::new(notification.body);
            if let (Ok(t), Ok(b)) = (title_ok, body_ok) {
                cb(self.ud(), t.as_ptr(), b.as_ptr());
            }
        }
    }
}

fn console_level_to_u8(level: ConsoleLogLevel) -> u8 {
    match level {
        ConsoleLogLevel::Log => 0u8,
        ConsoleLogLevel::Debug => 1,
        ConsoleLogLevel::Info => 2,
        ConsoleLogLevel::Warn => 3,
        ConsoleLogLevel::Error => 4,
        ConsoleLogLevel::Trace => 5,
        ConsoleLogLevel::Dir => 6,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_set_delegate(handle: *mut c_void, callbacks: ServoCallbacks) {
    if handle.is_null() { return; }
    let servo = unsafe { &*(handle as *mut Servo) };
    servo.set_delegate(Rc::new(FfiServoDelegate { callbacks }));
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_set_preference(
    handle: *mut c_void, name: *const c_char, value: *const c_char,
) {
    if handle.is_null() || name.is_null() || value.is_null() { return; }
    let servo = unsafe { &*(handle as *mut Servo) };
    let name_str = unsafe { CStr::from_ptr(name) }.to_str().unwrap_or_default();
    let value_str = unsafe { CStr::from_ptr(value) }.to_str().unwrap_or_default();

    let pref_value = match value_str {
        "true" => PrefValue::Bool(true),
        "false" => PrefValue::Bool(false),
        s if s.parse::<i64>().is_ok() => PrefValue::Int(s.parse().unwrap()),
        s if s.parse::<f64>().is_ok() => PrefValue::Float(s.parse().unwrap()),
        s => PrefValue::Str(s.to_string()),
    };
    servo.set_preference(name_str, pref_value);
}

struct RenderingContextHandle {
    rc: Rc<dyn RenderingContext>,
    hardware: Option<Rc<crate::hardware_rendering::HardwareRenderingContext>>,
}

unsafe fn rc_from_handle<'a>(handle: *mut c_void) -> &'a Rc<dyn RenderingContext> {
    unsafe { &(*(handle as *mut RenderingContextHandle)).rc }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_new_software(width: u32, height: u32) -> *mut c_void {
    let result = ffi_catch(std::panic::AssertUnwindSafe(|| {
        let ctx = SoftwareRenderingContext::new(dpi::PhysicalSize::new(width, height))
            .expect("Failed to create SoftwareRenderingContext");
        let rc: Rc<dyn RenderingContext> = Rc::new(ctx);
        Box::into_raw(Box::new(RenderingContextHandle { rc, hardware: None })) as *mut c_void
    }));
    result.unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_new_hardware(width: u32, height: u32) -> *mut c_void {
    let result = ffi_catch(std::panic::AssertUnwindSafe(|| {
        let ctx = crate::hardware_rendering::HardwareRenderingContext::new(
            dpi::PhysicalSize::new(width, height),
        ).expect("Failed to create HardwareRenderingContext");
        let hw = Rc::new(ctx);
        let rc: Rc<dyn RenderingContext> = hw.clone();
        Box::into_raw(Box::new(RenderingContextHandle { rc, hardware: Some(hw) })) as *mut c_void
    }));
    result.unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut RenderingContextHandle)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_frame_export_kind(handle: *mut c_void) -> u32 {
    if handle.is_null() {
        return 0;
    }
    let h = unsafe { &*(handle as *mut RenderingContextHandle) };
    h.hardware.as_ref().map_or(0, |hw| hw.frame_export_kind())
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_acquire_frame(
    handle: *mut c_void,
    out_info: *mut ServoFrameInfo,
) -> u8 {
    if handle.is_null() || out_info.is_null() {
        return 0;
    }
    let h = unsafe { &*(handle as *mut RenderingContextHandle) };
    let Some(hw) = h.hardware.as_ref() else { return 0 };
    match ffi_catch(std::panic::AssertUnwindSafe(|| hw.acquire_frame())) {
        Some(Some(frame)) => {
            unsafe {
                (*out_info).frame_id = frame.frame_id;
                (*out_info).native_handle = frame.native_handle;
                (*out_info).width = frame.width;
                (*out_info).height = frame.height;
            }
            1
        },
        _ => 0,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn timeline_semaphore_new() -> *mut c_void {
    shared_event::new()
}

#[unsafe(no_mangle)]
pub extern "C" fn timeline_semaphore_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { shared_event::destroy(handle) };
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn timeline_semaphore_signal(handle: *mut c_void, value: u64) {
    if !handle.is_null() {
        unsafe { shared_event::signal(handle, value) };
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn timeline_semaphore_signaled_value(handle: *mut c_void) -> u64 {
    if handle.is_null() {
        return 0;
    }
    unsafe { shared_event::signaled_value(handle) }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_signal_after_gpu_work(
    handle: *mut c_void,
    semaphore: *mut c_void,
    value: u64,
) {
    if handle.is_null() || semaphore.is_null() {
        return;
    }
    let h = unsafe { &*(handle as *mut RenderingContextHandle) };
    if let Some(hw) = h.hardware.as_ref() {
        let _ = ffi_catch(std::panic::AssertUnwindSafe(|| {
            hw.signal_after_gpu_work(semaphore, value);
        }));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_release_frame(handle: *mut c_void, frame_id: u64) {
    if handle.is_null() {
        return;
    }
    let h = unsafe { &*(handle as *mut RenderingContextHandle) };
    if let Some(hw) = h.hardware.as_ref() {
        let _ = ffi_catch(std::panic::AssertUnwindSafe(|| hw.release_frame(frame_id)));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_resize(handle: *mut c_void, width: u32, height: u32) {
    if !handle.is_null() {
        let rc = unsafe { rc_from_handle(handle) };
        rc.resize(dpi::PhysicalSize::new(width, height));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_present(handle: *mut c_void) {
    if !handle.is_null() {
        let rc = unsafe { rc_from_handle(handle) };
        rc.present();
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_make_current(handle: *mut c_void) -> u8 {
    if handle.is_null() { return 1; }
    let rc = unsafe { rc_from_handle(handle) };
    match rc.make_current() {
        Ok(_) => 0,
        Err(_) => 1,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_read_pixels(
    handle: *mut c_void,
    out_width: *mut u32,
    out_height: *mut u32,
    out_len: *mut usize,
) -> *mut u8 {
    if handle.is_null() {
        return std::ptr::null_mut();
    }
    let rc = unsafe { rc_from_handle(handle) };

    if let Err(e) = rc.make_current() {
        set_last_error(format!("Failed to make context current for read_pixels: {e:?}"));
        unsafe {
            if !out_width.is_null() { *out_width = 0; }
            if !out_height.is_null() { *out_height = 0; }
            if !out_len.is_null() { *out_len = 0; }
        }
        return std::ptr::null_mut();
    }

    let size = rc.size();
    let rect = servo::DeviceIntRect::from_origin_and_size(
        servo::DeviceIntPoint::new(0, 0),
        servo::DeviceIntSize::new(size.width as i32, size.height as i32),
    );

    match rc.read_to_image(rect) {
        Some(image) => {
            let w = image.width();
            let h = image.height();
            let pixels = image.into_raw();
            let len = pixels.len();
            unsafe {
                if !out_width.is_null() { *out_width = w; }
                if !out_height.is_null() { *out_height = h; }
                if !out_len.is_null() { *out_len = len; }
            }
            let mut boxed = pixels.into_boxed_slice();
            let ptr = boxed.as_mut_ptr();
            std::mem::forget(boxed);
            ptr
        },
        None => {
            unsafe {
                if !out_width.is_null() { *out_width = 0; }
                if !out_height.is_null() { *out_height = 0; }
                if !out_len.is_null() { *out_len = 0; }
            }
            std::ptr::null_mut()
        },
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rendering_context_read_pixels_into(
    handle: *mut c_void,
    dest: *mut u8,
    dest_len: usize,
    out_width: *mut u32,
    out_height: *mut u32,
) -> u8 {
    if handle.is_null() || dest.is_null() {
        return 0;
    }
    let rc = unsafe { rc_from_handle(handle) };

    if let Err(e) = rc.make_current() {
        set_last_error(format!("Failed to make context current for read_pixels_into: {e:?}"));
        return 0;
    }

    let size = rc.size();
    let rect = servo::DeviceIntRect::from_origin_and_size(
        servo::DeviceIntPoint::new(0, 0),
        servo::DeviceIntSize::new(size.width as i32, size.height as i32),
    );

    match rc.read_to_image(rect) {
        Some(image) => {
            let w = image.width();
            let h = image.height();
            let pixels = image.into_raw();
            let copy_len = pixels.len().min(dest_len);
            unsafe {
                std::ptr::copy_nonoverlapping(pixels.as_ptr(), dest, copy_len);
                if !out_width.is_null() { *out_width = w; }
                if !out_height.is_null() { *out_height = h; }
            }
            1
        },
        None => 0,
    }
}

struct FfiWebViewDelegate {
    callbacks: WebViewCallbacks,
}

impl FfiWebViewDelegate {
    #[inline]
    fn ud(&self) -> *mut c_void {
        self.callbacks.user_data
    }
}

impl WebViewDelegate for FfiWebViewDelegate {
    fn notify_new_frame_ready(&self, _webview: WebView) {
        if let Some(cb) = self.callbacks.on_new_frame_ready { cb(self.ud()); }
    }

    fn notify_load_status_changed(&self, _webview: WebView, status: LoadStatus) {
        if let Some(cb) = self.callbacks.on_load_status_changed {
            cb(self.ud(), match status {
                LoadStatus::Started => 0,
                LoadStatus::HeadParsed => 1,
                LoadStatus::Complete => 2,
            });
        }
    }

    fn notify_url_changed(&self, _webview: WebView, url: Url) {
        if let Some(cb) = self.callbacks.on_url_changed {
            if let Ok(c) = CString::new(url.as_str()) { cb(self.ud(), c.as_ptr()); }
        }
    }

    fn notify_page_title_changed(&self, _webview: WebView, title: Option<String>) {
        if let Some(cb) = self.callbacks.on_title_changed {
            match title {
                Some(t) => { if let Ok(c) = CString::new(t) { cb(self.ud(), c.as_ptr()); } },
                None => cb(self.ud(), std::ptr::null()),
            }
        }
    }

    fn notify_cursor_changed(&self, _webview: WebView, cursor: Cursor) {
        if let Some(cb) = self.callbacks.on_cursor_changed {
            cb(self.ud(), cursor as u8);
        }
    }

    fn notify_focus_changed(&self, _webview: WebView, focused: bool) {
        if let Some(cb) = self.callbacks.on_focus_changed {
            cb(self.ud(), focused as u8);
        }
    }

    fn notify_animating_changed(&self, _webview: WebView, animating: bool) {
        if let Some(cb) = self.callbacks.on_animating_changed {
            cb(self.ud(), animating as u8);
        }
    }

    fn notify_favicon_changed(&self, _webview: WebView) {
        if let Some(cb) = self.callbacks.on_favicon_changed { cb(self.ud()); }
    }

    fn notify_input_event_handled(&self, _webview: WebView, id: InputEventId, result: InputEventResult) {
        if let Some(cb) = self.callbacks.on_input_event_handled {
            let id_u64: u64 = unsafe { std::mem::transmute::<InputEventId, usize>(id) as u64 };
            cb(self.ud(), id_u64, result.bits());
        }
    }

    fn notify_history_changed(&self, _webview: WebView, entries: Vec<Url>, current: usize) {
        if let Some(cb) = self.callbacks.on_history_changed {
            let urls: Vec<String> = entries.iter()
                .map(|u| serde_json::to_string(u.as_str()).unwrap_or_else(|_| "\"\"".to_string()))
                .collect();
            let json = format!("[{}]", urls.join(","));
            if let Ok(c) = CString::new(json) {
                cb(self.ud(), c.as_ptr(), current, entries.len());
            }
        }
    }

    fn notify_closed(&self, _webview: WebView) {
        if let Some(cb) = self.callbacks.on_closed { cb(self.ud()); }
    }

    fn notify_fullscreen_state_changed(&self, _webview: WebView, fullscreen: bool) {
        if let Some(cb) = self.callbacks.on_fullscreen_changed {
            cb(self.ud(), fullscreen as u8);
        }
    }

    fn notify_crashed(&self, _webview: WebView, reason: String, backtrace: Option<String>) {
        if let Some(cb) = self.callbacks.on_crashed {
            let c_reason = CString::new(reason).unwrap_or_default();
            let c_bt = backtrace.and_then(|b| CString::new(b).ok());
            let bt_ptr = c_bt.as_ref().map(|c| c.as_ptr()).unwrap_or(std::ptr::null());
            cb(self.ud(), c_reason.as_ptr(), bt_ptr);
        }
    }

    fn show_console_message(&self, _webview: WebView, level: ConsoleLogLevel, message: String) {
        if let Some(cb) = self.callbacks.on_console_message {
            if let Ok(c) = CString::new(message) { cb(self.ud(), console_level_to_u8(level), c.as_ptr()); }
        }
    }

    fn request_unload(&self, _webview: WebView, request: AllowOrDenyRequest) {
        if let Some(cb) = self.callbacks.on_request_unload {
            let handle = Box::into_raw(Box::new(request)) as usize;
            cb(self.ud(), handle);
        }
    }

    fn notify_media_session_event(&self, _webview: WebView, event: MediaSessionEvent) {
        if let Some(cb) = self.callbacks.on_media_session_event {
            let (event_type, json) = match &event {
                MediaSessionEvent::SetMetadata(m) => {
                    (0u8, format!("{{\"title\":\"{}\",\"artist\":\"{}\",\"album\":\"{}\"}}", m.title, m.artist, m.album))
                },
                MediaSessionEvent::PlaybackStateChange(s) => {
                    let state = match s {
                        servo::MediaSessionPlaybackState::None_ => "none",
                        servo::MediaSessionPlaybackState::Playing => "playing",
                        servo::MediaSessionPlaybackState::Paused => "paused",
                    };
                    (1, format!("{{\"state\":\"{state}\"}}" ))
                },
                MediaSessionEvent::SetPositionState(p) => {
                    (2, format!("{{\"duration\":{},\"playbackRate\":{},\"position\":{}}}", p.duration, p.playback_rate, p.position))
                },
            };
            if let Ok(c) = CString::new(json) { cb(self.ud(), event_type, c.as_ptr()); }
        }
    }

    fn screen_geometry(&self, _webview: WebView) -> Option<ScreenGeometry> {
        if let Some(cb) = self.callbacks.get_screen_geometry {
            let mut geo = CScreenGeometry::default();
            if cb(self.ud(), &mut geo) != 0 {
                return Some(ScreenGeometry {
                    size: DeviceIntSize::new(geo.size_width, geo.size_height),
                    available_size: DeviceIntSize::new(geo.available_width, geo.available_height),
                    window_rect: DeviceIntRect::from_origin_and_size(
                        DeviceIntPoint::new(geo.window_x, geo.window_y),
                        DeviceIntSize::new(geo.window_width, geo.window_height),
                    ),
                });
            }
        }
        None
    }

    fn show_embedder_control(&self, _webview: WebView, control: EmbedderControl) {
        match control {
            EmbedderControl::SimpleDialog(dialog) => {
                match dialog {
                    SimpleDialog::Alert(alert) => {
                        if let Some(cb) = self.callbacks.on_show_alert {
                            let handle = Box::into_raw(Box::new(alert)) as usize;
                            let alert_ref = unsafe { &*(handle as *const servo::AlertDialog) };
                            if let Ok(c) = CString::new(alert_ref.message()) {
                                cb(self.ud(), c.as_ptr(), handle);
                            } else {
                                let _ = unsafe { Box::from_raw(handle as *mut servo::AlertDialog) };
                            }
                        }
                    },
                    SimpleDialog::Confirm(confirm) => {
                        if let Some(cb) = self.callbacks.on_show_confirm {
                            let handle = Box::into_raw(Box::new(confirm)) as usize;
                            let confirm_ref = unsafe { &*(handle as *const servo::ConfirmDialog) };
                            if let Ok(c) = CString::new(confirm_ref.message()) {
                                cb(self.ud(), c.as_ptr(), handle);
                            } else {
                                let _ = unsafe { Box::from_raw(handle as *mut servo::ConfirmDialog) };
                            }
                        }
                    },
                    SimpleDialog::Prompt(prompt) => {
                        if let Some(cb) = self.callbacks.on_show_prompt {
                            let handle = Box::into_raw(Box::new(prompt)) as usize;
                            let prompt_ref = unsafe { &*(handle as *const servo::PromptDialog) };
                            let msg_ok = CString::new(prompt_ref.message());
                            let val_ok = CString::new(prompt_ref.current_value());
                            if let (Ok(msg), Ok(val)) = (msg_ok, val_ok) {
                                cb(self.ud(), msg.as_ptr(), val.as_ptr(), handle);
                            } else {
                                let _ = unsafe { Box::from_raw(handle as *mut servo::PromptDialog) };
                            }
                        }
                    },
                }
            },
            EmbedderControl::SelectElement(select) => {
                if let Some(cb) = self.callbacks.on_show_select_element {
                    let selected_id = select.selected_options().first().map(|id| *id as i64).unwrap_or(-1);
                    let pos = select.position();
                    let json = select_options_to_json(select.options());
                    let handle = Box::into_raw(Box::new(select)) as usize;
                    if let Ok(c) = CString::new(json) {
                        cb(self.ud(), c.as_ptr(), selected_id,
                           pos.min.x, pos.min.y,
                           pos.size().width, pos.size().height,
                           handle);
                    } else {
                        let _ = unsafe { Box::from_raw(handle as *mut SelectElement) };
                    }
                }
            },
            EmbedderControl::ContextMenu(context_menu) => {
                if let Some(cb) = self.callbacks.on_show_context_menu {
                    let json = context_menu_items_to_json(context_menu.items());
                    let pos = context_menu.position();
                    let handle = Box::into_raw(Box::new(context_menu)) as usize;
                    if let Ok(c) = CString::new(json) {
                        cb(self.ud(), c.as_ptr(), pos.min.x, pos.min.y, handle);
                    } else {
                        let _ = unsafe { Box::from_raw(handle as *mut ContextMenu) };
                    }
                }
            },
            EmbedderControl::FilePicker(file_picker) => {
                if let Some(cb) = self.callbacks.on_show_file_picker {
                    let patterns: Vec<String> = file_picker.filter_patterns().iter()
                        .map(|p| serde_json::to_string(&p.0).unwrap_or_else(|_| "\"\"".to_string()))
                        .collect();
                    let patterns_json = format!("[{}]", patterns.join(","));
                    let allow_multiple = file_picker.allow_select_multiple() as u8;
                    let current: Vec<String> = file_picker.current_paths().iter()
                        .map(|p| serde_json::to_string(&p.to_string_lossy()).unwrap_or_else(|_| "\"\"".to_string()))
                        .collect();
                    let current_json = format!("[{}]", current.join(","));
                    let handle = Box::into_raw(Box::new(file_picker)) as usize;
                    let patterns_ok = CString::new(patterns_json);
                    let current_ok = CString::new(current_json);
                    if let (Ok(p), Ok(c)) = (patterns_ok, current_ok) {
                        cb(self.ud(), p.as_ptr(), allow_multiple, c.as_ptr(), handle);
                    } else {
                        let picker = unsafe { *Box::from_raw(handle as *mut FilePicker) };
                        picker.dismiss();
                    }
                }
            },
            EmbedderControl::ColorPicker(color_picker) => {
                if let Some(cb) = self.callbacks.on_show_color_picker {
                    let (has_color, r, g, b) = match color_picker.current_color() {
                        Some(c) => (1u8, c.red, c.green, c.blue),
                        None => (0, 0, 0, 0),
                    };
                    let pos = color_picker.position();
                    let handle = Box::into_raw(Box::new(color_picker)) as usize;
                    cb(self.ud(), has_color, r, g, b,
                       pos.min.x, pos.min.y,
                       pos.size().width, pos.size().height,
                       handle);
                }
            },
            EmbedderControl::InputMethod(ime) => {
                if let Some(cb) = self.callbacks.on_show_input_method {
                    let ime_type = match ime.input_method_type() {
                        InputMethodType::Color => 0u8,
                        InputMethodType::Date => 1,
                        InputMethodType::DatetimeLocal => 2,
                        InputMethodType::Email => 3,
                        InputMethodType::Month => 4,
                        InputMethodType::Number => 5,
                        InputMethodType::Password => 6,
                        InputMethodType::Search => 7,
                        InputMethodType::Tel => 8,
                        InputMethodType::Text => 9,
                        InputMethodType::Time => 10,
                        InputMethodType::Url => 11,
                        InputMethodType::Week => 12,
                    };
                    let insertion = ime.insertion_point().map(|p| p as i64).unwrap_or(-1);
                    let pos = ime.position();
                    let multiline = ime.multiline() as u8;
                    let vk = ime.allow_virtual_keyboard() as u8;
                    if let Ok(text) = CString::new(ime.text()) {
                        cb(self.ud(), ime_type, text.as_ptr(), insertion, multiline, vk,
                           pos.min.x, pos.min.y, pos.size().width, pos.size().height);
                    }
                }
            },
        }
    }

    fn request_navigation(&self, _webview: WebView, request: NavigationRequest) {
        if let Some(cb) = self.callbacks.on_request_navigation {
            let url_str = request.url.as_str().to_string();
            let handle = Box::into_raw(Box::new(request)) as usize;
            if let Ok(c) = CString::new(url_str) {
                cb(self.ud(), c.as_ptr(), handle);
            } else {
                let req = unsafe { *Box::from_raw(handle as *mut NavigationRequest) };
                req.allow();
            }
        }
        // If no callback is set, NavigationRequest drops and defaults to allow.
    }

    fn request_permission(&self, _webview: WebView, request: PermissionRequest) {
        if let Some(cb) = self.callbacks.on_request_permission {
            let feature = match request.feature() {
                PermissionFeature::Geolocation => 0u8,
                PermissionFeature::Notifications => 1,
                PermissionFeature::Push => 2,
                PermissionFeature::Midi => 3,
                PermissionFeature::Camera => 4,
                PermissionFeature::Microphone => 5,
                PermissionFeature::Speaker => 6,
                PermissionFeature::DeviceInfo => 7,
                PermissionFeature::BackgroundSync => 8,
                PermissionFeature::Bluetooth => 9,
                PermissionFeature::PersistentStorage => 10,
                PermissionFeature::ScreenWakeLock(_) => 11,
                PermissionFeature::Gamepad => 12,
            };
            let handle = Box::into_raw(Box::new(request)) as usize;
            cb(self.ud(), feature, handle);
        }
    }

    fn request_create_new(&self, _parent_webview: WebView, request: CreateNewWebViewRequest) {
        if let Some(cb) = self.callbacks.on_request_create_new_webview {
            let handle = Box::into_raw(Box::new(request)) as usize;
            cb(self.ud(), handle);
        }
    }

    fn request_authentication(&self, _webview: WebView, request: AuthenticationRequest) {
        if let Some(cb) = self.callbacks.on_request_authentication {
            let url_str = request.url().as_str().to_string();
            let for_proxy = request.for_proxy() as u8;
            let handle = Box::into_raw(Box::new(request)) as usize;
            if let Ok(c) = CString::new(url_str) {
                cb(self.ud(), c.as_ptr(), for_proxy, handle);
            } else {
                let _ = unsafe { Box::from_raw(handle as *mut AuthenticationRequest) };
            }
        }
    }

    fn hide_embedder_control(&self, _webview: WebView, _control_id: EmbedderControlId) {
        if let Some(cb) = self.callbacks.on_hide_embedder_control {
            cb(self.ud());
        }
    }

    fn load_web_resource(&self, _webview: WebView, load: WebResourceLoad) {
        if let Some(cb) = self.callbacks.on_load_web_resource {
            fire_web_resource_callback(cb, self.ud(), load);
        }
    }

    fn notify_status_text_changed(&self, _webview: WebView, status: Option<String>) {
        if let Some(cb) = self.callbacks.on_status_text_changed {
            match status {
                Some(s) => { if let Ok(c) = CString::new(s) { cb(self.ud(), c.as_ptr()); } },
                None => cb(self.ud(), std::ptr::null()),
            }
        }
    }

    fn notify_traversal_complete(&self, _webview: WebView, _: TraversalId) {
        if let Some(cb) = self.callbacks.on_traversal_complete {
            cb(self.ud());
        }
    }

    fn request_move_to(&self, _webview: WebView, point: DeviceIntPoint) {
        if let Some(cb) = self.callbacks.on_request_move_to {
            cb(self.ud(), point.x, point.y);
        }
    }

    fn request_resize_to(&self, _webview: WebView, size: DeviceIntSize) {
        if let Some(cb) = self.callbacks.on_request_resize_to {
            cb(self.ud(), size.width, size.height);
        }
    }

    fn request_protocol_handler(&self, _webview: WebView, registration: ProtocolHandlerRegistration, request: AllowOrDenyRequest) {
        if let Some(cb) = self.callbacks.on_request_protocol_handler {
            let reg_type = match registration.register_or_unregister {
                RegisterOrUnregister::Register => 0u8,
                RegisterOrUnregister::Unregister => 1u8,
            };
            let handle = Box::into_raw(Box::new(request)) as usize;
            let scheme_ok = CString::new(registration.scheme);
            let url_ok = CString::new(registration.url.as_str());
            if let (Ok(scheme), Ok(url)) = (scheme_ok, url_ok) {
                cb(self.ud(), scheme.as_ptr(), url.as_ptr(), reg_type, handle);
            } else {
                let req = unsafe { *Box::from_raw(handle as *mut AllowOrDenyRequest) };
                req.deny();
            }
        }
    }

    fn show_notification(&self, _webview: WebView, notification: Notification) {
        if let Some(cb) = self.callbacks.on_show_notification {
            let title_ok = CString::new(notification.title);
            let body_ok = CString::new(notification.body);
            if let (Ok(t), Ok(b)) = (title_ok, body_ok) {
                cb(self.ud(), t.as_ptr(), b.as_ptr());
            }
        }
    }

    fn show_bluetooth_device_dialog(&self, _webview: WebView, request: BluetoothDeviceSelectionRequest) {
        if let Some(cb) = self.callbacks.on_show_bluetooth_device_dialog {
            let devices: Vec<String> = request.devices().iter().map(|d| {
                format!("{{\"name\":{},\"address\":{}}}",
                    serde_json::to_string(&d.name).unwrap_or_else(|_| "\"\"".into()),
                    serde_json::to_string(&d.address).unwrap_or_else(|_| "\"\"".into()))
            }).collect();
            let json = format!("[{}]", devices.join(","));
            let handle = Box::into_raw(Box::new(request)) as usize;
            if let Ok(c) = CString::new(json) {
                cb(self.ud(), c.as_ptr(), handle);
            } else {
                // Can't form JSON, cancel the request.
                let req = unsafe { *Box::from_raw(handle as *mut BluetoothDeviceSelectionRequest) };
                let _ = req.cancel();
            }
        }
    }
}

struct WebViewHandle {
    webview: WebView,
    _rendering_context: Rc<dyn RenderingContext>,
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_new(
    servo_handle: *mut c_void,
    rendering_ctx_handle: *mut c_void,
    callbacks: WebViewCallbacks,
    clipboard: ClipboardCallbacks,
    initial_url: *const c_char,
) -> *mut c_void {
    if servo_handle.is_null() || rendering_ctx_handle.is_null() {
        set_last_error("servo_handle and rendering_ctx_handle must not be null".into());
        return std::ptr::null_mut();
    }
    let result = ffi_catch(std::panic::AssertUnwindSafe(|| {
        let servo = unsafe { &*(servo_handle as *mut Servo) };
        let rc = unsafe { rc_from_handle(rendering_ctx_handle) };
        let delegate = FfiWebViewDelegate { callbacks };
        let mut builder = WebViewBuilder::new(servo, rc.clone())
            .delegate(Rc::new(delegate));
        if clipboard.get_text.is_some() || clipboard.set_text.is_some() || clipboard.clear.is_some() {
            builder = builder.clipboard_delegate(Rc::new(FfiClipboardDelegate { callbacks: clipboard }));
        }
        if !initial_url.is_null() {
            let url_str = unsafe { CStr::from_ptr(initial_url) }.to_str().unwrap_or_default();
            if let Ok(url) = Url::parse(url_str) { builder = builder.url(url); }
        }
        let webview = builder.build();
        Box::into_raw(Box::new(WebViewHandle { webview, _rendering_context: rc.clone() })) as *mut c_void
    }));
    result.unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn create_new_webview_build(
    request_handle: usize,
    rendering_ctx_handle: *mut c_void,
    callbacks: WebViewCallbacks,
    clipboard: ClipboardCallbacks,
) -> *mut c_void {
    if request_handle == 0 || rendering_ctx_handle.is_null() {
        set_last_error("request_handle and rendering_ctx_handle must not be null/zero".into());
        return std::ptr::null_mut();
    }
    let result = ffi_catch(std::panic::AssertUnwindSafe(|| {
        let request = unsafe { *Box::from_raw(request_handle as *mut CreateNewWebViewRequest) };
        let rc = unsafe { rc_from_handle(rendering_ctx_handle) };
        let delegate = FfiWebViewDelegate { callbacks };
        let mut builder = request.builder(rc.clone())
            .delegate(Rc::new(delegate));
        if clipboard.get_text.is_some() || clipboard.set_text.is_some() || clipboard.clear.is_some() {
            builder = builder.clipboard_delegate(Rc::new(FfiClipboardDelegate { callbacks: clipboard }));
        }
        let webview = builder.build();
        Box::into_raw(Box::new(WebViewHandle { webview, _rendering_context: rc.clone() })) as *mut c_void
    }));
    result.unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn create_new_webview_dismiss(request_handle: usize) {
    if request_handle != 0 {
        unsafe { drop(Box::from_raw(request_handle as *mut CreateNewWebViewRequest)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn authentication_request_authenticate(
    request_handle: usize,
    username: *const c_char,
    password: *const c_char,
) {
    if request_handle == 0 { return; }
    let request = unsafe { *Box::from_raw(request_handle as *mut AuthenticationRequest) };
    let u = if username.is_null() { String::new() }
            else { unsafe { CStr::from_ptr(username) }.to_str().unwrap_or_default().to_string() };
    let p = if password.is_null() { String::new() }
            else { unsafe { CStr::from_ptr(password) }.to_str().unwrap_or_default().to_string() };
    request.authenticate(u, p);
}

#[unsafe(no_mangle)]
pub extern "C" fn authentication_request_dismiss(request_handle: usize) {
    if request_handle != 0 {
        unsafe { drop(Box::from_raw(request_handle as *mut AuthenticationRequest)); }
    }
}

fn fire_web_resource_callback(
    cb: extern "C" fn(*mut c_void, *const c_char, *const c_char, u8, u8, usize),
    ud: *mut c_void,
    load: WebResourceLoad,
) {
    let url_str = load.request.url.as_str().to_string();
    let method_str = load.request.method.as_str().to_string();
    let is_main = load.request.is_for_main_frame as u8;
    let is_redir = load.request.is_redirect as u8;
    let handle = Box::into_raw(Box::new(load)) as usize;
    let url_ok = CString::new(url_str);
    let method_ok = CString::new(method_str);
    if let (Ok(u), Ok(m)) = (url_ok, method_ok) {
        cb(ud, u.as_ptr(), m.as_ptr(), is_main, is_redir, handle);
    } else {
        let _ = unsafe { Box::from_raw(handle as *mut WebResourceLoad) };
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn web_resource_load_dismiss(handle: usize) {
    if handle != 0 {
        unsafe { drop(Box::from_raw(handle as *mut WebResourceLoad)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn web_resource_load_intercept(
    handle: usize,
    status_code: u16,
    body: *const u8,
    body_len: usize,
) {
    if handle == 0 { return; }
    let load = unsafe { *Box::from_raw(handle as *mut WebResourceLoad) };
    let url = load.request.url.clone();
    let response = WebResourceResponse::new(url)
        .status_code(http::StatusCode::from_u16(status_code).unwrap_or(http::StatusCode::OK));
    let mut intercepted = load.intercept(response);
    if !body.is_null() && body_len > 0 {
        let bytes = unsafe { std::slice::from_raw_parts(body, body_len) }.to_vec();
        intercepted.send_body_data(bytes);
    }
    intercepted.finish();
}

#[unsafe(no_mangle)]
pub extern "C" fn web_resource_load_cancel(handle: usize) {
    if handle == 0 { return; }
    let load = unsafe { *Box::from_raw(handle as *mut WebResourceLoad) };
    let url = load.request.url.clone();
    let response = WebResourceResponse::new(url);
    load.intercept(response).cancel();
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut WebViewHandle)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_load_url(handle: *mut c_void, url: *const c_char) {
    if let (Some(wv), false) = (wv_ref(handle), url.is_null()) {
        let s = unsafe { CStr::from_ptr(url) }.to_str().unwrap_or_default();
        if let Ok(u) = Url::parse(s) { wv.webview.load(u); }
        else { set_last_error(format!("Invalid URL: {s}")); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_reload(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.reload(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_go_back(handle: *mut c_void, steps: usize) {
    if let Some(wv) = wv_ref(handle) { wv.webview.go_back(steps.max(1)); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_go_forward(handle: *mut c_void, steps: usize) {
    if let Some(wv) = wv_ref(handle) { wv.webview.go_forward(steps.max(1)); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_paint(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.paint(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_resize(handle: *mut c_void, width: u32, height: u32) {
    if let Some(wv) = wv_ref(handle) { wv.webview.resize(dpi::PhysicalSize::new(width, height)); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_focus(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.focus(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_blur(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.blur(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_show(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.show(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_hide(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.hide(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_set_hidpi_scale(handle: *mut c_void, scale: f32) {
    if let Some(wv) = wv_ref(handle) {
        use euclid::Scale;
        wv.webview.set_hidpi_scale_factor(Scale::new(scale));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_url(handle: *mut c_void) -> *mut c_char {
    wv_ref(handle)
        .and_then(|wv| wv.webview.url())
        .and_then(|url| CString::new(url.as_str()).ok())
        .map(|s| s.into_raw())
        .unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_title(handle: *mut c_void) -> *mut c_char {
    wv_ref(handle)
        .and_then(|wv| wv.webview.page_title())
        .and_then(|t| CString::new(t).ok())
        .map(|s| s.into_raw())
        .unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_load_status(handle: *mut c_void) -> u8 {
    wv_ref(handle).map(|wv| match wv.webview.load_status() {
        LoadStatus::Started => 0,
        LoadStatus::HeadParsed => 1,
        LoadStatus::Complete => 2,
    }).unwrap_or(0)
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_cursor(handle: *mut c_void) -> u8 {
    wv_ref(handle).map(|wv| wv.webview.cursor() as u8).unwrap_or(1) // 1 = Default
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_is_focused(handle: *mut c_void) -> u8 {
    wv_ref(handle).map(|wv| wv.webview.focused() as u8).unwrap_or(0)
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_is_animating(handle: *mut c_void) -> u8 {
    wv_ref(handle).map(|wv| wv.webview.clone().animating() as u8).unwrap_or(0)
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_can_go_back(handle: *mut c_void) -> u8 {
    wv_ref(handle).map(|wv| wv.webview.can_go_back() as u8).unwrap_or(0)
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_can_go_forward(handle: *mut c_void) -> u8 {
    wv_ref(handle).map(|wv| wv.webview.can_go_forward() as u8).unwrap_or(0)
}

fn code_str_to_named_key(code: &str) -> servo::Key {
    use servo::{Key, NamedKey};
    Key::Named(match code {
        "Backspace" => NamedKey::Backspace,
        "Tab" => NamedKey::Tab,
        "Enter" => NamedKey::Enter,
        "Escape" => NamedKey::Escape,
        "Delete" => NamedKey::Delete,
        "ArrowUp" => NamedKey::ArrowUp,
        "ArrowDown" => NamedKey::ArrowDown,
        "ArrowLeft" => NamedKey::ArrowLeft,
        "ArrowRight" => NamedKey::ArrowRight,
        "Home" => NamedKey::Home,
        "End" => NamedKey::End,
        "PageUp" => NamedKey::PageUp,
        "PageDown" => NamedKey::PageDown,
        "Insert" => NamedKey::Insert,
        "Space" => return Key::Character(" ".into()),
        "ShiftLeft" | "ShiftRight" => NamedKey::Shift,
        "ControlLeft" | "ControlRight" => NamedKey::Control,
        "AltLeft" | "AltRight" => NamedKey::Alt,
        "MetaLeft" | "MetaRight" => NamedKey::Meta,
        "CapsLock" => NamedKey::CapsLock,
        "NumLock" => NamedKey::NumLock,
        "ScrollLock" => NamedKey::ScrollLock,
        "F1" => NamedKey::F1, "F2" => NamedKey::F2, "F3" => NamedKey::F3,
        "F4" => NamedKey::F4, "F5" => NamedKey::F5, "F6" => NamedKey::F6,
        "F7" => NamedKey::F7, "F8" => NamedKey::F8, "F9" => NamedKey::F9,
        "F10" => NamedKey::F10, "F11" => NamedKey::F11, "F12" => NamedKey::F12,
        "ContextMenu" => NamedKey::ContextMenu,
        "PrintScreen" => NamedKey::PrintScreen,
        "Pause" => NamedKey::Pause,
        _ => NamedKey::Unidentified,
    })
}

fn event_id_to_u64(id: InputEventId) -> u64 {
    unsafe { std::mem::transmute::<InputEventId, usize>(id) as u64 }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_mouse_button(
    handle: *mut c_void, action: u8, button: u16, x: f32, y: f32,
) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };
    let action = match action { 0 => MouseButtonAction::Down, _ => MouseButtonAction::Up };
    let button = MouseButton::from(button as u64);
    let point = WebViewPoint::from(DevicePoint::new(x, y));
    let event = InputEvent::MouseButton(MouseButtonEvent::new(action, button, point));
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_mouse_move(handle: *mut c_void, x: f32, y: f32) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };
    let point = WebViewPoint::from(DevicePoint::new(x, y));
    let event = InputEvent::MouseMove(MouseMoveEvent::new(point));
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_mouse_left_viewport(handle: *mut c_void) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };
    let event = InputEvent::MouseLeftViewport(MouseLeftViewportEvent::default());
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_key_event(
    handle: *mut c_void,
    state: u8,
    key_char: u32,
    key_code: *const c_char,
    modifiers: u32,
) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };

    use servo::{Key, KeyState, Code, Modifiers, NamedKey};

    let key_state = match state { 0 => KeyState::Down, _ => KeyState::Up };

    let code_str = if !key_code.is_null() {
        unsafe { CStr::from_ptr(key_code) }.to_str().unwrap_or("")
    } else {
        ""
    };
    let code = if !code_str.is_empty() {
        code_str.parse::<Code>().unwrap_or(Code::Unidentified)
    } else {
        Code::Unidentified
    };

    let key = if key_char != 0 {
        if let Some(ch) = char::from_u32(key_char) {
            Key::Character(ch.to_string().into())
        } else {
            Key::Named(NamedKey::Unidentified)
        }
    } else {
        code_str_to_named_key(code_str)
    };

    let mods = Modifiers::from_bits_truncate(modifiers);

    let kb_event = keyboard_types::KeyboardEvent {
        state: key_state,
        key,
        code,
        location: keyboard_types::Location::Standard,
        modifiers: mods,
        repeat: false,
        is_composing: false,
    };
    let event = InputEvent::Keyboard(KeyboardEvent::new(kb_event));
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_wheel(
    handle: *mut c_void,
    delta_x: f64, delta_y: f64, delta_z: f64,
    mode: u8, x: f32, y: f32,
) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };
    let wheel_mode = match mode {
        1 => WheelMode::DeltaLine,
        2 => WheelMode::DeltaPage,
        _ => WheelMode::DeltaPixel,
    };
    let delta = WheelDelta { x: delta_x, y: delta_y, z: delta_z, mode: wheel_mode };
    let point = WebViewPoint::from(DevicePoint::new(x, y));
    let event = InputEvent::Wheel(WheelEvent::new(delta, point));
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_scroll(
    handle: *mut c_void,
    delta_x: f32, delta_y: f32, point_x: f32, point_y: f32,
) {
    if let Some(wv) = wv_ref(handle) {
        let scroll = Scroll::Delta(servo::WebViewVector::Device(DeviceVector2D::new(delta_x, delta_y)));
        let point = WebViewPoint::from(DevicePoint::new(point_x, point_y));
        wv.webview.notify_scroll_event(scroll, point);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_touch(
    handle: *mut c_void, event_type: u8, touch_id: i32, x: f32, y: f32,
) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };
    let touch_type = match event_type {
        0 => TouchEventType::Down,
        1 => TouchEventType::Move,
        2 => TouchEventType::Up,
        _ => TouchEventType::Cancel,
    };
    let point = WebViewPoint::from(DevicePoint::new(x, y));
    let event = InputEvent::Touch(TouchEvent::new(
        touch_type,
        TouchId(touch_id),
        point,
        TouchPointerType::Touch,
    ));
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_editing_action(handle: *mut c_void, action: u8) -> u64 {
    let Some(wv) = wv_ref(handle) else { return 0; };
    let editing = match action {
        0 => EditingActionEvent::Copy,
        1 => EditingActionEvent::Cut,
        _ => EditingActionEvent::Paste,
    };
    let event = InputEvent::EditingAction(editing);
    event_id_to_u64(wv.webview.notify_input_event(event))
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_set_page_zoom(handle: *mut c_void, zoom: f32) {
    if let Some(wv) = wv_ref(handle) { wv.webview.set_page_zoom(zoom); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_page_zoom(handle: *mut c_void) -> f32 {
    wv_ref(handle).map(|wv| wv.webview.page_zoom()).unwrap_or(1.0)
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_exit_fullscreen(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) { wv.webview.exit_fullscreen(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_set_throttled(handle: *mut c_void, throttled: u8) {
    if let Some(wv) = wv_ref(handle) { wv.webview.set_throttled(throttled != 0); }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_evaluate_javascript(
    handle: *mut c_void,
    script: *const c_char,
    callback: extern "C" fn(*mut c_void, *const c_char, *const c_char),
    callback_data: *mut c_void,
) {
    let Some(wv) = wv_ref(handle) else { return; };
    if script.is_null() { return; }
    let script_str = unsafe { CStr::from_ptr(script) }.to_str().unwrap_or_default().to_string();

    let cb_data = callback_data as usize;
    let cb = callback;

    wv.webview.evaluate_javascript(script_str, move |result| {
        let ud = cb_data as *mut c_void;
        match result {
            Ok(value) => {
                let json = jsvalue_to_json(&value);
                if let Ok(c) = CString::new(json) {
                    cb(ud, c.as_ptr(), std::ptr::null());
                }
            },
            Err(err) => {
                let msg = format!("{err:?}");
                if let Ok(c) = CString::new(msg) {
                    cb(ud, std::ptr::null(), c.as_ptr());
                }
            },
        }
    });
}

fn jsvalue_to_json(val: &JSValue) -> String {
    match val {
        JSValue::Undefined => "undefined".to_string(),
        JSValue::Null => "null".to_string(),
        JSValue::Boolean(b) => b.to_string(),
        JSValue::Number(n) => n.to_string(),
        JSValue::String(s) => format!("\"{}\"", s.replace('\\', "\\\\").replace('"', "\\\"")),
        JSValue::Array(arr) => {
            let items: Vec<String> = arr.iter().map(jsvalue_to_json).collect();
            format!("[{}]", items.join(","))
        },
        JSValue::Object(map) => {
            let items: Vec<String> = map.iter()
                .map(|(k, v)| format!("\"{}\":{}", k.replace('"', "\\\""), jsvalue_to_json(v)))
                .collect();
            format!("{{{}}}", items.join(","))
        },
        JSValue::Element(s) | JSValue::ShadowRoot(s) | JSValue::Frame(s) | JSValue::Window(s) => {
            format!("\"{}\"", s)
        },
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn dialog_alert_dismiss(dialog_handle: usize) {
    if dialog_handle == 0 { return; }
    let alert = unsafe { *Box::from_raw(dialog_handle as *mut servo::AlertDialog) };
    alert.confirm();
}

#[unsafe(no_mangle)]
pub extern "C" fn dialog_confirm_respond(dialog_handle: usize, confirmed: u8) {
    if dialog_handle == 0 { return; }
    let confirm = unsafe { *Box::from_raw(dialog_handle as *mut servo::ConfirmDialog) };
    if confirmed != 0 { confirm.confirm(); } else { confirm.dismiss(); }
}

#[unsafe(no_mangle)]
pub extern "C" fn dialog_prompt_respond(dialog_handle: usize, value: *const c_char) {
    if dialog_handle == 0 { return; }
    let mut prompt = unsafe { *Box::from_raw(dialog_handle as *mut servo::PromptDialog) };
    if value.is_null() {
        prompt.dismiss();
    } else {
        let val = unsafe { CStr::from_ptr(value) }.to_str().unwrap_or_default();
        prompt.set_current_value(val);
        prompt.confirm();
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn navigation_request_allow(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut NavigationRequest) };
    req.allow();
}

#[unsafe(no_mangle)]
pub extern "C" fn navigation_request_deny(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut NavigationRequest) };
    req.deny();
}

#[unsafe(no_mangle)]
pub extern "C" fn permission_request_allow(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut PermissionRequest) };
    req.allow();
}

#[unsafe(no_mangle)]
pub extern "C" fn permission_request_deny(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut PermissionRequest) };
    req.deny();
}

#[unsafe(no_mangle)]
pub extern "C" fn allow_or_deny_request_allow(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut AllowOrDenyRequest) };
    req.allow();
}

#[unsafe(no_mangle)]
pub extern "C" fn allow_or_deny_request_deny(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut AllowOrDenyRequest) };
    req.deny();
}

#[unsafe(no_mangle)]
pub extern "C" fn bluetooth_device_pick(request_handle: usize, device_index: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut BluetoothDeviceSelectionRequest) };
    if let Some(device) = req.devices().get(device_index) {
        let device = device.clone();
        let _ = req.pick_device(&device);
    } else {
        let _ = req.cancel();
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn bluetooth_device_cancel(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut BluetoothDeviceSelectionRequest) };
    let _ = req.cancel();
}

#[unsafe(no_mangle)]
pub extern "C" fn unload_request_allow(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut AllowOrDenyRequest) };
    req.allow();
}

#[unsafe(no_mangle)]
pub extern "C" fn unload_request_deny(request_handle: usize) {
    if request_handle == 0 { return; }
    let req = unsafe { *Box::from_raw(request_handle as *mut AllowOrDenyRequest) };
    req.deny();
}

#[unsafe(no_mangle)]
pub extern "C" fn select_element_respond(request_handle: usize, selected_id: i64) {
    if request_handle == 0 { return; }
    let mut select = unsafe { *Box::from_raw(request_handle as *mut SelectElement) };
    if selected_id >= 0 {
        select.select(vec![selected_id as usize]);
        select.submit();
    }
}

fn select_options_to_json(options: &[SelectElementOptionOrOptgroup]) -> String {
    let items: Vec<String> = options.iter().map(|item| {
        match item {
            SelectElementOptionOrOptgroup::Option(opt) => {
                format!(
                    r#"{{"type":"option","id":{},"label":{},"disabled":{}}}"#,
                    opt.id,
                    serde_json::to_string(&opt.label).unwrap_or_else(|_| "\"\"".to_string()),
                    opt.is_disabled
                )
            },
            SelectElementOptionOrOptgroup::Optgroup { label, options } => {
                let sub: Vec<String> = options.iter().map(|opt| {
                    format!(
                        r#"{{"type":"option","id":{},"label":{},"disabled":{}}}"#,
                        opt.id,
                        serde_json::to_string(&opt.label).unwrap_or_else(|_| "\"\"".to_string()),
                        opt.is_disabled
                    )
                }).collect();
                format!(
                    r#"{{"type":"optgroup","label":{},"options":[{}]}}"#,
                    serde_json::to_string(label).unwrap_or_else(|_| "\"\"".to_string()),
                    sub.join(",")
                )
            },
        }
    }).collect();
    format!("[{}]", items.join(","))
}

#[unsafe(no_mangle)]
pub extern "C" fn context_menu_select(request_handle: usize, action: u8) {
    if request_handle == 0 { return; }
    let context_menu = unsafe { *Box::from_raw(request_handle as *mut ContextMenu) };
    let action = match action {
        0 => ContextMenuAction::GoBack,
        1 => ContextMenuAction::GoForward,
        2 => ContextMenuAction::Reload,
        3 => ContextMenuAction::CopyLink,
        4 => ContextMenuAction::OpenLinkInNewWebView,
        5 => ContextMenuAction::CopyImageLink,
        6 => ContextMenuAction::OpenImageInNewView,
        7 => ContextMenuAction::Cut,
        8 => ContextMenuAction::Copy,
        9 => ContextMenuAction::Paste,
        10 => ContextMenuAction::SelectAll,
        _ => return, // invalid action, drop the menu
    };
    context_menu.select(action);
}

#[unsafe(no_mangle)]
pub extern "C" fn context_menu_dismiss(request_handle: usize) {
    if request_handle == 0 { return; }
    let context_menu = unsafe { *Box::from_raw(request_handle as *mut ContextMenu) };
    context_menu.dismiss();
}

fn context_menu_items_to_json(items: &[ContextMenuItem]) -> String {
    let entries: Vec<String> = items.iter().map(|item| {
        match item {
            ContextMenuItem::Item { label, action, enabled } => {
                let action_id: u8 = match action {
                    ContextMenuAction::GoBack => 0,
                    ContextMenuAction::GoForward => 1,
                    ContextMenuAction::Reload => 2,
                    ContextMenuAction::CopyLink => 3,
                    ContextMenuAction::OpenLinkInNewWebView => 4,
                    ContextMenuAction::CopyImageLink => 5,
                    ContextMenuAction::OpenImageInNewView => 6,
                    ContextMenuAction::Cut => 7,
                    ContextMenuAction::Copy => 8,
                    ContextMenuAction::Paste => 9,
                    ContextMenuAction::SelectAll => 10,
                };
                format!(
                    r#"{{"type":"item","label":{},"action":{},"enabled":{}}}"#,
                    serde_json::to_string(label).unwrap_or_else(|_| "\"\"".to_string()),
                    action_id,
                    enabled
                )
            },
            ContextMenuItem::Separator => {
                r#"{"type":"separator"}"#.to_string()
            },
        }
    }).collect();
    format!("[{}]", entries.join(","))
}

struct FfiClipboardDelegate {
    callbacks: ClipboardCallbacks,
}

impl ClipboardDelegate for FfiClipboardDelegate {
    fn clear(&self, _webview: WebView) {
        if let Some(cb) = self.callbacks.clear {
            cb(self.callbacks.user_data);
        }
    }

    fn get_text(&self, _webview: WebView, request: StringRequest) {
        if let Some(cb) = self.callbacks.get_text {
            let ptr = cb(self.callbacks.user_data);
            if ptr.is_null() {
                request.failure("Clipboard empty".into());
            } else {
                let text = unsafe { CStr::from_ptr(ptr) }.to_str().unwrap_or_default().to_string();
                unsafe { drop(CString::from_raw(ptr)); }
                request.success(text);
            }
        }
    }

    fn set_text(&self, _webview: WebView, new_contents: String) {
        if let Some(cb) = self.callbacks.set_text {
            if let Ok(c) = CString::new(new_contents) {
                cb(self.callbacks.user_data, c.as_ptr());
            }
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_take_screenshot(
    handle: *mut c_void,
    callback: extern "C" fn(*mut c_void, *const u8, u32, u32, usize),
    callback_data: *mut c_void,
) {
    let Some(wv) = wv_ref(handle) else { return; };
    let cb_data = callback_data as usize;
    let cb = callback;
    wv.webview.take_screenshot(None, move |result| {
        let ud = cb_data as *mut c_void;
        match result {
            Ok(image) => {
                let w = image.width();
                let h = image.height();
                let data = image.as_raw();
                cb(ud, data.as_ptr(), w, h, data.len());
            },
            Err(_) => {
                cb(ud, std::ptr::null(), 0, 0, 0);
            },
        }
    });
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_favicon(
    handle: *mut c_void,
    out_width: *mut u32,
    out_height: *mut u32,
    out_format: *mut u8,
    out_len: *mut usize,
) -> *mut u8 {
    let Some(wv) = wv_ref(handle) else { return std::ptr::null_mut(); };
    match wv.webview.favicon() {
        Some(image) => {
            let data = image.data().to_vec();
            let len = data.len();
            unsafe {
                if !out_width.is_null() { *out_width = image.width; }
                if !out_height.is_null() { *out_height = image.height; }
                if !out_format.is_null() {
                    *out_format = match image.format {
                        servo::PixelFormat::K8 => 0,
                        servo::PixelFormat::KA8 => 1,
                        servo::PixelFormat::RGB8 => 2,
                        servo::PixelFormat::RGBA8 => 3,
                        servo::PixelFormat::BGRA8 => 4,
                    };
                }
                if !out_len.is_null() { *out_len = len; }
            }
            let mut boxed = data.into_boxed_slice();
            let ptr = boxed.as_mut_ptr();
            std::mem::forget(boxed);
            ptr
        },
        None => {
            unsafe {
                if !out_width.is_null() { *out_width = 0; }
                if !out_height.is_null() { *out_height = 0; }
                if !out_format.is_null() { *out_format = 0; }
                if !out_len.is_null() { *out_len = 0; }
            }
            std::ptr::null_mut()
        },
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn user_content_manager_new(servo_handle: *mut c_void) -> *mut c_void {
    if servo_handle.is_null() { return std::ptr::null_mut(); }
    let servo = unsafe { &*(servo_handle as *mut Servo) };
    let ucm = UserContentManager::new(servo);
    Box::into_raw(Box::new(ucm)) as *mut c_void
}

#[unsafe(no_mangle)]
pub extern "C" fn user_content_manager_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut UserContentManager)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn user_script_new(
    script: *const c_char,
    source_file: *const c_char,
) -> *mut c_void {
    if script.is_null() { return std::ptr::null_mut(); }
    let script_str = unsafe { CStr::from_ptr(script) }.to_str().unwrap_or_default().to_string();
    let source = if source_file.is_null() {
        None
    } else {
        let s = unsafe { CStr::from_ptr(source_file) }.to_str().unwrap_or_default();
        Some(PathBuf::from(s))
    };
    let us = UserScript::new(script_str, source);
    Box::into_raw(Box::new(Rc::new(us))) as *mut c_void
}

#[unsafe(no_mangle)]
pub extern "C" fn user_script_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut Rc<UserScript>)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn user_stylesheet_new(
    source: *const c_char,
    url: *const c_char,
) -> *mut c_void {
    if source.is_null() || url.is_null() { return std::ptr::null_mut(); }
    let source_str = unsafe { CStr::from_ptr(source) }.to_str().unwrap_or_default().to_string();
    let url_str = unsafe { CStr::from_ptr(url) }.to_str().unwrap_or_default();
    let parsed_url = match Url::parse(url_str) {
        Ok(u) => u,
        Err(_) => {
            set_last_error(format!("Invalid URL for stylesheet: {url_str}"));
            return std::ptr::null_mut();
        }
    };
    let uss = UserStyleSheet::new(source_str, parsed_url);
    Box::into_raw(Box::new(Rc::new(uss))) as *mut c_void
}

#[unsafe(no_mangle)]
pub extern "C" fn user_stylesheet_destroy(handle: *mut c_void) {
    if !handle.is_null() {
        unsafe { drop(Box::from_raw(handle as *mut Rc<UserStyleSheet>)); }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn user_content_manager_add_script(
    ucm_handle: *mut c_void,
    script_handle: *mut c_void,
) {
    if ucm_handle.is_null() || script_handle.is_null() { return; }
    let ucm = unsafe { &*(ucm_handle as *mut UserContentManager) };
    let script = unsafe { &*(script_handle as *mut Rc<UserScript>) };
    ucm.add_script(script.clone());
}

#[unsafe(no_mangle)]
pub extern "C" fn user_content_manager_remove_script(
    ucm_handle: *mut c_void,
    script_handle: *mut c_void,
) {
    if ucm_handle.is_null() || script_handle.is_null() { return; }
    let ucm = unsafe { &*(ucm_handle as *mut UserContentManager) };
    let script = unsafe { &*(script_handle as *mut Rc<UserScript>) };
    ucm.remove_script(script.clone());
}

#[unsafe(no_mangle)]
pub extern "C" fn user_content_manager_add_stylesheet(
    ucm_handle: *mut c_void,
    stylesheet_handle: *mut c_void,
) {
    if ucm_handle.is_null() || stylesheet_handle.is_null() { return; }
    let ucm = unsafe { &*(ucm_handle as *mut UserContentManager) };
    let stylesheet = unsafe { &*(stylesheet_handle as *mut Rc<UserStyleSheet>) };
    ucm.add_stylesheet(stylesheet.clone());
}

#[unsafe(no_mangle)]
pub extern "C" fn user_content_manager_remove_stylesheet(
    ucm_handle: *mut c_void,
    stylesheet_handle: *mut c_void,
) {
    if ucm_handle.is_null() || stylesheet_handle.is_null() { return; }
    let ucm = unsafe { &*(ucm_handle as *mut UserContentManager) };
    let stylesheet = unsafe { &*(stylesheet_handle as *mut Rc<UserStyleSheet>) };
    ucm.remove_stylesheet(stylesheet.clone());
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_site_data(
    handle: *mut c_void,
    storage_types: u8,
) -> *mut c_char {
    if handle.is_null() { return std::ptr::null_mut(); }
    let servo = unsafe { &*(handle as *mut Servo) };
    let st = StorageType::from_bits_truncate(storage_types);
    let data = servo.site_data_manager().site_data(st);
    let json = site_data_to_json(&data);
    CString::new(json).map(|s| s.into_raw()).unwrap_or(std::ptr::null_mut())
}

fn site_data_to_json(data: &[SiteData]) -> String {
    let items: Vec<String> = data.iter().map(|sd| {
        format!(
            r#"{{"name":{},"storage_types":{}}}"#,
            serde_json::to_string(&sd.name()).unwrap_or_else(|_| "\"\"".to_string()),
            sd.storage_types().bits(),
        )
    }).collect();
    format!("[{}]", items.join(","))
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_clear_site_data(
    handle: *mut c_void,
    sites: *const *const c_char,
    sites_len: usize,
    storage_types: u8,
) {
    if handle.is_null() { return; }
    let servo = unsafe { &*(handle as *mut Servo) };
    let st = StorageType::from_bits_truncate(storage_types);
    let mut site_strs: Vec<String> = Vec::with_capacity(sites_len);
    if !sites.is_null() && sites_len > 0 {
        let ptrs = unsafe { std::slice::from_raw_parts(sites, sites_len) };
        for &ptr in ptrs {
            if !ptr.is_null() {
                let s = unsafe { CStr::from_ptr(ptr) }.to_str().unwrap_or_default().to_string();
                site_strs.push(s);
            }
        }
    }
    let refs: Vec<&str> = site_strs.iter().map(|s| s.as_str()).collect();
    servo.site_data_manager().clear_site_data(&refs, st);
}

fn make_completion_callback(
    callback: Option<extern "C" fn(*mut c_void)>,
    user_data: *mut c_void,
) -> Option<Box<dyn FnOnce()>> {
    let cb = callback?;
    // Pass the raw pointer through as usize so the closure is Send.
    let ud = user_data as usize;
    Some(Box::new(move || cb(ud as *mut c_void)))
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_clear_cookies(
    handle: *mut c_void,
    callback: Option<extern "C" fn(*mut c_void)>,
    user_data: *mut c_void,
) {
    if handle.is_null() { return; }
    let servo = unsafe { &*(handle as *mut Servo) };
    servo.site_data_manager().clear_cookies(make_completion_callback(callback, user_data));
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_clear_session_cookies(
    handle: *mut c_void,
    callback: Option<extern "C" fn(*mut c_void)>,
    user_data: *mut c_void,
) {
    if handle.is_null() { return; }
    let servo = unsafe { &*(handle as *mut Servo) };
    servo.site_data_manager().clear_session_cookies(make_completion_callback(callback, user_data));
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_set_cookie_for_url(
    handle: *mut c_void,
    url: *const c_char,
    cookie: *const c_char,
    callback: Option<extern "C" fn(*mut c_void)>,
    user_data: *mut c_void,
) -> i32 {
    if handle.is_null() || url.is_null() || cookie.is_null() { return -1; }
    let servo = unsafe { &*(handle as *mut Servo) };
    let url_str = unsafe { CStr::from_ptr(url) }.to_str().unwrap_or_default();
    let cookie_str = unsafe { CStr::from_ptr(cookie) }.to_str().unwrap_or_default();

    let Ok(parsed_url) = Url::parse(url_str) else { return -1; };
    let Ok(parsed_cookie) = Cookie::parse(cookie_str.to_string()) else { return -1; };

    servo.site_data_manager().set_cookie_for_url(
        parsed_url,
        parsed_cookie.into_owned(),
        make_completion_callback(callback, user_data),
    );
    0
}

fn cookie_source_from_u8(source: u8) -> CookieSource {
    match source {
        1 => CookieSource::NonHTTP,
        _ => CookieSource::HTTP,
    }
}

fn cookie_to_json(c: &Cookie<'static>) -> serde_json::Value {
    let same_site = c.same_site().map(|s| match s {
        cookie::SameSite::Strict => "Strict",
        cookie::SameSite::Lax => "Lax",
        cookie::SameSite::None => "None",
    });
    serde_json::json!({
        "name": c.name(),
        "value": c.value(),
        "domain": c.domain(),
        "path": c.path(),
        "secure": c.secure(),
        "httpOnly": c.http_only(),
        "sameSite": same_site,
        "expires": c.expires().and_then(|e| e.datetime()).map(|d| d.unix_timestamp()),
        "maxAge": c.max_age().map(|d| d.whole_seconds()),
    })
}

fn cookies_to_json(cookies: &[Cookie<'static>]) -> String {
    serde_json::Value::Array(cookies.iter().map(cookie_to_json).collect()).to_string()
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_cookies_for_url(
    handle: *mut c_void,
    url: *const c_char,
    source: u8,
) -> *mut c_char {
    if handle.is_null() || url.is_null() { return std::ptr::null_mut(); }
    let servo = unsafe { &*(handle as *mut Servo) };
    let url_str = unsafe { CStr::from_ptr(url) }.to_str().unwrap_or_default();
    let Ok(parsed_url) = Url::parse(url_str) else { return std::ptr::null_mut(); };

    let cookies = servo
        .site_data_manager()
        .cookies_for_url(parsed_url, cookie_source_from_u8(source));
    CString::new(cookies_to_json(&cookies))
        .map(|s| s.into_raw())
        .unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_cookies_for_url_async(
    handle: *mut c_void,
    url: *const c_char,
    source: u8,
    callback: extern "C" fn(*mut c_void, *const c_char),
    user_data: *mut c_void,
) -> i32 {
    if handle.is_null() || url.is_null() { return -1; }
    let servo = unsafe { &*(handle as *mut Servo) };
    let url_str = unsafe { CStr::from_ptr(url) }.to_str().unwrap_or_default();
    let Ok(parsed_url) = Url::parse(url_str) else { return -1; };

    let cb_data = user_data as usize;
    servo.site_data_manager().cookies_for_url_async(
        parsed_url,
        cookie_source_from_u8(source),
        move |cookies| {
            let ud = cb_data as *mut c_void;
            match CString::new(cookies_to_json(&cookies)) {
                Ok(c) => callback(ud, c.as_ptr()),
                Err(_) => callback(ud, std::ptr::null()),
            }
        },
    );
    0
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_cache_entries(handle: *mut c_void) -> *mut c_char {
    if handle.is_null() { return std::ptr::null_mut(); }
    let servo = unsafe { &*(handle as *mut Servo) };
    let entries = servo.network_manager().cache_entries();
    let items: Vec<String> = entries.iter().map(|e| {
        serde_json::to_string(e.key()).unwrap_or_else(|_| "\"\"".to_string())
    }).collect();
    let json = format!("[{}]", items.join(","));
    CString::new(json).map(|s| s.into_raw()).unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_clear_cache(handle: *mut c_void) {
    if handle.is_null() { return; }
    let servo = unsafe { &*(handle as *mut Servo) };
    servo.network_manager().clear_cache();
}

#[unsafe(no_mangle)]
pub extern "C" fn file_picker_select_and_submit(
    handle: usize,
    paths: *const *const c_char,
    paths_len: usize,
) {
    if handle == 0 { return; }
    let mut picker = unsafe { *Box::from_raw(handle as *mut FilePicker) };
    let mut path_bufs = Vec::with_capacity(paths_len);
    if !paths.is_null() && paths_len > 0 {
        let ptrs = unsafe { std::slice::from_raw_parts(paths, paths_len) };
        for &ptr in ptrs {
            if !ptr.is_null() {
                let s = unsafe { CStr::from_ptr(ptr) }.to_str().unwrap_or_default();
                path_bufs.push(PathBuf::from(s));
            }
        }
    }
    picker.select(&path_bufs);
    picker.submit();
}

#[unsafe(no_mangle)]
pub extern "C" fn file_picker_dismiss(handle: usize) {
    if handle == 0 { return; }
    let picker = unsafe { *Box::from_raw(handle as *mut FilePicker) };
    picker.dismiss();
}

#[unsafe(no_mangle)]
pub extern "C" fn color_picker_select_and_submit(
    handle: usize,
    has_color: u8,
    r: u8, g: u8, b: u8,
) {
    if handle == 0 { return; }
    let mut picker = unsafe { *Box::from_raw(handle as *mut ColorPicker) };
    let color = if has_color != 0 {
        Some(RgbColor { red: r, green: g, blue: b })
    } else {
        None
    };
    picker.select(color);
    picker.submit();
}

#[unsafe(no_mangle)]
pub extern "C" fn color_picker_dismiss(handle: usize) {
    if handle == 0 { return; }
    let _ = unsafe { Box::from_raw(handle as *mut ColorPicker) };
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_notify_theme_change(handle: *mut c_void, theme: u8) {
    if let Some(wv) = wv_ref(handle) {
        let t = match theme {
            1 => Theme::Dark,
            _ => Theme::Light,
        };
        wv.webview.notify_theme_change(t);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_notify_media_session_action(handle: *mut c_void, action: u8) {
    if let Some(wv) = wv_ref(handle) {
        let action_type = match action {
            0 => MediaSessionActionType::Play,
            1 => MediaSessionActionType::Pause,
            2 => MediaSessionActionType::SeekBackward,
            3 => MediaSessionActionType::SeekForward,
            4 => MediaSessionActionType::PreviousTrack,
            5 => MediaSessionActionType::NextTrack,
            6 => MediaSessionActionType::SkipAd,
            7 => MediaSessionActionType::Stop,
            8 => MediaSessionActionType::SeekTo,
            _ => return,
        };
        wv.webview.notify_media_session_action_event(action_type);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_adjust_pinch_zoom(
    handle: *mut c_void, delta: f32, center_x: f32, center_y: f32,
) {
    if let Some(wv) = wv_ref(handle) {
        wv.webview.adjust_pinch_zoom(delta, DevicePoint::new(center_x, center_y));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_get_pinch_zoom(handle: *mut c_void) -> f32 {
    wv_ref(handle).map(|wv| wv.webview.pinch_zoom()).unwrap_or(1.0)
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_ime_composition(
    handle: *mut c_void, state: u8, data: *const c_char,
) {
    let Some(wv) = wv_ref(handle) else { return; };
    let data_str = if data.is_null() {
        String::new()
    } else {
        unsafe { CStr::from_ptr(data) }.to_str().unwrap_or_default().to_string()
    };
    let comp_state = match state {
        0 => CompositionState::Start,
        1 => CompositionState::Update,
        _ => CompositionState::End,
    };
    wv.webview.notify_input_event(InputEvent::Ime(ImeEvent::Composition(
        CompositionEvent { state: comp_state, data: data_str },
    )));
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_send_ime_dismissed(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) {
        wv.webview.notify_input_event(InputEvent::Ime(ImeEvent::Dismissed));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_toggle_webrender_debugging(handle: *mut c_void, option: u8) {
    if let Some(wv) = wv_ref(handle) {
        let debug_option = match option {
            0 => WebRenderDebugOption::Profiler,
            1 => WebRenderDebugOption::TextureCacheDebug,
            2 => WebRenderDebugOption::RenderTargetDebug,
            _ => return,
        };
        wv.webview.toggle_webrender_debugging(debug_option);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_capture_webrender(handle: *mut c_void) {
    if let Some(wv) = wv_ref(handle) {
        wv.webview.capture_webrender();
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_toggle_sampling_profiler(
    handle: *mut c_void, rate_ms: u64, max_duration_ms: u64,
) {
    if let Some(wv) = wv_ref(handle) {
        wv.webview.toggle_sampling_profiler(
            std::time::Duration::from_millis(rate_ms),
            std::time::Duration::from_millis(max_duration_ms),
        );
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn servo_initialize_gl_accelerated_media(
    display_type: u8, display_ptr: usize,
    api: u8,
    context_type: u8, context_ptr: usize,
) {
    let display = match display_type {
        0 => MediaNativeDisplay::Egl(display_ptr),
        1 => MediaNativeDisplay::X11(display_ptr),
        2 => MediaNativeDisplay::Wayland(display_ptr),
        3 => MediaNativeDisplay::Headless,
        _ => MediaNativeDisplay::Unknown,
    };
    let gl_api = match api {
        0 => MediaGlApi::OpenGL,
        1 => MediaGlApi::OpenGL3,
        2 => MediaGlApi::Gles1,
        3 => MediaGlApi::Gles2,
        _ => MediaGlApi::None,
    };
    let context = match context_type {
        0 => MediaGlContext::Egl(context_ptr),
        1 => MediaGlContext::Glx(context_ptr),
        _ => MediaGlContext::Unknown,
    };
    Servo::initialize_gl_accelerated_media(display, gl_api, context);
}
