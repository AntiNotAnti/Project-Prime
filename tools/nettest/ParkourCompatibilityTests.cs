using System;
using System.Buffers.Binary;
using System.IO;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Synthetic packet fixtures; existing replay archives remain untouched.
internal static class ParkourCompatibilityTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        { checks++; if (!value) throw new InvalidOperationException(label); }
        void Reject(byte[] packet, int protocol)
        {
            bool rejected = false;
            try { ReplayIdentityCompatibility.Convert(packet, protocol); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, $"Protocol{protocol} must reject Parkour bit19.");
        }
        Check(NetConfig.ProtocolVersion == 84, "Current protocol84 retains Parkour v5 introduced in protocol82.");
        Check((byte)MovementModel.Parkour == 4 && (uint)MatchModifierFlags.ParkourMovement == 524288u,
            "Parkour model4 and bit19 are stable.");
        Check(MovementModelRules.KnownModifierMask == 2097151u,
            "Live known modifier mask includes the package availability bit20.");
        Check(MovementModelRules.ValidRules(MatchModifierFlags.ParkourMovement)
            && MovementModelRules.FromRules(MatchModifierFlags.ParkourMovement) == MovementModel.Parkour,
            "Parkour is an explicit movement selection.");
        Check((uint)MatchModifierFlags.PackagedWeaponAvailable == 1u << 20
            && MovementModelRules.ValidRules(MatchModifierFlags.ParkourMovement | MatchModifierFlags.PackagedWeaponAvailable),
            "Package availability bit20 remains independent of the selected Parkour profile.");
        foreach (var other in new[] { MatchModifierFlags.MomentumMovement,
            MatchModifierFlags.Quake3Movement, MatchModifierFlags.TraversalMovement })
            Check(!MovementModelRules.ValidRules(other | MatchModifierFlags.ParkourMovement),
                "Movement model flags remain mutually exclusive.");
        Check(!MovementModelRules.ValidRules((MatchModifierFlags)(1u << 21)),
            "Future unknown modifier remains unsupported.");
        Check(MovementModelRules.ProfileVersion(MovementModel.Parkour) == 5
            && new ReplayMetadata { ProtocolVersion = 78, IndexedMovementModel = MovementModel.Parkour }
                .RecordedMovementProfileVersion == 2, "Protocol78 selects Parkour v2 while protocol77 stays v1.");
        Check(new ReplayMetadata { ProtocolVersion = 79, IndexedMovementModel = MovementModel.Parkour }
            .RecordedMovementProfileVersion == 3, "Protocol79 retains Parkour v3 explicitly.");
        Check(new ReplayMetadata { ProtocolVersion = 80, IndexedMovementModel = MovementModel.Parkour }
            .RecordedMovementProfileVersion == 4, "Protocol80 records Parkour v4 explicitly.");
        Check(new ReplayMetadata { ProtocolVersion = 81, IndexedMovementModel = MovementModel.Parkour }
            .RecordedMovementProfileVersion == 4, "Protocol81 package preparation preserves Parkour v4 explicitly.");
        Check(new ReplayMetadata { ProtocolVersion = 82, IndexedMovementModel = MovementModel.Parkour }
            .RecordedMovementProfileVersion == 5, "Protocol82 records Parkour v5 explicitly.");
        var metadata = new ReplayMetadata { ProtocolVersion = 77, IndexedMovementModel = MovementModel.Parkour };
        Check(metadata.RecordedMovementModel == MovementModel.Parkour
            && metadata.RecordedMovementProfileVersion == 1, "Protocol77 records Parkour v1 explicitly.");
        Check(new ReplayMetadata { ProtocolVersion = 76, IndexedMovementModel = MovementModel.Parkour }
            .RecordedMovementProfileVersion == null, "Pre77 cannot acquire a Parkour profile attribution.");
        foreach (var type in new[] { PacketType.MatchState, PacketType.SessionState })
        {
            int size = type == PacketType.MatchState ? MatchStatePacket.Size : SessionStatePacket.Size;
            int offset = type == PacketType.MatchState ? 104 : 1 + SessionStatePacket.Protocol28Size;
            byte[] packet = new byte[1 + size]; packet[0] = (byte)type;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), 524288u);
            Reject(packet, 76);
            Check(ReplayIdentityCompatibility.Convert(packet, 77).SequenceEqual(packet),
                "Protocol77 retains Parkour rule without changing canonical packet width.");
            Check(ReplayIdentityCompatibility.Convert(packet, 81).SequenceEqual(packet),
                "Historical native81 preserves Parkour v4 rule bytes.");
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), 524288u | 1048576u);
            foreach (int protocol in new[] { 80, 81, 82 })
            {
                bool rejectedPackage = false;
                try { ReplayIdentityCompatibility.Convert(packet, protocol); }
                catch (InvalidDataException) { rejectedPackage = true; }
                Check(rejectedPackage, "Custom/package replay remains refused at protocol" + protocol);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), 0);
            Check(ReplayIdentityCompatibility.Convert(packet, 76).SequenceEqual(packet),
                "Protocol76 default rules remain byte-identical.");
        }
        Check(PlayerState.Protocol80Size == 173 && PlayerState.Size == 192 && PlayerState.Protocol48Size == 145
            && SnapshotFast.PlayerSize == 138 && SnapshotHeader.Size == 23,
            "Grapple appends19 bytes while historical tuck and fast widths remain fixed.");
        byte[] snapshot = new byte[1 + SnapshotHeader.Size + 173 + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        snapshot[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { PlayerCount = 1 }.Write(snapshot.AsSpan(1));
        byte[] currentPlayer = new byte[192];
        new PlayerState { SlotIndex = 0, Health = 99, Crouched = false }.Write(currentPlayer);
        currentPlayer.AsSpan(0, 173).CopyTo(snapshot.AsSpan(1 + SnapshotHeader.Size));
        foreach (int protocol in new[] { 68, 69, 76, 77, 78, 79, 80, 81 })
        {
            var converted = ReplayIdentityCompatibility.Convert(snapshot, protocol);
            Check(converted.Length == snapshot.Length + 19
                && converted.Slice(1 + SnapshotHeader.Size, 173).SequenceEqual(snapshot.AsSpan(1 + SnapshotHeader.Size, 173))
                && converted.Slice(1 + SnapshotHeader.Size + 173, 19).IndexOfAnyExcept((byte)0) < 0
                && !PlayerState.Read(converted.Slice(1 + SnapshotHeader.Size, 192)).Crouched,
                $"Protocol{protocol} default stance stays untucked; no synthetic stance is invented.");
        }
        var tuckedState = new PlayerState { SlotIndex = 0, Health = 99, Crouched = true, Tucked = true };
        tuckedState.Write(currentPlayer);
        currentPlayer.AsSpan(0, 173).CopyTo(snapshot.AsSpan(1 + SnapshotHeader.Size));
        byte[] liveSnapshot = new byte[snapshot.Length + 19];
        snapshot.AsSpan(0, 1 + SnapshotHeader.Size + 173).CopyTo(liveSnapshot);
        snapshot.AsSpan(1 + SnapshotHeader.Size + 173).CopyTo(liveSnapshot.AsSpan(1 + SnapshotHeader.Size + 192));
        Check(ReplayIdentityCompatibility.Convert(liveSnapshot, 82).SequenceEqual(liveSnapshot),
            "Current82 complete snapshot is byte-exact without historical reinterpretation.");
        foreach (bool fast in new[] { false, true })
        {
            byte[] packet = snapshot;
            if (fast)
            {
                packet = new byte[1 + SnapshotHeader.Size + SnapshotFast.PlayerSize + SnapshotFast.ReceiptSize];
                packet[0] = (byte)PacketType.SnapshotFast;
                SnapshotFast.Write(liveSnapshot.AsSpan(1), packet.AsSpan(1));
            }
            PlayerState Decode(ReadOnlySpan<byte> value) => fast
                ? PlayerState.ReadFast(value[(1 + SnapshotHeader.Size)..],
                    snapshot.AsSpan(1 + SnapshotHeader.Size + 41, 7))
                : PlayerState.Read(value[(1 + SnapshotHeader.Size)..]);
            var current = Decode(ReplayIdentityCompatibility.Convert(packet, 77));
            Check(current.Crouched && current.Tucked, "Protocol77 full/fast snapshot retains accepted tuck.");
            foreach (int protocol in new[] { 68, 69, 76 })
            {
                var old = Decode(ReplayIdentityCompatibility.Convert(packet, protocol));
                Check(!old.Tucked && old.Crouched == (protocol >= 69),
                    "Historical full/fast snapshots clear tuck while preserving protocol69 crouch semantics.");
            }
            byte[] malformed = (byte[])packet.Clone();
            int auxiliary = 1 + SnapshotHeader.Size + 54 + DamageEvent.Size * PlayerState.DamageHistory - (fast ? 7 : 0);
            malformed[auxiliary] &= 0xfb; // A tucked flag without accepted crouch is invalid.
            var normalized = Decode(ReplayIdentityCompatibility.Convert(malformed, 77));
            Check(!normalized.Crouched && !normalized.Tucked, "Malformed tuck without crouch normalizes on decode.");
        }
        foreach (bool slotted in new[] { false, true })
        {
            int prefix = slotted ? 2 : 1;
            byte[] packet = new byte[prefix + IntentPacket.FullSize];
            packet[0] = (byte)(slotted ? PacketType.SlotIntent : PacketType.Intent);
            var buttons = IntentButtons.Crouch | IntentButtons.CrouchedState | IntentButtons.TuckedState
                | (IntentButtons)(13u << 25);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(prefix + 4), (uint)buttons);
            var current = ReplayIdentityCompatibility.Convert(packet, 77);
            Check(IntentPacket.Read(current[prefix..]).Buttons == buttons,
                "Protocol77 intent retains tuck and four-bit cumulative pivot count.");
            foreach (int protocol in new[] { 68, 69, 76 })
            {
                var old = IntentPacket.Read(ReplayIdentityCompatibility.Convert(packet, protocol)[prefix..]);
                var expected = protocol < 69 ? 0 : IntentButtons.Crouch | IntentButtons.CrouchedState;
                Check(old.Buttons == expected, "Historical intent clears tuck/counter and preserves protocol69 crouch.");
            }
        }
        Check(ParkourMovementProfileV1.ProfileVersion == 1
            && ParkourMovementProfileV2.ProfileVersion == 2
            && ParkourMovementProfileV3.ProfileVersion == 3
            && ParkourMovementProfileV4.ProfileVersion == 4
            && ParkourMovementProfileV5.ProfileVersion == 5,
            "Every immutable Parkour profile retains its recorded identity.");
        foreach (bool grounded in new[] { false, true })
        foreach (bool crouched in new[] { false, true })
        foreach (bool sliding in new[] { false, true })
        foreach (Vector3 velocity in new[] { Vector3.Zero, new Vector3(12, -4, 17) })
        {
            var input = new MomentumInput(new Vector2(.6f, .8f), Vector3.UnitZ, Vector3.UnitX,
                ParkourMovementProfileV4.GroundSpeed, grounded, 1f / 60);
            Check(ParkourMovementProfileV4.Step(velocity, input, crouched, sliding)
                == ParkourMovementProfileV3.Step(velocity, input, crouched, sliding),
                "Mantle profile retains exact v3 horizontal policy across stance/contact states.");
            Check(ParkourMovementProfileV5.Step(velocity, input, crouched, sliding)
                == ParkourMovementProfileV4.Step(velocity, input, crouched, sliding),
                "Grapple profile retains exact v4 horizontal policy across stance/contact states.");
        }
        var displacement = new Vector3(2, ParkourStance.TuckRise + 3, 4);
        var oneLanding = ParkourStance.PhysicalTravel(displacement, 5, 6, out bool validOne);
        Check(validOne && (oneLanding - new Vector3(2, 3, 4)).Length < .00001f,
            "One landing subtracts exactly one stance pivot from physical displacement.");
        var wrapped = ParkourStance.PhysicalTravel(displacement, 15, 0, out bool validWrap);
        Check(validWrap && (wrapped - oneLanding).Length < .00001f, "Pivot counter wraps15 to0.");
        Check(ParkourStance.PhysicalTravel(displacement, 6, 6, out bool validZero) == displacement && validZero,
            "No pivot preserves actual physical travel.");
        ParkourStance.PhysicalTravel(displacement, 5, 7, out bool validMany);
        Check(!validMany, "Multiple pivot deltas are discontinuities rather than valid physical velocity.");
        Console.WriteLine($"Parkour compatibility: {checks} checks passed.");
        return 0;
    }
}
