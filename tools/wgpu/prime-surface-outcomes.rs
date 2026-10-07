// Additive ABI for Project Prime's pinned wgpu-native. Never calls the upstream
// fatal surface wrappers: device failures and WSI status are returned to C#.
const PRIME_WGPU_SUCCESS: u32 = 0;
const PRIME_WGPU_TIMEOUT: u32 = 1;
const PRIME_WGPU_OUTDATED: u32 = 2;
const PRIME_WGPU_SURFACE_LOST: u32 = 3;
const PRIME_WGPU_DEVICE_LOST: u32 = 4;
const PRIME_WGPU_OUT_OF_MEMORY: u32 = 5;
const PRIME_WGPU_VALIDATION: u32 = 6;
const PRIME_WGPU_INTERNAL: u32 = 7;

unsafe fn prime_message(message: *mut u8, capacity: usize, text: &str) {
    if !message.is_null() && capacity != 0 {
        let count = text.len().min(capacity - 1);
        std::ptr::copy_nonoverlapping(text.as_ptr(), message, count);
        *message.add(count) = 0;
    }
}

fn prime_device_error(error: &wgc::device::DeviceError) -> u32 {
    match error {
        wgc::device::DeviceError::Lost => PRIME_WGPU_DEVICE_LOST,
        wgc::device::DeviceError::OutOfMemory => PRIME_WGPU_OUT_OF_MEMORY,
        _ => PRIME_WGPU_VALIDATION,
    }
}

fn prime_surface_error(error: &wgc::present::SurfaceError) -> u32 {
    match error {
        wgc::present::SurfaceError::Device(error) => prime_device_error(error),
        wgc::present::SurfaceError::NotConfigured => PRIME_WGPU_OUTDATED,
        wgc::present::SurfaceError::Invalid => PRIME_WGPU_SURFACE_LOST,
        _ => PRIME_WGPU_VALIDATION,
    }
}

fn prime_configure_error(error: &wgc::present::ConfigureSurfaceError) -> u32 {
    match error {
        wgc::present::ConfigureSurfaceError::Device(error) => prime_device_error(error),
        wgc::present::ConfigureSurfaceError::InvalidSurface => PRIME_WGPU_SURFACE_LOST,
        wgc::present::ConfigureSurfaceError::StuckGpu => PRIME_WGPU_DEVICE_LOST,
        _ => PRIME_WGPU_VALIDATION,
    }
}

fn prime_surface_status(status: wgt::SurfaceStatus) -> u32 {
    match status {
        wgt::SurfaceStatus::Good | wgt::SurfaceStatus::Suboptimal => PRIME_WGPU_SUCCESS,
        wgt::SurfaceStatus::Timeout => PRIME_WGPU_TIMEOUT,
        wgt::SurfaceStatus::Outdated => PRIME_WGPU_OUTDATED,
        wgt::SurfaceStatus::Lost => PRIME_WGPU_SURFACE_LOST,
    }
}

#[no_mangle]
pub extern "C" fn primeWgpuBridgeVersion() -> u32 { 1 }

// This test-only feature is never built into a production artifact. Injection
// happens before native pointer access, so subprocess tests need no GPU/window.
#[cfg(feature = "prime-fault-injection")]
static PRIME_WGPU_FAULT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

#[cfg(feature = "prime-fault-injection")]
#[no_mangle]
pub extern "C" fn primeWgpuInjectSurfaceOutcome(operation: u32, outcome: u32) {
    PRIME_WGPU_FAULT.store(((operation as u64) << 32) | outcome as u64,
        atomic::Ordering::SeqCst);
}

unsafe fn prime_take_fault(operation: u32, message: *mut u8, capacity: usize) -> Option<u32> {
    #[cfg(feature = "prime-fault-injection")]
    {
        let fault = PRIME_WGPU_FAULT.load(atomic::Ordering::SeqCst);
        if fault >> 32 == operation as u64 && fault != 0 {
            PRIME_WGPU_FAULT.store(0, atomic::Ordering::SeqCst);
            prime_message(message, capacity, "injected native surface outcome");
            return Some(fault as u32);
        }
    }
    let _ = (operation, message, capacity);
    None
}

