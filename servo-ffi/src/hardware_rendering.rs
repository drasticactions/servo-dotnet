use std::cell::Cell;
use std::collections::HashMap;
use std::ffi::c_void;
use std::rc::Rc;
use std::sync::Arc;

use dpi::PhysicalSize;
use euclid::Size2D;
use servo::{DeviceIntRect, RenderingContext, RgbaImage};
use surfman::{
    Connection, Context, ContextAttributeFlags, ContextAttributes, Device, Error, GLApi,
    SurfaceAccess, SurfaceType, Surface, SurfaceTexture,
    chains::{PreserveBuffer, SwapChain, SwapChainAPI},
};

pub const FRAME_EXPORT_NONE: u32 = 0;
pub const FRAME_EXPORT_IOSURFACE: u32 = 1;
pub const FRAME_EXPORT_D3D11_SHARED_HANDLE: u32 = 2;

pub struct AcquiredFrame {
    pub frame_id: u64,
    pub native_handle: *mut c_void,
    pub width: u32,
    pub height: u32,
}

pub struct HardwareRenderingContext {
    size: Cell<PhysicalSize<u32>>,
    gleam_gl: Rc<dyn gleam::gl::Gl>,
    glow_gl: Arc<glow::Context>,
    device: std::cell::RefCell<Device>,
    context: std::cell::RefCell<Context>,
    swap_chain: SwapChain<Device>,
    taken_frames: std::cell::RefCell<HashMap<u64, Surface>>,
    next_frame_id: Cell<u64>,
    #[cfg(target_os = "windows")]
    d3d11_keyed_mutex: bool,
    #[cfg(target_os = "macos")]
    fence_worker: std::cell::OnceCell<Option<crate::fence_worker::FenceWorker>>,
}

impl HardwareRenderingContext {
    pub fn new(size: PhysicalSize<u32>) -> Result<Self, Error> {
        let connection = Connection::new()?;
        let adapter = connection.create_hardware_adapter()?;
        let device = connection.create_device(&adapter)?;

        let flags = ContextAttributeFlags::ALPHA
            | ContextAttributeFlags::DEPTH
            | ContextAttributeFlags::STENCIL;
        let gl_api = connection.gl_api();
        let version = match &gl_api {
            GLApi::GLES => surfman::GLVersion { major: 3, minor: 0 },
            GLApi::GL => surfman::GLVersion { major: 3, minor: 2 },
        };
        let context_descriptor =
            device.create_context_descriptor(&ContextAttributes { flags, version })?;
        let context = device.create_context(&context_descriptor, None)?;

        #[expect(unsafe_code)]
        let gleam_gl = unsafe {
            match gl_api {
                GLApi::GL => gleam::gl::GlFns::load_with(|s| device.get_proc_address(&context, s)),
                GLApi::GLES => gleam::gl::GlesFns::load_with(|s| device.get_proc_address(&context, s)),
            }
        };

        #[expect(unsafe_code)]
        let glow_gl = unsafe {
            glow::Context::from_loader_function(|s| device.get_proc_address(&context, s))
        };

        let surfman_size = Size2D::new(size.width as i32, size.height as i32);
        let surface = device.create_surface(
            &context,
            SurfaceAccess::GPUOnly,
            SurfaceType::Generic { size: surfman_size },
        )?;
        let mut context = context;
        device
            .bind_surface_to_context(&mut context, surface)
            .map_err(|(e, _)| e)?;
        device.make_context_current(&context)?;

        let mut device = device;
        let swap_chain = SwapChain::create_attached(
            &mut device,
            &mut context,
            SurfaceAccess::GPUOnly,
        )?;
        #[cfg(target_os = "windows")]
        let d3d11_keyed_mutex = probe_keyed_mutex(&device, &mut context);
        let device = std::cell::RefCell::new(device);
        let context = std::cell::RefCell::new(context);

        Ok(HardwareRenderingContext {
            size: Cell::new(size),
            gleam_gl,
            glow_gl: Arc::new(glow_gl),
            device,
            context,
            swap_chain,
            taken_frames: std::cell::RefCell::new(HashMap::new()),
            next_frame_id: Cell::new(1),
            #[cfg(target_os = "windows")]
            d3d11_keyed_mutex,
            #[cfg(target_os = "macos")]
            fence_worker: std::cell::OnceCell::new(),
        })
    }

    pub fn frame_export_kind(&self) -> u32 {
        #[cfg(target_os = "macos")]
        return FRAME_EXPORT_IOSURFACE;
        #[cfg(target_os = "windows")]
        return if self.d3d11_keyed_mutex {
            FRAME_EXPORT_D3D11_SHARED_HANDLE
        } else {
            FRAME_EXPORT_NONE
        };
        #[cfg(not(any(target_os = "macos", target_os = "windows")))]
        FRAME_EXPORT_NONE
    }

