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

        private static readonly IReadOnlySet<int> EmptyKeys = new HashSet<int>();
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
        /// <summary>Notify presentation-owned overlays of the current drawable size.</summary>
        void OnResize(Vector2i size) { }
        void AdvanceSimulation(int steps);
        void OnDrawFrame();
        void Render(RenderBackendFrame frame, IRenderBackend backend);
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
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            long wholeFrameStart = Stopwatch.GetTimestamp();
            long inputStart = wholeFrameStart;
            long inputEnd = 0;
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
            try
            {
                // 1. Native polling/translation stays in the host before Tick.
                // This is the portable input-consumption seam before simulation.
                client.OnInput(input);
                inputEnd = Stopwatch.GetTimestamp();
                int steps;
                if (input.FrameAdvanceMode)
                {
                    // A manual step is a discontinuity in wall-clock time. Reset
                    // the accumulator first so a held display interval cannot
                    // add a second simulation tick to the explicitly requested
                    // one.
                    _timing.Reset();
                    steps = _timing.ManualStep();
                }
                else
                {
                    steps = _timing.Advance(elapsedSeconds);
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

                // 3. The backend may have no drawable image while minimized or
                // occluded. In that case there is deliberately no presentation
                // acknowledgement, but the pause-menu pump still runs below.
                bool submitted = false;
                bool acquired = backend.TryBeginFrame(out RenderBackendFrame? frame);
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
                        client.OnFramePresented();
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
                    ElapsedSeconds: elapsedSeconds);
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
