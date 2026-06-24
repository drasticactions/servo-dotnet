use servo::EventLoopWaker;

use crate::types::CEventLoopWaker;

pub struct FfiEventLoopWaker {
    inner: CEventLoopWaker,
}

impl FfiEventLoopWaker {
    pub fn new(waker: CEventLoopWaker) -> Self {
        Self { inner: waker }
    }
}

unsafe impl Send for FfiEventLoopWaker {}
unsafe impl Sync for FfiEventLoopWaker {}

impl EventLoopWaker for FfiEventLoopWaker {
    fn clone_box(&self) -> Box<dyn EventLoopWaker> {
        Box::new(FfiEventLoopWaker {
            inner: CEventLoopWaker {
                user_data: self.inner.user_data,
                wake: self.inner.wake,
            },
        })
    }

    fn wake(&self) {
        (self.inner.wake)(self.inner.user_data);
    }
}
