using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using SDL;
using MphRead.Mods.Render;

namespace MphRead
{
    internal readonly record struct SdlGpuPresentPolicy(SDL_GPUPresentMode NativeMode,
        string Label, bool UsesSoftwarePacing, bool ImmediateFallback)
    {
        public static SdlGpuPresentPolicy Resolve(int frameRateCap, bool immediateSupported)
        {
            if (frameRateCap == FrameTiming.DisplayRate)
            {
                return new SdlGpuPresentPolicy(SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC,
                    "vsync", UsesSoftwarePacing: false, ImmediateFallback: false);
            }
            if (immediateSupported)
            {
                return new SdlGpuPresentPolicy(SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE,
                    "immediate", UsesSoftwarePacing: true, ImmediateFallback: false);
            }
            return new SdlGpuPresentPolicy(SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC,
                "vsync-fallback", UsesSoftwarePacing: false, ImmediateFallback: true);
        }
    }

    /// <summary>
    /// SDL GPU backend slice. It owns an off-screen final-composite target and
    /// only treats the swapchain image as a presentation target. A sealed
    /// client-owned frame is translated into the six DS scene passes without
    /// consulting mutable gameplay or legacy OpenGL state.
    /// </summary>
    public unsafe sealed class SdlGpuBackend : IRenderBackend, IRenderCaptureSource,
        ISdlOffscreenToolBackend, IRenderBackendTelemetry
    {
        private readonly SDL_Window* _window;
        private readonly SdlGpuDevice _device;
        private readonly SdlGpuReadback _readback;
        private readonly List<CaptureScheduleFailure> _captureFailures = new();
        private SDL_GPUTexture* _finalComposite;
        private int _finalCompositeWidth;
        private int _finalCompositeHeight;
        private RenderBackendFrame? _currentFrame;
        private bool _disposed;
        private Vector2i _logicalSize;
        private Vector2i _framebufferSize;
        private bool _minimized;
        private SdlGpuSceneResources? _sceneResources;
        private SDL_GPUPresentMode _presentMode = SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC;
        private string _presentModeLabel = "vsync";
        private bool _loggedImmediateFallback;
        private bool _loggedFirstSubmit;
        private readonly RenderTelemetryAccumulator _telemetry = new();

        public SdlGpuBackend(SDL_Window* window, Vector2i logicalSize, Vector2i framebufferSize)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            _window = window;
            _logicalSize = logicalSize;
            _framebufferSize = framebufferSize;
            _device = SdlGpuDevice.Create(window, logicalSize, framebufferSize);
            _readback = new SdlGpuReadback(_device);
            try
            {
                RecreateFinalComposite();
                _sceneResources = _device.Caches.GetOrAddDeviceResource(
                    "scene/resources", () => SdlGpuSceneResources.Create(_device));
            }
            catch
            {
                _readback.Dispose();
                _device.Dispose();
                throw;
            }
        }

        public RenderBackendInfo Info => new(
            "sdl-gpu",
            _device.Driver,
            SdlGpuDevice.DescribeShaderFormats(_device.ShaderFormats),
            _device.SwapchainFormat.ToString(),
            _presentModeLabel,
            _finalComposite != null,
            true,
            true);
        public RenderSurfaceInfo Surface => _device.Surface;
        public DeviceRenderCaches Caches => _device.Caches;
        public RenderTelemetrySnapshot Telemetry => _telemetry.Latest;

        internal SdlGpuPresentPolicy ApplyPresentPolicy(int frameRateCap)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Present parameters belong to the swapchain boundary. A cap
            // changed during a frame is applied on the next host iteration,
            // after that frame has submitted.
            if (_currentFrame != null || _commandBuffer != null)
            {
                return SdlGpuPresentPolicy.Resolve(frameRateCap,
                    _device.SupportsImmediatePresent);
            }

