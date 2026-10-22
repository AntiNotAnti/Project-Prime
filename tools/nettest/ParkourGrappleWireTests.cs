using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Independent literal byte/MTU oracles and passive controller gates.
// No assets, authority anchor admission or gameplay simulation.
internal static class ParkourGrappleWireTests
{
    private static int _checks;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException(label); }
    private static readonly PlayerGrappleState Active = new(true, 0x1234, new(3, 4, 0), 5);
    private static readonly byte[] Golden = Convert.FromHexString("0134120000404000008040000000000000A040");

    internal static int Run()
    {
        try
        {
            _checks = 0;
            StateBytes(); Nonces(); IntentBytes(); ReplayAdapters(); SplitLanes(); PassiveAcceptanceClock();
            Console.WriteLine($"GRAPPLE WIRE PASS {_checks} independent byte/ordering/replication assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("GRAPPLE WIRE FAIL " + error); return 1; }
    }

    private static void StateBytes()
    {
        Check(PlayerGrappleState.Size == 19 && PlayerState.Protocol80Size == 173 && PlayerState.Size == 192,
            "published173-byte player prefix and literal19-byte grapple extension");
        byte[] destination = Enumerable.Repeat((byte)0xCC, 24).ToArray();
        Check(Active.TryWrite(destination, out int written) && written == 19
            && destination.AsSpan(0, 19).SequenceEqual(Golden)
            && destination.AsSpan(19).IndexOfAnyExcept((byte)0xCC) < 0, "literal little-endian accepted anchor bytes and destination guard");
        Check(PlayerGrappleState.TryRead(Golden, out var decoded) && decoded == Active, "literal anchor decodes exactly");
        byte[] inactive = new byte[19]; inactive[1] = 0x34; inactive[2] = 0x12;
        Check(PlayerGrappleState.Inactive(0x1234).TryWrite(destination, out written)
            && destination.AsSpan(0, 19).SequenceEqual(inactive), "denial/release preserves nonce with canonical positive zeros");
        Check(PlayerGrappleState.TryRead(new byte[19], out decoded) && decoded == default, "initial all-zero state valid");
        foreach (int length in Enumerable.Range(0, 19).Append(20))
            Check(!PlayerGrappleState.TryRead(new byte[length], out decoded) && decoded == default, "exact state width " + length);
        foreach (var invalid in new[] {
            Active with { RequestNonce = 0 }, Active with { RopeLength = 0 }, Active with { RopeLength = 1.999f },
            Active with { RopeLength = 24.001f }, Active with { RopeLength = float.NaN }, Active with { RopeLength = float.PositiveInfinity },
            Active with { Anchor = new(float.NaN, 0, 0) }, Active with { Anchor = new(0, float.NegativeInfinity, 0) },
            Active with { Anchor = new(0, 0, MathF.BitIncrement(100000f)) },
            new PlayerGrappleState(false, 2, Vector3.UnitX, 0),
            new PlayerGrappleState(false, 2, new(-0f, 0, 0), 0),
            new PlayerGrappleState(false, 2, new(0, -0f, 0), 0),
            new PlayerGrappleState(false, 2, new(0, 0, -0f), 0),
            new PlayerGrappleState(false, 2, Vector3.Zero, -0f) })
        {
            Array.Fill(destination, (byte)0xCC);
            Check(!invalid.IsValid && !invalid.TryWrite(destination, out written) && written == 0
                && destination.All(x => x == 0xCC), "invalid state write is transactional " + invalid);
        }
        foreach (float range in new[] { 2f, 24f })
            Check((Active with { Anchor = new(-100000, 100000, 100000), RopeLength = range }).IsValid,
                "finite coordinate/range inclusive boundary " + range);
        Array.Fill(destination, (byte)0xCC);
        Check(!Active.TryWrite(destination.AsSpan(0, 18), out written) && written == 0
            && destination.All(x => x == 0xCC), "short destination unchanged");
        for (int flags = 2; flags < 256; flags++)
        {
            byte[] bad = Golden.ToArray(); bad[0] = (byte)flags;
            Check(!PlayerGrappleState.TryRead(bad, out decoded) && decoded == default, "unknown flags fail closed " + flags);
        }
        foreach (int offset in new[] { 3, 7, 11, 15 })
        {
            byte[] bad = inactive.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(offset), int.MinValue);
            Check(!PlayerGrappleState.TryRead(bad, out decoded) && decoded == default, "inactive negative zero rejected at " + offset);
            bad = Golden.ToArray(); BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(offset), float.NaN);
            Check(!PlayerGrappleState.TryRead(bad, out decoded), "active nonfinite rejected at " + offset);
        }
        byte[] player = new byte[192];
        new PlayerState { SlotIndex = 3, SlotGeneration = 4, LifeId = 5, Health = 90, Grapple = Active }.Write(player);
        Check(player.AsSpan(173, 19).SequenceEqual(Golden) && PlayerState.Read(player).Grapple == Active,
            "canonical player uses literal173 extension offset");
        Check(PlayerState.Read(player.AsSpan(0, 173)).Grapple == default, "old173-byte player invents no accepted anchor");
    }

    private static void Nonces()
    {
        Check((uint)IntentButtons.Grapple == 1u << 29, "grapple bit29 leaves stance/pivot bits25–28 intact");
        foreach (var row in new[] {
            ((ushort)0, (ushort)0, GrappleRequestOrder.Invalid),
            ((ushort)0, (ushort)1, GrappleRequestOrder.Invalid), ((ushort)1, (ushort)0, GrappleRequestOrder.Newer),
            ((ushort)7, (ushort)7, GrappleRequestOrder.Same), ((ushort)8, (ushort)7, GrappleRequestOrder.Newer),
            ((ushort)7, (ushort)8, GrappleRequestOrder.Older), ((ushort)1, ushort.MaxValue, GrappleRequestOrder.Newer),
            (ushort.MaxValue, (ushort)1, GrappleRequestOrder.Older), ((ushort)32769, (ushort)1, GrappleRequestOrder.Ambiguous),
            ((ushort)1, (ushort)32769, GrappleRequestOrder.Ambiguous) })
            Check(GrappleRequestOrdering.Compare(row.Item1, row.Item2) == row.Item3, "serial nonce order " + row);
        Check(GrappleRequestOrdering.Next(0) == 1 && GrappleRequestOrdering.Next(65535) == 1
            && GrappleRequestOrdering.Next(7) == 8, "new hold wraps while skipping reserved zero");
    }

    private static IntentPacket Intent(ushort nonce) => new() {
        MatchId = 7, AuthorityEpoch = 8, SlotGeneration = 2, LifeId = 3, Frame = 100,
        Position = new(1, 2, 3), Aim = Vector3.UnitZ, Target = NetTargetIdentity.None,
        Buttons = IntentButtons.Grapple | IntentButtons.CrouchedState | IntentButtons.TuckedState | (IntentButtons)(13u << 25),
        GrappleRequestNonce = nonce };

    private static void IntentBytes()
    {
        Check(IntentPacket.FullSize == 1287 && IntentPacket.Protocol80TouchSize == 1296
            && IntentPacket.Protocol82TouchSize == 1298 && IntentPacket.TouchCanonicalSize == 1299 && CompactIntentCodec.HeaderSize == 136,
            "old canonical/touch prefixes and compact header retain literal widths");
        var intent = Intent(0x1234);
        intent.NativeTouch = new(true, true, true, 100, -25, 7, false);
        byte[] canonical = new byte[1302]; Array.Fill(canonical, (byte)0xCC); intent.Write(canonical.AsSpan(0, 1298));
        byte[] touch = new byte[9]; intent.NativeTouch.Write(touch);
        Check(canonical.AsSpan(1287, 9).SequenceEqual(touch) && canonical[1296] == 0x34 && canonical[1297] == 0x12
            && canonical.AsSpan(1298).IndexOfAnyExcept((byte)0xCC) < 0, "canonical nonce appended after untouched native touch prefix");
        var old = IntentPacket.Read(canonical.AsSpan(0, 1296));
        Check(old.GrappleRequestNonce == 0 && old.NativeTouch == intent.NativeTouch, "old touch intent keeps report and no synthetic nonce");
        Check(IntentPacket.Read(canonical.AsSpan(0, 1298)).GrappleRequestNonce == 0x1234, "new canonical nonce roundtrip");
        foreach (ushort nonce in new ushort[] { 0, 1, 65535 })
        foreach (bool native in new[] { false, true })
        for (byte count = 0; count <= 14; count++)
        {
            intent = Intent(nonce); intent.FireEventCount = count;
            if (native) intent.NativeTouch = new(true, true, true, 100, -25, 7, false);
            for (int i = 0; i < count; i++)
                intent.FireEvents[i] = new((uint)(i + 1), 100, 90, 128, FireEventKind.PressFire, 0, 17, 0,
                    FireEvent.FlagPose | FireEvent.FlagReticle, new(3, 4, 5), Vector3.UnitZ,
                    Vector3.UnitX, Vector3.UnitY, new(0, 1), SourceLife: 3);
            byte[] compact = Enumerable.Repeat((byte)0xCC, 1200).ToArray();
            int size = CompactIntentCodec.Write(intent, compact);
            int eventStart = 136 + (native && count < 14 ? 9 : 0);
            Check(size == 139 + (native && count < 14 ? 9 : 0) + count * 74
                && compact[size] == 0xCC && BinaryPrimitives.ReadUInt16LittleEndian(compact.AsSpan(size - 3)) == nonce,
                $"literal compact length/trailer/guard {nonce}/{native}/{count}");
            Check(size + 24 + 1 <= 1200 && (count != 14 || size + 24 + 1 == 1200), "actual SlotIntent MTU including slot/header");
            Check(CompactIntentCodec.TryRead(compact.AsSpan(0, size), out var read)
                && read.GrappleRequestNonce == nonce && read.Buttons == intent.Buttons && read.FireEventCount == count
                && (count == 0 || read.FireEvents[count - 1] == intent.FireEvents[count - 1]), "compact actual nonce/control/combat roundtrip");
            Check(!native || read.NativeTouch.Active && (count == 14 ? read.NativeTouch.Suppressed : read.NativeTouch == intent.NativeTouch),
                "max combat preserves native ownership without old touch impulse");
            intent.Write(canonical.AsSpan(0, 1298));
            Check(compact.AsSpan(0, 103).SequenceEqual(canonical.AsSpan(0, 103))
                && compact.AsSpan(eventStart, count * 74).SequenceEqual(canonical.AsSpan(103, count * 74)),
                "frozen canonical prefix and each74-byte combat event independent of new trailer");
            Check(!CompactIntentCodec.TryRead(compact.AsSpan(0, size - 1), out _)
                && !CompactIntentCodec.TryRead(compact.AsSpan(0, size + 1), out _), "compact exact width rejects truncation/trailing bytes");
        }
        byte[] badCompact = new byte[1200]; intent = Intent(1);
        int idle = CompactIntentCodec.Write(intent, badCompact);
        badCompact[103] = CompactIntentCodec.TouchPresent;
        Check(!CompactIntentCodec.TryRead(badCompact.AsSpan(0, idle), out _), "touch sample without native mode rejected");
        CompactIntentCodec.Write(intent, badCompact); badCompact[102] = 15;
        Check(!CompactIntentCodec.TryRead(badCompact.AsSpan(0, idle), out _), "15-event live carrier rejected");
        CompactIntentCodec.Write(intent, badCompact); BinaryPrimitives.WriteSingleLittleEndian(badCompact.AsSpan(8), float.NaN);
        Check(!CompactIntentCodec.TryRead(badCompact.AsSpan(0, idle), out _), "nonce cannot make nonfinite carrier valid");
    }

    private static byte[] Canonical(uint frame, PlayerGrappleState grapple, byte count = 1, ushort generation = 2, ushort life = 3)
    {
        byte[] bytes = new byte[23 + count * 192 + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = frame, PlayerCount = count }.Write(bytes);
        for (byte i = 0; i < count; i++)
            new PlayerState { SlotIndex = i, SlotGeneration = generation, LifeId = life, Health = 80,
                Facing = Vector3.UnitZ, Grapple = grapple }.Write(bytes.AsSpan(23 + i * 192));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - NetHealthSync.HeaderSize), 7);
        return bytes;
    }

    private static void ReplayAdapters()
    {
        byte[] oldSnapshot = new byte[1 + 23 + 8 * 173 + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        oldSnapshot[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = 100, PlayerCount = 8 }.Write(oldSnapshot.AsSpan(1));
        byte[] player = new byte[192];
        for (byte slot = 0; slot < 8; slot++)
        {
            new PlayerState { SlotIndex = slot, SlotGeneration = (ushort)(slot + 1), LifeId = (ushort)(slot + 2),
                Health = (ushort)(80 + slot), Position = new(slot, slot + 1, slot + 2), Facing = Vector3.UnitZ }.Write(player);
            player.AsSpan(0, 173).CopyTo(oldSnapshot.AsSpan(24 + slot * 173));
        }
        int oldTail = 24 + 8 * 173;
        for (int at = 0; at < NetMatchTimeSync.Size; at += 4)
            BinaryPrimitives.WriteSingleLittleEndian(oldSnapshot.AsSpan(oldTail + at), at * .25f);
        BinaryPrimitives.WriteUInt16LittleEndian(oldSnapshot.AsSpan(oldTail + NetMatchTimeSync.Size), 7);
        foreach (int protocol in new[] { 77, 78, 79, 80, 81 })
        {
            var converted = ReplayIdentityCompatibility.Convert(oldSnapshot, protocol);
            Check(converted.Length == oldSnapshot.Length + 8 * 19
                && converted.Slice(0, 24).SequenceEqual(oldSnapshot.AsSpan(0, 24))
                && converted[(24 + 8 * 192)..].SequenceEqual(oldSnapshot.AsSpan(oldTail)),
                "historical snapshot preserves header and nonzero time/world tail " + protocol);
            for (int slot = 0; slot < 8; slot++)
                Check(converted.Slice(24 + slot * 192, 173).SequenceEqual(oldSnapshot.AsSpan(24 + slot * 173, 173))
                    && converted.Slice(24 + slot * 192 + 173, 19).IndexOfAnyExcept((byte)0) < 0
                    && PlayerState.Read(converted.Slice(24 + slot * 192, 192)).Grapple == default,
                    $"historical snapshot exact row expansion {protocol}/{slot}");
            var oldIntent = Intent(65535); oldIntent.NativeTouch = new(true, true, true, 100, -25, 7, false);
            byte[] packet = new byte[2 + 1296]; packet[0] = (byte)PacketType.SlotIntent; packet[1] = 3;
            oldIntent.Write(packet.AsSpan(2));
            var intentBytes = ReplayIdentityCompatibility.Convert(packet, protocol);
            var read = IntentPacket.Read(intentBytes[2..]);
            Check(intentBytes.Length == 2 + 1299 && intentBytes[1] == 3 && read.GrappleRequestNonce == 0
                && read.Buttons == (oldIntent.Buttons & ~IntentButtons.Grapple) && read.NativeTouch == oldIntent.NativeTouch
                && intentBytes.Slice(2 + 8, 1296 - 8).SequenceEqual(packet.AsSpan(2 + 8)),
                "historical reserved grapple bit cleared, touch/pivot prefix retained, nonce synthesized zero " + protocol);
            bool rejected = false;
            try { ReplayIdentityCompatibility.Convert(oldSnapshot.AsSpan(0, oldTail), protocol); }
            catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "historical snapshot missing actual time/world tail rejected " + protocol);
        }
        byte[] live = new byte[1 + Canonical(100, Active, 8).Length]; live[0] = (byte)PacketType.Snapshot;
        Canonical(100, Active, 8).CopyTo(live, 1);
        Check(ReplayIdentityCompatibility.Convert(live, 82).SequenceEqual(live), "current82 eight accepted anchors remain byte-exact through replay boundary");
    }

    private static void SplitLanes()
    {
        Check(NetReplicationLanes.Protocol80SlowEntry == 58 && NetReplicationLanes.GrappleOffset == 58
            && NetReplicationLanes.SlowEntry == 77 && SnapshotFast.PlayerSize == 138, "slow extension preserves fast and form prefixes");
        var lanes = new NetReplicationLanes(); lanes.Prepare(Canonical(20, Active, 8));
        Check(lanes.FastLength + 24 == 1172 && lanes.SlowLength + 24 == 719
            && SnapshotFast.MaximumEncodedSize == 1172 && SnapshotFast.BootstrapMaximumEncodedSize == 1151,
            "actual eight-player live/bootstrap fast and grapple slow MTUs");
        for (int i = 0; i < 8; i++)
            Check(lanes.Slow.AsSpan(15 + i * 77 + 58, 19).SequenceEqual(Golden), "literal slow slot stride " + i);
        var receiver = new NetReplicationReceiver(); var decoded = new PlayerState[8];
        Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 7, 8)
            && receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 7, 8, out _)
            && decoded.All(p => p.Grapple == Active), "eight accepted anchors join matching fast identities");
        byte[] assembled = new byte[4096];
        int length = receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength - 21), assembled, 7, 8);
        Check(length == Canonical(20, Active, 8).Length
            && PlayerState.Read(assembled.AsSpan(23 + 7 * 192, 192)).Grapple == Active, "bootstrap assembly retains final grapple row");
        byte[] baselineFast = lanes.Fast.AsSpan(0, lanes.FastLength).ToArray();
        uint revision = receiver.SlowRevision;
        void Reject(byte[] bad, ushort match, ulong epoch, string label)
        {
            Check(!receiver.Receive(PacketType.PlayerSlowState, bad, match, epoch) && receiver.SlowRevision == revision
                && receiver.TryDecodeLive(baselineFast, decoded, 7, 8, out _) && decoded.All(p => p.Grapple == Active), label);
        }
        byte[] original = lanes.Slow.AsSpan(0, lanes.SlowLength).ToArray();
        Reject(original, 7, 8, "duplicate revision refused without mutation");
        foreach (int width in new[] { original.Length - 1, original.Length + 1 })
        {
            byte[] bad = new byte[width]; original.AsSpan(0, Math.Min(width, original.Length)).CopyTo(bad);
            Reject(bad, 7, 8, "slow exact packet width " + width);
        }
        byte[] corrupt = original.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(10), revision + 1);
        corrupt[15 + 7 * 77 + 58] = 0x80;
        Reject(corrupt, 7, 8, "invalid final anchor rejects all rows transactionally");
        BinaryPrimitives.WriteUInt16LittleEndian(corrupt, 99); BinaryPrimitives.WriteUInt64LittleEndian(corrupt.AsSpan(2), 100);
        Reject(corrupt, 99, 100, "invalid new stream cannot reset old accepted state");
        corrupt = original.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(10), revision + 1); corrupt[15 + 7 * 77] = 0;
        Reject(corrupt, 7, 8, "duplicate final slot rejects full slow update");
        Reject(original, 8, 8, "wrong match cannot install anchor");
        Reject(original, 7, 9, "wrong authority epoch cannot install anchor");
        lanes.Prepare(Canonical(21, Active, 8, life: 4));
        Check(receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 7, 8, out _)
            && decoded.All(p => p.Grapple == default), "old life accepted anchor never joins new life fast state");
        lanes.Prepare(Canonical(22, Active, 8, generation: 3));
        Check(receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), decoded, 7, 8, out _)
            && decoded.All(p => p.Grapple == default), "old generation accepted anchor never joins reused slot");
        var immediate = new NetReplicationLanes(); immediate.Prepare(Canonical(30, Active));
        immediate.Prepare(Canonical(31, Active)); Check(!immediate.SendSlow, "unchanged accepted anchor keeps six-frame cadence");
        uint frame = 32;
        foreach (var state in new[] { Active with { Anchor = new(4, 4, 0) }, Active with { RopeLength = 6 },
            Active with { RequestNonce = 0x1235 }, PlayerGrappleState.Inactive(0x1235), PlayerGrappleState.Inactive(0x1236) })
        {
            uint prior = immediate.SlowRevision; immediate.Prepare(Canonical(frame++, state));
            Check(immediate.SendSlow && immediate.SlowRevision == prior + 1, "every accepted geometry/nonce/tombstone change publishes immediately");
        }
    }

    private static void PassiveAcceptanceClock()
    {
        // Use the real passive scene/service owner; no network authority globals
        // or private accepted state are injected. This is not owner-force QA.
        Headless.Enter();
        var foreground = GameState.Current; var registry = PlayerEntity.LegacyRegistry;
        var host = new PassiveReplaySessionHost(); using var session = new ReplayPlaybackSession(host);
        var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, new ReplaySceneServices(session, host.State), initializeRuntime: false);
        try
        {
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var clock = typeof(Scene).GetField("_frameCount", hidden)!;
            var started = typeof(PlayerEntity).GetField("_modGrappleStarted", hidden)!;
            var applyForce = typeof(PlayerEntity).GetMethod("ModApplyParkourGrapple", hidden)!;
            var maintain = typeof(PlayerEntity).GetMethod("ModMaintainParkourGrapple", hidden)!;
            var cancel = typeof(PlayerEntity).GetMethod("ModCancelParkourGrapple", hidden)!;
            var player = scene.Players.Items[0];
            Vector3 position = player.Position, speed = player.Speed;
            clock.SetValue(scene, 1000UL);
            player.ModApplyReplicatedGrapple(Active, 4, 5);
            Check(player.ModGrappleState == Active && (ulong)started.GetValue(player)! == 1000,
                "first accepted activation after frame180 starts lifetime on current scene clock");
            clock.SetValue(scene, 1010UL); player.ModApplyReplicatedGrapple(Active, 4, 5);
            Check((ulong)started.GetValue(player)! == 1000, "same accepted hold does not extend lifetime on every snapshot");
            player.ModApplyReplicatedGrapple(PlayerGrappleState.Inactive(0x1234), 4, 5);
            player.ModApplyReplicatedGrapple(Active with { RequestNonce = 0x1235 }, 4, 5);
            Check((ulong)started.GetValue(player)! == 1010, "new accepted hold starts a fresh bounded lifetime");
            clock.SetValue(scene, 1200UL); player.ModRestoreGrapplePresentation(Active, 4, 5);
            Check(player.ModGrappleState == Active && (ulong)started.GetValue(player)! == 1200,
                "checkpoint presentation restore initializes current passive clock");
            player.Controls.Grapple.IsDown = true; player.ModSampleGrappleControl();
            maintain.Invoke(player, null); cancel.Invoke(player, null);
            applyForce.Invoke(player, new object[] { true, false });
            Check(player.ModGrappleSelectionCount == 0 && player.ModGrappleForceCount == 0
                && player.ModGrappleGeometryQueryCount == 0 && player.ModGrappleState == Active
                && player.ModGrappleRequestNonce == 0 && !player.ModGrappleOwnsMovement
                && player.Position == position && player.Speed == speed,
                "passive accepted anchor plus held control never queries, owns force or changes motion");
            player.ModApplyReplicatedGrapple(Active with { RopeLength = float.NaN }, 4, 5);
            Check(player.ModGrappleState == Active, "invalid replicated state cannot replace accepted passive state");
            Check(ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(registry, PlayerEntity.LegacyRegistry),
                "passive wire/controller regression preserves foreground owners");
        }
        finally { scene.DoCleanup(); scene.UnloadGl(); }
    }
}
