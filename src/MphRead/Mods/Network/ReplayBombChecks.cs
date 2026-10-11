using System;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal static class ReplayBombChecks
{
    internal static void Run(Action<bool, string> require)
    {
        var match = new MatchStatePacket { MatchId = 1, AuthorityEpoch = 2,
            RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.Battle };
        byte[] matchPacket = new byte[1 + MatchStatePacket.Size];
        matchPacket[0] = (byte)PacketType.MatchState; match.Write(matchPacket.AsSpan(1));
        var state = new BombState(new ShotKey(2, 1, 0, 1, 1, 1), BombType.MorphBall, 0,
            Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, Vector3.Zero, 80, 0, 0, 0, 0);
        var fact = new BombEvent(1, 0, BombEventKind.Placed, 0, state);
        byte[] eventPacket = new byte[1 + BombEvent.Size];
        eventPacket[0] = (byte)PacketType.BombEvent; fact.Write(eventPacket.AsSpan(1));
        var decoder = new ReplayReplicaState(); decoder.Accept(matchPacket, 0);
        decoder.Accept(eventPacket, 0); decoder.Advance(10);
        require(decoder.PendingBombPacketCount == 1, "bomb event survives decoder Advance until scene applies it");
        foreach (byte[] page in BombStatePage.PackSnapshot(1, 2, 0, 1, new[] { state }))
        {
            byte[] packet = new byte[page.Length + 1]; packet[0] = (byte)PacketType.BombState;
            page.CopyTo(packet, 1); decoder.Accept(packet, 10); Array.Clear(packet);
        }
        var checkpoint = decoder.CaptureCheckpoint();
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(checkpoint);
        require(restored.PendingBombPacketCount == BombStatePage.Pages + 1
            && restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),
            "bomb decoder checkpoint retains detached event and all state pages exactly");
        require(ReplayIdentityCompatibility.Convert(eventPacket, 47).IsEmpty,
            "legacy replay does not activate canonical bombs from future packet types");
        (fact with { State = state with { Key = state.Key with { MatchId = 2 } } }).Write(eventPacket.AsSpan(1));
        restored.Accept(eventPacket, 11);
        require(restored.PendingBombPacketCount == BombStatePage.Pages + 1, "foreign-stream bomb replay facts cannot enter queue");
        restored.Rewind(); require(restored.PendingBombPacketCount == 0, "rewind drops unapplied bomb facts");

        var recorder = new ReplayRecorder(); recorder.AcceptMatch(match, 0);
        recorder.AcceptBombEvent(new(1, 0, BombEventKind.Placed, 0, state), 0);
        var pages = BombStatePage.PackSnapshot(1, 2, 0, 1, new[] { state });
        recorder.AcceptBombState(pages[0], 0);
        int partialEvents = 0;
        recorder.SeedBombFacts(record => { if (record.Payload[0] == (byte)PacketType.BombEvent) partialEvents++; });
        require(partialEvents == 1, "frame-zero partial baseline cannot prune bomb creation history");
        recorder.AcceptBombEvent(new(2, 1, BombEventKind.Detonated, 0, state), 1);
        for (int i = 1; i < pages.Length; i++) recorder.AcceptBombState(pages[i], 1);
        int baselinePages = 0, futureEvents = 0;
        recorder.SeedBombFacts(record =>
        {
            if (record.Payload[0] == (byte)PacketType.BombState) baselinePages++;
            else if (BombEvent.TryRead(record.Payload[1..], out var retained) && retained.Sequence == 2) futureEvents++;
        });
        require(baselinePages == BombStatePage.Pages && futureEvents == 1,
            "complete bomb seed replaces old facts and retains terminal event beyond exact frontier");
        recorder.Reset(); int seeded = 0; recorder.SeedBombFacts(_ => seeded++);
        require(seeded == 0, "recorder reset removes previous stream bomb baseline");
    }
}
