using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class DesktopTransitionCoordinatorTests
{
    private static MatchTransitionState Loading(MatchTransitionStage stage)
        => new(stage, "map", "mode", "hunter", "detail");

    [Theory]
    [InlineData((int)DesktopTransitionState.Shell, "shell")]
    [InlineData((int)DesktopTransitionState.PreparingGame, "preparing-game")]
    [InlineData((int)DesktopTransitionState.Game, "game")]
    [InlineData((int)DesktopTransitionState.Results, "results")]
    [InlineData((int)DesktopTransitionState.PreparingContinuation,
        "preparing-continuation")]
    [InlineData((int)DesktopTransitionState.ReturningToShell, "returning-to-shell")]
    [InlineData((int)DesktopTransitionState.Failed, "failed")]
    [InlineData((int)DesktopTransitionState.Closing, "closing")]
    [InlineData(999, "unknown(999)")]
    public void TransitionDiagnosticNamesDoNotDependOnEnumMetadata(
        int stateValue, string expected)
    {
        Assert.Equal(expected, DesktopTransitionCoordinator.DiagnosticName(
            (DesktopTransitionState)stateValue));
    }

    [Theory]
    [InlineData(MatchExitReason.Transitioning, false)]
    [InlineData(MatchExitReason.Completed, true)]
    [InlineData(MatchExitReason.LeftMatch, true)]
    public void ResumeUsesImmutableMatchDispositionForPresentationOwnership(
        MatchExitReason reason, bool expected)
    {
        Assert.Equal(expected, GuiLauncher.ShouldReturnToShellAfterResume(
            new MatchRunResult(reason)));
    }

    [Fact]
    public void ResultsContinueRetainsCompletedMatchAndNeverReturnsToShell()
    {
        Guid completedMatch = Guid.NewGuid();
        MatchResultsPresentationResult presentation =
            PostMatchFlow.PresentationResult(PostMatchTransition.Continue);
        MatchRunResult result = Assert.IsType<MatchRunResult>(
            MatchStart.ResultForPresentation(presentation, completedMatch, null));

        Assert.True(presentation.Continue);
        Assert.Equal(MatchExitReason.Transitioning, result.Reason);
        Assert.Equal(completedMatch, result.MatchId);
        Assert.False(GuiLauncher.ShouldReturnToShellAfterResume(result));
    }

    [Fact]
    public void ReturnToLobbyDispositionStillReturnsToShell()
    {
        MatchResultsPresentationResult presentation =
            PostMatchFlow.PresentationResult(PostMatchTransition.Lobby);

        Assert.False(presentation.Continue);
        Assert.Null(MatchStart.ResultForPresentation(presentation,
            Guid.NewGuid(), null));
        Assert.True(GuiLauncher.ShouldReturnToShellAfterResume(
            new MatchRunResult(MatchExitReason.Completed)));
    }

    [Fact]
    public void ShellToGameWaitsForPreparedAndFirstPresentedFrame()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        ulong generation = coordinator.BeginMatchLaunch(Loading(
            MatchTransitionStage.Preparing));

        Assert.Equal(DesktopTransitionState.PreparingGame, coordinator.State);
        Assert.Equal(new[] { "shell-transition:Preparing" }, surface.Calls);
        Assert.False(coordinator.GameFirstFramePresented(generation));
        Assert.True(coordinator.GameWindowPrepared(generation));
        Assert.False(coordinator.GameWindowPrepared(generation));
        Assert.True(coordinator.GameFirstFramePresented(generation));
        Assert.False(coordinator.GameFirstFramePresented(generation));
        Assert.Equal(DesktopTransitionState.Game, coordinator.State);
        Assert.Equal(new[]
        {
            "shell-transition:Preparing", "show-scene", "close-shell-transition",
            "hide-shell", "activate-scene"
        }, surface.Calls);
    }

    [Fact]
    public void ResultsContinuationHidesResultsOnlyAfterNextFirstFrame()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        ulong first = coordinator.BeginMatchLaunch(Loading(MatchTransitionStage.Preparing));
        coordinator.GameWindowPrepared(first);
        coordinator.GameFirstFramePresented(first);
        coordinator.BeginResults();

        ulong second = coordinator.BeginContinuation(Loading(
            MatchTransitionStage.LoadingNextRound));
        Assert.Equal(DesktopTransitionState.PreparingContinuation, coordinator.State);
        Assert.DoesNotContain("hide-results", surface.Calls);
        coordinator.GameWindowPrepared(second);
        coordinator.GameFirstFramePresented(second);

        Assert.Equal(DesktopTransitionState.Game, coordinator.State);
        Assert.Equal("hide-results", surface.Calls[^2]);
        Assert.Equal("activate-scene", surface.Calls[^1]);
        Assert.NotEqual(first, second);
        Assert.False(coordinator.GameFirstFramePresented(first));
    }

    [Fact]
    public void GameContinuationKeepsCoordinatorPreparingUntilReplacementFrame()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        ulong first = coordinator.BeginMatchLaunch(Loading(
            MatchTransitionStage.Preparing));
        coordinator.GameWindowPrepared(first);
        coordinator.GameFirstFramePresented(first);
        surface.Calls.Clear();

        ulong continuation = coordinator.BeginContinuation(Loading(
            MatchTransitionStage.LoadingNextRound));
        Assert.Equal(DesktopTransitionState.PreparingContinuation, coordinator.State);
        Assert.DoesNotContain("hide-results", surface.Calls);
        coordinator.GameWindowPrepared(continuation);
        Assert.Equal(DesktopTransitionState.PreparingContinuation, coordinator.State);
        Assert.True(coordinator.GameFirstFramePresented(continuation));
        Assert.Equal(DesktopTransitionState.Game, coordinator.State);
        Assert.DoesNotContain("hide-results", surface.Calls);
        Assert.Contains("hide-continuation", surface.Calls);
    }

    [Fact]
    public void ReturnToShellShowsAndPumpsTargetBeforeRetiringSources()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        ulong generation = coordinator.BeginMatchLaunch(Loading(MatchTransitionStage.Preparing));
        coordinator.GameWindowPrepared(generation);
        coordinator.GameFirstFramePresented(generation);
        surface.Calls.Clear();
        surface.OnPumpShell = () => Assert.Equal(
            DesktopTransitionState.ReturningToShell, coordinator.State);

        Assert.True(coordinator.BeginReturnToShell());
        Assert.Equal(DesktopTransitionState.Shell, coordinator.State);
        Assert.Equal(new[]
        {
            "show-shell", "pump-shell", "hide-results", "hide-scene",
            "close-shell-transition", "activate-shell"
        }, surface.Calls);
        Assert.False(coordinator.BeginReturnToShell());
    }

    [Fact]
    public void ContinuationFailurePreparesShellBeforeClosingResultsAndScene()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        ulong first = coordinator.BeginMatchLaunch(Loading(MatchTransitionStage.Preparing));
        coordinator.GameWindowPrepared(first);
        coordinator.GameFirstFramePresented(first);
        coordinator.BeginResults();
        ulong second = coordinator.BeginContinuation(Loading(
            MatchTransitionStage.LoadingNextRound));
        surface.Calls.Clear();

        Assert.True(coordinator.FailLaunch(second, "failed"));
        Assert.Equal(DesktopTransitionState.Failed, coordinator.State);
        Assert.Equal(new[]
        {
            "show-shell", "pump-shell", "fail-shell:failed", "pump-shell", "hide-results",
            "hide-scene", "activate-shell"
        }, surface.Calls);
        surface.Calls.Clear();
        Assert.True(coordinator.CompleteFailedReturn());
        Assert.Equal(DesktopTransitionState.Shell, coordinator.State);
        Assert.False(coordinator.GameFirstFramePresented(second));
    }

    [Fact]
    public void StateIsCommittedBeforeReentrantSurfaceEffects()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        surface.OnShowTransition = () => Assert.Equal(
            DesktopTransitionState.PreparingGame, coordinator.State);
        ulong generation = coordinator.BeginMatchLaunch(Loading(
            MatchTransitionStage.Preparing));
        surface.OnHideShell = () => Assert.False(
            coordinator.GameFirstFramePresented(generation));
        coordinator.GameWindowPrepared(generation);
        Assert.True(coordinator.GameFirstFramePresented(generation));
    }

    [Fact]
    public void CloseDuringTargetPumpCannotBeOverwrittenByReturn()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopTransitionCoordinator(surface);
        ulong generation = coordinator.BeginMatchLaunch(Loading(
            MatchTransitionStage.Preparing));
        coordinator.GameWindowPrepared(generation);
        coordinator.GameFirstFramePresented(generation);
        surface.Calls.Clear();
        surface.OnPumpShell = coordinator.Close;

        Assert.False(coordinator.BeginReturnToShell());
        Assert.Equal(DesktopTransitionState.Closing, coordinator.State);
        Assert.Equal(new[] { "show-shell", "pump-shell" }, surface.Calls);
    }

    private sealed class RecordingSurface : IDesktopTransitionSurface
    {
        public List<string> Calls { get; } = new();
        public Action? OnShowTransition { get; set; }
        public Action? OnHideShell { get; set; }
        public Action? OnPumpShell { get; set; }
        public void ShowShellForPreparation() => Calls.Add("show-shell");
        public void PumpShell() { Calls.Add("pump-shell"); OnPumpShell?.Invoke(); }
        public void HideShell() { Calls.Add("hide-shell"); OnHideShell?.Invoke(); }
        public void ActivateShell() => Calls.Add("activate-shell");
        public void ShowSceneForPreparation() => Calls.Add("show-scene");
        public void HideScene() => Calls.Add("hide-scene");
        public void ActivateScene() => Calls.Add("activate-scene");
        public void ShowShellTransition(MatchTransitionState state)
        {
            Calls.Add($"shell-transition:{state.Stage}");
            OnShowTransition?.Invoke();
        }
        public void UpdateShellTransition(MatchTransitionState state)
            => Calls.Add($"update-shell:{state.Stage}");
        public void FailShellTransition(string message)
            => Calls.Add($"fail-shell:{message}");
        public void CloseShellTransition() => Calls.Add("close-shell-transition");
        public void ShowContinuationTransition(MatchTransitionState state)
            => Calls.Add($"continuation:{state.Stage}");
        public void UpdateContinuationTransition(MatchTransitionState state)
            => Calls.Add($"update-continuation:{state.Stage}");
        public void HideContinuationTransition() => Calls.Add("hide-continuation");
        public void ShowResults() => Calls.Add("show-results");
        public void HideResults() => Calls.Add("hide-results");
    }
}
