using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using OpenTK.Mathematics;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Replay global state")]
public sealed class ReplayTimelineTests
{
    [Fact]
    public void RollingTimelineOwnsPayloadAndMapsSparseServerTickToRecordingFrame()
    {
        var timeline = new RollingReplayTimeline();
        byte[] source = { (byte)ReplayRecordKind.Event, 10, 20, 30 };
        Assert.True(timeline.Append(new ReplayTimelineRecord(17, 900, source)));
        source[1] = 99;

        Assert.True(timeline.TryMapServerTickToRecordingFrame(900, out uint frame));
        Assert.Equal(17u, frame);
        Assert.False(timeline.TryMapServerTickToRecordingFrame(17, out _));
    }

    [Fact]
    public void FreezeFailsExplicitlyUntilCompleteRestoreExists()
    {
        var timeline = new RollingReplayTimeline();
        timeline.Append(new ReplayTimelineRecord(4, 40,
            new byte[] { (byte)ReplayRecordKind.Snapshot, 1 }));
        Assert.False(timeline.TryFreeze(4, 4, out _));

        List<ReplayTimelineRecord> malformed = CompleteRestoreRecords(5, 50);
        malformed.RemoveAll(record => record.Kind == ReplayRecordKind.Presentation);
        Assert.False(ReplayRestorePoint.TryCreate(5, 50, malformed, out _));

        ReplayRestorePoint restore = CompleteRestore(5, 50);
        Assert.True(timeline.AppendRestorePoint(restore));
        Assert.True(timeline.TryFreeze(5, 5, out ReplayTimelineClip? clip));
        Assert.NotNull(clip);
        Assert.Equal(5u, clip!.RestorePoint.RecordingFrame);
    }