            SdlGpuPresentPolicy policy = SdlGpuPresentPolicy.Resolve(frameRateCap,
                _device.SupportsImmediatePresent);
            if (policy.NativeMode != _presentMode)
            {
                _device.SetPresentMode(policy.NativeMode);
                _presentMode = policy.NativeMode;
            }
            _presentModeLabel = policy.Label;
            _device.Surface = _device.Surface with { PresentMode = _presentModeLabel };
            if (policy.ImmediateFallback && !_loggedImmediateFallback)
            {
                _loggedImmediateFallback = true;
                Console.WriteLine("[render] SDL GPU immediate present mode is unavailable; "
                    + "using vsync-fallback without software pacing to avoid double-throttling; "
                    + "the requested explicit cap is display-rate limited.");
            }
            return policy;
        }

        public bool TryBeginFrame(out RenderBackendFrame frame)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RefreshSurface();
            frame = null!;
            if (_minimized || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0 || _finalComposite == null)
            {
                return false;
            }

            // A readback ticket retains its own fence lease. Drain before
            // frame-resource rotation so a completed ticket can be mapped
            // before its dynamic slot is considered reusable.
            _readback.DrainCompleted();
            _device.FrameResources.BeginFrame();
            _telemetry.BeginFrame();
            _telemetry.MarkAttempted();
            SDL_GPUCommandBuffer* commandBuffer = SDL3.SDL_AcquireGPUCommandBuffer(_device.Handle);
            if (commandBuffer == null)
            {
                CompleteTelemetry();
                return false;
            }

            // Own the command buffer before swapchain acquisition and resize work.
            // If checked dimensions or resource recreation throws, EndScene must
            // still be able to retire it before this persistent device is reused.
            _commandBuffer = commandBuffer;
            _swapchainAcquireSucceeded = false;
            SDL_GPUTexture* swapchainTexture = null;
            uint width = 0;
            uint height = 0;
            // Explicit caps use immediate presentation and must not let the
            // display's refresh rate pace the entire render loop. If all
            // swapchain images are busy, SDL's non-blocking acquire returns a
            // successful null image; the frame is still rendered into the
            // final-composite target and submitted as a real offscreen GPU
            // frame. Display/VSync mode keeps the blocking acquire so it stays
            // naturally synchronized to the monitor without a busy loop.
            bool nonBlocking = _presentMode
                == SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE;
            bool acquired = nonBlocking
                ? SDL3.SDL_AcquireGPUSwapchainTexture(commandBuffer,
                    _window, &swapchainTexture, &width, &height)
                : SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(commandBuffer,
                    _window, &swapchainTexture, &width, &height);
            if (!acquired)
            {
                SDL3.SDL_CancelGPUCommandBuffer(commandBuffer);
                _commandBuffer = null;
                CompleteTelemetry();
                return false;
            }
            _swapchainAcquireSucceeded = swapchainTexture != null;
            // A null image is the expected non-blocking result while the GPU
            // or display owns every swapchain image. Keep the command buffer
            // and produce an offscreen frame instead of stalling. The blocking
            // VSync path retains its existing no-drawable behavior.
            if (swapchainTexture == null && nonBlocking)
            {
                _swapchainWidth = checked((uint)_framebufferSize.X);
                _swapchainHeight = checked((uint)_framebufferSize.Y);
                frame = new RenderBackendFrame(false, false, _framebufferSize);
                _currentFrame = frame;
                return true;
            }
            // SDL has successfully acquired the swapchain operation at this
            // point. Even if it reports no drawable image (minimized/window
            // manager race), cancellation is invalid; submit this empty
            // command buffer and suppress presentation acknowledgement.
            if (swapchainTexture == null || width == 0 || height == 0)
            {
                SubmitEmpty(commandBuffer);
                return false;
            }
            // Resource dimensions remain unchanged when allocation fails. Compare
            // those as well so the next scene retries after a failed resize.
            try
            {
                if (width != (uint)_framebufferSize.X || height != (uint)_framebufferSize.Y
                    || width != (uint)_finalCompositeWidth || height != (uint)_finalCompositeHeight)
                {
                    _framebufferSize = new Vector2i(checked((int)width), checked((int)height));
                    RecreateFinalComposite();
                    if (_finalComposite == null)
                    {
                        SubmitEmpty(commandBuffer);
                        return false;
                    }
                }
            }
            catch
            {
                // Swapchain acquisition succeeded, so SDL forbids cancelling
                // this command buffer. Retire it here because TryBeginFrame
                // cannot return a token that a caller could use for cleanup.
                SubmitEmpty(commandBuffer);
                throw;
            }

            // The actual command buffer/texture remain backend-private. The
            // token only crosses the neutral interface and gates callbacks.
            frame = new RenderBackendFrame(true, false, _framebufferSize);
            _currentFrame = frame;
            _commandBuffer = commandBuffer;
            _swapchainTexture = swapchainTexture;
            _swapchainWidth = width;
            _swapchainHeight = height;
            return true;
        }

        private void SubmitEmpty(SDL_GPUCommandBuffer* commandBuffer)
        {
            SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(commandBuffer);
            if (_commandBuffer == commandBuffer) _commandBuffer = null;
            _swapchainAcquireSucceeded = false;
            if (fence != null)
            {
                if (_telemetry.IsFrameActive)
                {
                    _telemetry.MarkSubmitted();
                    _telemetry.CommitScheduledUploads();
                    _telemetry.MarkFenceCommitted();
                }
                SdlGpuFenceLease lease = _device.FrameResources.CommitFence(fence);
                lease.Release();
            }
            CompleteTelemetry();
        }

        private SDL_GPUCommandBuffer* _commandBuffer;
        private SDL_GPUTexture* _swapchainTexture;
        private uint _swapchainWidth;
        private uint _swapchainHeight;
        private bool _swapchainAcquireSucceeded;

        /// <summary>
        /// Acquires a command buffer for a deterministic offscreen picture.
        /// This path deliberately does not acquire a swapchain image and
        /// therefore cannot produce a presentation acknowledgement. The same
        /// owned final-composite target and capture fence path are used as the
        /// visible frame path.
        /// </summary>
        public bool TryBeginOffscreenFrame(Vector2i framebufferSize, out RenderBackendFrame frame)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            frame = null!;
            if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
            {
                return false;
            }

            _readback.DrainCompleted();
            _framebufferSize = framebufferSize;
            if (_finalComposite == null
                || _finalCompositeWidth != framebufferSize.X
                || _finalCompositeHeight != framebufferSize.Y)
            {
                RecreateFinalComposite();
            }
            if (_finalComposite == null) return false;

            _device.FrameResources.BeginFrame();
            _telemetry.BeginFrame();
            _telemetry.MarkAttempted();
            SDL_GPUCommandBuffer* commandBuffer = SDL3.SDL_AcquireGPUCommandBuffer(_device.Handle);
            if (commandBuffer == null)
            {
                CompleteTelemetry();
                return false;
            }

            _swapchainAcquireSucceeded = false;
            _commandBuffer = commandBuffer;
            _swapchainTexture = null;
            _swapchainWidth = checked((uint)framebufferSize.X);
            _swapchainHeight = checked((uint)framebufferSize.Y);
            frame = new RenderBackendFrame(false, false, framebufferSize);
            _currentFrame = frame;
            return true;
        }

        /// <summary>Uses the current device-pixel size for tool hosts.</summary>
        public bool TryBeginOffscreenFrame(out RenderBackendFrame frame)
            => TryBeginOffscreenFrame(_framebufferSize, out frame);

        public void Render(RenderBackendFrame frame, RenderFrame snapshot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(frame, _currentFrame)) throw new InvalidOperationException("Unknown SDL GPU frame.");
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.IsSealed) throw new InvalidOperationException("SDL GPU requires a sealed render snapshot.");
            if (_commandBuffer == null || _finalComposite == null)
            {
                CompleteTelemetry();
                return;
            }
            // The final target is the real draw target. The swapchain image is
            // presentation-only and receives a final blit below.
            SdlGpuTelemetryContext.Set(_telemetry);
            try
            {
                EnsureSceneResources();
                _sceneResources!.Encode(_commandBuffer, _finalComposite, snapshot,
                    (uint)_framebufferSize.X, (uint)_framebufferSize.Y);
                _captureFailures.Clear();
                if (snapshot.CaptureRequests.Count > 0)
                {
                    if (_readback.TryBeginSubmission(_commandBuffer, out string copyError))
                    {
                        foreach (RenderCaptureRequest request in snapshot.CaptureRequests)
                        {
                            if (!TryGetCaptureSource(request, out SDL_GPUTexture* source,
                                out uint sourceWidth, out uint sourceHeight, out string sourceError))
                            {
                                _captureFailures.Add(new CaptureScheduleFailure(request, sourceError));
                                continue;
                            }
                            if (!_readback.TrySchedule(source, sourceWidth, sourceHeight,
                                SdlGpuCaptureColorPolicy.ReadbackFormat(_device.SwapchainFormat),
                                request, out string readbackError))
                            {
                                _captureFailures.Add(new CaptureScheduleFailure(request, readbackError));
                            }
                        }
                        _readback.EndSubmission();
                    }
                    else
                    {
                        foreach (RenderCaptureRequest request in snapshot.CaptureRequests)
                        {
                            _captureFailures.Add(new CaptureScheduleFailure(request, copyError));
                        }
                    }
                }

                if (_swapchainTexture != null)
                {
                    SDL_GPUBlitInfo blit = new SDL_GPUBlitInfo
                    {
                        source = new SDL_GPUBlitRegion
                        {
                            texture = _finalComposite,
                            mip_level = 0,
                            layer_or_depth_plane = 0,
                            x = 0,
                            y = 0,
                            w = (uint)_framebufferSize.X,
                            h = (uint)_framebufferSize.Y
                        },
                        destination = new SDL_GPUBlitRegion
                        {
                            texture = _swapchainTexture,
                            mip_level = 0,
                            layer_or_depth_plane = 0,
                            x = 0,
                            y = 0,
                            w = _swapchainWidth,
                            h = _swapchainHeight
                        },
                        load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                        filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
                        cycle = false
                    };
                    SDL3.SDL_BlitGPUTexture(_commandBuffer, &blit);
                }
                frame.Encoded = true;
                _telemetry.MarkEncoded();
                int knownResources = _device.Caches.MeshCount
                    + _device.Caches.TextureCount + _device.Caches.PipelineCount
                    + _device.Caches.DeviceResourceCount;
                _telemetry.DeviceCacheEntryCount(knownResources);
                _telemetry.SessionCacheEntryPeak(knownResources);
            }
            catch
            {
                // A failed encoder must not leave a graphics-thread sample
                // active forever. EndScene still owns command-buffer cleanup.
                CompleteTelemetry();
                throw;
            }
            finally
            {
                SdlGpuTelemetryContext.Set(null);
            }
        }

        private void EnsureSceneResources()
        {
            _sceneResources ??= _device.Caches.GetOrAddDeviceResource(
                "scene/resources", () => SdlGpuSceneResources.Create(_device));
        }

        public bool TrySubmitFrame(RenderBackendFrame frame)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(frame, _currentFrame) || !frame.Encoded || _commandBuffer == null)
            {
                // A frame that never reaches SDL submission did not consume
                // its capture requests. Release only uncommitted tickets so
                // Renderer can attach the same request again.
                _readback.DiscardUnsubmitted();
                _captureFailures.Clear();
                CompleteTelemetry();
                return false;
            }
            SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(_commandBuffer);
            _commandBuffer = null;
            _swapchainTexture = null;
            _currentFrame = null;
            _swapchainAcquireSucceeded = false;
            if (fence == null)
            {
                // Upload commands and draws were recorded into this command
                // buffer, but without a submission fence none may be assumed
                // resident. Preserve CPU resources and force re-upload.
                _sceneResources?.InvalidatePendingUploads();
                // No successful submission means the renderer still owns its
                // immutable requests and will attach them to the next frame.
                if (_readback.PendingCount > 0)
                {
                    Console.Error.WriteLine("[capture] SDL GPU submit returned no fence; pending readbacks will retry on the next successful frame.");
                }
                _readback.DiscardUnsubmitted();
                _captureFailures.Clear();
                CompleteTelemetry();
                return false;
            }
            _telemetry.MarkSubmitted();
            SdlGpuFenceLease lease = _device.FrameResources.CommitFence(fence);
            try
            {
                _readback.CommitSubmission(lease);
                _telemetry.CommitScheduledUploads();
                _telemetry.MarkFenceCommitted();
            }
            finally
            {
                // FrameResources owns its retained reference; readback slots
                // retain one each while their transfer is in flight.
                lease.Release();
            }
            foreach (CaptureScheduleFailure failure in _captureFailures)
            {
                _readback.ReportFailure(new RenderCaptureFailure(failure.Request, failure.Error));
            }
            _captureFailures.Clear();
            frame.Submitted = true;
            CompleteTelemetry();
            if (!_loggedFirstSubmit)
            {
                _loggedFirstSubmit = true;
                RendererLog.Line("gpu", $"first frame submitted; backend={Info.Name} driver={Info.Driver} "
                    + $"swapchain={Surface.SwapchainFormat} present={Surface.PresentMode} "
                    + $"logical={Surface.LogicalSize.X}x{Surface.LogicalSize.Y} "
                    + $"framebuffer={Surface.FramebufferSize.X}x{Surface.FramebufferSize.Y}");
            }
            return true;
        }

        /// <summary>
        /// Submits an offscreen command buffer with the normal GPU fence. It
        /// intentionally has no callback or presentation side effect; the
        /// caller may flush the capture channel when a deterministic utility
        /// needs the result before returning.
        /// </summary>
        public bool TrySubmitOffscreenFrame(RenderBackendFrame frame)
            => TrySubmitFrame(frame);

        public void RenderOffscreen(RenderBackendFrame frame, RenderFrame snapshot)
            => Render(frame, snapshot);

        private bool TryGetCaptureSource(RenderCaptureRequest request, out SDL_GPUTexture* texture,
            out uint width, out uint height, out string error)
        {
            texture = null;
            width = 0;
            height = 0;
            switch (request.Target)
            {
                case CaptureTargetKind.FinalPresentedFrame:
                    texture = _finalComposite;
                    width = checked((uint)_framebufferSize.X);
                    height = checked((uint)_framebufferSize.Y);
                    break;
                case CaptureTargetKind.SceneTarget:
                case CaptureTargetKind.ThumbnailTarget:
                    // Scene and thumbnail requests intentionally omit 2D
                    // overlays. Enhanced uses the capture-safe SDR branch
                    // after scene tone mapping and HUD-scene composition.
                    // Thumbnail remains an alias until a tool-specific
                    // thumbnail target is introduced; the source point is
                    // still deterministic.
                    texture = _sceneResources?.CaptureSceneColor;
                    width = _sceneResources?.SceneTargetWidth ?? 0;
                    height = _sceneResources?.SceneTargetHeight ?? 0;
                    break;
                default:
                    error = $"SDL GPU capture target {request.Target} is unknown.";
                    return false;
            }
            if (texture == null || width == 0 || height == 0)
            {
                error = $"SDL GPU capture target {request.Target} has no drawable texture.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public void FlushCaptures()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _readback.Flush();
        }

        public bool TryDequeueCapture(out RenderCaptureResult? result)
            => _readback.TryDequeueCapture(out result);

        public bool TryDequeueCaptureFailure(out RenderCaptureFailure? failure)
            => _readback.TryDequeueFailure(out failure);

        public void Resize(Vector2i logicalSize, Vector2i framebufferSize)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _logicalSize = logicalSize;
            _framebufferSize = framebufferSize;
            RecreateFinalComposite();
            RefreshSurface();
        }

        public void InvalidateCaches()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _device.Caches.Invalidate();
            _sceneResources = null;
        }

        private void RefreshSurface()
        {
            int logicalWidth = 0;
            int logicalHeight = 0;
            int framebufferWidth = 0;
            int framebufferHeight = 0;
            SDL3.SDL_GetWindowSize(_window, &logicalWidth, &logicalHeight);
            if (!SDL3.SDL_GetWindowSizeInPixels(_window, &framebufferWidth, &framebufferHeight))
            {
                framebufferWidth = logicalWidth;
                framebufferHeight = logicalHeight;
            }
            if (logicalWidth > 0 && logicalHeight > 0)
            {
                _logicalSize = new Vector2i(logicalWidth, logicalHeight);
            }
            if (framebufferWidth >= 0 && framebufferHeight >= 0)
            {
                _framebufferSize = new Vector2i(framebufferWidth, framebufferHeight);
            }
            SDL_WindowFlags flags = SDL3.SDL_GetWindowFlags(_window);
            _minimized = (flags & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0;
            _device.Surface = new RenderSurfaceInfo(_logicalSize, _framebufferSize, _minimized,
                !_minimized && _framebufferSize.X > 0 && _framebufferSize.Y > 0,
                _device.SwapchainFormat.ToString(), _presentModeLabel);
        }

        private void RecreateFinalComposite()
        {
            if (_device is null || _framebufferSize.X <= 0 || _framebufferSize.Y <= 0) return;
            SDL_GPUTextureCreateInfo description = new SDL_GPUTextureCreateInfo
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                format = _device.SwapchainFormat,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                    | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                width = checked((uint)_framebufferSize.X),
                height = checked((uint)_framebufferSize.Y),
                layer_count_or_depth = 1,
                num_levels = 1,
                sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
            };
            SDL_GPUTexture* replacement = SDL3.SDL_CreateGPUTexture(_device.Handle, &description);
            if (replacement == null)
                throw new InvalidOperationException($"SDL final composite allocation failed: {SDL3.SDL_GetError()}");
            if (_finalComposite != null)
            {
                if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
                {
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacement);
                    throw new InvalidOperationException($"SDL final composite resize wait failed: {SDL3.SDL_GetError()}");
                }
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _finalComposite);
            }
            _finalComposite = replacement;
            _finalCompositeWidth = _framebufferSize.X;
            _finalCompositeHeight = _framebufferSize.Y;
        }

        /// <summary>Retires interrupted frame work before the next scene uses this device.</summary>
        internal void EndScene()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_commandBuffer != null)
            {
                if (_swapchainAcquireSucceeded)
                {
                    // Acquired swapchains must be submitted, even if scene rendering threw.
                    SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(_commandBuffer);
                    _commandBuffer = null;
                    if (fence != null)
                    {
                        SdlGpuFenceLease lease = _device.FrameResources.CommitFence(fence);
                        try
                        {
                            _readback.CommitSubmission(lease);
                            if (_telemetry.IsFrameActive)
                            {
                                _telemetry.MarkSubmitted();
                                _telemetry.CommitScheduledUploads();
                                _telemetry.MarkFenceCommitted();
                            }
                        }
                        finally { lease.Release(); }
                    }
                    else
                    {
                        _sceneResources?.InvalidatePendingUploads();
                        _readback.DiscardUnsubmitted();
                    }
                }
                else
                {
                    SDL3.SDL_CancelGPUCommandBuffer(_commandBuffer);
                    _commandBuffer = null;
                    _sceneResources?.InvalidatePendingUploads();
                    _readback.DiscardUnsubmitted();
                }
            }
            _currentFrame = null;
            _swapchainTexture = null;
            _swapchainAcquireSucceeded = false;
            foreach (CaptureScheduleFailure failure in _captureFailures)
                _readback.ReportFailure(new RenderCaptureFailure(failure.Request, failure.Error));
            _captureFailures.Clear();
            FlushCaptures();
            CompleteTelemetry();
        }

        private void CompleteTelemetry()
        {
            if (_telemetry.IsFrameActive)
                _telemetry.CompleteFrame();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_commandBuffer != null)
            {
                // SDL requires submission after a successful swapchain
                // acquire, including an interrupted/empty frame. Cancellation
                // is reserved for the pre-acquire failure path.
                if (_swapchainAcquireSucceeded)
                {
                    SubmitEmpty(_commandBuffer);
                }
                else
                {
                    SDL3.SDL_CancelGPUCommandBuffer(_commandBuffer);
                }
                _commandBuffer = null;
            }
            _currentFrame = null;
            _readback.Dispose();
            if (_finalComposite != null)
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _finalComposite);
                _finalComposite = null;
                _finalCompositeWidth = 0;
                _finalCompositeHeight = 0;
            }
            _device.Dispose();
            CompleteTelemetry();
        }

        private readonly record struct CaptureScheduleFailure(RenderCaptureRequest Request, string Error);
    }
}
