using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ReplicaCheckpointCompatibilityTests
{
    // Source layouts frozen from main16b1d89b and stagedA2a4f4371a:
    // seven-byte header, eight fixed actor rows, then the original tail.
    // Main v10 has nine touch bytes per row and no pickup appendix. Staged
    // A2 v10 has no touch bytes and appends room/counts/facts. The converter
    // preserves the source row's received-frame bytes separately from touch.
    internal static byte[] HistoricalFixture(ReplayReplicaCheckpoint checkpoint,
        bool touch, bool pickups, ushort version = 10, byte protocol = 53)
    {
        byte[] current = checkpoint.Bytes.ToArray();
        using var input = new MemoryStream(current, writable: false);
        using var reader = new BinaryReader(input);
        input.Position = 8 + 5 * sizeof(uint) + 2 * sizeof(long);
        reader.ReadByte();
        if (reader.ReadBoolean()) reader.ReadUInt32();
        if (reader.ReadBoolean()) input.Position += MatchStatePacket.Size;
        if (reader.ReadBoolean()) input.Position += SessionStatePacket.Size;
        input.Position += RosterPacket.Size;
        int rowStart = checked((int)input.Position);
        using var output = new MemoryStream();
        output.Write(current.AsSpan(0, 7)); output.Write(current.AsSpan(8, rowStart - 8));
        int at = rowStart;
        for (int slot = 0; slot < RosterPacket.MaxSlots; slot++)
        {
            int core = 7 + PlayerState.Size + 1 + IntentPacket.FullSize;
            output.Write(current.AsSpan(at, core)); at += core;
            if (touch) output.Write(current.AsSpan(at, NativeTouchReport.WireSize));
            at += NativeTouchReport.WireSize;
            output.Write(current.AsSpan(at, sizeof(uint))); at += sizeof(uint);
        }
        int tail = current.Length - at;
        if (!pickups)
        {
            const int emptyPickupBytes = sizeof(uint) + 3 * sizeof(int);
            if (current.AsSpan(current.Length - emptyPickupBytes).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidOperationException("Historical touch/legacy fixture cannot discard populated pickup state.");
            tail -= emptyPickupBytes;
        }
        output.Write(current.AsSpan(at, tail));
        byte[] result = output.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), version); result[6] = protocol;
        return result;
    }

    internal static void Run(Action<bool, string> check)
    {
        var foreground = GameState.Current;
        var random = Rng.Current;
        var ledger = NetPowerUpLedger.Ledger;
        var state = new ReplayReplicaState();
        var match = new MatchStatePacket { MatchId = 2, AuthorityEpoch = 9,
            RoomKey = "unit-checkpoint-layout", Mode = (byte)GameMode.Battle };
        var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        match.Write(packet.AsSpan(1)); state.Accept(packet, 0);
        var roster = RosterPacket.Create(); roster.MatchId = 2; roster.AuthorityEpoch = 9;
        roster.Revision = 1; roster.Count = 1; roster.Slots[0] = 0; roster.Generations[0] = 4;
        roster.Names[0] = "legacy-source";
        packet = new byte[1 + RosterPacket.Size]; packet[0] = (byte)PacketType.Roster;
        roster.Write(packet.AsSpan(1)); state.Accept(packet, 1);
        var player = new PlayerState { SlotIndex = 0, SlotGeneration = 4, LifeId = 5,
            Health = 77, Flags = PlayerState.FlagSpawned, Position = new Vector3(1, 2, 3), Facing = Vector3.UnitZ };
        packet = new byte[1 + SnapshotHeader.Size + PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        packet[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 2, AuthorityEpoch = 9, Frame = 20, PlayerCount = 1 }.Write(packet.AsSpan(1));
        player.Write(packet.AsSpan(1 + SnapshotHeader.Size, PlayerState.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1 + SnapshotHeader.Size + PlayerState.Size + NetMatchTimeSync.Size), 2);
        state.Accept(packet, 20);
        var touch = new NativeTouchReport(true, true, true, 12, -8, 123, true);
        var intent = new IntentPacket { MatchId = 2, AuthorityEpoch = 9, SlotGeneration = 4, LifeId = 5,
            Frame = 18, Position = new Vector3(4, 5, 6), Aim = Vector3.UnitZ, MoveX = 31, NativeTouch = touch };
        packet = new byte[2 + IntentPacket.TouchCanonicalSize]; packet[0] = (byte)PacketType.SlotIntent;
        intent.Write(packet.AsSpan(2)); state.Accept(packet, 21);
        check(state.TryGetIntent(0, out var original) && original.NativeTouch == touch,
            "fixture owns a nonzero native touch report and admitted full life/source intent");
        byte[] main = HistoricalFixture(state.CaptureCheckpoint(), touch: true, pickups: false);
        check(ReplayReplicaState.InspectCheckpointLayout(new(main)).Features == ReplayReplicaCheckpointFeatures.NativeTouch,
            "validated historical main descriptor identifies the touch appendix independently of protocol53");
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(new(main));
        check(restored.TryGetIntent(0, out var oldMain) && oldMain.NativeTouch == touch
            && oldMain.MoveX == 31 && restored.IntentAge(0) == 0 && restored.PickupItems.Count == 0,
            "actual main v10/protocol53 layout retains touch, movement and independent received-frame with unknown pickup coverage");
        var item = new PickupItemFact(new(9, 2, 7, -1, 8), TimedPowerUp.Cloak, 22, true, new Vector3(7, 8, 9));
        var claim = new PickupClaim(new(9, 2, 0, 4, 5, 6), item.Spawn, item.Type, 18, intent.Position);
        var verdict = new PickupVerdict(claim, PickupResult.Taken, 22, 18, 0, 0, 0);
        packet = new byte[1 + PickupClaim.Size]; packet[0] = (byte)PacketType.PickupClaim; claim.Write(packet.AsSpan(1)); state.Accept(packet, 22);
        packet = new byte[1 + PickupVerdict.Size]; packet[0] = (byte)PacketType.PickupVerdict; verdict.Write(packet.AsSpan(1)); state.Accept(packet, 23);
        packet = new byte[1 + PickupItemFact.Size]; packet[0] = (byte)PacketType.PickupItem; item.Write(packet.AsSpan(1)); state.Accept(packet, 24);
        byte[] staged = HistoricalFixture(state.CaptureCheckpoint(), touch: false, pickups: true);
        check(ReplayReplicaState.InspectCheckpointLayout(new(staged)).Features == ReplayReplicaCheckpointFeatures.Pickups,
            "validated staged A2 descriptor identifies pickup appendix independently of protocol53");
        restored.RestoreCheckpoint(new(staged));
        check(restored.TryGetIntent(0, out var oldA2) && oldA2.NativeTouch == default && oldA2.Position == intent.Position
            && restored.IntentAge(0) == 3 && restored.TryGetPlayer(0, out var actor) && actor.Health == 77
            && restored.PickupItems[item.Spawn] == item && restored.PickupClaims[claim.Key] == claim
            && restored.PickupVerdicts[claim.Key] == verdict,
            "actual staged A2 v10/protocol53 layout restores populated claim/verdict/item tail without invented touch");
        var checkpoint = state.CaptureCheckpoint();
        check(BinaryPrimitives.ReadUInt16LittleEndian(checkpoint.Bytes[4..]) == 12 && checkpoint.Bytes[7] == 3,
            "current v12 explicitly declares native touch and pickup features");
        restored.RestoreCheckpoint(checkpoint);
        check(restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),
            "v12 roundtrip preserves both nonzero touch and complete pickup state");
        byte[] before = restored.CaptureCheckpoint().Bytes.ToArray();
        void Reject(byte[] bad, string label)
        {
            bool rejected = false;
            try { restored.RestoreCheckpoint(new(bad)); } catch (InvalidDataException) { rejected = true; }
            check(rejected && restored.CaptureCheckpoint().Bytes.SequenceEqual(before), label);
        }
        foreach (int length in new[] { 0, 4, 6, 7, 8, staged.Length / 2, staged.Length - 1 })
            Reject(staged.AsSpan(0, length).ToArray(), "truncated historical body is transactional at length " + length);
        Reject(staged.Concat(new byte[] { 0 }).ToArray(), "trailing historical data cannot select a candidate or mutate state");
        var badBoolean = staged.ToArray(); badBoolean[7 + 5 * sizeof(uint) + 2 * sizeof(long)] = 2;
        Reject(badBoolean, "invalid historical boolean cannot disguise the other source layout");
        var badFlags = checkpoint.Bytes.ToArray(); badFlags[7] |= 0x80;
        Reject(badFlags, "unknown explicit feature bits are refused transactionally");
        var a3 = staged.ToArray(); a3[4] = 11; a3[6] = 54;
        Reject(a3, "staged v11 grant schema stays unsupported until A3 installs its decoder");
        var absent = checkpoint.Bytes.ToArray(); absent[7] |= (byte)ReplayReplicaCheckpointFeatures.Grants;
        Reject(absent, "reserved grant feature cannot fabricate A3 coverage in A2");
        bool ambiguous = false;
        try { ReplayReplicaCheckpointLayout.DecodeUnique(main, _ => true); }
        catch (InvalidDataException) { ambiguous = true; }
        check(ambiguous, "two successful historical candidate decoders are rejected rather than chosen by order");
        check(ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(random, Rng.Current)
            && ReferenceEquals(ledger, NetPowerUpLedger.Ledger),
            "historical and current private restoration never mutates foreground or authority grant owners");
    }
}