#[no_mangle]
pub unsafe extern "C" fn primeWgpuSurfaceConfigure(
    surface: native::WGPUSurface, config: *const native::WGPUSurfaceConfiguration,
    message: *mut u8, capacity: usize,
) -> u32 {
    prime_message(message, capacity, "");
    if let Some(outcome) = prime_take_fault(1, message, capacity) { return outcome; }
    let (Some(surface), Some(config)) = (surface.as_ref(), config.as_ref()) else {
        prime_message(message, capacity, "configure requires surface and configuration");
        return PRIME_WGPU_VALIDATION;
    };
    let Some(device) = config.device.as_ref() else {
        prime_message(message, capacity, "configure requires a device");
        return PRIME_WGPU_VALIDATION;
    };
    if conv::map_texture_format(config.format).is_none()
        || conv::map_composite_alpha_mode(config.alphaMode).is_err()
        || !matches!(config.presentMode, native::WGPUPresentMode_Fifo
            | native::WGPUPresentMode_FifoRelaxed | native::WGPUPresentMode_Immediate
            | native::WGPUPresentMode_Mailbox) {
        prime_message(message, capacity, "configure contains an invalid format, alpha or present mode");
        return PRIME_WGPU_VALIDATION;
    }
    if !Arc::ptr_eq(&surface.context, &device.context) {
        prime_message(message, capacity, "surface and device belong to different instances");
        return PRIME_WGPU_VALIDATION;
    }
    let context = &device.context;
    let surface_config = follow_chain!(map_surface_configuration(
        (config), WGPUSType_SurfaceConfigurationExtras => native::WGPUSurfaceConfigurationExtras
    ));
    if let Some(error) = gfx_select!(device.id => context.surface_configure(
        surface.id, device.id, &surface_config)) {
        let outcome = prime_configure_error(&error);
        prime_message(message, capacity, &format_error(context, &error));
        return outcome;
    }
    *surface.data.lock() = Some(SurfaceData {
        device_id: device.id,
        error_sink: device.error_sink.clone(),
        texture_data: TextureData {
            usage: config.usage, dimension: native::WGPUTextureDimension_2D,
            format: config.format, mip_level_count: 1,
            size: native::WGPUExtent3D {
                width: config.width, height: config.height, depthOrArrayLayers: 1,
            },
            sample_count: 1,
        },
    });
    surface.has_surface_presented.store(false, atomic::Ordering::SeqCst);
    PRIME_WGPU_SUCCESS
}

