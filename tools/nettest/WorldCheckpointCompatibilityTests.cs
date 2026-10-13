using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class WorldCheckpointCompatibilityTests
{
    private static ReplayReplicaState State(bool grant = false)
    {
        var state = new ReplayReplicaState();
        var match = new MatchStatePacket { MatchId = 2, AuthorityEpoch = 9,
            RoomKey = "unit-world-contract", Mode = (byte)GameMode.Battle };
        var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        match.Write(packet.AsSpan(1)); state.Accept(packet, 24);
        if (grant)
        {
            var life = new ShotKey(9, 2, 0, 4, 5, 0);
            state.TimedGrants.Accept(new(life, 7, 1, TimedPowerUp.DoubleDamage, 12, -1, 8,
                20, 1820, TimedGrantEnd.Active, 24), 24);
        }
        return state;
    }
    private static byte[] TouchAppendix() => NativeTouchCheckpointTests.WriteOwnedAppendix();
    private static byte[] PickupAppendix()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(1); writer.Write(0); // one graph-item row, graph ID zero
        writer.Write(9UL); writer.Write((ushort)2); writer.Write(7U); writer.Write(-1); writer.Write(8U); writer.Write(true);
        return stream.ToArray();
    }
    // Source-qualified envelope fixture: optional codecs are production writers,
    // while its graph has one typed placeholder. It validates framing/capability
    // contracts without an asset-loaded Scene; it cannot certify a rendered seek.
    private static byte[] Envelope(ushort version, byte features, byte[] construction, byte[] decoder,
        byte[]? touch, byte[]? pickups)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Component(byte[] value) { writer.Write(value.Length); writer.Write(value); }
        writer.Write(0x43575050U); writer.Write(version); if (version >= 10) writer.Write(features);
        writer.Write(ReplayWorldCheckpoint.ComputeContract(stable: true));
        writer.Write("unit-world-contract"); writer.Write((int)GameMode.Battle); writer.Write(17UL);
        writer.Write(24U); writer.Write(123U); writer.Write(456U);
        Component(construction); Component(decoder);
        var types = (Type[])typeof(ReplayWorldCheckpoint).GetField("ObjectTypes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        writer.Write(1); writer.Write(checked((ushort)Array.IndexOf(types, typeof(ItemInstanceEntity))));
        writer.Write(0UL); Component(Array.Empty<byte>());
        for (int i = 0; i < 5; i++) Component(Array.Empty<byte>()); // asset/cosmetic/bomb/traversal/form components
        if (touch != null) Component(touch); if (pickups != null) Component(pickups);
        writer.Write(false); return stream.ToArray();
    }
    internal static void Run(Action<bool, string> check)
    {
        var foreground = GameState.Current; var random = Rng.Current; var authority = NetPowerUpLedger.Ledger;
        byte[] touch = TouchAppendix(), pickups = PickupAppendix();
        var empty = State().CaptureCheckpoint();
        byte[] main = ReplicaCheckpointCompatibilityTests.HistoricalFixture(empty, touch: true, pickups: false);
        byte[] staged = ReplicaCheckpointCompatibilityTests.HistoricalFixture(State(grant: true).CaptureCheckpoint(),
            touch: false, pickups: true, version: 11, protocol: 54, grants: true);
        byte[] mainWorld = Envelope(9, 0, main, main, touch, null);
        byte[] stagedWorld = Envelope(9, 0, staged, staged, null, pickups);
        using (var source = ReplayWorldCheckpoint.FromBytes(mainWorld))
        {
            check(source.HasNativeTouchContract && source.RequiresNativeTouchContract && !source.HasTimedPickupContract,
                "historical main world v9 is actual touch-only source contract, with no fabricated pickup appendix");
            var decoded = new ReplayReplicaState(); decoded.RestoreCheckpoint(source.ConstructionState());
            check(!decoded.TimedGrants.HasCoverage && source.ClipMetadata(0, Array.Empty<ReplayPlayerInfo>()).RoomKey == "unit-world-contract",
                "historical main world header retains clip metadata and unknown grants");
        }
        using (var source = ReplayWorldCheckpoint.FromBytes(stagedWorld))
        {
            check(!source.HasNativeTouchContract && !source.RequiresNativeTouchContract && source.HasTimedPickupContract,
                "staged A3 world v9 is pickup-only source contract, never inferred native touch from protocol54");
            var decoded = new ReplayReplicaState(); decoded.RestoreCheckpoint(source.ConstructionState());
            check(decoded.TimedGrants.HasCoverage && decoded.TimedGrants.View(new(9, 2, 0, 4, 5, 0), TimedPowerUp.DoubleDamage, 24).ConfirmedRemaining > 0,
                "staged A3 world preserves actual private confirmed grant rather than fabricated absence");
        }
        var combined = State(grant: true).CaptureCheckpoint().Bytes.ToArray();
        byte[] currentWorld = Envelope(10, 3, combined, combined, touch, pickups);
        using var current = ReplayWorldCheckpoint.FromBytes(currentWorld);
        check(current.HasNativeTouchContract && current.HasTimedPickupContract && current.RequiresNativeTouchContract,
            "new world v10 declares and validates ordered native-touch and pickup appendices");
        check(current.ConstructionState().Bytes.SequenceEqual(combined)
            && current.ClipMetadata(0, Array.Empty<ReplayPlayerInfo>()).WorldCheckpoint!.AsSpan().SequenceEqual(currentWorld),
            "new flagged header preserves exact construction state and clip capsule bytes");
        void Reject(byte[] bytes, string label)
        {
            bool rejected = false;
            try { using var invalid = ReplayWorldCheckpoint.FromBytes(bytes); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { rejected = true; }
            check(rejected && current.Bytes.SequenceEqual(currentWorld)
                && ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(random, Rng.Current)
                && ReferenceEquals(authority, NetPowerUpLedger.Ledger), label);
        }
        Reject(Envelope(9, 0, main, staged, null, pickups), "contradictory historical embedded contracts cannot reinterpret touch as pickup");
        Reject(Envelope(9, 0, combined, combined, touch, pickups), "unqualified intermediate world v9 with both appendices is rejected explicitly");
        Reject(Envelope(9, 0, main, main, touch, pickups), "old touch contract rejects an unexpected second appendix");
        Reject(Envelope(9, 0, staged, staged, touch, null), "old pickup contract cannot interpret native touch bytes as identities");
        Reject(Envelope(10, 0x83, combined, combined, touch, pickups), "unknown tagged world feature bits rejected before publication");
        Reject(Envelope(10, 3, combined, combined, pickups, touch), "swapped current appendices cannot pass by lengths or numeric versions");
        var badTouch = touch.ToArray(); badTouch[0] = 2;
        Reject(Envelope(10, 3, combined, combined, badTouch, pickups), "noncanonical native touch flags rejected without mutating a scene or existing capsule");
        badTouch = touch.ToArray(); BinaryPrimitives.WriteSingleLittleEndian(badTouch.AsSpan(14), float.NaN);
        Reject(Envelope(10, 3, combined, combined, badTouch, pickups), "nonfinite native touch sample rejected transactionally");
        var badPickup = pickups.ToArray(); badPickup[^1] = 2;
        Reject(Envelope(10, 3, combined, combined, touch, badPickup), "noncanonical hidden-item field rejected before world restoration");
        var wrongScope = pickups.ToArray(); BinaryPrimitives.WriteUInt64LittleEndian(wrongScope.AsSpan(8), 10);
        Reject(Envelope(10, 3, combined, combined, touch, wrongScope), "pickup appendix must match validated private decoder epoch");
        foreach (int length in new[] { 0, 5, 6, 7, currentWorld.Length / 2, currentWorld.Length - 1 })
            Reject(currentWorld.AsSpan(0, length).ToArray(), "truncated world framing rejected at length " + length);
        Reject(currentWorld.Concat(new byte[] { 0 }).ToArray(), "trailing world bytes cannot hide an appendix or presentation marker");
        check(ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(random, Rng.Current)
            && ReferenceEquals(authority, NetPowerUpLedger.Ledger), "all world envelope compatibility is detached from live facade and grant owners");
    }
}
