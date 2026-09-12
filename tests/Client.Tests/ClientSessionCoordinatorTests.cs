using System;
using MphRead.Mods.Launcher;
using Xunit;

namespace MphRead.Tests;

public sealed class ClientSessionCoordinatorTests
{
    [Fact]
    public void CompletionAllowsAnotherMatchWithoutClosingSession()
    {
        var flow = new ClientSessionCoordinator();
        flow.ShowHome(true, true);
        flow.BeginLaunch();
        flow.NotifyMatchStarted();
        var result = new MatchRunResult(MatchExitReason.Completed);
        flow.NotifyMatchEnded(result);
        Assert.Equal(ClientSessionPhase.Results, flow.Phase);
        Assert.Same(result, flow.LastResult);
        flow.BeginLaunch();
        flow.NotifyMatchStarted();
        flow.NotifyMatchEnded(new(MatchExitReason.LeftMatch));
        Assert.Equal(ClientSessionPhase.ReturningToLobby, flow.Phase);
        flow.ShowHome(true, true);
        Assert.Equal(ClientSessionPhase.Lobby, flow.Phase);
    }

    [Fact]
    public void FailedLaunchReturnsAndQuitIsTerminal()
    {
        var flow = new ClientSessionCoordinator();
        Assert.Throws<InvalidOperationException>(flow.NotifyMatchStarted);
        flow.BeginLaunch();
        flow.NotifyMatchEnded(new(MatchExitReason.FailedToStart));
        Assert.Equal(ClientSessionPhase.ReturningToLobby, flow.Phase);
        flow.ShowHome(true, true);
        flow.BeginLaunch();
        flow.NotifyMatchEnded(new(MatchExitReason.QuitApplication));
        Assert.Equal(ClientSessionPhase.Closing, flow.Phase);
        Assert.Throws<InvalidOperationException>(flow.BeginLaunch);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, true, MatchExitReason.FailedToStart)]
    [InlineData(true, false, false, true, false, true, false, MatchExitReason.Completed)]
    [InlineData(true, false, false, false, true, true, false, MatchExitReason.Disconnected)]
    [InlineData(true, false, false, false, false, true, true, MatchExitReason.Disconnected)]
    [InlineData(true, false, false, false, false, false, true, MatchExitReason.ClientError)]
    [InlineData(true, false, true, true, false, false, false, MatchExitReason.LeftMatch)]
    [InlineData(true, true, false, true, false, false, false, MatchExitReason.QuitApplication)]
    public void ClassifiesLifecycleOutcomes(bool started, bool quit, bool left, bool completed,
        bool interrupted, bool connectionFailed, bool clientError, MatchExitReason expected)
        => Assert.Equal(expected, MatchRunResult.Classify(started, quit, left, completed,
            interrupted, connectionFailed, clientError));
}
