using MphRead.Mods.Network;
using MphRead.Mods.Launcher;
using ProjectPrime.Server.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class ClientOnlineRuntimeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("false")]
    [InlineData("off")]
    [InlineData("0")]
    public void OnlineRuntimeIsUnconditional(string? value)
        => Assert.True(ClientOnlineRuntime.ParseEnabled(value));

    [Fact]
    public async Task RuntimeOwnsTheExistingFlowStateMachineAndLifecycleCancellation()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        Assert.Equal(ClientSessionPhase.Gateway, runtime.Flow.Phase);
        runtime.Flow.ShowHome(hasIdentity: true, hasLobby: false);
        Assert.Equal(ClientSessionPhase.OnlineHome, runtime.Flow.Phase);
        CancellationToken lifetime = runtime.Lifetime;
        Assert.False(lifetime.IsCancellationRequested);
        await runtime.DisposeAsync();
        Assert.True(lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task RejoinStormSettlesOlderRequestAndKeepsOnlyNewestHandoff()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        using var play = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
        Guid matchId = Guid.NewGuid();
        MatchClientContext context = runtime.AdoptMatch(play, matchId)!;
        NodeMatchHandoff firstHandoff = Handoff(matchId, 1);
        NodeMatchHandoff newestHandoff = Handoff(matchId, 2);

        Task<RejoinCompletion> first = context.QueueRejoinAsync(firstHandoff, CancellationToken.None);
        Task<RejoinCompletion> newest = context.QueueRejoinAsync(newestHandoff, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        Assert.True(context.TryTakeRejoin(out RejoinRequest request));
        Assert.Equal(newestHandoff, request.Handoff);
        Assert.True(context.IsCurrentRejoin(request));
        context.CancelPendingRejoin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await newest);
    }

    [Fact]
    public async Task DisposedMatchSettlesQueuedRejoinAndRejectsOldOwner()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        using var play = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
        Guid matchId = Guid.NewGuid();
        MatchClientContext context = runtime.AdoptMatch(play, matchId)!;
        Task<RejoinCompletion> pending = context.QueueRejoinAsync(Handoff(matchId, 3), CancellationToken.None);

        runtime.ReleaseMatch(play, dispose: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.False(context.Owns(play));
        Assert.False(context.TryTakeRejoin(out _));
    }

    [Fact]
    public async Task RejoinHandoffCannotCrossMatchOwnership()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        using var play = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
        MatchClientContext context = runtime.AdoptMatch(play, Guid.NewGuid())!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.QueueRejoinAsync(Handoff(Guid.NewGuid(), 31), CancellationToken.None));
        Assert.False(context.TryTakeRejoin(out _));
    }

    [Fact]
    public async Task RejoinQueueDoesNotBlockOwnerAndSettlesCompletionExactlyOnce()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        using var play = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
        Guid matchId = Guid.NewGuid();
        MatchClientContext context = runtime.AdoptMatch(play, matchId)!;

        Task<RejoinCompletion> pending = context.QueueRejoinAsync(Handoff(matchId, 4), CancellationToken.None);
        Assert.False(pending.IsCompleted);
        Assert.True(context.TryTakeRejoin(out RejoinRequest request));

        RejoinCompletion expected = new(91, 7);
        context.CompleteRejoin(request, expected);
        context.CompleteRejoin(request, new RejoinCompletion(92, 8));
        context.FailRejoin(request, new InvalidOperationException("late failure"));
        Assert.Equal(expected, await pending.WaitAsync(TimeSpan.FromSeconds(1)));

        Task<RejoinCompletion> cancelled = context.QueueRejoinAsync(Handoff(matchId, 5), CancellationToken.None);
        context.CancelPendingRejoin();
        context.CancelPendingRejoin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
    }

    [Fact]
    public async Task StaleCompletionCannotTouchReplacementMatch()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        using var oldPlay = new AuthoritativePlay("127.0.0.1", 5000, "Old", Hunter.Samus);
        Guid oldMatchId = Guid.NewGuid();
        MatchClientContext oldContext = runtime.AdoptMatch(oldPlay, oldMatchId)!;
        Task<RejoinCompletion> stale = oldContext.QueueRejoinAsync(Handoff(oldMatchId, 6), CancellationToken.None);
        Assert.True(oldContext.TryTakeRejoin(out RejoinRequest staleRequest));

        runtime.ReleaseMatch(oldPlay, dispose: true);

        using var newPlay = new AuthoritativePlay("127.0.0.1", 5000, "New", Hunter.Samus);
        Guid newMatchId = Guid.NewGuid();
        MatchClientContext newContext = runtime.AdoptMatch(newPlay, newMatchId)!;
        Task<RejoinCompletion> current = newContext.QueueRejoinAsync(Handoff(newMatchId, 7), CancellationToken.None);
        Assert.True(newContext.TryTakeRejoin(out RejoinRequest currentRequest));

        oldContext.CompleteRejoin(staleRequest, new RejoinCompletion(99, 99));
        Assert.True(newContext.IsCurrentRejoin(currentRequest));
        Assert.False(oldContext.Owns(oldPlay));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stale);

        newContext.CancelPendingRejoin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await current);
    }

    private static NodeMatchHandoff Handoff(Guid matchId, ulong nonce)
        => new(matchId, 1, "127.0.0.1", 5000, "ticket", nonce, false, Hunter.Samus);
}
