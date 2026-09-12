using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests.Client;

/// <summary>
/// Content-free acceptance coverage for the canonical online owner. Each
/// round reuses one runtime so a seamless rematch cannot accidentally depend
/// on process teardown to clear an old match or rejoin request.
/// </summary>
[Collection("Match baseline globals")]
public sealed class SeamlessLifecycleStressTests
{
    [Fact]
    public async Task TwentySeamlessRoundsFenceOwnerIdentityAndStaleRejoins()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);

        for (int round = 0; round < 20; round++)
        {
            Guid oldMatchId = Id(round, 1);
            using var oldPlay = new AuthoritativePlay(
                "127.0.0.1", 5000, $"old-{round}", Hunter.Samus);
            MatchClientContext oldContext = runtime.AdoptMatch(
                oldPlay, oldMatchId)!;

            Assert.Same(oldContext, runtime.Match);
            Assert.Same(oldPlay, oldContext.Play);
            Assert.True(oldContext.Owns(oldPlay));
            Assert.Same(oldContext, runtime.AdoptMatch(oldPlay, oldMatchId));

            Task<RejoinCompletion> stale = oldContext.QueueRejoinAsync(
                Handoff(round, 2), CancellationToken.None);
            Assert.True(oldContext.TryTakeRejoin(out RejoinRequest staleRequest));
            Assert.True(oldContext.IsCurrentRejoin(staleRequest));

            runtime.ReleaseMatch(expected: oldPlay, dispose: true);

            Assert.Null(runtime.Match);
            Assert.False(oldContext.Owns(oldPlay));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await stale);

            Guid currentMatchId = Id(round, 3);
            using var currentPlay = new AuthoritativePlay(
                "127.0.0.1", 5000, $"current-{round}", Hunter.Samus);
            MatchClientContext currentContext = runtime.AdoptMatch(
                currentPlay, currentMatchId)!;

            // A release request carrying the old owner cannot release the
            // replacement context, and a late old completion cannot settle
            // the replacement request.
            runtime.ReleaseMatch(expected: oldPlay, dispose: true);
            Assert.Same(currentContext, runtime.Match);
            oldContext.CompleteRejoin(staleRequest,
                new RejoinCompletion((ulong)(round + 1), (uint)(round + 1)));

            Task<RejoinCompletion> current = currentContext.QueueRejoinAsync(
                Handoff(round, 4), CancellationToken.None);
            Assert.True(currentContext.TryTakeRejoin(
                out RejoinRequest currentRequest));
            Assert.True(currentContext.IsCurrentRejoin(currentRequest));

            RejoinCompletion expected = new((ulong)(round + 101),
                (uint)(round + 101));
            currentContext.CompleteRejoin(currentRequest, expected);
            Assert.Equal(expected, await current);

            runtime.ReleaseMatch(expected: currentPlay, dispose: true);
            Assert.Null(runtime.Match);
        }
    }

    private static NodeMatchHandoff Handoff(int round, int slot)
        => new(Id(round, slot), 1, "127.0.0.1", 5000,
            $"ticket-{round}", (ulong)(round + 1), false, Hunter.Samus);

    private static Guid Id(int round, int slot)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, round + 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], slot);
        return new Guid(bytes);
    }
}
