using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using MphRead.Mods.Chat;
using Xunit.Abstractions;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Replay global state")]
public sealed class ReplaySeekTests
{
    private readonly ITestOutputHelper _output;
    public ReplaySeekTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void ChatCheckpointPrecedesSameFrameAcceptedChatAndMalformedRestoreIsAtomic()
    {
        ChatBox.Clear();
        try
        {
            ChatBox.Add("Before", "checkpoint line", 0);
            byte[] baseline = ChatBox.CaptureReplay();
            byte[] body = new byte[8 + baseline.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 1);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), baseline.Length);
            baseline.CopyTo(body, 8);
            var state = new ModernReplayState();
            Assert.True(state.Receive(ReplayPlaybackTests.Match(1)));
            Assert.True(state.Receive(ReplayPlaybackTests.Record(ReplayRecordKind.ChatState, body)));
            body = new byte[5 + SessionChatPacket.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(body, 1); body[4] = (byte)ReliableEventType.Chat;
            new SessionChatPacket(200, 1, "After", "same frame line").Write(body.AsSpan(5));
            Assert.True(state.Receive(ReplayPlaybackTests.Record(ReplayRecordKind.Event, body)));
            state.ApplyPendingChat();
            var visible = new List<(ChatLine Line, float Alpha)>(); ChatBox.CollectVisible(visible);
            Assert.Equal(new[] { "checkpoint line", "same frame line" }, visible.Select(line => line.Line.Text));
            Assert.False(ChatBox.RestoreReplay(baseline.AsSpan(0, baseline.Length - 1).ToArray()));
            ChatBox.CollectVisible(visible);
            Assert.Equal(2, visible.Count);
        }
        finally { ChatBox.Clear(); }
    }

    [Fact]
    public void KillMarkersNeverInferMultiKillWithoutAuthoritativeAward()
    {
        var actor = new CombatActor(6, ulong.MaxValue - 10, 123);
        var target = new CombatActor(7, ulong.MaxValue - 11, 123);
        var kill = new KillEvent(100, 100, 1, 1, actor, target, 4, KillEventFlags.Headshot,
            System.Collections.Immutable.ImmutableArray<CombatActor>.Empty);
        byte[] bytes = new byte[KillEvent.Size]; kill.Write(bytes);
        Assert.Equal(ReplayMarker.Kill | ReplayMarker.Headshot,
            ReplayRecorder.MarkerFor(new NetApplicationEvent(1, ReliableEventType.Kill, bytes)));

        CombatActor respawned = target with { Life = 124 };
        MatchEvent spawned = new(200, 101, 1, 1, MatchEventKind.PlayerSpawned,
            respawned, CombatActor.None);
        MatchSemanticEventPacket semantic = MatchSemanticEventPacketConversion.FromEvent(spawned);
        byte[] semanticBytes = new byte[MatchSemanticEventPacket.Size];
        semantic.Write(semanticBytes);
        Assert.Equal(ReplayMarker.None, ReplayRecorder.MarkerFor(new NetApplicationEvent(1,
            ReliableEventType.MatchSemantic, semanticBytes)));

        (kill with { Id = 101, Tick = 102, Victim = respawned }).Write(bytes);
        Assert.Equal(ReplayMarker.Kill | ReplayMarker.Headshot,
            ReplayRecorder.MarkerFor(new NetApplicationEvent(1, ReliableEventType.Kill, bytes)));
    }

    [Fact]
    public void SemanticallyInvalidCheckpointFreezesPlaybackWithAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-invalid-{Guid.NewGuid():N}.fpreplay");
        try
        {
            using (var writer = new ReplayWriter(path, indexed: true))
            {
                writer.WriteRecord(0, ReplayPlaybackTests.Match(1));
                writer.WriteRecord(0, ReplayPlaybackTests.Snapshot(1, 1, 0));
                foreach (byte[] world in ReplayPlaybackTests.LiveWorld(1)) writer.WriteRecord(0, world);
                writer.WriteKeyframe(300, new[] { new byte[] { 255 } });
                writer.WriteRecord(3000, ReplayPlaybackTests.Snapshot(1, 2, 7));
            }
            Assert.True(ReplayPlayback.Join(path)); Step();
            Assert.True(ReplayPlayback.Seek(2300));
            Assert.True(ReplayPlayback.ProcessSeek(() => Assert.Fail("Malformed checkpoint must not simulate.")));
            Assert.False(ReplayPlayback.IsSeeking); Assert.True(ReplayPlayback.Transport.Paused);
            Assert.True(ReplayPlayback.AtEnd); Assert.Contains("invalid fact", ReplayPlayback.LastError);
        }
        finally { ReplayPlayback.Stop(); File.Delete(path); }
    }

    [Fact]
    public void MissingCheckpointRejectsBoundedSeekWithoutConsumingSequentialRecords()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-gap-{Guid.NewGuid():N}.fpreplay");
        try
        {
            using (var writer = new ReplayWriter(path, indexed: true))
            {
                writer.WriteRecord(0, ReplayPlaybackTests.Match(1));
                writer.WriteRecord(0, ReplayPlaybackTests.Snapshot(1, 1, 0));
                foreach (byte[] world in ReplayPlaybackTests.LiveWorld(1)) writer.WriteRecord(0, world);
                writer.WriteRecord(5000, ReplayPlaybackTests.Snapshot(1, 2, 7));
            }
            Assert.True(ReplayPlayback.Join(path)); Step();
            Assert.True(ReplayPlayback.Seek(4900));
            Assert.True(ReplayPlayback.ProcessSeek(() => Assert.Fail("No checkpoint means no simulation work.")));
            Assert.False(ReplayPlayback.IsSeeking); Assert.Contains("bounded seek window", ReplayPlayback.LastError);
            Assert.Equal(0u, ReplayPlayback.CurrentFrame);
            for (int i = 0; i < 5000; i++) Step();
            Assert.Equal(7, ReplayPlayback.Modern.Players[0].Points);
            Assert.True(ReplayPlayback.AtEnd);
        }
        finally { ReplayPlayback.Stop(); File.Delete(path); }
    }

    private static void AddCheckpointPresentation(List<byte[]> facts)
    {
        facts.Add(new byte[] {(byte)ReplayRecordKind.Perspective,255});
        byte[] roster = new byte[1 + 4 + SessionRosterPacket.HeaderSize]; roster[0]=(byte)ReplayRecordKind.Roster;
        uint match = BinaryPrimitives.ReadUInt32LittleEndian(facts[0].AsSpan(1));
        BinaryPrimitives.WriteUInt32LittleEndian(roster.AsSpan(1),match);
        SessionRosterPacket.Write(roster.AsSpan(5),1,ReadOnlySpan<NetRosterEntry>.Empty); facts.Add(roster);
        byte[] clock = new byte[33]; clock[0] = (byte)ReplayRecordKind.Clock; facts.Add(clock);
        byte[] state = MphRead.Combat.ReplayFeedbackState.Capture(new(),new());
        const int chunk = 1000;
        int parts = (state.Length + chunk - 1) / chunk;
        for (int part = 0; part < parts; part++)
        {
            int length = Math.Min(chunk,state.Length-part*chunk); byte[] record = new byte[9+length];
            record[0]=(byte)ReplayRecordKind.Presentation;
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1),(ushort)part);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(3),(ushort)parts);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(5),state.Length);
            state.AsSpan(part*chunk,length).CopyTo(record.AsSpan(9)); facts.Add(record);
        }
    }

    [Theory]
    [InlineData("clock")]
    [InlineData("world")]
    [InlineData("feedback")]
    [InlineData("fragment")]
    [InlineData("roster")]
    [InlineData("perspective")]
    public void IncompleteCheckpointStopsBeforeSimulation(string omission)
    {
        string path = Path.Combine(Path.GetTempPath(),$"replay-incomplete-{Guid.NewGuid():N}.fpreplay");
        try
        {
            using(var writer = new ReplayWriter(path,indexed:true))
            {
                writer.WriteRecord(0,ReplayPlaybackTests.Match(1)); writer.WriteRecord(0,ReplayPlaybackTests.Snapshot(1,1,0));
                foreach(byte[] world in ReplayPlaybackTests.LiveWorld(1)) writer.WriteRecord(0,world);
                var facts = new List<byte[]> {ReplayPlaybackTests.Match(1),ReplayPlaybackTests.Snapshot(1,2,300)};
                facts.AddRange(ReplayPlaybackTests.LiveWorld(1)); AddCheckpointPresentation(facts);
                if (omission == "fragment")
                {
                    byte[] fragment = facts.First(f=>(ReplayRecordKind)f[0]==ReplayRecordKind.Presentation);
                    facts.RemoveAll(f=>(ReplayRecordKind)f[0]==ReplayRecordKind.Presentation);
                    BinaryPrimitives.WriteUInt16LittleEndian(fragment.AsSpan(3),2); facts.Add(fragment);
                }
                else facts.RemoveAll(f=>(ReplayRecordKind)f[0] == (omission == "clock" ? ReplayRecordKind.Clock : omission == "world" ? ReplayRecordKind.World : omission == "roster" ? ReplayRecordKind.Roster : omission == "perspective" ? ReplayRecordKind.Perspective : ReplayRecordKind.Presentation));
                writer.WriteKeyframe(300,facts); writer.WriteRecord(3000,ReplayPlaybackTests.Snapshot(1,3,7));
            }
            Assert.True(ReplayPlayback.Join(path)); Step(); Assert.True(ReplayPlayback.Seek(2300));
            Assert.True(ReplayPlayback.ProcessSeek(()=>Assert.Fail("Incomplete checkpoint cannot restore or step.")));
            Assert.False(ReplayPlayback.IsSeeking); Assert.True(ReplayPlayback.Transport.Paused);
            Assert.Contains("incomplete",ReplayPlayback.LastError);
        }
        finally {ReplayPlayback.Stop();File.Delete(path);}
    }

    [Fact]
    public void IndexedSeekRestoresFactsAcrossMatchChangesAndRespectsWarmupAndPause()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-seek-{Guid.NewGuid():N}.fpreplay");
        try
        {
            using (var writer = new ReplayWriter(path, indexed: true))
            {
                for (uint frame = 0; frame <= 4800; frame += 6)
                {
                    uint match = frame < 2400 ? 1u : 2u;
                    var facts = new List<byte[]> { ReplayPlaybackTests.Match(match), ReplayPlaybackTests.Snapshot(match, frame + 1, (int)frame) };
                    facts.AddRange(ReplayPlaybackTests.LiveWorld(match));
                    if (frame % 2400 == 0) foreach (byte[] fact in facts) writer.WriteRecord(frame, fact);
                    else writer.WriteRecord(frame, facts[1]);
                    if (frame % 300 == 0) { AddCheckpointPresentation(facts); writer.WriteKeyframe(frame, facts); }
                }
            }
            Assert.True(ReplayPlayback.Join(path));
            for (uint frame = 0; frame <= 4100; frame++) Step();
            byte[] expected = SnapshotBytes();
            WorldRecord[] world = ReplayPlayback.Modern.World.Records.ToArray();
            Assert.Equal(2u, ReplayPlayback.Modern.Match.MatchId);
            ReplayPlayback.Transport.Paused = true;
            foreach (uint target in new uint[] { 0, 2300, 2500, 4100 })
            {
                ChatBox.Receive(new ChatPacket { Slot=0,Name="Future",Text="Future chat",Kind=ChatPacket.KindSay });
                Assert.True(ChatBox.Visible);
                Assert.True(ReplayPlayback.Seek(target));
                int updates = 0;
                do
                {
                    Assert.True(ReplayPlayback.ProcessSeek(Step));
                    Assert.True(++updates <= 18);
                } while (ReplayPlayback.IsSeeking);
                _output.WriteLine($"Target {target}: restored {ReplayPlayback.LastRestoreFrame}, {ReplayPlayback.LastSeekSteps} fact steps, {ReplayPlayback.LastSeekMilliseconds:F3} ms (no GPU).");
                Assert.False(ChatBox.Visible); // Optional ChatState was absent: old seek state must not survive.
                Assert.Equal(target, ReplayPlayback.CurrentFrame);
                Assert.True(ReplayPlayback.Transport.Paused);
                Assert.InRange(ReplayPlayback.LastSeekSteps, 1, 2102);
                Assert.Null(NetSession.TrafficMetrics);
                Assert.Null(AuthoritativePlay.Current);
                Assert.Equal(-1, NetSession.LocalSlot);
            }
            Assert.Equal(expected, SnapshotBytes());
            Assert.Equal(world, ReplayPlayback.Modern.World.Records.ToArray());
            Assert.Equal(2100u, ReplayPlayback.LastRestoreFrame);
        }
        finally { ReplayPlayback.Stop(); File.Delete(path); }
    }
    private static void Step() { ReplayPlayback.PumpFrame(); ReplayPlayback.Modern.DiscardEvents(); }
    private static byte[] SnapshotBytes()
    {
        var state = ReplayPlayback.Modern;
        byte[] bytes = new byte[SnapshotPacket.HeaderSize + state.PlayerCount * SnapshotPlayer.Size];
        state.Snapshot.Write(bytes, state.Players); return bytes;
    }
}
