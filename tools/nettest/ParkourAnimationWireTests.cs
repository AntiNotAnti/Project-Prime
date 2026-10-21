using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Literal published widths and byte offsets. The final fixture exercises only
// real presentation admission and a controlled scene clock, with no simulation,
// assets, authority/controller injection, transport or rendering.
internal static class ParkourAnimationWireTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }
    private static bool Refused(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or EndOfStreamException)
        { return true; }
    }

    internal static int Run()
    {
        try
        {
            _checks = 0;
            IntentCodecs(); InvalidIntents(); PlayerLanes(); HistoricalPackets(); Checkpoints(); PoseSamples(); ClockRegression();
            Console.WriteLine($"PARKOUR ANIMATION WIRE PASS {_checks} byte/MTU/history/transaction assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("PARKOUR ANIMATION WIRE FAIL " + error); return 1; }
    }

    private static IntentPacket Intent(byte phase) => new() {
        MatchId = 7, AuthorityEpoch = 8, SlotGeneration = 2, LifeId = 3, Frame = 100,
        Aim = Vector3.UnitZ, Position = new(1, 2, 3), Target = NetTargetIdentity.None,
        Buttons = IntentButtons.InPlayState | IntentButtons.MoveUp | IntentButtons.Grapple,
        GrappleRequestNonce = 0xbeef, MantleVisualPhase = phase };

    private static void IntentCodecs()
    {
        Check(NetConfig.ProtocolVersion == 84 && IntentPacket.Protocol80TouchSize == 1296
            && IntentPacket.Protocol82TouchSize == 1298 && IntentPacket.TouchCanonicalSize == 1299
            && CompactIntentCodec.HeaderSize == 136 && FireEvent.Size == 74,
            "literal84 phase extension preserves published touch/nonce/fire widths");
        for (byte phase = 0; phase <= 15; phase++)
        foreach (bool native in new[] { false, true })
        for (byte count = 0; count <= 14; count++)
        {
            var intent = Intent(phase); intent.FireEventCount = count;
            if (native) intent.NativeTouch = new(true, true, true, 100, -25, 7, false);
            for (int i = 0; i < count; i++)
                intent.FireEvents[i] = new((uint)(i + 1), 100, 90, 128, FireEventKind.PressFire, 0, 17, 0,
                    FireEvent.FlagPose | FireEvent.FlagReticle, new(3, 4, 5), Vector3.UnitZ,
                    Vector3.UnitX, Vector3.UnitY, new(0, 1), SourceLife: 3);
            byte[] old = new byte[1298], canonical = Enumerable.Repeat((byte)0xcc, 1303).ToArray();
            intent.Write(old); intent.Write(canonical.AsSpan(0, 1299));
            Check(canonical.AsSpan(0, 1298).SequenceEqual(old) && canonical[1298] == phase
                && canonical[1296] == 0xef && canonical[1297] == 0xbe
                && canonical.AsSpan(1299).IndexOfAnyExcept((byte)0xcc) < 0,
                $"canonical1299 literal prefix/nonce/phase/guard {phase}/{native}/{count}");
            var decoded = IntentPacket.Read(canonical.AsSpan(0, 1299));
            Check(decoded.MantleVisualPhase == phase && decoded.GrappleRequestNonce == 0xbeef
                && decoded.Buttons == intent.Buttons && decoded.NativeTouch == intent.NativeTouch
                && decoded.FireEventCount == count, "canonical roundtrip does not recast phase as input");
            Check(IntentPacket.Read(old).MantleVisualPhase == 0, "published1298 read defaults phase to None");

            byte[] compact = Enumerable.Repeat((byte)0xcc, 1201).ToArray();
            int written = CompactIntentCodec.Write(intent, compact);
            int eventAt = 136 + (native && count < 14 ? 9 : 0);
            Check(written == 139 + (native && count < 14 ? 9 : 0) + 74 * count
                && written == CompactIntentCodec.Size(intent)
                && compact[written] == 0xcc && compact[written - 1] == phase
                && compact[written - 3] == 0xef && compact[written - 2] == 0xbe,
                "compact frozen event rows and final nonce/phase trailer");
            Check(written + 24 + 1 <= 1200 && (count != 14 || written + 24 + 1 == 1200),
                "14-fire SlotIntent including slot and transport is exactly1200 bytes");
            Check(compact.AsSpan(0, 103).SequenceEqual(canonical.AsSpan(0, 103))
                && compact.AsSpan(eventAt, count * 74).SequenceEqual(canonical.AsSpan(103, count * 74)),
                "compact retains original control prefix and each independent74-byte fire event");
            Check(CompactIntentCodec.TryRead(compact.AsSpan(0, written), out decoded)
                && decoded.MantleVisualPhase == phase && decoded.GrappleRequestNonce == 0xbeef
                && decoded.Buttons == intent.Buttons && decoded.FireEventCount == count
                && (count == 0 || decoded.FireEvents[count - 1] == intent.FireEvents[count - 1]),
                "compact phase roundtrip preserves control and combat identity");
            Check(!native || decoded.NativeTouch.Active && (count == 14 ? decoded.NativeTouch.Suppressed : decoded.NativeTouch == intent.NativeTouch),
                "phase byte preserves full-combat native-touch suppression policy");
            Check(!CompactIntentCodec.TryRead(compact.AsSpan(0, written - 1), out _)
                && !CompactIntentCodec.TryRead(compact.AsSpan(0, written + 1), out _), "compact exact length enforced");
        }
    }

    private static void InvalidIntents()
    {
        var valid = Intent(7); byte[] good = new byte[1299]; valid.Write(good);
        byte[] packed = new byte[1200]; int length = CompactIntentCodec.Write(valid, packed);
        for (int phase = 16; phase <= 255; phase++)
        {
            var invalid = valid; invalid.MantleVisualPhase = (byte)phase;
            byte[] destination = Enumerable.Repeat((byte)0xa5, 1303).ToArray();
            Check(Refused(() => invalid.Write(destination.AsSpan(0, 1299))) && destination.All(b => b == 0xa5),
                "canonical invalid phase write is atomic " + phase);
            Check(Refused(() => CompactIntentCodec.Write(invalid, destination)) && destination.All(b => b == 0xa5)
                && Refused(() => CompactIntentCodec.Size(invalid)), "compact invalid phase preflight is atomic " + phase);
            Check(Refused(() => new PlayerState { MantleVisualPhase = (byte)phase }.Write(destination))
                && destination.All(b => b == 0xa5), "canonical player invalid phase write is atomic " + phase);
            byte[] bad = good.ToArray(); bad[1298] = (byte)phase; var read = valid;
            Check(Refused(() => read = IntentPacket.Read(bad)) && read.MantleVisualPhase == 7
                && read.Frame == valid.Frame && bad.AsSpan(0, 1298).SequenceEqual(good.AsSpan(0, 1298)),
                "canonical invalid phase read refuses before replacing caller value " + phase);
            byte[] badPacked = packed.AsSpan(0, length).ToArray(); badPacked[^1] = (byte)phase;
            Check(!CompactIntentCodec.TryRead(badPacked, out var rejected) && rejected.Frame == 0
                && rejected.MantleVisualPhase == 0 && rejected.GrappleRequestNonce == 0 && rejected.FireEventCount == 0,
                "compact invalid phase returns an entirely uninstalled result " + phase);
        }
        byte[] shortDestination = Enumerable.Repeat((byte)0xa5, 138).ToArray();
        Check(Refused(() => CompactIntentCodec.Write(valid, shortDestination)) && shortDestination.All(b => b == 0xa5),
            "compact short write preflight leaves destination unchanged");
    }

    private static byte[] Snapshot(byte phase, byte count = 1)
    {
        byte[] bytes = new byte[23 + count * 192 + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = 100, PlayerCount = count }.Write(bytes);
        for (byte slot = 0; slot < count; slot++)
            new PlayerState { SlotIndex = slot, SlotGeneration = 2, LifeId = 3, Flags = PlayerState.FlagSpawned,
                Health = 80, Facing = Vector3.UnitZ, Crouched = true, Tucked = true, SpawnProtected = true,
                HalfturretActive = true, MantleVisualPhase = phase }.Write(bytes.AsSpan(23 + slot * 192));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - NetHealthSync.HeaderSize), 7);
        return bytes;
    }

    private static void PlayerLanes()
    {
        Check(54 + DamageEvent.Size * PlayerState.DamageHistory == 114 && PlayerState.Size == 192
            && SnapshotFast.PlayerSize == 138 && SnapshotFast.MaximumEncodedSize == 1172
            && SnapshotFast.BootstrapMaximumEncodedSize == 1151, "published auxiliary114 and unchanged fast/bootstrap MTUs");
        for (byte phase = 0; phase <= 15; phase++)
        {
            byte[] canonical = Snapshot(phase, 8); var lanes = new NetReplicationLanes(); lanes.Prepare(canonical);
            Check(lanes.FastLength + 24 == 1172, "eight-player fast lane retains1172-byte transport size");
            var receiver = new NetReplicationReceiver(); var players = new PlayerState[8];
            Check(receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 7, 8, out _), "phase fast lane decodes");
            int bootstrapLength = 23 + 8 * 138; byte[] assembled = new byte[4096];
            int size = new NetReplicationReceiver().Assemble(lanes.Fast.AsSpan(0, bootstrapLength), assembled, 7, 8);
            Check(size == canonical.Length, "bootstrap assembly remains canonical192 plus existing world tail");
            for (int slot = 0; slot < 8; slot++)
            {
                Check(canonical[23 + slot * 192 + 114] == (byte)((phase << 4) | 15)
                    && lanes.Fast[23 + slot * 138 + 107] == (byte)((phase << 4) | 15),
                    "literal high nibble phase preserves all four existing auxiliary flags");
                Check(players[slot].MantleVisualPhase == phase && players[slot].Crouched && players[slot].Tucked
                    && players[slot].SpawnProtected && players[slot].HalfturretActive,
                    "live fast phase and old auxiliary flag roundtrip");
                Check(PlayerState.Read(assembled.AsSpan(23 + slot * 192, 192)).MantleVisualPhase == phase,
                    "bootstrap carries phase without a new lane or width");
            }
        }
    }

    private static void HistoricalPackets()
    {
        foreach (int protocol in new[] { 82, 83 })
        foreach (bool slotted in new[] { false, true })
        {
            int prefix = slotted ? 2 : 1; byte[] packet = new byte[prefix + 1298];
            packet[0] = (byte)(slotted ? PacketType.SlotIntent : PacketType.Intent); if (slotted) packet[1] = 0;
            var intent = Intent(0); intent.NativeTouch = new(true, true, true, 100, -25, 7, false); intent.Write(packet.AsSpan(prefix));
            byte[] old = packet.ToArray(); var upgraded = ReplayIdentityCompatibility.Convert(packet, protocol);
            Check(upgraded.Length == prefix + 1299 && upgraded[..(prefix + 1298)].SequenceEqual(old)
                && upgraded[^1] == 0 && IntentPacket.Read(upgraded[prefix..]).GrappleRequestNonce == 0xbeef
                && packet.SequenceEqual(old), "old82/83 intent expands only zero phase and preserves original bytes");
        }
        foreach (int protocol in new[] { 82, 83 })
        foreach (bool fast in new[] { false, true })
        {
            byte[] canonical = Snapshot(15, 8); int length;
            byte[] payload;
            if (fast) { payload = new byte[1200]; length = SnapshotFast.Write(canonical, payload); }
            else { payload = canonical; length = canonical.Length; }
            byte[] packet = new byte[1 + length]; packet[0] = (byte)(fast ? PacketType.SnapshotFast : PacketType.Snapshot);
            payload.AsSpan(0, length).CopyTo(packet.AsSpan(1)); byte[] original = packet.ToArray();
            byte[] expected = original.ToArray(); int stride = fast ? 138 : 192, auxiliary = fast ? 107 : 114;
            for (int slot = 0; slot < 8; slot++) expected[24 + slot * stride + auxiliary] &= 15;
            Check(ReplayIdentityCompatibility.Convert(packet, protocol).SequenceEqual(expected) && packet.SequenceEqual(original),
                "old82/83 reserved auxiliary nibble is cleared in full/fast without mutating archive");
            Check(ReplayIdentityCompatibility.Convert(packet, 84).SequenceEqual(original), "current84 accepted phase survives exact packet boundary");
        }
    }

    private static ReplayReplicaState Seed(byte phase, ReplayCustomWeaponContext? custom)
    {
        var state = new ReplayReplicaState();
        if (custom != null) state.EnableCustomInspection(ReplayCustomWeaponDescriptor.ForInspection(custom));
        var match = new MatchStatePacket { MatchId = 7, AuthorityEpoch = 8, RoomKey = "MP3 PROVING GROUND", Mode = (byte)GameMode.Battle,
            RuleBits = custom == null ? 0 : (uint)(MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable) };
        byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState; match.Write(packet.AsSpan(1)); state.Accept(packet, 1);
        if (custom != null)
        {
            state.Accept(ReplayCustomWeaponFact.Bootstrap(custom).CopyBytes(), 10);
            var slots = new CustomReplicaSlot[8];
            for (byte slot = 0; slot < 8; slot++) slots[slot] = new(slot, false, 0, 0, false, false, default, 0, 0, default);
            state.Accept(ReplayCustomWeaponFact.Baseline(custom, 2, new(7, 8, custom.Bootstrap.Packages.MechanicsDigest, 1001, 8, 0), slots, Array.Empty<CustomReplicaFlight>()).CopyBytes(), 11);
            for (int i = 0; i < custom.Layout.Count; i++)
                state.Accept(ReplayCustomWeaponFact.Pickup(custom, state.CustomWeapons!.Sequence + 1, 1001, i,
                    new(new(custom.PickupWorld, custom.Layout.Placements[i].SpawnId, 1), 990, true, 0)).CopyBytes(), 12);
        }
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 8; roster.Revision = 1;
        roster.Count = 1; roster.Slots[0] = 0; roster.Generations[0] = 2; roster.Names[0] = "phase-owner";
        packet = new byte[1 + RosterPacket.Size]; packet[0] = (byte)PacketType.Roster; roster.Write(packet.AsSpan(1)); state.Accept(packet, 13);
        byte[] snapshot = Snapshot(phase); packet = new byte[1 + snapshot.Length]; packet[0] = (byte)PacketType.Snapshot; snapshot.CopyTo(packet, 1); state.Accept(packet, 14);
        packet = new byte[2 + 1299]; packet[0] = (byte)PacketType.SlotIntent; Intent(phase).Write(packet.AsSpan(2)); state.Accept(packet, 15);
        return state;
    }

    private static void Checkpoints()
    {
        var package = WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
        using var registry = new WeaponRegistry(true, new[] { package.Definition });
        var manifest = new CustomWeaponManifest(new[] { package.Definition });
        var layout = new CustomPickupLayout(MapContentIdentity.BuiltIn("MP3 PROVING GROUND"), manifest, registry,
            StockRiftlinePickups.ForRoom("MP3 PROVING GROUND", package.Definition));
        WorldBootstrapIdentity world = new(new(7, 8, 3), 2, 5, 990, 1, 1);
        var context = new ReplayCustomWeaponContext(new(world, 42, layout.Digest, new(new[] { package })), package.Definition, layout);
        foreach (bool custom in new[] { false, true })
        {
            void Restore(ReplayReplicaState target, byte[] bytes)
            { if (custom) target.RestoreCustomCheckpoint(new(bytes)); else target.RestoreCheckpoint(new(bytes)); }
            for (byte phase = 0; phase <= 15; phase++)
            {
                var state = Seed(phase, custom ? context : null); byte[] bytes = state.CaptureCheckpoint().Bytes.ToArray();
                Check(bytes[4] == (custom ? 17 : 16) && bytes[6] == 84 && bytes[7] == (custom ? 119 : 87), "current checkpoint explicit16/17 phase64 contract");
                var restored = new ReplayReplicaState(); Restore(restored, bytes);
                Check(restored.CaptureCheckpoint().Bytes.SequenceEqual(bytes) && restored.TryGetPlayer(0, out var player)
                    && player.MantleVisualPhase == phase && restored.TryGetIntent(0, out var intent) && intent.MantleVisualPhase == phase
                    && intent.GrappleRequestNonce == 0xbeef, "checkpoint exact phase/nonce/native/custom roundtrip");
                if (phase != 0) continue;
                byte[] historical = Protocol83CheckpointFixtures.ToProtocol83(bytes);
                int at = Protocol83CheckpointFixtures.FirstActorOffset(bytes), phaseAt = at + 7 + 192 + 1 + 1298;
                historical[at + 7 + 114] |= 0xf0; // historical reserved bits must never become animation
                foreach (byte protocol in custom ? new byte[] { 83 } : new byte[] { 82, 83 })
                {
                    historical[6] = protocol; var old = new ReplayReplicaState(); Restore(old, historical);
                    Check(old.TryGetPlayer(0, out player) && player.MantleVisualPhase == 0
                        && old.TryGetIntent(0, out intent) && intent.MantleVisualPhase == 0 && intent.GrappleRequestNonce == 0xbeef
                        && old.CaptureCheckpoint().Bytes.SequenceEqual(bytes), "native14/custom15 clears reserved auxiliary while retaining all archived facts");
                }
                for (int invalid = 16; invalid <= 255; invalid++)
                {
                    byte[] bad = bytes.ToArray(); bad[phaseAt] = (byte)invalid;
                    Check(Refused(() => Restore(restored, bad)) && restored.CaptureCheckpoint().Bytes.SequenceEqual(bytes),
                        "malformed checkpoint phase refuses transactionally " + custom + "/" + invalid);
                }
                foreach (int end in new[] { phaseAt, phaseAt + 1, bytes.Length - 1 })
                    Check(Refused(() => Restore(restored, bytes[..end])) && restored.CaptureCheckpoint().Bytes.SequenceEqual(bytes), "truncated phase checkpoint rollback");
                byte[] contradictory = bytes.ToArray(); contradictory[7] &= 0xbf;
                Check(Refused(() => Restore(restored, contradictory)) && restored.CaptureCheckpoint().Bytes.SequenceEqual(bytes), "current16/17 cannot masquerade as old features without phase64");
            }
        }
    }

    private static void PoseSamples()
    {
        Check(ParkourAnimationPose.EncodeMantle(-1) == 0 && ParkourAnimationPose.EncodeMantle(float.NaN) == 0
            && ParkourAnimationPose.EncodeMantle(float.PositiveInfinity) == 0 && ParkourAnimationPose.EncodeMantle(0) == 1
            && ParkourAnimationPose.EncodeMantle(1) == 15 && ParkourAnimationPose.EncodeMantle(10) == 15,
            "pure phase encoder reservesNone and clamps finite active endpoints");
        for (int phase = 0; phase <= 255; phase++)
        {
            float progress = ParkourAnimationPose.DecodeMantle((byte)phase);
            if (phase is < 1 or > 15) { Check(progress == -1, "all unused phase codes decode to absent"); continue; }
            Check(progress >= 0 && progress <= 1 && ParkourAnimationPose.EncodeMantle(progress) == phase,
                "all15 quantized climb progress states roundtrip without a second clock");
            foreach (float grapple in new[] { 0f, .5f, 1f })
            foreach (double seconds in new[] { 0d, 1 / 60d, 1e12 })
            {
                var pose = new ParkourAnimationPose(progress, grapple, Vector3.UnitY, seconds);
                var offset = pose.GunOffset;
                Check(float.IsFinite(pose.MantleWeight) && pose.MantleWeight >= 0 && pose.MantleWeight <= 1
                    && float.IsFinite(offset.X) && float.IsFinite(offset.Y) && float.IsFinite(offset.Z), "finite pure pose sample at every quantized phase");
                if (phase is 1 or 15) Check(pose.MantleWeight == 0 && (grapple != 0 || offset == Vector3.Zero), "climb endpoints blend exactly back to native pose");
            }
        }
    }

    private static void ClockRegression()
    {
        Check(!NetSession.Active, "clock fixture never creates or impersonates authority");
        Headless.Enter();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var clock = typeof(Scene).GetField("_frameCount", hidden)!;
        var received = typeof(PlayerEntity).GetField("_modMantleVisualReceived", hidden)!;
        var resetLife = typeof(PlayerEntity).GetMethod("ModResetParkourGrappleForLife", hidden)!;
        var foreground = GameState.Current; var registry = PlayerEntity.LegacyRegistry; var random = Rng.Current;
        Scene? live = null, replica = null;
        var host = new PassiveReplaySessionHost(); using var session = new ReplayPlaybackSession(host);
        try
        {
            live = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            live.GameState.MovementModel = MovementModel.Parkour;
            // Slot1 is a genuine non-owning actor; only replicated presentation
            // state is applied. No mantle solver or accepted authority is forged.
            var player = live.Players.Items[1]; player.IsBot = false; player.Hunter = Hunter.Samus;
            player.Health = 100; player.LoadFlags = LoadFlags.Active | LoadFlags.SlotActive | LoadFlags.Spawned;
            Vector3 position = player.Position, speed = player.Speed;
            clock.SetValue(live, 1000UL); player.ModApplyReplicatedMantleVisual(8, 100);
            Check(player.ModPublishedMantleVisualPhase == 8 && (ulong)received.GetValue(player)! == 1000,
                "first accepted presentation starts live lease on current scene clock");
            clock.SetValue(live, 1030UL); player.ModApplyReplicatedMantleVisual(8, 100);
            Check(player.ModPublishedMantleVisualPhase == 8 && (ulong)received.GetValue(player)! == 1000,
                "cached same snapshot retains inclusive30-tick boundary without refreshing receipt");
            clock.SetValue(live, 1031UL); player.ModApplyReplicatedMantleVisual(15, 100);
            player.ModApplyReplicatedMantleVisual(15, 99);
            Check(player.ModPublishedMantleVisualPhase == 0 && (ulong)received.GetValue(player)! == 1000,
                "duplicate or older cached state cannot resurrect expired live climb pose");
            player.ModApplyReplicatedMantleVisual(9, 101);
            Check(player.ModPublishedMantleVisualPhase == 9, "new accepted source renews live presentation");
            player.ModApplyReplicatedMantleVisual(0, 102);
            Check(player.ModPublishedMantleVisualPhase == 0, "new explicitNone immediately clears live pose");
            player.ModApplyReplicatedMantleVisual(7, 103, local: true);
            Check(player.ModPublishedMantleVisualPhase == 0, "same-life live owner correction cannot install presentation claim");
            player.ModApplyReplicatedMantleVisual(7, 103); resetLife.Invoke(player, null);
            Check(player.ModPublishedMantleVisualPhase == 0, "actual existing life-reset hook retires prior pose source and lease");
            clock.SetValue(live, 2000UL); player.ModApplyReplicatedMantleVisual(6, 1);
            Check(player.ModPublishedMantleVisualPhase == 6 && player.Position == position && player.Speed == speed
                && player.ModGrappleGeometryQueryCount == 0 && player.ModGrappleForceCount == 0,
                "fresh life accepts restarted source without changing movement or issuing geometry queries");

            replica = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, new ReplaySceneServices(session, host.State), initializeRuntime: false);
            replica.GameState.MovementModel = MovementModel.Parkour;
            var passive = replica.Players.Items[0]; passive.Hunter = Hunter.Samus; passive.Health = 100;
            passive.LoadFlags = LoadFlags.Active | LoadFlags.SlotActive | LoadFlags.Spawned;
            clock.SetValue(replica, 1000UL); passive.ModApplyReplicatedMantleVisual(10, 100);
            clock.SetValue(replica, 5000UL); passive.ModApplyReplicatedMantleVisual(10, 100);
            Check(passive.ModPublishedMantleVisualPhase == 10 && (ulong)received.GetValue(passive)! == 1000,
                "passive replay holds accepted recorded phase across quiet frames without fabricating a fresh receipt");
            passive.ModRestoreMantleVisual(6);
            Check(passive.ModPublishedMantleVisualPhase == 6, "world restore installs exact decoded phase and resets stale source cache");
            passive.ModApplyReplicatedMantleVisual(0, 1);
            Check(passive.ModPublishedMantleVisualPhase == 0 && passive.ModGrappleGeometryQueryCount == 0
                && passive.ModGrappleForceCount == 0, "post-restore recordedNone clears passive pose without simulation authority");
        }
        finally
        {
            if (replica != null) { replica.DoCleanup(); replica.UnloadGl(); }
            if (live != null) { live.DoCleanup(); live.UnloadGl(); }
            GameState.Current = foreground; PlayerEntity.LegacyRegistry = registry; Rng.Current = random;
        }
    }
}
