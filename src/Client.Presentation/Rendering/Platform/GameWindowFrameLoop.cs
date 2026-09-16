using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using MphRead.Mods.Render;

namespace MphRead
{
    /// <summary>
    /// Input after the native host has translated its events. It is
    /// intentionally independent of SDL/OpenTK window objects so the scene
    /// and test harness can consume one stable shape on every desktop host.
    /// </summary>
    public readonly struct WindowInputSnapshot
    {
        private static readonly IReadOnlySet<int> EmptyKeys = new HashSet<int>();

        /// <summary>
        /// Stable input-free snapshot for deterministic host modes. Unlike the
        /// default struct value, its collection and text properties are never null.
        /// </summary>
        public static WindowInputSnapshot Neutral { get; } = new(null, null,
            default, default, default, string.Empty, focused: false);

        public WindowInputSnapshot(IReadOnlySet<int>? keys, IReadOnlySet<int>? mouseButtons,
            Vector2 mousePosition, Vector2 relativeMouse, Vector2 wheel, string text, bool focused,
            IReadOnlyList<WindowKeyEvent>? keyEvents = null,
            IReadOnlyList<WindowMouseButtonEvent>? mouseButtonEvents = null,
            bool frameAdvanceMode = false)
        {
            Keys = keys ?? EmptyKeys;
            MouseButtons = mouseButtons ?? EmptyKeys;
            MousePosition = mousePosition;
            RelativeMouse = relativeMouse;
            Wheel = wheel;
            Text = text ?? string.Empty;
            Focused = focused;
            KeyEvents = keyEvents ?? Array.Empty<WindowKeyEvent>();
            MouseButtonEvents = mouseButtonEvents ?? Array.Empty<WindowMouseButtonEvent>();
            FrameAdvanceMode = frameAdvanceMode;
        }

        public IReadOnlySet<int> Keys { get; }
        public IReadOnlySet<int> MouseButtons { get; }
        public Vector2 MousePosition { get; }
        public Vector2 RelativeMouse { get; }
        public Vector2 Wheel { get; }
        public string Text { get; }
        public bool Focused { get; }
        public IReadOnlyList<WindowKeyEvent> KeyEvents { get; }
        public IReadOnlyList<WindowMouseButtonEvent> MouseButtonEvents { get; }
        /// <summary>
        /// The host is in frame-advance mode. The loop still invokes one
        /// presentation tick per picture so camera/UI state can update, but
        /// ScenePresentation's ProcessFrame gate decides whether authority
        /// advances. This preserves the legacy paused-frame semantics.
        /// </summary>
        public bool FrameAdvanceMode { get; }
    }

    /// <summary>One translated mouse button edge in the existing OpenTK-compatible model.</summary>
    public readonly record struct WindowMouseButtonEvent(MouseButton Button, bool Down);

    /// <summary>
    /// The scene-facing half of a desktop loop. The host owns event polling;
    /// the client owns simulation, presentation and submissions.
    /// </summary>
    public interface IGameWindowFrameClient
    {
        void OnInput(WindowInputSnapshot input);
        /// <summary>
        /// The active presentation is consuming a logical command surface and
        /// native compatibility input must not reach the live scene this tick.
        /// </summary>
        bool SuppressNativeInput => false;
        /// <summary>
        /// Replace the host snapshot with a neutral value before scene input
        /// handling and frame-advance evaluation. Deterministic benchmark hosts
        /// use this to prevent local input from changing measured work.
        /// </summary>
        bool DiscardInputSnapshot => false;
        /// <summary>Notify presentation-owned overlays of the current drawable size.</summary>
        void OnResize(Vector2i size) { }
        void AdvanceSimulation(int steps);
        void OnDrawFrame();
        void Render(RenderBackendFrame frame, IRenderBackend backend);
        /// <summary>Called after any encoded GPU frame is successfully submitted.</summary>
        void OnFrameRendered() { }
        /// <summary>Called only when that submission also owns a swapchain image.</summary>
        void OnFramePresented();
        void AfterRenderFrame();
        void PumpPauseMenu();
        /// <summary>
        /// A presentation update may finish the scene (for example when a
        /// fade reaches an exit transition). In that case the host still
        /// pumps auxiliary UI but must not acquire, submit, or acknowledge a
        /// GPU frame built after the exit boundary.
        /// </summary>
        bool CanRenderFrame => true;
    }

