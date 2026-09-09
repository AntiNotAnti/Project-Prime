using System;
using System.Collections.Generic;
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
        public void Tick(double elapsedSeconds, WindowInputSnapshot input,
            IGameWindowFrameClient client, IRenderBackend backend)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (backend == null) throw new ArgumentNullException(nameof(backend));

            // 1. Poll/translate input before the clock and simulation.
            client.OnInput(input);
            int steps;
            if (input.FrameAdvanceMode)
            {
                // A manual step is a discontinuity in wall-clock time. Reset
                // the accumulator first so a held display interval cannot
                // add a second simulation tick to the explicitly requested
                // one.
                FrameTiming.Reset();
                steps = FrameTiming.ManualStep();
            }
            else
            {
                steps = FrameTiming.Advance(elapsedSeconds);
            }
            client.AdvanceSimulation(steps);

            // 2. Build presentation data once per drawn frame.
            client.OnDrawFrame();

            if (!client.CanRenderFrame)
            {
                client.PumpPauseMenu();
                return;
            }

            // 3. The backend may have no drawable image while minimized or
            // occluded. In that case there is deliberately no presentation
            // acknowledgement, but the pause-menu pump still runs below.
            bool submitted = false;
            if (backend.TryBeginFrame(out RenderBackendFrame? frame))
            {
                client.Render(frame, backend);
                if (backend.TrySubmitFrame(frame))
                {
                    submitted = true;
                    client.OnFramePresented();
                    // Pause/settings UI is owned by the window thread and
                    // must be serviced after the present acknowledgement but
                    // before the frame's transient state is retired.
                    client.PumpPauseMenu();
                }
            }

            // The desktop path records/clears its per-frame state only
            // after a real swapchain image was encoded and submitted. A
            // minimized, occluded, or failed-submit frame must not advance
            // that acknowledgement boundary; input/pause pumping below is
            // still required for the auxiliary UI to remain responsive.
            if (submitted)
            {
                client.AfterRenderFrame();
            }
            // Avalonia pause/settings windows must remain responsive even when
            // the game surface did not produce a swapchain image. The success
            // path already pumped above to preserve the desktop ordering.
            if (!submitted)
            {
                client.PumpPauseMenu();
            }
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
