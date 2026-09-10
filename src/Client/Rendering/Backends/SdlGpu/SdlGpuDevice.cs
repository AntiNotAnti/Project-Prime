using System;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
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
            SDL_GPUShaderFormat shaderFormats, bool supportsImmediatePresent)
        {
            Handle = handle;
            Window = window;
            SwapchainFormat = swapchainFormat;
            Driver = driver;
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
        public SDL_GPUShaderFormat ShaderFormats { get; }
        public bool SupportsImmediatePresent { get; }
        public DeviceRenderCaches Caches { get; }
        public SdlGpuFrameResources FrameResources { get; }
        public RenderSurfaceInfo Surface { get; set; }

        public static SdlGpuDevice Create(SDL_Window* window, Vector2i logicalSize, Vector2i framebufferSize)
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
            SDL.Utf8String preferredDriver = OperatingSystem.IsMacOS() ? "metal" : string.Empty;
            SDL_GPUDevice* device = SDL3.SDL_CreateGPUDevice(requested, true, preferredDriver);
            if (device == null)
            {
                throw new InvalidOperationException($"SDL GPU device creation failed: {SDL3.SDL_GetError()}");
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
                SDL_GPUShaderFormat formats = SDL3.SDL_GetGPUShaderFormats(device);
                Console.WriteLine($"[render] renderer=sdl-gpu driver={driver} shaders={DescribeShaderFormats(formats)} swapchain={swapchainFormat} present=vsync");
                return new SdlGpuDevice(device, window, logicalSize, framebufferSize,
                    swapchainFormat, driver, formats, supportsImmediatePresent);
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
