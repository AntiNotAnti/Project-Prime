using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class RichWorldResultTests
{
    private sealed class Replica : ISceneServices { public bool IsReplica => true; }
    [Fact]
    public void TerminalWorldUsesImmutableResultAndRequiresEveryBatchBeforeReplicaCapture()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0); state.Activate(1, 1);
        match.MatchId = 4; match.Phase = MatchPhase.Ending; match.PhaseRevision = 3;
        state.Scene.Roster.Nicknames[0] = "Original"; state.Scene.Roster.Nicknames[1] = "Second";
        match.ResultSlots[1] = 1;
        match.Players[0].Assists = 3; match.Players[0].DamageDealt = 123;
        match.Players[0].LongestKillStreak = 4; match.Players[0].SetBeamKills(4, 5);
        match.Players[0].OctolithStops = 2;
        match.CaptureResult(17);
        match.Players[0].Assists = 999;
        state.Scene.Roster.Nicknames[0] = "Replacement";
        state.Players[0].LoadFlags = 0;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Hunter))!.SetValue(state.Players[0], Hunter.Kanden);
        var capture = new WorldStateCapture();
        capture.Capture(state.Scene, 4, 1, 1000);
        Assert.Equal(58, capture.Count);
        Assert.Equal("Original", capture.Records[22].PlayerName);
        Assert.Equal(1u, capture.Records[22].C);
        byte[][] batches = Enumerable.Range(0, capture.BatchCount).Select(i =>
        {
            var bytes = new byte[WorldPacket.MaxSize];
            int length = capture.WriteBatch(bytes, i); return bytes[..length];
        }).ToArray();
        var receiver = new ClientWorldState(); receiver.Reset(4);
        Assert.False(receiver.Receive(batches[2]));
        Assert.False(receiver.Receive(batches[0]));
        Assert.False(receiver.HasState);
        Assert.True(receiver.Receive(batches[1]));
        WorldRecord[] invalid = capture.Records.ToArray();
        invalid[27] = invalid[27] with { D = invalid[27].D & 0xFFFF }; // duplicate active result slot
        for (int offset = 0; offset < invalid.Length; offset += WorldPacket.RecordsPerBatch)
        {
            byte[] body = new byte[WorldPacket.MaxSize];
            int length = WorldPacket.Write(body, 4, 2, 1001, invalid, offset);
            Assert.False(receiver.Receive(body.AsSpan(0, length)));
        }
        Assert.Equal(1u, receiver.Revision);
        invalid[27] = capture.Records[27];
        invalid[22] = invalid[22] with { PlayerName = "" };
        for (int offset = 0; offset < invalid.Length; offset += WorldPacket.RecordsPerBatch)
        {
            byte[] body = new byte[WorldPacket.MaxSize];
            int length = WorldPacket.Write(body, 4, 3, 1002, invalid, offset);
            Assert.False(receiver.Receive(body.AsSpan(0, length)));
        }
        Assert.Equal(1u, receiver.Revision);
        state.Scene.Services = new Replica(); match.ResetResult();
        receiver.Apply(state.Scene, playerSnapshotTick: 2000);
        Assert.Equal(3, match.Result!.Players[0].Assists);
        Assert.Equal(123, match.Result.Players[0].DamageDealt);
        Assert.Equal(4, match.Result.Players[0].LongestKillStreak);
        Assert.Equal(5, match.Result.Players[0].BeamKills[4]);
        Assert.Equal(2, match.Result.Players[0].OctolithStops);
        Assert.Equal("Original", match.Result.Players[0].Nickname);
        Assert.True(match.Result.Players[0].Active);
        Assert.Equal(Hunter.Samus, match.Result.Players[0].Hunter);
        match.Players[0].Assists = 700;
        Assert.Equal(3, match.Result.Players[0].Assists);
    }
    [Fact]
    public void FrozenProtocol7WorldRemainsEighteenRecordsAndLiveRequiresRichPrefix()
    {
        var records = new WorldRecord[18];
        records[0] = new(WorldRecordKind.Match, 255, 0, 0, default, (uint)GameMode.Battle, (uint)MatchPhase.Playing, 7, uint.MaxValue, 0);
        for (byte slot = 0; slot < 8; slot++)
        {
            records[1 + slot * 2] = new(WorldRecordKind.Score, slot, 0, 0, default, 0, 0, 0, 0, 0);
            records[2 + slot * 2] = new(WorldRecordKind.Time, slot, 0, 0, default, 0, 0, 0, 0, 0);
        }
        records[17] = new(WorldRecordKind.Lifecycle, 255, 0, 0, default, 0, 0, 1, 0, 0);
        byte[] bytes = new byte[WorldPacket.MaxSize];
        int length = WorldPacket.Write(bytes, 1, 1, 100, records, 0);
        var historical = new ClientWorldState { Protocol7Replay = true }; historical.Reset(1);
        Assert.True(historical.Receive(bytes.AsSpan(0, length)));
        Assert.Equal(18, historical.Count);
        var live = new ClientWorldState(); live.Reset(1);
        Assert.False(live.Receive(bytes.AsSpan(0, length)));
        Assert.False(live.HasState);
    }
    [Fact]
    public void PlayerIdentityHasTypedCanonicalNameAndRejectsMalformedMetadata()
    {
        var record = new WorldRecord(WorldRecordKind.PlayerIdentity, 2, 0, 0, default, 1, 2, 1, 0x030201, 0)
            { PlayerName = "Named player" };
        byte[] bytes = new byte[WorldRecord.Size]; record.Write(bytes);
        Assert.True(WorldRecord.TryRead(bytes, out var parsed));
        Assert.Equal(record.PlayerName, parsed.PlayerName);
        Assert.Equal(default, parsed.Position);
        bytes[4] = 255; Assert.False(WorldRecord.TryRead(bytes, out _));
        record.Write(bytes); bytes[24] = 8; Assert.False(WorldRecord.TryRead(bytes, out _));
        record.Write(bytes); bytes[32] = 8; Assert.False(WorldRecord.TryRead(bytes, out _));
    }
    [Fact]
    public void OvertimeAndLateJoinPoliciesRoundTripAndRejectUnknownValues()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "ROOM").With(overtimePolicy: OvertimePolicy.ModeDefault,
            lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch, pickupRespawnAnnouncements: true);
        byte[] bytes = new byte[MatchRulesWire.Size]; MatchRulesWire.Write(bytes, rules);
        Assert.True(MatchRulesWire.TryRead(bytes, out var read));
        Assert.True(read.PickupRespawnAnnouncements);
        Assert.Equal(rules.OvertimePolicy, read.OvertimePolicy); Assert.Equal(rules.LateJoinPolicy, read.LateJoinPolicy);
        bytes[69] = 2; Assert.False(MatchRulesWire.TryRead(bytes, out _));
        bytes[69] = 1; bytes[70] = 3; Assert.False(MatchRulesWire.TryRead(bytes, out _));
    }
}