    /// <summary>
    /// Timing captured by a native host around the work that happens before a
    /// presentation tick. This is deliberately a value type: the SDL loop
    /// passes one through every frame without creating a diagnostic object.
    /// </summary>
    public readonly struct FrameLoopHostTimingContext
    {
        private readonly long _wholeFrameStartTimestamp;
        private readonly double _hostWorkMilliseconds;
        private readonly double _softwarePacingMilliseconds;

        public FrameLoopHostTimingContext(long wholeFrameStartTimestamp,
            double hostWorkMilliseconds, double softwarePacingMilliseconds)
        {
            _wholeFrameStartTimestamp = wholeFrameStartTimestamp;
            _hostWorkMilliseconds = hostWorkMilliseconds;
            _softwarePacingMilliseconds = softwarePacingMilliseconds;
        }

        /// <summary>Timestamp captured immediately before host pacing begins.</summary>
        public long WholeFrameStartTimestamp => _wholeFrameStartTimestamp;

        /// <summary>Host event/input/snapshot work before <see cref="GameWindowFrameLoop.Tick"/>.</summary>
        public double HostWorkMilliseconds => _wholeFrameStartTimestamp == 0
            ? double.NaN : _hostWorkMilliseconds;

        /// <summary>Time spent in the host's optional software pacing wait.</summary>
        public double SoftwarePacingMilliseconds => _wholeFrameStartTimestamp == 0
            ? double.NaN : _softwarePacingMilliseconds;
    }

    /// <summary>
    /// Shared loop ordering used by SDL and deterministic host tests.
    /// FrameTiming remains the only source of simulation ticks, preserving the
    /// fixed 60 Hz contract and late-latched input ordering.
    /// </summary>
    public sealed class GameWindowFrameLoop
    {
        private readonly FrameTiming _timing;

        public GameWindowFrameLoop(FrameTiming timing)
        {
            _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        }

        public void Tick(double elapsedSeconds, WindowInputSnapshot input,
            IGameWindowFrameClient client, IRenderBackend backend)
            => Tick(elapsedSeconds, input, client, backend, default);

