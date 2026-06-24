#[cfg(target_os = "macos")]
mod imp {
    use std::ffi::c_void;

    use objc2::rc::Retained;
    use objc2::runtime::ProtocolObject;
    use objc2_metal::{MTLCreateSystemDefaultDevice, MTLDevice, MTLSharedEvent};

    pub fn new() -> *mut c_void {
        let Some(device) = MTLCreateSystemDefaultDevice() else {
            return std::ptr::null_mut();
        };
        let Some(event) = device.newSharedEvent() else {
            return std::ptr::null_mut();
        };
        Retained::into_raw(event) as *mut c_void
    }

    unsafe fn event<'a>(handle: *mut c_void) -> &'a ProtocolObject<dyn MTLSharedEvent> {
        unsafe { &*(handle as *const ProtocolObject<dyn MTLSharedEvent>) }
    }

    pub unsafe fn destroy(handle: *mut c_void) {
        let _ = unsafe { Retained::from_raw(handle as *mut ProtocolObject<dyn MTLSharedEvent>) };
    }

    pub unsafe fn retain(handle: *mut c_void) -> *mut c_void {
        let event =
            unsafe { Retained::retain(handle as *mut ProtocolObject<dyn MTLSharedEvent>) };
        event.map_or(std::ptr::null_mut(), |e| Retained::into_raw(e) as *mut c_void)
    }

    pub unsafe fn signal(handle: *mut c_void, value: u64) {
        unsafe { event(handle) }.setSignaledValue(value);
    }

    pub unsafe fn signaled_value(handle: *mut c_void) -> u64 {
        unsafe { event(handle) }.signaledValue()
    }
}

#[cfg(not(target_os = "macos"))]
mod imp {
    use std::ffi::c_void;

    pub fn new() -> *mut c_void {
        std::ptr::null_mut()
    }
    pub unsafe fn destroy(_handle: *mut c_void) {}
    pub unsafe fn retain(_handle: *mut c_void) -> *mut c_void {
        std::ptr::null_mut()
    }
    pub unsafe fn signal(_handle: *mut c_void, _value: u64) {}
    pub unsafe fn signaled_value(_handle: *mut c_void) -> u64 {
        0
    }
}

pub use imp::*;
