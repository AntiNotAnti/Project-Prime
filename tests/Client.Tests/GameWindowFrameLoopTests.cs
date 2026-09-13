using System;
using System.Collections.Generic;
using MphRead;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;

namespace ProjectPrime.Client.Tests;

public sealed class GameWindowFrameLoopTests
{
    private static readonly string[] SuccessfulOrder =
        ["input", "simulation", "draw", "begin", "render", "submit", "presented", "pause", "after"];
    private static readonly string[] FailedAcquireOrder =
        ["input", "simulation", "draw", "begin", "pause"];
    private static readonly string[] FailedSubmitOrder =
        ["input", "simulation", "draw", "begin", "render", "submit", "pause"];

    [Fact]
    public void SuccessfulFramePreservesAcknowledgementAndRetirementOrder()
    {
        var timing = new FrameTiming();
        var client = new RecordingClient();
        using var backend = new RecordingBackend(begin: true, submit: true, client.Order);

        new GameWindowFrameLoop(timing).Tick(FrameTiming.StepSeconds, default, client, backend);

        Assert.Equal(SuccessfulOrder, client.Order);
        Assert.Equal(1, client.SimulationSteps);
        FrameTimingDiagnosticsSnapshot snapshot = timing.CaptureDiagnostics();
        Assert.Equal(1, snapshot.Input.Count);
        Assert.Equal(1, snapshot.Simulation.Count);
        Assert.Equal(0, snapshot.ScenePreparation.Count);
        Assert.Equal(1, snapshot.DrawListBuild.Count);
        Assert.Equal(1, snapshot.RenderEncode.Count);
        Assert.Equal(1, snapshot.RenderSubmit.Count);
        Assert.Equal(0, snapshot.Present.Count);
        Assert.Equal(1, snapshot.OverlayUi.Count);
        Assert.Equal(1, snapshot.AfterFrame.Count);
        Assert.Equal(1, snapshot.WholeFrame.Count);
        Assert.Equal(1, snapshot.LegacyTotalRender.Count);
    }

    [Fact]
    public void FailedAcquireDoesNotAcknowledgeAndLeavesPresentPhaseUnavailable()
    {
        var timing = new FrameTiming();
        var client = new RecordingClient();
        using var backend = new RecordingBackend(begin: false, submit: false, client.Order);

        new GameWindowFrameLoop(timing).Tick(0, default, client, backend);

        Assert.Equal(FailedAcquireOrder, client.Order);
        Assert.Equal(0, client.Presented);
        Assert.Equal(0, client.AfterRender);
        FrameTimingDiagnosticsSnapshot snapshot = timing.CaptureDiagnostics();
        Assert.Equal(1, snapshot.Input.Count);
        Assert.Equal(1, snapshot.Simulation.Count);
        Assert.Equal(0, snapshot.ScenePreparation.Count);
        Assert.Equal(1, snapshot.DrawListBuild.Count);
        Assert.Equal(0, snapshot.RenderEncode.Count);
        Assert.Equal(0, snapshot.RenderSubmit.Count);
        Assert.Equal(0, snapshot.Present.Count);
        Assert.Equal(1, snapshot.OverlayUi.Count);
        Assert.Equal(0, snapshot.AfterFrame.Count);
        Assert.Equal(1, snapshot.WholeFrame.Count);
        Assert.Equal(1, snapshot.LegacyTotalRender.Count);
    }

    [Fact]
    public void FailedSubmitRecordsEncodeAndSubmitWithoutPresentOrRetirement()
    {
        var timing = new FrameTiming();
        var client = new RecordingClient();
        using var backend = new RecordingBackend(begin: true, submit: false, client.Order);

        new GameWindowFrameLoop(timing).Tick(0, default, client, backend);

        Assert.Equal(FailedSubmitOrder, client.Order);
        FrameTimingDiagnosticsSnapshot snapshot = timing.CaptureDiagnostics();
        Assert.Equal(1, snapshot.RenderEncode.Count);
        Assert.Equal(1, snapshot.RenderSubmit.Count);
        Assert.Equal(0, snapshot.Present.Count);
        Assert.Equal(1, snapshot.OverlayUi.Count);
        Assert.Equal(0, snapshot.AfterFrame.Count);
        Assert.Equal(1, snapshot.WholeFrame.Count);
    }

