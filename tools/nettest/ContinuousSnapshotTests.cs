using System;
using MphRead.Mods.MatchEvents;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ContinuousSnapshotTests
{
    private const int TailSize = 21;
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }

    public static int Run()
    {
        try
        {
            HealthShotTests.Session();
            var roster = RosterPacket.Create(); roster.MatchId = 51; roster.AuthorityEpoch = 4;
            roster.Revision = 2; roster.Count = 8;
            for (int i = 0; i < 8; i++) { roster.Slots[i] = (byte)i; roster.Generations[i] = (ushort)(9 + i); }
            NetSession.ApplyRoster(roster);
            byte[] canonical = Canonical();
            int canonicalPlayerSize = PlayerState.Size;
            var lanes = new NetReplicationLanes();
            RecordPhase(0, 100); RecordPhase(0, 36); RecordPhase(1, 200);
            lanes.Prepare(canonical);
            Check(SnapshotFast.PlayerSize == canonicalPlayerSize - 7,
                "continuous receipts do not change canonical or fast player record size");
            Check(lanes.FastLength == SnapshotHeader.Size + 8 * SnapshotFast.PlayerSize + TailSize
                && lanes.FastLength + NetHeader.Size == SnapshotFast.MaximumEncodedSize
                && SnapshotFast.MaximumEncodedSize == 1172 && SnapshotFast.MaximumEncodedSize <= 1200,
                "eight-player personalized snapshot retains the 1200-byte MTU budget");

            var receiver = new NetReplicationReceiver();
            Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 51, 4),
                "slow canonical state admitted");
            Check(receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 51, 4),
                "world canonical state admitted");
            var decoded = new PlayerState[8];
            byte[] ownerZero = lanes.ForRecipient(0).ToArray();
            Check(receiver.TryDecodeLive(ownerZero, decoded, 51, 4, out var header) && header.PlayerCount == 8,
                "personalized eight-player snapshot round trips");
            Check(decoded[0].ContinuousPaidTick == 100 && decoded[0].ContinuousPaidMaskLow == 1
                && decoded[0].ContinuousPaidMaskHigh == 1, "recipient exact high/low receipt bitmap survives wire decode");
            for (int i = 1; i < 8; i++) Check(Empty(decoded[i]), "recipient receipt does not leak into another slot");
            byte[] ownerOne = lanes.ForRecipient(1).ToArray();
            Check(receiver.TryDecodeLive(ownerOne, decoded, 51, 4, out _) && Empty(decoded[0])
                && decoded[1].ContinuousPaidTick == 200 && decoded[1].ContinuousPaidMaskLow == 1,
                "changing recipient clears preceding personalized owner receipt");
            byte[] noOwner = lanes.ForRecipient(255).ToArray();
            Check(noOwner[^TailSize] == 255 && receiver.TryDecodeLive(noOwner, decoded, 51, 4, out _),
                "recipient-free snapshots use explicit empty receipt tail");
            foreach (var player in decoded) Check(Empty(player), "recipient-free decode contains no owner receipt");

            // A later payment must not accompany the earlier captured canonical health.
            RecordPhase(0, 101);
            Check(lanes.ForRecipient(0).SequenceEqual(ownerZero),
                "personalization uses immutable receipt capture from the canonical health snapshot");
            byte[] rebuilt = new byte[4096];
            int length = receiver.Assemble(ownerZero, rebuilt, 51, 4);
            Check(length == canonical.Length && canonical.AsSpan().SequenceEqual(rebuilt.AsSpan(0, length)),
                "canonical assembly strips personalization without changing replay bytes");
            var bootstrapFast = ownerZero.AsSpan(0, ownerZero.Length - TailSize);
            Check(!NetReplicationReceiver.ValidateFast(bootstrapFast, 51, 4, out _)
                && NetReplicationReceiver.ValidateFast(bootstrapFast, 51, 4, out _, bootstrap: true),
                "receipt-free bootstrap cannot be accepted as a live snapshot");
            int bootstrapLength = receiver.Assemble(bootstrapFast, rebuilt, 51, 4);
            Check(bootstrapLength == canonical.Length && canonical.AsSpan().SequenceEqual(rebuilt.AsSpan(0, bootstrapLength))
                && SnapshotFast.BootstrapMaximumEncodedSize + WorldBootstrapIdentity.Size + 1 + 4 <= 1200,
                "bootstrap stripping preserves canonical bytes and the full reliable envelope MTU");
            byte[] ordinary = new byte[1200]; int ordinaryLength = SnapshotFast.Write(canonical, ordinary);
            Check(ordinaryLength == lanes.FastLength && ordinary[ordinaryLength - TailSize] == 255
                && ordinary.AsSpan(ordinaryLength - TailSize + 1, TailSize - 1).IndexOfAnyExcept((byte)0) < 0,
                "canonical fast writer emits a deterministic empty receipt tail");
            Check(receiver.TryDecodeLive(ordinary.AsSpan(0, ordinaryLength), decoded, 51, 4, out _),
                "unpersonalized canonical fast writer remains decodable");

            Malformed(ownerZero, receiver, decoded);
            LifecycleCapture(canonical);
            // Keep measurement control outside tiered-JIT loop transitions. The
            // production rewrite/decode methods remain optimized and are warmed.
            DecodeLoop(lanes, receiver, decoded, 10000);
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool success = DecodeLoop(lanes, receiver, decoded, 10000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(success, "warmed personalized snapshots remain valid across all recipients");
            Check(allocated == 0, $"warmed personalized fast rewrite and direct decode allocate zero bytes (actual {allocated})");
            PresentationCodecAndSidecar(canonical);
            FactBatching();
            Console.WriteLine($"PASS: {_checks} continuous snapshot assertions, {SnapshotFast.MaximumEncodedSize}B maximum");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetSession.Stop(); }
    }

    private static bool Empty(in PlayerState player) => player.ContinuousPaidTick == 0
        && player.ContinuousPaidMaskLow == 0 && player.ContinuousPaidMaskHigh == 0;

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static bool DecodeLoop(NetReplicationLanes lanes, NetReplicationReceiver receiver,
        PlayerState[] decoded, int count)
    {
        bool success = true;
        for (int i = 0; i < count; i++) success &= receiver.TryDecodeLive(lanes.ForRecipient(i % 8), decoded, 51, 4, out _);
        return success;
    }

    private static byte[] Canonical()
    {
        int time = SnapshotHeader.Size + 8 * PlayerState.Size;
        byte[] bytes = new byte[time + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        new SnapshotHeader { MatchId = 51, AuthorityEpoch = 4, Frame = 25, PlayerCount = 8,
            Rng1 = 7, Rng2 = 8 }.Write(bytes);
        for (int i = 0; i < 8; i++)
        {
            var state = new PlayerState { SlotIndex = (byte)i, SlotGeneration = (ushort)(9 + i),
                LifeId = (ushort)(20 + i), Health = (ushort)(80 + i), Points = (short)(i * 3),
                Flags = PlayerState.FlagActive | PlayerState.FlagSpawned,
                ContinuousPaidTick = 999, ContinuousPaidMaskLow = ulong.MaxValue, ContinuousPaidMaskHigh = ulong.MaxValue };
            NetPlayerLifecycle.AcceptState(state, 25);
            state.Write(bytes.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(time + NetMatchTimeSync.Size), 51);
        return bytes;
    }

    private static void RecordPhase(int slot, uint phase)
    {
        Type type = typeof(NetSession).Assembly.GetType("MphRead.Mods.Network.NetContinuousPayments")!;
        type.GetMethod("RecordPhase", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { ShotKey.For(slot, phase) });
    }

    private static void Malformed(byte[] valid, NetReplicationReceiver receiver, PlayerState[] decoded)
    {
        for (int cut = 1; cut <= TailSize; cut++)
            Check(!NetReplicationReceiver.ValidateFast(valid.AsSpan(0, valid.Length - cut), 51, 4, out _),
                "truncated continuous receipt tail is refused atomically");
        byte[] oversized = new byte[valid.Length + 1]; valid.CopyTo(oversized, 0);
        Check(!NetReplicationReceiver.ValidateFast(oversized, 51, 4, out _), "extra receipt bytes are refused");
        byte[] malformed = (byte[])valid.Clone(); malformed[^TailSize] = 8;
        Check(!receiver.TryDecodeLive(malformed, decoded, 51, 4, out _), "invalid recipient slot is refused");
        byte[] absentRecipient = new byte[valid.Length - SnapshotFast.PlayerSize];
        var header = SnapshotHeader.Read(valid); header.PlayerCount = 7; header.Write(absentRecipient);
        valid.AsSpan(SnapshotHeader.Size, 7 * SnapshotFast.PlayerSize).CopyTo(absentRecipient.AsSpan(SnapshotHeader.Size));
        valid.AsSpan(valid.Length - TailSize, TailSize).CopyTo(absentRecipient.AsSpan(absentRecipient.Length - TailSize));
        absentRecipient[^TailSize] = 7;
        Check(!NetReplicationReceiver.ValidateFast(absentRecipient, 51, 4, out _),
            "receipt recipient must be present in the enclosing snapshot");
        malformed = (byte[])valid.Clone(); malformed[SnapshotHeader.Size + 7 * SnapshotFast.PlayerSize] = 6;
        Check(!NetReplicationReceiver.ValidateFast(malformed, 51, 4, out _),
            "duplicate player slot cannot hide a malformed receipt target");
        Check(!NetReplicationReceiver.ValidateFast(valid, 52, 4, out _)
            && !NetReplicationReceiver.ValidateFast(valid, 51, 5, out _), "receipt cannot cross stream identity");
    }

    private static void LifecycleCapture(byte[] canonical)
    {
        byte[] stale = (byte[])canonical.Clone();
        var state = PlayerState.Read(stale.AsSpan(SnapshotHeader.Size, PlayerState.Size));
        state.LifeId++; state.Write(stale.AsSpan(SnapshotHeader.Size, PlayerState.Size));
        var lanes = new NetReplicationLanes(); lanes.Prepare(stale);
        var packet = lanes.ForRecipient(0); int tail = packet.Length - TailSize;
        Check(packet[tail] == 255 || BinaryPrimitives.ReadUInt32LittleEndian(packet[(tail + 1)..]) == 0
            && BinaryPrimitives.ReadUInt64LittleEndian(packet[(tail + 5)..]) == 0
            && BinaryPrimitives.ReadUInt64LittleEndian(packet[(tail + 13)..]) == 0,
            "canonical owner life mismatch cannot capture another life's paid receipt");
    }

    private static void PresentationCodecAndSidecar(byte[] canonical)
    {
        var target = NetTargetIdentity.ForSlot(0);
        var ownerState = PlayerState.Read(canonical.AsSpan(SnapshotHeader.Size + PlayerState.Size, PlayerState.Size));
        var point = new Vector3(12.125f, -3.5f, 17.75f);
        var view = new ContinuousPresentation(ownerState.LifeId, 400, target, point);
        Span<byte> codec = stackalloc byte[ContinuousPresentation.Size]; view.Write(codec);
        Check(ContinuousPresentation.WellFormed(codec) && ContinuousPresentation.Read(codec) == view,
            "continuous presentation life/phase/target and quantized endpoint round trip");
        Check(!ContinuousPresentation.WellFormed(codec[..^1]), "truncated presentation sidecar is refused");
        codec[17] = 2;
        Check(!ContinuousPresentation.WellFormed(codec), "unknown presentation status is refused");
        codec.Clear(); codec[0] = 1;
        Check(!ContinuousPresentation.WellFormed(codec), "empty presentation status cannot contain stale nonzero data");
        var invalid = view with { Endpoint = new Vector3(float.NaN, 0, 0) }; invalid.Write(codec);
        Check(ContinuousPresentation.WellFormed(codec) && !ContinuousPresentation.Read(codec).Valid,
            "nonfinite endpoint encodes an empty cosmetic confirmation");
        invalid = view with { Endpoint = new Vector3(512, 0, 0) }; invalid.Write(codec);
        Check(ContinuousPresentation.WellFormed(codec) && !ContinuousPresentation.Read(codec).Valid,
            "out-of-range endpoint cannot wrap quantized coordinates");

        NetContinuousPresentation.Reset();
        uint now = NetSession.NetFrame;
        var key = new ContinuousPaymentKey(ShotKey.For(1, 400), target, ContinuousPaymentKey.Body);
        NetContinuousPresentation.Confirm(key, point);
        Check(NetContinuousPresentation.Capture(ownerState) == view, "authority presentation capture matches canonical owner lifecycle");
        var wrong = ownerState; wrong.LifeId++;
        Check(!NetContinuousPresentation.Capture(wrong).Valid, "presentation cannot cross canonical owner life");
        wrong = ownerState; wrong.SlotGeneration++;
        Check(!NetContinuousPresentation.Capture(wrong).Valid, "presentation cannot cross canonical owner occupant");
        NetContinuousPresentation.Confirm(key with { OwnerTick = ShotKey.For(1, 399) }, Vector3.Zero);
        Check(NetContinuousPresentation.Capture(ownerState).Endpoint == point, "reordered cosmetic phase cannot replace newer endpoint");
        NetContinuousPresentation.Reset();
        NetContinuousPresentation.Confirm(key with { OwnerTick = key.OwnerTick with { MatchId = 52 } }, point);
        Check(!NetContinuousPresentation.Capture(ownerState).Valid, "cosmetic confirmation cannot cross match context");
        NetContinuousPresentation.Reset();
        NetContinuousPresentation.Confirm(key with { Victim = target with { LifeId = (ushort)(target.LifeId + 1) } }, point);
        Check(!NetContinuousPresentation.Capture(ownerState).Valid, "cosmetic confirmation cannot reuse a target's old or future life");
        NetContinuousPresentation.Reset(); NetContinuousPresentation.Confirm(key, point);
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now + 13);
        Check(!NetContinuousPresentation.Capture(ownerState).Valid, "authority cosmetic confirmation expires after twelve frames");
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now);
        NetContinuousPresentation.Reset(); NetContinuousPresentation.Confirm(key, point);

        var lanes = new NetReplicationLanes(); lanes.Prepare(canonical);
        Check(lanes.SlowLength + NetHeader.Size <= 1200 && NetReplicationLanes.SlowEntry == 28,
            "eight owner presentation sidecars remain within the slow-lane MTU");
        byte[] firstSlow = lanes.Slow.AsSpan(0, lanes.SlowLength).ToArray();
        var receiver = new NetReplicationReceiver();
        Check(receiver.Receive(PacketType.PlayerSlowState, firstSlow, 51, 4), "presentation sidecar slow packet admitted");
        Check(receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 51, 4), "presentation fixture world admitted");
        byte[] rebuilt = new byte[4096]; int length = receiver.Assemble(lanes.ForRecipient(0), rebuilt, 51, 4);
        Check(length == canonical.Length && canonical.AsSpan().SequenceEqual(rebuilt.AsSpan(0, length)),
            "cosmetic sidecar cannot mutate canonical health, damage, score or replay bytes");
        uint revision = lanes.SlowRevision;
        for (uint step = 1; step <= 5; step++)
        {
            typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now + step);
            NetContinuousPresentation.Confirm(key with { OwnerTick = ShotKey.For(1, 400 + step) }, point + Vector3.UnitX * step);
            byte[] newer = (byte[])canonical.Clone(); var header = SnapshotHeader.Read(newer); header.Frame += step; header.Write(newer);
            lanes.Prepare(newer);
            Check(!lanes.SendSlow && lanes.SlowRevision == revision, "changing endpoint alone cannot increase slow-lane cadence");
        }
        var next = (byte[])canonical.Clone(); var nextHeader = SnapshotHeader.Read(next); nextHeader.Frame += 6; nextHeader.Write(next);
        lanes.Prepare(next);
        Check(lanes.SendSlow && lanes.SlowRevision != revision, "presentation endpoint publishes at existing six-frame slow cadence");

        byte[] malformed = (byte[])firstSlow.Clone(); malformed[15 + NetReplicationLanes.SlowEntry + 10 + 17] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(10), 100);
        Check(!receiver.Receive(PacketType.PlayerSlowState, malformed, 51, 4), "malformed sidecar refuses whole slow packet before revision update");
        Check(receiver.SlowRevision == revision, "malformed cosmetic state cannot mutate received revision");

        var owner = HealthShotTests.Player(1); var victim = HealthShotTests.Player(0);
        var beam = (BeamProjectileEntity)RuntimeHelpers.GetUninitializedObject(typeof(BeamProjectileEntity));
        beam.Owner = owner; HealthShotTests.Field(beam, "<Target>k__BackingField", victim);
        beam.ModLaunchGeneration = ownerState.SlotGeneration; beam.ModLaunchLife = ownerState.LifeId; beam.ModContinuousPhase = 400;
        Check(receiver.TryContinuousEndpoint(beam, out var endpoint) && endpoint == point,
            "matching remote beam consumes confirmed cosmetic endpoint");
        beam.ModLaunchLife++;
        Check(!receiver.TryContinuousEndpoint(beam, out _), "draw confirmation cannot cross beam launch life");
        beam.ModLaunchLife = ownerState.LifeId; beam.ModContinuousPhase = 399;
        Check(!receiver.TryContinuousEndpoint(beam, out _), "draw confirmation cannot be newer than remote beam phase");
        beam.ModContinuousPhase = 400; HealthShotTests.Field(beam, "<Target>k__BackingField", HealthShotTests.Player(2));
        Check(!receiver.TryContinuousEndpoint(beam, out _), "draw confirmation cannot redirect beam to a different target");
        HealthShotTests.Field(beam, "<Target>k__BackingField", victim); beam.Owner = victim;
        Check(!receiver.TryContinuousEndpoint(beam, out _), "owner prediction is never overwritten by remote cosmetic endpoint");
        beam.Owner = owner;
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now + 25);
        Check(!receiver.TryContinuousEndpoint(beam, out _), "received cosmetic endpoint expires after twenty-four frames");
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now);
        NetContinuousPresentation.ForgetSlot(0);
        Check(!NetContinuousPresentation.Capture(ownerState).Valid, "target slot retirement clears cosmetic confirmation");
    }
    private static void FactBatching()
    {
        byte[] semanticPacket = new byte[MatchSemanticEventPacket.Size];
        new MatchSemanticEventPacket(51, 4, 0, 0, MatchSemanticEventType.MatchStarted,
            255, 0, 0, 255, 0, 0, -1, 0, -1, 0, IsBaseline: true).Write(semanticPacket);
        byte[] awardPacket = new byte[MatchAwardPacket.Size];
        new MatchAwardPacket(51, 4, 1, 1, 10, 0, 9, 20, (MatchAwardKind)0).Write(awardPacket);
        byte[] replay = new byte[ReplayShotFactPacket.Size];
        ReplayShotFactPacket.Write(new ReplayShotFact(51, 4, 10, 9, 1, 1,
            0, 9, 20, 1, 10, 21, 7, ReplayShotFactFlags.Continuous,
            1, 98, 0, 0, Vector3.Zero), replay);
        var records = new[] { semanticPacket.AsSpan(1).ToArray(), awardPacket.AsSpan(1).ToArray(), replay };
        var types = new[] { PacketType.MatchSemanticEvent, PacketType.MatchAward, PacketType.ReplayShotFact };
        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i]; var record = records[i];
            Check(NetFactBatch.Validate(type, record), "protocol 46 admits one canonical fact");
            byte[] pair = new byte[record.Length * 2]; record.CopyTo(pair, 0); record.CopyTo(pair, record.Length);
            Check(NetFactBatch.Validate(type, pair), "protocol 46 admits multiple same-type canonical facts");
            Check(!NetFactBatch.Validate(type, pair.AsSpan(0, pair.Length - 1)), "truncated final fact refuses envelope");
            byte[] extra = new byte[pair.Length + 1]; pair.CopyTo(extra, 0);
            Check(!NetFactBatch.Validate(type, extra), "unframed tail refuses envelope");
            // Match ID is the first two payload bytes for semantic/award, after version for replay.
            int matchOffset = record.Length + (type == PacketType.ReplayShotFact ? 1 : 0);
            pair[matchOffset] = pair[matchOffset + 1] = 0;
            Check(!NetFactBatch.Validate(type, pair), "invalid trailing member refuses whole envelope");
            Check(!NetFactBatch.Validate(type, ReadOnlySpan<byte>.Empty), "empty fact batch refused");
            var retained = new NetRetainedDelivery(100); var cursor = retained.Join();
            for (int n = 0; n < 64; n++) retained.Append(type, record);
            ulong start = cursor.Next; int calls = 0, admitted = 0;
            Check(retained.PumpBatched(cursor, (kind, payload) => { calls++; return false; })
                && cursor.Next == start && calls == 1, "backpressure preserves entire fact cursor");
            Check(retained.PumpBatched(cursor, (kind, payload) =>
            {
                Check(kind == type && NetFactBatch.Validate(kind, payload), "retained batch contains valid same-type facts");
                Check(payload.Length <= NetFactBatch.MaximumPayload
                    && payload.Length + 4 + NetHeader.Size <= 1200,
                    "fact batch including reliable ID and network header respects datagram MTU");
                for (int offset = 0; offset < payload.Length; offset += record.Length)
                    Check(payload.Slice(offset, record.Length).SequenceEqual(record), "canonical fact bytes survive batching exactly");
                admitted += payload.Length / record.Length; return true;
            }) && admitted == 64 && cursor.Next == start + 64, "retry admits each retained fact exactly once");
            Check(retained.PumpBatched(cursor, (kind, payload) => { admitted++; return true; })
                && admitted == 64, "drained cursor cannot resend accepted facts");
            byte[] oversized = new byte[(NetFactBatch.MaximumPayload / record.Length + 1) * record.Length];
            for (int offset = 0; offset < oversized.Length; offset += record.Length) record.CopyTo(oversized, offset);
            Check(!NetFactBatch.Validate(type, oversized), "oversized valid-record envelope is refused");
        }
        var mixed = new NetRetainedDelivery(); var mixedCursor = mixed.Join();
        for (int i = 0; i < types.Length; i++) mixed.Append(types[i], records[i]);
        int accepted = 0;
        Check(mixed.PumpBatched(mixedCursor, (kind, payload) =>
        {
            if (accepted == 1) return false;
            Check(kind == types[accepted] && payload.SequenceEqual(records[accepted]), "mixed type transition preserves order");
            accepted++; return true;
        }) && accepted == 1 && mixedCursor.Next == 2, "backpressure after first type preserves next type cursor");
        Check(mixed.PumpBatched(mixedCursor, (kind, payload) =>
        {
            Check(kind == types[accepted] && payload.SequenceEqual(records[accepted]), "mixed retry preserves exact next record");
            accepted++; return true;
        }) && accepted == 3 && mixedCursor.Next == 4, "mixed retry drains remaining types once");
        var small = new NetRetainedDelivery(2); var stale = small.Join();
        for (int i = 0; i < 3; i++) small.Append(types[0], records[0]);
        Check(!small.PumpBatched(stale, (kind, payload) => throw new InvalidOperationException("overrun was delivered"))
            && stale.Next == 1, "retained overrun fails explicitly without cursor advance");
        Check(NetFactBatch.Stride(PacketType.Snapshot) == 0
            && !NetFactBatch.Validate(PacketType.Snapshot, records[0]), "unrelated packet kinds cannot enter fact batching");
    }

}