    pub fn acquire_frame(&self) -> Option<AcquiredFrame> {
        let _ = self.make_current();
        self.gleam_gl.flush();
        let surface = self.swap_chain.take_pending_surface()?;
        self.export_frame(surface)
    }

    #[cfg(target_os = "macos")]
    fn export_frame(&self, surface: Surface) -> Option<AcquiredFrame> {
        let (native_handle, width, height) = {
            let device = self.device.borrow();
            let info = device.surface_info(&surface);
            let native = device.native_surface(&surface);
            let ptr = &*native.0 as *const _ as *mut c_void;
            (ptr, info.size.width as u32, info.size.height as u32)
        };
        let frame_id = self.next_frame_id.get();
        self.next_frame_id.set(frame_id + 1);
        self.taken_frames.borrow_mut().insert(frame_id, surface);
        Some(AcquiredFrame { frame_id, native_handle, width, height })
    }

    #[cfg(target_os = "windows")]
    fn export_frame(&self, surface: Surface) -> Option<AcquiredFrame> {
        let share_handle = if self.d3d11_keyed_mutex {
            surface.share_handle()
        } else {
            None
        };
        let Some(native_handle) = share_handle else {
            self.swap_chain.recycle_surface(surface);
            return None;
        };
        let info = self.device.borrow().surface_info(&surface);
        let frame_id = self.next_frame_id.get();
        self.next_frame_id.set(frame_id + 1);
        self.taken_frames.borrow_mut().insert(frame_id, surface);
        Some(AcquiredFrame {
            frame_id,
            native_handle: native_handle as *mut c_void,
            width: info.size.width as u32,
            height: info.size.height as u32,
        })
    }

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    fn export_frame(&self, surface: Surface) -> Option<AcquiredFrame> {
        self.swap_chain.recycle_surface(surface);
        None
    }

    pub fn signal_after_gpu_work(&self, semaphore: *mut c_void, value: u64) {
        let _ = self.make_current();
        #[cfg(target_os = "macos")]
        {
            let worker = self
                .fence_worker
                .get_or_init(crate::fence_worker::FenceWorker::new);
            if let Some(worker) = worker {
                let sync = self
                    .gleam_gl
                    .fence_sync(gleam::gl::SYNC_GPU_COMMANDS_COMPLETE, 0);
                if !sync.is_null() {
                    self.gleam_gl.flush();
                    if worker.submit(sync as *const c_void, semaphore, value) {
                        return;
                    }
                    self.gleam_gl.delete_sync(sync);
                }
            }
        }
        self.gleam_gl.finish();
        #[expect(unsafe_code)]
        unsafe {
            crate::shared_event::signal(semaphore, value)
        };
    }

    pub fn release_frame(&self, frame_id: u64) {
        let Some(mut surface) = self.taken_frames.borrow_mut().remove(&frame_id) else {
            return;
        };
        let device = self.device.borrow();
        let info = device.surface_info(&surface);
        let current = self.size.get();
        if info.size.width as u32 == current.width && info.size.height as u32 == current.height {
            drop(device);
            self.swap_chain.recycle_surface(surface);
        } else {
            let mut context = self.context.borrow_mut();
            let _ = device.destroy_surface(&mut context, &mut surface);
        }
    }
}

impl Drop for HardwareRenderingContext {
    fn drop(&mut self) {
        #[cfg(target_os = "macos")]
        drop(self.fence_worker.take());
        if let (Ok(mut device), Ok(mut context)) =
            (self.device.try_borrow_mut(), self.context.try_borrow_mut())
        {
            for (_, mut surface) in self.taken_frames.borrow_mut().drain() {
                let _ = device.destroy_surface(&mut context, &mut surface);
            }
            let _ = self.swap_chain.destroy(&mut device, &mut context);
            let _ = device.destroy_context(&mut context);
        }
    }
}

impl RenderingContext for HardwareRenderingContext {
    fn prepare_for_rendering(&self) {
        let device = &self.device.borrow();
        let context = &self.context.borrow();
        let framebuffer_id = device
            .context_surface_info(context)
            .unwrap_or(None)
            .and_then(|info| info.framebuffer_object)
            .map_or(0, |fb| fb.0.into());
        self.gleam_gl
            .bind_framebuffer(gleam::gl::FRAMEBUFFER, framebuffer_id);
    }

    fn read_to_image(&self, source_rectangle: DeviceIntRect) -> Option<RgbaImage> {
        let device = &self.device.borrow();
        let context = &self.context.borrow();
        let framebuffer_id = device
            .context_surface_info(context)
            .unwrap_or(None)
            .and_then(|info| info.framebuffer_object)
            .map_or(0, |fb| fb.0.into());

        use gleam::gl;
        let gl = &self.gleam_gl;
        gl.bind_framebuffer(gl::FRAMEBUFFER, framebuffer_id);
        gl.bind_vertex_array(0);

        let pixels = gl.read_pixels(
            source_rectangle.min.x,
            source_rectangle.min.y,
            source_rectangle.width(),
            source_rectangle.height(),
            gl::RGBA,
            gl::UNSIGNED_BYTE,
        );

        let width = source_rectangle.width() as usize;
        let height = source_rectangle.height() as usize;
        let stride = width * 4;
        let mut flipped = vec![0u8; pixels.len()];
        for y in 0..height {
            let src_row = &pixels[y * stride..(y + 1) * stride];
            let dst_row = &mut flipped[(height - 1 - y) * stride..(height - y) * stride];
            dst_row.copy_from_slice(src_row);
        }

        RgbaImage::from_raw(width as u32, height as u32, flipped)
    }