    [Fact]
    public void ManualAdvanceClearsDebtAndRunsExactlyOneStep()
    {
        var timing = new FrameTiming();
        Assert.Equal(FrameTiming.MaxCatchUpSteps,
            timing.Advance(FrameTiming.StepSeconds * FrameTiming.MaxCatchUpSteps));
        var client = new RecordingClient();
        using var backend = new RecordingBackend(begin: true, submit: true, client.Order);
        var input = new WindowInputSnapshot(null, null, default, default, default,
            string.Empty, focused: true, frameAdvanceMode: true);

        new GameWindowFrameLoop(timing).Tick(.25, input, client, backend);

        Assert.Equal(1, client.SimulationSteps);
        Assert.Equal(1, timing.StepsThisFrame);
        Assert.Equal(0, timing.Advance(0));
    }

    [Fact]
    public void SeparateLoopsCannotMutateEachOthersClock()
    {
        var first = new FrameTiming();
        var second = new FrameTiming();
        var client = new RecordingClient();
        using var backend = new RecordingBackend(begin: false, submit: false, client.Order);

        new GameWindowFrameLoop(first).Tick(FrameTiming.StepSeconds,
            default, client, backend);

        Assert.Equal(1, first.TotalFrames);
        Assert.Equal(1, first.TotalSteps);
        Assert.Equal(0, second.TotalFrames);
        Assert.Equal(0, second.TotalSteps);
    }

    private sealed class RecordingClient : IGameWindowFrameClient
    {
        private readonly RenderFrame _snapshot = new(1, 1);
        public List<string> Order { get; } = new();
        public int SimulationSteps { get; private set; }
        public int Presented { get; private set; }
        public int AfterRender { get; private set; }

        public void OnInput(WindowInputSnapshot input) => Order.Add("input");
        public void AdvanceSimulation(int steps)
        {
            SimulationSteps += steps;
            Order.Add("simulation");
        }
        public void OnDrawFrame() => Order.Add("draw");
        public void Render(RenderBackendFrame frame, IRenderBackend backend)
        {
            Order.Add("render");
            backend.Render(frame, _snapshot);
        }
        public void OnFramePresented()
        {
            Presented++;
            Order.Add("presented");
        }
        public void AfterRenderFrame()
        {
            AfterRender++;
            Order.Add("after");
        }
        public void PumpPauseMenu() => Order.Add("pause");
    }

    private sealed class RecordingBackend : IRenderBackend
    {
        private readonly bool _begin;
        private readonly bool _submit;
        private readonly List<string> _order;
        private RenderBackendFrame? _frame;

        public RecordingBackend(bool begin, bool submit, List<string> order)
        {
            _begin = begin;
            _submit = submit;
            _order = order;
        }

        public RenderBackendInfo Info => new("test", "test", "test", "test", "test",
            true, true, true);
        public RenderSurfaceInfo Surface => new(new Vector2i(1, 1), new Vector2i(1, 1),
            !_begin, _begin, "test", "test");

        public bool TryBeginFrame(out RenderBackendFrame frame)
        {
            _order.Add("begin");
            if (!_begin)
            {
                frame = null!;
                return false;
            }
            _frame = frame = new RenderBackendFrame(true, false, new Vector2i(1, 1));
            return true;
        }

        public void Render(RenderBackendFrame frame, RenderFrame snapshot)
        {
            Assert.Same(_frame, frame);
            frame.Encoded = true;
        }

        public bool TrySubmitFrame(RenderBackendFrame frame)
        {
            _order.Add("submit");
            if (!_submit || !ReferenceEquals(_frame, frame)) return false;
            frame.Submitted = true;
            return true;
        }

        public void Resize(Vector2i logicalSize, Vector2i framebufferSize) { }
        public void InvalidateCaches() { }
        public void Dispose() { }
    }
}
