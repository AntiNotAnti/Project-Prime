using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead
{
    /// <summary>
    /// A capture requested by a deterministic render utility. The request is
    /// deliberately backend-neutral; the SDL host queues it on the sealed
    /// RenderFrame.
    /// </summary>
    internal sealed class RenderToolCapture
    {
        public RenderToolCapture(CaptureTargetKind target, string? outputName = null)
        {
            Target = target;
            OutputName = outputName;
            RequestId = Guid.NewGuid();
        }

        public Guid RequestId { get; }
        public CaptureTargetKind Target { get; }
        public string? OutputName { get; }
    }

    /// <summary>Owned results observed while rendering one utility picture.</summary>
    internal sealed class RenderToolFrameResult
    {
        public RenderToolFrameResult(bool drew, bool acquired, bool encoded, bool submitted,
            bool acknowledged, IReadOnlyList<RenderCaptureResult>? captures = null,
            IReadOnlyList<RenderCaptureFailure>? failures = null)
        {
            Drew = drew;
            Acquired = acquired;
            Encoded = encoded;
            Submitted = submitted;
            Acknowledged = acknowledged;
            Captures = captures ?? Array.Empty<RenderCaptureResult>();
            CaptureFailures = failures ?? Array.Empty<RenderCaptureFailure>();
        }

        public bool Drew { get; }
        public bool Acquired { get; }
        public bool Encoded { get; }
        public bool Submitted { get; }
        public bool Acknowledged { get; }
        public IReadOnlyList<RenderCaptureResult> Captures { get; }
        public IReadOnlyList<RenderCaptureFailure> CaptureFailures { get; }
    }

    /// <summary>
    /// The utility-facing host boundary. OpenTK-compatible input types remain
    /// at this existing utility seam; window ownership and rendering are SDL.
    /// </summary>
    internal interface IRenderToolHost : IDisposable
    {
        Vector2i Size { get; }
        KeyboardState Keyboard { get; }
        MouseState Mouse { get; }
        bool IsVisible { get; set; }

        ScenePresentation CreatePresentation(Scene scene);
        void Run(IRenderToolClient client);
        RenderToolFrameResult Render(ScenePresentation presentation,
            RenderToolCapture? capture = null, bool acknowledgePresentation = false);
        void Close();
    }

    internal interface IRenderToolClient
    {
        void OnLoad();
        void OnFrame();
        void OnClosing();

        /// <summary>
        /// Receives a readback that completed after the picture which queued
        /// it. Synchronous hosts normally return the result from
        /// <see cref="IRenderToolHost.Render"/>; SDL may deliver it on a later
        /// graphics-thread drain.
        /// </summary>
        void OnCapture(RenderCaptureResult capture) { }
    }

    /// <summary>
    /// Keeps the host's success contract in one place: failed/skipped submits
    /// never acknowledge or finish a presentation, while a successful one
    /// acknowledges first and always runs the frame cleanup second.
    /// </summary>
    internal static class RenderToolFramePolicy
    {
        public static bool Complete(bool submitted, bool acknowledgePresentation,
            Action onFramePresented, Action afterRenderFrame)
        {
            if (!submitted) return false;
            if (acknowledgePresentation) onFramePresented();
            afterRenderFrame();
            return true;
        }
    }

    internal static class RenderToolHostFactory
    {
        public static IRenderToolHost Create(Vector2i size, string title,
            int updateFrequency = 0, bool visible = false, bool presentable = false)
        {
            _ = updateFrequency;
            return new SdlRenderToolHost(size, title, visible, presentable);
        }
    }

    /// <summary>
    /// SDL host for deterministic utilities. Offscreen tools acquire only the
    /// backend's command buffer/final target; the authoritative check opts
    /// into the presentable path explicitly. Neither path enters the
    /// wall-clock GameWindowFrameLoop, and each Run iteration invokes exactly
    /// one utility-controlled picture.
    /// </summary>
    internal sealed class SdlRenderToolHost : IRenderToolHost
    {
        private readonly SdlGameHost _host;
        private readonly SdlGpuBackend _backend;
        private readonly bool _presentable;
        private IRenderToolClient? _client;
        private bool _closed;
        private bool _disposed;
        private long _frame;

        public SdlRenderToolHost(Vector2i size, string title, bool visible, bool presentable)
        {
            _host = new SdlGameHost(size, title, showWindow: visible);
            _backend = _host.Backend;
            _presentable = presentable;
        }

        public Vector2i Size => _host.FramebufferSize;
        internal RenderBackendInfo BackendInfo => _backend.Info;
        public KeyboardState Keyboard => _host.CompatibilityInput.Keyboard;
        public MouseState Mouse => _host.CompatibilityInput.Mouse;

        public bool IsVisible
        {
            get => _host.ToolWindowVisible;
            set => _host.SetToolWindowVisible(value);
        }

        public ScenePresentation CreatePresentation(Scene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            ScenePresentation presentation = new(scene, Size, Keyboard, Mouse,
                _host.SetTitle, Close);
            _host.AttachToolPresentation(presentation);
            return presentation;
        }

        public void Run(IRenderToolClient client)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.OnLoad();
            while (!_closed && _host.PumpToolEvents())
            {
                PublishPendingCaptures();
                if (_closed) break;
                _client.OnFrame();
                PublishPendingCaptures();
            }
            // Readbacks are asynchronous by design. A deterministic utility
            // is allowed to perform this explicit final wait for any work not
            // requested through Render (for example, a request queued while
            // closing), while requested tool pictures are flushed below.
            _backend.FlushCaptures();
            PublishPendingCaptures();
            _client.OnClosing();
        }

        public RenderToolFrameResult Render(ScenePresentation presentation,
            RenderToolCapture? capture = null, bool acknowledgePresentation = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            long originatingFrame = _frame++;
            if (capture != null)
            {
                presentation.QueueSdlCapture(CreateRequest(presentation, capture, originatingFrame));
            }

            presentation.OnDrawFrame();
            if (presentation.Exiting)
            {
                return new RenderToolFrameResult(drew: true, acquired: false,
                    encoded: false, submitted: false, acknowledged: false);
            }
            bool hasCaptureRequest = presentation.CurrentRenderFrame.CaptureRequests.Count > 0;

            bool acquired;
            RenderBackendFrame frame;
            if (_presentable)
            {
                acquired = _backend.TryBeginFrame(out frame);
                if (!acquired)
                {
                    return new RenderToolFrameResult(drew: true, acquired: false,
                        encoded: false, submitted: false, acknowledged: false,
                        DrainCaptures(out _));
                }
                _backend.Render(frame, presentation.CurrentRenderFrame);
                bool submitted = _backend.TrySubmitFrame(frame);
                // Utility probes compare a specific simulated picture with its
                // pixels. Keep gameplay capture asynchronous, but make this
                // deliberately narrow tool boundary deterministic so a result
                // cannot arrive on an unrelated later probe frame.
                if (submitted && hasCaptureRequest) _backend.FlushCaptures();
                return FinishFrame(presentation, frame, submitted,
                    acknowledgePresentation, DrainCaptures(out List<RenderCaptureFailure> failures), failures);
            }

            if (_backend is not ISdlOffscreenToolBackend offscreen)
            {
                throw new PlatformNotSupportedException(
                    "The SDL GPU backend does not expose the deterministic offscreen tool path.");
            }
            acquired = offscreen.TryBeginOffscreenFrame(out frame);
            if (!acquired)
            {
                return new RenderToolFrameResult(drew: true, acquired: false,
                    encoded: false, submitted: false, acknowledged: false,
                    DrainCaptures(out _));
            }
            offscreen.RenderOffscreen(frame, presentation.CurrentRenderFrame);
            bool offscreenSubmitted = offscreen.TrySubmitOffscreenFrame(frame);
            if (offscreenSubmitted && hasCaptureRequest) offscreen.FlushCaptures();
            return FinishFrame(presentation, frame, offscreenSubmitted,
                acknowledgePresentation: false,
                DrainCaptures(out List<RenderCaptureFailure> offscreenFailures), offscreenFailures);
        }

        private RenderToolFrameResult FinishFrame(ScenePresentation presentation,
            RenderBackendFrame frame, bool submitted, bool acknowledgePresentation,
            List<RenderCaptureResult> captures, List<RenderCaptureFailure> failures)
        {
            bool acknowledged = submitted && acknowledgePresentation;
            RenderToolFramePolicy.Complete(submitted, acknowledgePresentation,
                presentation.OnFramePresented, presentation.AfterRenderFrame);
            return new RenderToolFrameResult(drew: true, acquired: true,
                encoded: frame.Encoded, submitted, acknowledged, captures, failures);
        }

        private RenderCaptureRequest CreateRequest(ScenePresentation presentation,
            RenderToolCapture capture, long originatingFrame)
        {
            Vector2i size = capture.Target == CaptureTargetKind.FinalPresentedFrame
                ? Size : presentation.RenderSize;
            return new RenderCaptureRequest(capture.RequestId, originatingFrame,
                capture.Target, Math.Max(1, size.X), Math.Max(1, size.Y),
                CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp,
                CaptureDeliveryKind.Screenshot, capture.OutputName);
        }

        private List<RenderCaptureResult> DrainCaptures(out List<RenderCaptureFailure> failures)
        {
            failures = new List<RenderCaptureFailure>();
            List<RenderCaptureResult> results = new();
            if (_backend is not IRenderCaptureSource source)
            {
                return results;
            }
            while (source.TryDequeueCapture(out RenderCaptureResult? result))
            {
                if (result != null) results.Add(result);
            }
            while (source.TryDequeueCaptureFailure(out RenderCaptureFailure? failure))
            {
                if (failure != null) failures.Add(failure);
            }
            return results;
        }

        private void PublishPendingCaptures()
        {
            List<RenderCaptureResult> captures = DrainCaptures(out List<RenderCaptureFailure> failures);
            for (int i = 0; i < captures.Count; i++)
            {
                _client?.OnCapture(captures[i]);
            }
            for (int i = 0; i < failures.Count; i++)
            {
                Console.Error.WriteLine($"[capture] request {failures[i].RequestId} ({failures[i].Delivery}) failed: {failures[i].Error}");
            }
        }

        public void Close() => _closed = true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _host.DetachToolPresentation();
            _host.Dispose();
        }
    }

    /// <summary>Backend-neutral pixel probes and PNG export for SDL tools.</summary>
    internal static class RenderToolCaptureSupport
    {
        public static double NonBlackFraction(RenderCaptureResult capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            ReadOnlySpan<byte> pixels = capture.Bytes.Span;
            int bpp = RenderCaptureResult.BytesPerPixel(capture.PixelFormat);
            int total = capture.Width * capture.Height;
            int lit = 0;
            for (int i = 0; i + bpp <= pixels.Length; i += bpp)
            {
                byte red;
                byte green;
                byte blue;
                if (capture.PixelFormat == CapturePixelFormat.Bgra8)
                {
                    blue = pixels[i];
                    green = pixels[i + 1];
                    red = pixels[i + 2];
                }
                else
                {
                    red = pixels[i];
                    green = pixels[i + 1];
                    blue = pixels[i + 2];
                }
                if (red > 8 || green > 8 || blue > 8) lit++;
            }
            return total == 0 ? 0 : lit / (double)total;
        }

        public static bool Save(RenderCaptureResult capture, string path)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (path == null) throw new ArgumentNullException(nameof(path));
            try
            {
                if (NonBlackFraction(capture) < 0.01)
                {
                    Console.WriteLine($"[capture] {System.IO.Path.GetFileName(path)} came out black "
                        + $"({NonBlackFraction(capture) * 100:0.00}% lit, {capture.Width}x{capture.Height}); not saving it.");
                    return false;
                }
                string? directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
                Action<byte[], int, int, string>? writer = Mods.ScreenCapture.PngWriter;
                if (writer == null) throw new InvalidOperationException("This platform has not installed a PNG encoder.");
                writer(capture.CopyBytes(), capture.Width, capture.Height, path);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[capture] could not save {path}: {ex.Message}");
                return false;
            }
        }
    }
}
