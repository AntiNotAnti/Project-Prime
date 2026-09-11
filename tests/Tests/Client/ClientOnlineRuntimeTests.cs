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
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void OnlineRuntimeRollbackFlagIsExplicit(string? value, bool expected)
        => Assert.Equal(expected, ClientOnlineRuntime.ParseEnabled(value));

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
        MatchClientContext context = runtime.AdoptMatch(play, Guid.NewGuid())!;
        NodeMatchHandoff firstHandoff = Handoff(1);
        NodeMatchHandoff newestHandoff = Handoff(2);

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
        MatchClientContext context = runtime.AdoptMatch(play, Guid.NewGuid())!;
        Task<RejoinCompletion> pending = context.QueueRejoinAsync(Handoff(3), CancellationToken.None);

        runtime.ReleaseMatch(play, dispose: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.False(context.Owns(play));
        Assert.False(context.TryTakeRejoin(out _));
    }

    [Fact]
    public async Task RejoinQueueDoesNotBlockOwnerAndSettlesCompletionExactlyOnce()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        using var play = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
        MatchClientContext context = runtime.AdoptMatch(play, Guid.NewGuid())!;

        Task<RejoinCompletion> pending = context.QueueRejoinAsync(Handoff(4), CancellationToken.None);
        Assert.False(pending.IsCompleted);
        Assert.True(context.TryTakeRejoin(out RejoinRequest request));

        RejoinCompletion expected = new(91, 7);
        context.CompleteRejoin(request, expected);
        context.CompleteRejoin(request, new RejoinCompletion(92, 8));
        context.FailRejoin(request, new InvalidOperationException("late failure"));
        Assert.Equal(expected, await pending.WaitAsync(TimeSpan.FromSeconds(1)));

        Task<RejoinCompletion> cancelled = context.QueueRejoinAsync(Handoff(5), CancellationToken.None);
        context.CancelPendingRejoin();
        context.CancelPendingRejoin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
    }

    private static NodeMatchHandoff Handoff(ulong nonce)
        => new(Guid.NewGuid(), 1, "127.0.0.1", 5000, "ticket", nonce, false, Hunter.Samus);
}
