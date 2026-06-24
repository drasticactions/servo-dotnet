use std::ffi::c_void;
use std::sync::mpsc::{Receiver, Sender, channel};
use std::thread::JoinHandle;

use crate::shared_event;

#[link(name = "OpenGL", kind = "framework")]
unsafe extern "C" {
    fn CGLGetCurrentContext() -> *mut c_void;
    fn CGLGetPixelFormat(ctx: *mut c_void) -> *mut c_void;
    fn CGLCreateContext(
        pixel_format: *mut c_void,
        share: *mut c_void,
        out_ctx: *mut *mut c_void,
    ) -> i32;
    fn CGLDestroyContext(ctx: *mut c_void) -> i32;
    fn CGLSetCurrentContext(ctx: *mut c_void) -> i32;
    fn glClientWaitSync(sync: *const c_void, flags: u32, timeout_ns: u64) -> u32;
    fn glDeleteSync(sync: *const c_void);
}

const GL_TIMEOUT_EXPIRED: u32 = 0x911B;
const WAIT_SLICE_NS: u64 = 100_000_000;
const MAX_WAIT_SLICES: u32 = 20;

struct Job {
    sync: *const c_void,
    semaphore: *mut c_void,
    value: u64,
}

unsafe impl Send for Job {}

struct WorkerContext(*mut c_void);
unsafe impl Send for WorkerContext {}

pub struct FenceWorker {
    sender: Option<Sender<Job>>,
    thread: Option<JoinHandle<()>>,
}

impl FenceWorker {
    pub fn new() -> Option<FenceWorker> {
        let producer = unsafe { CGLGetCurrentContext() };
        if producer.is_null() {
            return None;
        }
        let pixel_format = unsafe { CGLGetPixelFormat(producer) };
        if pixel_format.is_null() {
            return None;
        }
        let mut worker_ctx: *mut c_void = std::ptr::null_mut();
        let err = unsafe { CGLCreateContext(pixel_format, producer, &mut worker_ctx) };
        if err != 0 || worker_ctx.is_null() {
            log::warn!("fence worker: CGLCreateContext failed ({err}); using glFinish");
            return None;
        }
        let context = WorkerContext(worker_ctx);
        let (sender, receiver) = channel::<Job>();
        match std::thread::Builder::new()
            .name("servo-fence-worker".into())
            .spawn(move || run(context, receiver))
        {
            Ok(thread) => Some(FenceWorker {
                sender: Some(sender),
                thread: Some(thread),
            }),
            Err(e) => {
                log::warn!("fence worker: thread spawn failed ({e}); using glFinish");
                unsafe { CGLDestroyContext(worker_ctx) };
                None
            },
        }
    }

    pub fn submit(&self, sync: *const c_void, semaphore: *mut c_void, value: u64) -> bool {
        let Some(sender) = self.sender.as_ref() else {
            return false;
        };
        let retained = unsafe { shared_event::retain(semaphore) };
        if retained.is_null() {
            return false;
        }
        let job = Job { sync, semaphore: retained, value };
        if sender.send(job).is_err() {
            unsafe { shared_event::destroy(retained) };
            return false;
        }
        true
    }
}

impl Drop for FenceWorker {
    fn drop(&mut self) {
        drop(self.sender.take());
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn run(context: WorkerContext, receiver: Receiver<Job>) {
    let current = unsafe { CGLSetCurrentContext(context.0) } == 0;
    if !current {
        log::warn!("fence worker: CGLSetCurrentContext failed; signaling without GPU waits");
    }
    for job in receiver {
        if current {
            let mut slices = 0;
            loop {
                let status = unsafe { glClientWaitSync(job.sync, 0, WAIT_SLICE_NS) };
                if status != GL_TIMEOUT_EXPIRED {
                    break;
                }
                slices += 1;
                if slices >= MAX_WAIT_SLICES {
                    log::warn!("fence worker: fence for value {} never signaled", job.value);
                    break;
                }
            }
            unsafe { glDeleteSync(job.sync) };
        }
        unsafe {
            shared_event::signal(job.semaphore, job.value);
            shared_event::destroy(job.semaphore);
        }
    }
    unsafe {
        CGLSetCurrentContext(std::ptr::null_mut());
        CGLDestroyContext(context.0);
    }
}
