using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class ClientOnlineLifecycleTests
{
    [Theory]
    [InlineData(OnlineRecoveryState.Connected, true, true, OnlineLifecyclePhase.Connected)]
    [InlineData(OnlineRecoveryState.Connected, true, false, OnlineLifecyclePhase.Interrupted)]
    [InlineData(OnlineRecoveryState.Connected, false, false, OnlineLifecyclePhase.Offline)]
    [InlineData(OnlineRecoveryState.ConnectionLost, true, false, OnlineLifecyclePhase.Interrupted)]
    [InlineData(OnlineRecoveryState.ConnectionLost, false, false, OnlineLifecyclePhase.Offline)]
    [InlineData(OnlineRecoveryState.Reconnecting, true, false, OnlineLifecyclePhase.Reconnecting)]
    [InlineData(OnlineRecoveryState.RejoiningMatch, true, false, OnlineLifecyclePhase.RejoiningMatch)]
    [InlineData(OnlineRecoveryState.AwaitingMatchSnapshot, true, false, OnlineLifecyclePhase.AwaitingMatchSnapshot)]
    [InlineData(OnlineRecoveryState.SessionExpired, true, false, OnlineLifecyclePhase.SessionExpired)]
    public void PresentationMapsCanonicalRuntimeStateWithoutOwningTransitions(
        OnlineRecoveryState state, bool hasNode, bool nodeConnected,
        OnlineLifecyclePhase expected)
    {
        OnlineLifecyclePresentation presentation = OnlineLifecyclePresentation.FromRuntime(
            state, hasNode, nodeConnected, hasMatch: true);

        Assert.Equal(expected, presentation.Phase);
        Assert.Equal(state, presentation.RecoveryState);
        Assert.Equal(hasNode, presentation.HasNode);
        Assert.Equal(nodeConnected, presentation.NodeConnected);
        Assert.True(presentation.HasMatch);
        Assert.False(string.IsNullOrWhiteSpace(presentation.Message));
    }

    [Fact]
    public void ConnectedMatchProjectionIncludesOnlyDerivedPresentationDetail()
    {
        OnlineLifecyclePresentation presentation = OnlineLifecyclePresentation.FromRuntime(
            OnlineRecoveryState.Connected, hasNode: true, nodeConnected: true,
            hasMatch: true);

        Assert.Equal(OnlineLifecyclePhase.Connected, presentation.Phase);
        Assert.Equal("Connected · match active", presentation.Message);
    }

    [Fact]
    public void ControllerProjectionDerivesDiscoveryLobbyContentAndRecoveryPhases()
    {
        OnlineLifecyclePresentation runtime = OnlineLifecyclePresentation.FromRuntime(
            OnlineRecoveryState.Connected, hasNode: true, nodeConnected: true,
            hasMatch: false);
        PlayState lobby = PlayState.Initial with { Phase = PlayPhase.Lobby };
        OnlineLifecyclePresentation lobbyView = OnlineLifecyclePresentation.FromRuntime(
            runtime.RecoveryState, runtime.HasNode, runtime.NodeConnected, runtime.HasMatch,
            lobby, MapPreparationPhase.None, ClientSessionPhase.Lobby);
        Assert.Equal(OnlineLifecyclePhase.InLobby, lobbyView.Phase);

        PlayState discovering = PlayState.Initial with { Loading = true };
        OnlineLifecyclePresentation discoveryView = OnlineLifecyclePresentation.FromRuntime(
            runtime.RecoveryState, runtime.HasNode, runtime.NodeConnected, runtime.HasMatch,
            discovering, MapPreparationPhase.None, ClientSessionPhase.OnlineHome);
        Assert.Equal(OnlineLifecyclePhase.Discovering, discoveryView.Phase);

        OnlineLifecyclePresentation contentView = OnlineLifecyclePresentation.FromRuntime(
            runtime.RecoveryState, runtime.HasNode, runtime.NodeConnected, runtime.HasMatch,
            lobby, MapPreparationPhase.Compiling, ClientSessionPhase.Lobby);
        Assert.Equal(OnlineLifecyclePhase.PreparingContent, contentView.Phase);
        Assert.Equal(MapPreparationPhase.Compiling, contentView.PreparationPhase);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 250)]
    [InlineData(2, 1, 500)]
    [InlineData(3, 0, 500)]
    [InlineData(3, 1, 1000)]
    [InlineData(4, 0, 1000)]
    [InlineData(4, 1, 2000)]
    [InlineData(8, 0, 5000)]
    [InlineData(8, 1, 5000)]
    public void ReconnectDelayIsImmediateThenExponentiallyBoundedWithJitter(
        int attempt, double jitter, double expectedMilliseconds)
        => Assert.Equal(expectedMilliseconds,
            ClientOnlineRuntime.ReconnectDelay(attempt, jitter).TotalMilliseconds);

    [Fact]
    public void RetryAfterParsesSecondsDatesAndCapsUntrustedServerDelay()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;

        Assert.Equal(TimeSpan.FromSeconds(45),
            NodeControlClient.ParseRetryAfter(["45"], now));
        Assert.Equal(TimeSpan.FromMinutes(5),
            NodeControlClient.ParseRetryAfter(["9999"], now));
        Assert.Equal(TimeSpan.FromSeconds(30),
            NodeControlClient.ParseRetryAfter(
                [now.AddSeconds(30).ToString("R")], now));
        Assert.Null(NodeControlClient.ParseRetryAfter(["invalid"], now));
    }

    [Theory]
    [InlineData(NodeControlConnectFailure.RateLimited,
        OnlineRecoveryState.ControlDisconnected)]
    [InlineData(NodeControlConnectFailure.AuthenticationRejected,
        OnlineRecoveryState.Expired)]
    public void ControlUpgradeFailureMapsToRetryableOrTerminalRecovery(
        NodeControlConnectFailure failure, OnlineRecoveryState expected)
        => Assert.Equal(expected,
            ClientOnlineRuntime.RecoveryStateForConnectFailure(failure));
}
