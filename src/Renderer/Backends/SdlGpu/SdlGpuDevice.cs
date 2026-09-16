using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
    internal static unsafe class SdlGpuSamplerBindingAbi
    {
        // SDL 3.4.16's D3D12 backend checks only whether its 2,048-entry
        // sampler heap is already full before copying a complete binding
        // batch. Use one divisor-sized batch for every sampler-bearing shader
        // so no draw can begin a copy that crosses the native heap boundary.
        public const int D3D12BatchSize = 8;

        public static int BindingCountForDriver(string driver,
            int shaderSamplerCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(shaderSamplerCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(shaderSamplerCount,
                D3D12BatchSize);
            if (shaderSamplerCount == 0) return 0;
            return driver.Equals("direct3d12", StringComparison.OrdinalIgnoreCase)
                ? D3D12BatchSize : shaderSamplerCount;
        }

        public static void Pad(SDL_GPUTextureSamplerBinding* bindings,
            int populatedCount, int bindingCount, SDL_GPUTexture* fallbackTexture,
            SDL_GPUSampler* fallbackSampler)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(populatedCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(populatedCount,
                bindingCount);
            for (int i = populatedCount; i < bindingCount; i++)
            {
                bindings[i] = new SDL_GPUTextureSamplerBinding
                {
                    texture = fallbackTexture,
                    sampler = fallbackSampler
                };
            }
        }
    }

    /// <summary>
    /// SDL GPU device lifetime and capability discovery. The window is claimed
    /// here on the calling thread; no worker thread may create or claim an SDL
    /// window/device.
    /// </summary>
    internal unsafe sealed class SdlGpuDevice : IDisposable
    {
        private bool _disposed;

        private SdlGpuDevice(SDL_GPUDevice* handle, SDL_Window* window, Vector2i logicalSize,
            Vector2i framebufferSize, SDL_GPUTextureFormat swapchainFormat, string driver,
            SDL_GPUShaderFormat shaderFormats, bool supportsImmediatePresent,
            DesktopGpuBackend requestedBackend, bool gpuDebug, string? deviceName,
            string? deviceDriverInfo, string runtimeVersion)
        {
            Handle = handle;
            Window = window;
            SwapchainFormat = swapchainFormat;
            Driver = driver;
            RequestedBackend = requestedBackend;
            GpuDebug = gpuDebug;
            DeviceName = deviceName;
            DeviceDriverInfo = deviceDriverInfo;
            RuntimeVersion = runtimeVersion;
            ShaderFormats = shaderFormats;
            SupportsImmediatePresent = supportsImmediatePresent;
            Caches = new DeviceRenderCaches();
            FrameResources = new SdlGpuFrameResources(handle);
            Surface = new RenderSurfaceInfo(logicalSize, framebufferSize, false, true,
                swapchainFormat.ToString(), "vsync");
        }

        public SDL_GPUDevice* Handle { get; }
        public SDL_Window* Window { get; }
        public SDL_GPUTextureFormat SwapchainFormat { get; }
        /// <summary>
        /// True only when SDL reports a swapchain format whose render-target
        /// writes perform the linear-to-sRGB transfer. Enhanced output uses
        /// this explicit policy instead of assuming platform gamma behavior.
        /// </summary>
        public bool SwapchainIsSrgb => IsSrgbFormat(SwapchainFormat);
        public string Driver { get; }
        public DesktopGpuBackend RequestedBackend { get; }
        public string RequestedDriver => DesktopGpuBackendResolver.ToCliValue(RequestedBackend);
        public bool GpuDebug { get; }
        public string? DeviceName { get; }
        public string? DeviceDriverInfo { get; }
        public string RuntimeVersion { get; }
        public SDL_GPUShaderFormat ShaderFormats { get; }
        public bool SupportsImmediatePresent { get; }
        public DeviceRenderCaches Caches { get; }
        public SdlGpuFrameResources FrameResources { get; }
        public RenderSurfaceInfo Surface { get; set; }

        public static SdlGpuDevice Create(SDL_Window* window, Vector2i logicalSize,
            Vector2i framebufferSize)
            => Create(window, logicalSize, framebufferSize,
                SdlGpuDeviceOptions.ForCurrentPlatform());

        public static SdlGpuDevice Create(SDL_Window* window, Vector2i logicalSize,
            Vector2i framebufferSize, SdlGpuDeviceOptions options,
            DesktopGpuBackend requestedBackend = DesktopGpuBackend.Auto)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));

            SDL_GPUShaderFormat requested = OperatingSystem.IsMacOS()
                ? SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL
                    | SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_METALLIB
                : SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV
                    | SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL
                    | SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXBC
                    | SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL
                    | SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_METALLIB;
            SDL.Utf8String preferredDriver = options.PreferredDriver;
            string requestedDriver = DesktopGpuBackendResolver.ToCliValue(requestedBackend);
            SDL_GPUDevice* device = SDL3.SDL_CreateGPUDevice(requested,
                options.DebugMode, preferredDriver);
            if (device == null)
            {
                throw new InvalidOperationException(
                    $"SDL GPU device creation failed for requested-driver={requestedDriver} "
                    + $"gpu-debug={options.DebugMode.ToString().ToLowerInvariant()}: "
                    + SDL3.SDL_GetError());
            }

            try
            {
                if (!SDL3.SDL_ClaimWindowForGPUDevice(device, window))
                {
                    throw new InvalidOperationException($"SDL GPU window claim failed: {SDL3.SDL_GetError()}");
                }
                SDL3.SDL_SetGPUAllowedFramesInFlight(device, 3);
                SDL_GPUTextureFormat swapchainFormat = SDL3.SDL_GetGPUSwapchainTextureFormat(device, window);
                if (swapchainFormat == SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID)
                {
                    throw new InvalidOperationException($"SDL GPU reported no swapchain format: {SDL3.SDL_GetError()}");
                }
                bool supportsImmediatePresent = SDL3.SDL_WindowSupportsGPUPresentMode(device,
                    window, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE);
                if (!SDL3.SDL_SetGPUSwapchainParameters(device, window,
                    SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR,
                    SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC))
                {
                    throw new InvalidOperationException($"SDL GPU swapchain setup failed: {SDL3.SDL_GetError()}");
                }

                string driver = SDL3.SDL_GetGPUDeviceDriver(device) ?? "unknown";
                if (requestedBackend != DesktopGpuBackend.Auto
                    && !String.Equals(driver, options.PreferredDriver,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SDL GPU selected actual-driver={driver} for explicit "
                        + $"requested-driver={requestedDriver}; refusing silent fallback.");
                }
                SDL_GPUShaderFormat formats = SDL3.SDL_GetGPUShaderFormats(device);
                SDL_PropertiesID properties = SDL3.SDL_GetGPUDeviceProperties(device);
                string? deviceName = SDL3.SDL_GetStringProperty(properties,
                    SDL3.SDL_PROP_GPU_DEVICE_NAME_STRING, null);
                string? driverInfo = SDL3.SDL_GetStringProperty(properties,
                    SDL3.SDL_PROP_GPU_DEVICE_DRIVER_INFO_STRING, null)
                    ?? SDL3.SDL_GetStringProperty(properties,
                        SDL3.SDL_PROP_GPU_DEVICE_DRIVER_VERSION_STRING, null)
                    ?? SDL3.SDL_GetStringProperty(properties,
                        SDL3.SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING, null);
                Console.WriteLine($"[render] renderer=sdl-gpu requested-driver={requestedDriver} "
                    + $"actual-driver={driver} gpu-debug={options.DebugMode.ToString().ToLowerInvariant()} "
                    + $"shaders={DescribeShaderFormats(formats)} swapchain={swapchainFormat} present=vsync");
                return new SdlGpuDevice(device, window, logicalSize, framebufferSize,
                    swapchainFormat, driver, formats, supportsImmediatePresent,
                    requestedBackend, options.DebugMode, deviceName, driverInfo,
                    SDL3.SDL_GetVersion().ToString());
            }
            catch
            {
                SDL3.SDL_ReleaseWindowFromGPUDevice(device, window);
                SDL3.SDL_DestroyGPUDevice(device);
                throw;
            }
        }

        public void SetPresentMode(SDL_GPUPresentMode presentMode)
        {
            if (!SDL3.SDL_SetGPUSwapchainParameters(Handle, Window,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR, presentMode))
            {
                throw new InvalidOperationException(
                    $"SDL GPU swapchain present-mode setup failed for {presentMode}: {SDL3.SDL_GetError()}");
            }
        }

        public static string DescribeShaderFormats(SDL_GPUShaderFormat formats)
        {
            var values = new System.Collections.Generic.List<string>();
            Add(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, "SPIR-V");
            Add(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, "DXIL");
            Add(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXBC, "DXBC");
            Add(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, "MSL");
            Add(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_METALLIB, "METALLIB");
            Add(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_PRIVATE, "PRIVATE");
            return values.Count == 0 ? "none" : string.Join('|', values);

            void Add(SDL_GPUShaderFormat flag, string name)
            {
                if ((formats & flag) != 0) values.Add(name);
            }
        }

        internal static bool IsSrgbFormat(SDL_GPUTextureFormat format)
            => format is SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM_SRGB
                or SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM_SRGB;

        internal static string? PreferredDriverNameForPlatform(bool isMacOS)
            => DesktopGpuBackendResolver.ResolvePreferredDriver(
                DesktopGpuBackend.Auto, isMacOS ? OSPlatform.OSX : OSPlatform.Windows);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FrameResources.Dispose();
            Caches.Dispose();
            SDL3.SDL_ReleaseWindowFromGPUDevice(Handle, Window);
            SDL3.SDL_DestroyGPUDevice(Handle);
        }
    }
}