    [Fact]
    public void AgeEvictionDropsOnlyWholeRestoreSegments()
    {
        var timeline = new RollingReplayTimeline(targetServerTicks: 100);
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(0, 0)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(1, 10,
            new byte[] { (byte)ReplayRecordKind.Event, 1 })));
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(10, 100)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(11, 110,
            new byte[] { (byte)ReplayRecordKind.Event, 2 })));
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(20, 250)));

        Assert.Equal(2, timeline.RestorePointCount);
        Assert.False(timeline.TryGetRestorePoint(9, out _));
        Assert.True(timeline.TryGetRestorePoint(10, out ReplayRestorePoint? point));
        Assert.Equal(10u, point!.RecordingFrame);
    }

    [Fact]
    public void HardByteCapRejectsOversizeWithoutDestroyingLastCompleteRestore()
    {
        ReplayRestorePoint restore = CompleteRestore(0, 0);
        var timeline = new RollingReplayTimeline(maximumPayloadBytes: restore.PayloadBytes + 8);
        Assert.True(timeline.AppendRestorePoint(restore));
        Assert.False(timeline.Append(new ReplayTimelineRecord(1, 1,
            new byte[20] { (byte)ReplayRecordKind.Event, 1, 2, 3, 4, 5, 6, 7, 8, 9,
                10, 11, 12, 13, 14, 15, 16, 17, 18, 19 })));
        Assert.True(timeline.TryGetRestorePoint(1, out _));
        Assert.InRange(timeline.PayloadBytes, 1, restore.PayloadBytes + 8);
    }

    [Fact]
    public void SegmentPayloadAccountingRemainsExactAfterEviction()
    {
        ReplayRestorePoint first = CompleteRestore(0, 0);
        ReplayRestorePoint second = CompleteRestore(10, 100);
        var timeline = new RollingReplayTimeline(targetServerTicks: 50);
        Assert.True(timeline.AppendRestorePoint(first));
        Assert.True(timeline.Append(new ReplayTimelineRecord(1, 10,
            new byte[] { (byte)ReplayRecordKind.Event, 1, 2, 3 })));
        Assert.True(timeline.AppendRestorePoint(second));
        Assert.True(timeline.Append(new ReplayTimelineRecord(11, 160,
            new byte[] { (byte)ReplayRecordKind.Event, 4, 5 })));

        Assert.Equal(second.PayloadBytes + 3, timeline.PayloadBytes);
        Assert.Equal(second.Records.Count + 1, timeline.Count);
    }

    [Fact]
    public void MatchAwardUsesItsAuthoritativeServerTickRatherThanSourceEventId()
    {
        MatchAward award = new(1, 77, 3, 1, 900, MatchAwardKind.FirstHunt,
            new CombatActor(0, 10, 1), CombatActor.None, 1, 0);
        MatchAwardPacket packet = MatchAwardPacketConversion.FromAward(award);
        byte[] payload = new byte[MatchAwardPacket.Size];
        packet.Write(payload);
        byte[] record = new byte[1 + 5 + payload.Length];
        record[0] = (byte)ReplayRecordKind.Event;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), 3);
        record[5] = (byte)ReliableEventType.MatchAward;
        payload.CopyTo(record.AsSpan(6));

        Assert.True(ReplayTimelineTickReader.TryRead(record, 0, out uint tick));
        Assert.Equal(900u, tick);
    }

    [Fact]
    public void FrozenClipSurvivesTimelineReset()
    {
        var timeline = new RollingReplayTimeline();
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(10, 100)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(12, 105,
            new byte[] { (byte)ReplayRecordKind.Event, 7 })));
        Assert.True(timeline.TryFreeze(10, 12, out ReplayTimelineClip? clip));

        timeline.Reset();
        Assert.Equal(0, timeline.Count);
        Assert.Single(clip!.Records);
        Assert.Equal(105u, clip.Records[0].ServerTick);
    }

    [Fact]
    public void KillcamMapsAuthoritativeKillTickBeforeApplyingFrameWindow()
    {
        var timeline = new RollingReplayTimeline();
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(100, 5_000)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, 9_000,
            new byte[] { (byte)ReplayRecordKind.Event, 1 })));

        Assert.True(KillcamController.TryCaptureClip(timeline, 9_000,
            out ReplayTimelineClip? clip));
        Assert.Equal(100u, clip!.StartRecordingFrame);
        Assert.Equal(400u, clip.EndRecordingFrame);
        Assert.False(KillcamController.TryCaptureClip(timeline, 400, out _));
    }

    [Fact]
    public void KillcamMapsTheExactKillRecordWhenCheckpointSharesItsServerTick()
    {
        var timeline = new RollingReplayTimeline();
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(0, 9_000)));
        KillEvent kill = new(77, 9_000, 1, 2,
            new CombatActor(1, 101, 3), new CombatActor(2, 202, 4), 0,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty);
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, kill.Tick,
            KillRecord(kill))));

        Assert.True(KillcamController.TryCaptureClip(timeline, kill,
            out ReplayTimelineClip? clip));
        Assert.Equal(400u, clip!.EndRecordingFrame);
    }

    [Fact]
    public void FinalKillcamFreezesTheLatestRetainedKillBeforeATimedEnding()
    {
        var timeline = new RollingReplayTimeline();
        KillEvent kill = TestKill(82, 8_750);
        MatchEvent ended = new(9, 9_000, 1, 2,
            MatchEventKind.MatchEnded, CombatActor.None, CombatActor.None);
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(100, 5_000)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, kill.Tick,
            KillRecord(kill))));

        Assert.True(KillcamController.TryCaptureFinalClip(timeline, kill,
            ended, out ReplayTimelineClip? clip));
        Assert.Equal(100u, clip!.StartRecordingFrame);
        Assert.Equal(400u, clip.EndRecordingFrame);
        Assert.False(KillcamController.TryCaptureFinalClip(timeline,
            kill with { Tick = 9_001 }, ended, out _));
    }

    [Fact]
    public void KillcamPendingCaptureUsesExactStartAndCapturesTheFullTailWhenReady()
    {
        var timeline = new RollingReplayTimeline();
        KillEvent kill = TestKill(77, 9_000);
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(100, 5_000)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, kill.Tick,
            KillRecord(kill))));

        Assert.True(KillcamController.TryPrepareCapture(timeline, kill,
            KillcamPolicy.Immediate, out PendingKillcamCapture capture));
        Assert.Equal(400u, capture.KillRecordingFrame);
        Assert.Equal(100u, capture.DesiredStart);
        Assert.Equal(445u, capture.DesiredEnd);
        Assert.Equal(400u, capture.FallbackClip.EndRecordingFrame);
        Assert.Equal(0u, KillcamController.TailFrames(capture.KillRecordingFrame,
            capture.FallbackClip.EndRecordingFrame));

        Assert.True(timeline.Append(new ReplayTimelineRecord(430, 9_030,
            new byte[] { (byte)ReplayRecordKind.Event, 1 })));
        Assert.True(KillcamController.TryFreezeLatestValid(timeline, capture,
            out ReplayTimelineClip? truncated));
        Assert.Equal(430u, truncated!.EndRecordingFrame);
        Assert.Equal(30u, KillcamController.TailFrames(capture.KillRecordingFrame,
            truncated.EndRecordingFrame));

        Assert.True(timeline.Append(new ReplayTimelineRecord(445, 9_045,
            new byte[] { (byte)ReplayRecordKind.Event, 2 })));
        Assert.True(KillcamController.TryFreezeLatestValid(timeline, capture,
            out ReplayTimelineClip? complete));
        Assert.Equal(capture.DesiredStart, complete!.StartRecordingFrame);
        Assert.Equal(capture.DesiredEnd, complete.EndRecordingFrame);
        Assert.Equal(45u, KillcamController.TailFrames(capture.KillRecordingFrame,
            complete.EndRecordingFrame));
    }

    [Fact]
    public void KillcamCaptureWindowSaturatesAtTheMaximumRecordingFrame()
    {
        KillcamCaptureWindow window = KillcamController.GetCaptureWindow(
            uint.MaxValue - 10);
        Assert.Equal(uint.MaxValue - 310, window.Start);
        Assert.Equal(uint.MaxValue, window.End);
    }

    [Fact]
    public void KillcamUsesEarliestRestoreWhenEarlyDesiredStartPrecedesFrameOne()
    {
        var timeline = new RollingReplayTimeline();
        KillEvent kill = TestKill(80, 9_300);
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(1, 5_000)));
        // A later checkpoint must not become the clip's first frame merely
        // because the requested lead boundary (zero) has no predecessor.
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(50, 7_000)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(100, kill.Tick,
            KillRecord(kill))));

        Assert.True(KillcamController.TryPrepareCapture(timeline, kill,
            KillcamPolicy.Immediate, out PendingKillcamCapture capture));
        Assert.Equal(0u, capture.DesiredStart);
        Assert.Equal(1u, capture.ClipStart);
        Assert.Equal(1u, capture.FallbackClip.StartRecordingFrame);
        Assert.Equal(100u, capture.FallbackClip.EndRecordingFrame);
        Assert.Equal(145u, capture.DesiredEnd);
        Assert.True(KillcamController.IsUsableClip(capture.FallbackClip, kill,
            capture.KillRecordingFrame));
    }

    [Fact]
    public void KillcamCaptureRequiresARestorePointForTheLeadWindow()
    {
        var timeline = new RollingReplayTimeline();
        KillEvent kill = TestKill(78, 9_100);
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, kill.Tick,
            KillRecord(kill))));

        Assert.False(KillcamController.TryPrepareCapture(timeline, kill,
            KillcamPolicy.Immediate, out _));
    }

    [Fact]
    public void DeferredKillcamPreparationSucceedsWhenTheExactEventBecomesVisible()
    {
        var timeline = new RollingReplayTimeline();
        KillEvent kill = TestKill(81, 9_400);
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(100, 5_000)));

        Assert.False(KillcamController.TryPrepareCapture(timeline, kill,
            KillcamPolicy.Immediate, out _));
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, kill.Tick,
            KillRecord(kill))));
        Assert.True(KillcamController.TryPrepareCapture(timeline, kill,
            KillcamPolicy.Immediate, out PendingKillcamCapture capture));
        Assert.Equal(400u, capture.KillRecordingFrame);
        Assert.True(KillcamController.IsUsableClip(capture.FallbackClip, kill,
            capture.KillRecordingFrame));
    }

    [Fact]
    public void FrozenKillcamFallbackSurvivesTimelineReset()
    {
        var timeline = new RollingReplayTimeline();
        KillEvent kill = TestKill(79, 9_200);
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(100, 5_000)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(400, kill.Tick,
            KillRecord(kill))));
        Assert.True(KillcamController.TryPrepareCapture(timeline, kill,
            KillcamPolicy.Immediate, out PendingKillcamCapture capture));

        timeline.Reset();
        Assert.True(KillcamController.IsUsableClip(capture.FallbackClip, kill,
            capture.KillRecordingFrame));
        Assert.True(KillcamController.TryFreezeLatestValid(timeline, capture,
            out ReplayTimelineClip? frozen));
        Assert.Equal(capture.FallbackClip.StartRecordingFrame,
            frozen!.StartRecordingFrame);
        Assert.Equal(capture.KillRecordingFrame, frozen.EndRecordingFrame);
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 1)]
    [InlineData(false, true, false, 1)]
    [InlineData(false, false, true, 1)]
    [InlineData(true, true, true, 1)]
    public void KillcamCommandSurfaceIsOneShotAcrossInputDevices(
        bool keyboard, bool gamepad, bool touch, int expected)
    {
        Assert.Equal(expected == 0 ? KillcamCommand.None : KillcamCommand.Skip,
            KillcamController.TranslateCommand(keyboard, gamepad, touch));
    }

    [Fact]
    public void FailedClipSeekBecomesTerminalInsteadOfRetainingPendingRecords()
    {
        var timeline = new RollingReplayTimeline();
        Assert.True(timeline.AppendRestorePoint(CompleteRestore(0, 100)));
        Assert.True(timeline.Append(new ReplayTimelineRecord(1, 101,
            new byte[] { (byte)ReplayRecordKind.Event, 1 })));
        Assert.True(timeline.TryFreeze(0, 1, out ReplayTimelineClip? clip));
        using var session = new ReplayPlaybackSession();
        Assert.True(session.Join(clip!));
        Assert.True(session.Seek(1));

        Assert.True(session.ProcessSeek(() =>
            throw new ProgramException("synthetic replay application failure")));

        Assert.False(session.IsSeeking);
        Assert.True(session.AtEnd);
        Assert.Contains("synthetic replay application failure", session.LastError);
    }

    [Fact]
    public void AcceptedCaptureRunsWhenFileRecordingIsDisabled()
    {
        ReplayRecorder.ResetTimelineForSession();
        Assert.False(ReplayRecorder.IsRecording);
        Assert.Equal(NetHeader.Version, ReplayRecorder.CaptureProtocolVersion);
        Assert.True(ReplayRecorder.CaptureTimelineRecord(41, 700,
            new byte[] { (byte)ReplayRecordKind.Event, 1, 2 }));
        Assert.True(ReplayRecorder.Timeline.TryMapServerTickToRecordingFrame(700, out uint frame));
        Assert.Equal(41u, frame);
        ReplayRecorder.ResetTimelineForSession();
    }

    [Fact]
    public void AcceptedEventTicksAreDecodedFromTheirOwnWireLayouts()
    {
        CombatActor actor = new(0, 10, 1);
        CombatActor victim = new(1, 11, 1);
        var combat = new CombatEvent(1, 701, 0, CombatEventKind.Damage, 0,
            CombatEventFlags.None, actor, victim, 90, 10, Vector3.Zero, Vector3.UnitZ, 0, 0, 0);
        byte[] combatBody = new byte[CombatEventBatch.MaxSize];
        int combatLength = CombatEventBatch.Write(combatBody, new[] { combat });
        AssertTick(ReliableEventType.Combat, combatBody.AsSpan(0, combatLength), 701);

        var kill = new KillEvent(2, 702, 1, 1, actor, victim, 0,
            KillEventFlags.None, ImmutableArray<CombatActor>.Empty);
        byte[] payload = new byte[KillEvent.Size]; kill.Write(payload);
        AssertTick(ReliableEventType.Kill, payload, 702);

        var world = new WorldEvent(3, 703, 1, 1, WorldSubjectKind.Node,
            WorldSignalKind.NodeCaptured, 0, 10, actor, Vector3.Zero);
        payload = new byte[WorldEvent.Size]; world.Write(payload);
        AssertTick(ReliableEventType.WorldEvent, payload, 703);

        var award = new MatchAward(4, 2, 1, 1, 704, MatchAwardKind.FirstHunt,
            actor, CombatActor.None, 1, 1);
        MatchAwardPacket awardPacket = MatchAwardPacketConversion.FromAward(award);
        payload = new byte[MatchAwardPacket.Size]; awardPacket.Write(payload);
        AssertTick(ReliableEventType.MatchAward, payload, 704);

        var semantic = new MatchEvent(5, 705, 1, 1, MatchEventKind.PlayerSpawned,
            actor, CombatActor.None);
        MatchSemanticEventPacket semanticPacket = MatchSemanticEventPacketConversion.FromEvent(semantic);
        payload = new byte[MatchSemanticEventPacket.Size]; semanticPacket.Write(payload);
        AssertTick(ReliableEventType.MatchSemantic, payload, 705);
    }

    [Fact]
    public void FileTimelineRejectsIncompleteRestoreAndLoadsCompleteRestore()
    {
        string valid = Path.Combine(Path.GetTempPath(), $"timeline-{Guid.NewGuid():N}.fpreplay");
        string invalid = Path.Combine(Path.GetTempPath(), $"timeline-bad-{Guid.NewGuid():N}.fpreplay");
        try
        {
            ReplayRestorePoint point = CompleteRestore(0, 123);
            var bytes = new List<byte[]>();
            foreach (ReplayTimelineRecord record in point.Records) bytes.Add(record.Data.ToArray());
            using (var writer = new ReplayWriter(valid, indexed: true))
            {
                writer.WriteKeyframe(0, bytes);
                writer.WriteRecord(3, ReplayPlaybackTests.Snapshot(1, 3, 130));
            }
            using (var writer = new ReplayWriter(invalid, indexed: true))
            {
                writer.WriteKeyframe(0, new[] { ReplayPlaybackTests.Match(1) });
                writer.WriteRecord(1, ReplayPlaybackTests.Match(1));
            }

            ReplayFileTimeline? timeline = ReplayFileTimeline.Open(valid);
            Assert.NotNull(timeline);
            Assert.True(timeline!.TryFreeze(0, 3, out ReplayTimelineClip? clip));
            Assert.NotNull(clip);
            Assert.Null(ReplayFileTimeline.Open(invalid));
        }
        finally
        {
            File.Delete(valid);
            File.Delete(invalid);
        }
    }

    [Fact]
    public void QuickCaptureWritesAStandaloneIndexedReplayFromTheRollingWindow()
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"quick-replay-{Guid.NewGuid():N}.fpreplay");
        try
        {
            var timeline = new RollingReplayTimeline();
            Assert.True(timeline.AppendRestorePoint(CompleteRestore(100, 5_000)));
            Assert.True(timeline.Append(new ReplayTimelineRecord(800, 5_700,
                ReplayPlaybackTests.Snapshot(1, 2, 1), ReplayMarker.Kill)));

            Assert.True(ReplayRecorder.TryWriteRecentClip(timeline, path,
                frames: 600, out string? error), error);

            using ReplayReader reader = ReplayReader.Open(path)!;
            Assert.Equal(ReplayFile.IndexedFormatVersion, reader.FormatVersion);
            Assert.Equal(NetHeader.Version, reader.ProtocolVersion);
            Assert.Contains(reader.Index, entry => entry.Keyframe && entry.Frame == 0);
            Assert.Contains(reader.Index, entry => entry.Marker == ReplayMarker.Kill
                && entry.Frame == 700);
            Assert.Equal(700u, reader.LastFrame);
            ReplayFileTimeline? fileTimeline = ReplayFileTimeline.Open(path);
            Assert.NotNull(fileTimeline);
            Assert.True(fileTimeline!.TryFreeze(0, 700,
                out ReplayTimelineClip? clip));
            Assert.NotNull(clip);
            using var playback = new ReplayPlaybackSession();
            Assert.True(playback.Join(path), playback.LastError);
            Assert.True(playback.IsModern);
            Assert.NotNull(playback.InitialRules);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void QuickCaptureFallsForwardToTheFirstAvailableCheckpointEarlyInMatch()
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"quick-replay-early-{Guid.NewGuid():N}.fpreplay");
        try
        {
            var timeline = new RollingReplayTimeline();
            Assert.True(timeline.AppendRestorePoint(CompleteRestore(300, 5_000)));
            Assert.True(timeline.Append(new ReplayTimelineRecord(400, 5_100,
                ReplayPlaybackTests.Snapshot(1, 2, 1))));

            Assert.True(ReplayRecorder.TryWriteRecentClip(timeline, path,
                frames: 600, out string? error), error);
            using ReplayReader reader = ReplayReader.Open(path)!;
            Assert.Equal(100u, reader.LastFrame);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ReplayRestorePoint CompleteRestore(uint frame, uint tick)
    {
        Assert.True(ReplayRestorePoint.TryCreate(frame, tick,
            CompleteRestoreRecords(frame, tick), out ReplayRestorePoint? restore));
        return restore!;
    }

    private static KillEvent TestKill(uint id, uint tick)
        => new(id, tick, 1, 2, new CombatActor(1, 101, 3),
            new CombatActor(2, 202, 4), 0, KillEventFlags.None,
            ImmutableArray<CombatActor>.Empty);

    private static void AssertTick(ReliableEventType type, ReadOnlySpan<byte> payload,
        uint expected)
    {
        byte[] record = new byte[6 + payload.Length];
        record[0] = (byte)ReplayRecordKind.Event;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), 1);
        record[5] = (byte)type;
        payload.CopyTo(record.AsSpan(6));
        Assert.True(ReplayTimelineTickReader.TryRead(record, 0, out uint actual));
        Assert.Equal(expected, actual);
    }

    private static List<ReplayTimelineRecord> CompleteRestoreRecords(uint frame, uint tick)
    {
        byte[] match = ReplayPlaybackTests.Match(1);
        // Keep the authoritative tick deliberately independent from the recording frame.
        BinaryPrimitives.WriteUInt32LittleEndian(match.AsSpan(5), tick);
        byte[] snapshot = ReplayPlaybackTests.Snapshot(1, 1, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(1), tick);
        var records = new List<byte[]>
        {
            match,
            snapshot
        };
        foreach (byte[] world in ReplayPlaybackTests.LiveWorld(1))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(world.AsSpan(9), tick);
            records.Add(world);
        }
        byte[] roster = new byte[1 + 4 + SessionRosterPacket.HeaderSize];
        roster[0] = (byte)ReplayRecordKind.Roster;
        BinaryPrimitives.WriteUInt32LittleEndian(roster.AsSpan(1), 1);
        SessionRosterPacket.Write(roster.AsSpan(5), 1, ReadOnlySpan<NetRosterEntry>.Empty);
        records.Add(roster);
        records.Add(Presentation());
        records.Add(Clock());
        records.Add(new byte[] { (byte)ReplayRecordKind.Perspective, 255 });
        var result = new List<ReplayTimelineRecord>(records.Count);
        foreach (byte[] record in records) result.Add(new ReplayTimelineRecord(frame, tick, record));
        return result;
    }

    private static byte[] Presentation()
    {
        byte[] record = new byte[10];
        record[0] = (byte)ReplayRecordKind.Presentation;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(3), 1);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(5), 1);
        record[9] = 42;
        return record;
    }

    private static byte[] Clock()
    {
        byte[] record = new byte[33];
        record[0] = (byte)ReplayRecordKind.Clock;
        return record;
    }

    private static byte[] KillRecord(in KillEvent kill)
    {
        byte[] record = new byte[6 + KillEvent.Size];
        record[0] = (byte)ReplayRecordKind.Event;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), kill.MatchId);
        record[5] = (byte)ReliableEventType.Kill;
        kill.Write(record.AsSpan(6));
        return record;
    }
}
