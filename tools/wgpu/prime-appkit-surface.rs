// Local extension for the pinned wgpu-native ABI. Its MetalLayer descriptor
// calls the Metal-only core entry point, which panics for a Vulkan instance.
// A raw AppKit view instead lets core create the selected backend's surface.
#[cfg(target_os = "macos")]
#[no_mangle]
pub unsafe extern "C" fn primeInstanceCreateSurfaceAppKit(
    instance: native::WGPUInstance,
    view: *mut std::ffi::c_void,
) -> native::WGPUSurface {
    let Some(instance) = instance.as_ref() else { return std::ptr::null(); };
    let Some(view) = std::ptr::NonNull::new(view) else { return std::ptr::null(); };
    let context = &instance.context;
    let display = raw_window_handle::AppKitDisplayHandle::new();
    let window = raw_window_handle::AppKitWindowHandle::new(view);
    let id = match context.instance_create_surface(display.into(), window.into(), ()) {
        Ok(id) => id,
        Err(error) => {
            eprintln!("[prime-wgpu] AppKit surface creation failed: {error}");
            return std::ptr::null();
        }
    };
    Arc::into_raw(Arc::new(WGPUSurfaceImpl {
        context: context.clone(),
        id,
        data: Mutex::default(),
        has_surface_presented: Arc::default(),
    }))
}