    fn size(&self) -> PhysicalSize<u32> {
        self.size.get()
    }

    fn resize(&self, size: PhysicalSize<u32>) {
        if self.size.get() == size {
            return;
        }
        self.size.set(size);
        let device = &mut self.device.borrow_mut();
        let context = &mut self.context.borrow_mut();
        let size = Size2D::new(size.width as i32, size.height as i32);
        let _ = self.swap_chain.resize(device, context, size);
    }

    fn present(&self) {
        let device = &mut self.device.borrow_mut();
        let context = &mut self.context.borrow_mut();
        #[cfg(target_os = "windows")]
        if self.d3d11_keyed_mutex {
            let _ = device.make_context_current(context);
            self.gleam_gl.flush();
        }
        let _ = self
            .swap_chain
            .swap_buffers(device, context, PreserveBuffer::No);
    }

    fn make_current(&self) -> Result<(), Error> {
        let device = &self.device.borrow();
        let context = &self.context.borrow();
        device.make_context_current(context)
    }

    fn gleam_gl_api(&self) -> Rc<dyn gleam::gl::Gl> {
        self.gleam_gl.clone()
    }

    fn glow_gl_api(&self) -> Arc<glow::Context> {
        self.glow_gl.clone()
    }

    fn create_texture(
        &self,
        surface: Surface,
    ) -> Option<(SurfaceTexture, u32, euclid::default::Size2D<i32>)> {
        let device = &self.device.borrow();
        let context = &mut self.context.borrow_mut();
        let info = device.surface_info(&surface);
        let size = info.size;
        let surface_texture = device.create_surface_texture(context, surface).ok()?;
        let gl_texture = device
            .surface_texture_object(&surface_texture)
            .map(|tex| tex.0.get())
            .unwrap_or(0);
        Some((surface_texture, gl_texture, size))
    }

    fn destroy_texture(&self, surface_texture: SurfaceTexture) -> Option<Surface> {
        let device = &self.device.borrow();
        let context = &mut self.context.borrow_mut();
        device
            .destroy_surface_texture(context, surface_texture)
            .map_err(|(e, _)| e)
            .ok()
    }

    fn connection(&self) -> Option<Connection> {
        Some(self.device.borrow().connection())
    }
}

#[cfg(target_os = "windows")]
fn probe_keyed_mutex(device: &Device, context: &mut Context) -> bool {
    let Ok(mut surface) = device.create_surface(
        context,
        SurfaceAccess::GPUOnly,
        SurfaceType::Generic { size: Size2D::new(4, 4) },
    ) else {
        return false;
    };
    let supported = surface
        .share_handle()
        .is_some_and(share_handle_has_keyed_mutex);
    let _ = device.destroy_surface(context, &mut surface);
    supported
}

#[cfg(target_os = "windows")]
fn share_handle_has_keyed_mutex(share_handle: *mut c_void) -> bool {
    use windows::Win32::Foundation::{HANDLE, HMODULE};
    use windows::Win32::Graphics::Direct3D::D3D_DRIVER_TYPE_HARDWARE;
    use windows::Win32::Graphics::Direct3D11::{
        D3D11_CREATE_DEVICE_FLAG, D3D11_SDK_VERSION, D3D11CreateDevice, ID3D11Device,
        ID3D11Texture2D,
    };
    use windows::Win32::Graphics::Dxgi::IDXGIKeyedMutex;
    use windows::core::Interface;

    let mut d3d_device: Option<ID3D11Device> = None;
    #[expect(unsafe_code)]
    let created = unsafe {
        D3D11CreateDevice(
            None,
            D3D_DRIVER_TYPE_HARDWARE,
            HMODULE::default(),
            D3D11_CREATE_DEVICE_FLAG(0),
            None,
            D3D11_SDK_VERSION,
            Some(&mut d3d_device),
            None,
            None,
        )
    };
    if created.is_err() {
        return false;
    }
    let Some(d3d_device) = d3d_device else {
        return false;
    };
    let mut texture: Option<ID3D11Texture2D> = None;
    #[expect(unsafe_code)]
    let opened = unsafe { d3d_device.OpenSharedResource(HANDLE(share_handle), &mut texture) };
    opened.is_ok() && texture.is_some_and(|texture| texture.cast::<IDXGIKeyedMutex>().is_ok())
}