        public void Tick(double elapsedSeconds, WindowInputSnapshot input,
            IGameWindowFrameClient client, IRenderBackend backend,
            in FrameLoopHostTimingContext hostTiming)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            long tickStart = Stopwatch.GetTimestamp();
            long wholeFrameStart = hostTiming.WholeFrameStartTimestamp != 0
                ? hostTiming.WholeFrameStartTimestamp : tickStart;
            long inputStart = tickStart;
            long inputEnd = 0;
            long frameTimingAdvanceStart = 0;
            long frameTimingAdvanceEnd = 0;
            long simulationStart = 0;
            long simulationEnd = 0;
            long legacyRenderStart = 0;
            long drawListBuildStart = 0;
            long drawListBuildEnd = 0;
            long renderEncodeStart = 0;
            long renderEncodeEnd = 0;
            long renderSubmitStart = 0;
            long renderSubmitEnd = 0;
            long overlayUiStart = 0;
            long overlayUiEnd = 0;
            long afterFrameStart = 0;
            long afterFrameEnd = 0;
            long swapchainAcquireStart = 0;
            long swapchainAcquireEnd = 0;
            bool acquisitionAttempted = false;
            bool acquired = false;
            bool submitted = false;
            bool presented = false;
            RenderBackendFrame? frame = null;
            try
            {
                // 1. Native polling/translation stays in the host before Tick.
                // This is the portable input-consumption seam before simulation.
                WindowInputSnapshot frameInput = client.DiscardInputSnapshot
                    ? WindowInputSnapshot.Neutral : input;
                client.OnInput(frameInput);
                inputEnd = Stopwatch.GetTimestamp();
                int steps;
                if (frameInput.FrameAdvanceMode)
                {
                    // A manual step is a discontinuity in wall-clock time. Reset
                    // the accumulator first so a held display interval cannot
                    // add a second simulation tick to the explicitly requested
                    // one.
                    frameTimingAdvanceStart = Stopwatch.GetTimestamp();
                    _timing.Reset();
                    steps = _timing.ManualStep();
                    frameTimingAdvanceEnd = Stopwatch.GetTimestamp();
                }
                else
                {
                    frameTimingAdvanceStart = Stopwatch.GetTimestamp();
                    steps = _timing.Advance(elapsedSeconds);
                    frameTimingAdvanceEnd = Stopwatch.GetTimestamp();
                }
                simulationStart = Stopwatch.GetTimestamp();
                client.AdvanceSimulation(steps);
                simulationEnd = Stopwatch.GetTimestamp();

                // 2. Build and submit presentation data once per drawn frame.
                legacyRenderStart = simulationEnd;
                drawListBuildStart = legacyRenderStart;
                client.OnDrawFrame();
                drawListBuildEnd = Stopwatch.GetTimestamp();

                if (!client.CanRenderFrame)
                {
                    overlayUiStart = Stopwatch.GetTimestamp();
                    client.PumpPauseMenu();
                    overlayUiEnd = Stopwatch.GetTimestamp();
                    return;
                }

                // 3. The backend may decline acquisition (normally because no
                // drawable image exists while minimized or occluded). The
                // portable bool contract does not expose the exact cause. In
                // every case there is deliberately no presentation
                // acknowledgement, but the pause-menu pump still runs below.
                swapchainAcquireStart = Stopwatch.GetTimestamp();
                acquired = backend.TryBeginFrame(out frame);
                swapchainAcquireEnd = Stopwatch.GetTimestamp();
                acquisitionAttempted = true;
                if (acquired)
                {
                    renderEncodeStart = Stopwatch.GetTimestamp();
                    client.Render(frame, backend);
                    renderEncodeEnd = Stopwatch.GetTimestamp();
                    renderSubmitStart = renderEncodeEnd;
                    // The portable backend contract combines command submission
                    // and any native swap/present in one call. Attribute that
                    // combined CPU seam to RenderSubmit; Present remains absent
                    // rather than timing the later acknowledgement callback.
                    bool submitSucceeded = backend.TrySubmitFrame(frame);
                    renderSubmitEnd = Stopwatch.GetTimestamp();
                    if (submitSucceeded)
                    {
                        submitted = true;
                        presented = frame.HasSwapchain;
                        client.OnFrameRendered();
                        if (presented)
                        {
                            client.OnFramePresented();
                        }
                        // Pause/settings UI is owned by the window thread and
                        // must be serviced after the present acknowledgement but
                        // before the frame's transient state is retired.
                        overlayUiStart = Stopwatch.GetTimestamp();
                        client.PumpPauseMenu();
                        overlayUiEnd = Stopwatch.GetTimestamp();
                    }
                }

                // The desktop path records/clears its per-frame state only
                // after a real swapchain image was encoded and submitted. A
                // minimized, occluded, or failed-submit frame must not advance
                // that acknowledgement boundary; input/pause pumping below is
                // still required for the auxiliary UI to remain responsive.
                if (submitted)
                {
                    afterFrameStart = Stopwatch.GetTimestamp();
                    client.AfterRenderFrame();
                    afterFrameEnd = Stopwatch.GetTimestamp();
                }
                // Avalonia pause/settings windows must remain responsive even when
                // the game surface did not produce a swapchain image. The success
                // path already pumped above to preserve the desktop ordering.
                if (!submitted)
                {
                    overlayUiStart = Stopwatch.GetTimestamp();
                    client.PumpPauseMenu();
                    overlayUiEnd = Stopwatch.GetTimestamp();
                }
            }
            finally
            {
                long now = Stopwatch.GetTimestamp();
                var sample = new FramePhaseTimingSample(
                    InputMilliseconds: Milliseconds(inputStart, inputEnd, now),
                    SimulationMilliseconds: Milliseconds(simulationStart, simulationEnd, now),
                    // The portable desktop loop has no honest seam between scene
                    // preparation and draw-list construction. OnDrawFrame owns the
                    // narrowest observable build bucket, so preparation stays absent.
                    ScenePreparationMilliseconds: double.NaN,
                    DrawListBuildMilliseconds: Milliseconds(drawListBuildStart,
                        drawListBuildEnd, now),
                    RenderEncodeMilliseconds: Milliseconds(renderEncodeStart,
                        renderEncodeEnd, now),
                    RenderSubmitMilliseconds: Milliseconds(renderSubmitStart,
                        renderSubmitEnd, now),
                    PresentMilliseconds: double.NaN,
                    OverlayUiMilliseconds: Milliseconds(overlayUiStart, overlayUiEnd, now),
                    AfterFrameMilliseconds: Milliseconds(afterFrameStart, afterFrameEnd, now),
                    WholeFrameMilliseconds: Milliseconds(wholeFrameStart, now, now),
                    LegacyTotalRenderMilliseconds: Milliseconds(legacyRenderStart, now, now),
                    ElapsedSeconds: elapsedSeconds,
                    HostWorkMilliseconds: hostTiming.HostWorkMilliseconds,
                    SoftwarePacingMilliseconds: hostTiming.SoftwarePacingMilliseconds,
                    FrameTimingAdvanceMilliseconds: Milliseconds(frameTimingAdvanceStart,
                        frameTimingAdvanceEnd, now),
                    SwapchainAcquireMilliseconds: Milliseconds(swapchainAcquireStart,
                        swapchainAcquireEnd, now),
                    PresentedWholeFrameMilliseconds: presented
                        ? Milliseconds(wholeFrameStart, now, now) : double.NaN,
                    Acquired: acquired,
                    Submitted: submitted,
                    Presented: presented,
                    AcquisitionAttempted: acquisitionAttempted);
                _timing.RecordRuntimeFrame(in sample);
            }
        }

        private static double Milliseconds(long start, long end, long fallback)
        {
            if (start == 0) return double.NaN;
            long completed = end != 0 ? end : fallback;
            return (completed - start) * 1000.0 / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// Platform lifecycle abstraction. SDL owns the desktop window and event
    /// loop; Android supplies its existing SurfaceView/EGL implementation.
    /// </summary>
    public interface IGameWindowHost : IDisposable
    {
        Vector2i LogicalSize { get; }
        Vector2i FramebufferSize { get; }
        bool IsMinimized { get; }
        bool IsFocused { get; }
        void Close();
        void SetTitle(string title);
        void SetCursorCaptured(bool captured);
        void ApplyWindowMode(MphRead.Mods.WindowStartMode mode);
        void Run(IGameWindowFrameClient client);
    }
}