#[no_mangle]
pub unsafe extern "C" fn primeWgpuSurfaceAcquire(
    surface: native::WGPUSurface, output: *mut native::WGPUSurfaceTexture,
    message: *mut u8, capacity: usize,
) -> u32 {
    prime_message(message, capacity, "");
    if let Some(output) = output.as_mut() {
        output.texture = std::ptr::null();
        output.suboptimal = false as native::WGPUBool;
        output.status = native::WGPUSurfaceGetCurrentTextureStatus_DeviceLost;
    }
    if let Some(outcome) = prime_take_fault(2, message, capacity) { return outcome; }
    let (Some(surface), Some(output)) = (surface.as_ref(), output.as_mut()) else {
        prime_message(message, capacity, "acquire requires surface and output");
        return PRIME_WGPU_VALIDATION;
    };
    let context = &surface.context;
    let guard = surface.data.lock();
    let Some(data) = guard.as_ref() else {
        output.status = native::WGPUSurfaceGetCurrentTextureStatus_Outdated;
        prime_message(message, capacity, "surface is not configured");
        return PRIME_WGPU_OUTDATED;
    };
    match gfx_select!(data.device_id => context.surface_get_current_texture(surface.id, ())) {
        Ok(wgc::present::SurfaceOutput { status, texture_id }) => {
            let suboptimal = matches!(status, wgt::SurfaceStatus::Suboptimal);
            let outcome = prime_surface_status(status);
            output.status = match outcome {
                PRIME_WGPU_SUCCESS => native::WGPUSurfaceGetCurrentTextureStatus_Success,
                PRIME_WGPU_TIMEOUT => native::WGPUSurfaceGetCurrentTextureStatus_Timeout,
                PRIME_WGPU_OUTDATED => native::WGPUSurfaceGetCurrentTextureStatus_Outdated,
                _ => native::WGPUSurfaceGetCurrentTextureStatus_Lost,
            };
            // Suboptimal is advisory; it is not a failed acquisition.
            output.suboptimal = suboptimal as native::WGPUBool;
            if let Some(id) = texture_id {
                surface.has_surface_presented.store(false, atomic::Ordering::SeqCst);
                output.texture = Arc::into_raw(Arc::new(WGPUTextureImpl {
                    context: context.clone(), id, error_sink: data.error_sink.clone(),
                    data: data.texture_data, surface_id: Some(surface.id),
                    has_surface_presented: surface.has_surface_presented.clone(),
                }));
            }
            outcome
        }
        Err(error) => {
            let outcome = prime_surface_error(&error);
            prime_message(message, capacity, &format_error(context, &error));
            outcome
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn primeWgpuSurfacePresent(
    surface: native::WGPUSurface, message: *mut u8, capacity: usize,
) -> u32 {
    prime_message(message, capacity, "");
    if let Some(outcome) = prime_take_fault(3, message, capacity) { return outcome; }
    let Some(surface) = surface.as_ref() else {
        prime_message(message, capacity, "present requires a surface");
        return PRIME_WGPU_VALIDATION;
    };
    let context = &surface.context;
    let guard = surface.data.lock();
    let Some(data) = guard.as_ref() else {
        prime_message(message, capacity, "surface is not configured");
        return PRIME_WGPU_OUTDATED;
    };
    let result = gfx_select!(data.device_id => context.surface_present(surface.id));
    // Core consumes the submitted surface output even on WSI Lost/Outdated.
    // On a device error, legacy texture Drop must not retry a fatal discard.
    surface.has_surface_presented.store(true, atomic::Ordering::SeqCst);
    match result {
        Ok(status) => prime_surface_status(status),
        Err(error) => {
            let outcome = prime_surface_error(&error);
            prime_message(message, capacity, &format_error(context, &error));
            outcome
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn primeWgpuSurfaceDiscard(
    texture: native::WGPUTexture, message: *mut u8, capacity: usize,
) -> u32 {
    prime_message(message, capacity, "");
    if let Some(outcome) = prime_take_fault(4, message, capacity) { return outcome; }
    let Some(texture) = texture.as_ref() else {
        prime_message(message, capacity, "discard requires a surface texture");
        return PRIME_WGPU_VALIDATION;
    };
    let Some(surface_id) = texture.surface_id else {
        prime_message(message, capacity, "discard requires a surface texture");
        return PRIME_WGPU_VALIDATION;
    };
    if texture.has_surface_presented.swap(true, atomic::Ordering::SeqCst) {
        return PRIME_WGPU_SUCCESS;
    }
    let context = &texture.context;
    match gfx_select!(texture.id => context.surface_texture_discard(surface_id)) {
        Ok(()) => PRIME_WGPU_SUCCESS,
        Err(error) => {
            let outcome = prime_surface_error(&error);
            prime_message(message, capacity, &format_error(context, &error));
            outcome
        }
    }
}

// wgpu 0.19 timestamps are raw ticks. This missing C-ABI query supplies the
// multiplier required for meaningful cross-device nanosecond timing.
#[no_mangle]
pub unsafe extern "C" fn primeWgpuQueueGetTimestampPeriod(queue: native::WGPUQueue) -> f32 {
    let Some(queue) = queue.as_ref() else { return 0.0; };
    let context = &queue.queue.context;
    gfx_select!(queue.queue.id => context.queue_get_timestamp_period(queue.queue.id)).unwrap_or(0.0)
}

#[no_mangle]
pub unsafe extern "C" fn primeWgpuQueueSubmit(
    queue: native::WGPUQueue, count: usize, commands: *const native::WGPUCommandBuffer,
    message: *mut u8, capacity: usize,
) -> u32 {
    prime_message(message, capacity, "");
    if let Some(outcome) = prime_take_fault(5, message, capacity) { return outcome; }
    let Some(queue) = queue.as_ref() else {
        prime_message(message, capacity, "submit requires a queue");
        return PRIME_WGPU_VALIDATION;
    };
    if count != 0 && commands.is_null() {
        prime_message(message, capacity, "submit requires command buffers");
        return PRIME_WGPU_VALIDATION;
    }
    let mut ids = SmallVec::<[_; 4]>::new();
    for command in make_slice(commands, count) {
        let Some(command) = command.as_ref() else {
            prime_message(message, capacity, "submit received a null command buffer");
            return PRIME_WGPU_VALIDATION;
        };
        if !Arc::ptr_eq(&command.context, &queue.queue.context) {
            prime_message(message, capacity, "command buffer and queue belong to different instances");
            return PRIME_WGPU_VALIDATION;
        }
        ids.push(command.id);
    }
    // Transfer command ownership only after validating the entire array.
    for command in make_slice(commands, count) {
        command.as_ref().unwrap().open.store(false, atomic::Ordering::SeqCst);
    }
    let context = &queue.queue.context;
    match gfx_select!(queue.queue.id => context.queue_submit(queue.queue.id, &ids)) {
        Ok(_) => PRIME_WGPU_SUCCESS,
        Err(error) => {
            // QueueSubmitError's source chain includes DeviceError on loss/OOM.
            let mut source: Option<&(dyn error::Error + 'static)> = Some(&error);
            let mut outcome = PRIME_WGPU_VALIDATION;
            while let Some(cause) = source {
                if let Some(device) = cause.downcast_ref::<wgc::device::DeviceError>() {
                    outcome = prime_device_error(device);
                    break;
                }
                source = cause.source();
            }
            prime_message(message, capacity, &format_error(context, &error));
            outcome
        }
    }
}

// Additive, explicitly check-owned observation ABI. Metadata is copied before
// original submission; no native pointer is retained in its TLS scope. Invalid
// metadata or a disabled observer still executes the original Submit once.
#[no_mangle]
pub unsafe extern "C" fn primeWgpuQueueSubmitObserved(
    queue: native::WGPUQueue, count: usize, commands: *const native::WGPUCommandBuffer,
    message: *mut u8, capacity: usize,
    identity: *const wgc::prime_submit_observation::Identity,
) -> u32 {
    let identity = wgc::prime_submit_observation::copy_identity(identity);
    let observation = wgc::prime_submit_observation::begin_submission(identity);
    observation.mark("checked-abi", "START");
    let result = primeWgpuQueueSubmit(queue, count, commands, message, capacity);
    observation.mark("checked-abi", "DONE");
    result
}

#[cfg(test)]
mod prime_surface_tests {
    use super::*;
    #[test]
    fn recoverable_status_and_device_errors_remain_distinct() {
        assert_eq!(prime_surface_status(wgt::SurfaceStatus::Timeout), PRIME_WGPU_TIMEOUT);
        assert_eq!(prime_surface_status(wgt::SurfaceStatus::Lost), PRIME_WGPU_SURFACE_LOST);
        assert_eq!(prime_surface_status(wgt::SurfaceStatus::Outdated), PRIME_WGPU_OUTDATED);
        assert_eq!(prime_surface_error(&wgc::present::SurfaceError::Device(
            wgc::device::DeviceError::Lost)), PRIME_WGPU_DEVICE_LOST);
        assert_eq!(prime_configure_error(&wgc::present::ConfigureSurfaceError::Device(
            wgc::device::DeviceError::OutOfMemory)), PRIME_WGPU_OUT_OF_MEMORY);
        assert_eq!(prime_surface_error(&wgc::present::SurfaceError::AlreadyAcquired),
            PRIME_WGPU_VALIDATION);
    }
    #[test]
    fn diagnostic_buffer_is_bounded_and_terminated() {
        let mut output = [0xAAu8; 5];
        unsafe { prime_message(output.as_mut_ptr(), 4, "long diagnostic"); }
        assert_eq!(output, [b'l', b'o', b'n', 0, 0xAA]);
        unsafe { prime_message(std::ptr::null_mut(), 0, "ignored"); }
    }
}
