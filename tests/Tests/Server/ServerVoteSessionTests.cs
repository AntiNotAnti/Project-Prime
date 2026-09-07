using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class ServerVoteSessionTests
{
    private static ServerVoteSession Begin(uint seed = 42, MatchPhase phase = MatchPhase.Intermission, uint deadline = 100)
    {
        var session = new ServerVoteSession(seed);
        session.Begin(7, 9, phase, deadline, VotePolicy.PrivateRematch, null, new RotationEntry().ToMatchRules());
        session.SetEligible(new ulong[] { 11, 22, 0, 0, 0, 0, 0, 0 });
        return session;
    }
    private static IntermissionVoteRequest Vote(ServerVoteSession s, byte id) => new(s.MatchId, s.PhaseRevision, s.Revision, id);

    [Fact]
    public void ConcurrentVotesUseStableOptionsAndMonotonicPublications()
    {
        var s = Begin(); var before = s.Snapshot(0); var request = Vote(s, 1);
        Assert.True(s.Cast(0, 11, request, 1));
        var first = s.Snapshot(0);
        Assert.True(s.Cast(1, 22, request, 2));
        Assert.Equal(before.Revision, s.Revision);
        Assert.True(Sequence32.IsNewer(first.UpdateRevision, before.UpdateRevision));
        Assert.True(Sequence32.IsNewer(s.UpdateRevision, first.UpdateRevision));
        Assert.Equal(2, s.Snapshot(0).Options[0].Votes);
        Assert.True(s.Cast(0, 11, request, 3));
        Assert.False(s.Cast(0, 11, Vote(s, 2), 3));
    }

    [Fact]
    public void AdmissionFencesIdentityPhaseOptionsAndDeadline()
    {
        var s = Begin(); var v = Vote(s, 1);
        Assert.False(s.Cast(0, 22, v, 1));
        Assert.False(s.Cast(8, 11, v, 1));
        Assert.False(s.Cast(0, 11, v with { MatchId = 8 }, 1));
        Assert.False(s.Cast(0, 11, v with { PhaseRevision = 10 }, 1));
        Assert.False(s.Cast(0, 11, v with { BallotRevision = 2 }, 1));
        Assert.False(s.Cast(0, 11, v with { OptionId = 8 }, 1));
        Assert.False(s.Cast(0, 11, v, 100));
        Assert.True(s.Cast(0, 11, v, 99));
    }

    [Fact]
    public void DisconnectClearsVoteAndResolvedResultCannotBeRewritten()
    {
        var s = Begin(); Assert.True(s.Cast(0, 11, Vote(s, 1), 1));
        s.SetEligible(new ulong[] { 33, 22, 0, 0, 0, 0, 0, 0 });
        Assert.Equal(0, s.Snapshot(0).SelectedId);
        Assert.False(s.Cast(0, 11, Vote(s, 1), 2));
        Assert.True(s.Cast(0, 33, Vote(s, 3), 2));
        var result = s.Resolve();
        s.SetEligible(new ulong[8]);
        Assert.Equal(result, s.Resolve());
        Assert.Equal(IntermissionChoice.Lobby, result.Kind);
        Assert.Equal(0, s.Snapshot(0, 44).SelectedId);
        Assert.Equal(3, s.Snapshot(0, 33).SelectedId);
        Assert.False(s.Cast(1, 22, Vote(s, 2), 3));
    }

    [Fact]
    public void LobbyHasNoTimeoutUntilFirstVoteAndHandlesTickWrap()
    {
        var s = Begin(phase: MatchPhase.WaitingForPlayers);
        Assert.False(s.HasDeadline); Assert.False(s.DeadlineReached(uint.MaxValue));
        Assert.Equal(2, s.Snapshot(0).Options.Length);
        uint tick = uint.MaxValue - 20;
        Assert.True(s.Cast(0, 11, Vote(s, 1), tick));
        Assert.Equal(unchecked(tick + MatchLifecycle.IntermissionTicks), s.DeadlineTick);
        Assert.False(s.DeadlineReached(unchecked(s.DeadlineTick - 1)));
        Assert.True(s.DeadlineReached(s.DeadlineTick));
        Assert.True(s.DeadlineReached(unchecked(s.DeadlineTick + 1)));
    }

    [Fact]
    public void OnlyTiesConsumeDeterministicRng()
    {
        var empty = Begin(); Assert.Equal(IntermissionChoice.NextMap, empty.Resolve().Kind); Assert.Equal(42u, empty.RandomState);
        var winner = Begin(); winner.Cast(0, 11, Vote(winner, 1), 1);
        Assert.Equal(IntermissionChoice.Rematch, winner.Resolve().Kind); Assert.Equal(42u, winner.RandomState);
        var a = Begin(); var b = Begin();
        foreach (var s in new[] { a, b }) { s.Cast(0, 11, Vote(s, 1), 1); s.Cast(1, 22, Vote(s, 2), 1); }
        Assert.Equal(a.Resolve(), b.Resolve()); Assert.Equal(a.RandomState, b.RandomState); Assert.NotEqual(42u, a.RandomState);
    }

    [Fact]
    public void DuplicateHumanSlotsAreRejectedBeforeMutation()
    {
        var s = Begin(); var prior = s.Snapshot(0);
        Assert.Throws<ArgumentException>(() => s.SetEligible(new ulong[] { 11, 11, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal(prior.UpdateRevision, s.UpdateRevision);
    }

    [Fact]
    public void BallotCodecRejectsEveryTruncationAndReservedByte()
    {
        var s = Begin(); s.Cast(0, 11, Vote(s, 1), 1);
        var bytes = new byte[IntermissionBallot.MaximumSize]; int length = s.Snapshot(0).Write(bytes);
        Assert.True(IntermissionBallot.TryRead(bytes.AsSpan(0, length), out var decoded));
        Assert.Equal(s.UpdateRevision, decoded!.UpdateRevision); Assert.Equal(1, decoded.SelectedId);
        for (int n = 0; n < length; n++) Assert.False(IntermissionBallot.TryRead(bytes.AsSpan(0, n), out _));
        foreach (int offset in new[] { 21, 22, 23, 31 })
        { bytes[offset] = 1; Assert.False(IntermissionBallot.TryRead(bytes.AsSpan(0, length), out _)); bytes[offset] = 0; }
        bytes[19] = 0; Assert.False(IntermissionBallot.TryRead(bytes.AsSpan(0, length), out _));
    }

    [Fact]
    public void VoteCodecHasExactBoundedShape()
    {
        var vote = new IntermissionVoteRequest(7, 9, 1, 3); var bytes = new byte[16]; vote.Write(bytes);
        Assert.True(IntermissionVoteRequest.TryRead(bytes, out var read)); Assert.Equal(vote, read);
        for (int n = 0; n < 16; n++) Assert.False(IntermissionVoteRequest.TryRead(bytes.AsSpan(0, n), out _));
        bytes[15] = 1; Assert.False(IntermissionVoteRequest.TryRead(bytes, out _));
    }
    [Fact]
    public void PublicBallotOffersOnlyBoundedServerRotationEntries()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, Enumerable.Range(0, 10).Select(i => $"MAP{i}|Battle|7|7"));
            MapRotation rotation = MapRotation.Load(path);
            var s = new ServerVoteSession(42);
            s.Begin(1, 1, MatchPhase.Intermission, 300, VotePolicy.PublicRotation, rotation, rotation.Current.ToMatchRules());
            var ballot = s.Snapshot(0);
            Assert.Equal(8, ballot.Options.Length);
            Assert.DoesNotContain(ballot.Options, option => option.Kind == IntermissionChoice.Lobby || option.Label == "MAP0");
            Assert.All(ballot.Options.Skip(2), option => Assert.Equal(IntermissionChoice.Map, option.Kind));
            s.SetEligible(new ulong[] { 11, 0, 0, 0, 0, 0, 0, 0 });
            Assert.True(s.Cast(0, 11, Vote(s, 8), 1));
            VoteResolution result = s.Resolve();
            Assert.Equal("MAP6", rotation.Select(result.RotationIndex).RoomKey);
            Assert.Throws<ArgumentOutOfRangeException>(() => rotation.Select(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rotation.Select(10));
        }
        finally { File.Delete(path); }
    }
}
