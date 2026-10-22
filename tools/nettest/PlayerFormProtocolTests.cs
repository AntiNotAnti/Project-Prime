using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.NetTest;
internal static class PlayerFormProtocolTests
{
    private static int _checks;
    private static void Check(bool ok, string name)
    { if (!ok) throw new InvalidOperationException(name); _checks++; Console.WriteLine($"FORM PROTOCOL PASS {name}"); }
    private static PlayerFormState Form(uint frame = 20) => new() { Frame = frame, StartedFrame = 18,
        Phase = FormPhase.Alt, TurretPosition = new(1, 2, 3), TurretActive = true, TurretHealth = 37, DamageEventId = 41, BodyHealth = 80 };
    private static byte[] Canonical(uint frame, ushort generation = 2, ushort life = 3, PlayerFormState? form = null)
    {
        var b = new byte[SnapshotHeader.Size + PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = frame, PlayerCount = 1 }.Write(b);
        new PlayerState { SlotIndex = 0, SlotGeneration = generation, LifeId = life, Health = 80, Facing = Vector3.UnitZ,
            Form = form ?? Form(frame) }.Write(b.AsSpan(SnapshotHeader.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(b.Length - NetHealthSync.HeaderSize), 7); return b;
    }
    public static int Run()
    {
        _checks = 0;
        foreach (FormPhase phase in Enum.GetValues<FormPhase>())
        {
            var state = Form(); state.Phase = phase; var b = new byte[PlayerFormState.Size]; state.Write(b);
            Check(PlayerFormState.TryRead(b, out var read) && read.Frame == state.Frame && read.Phase == phase
                && read.TurretPosition == state.TurretPosition && read.DamageEventId == 41 && read.BodyHealth == 80, $"roundtrip {phase}");
            Check(!PlayerFormState.TryRead(b.AsSpan(1), out _), "truncated form rejected");
        }
        var bad = new byte[PlayerFormState.Size]; Form().Write(bad); bad[8] = 255; Check(!PlayerFormState.TryRead(bad, out _), "unknown phase rejected");
        Form().Write(bad); bad[21] = 2; Check(!PlayerFormState.TryRead(bad, out _), "noncanonical boolean rejected");
        Form().Write(bad); BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(9), float.NaN); Check(!PlayerFormState.TryRead(bad, out _), "NaN anchor rejected");
        Form().Write(bad); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(4), 21); Check(!PlayerFormState.TryRead(bad, out _), "future transition start rejected");
        var wrap = Form(0); wrap.StartedFrame = uint.MaxValue; wrap.Write(bad); Check(PlayerFormState.TryRead(bad, out _), "transition order supports wrap");
        var lanes = new NetReplicationLanes(); lanes.Prepare(Canonical(20));
        Check(PlayerFormState.Size == 28 && NetReplicationLanes.Protocol80SlowEntry == 58
            && NetReplicationLanes.SlowEntry == 77 && SnapshotFast.PlayerSize == PlayerState.Protocol48Size - 7
            && SnapshotFast.MaximumEncodedSize <= 1200, "canonical stride and unchanged fast MTU");
        var receiver = new NetReplicationReceiver(); var decoded = new PlayerState[1];
        Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 7, 8)
            && receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 7, 8, out _) && decoded[0].Form.Phase == FormPhase.Alt, "matching form decoded");
        var future = new NetReplicationLanes(); future.Prepare(Canonical(20));
        var morph = Form(21); morph.Phase = FormPhase.Morphing; morph.StartedFrame = 21;
        future.Prepare(Canonical(21, form: morph));
        Check(receiver.Receive(PacketType.PlayerSlowState, future.Slow.AsSpan(0, future.SlowLength), 7, 8)
            && receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 7, 8, out _) && decoded[0].Form.Phase == FormPhase.Unknown, "future morph slow form deferred");
        Check(receiver.TryDecodeLive(future.Fast.AsSpan(0, future.FastLength), decoded, 7, 8, out _) && decoded[0].Form.Frame == 21 && decoded[0].Form.Phase == FormPhase.Morphing, "cached future morph becomes eligible");
        byte[] morphFast = future.Fast.AsSpan(0, future.FastLength).ToArray();
        var unmorph = Form(22); unmorph.Phase = FormPhase.Unmorphing; unmorph.StartedFrame = 22;
        future.Prepare(Canonical(22, form: unmorph));
        Check(receiver.Receive(PacketType.PlayerSlowState, future.Slow.AsSpan(0, future.SlowLength), 7, 8)
            && receiver.TryDecodeLive(morphFast, decoded, 7, 8, out _) && decoded[0].Form.Phase == FormPhase.Unknown, "future unmorph slow form deferred");
        Check(receiver.TryDecodeLive(future.Fast.AsSpan(0, future.FastLength), decoded, 7, 8, out _) && decoded[0].Form.Phase == FormPhase.Unmorphing, "cached future unmorph becomes eligible");
        future.Prepare(Canonical(23, life: 4)); Check(receiver.TryDecodeLive(future.Fast.AsSpan(0, future.FastLength), decoded, 7, 8, out _) && decoded[0].Form.Phase == FormPhase.Unknown, "old life cache rejected");
        future.Prepare(Canonical(23, generation: 4)); Check(receiver.TryDecodeLive(future.Fast.AsSpan(0, future.FastLength), decoded, 7, 8, out _) && decoded[0].Form.Phase == FormPhase.Unknown, "old generation cache rejected");
        lanes.Prepare(Canonical(21)); Check(!lanes.SendSlow, "frame only retains six-frame cadence");
        var hp = Form(22); hp.TurretHealth--; lanes.Prepare(Canonical(22, form: hp)); Check(lanes.SendSlow, "turret health publishes immediately");
        hp.Frame = 23; hp.BodyHealth--; lanes.Prepare(Canonical(23, form: hp)); Check(lanes.SendSlow, "paired body health publishes immediately");
        hp.Frame = 24; hp.TurretActive = false; lanes.Prepare(Canonical(24, form: hp)); Check(lanes.SendSlow, "turret active publishes immediately");
        hp.Frame = 25; hp.Phase = FormPhase.Unmorphing; hp.StartedFrame = 25; lanes.Prepare(Canonical(25, form: hp)); Check(lanes.SendSlow, "phase and transition start publish immediately");
        var corruptSlow = lanes.Slow.AsSpan(0, lanes.SlowLength).ToArray(); corruptSlow[15 + NetReplicationLanes.FormOffset + 8] = 255;
        receiver.Reset(7, 8); Check(!receiver.Receive(PacketType.PlayerSlowState, corruptSlow, 7, 8), "slow codec rejects corrupted phase");
        byte[] full = new byte[SnapshotHeader.Size + 8 * PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = 30, PlayerCount = 8 }.Write(full);
        for (byte i = 0; i < 8; i++) new PlayerState { SlotIndex = i, SlotGeneration = 2, LifeId = 3, Health = ushort.MaxValue,
            Form = Form(30) }.Write(full.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(full.AsSpan(full.Length - NetHealthSync.HeaderSize), 7);
        var eight = new NetReplicationLanes(); eight.Prepare(full);
        Check(full.Length <= NetConfig.MaxSnapshotSize && eight.FastLength + NetHeader.Size <= 1200 && eight.SlowLength + NetHeader.Size <= 1200,
            "eight complete canonical forms and split lanes remain bounded");
        var eightReceiver = new NetReplicationReceiver(); var eightStates = new PlayerState[8];
        Check(eightReceiver.Receive(PacketType.PlayerSlowState, eight.Slow.AsSpan(0, eight.SlowLength), 7, 8)
            && eightReceiver.TryDecodeLive(eight.Fast.AsSpan(0, eight.FastLength), eightStates, 7, 8, out _) && eightStates[7].Form.TurretHealth == 37,
            "eight slow form rows roundtrip without stride overlap");
        Console.WriteLine($"FORM PROTOCOL {_checks} assertions PASS"); return 0;
    }
}
