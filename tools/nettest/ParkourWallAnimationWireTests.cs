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
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Independent published offsets/widths. Accepted presentation never authorizes
// movement, geometry queries, a grapple request or a new gameplay profile.
internal static class ParkourWallAnimationWireTests
{
    private static int _checks;
    private static readonly PlayerGrappleState Active = new(true, 0xbeef, new(3, 4, 0), 5);
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException(label); }
    private static bool Refused(Action action)
    {
        try { action(); return false; }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or EndOfStreamException)
        { return true; }
    }
    internal static int Run()
    {
        try
        {
            _checks = 0;
            PackedBytes(); InvalidBytes(); LanesAndClock(); History(); Checkpoints();
            Console.WriteLine($"PARKOUR WALL ANIMATION WIRE PASS {_checks} packing/MTU/lease/history/rollback assertions");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("PARKOUR WALL ANIMATION WIRE FAIL " + e); return 1; }
    }

    private static IntentPacket Intent(byte mantle, byte wall) => new() {
        MatchId = 7, AuthorityEpoch = 8, Frame = 100, SlotGeneration = 2, LifeId = 3,
        Aim = Vector3.UnitZ, Position = Vector3.UnitY, Target = NetTargetIdentity.None,
        Buttons = IntentButtons.InPlayState | IntentButtons.MoveUp | IntentButtons.Grapple,
        GrappleRequestNonce = 0xbeef, MantleVisualPhase = mantle, WallVisualCue = wall };

    private static void PackedBytes()
    {
        Check(NetConfig.ProtocolVersion == 85 && IntentPacket.TouchCanonicalSize == 1299
            && IntentPacket.Protocol82TouchSize == 1298 && PlayerState.Size == 192
            && PlayerState.Protocol80Size == 173 && PlayerGrappleState.Size == 19,
            "protocol85 retains every published width and the173-byte mechanical extension offset");
        Vector3[] cardinal = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ };
        for (int kind = 1; kind <= 3; kind++)
        for (int direction = 0; direction < 4; direction++)
        {
            byte expected = (byte)(1 + (kind - 1) * 4 + direction);
            Check(ParkourWallVisualCodec.Encode(kind, cardinal[direction]) == expected
                && ParkourWallVisualCodec.Kind(expected) == kind && ParkourWallVisualCodec.Normal(expected) == cardinal[direction],
                "independent strafe/run/kick x four WORLD cardinal direction codes");
        }
        foreach (int kind in new[] { -1, 0, 4, 255 })
            Check(ParkourWallVisualCodec.Encode(kind, Vector3.UnitX) == 0, "undeclared wall kind has no cue");
        foreach (Vector3 normal in new[] { Vector3.Zero, Vector3.UnitY, new Vector3(float.NaN, 0, 0), new Vector3(0, 0, float.PositiveInfinity) })
            Check(ParkourWallVisualCodec.Encode(1, normal) == 0, "nonfinite/absent horizontal normal has no cue");
        byte[] golden = Convert.FromHexString("01EFBE0000404000008040000000000000A040");
        for (byte mantle = 0; mantle < 16; mantle++)
        for (byte wall = 0; wall < 13; wall++)
        {
            byte packed = (byte)(mantle | wall << 4);
            Check(ParkourWallVisualCodec.Valid(wall) && ParkourWallVisualCodec.PackIntent(mantle, wall) == packed,
                "literal16x13 requested pose packing");
            var intent = Intent(mantle, wall);
            byte[] before = new byte[1298], canonical = Enumerable.Repeat((byte)0xcc, 1301).ToArray();
            intent.Write(before); intent.Write(canonical.AsSpan(0, 1299));
            var decoded = IntentPacket.Read(canonical.AsSpan(0, 1299));
            Check(canonical.AsSpan(0, 1298).SequenceEqual(before) && canonical[1298] == packed
                && canonical[1296] == 0xef && canonical[1297] == 0xbe && canonical[1299] == 0xcc
                && decoded.MantleVisualPhase == mantle && decoded.WallVisualCue == wall,
                "canonical packed byte preserves all1298 prior bytes and both independent properties");
            for (byte count = 0; count <= 14; count += 14)
            {
                intent.FireEventCount = count;
                for (int i = 0; i < count; i++) intent.FireEvents[i] = new((uint)(i + 1), 100, 90, 128,
                    FireEventKind.PressFire, 0, 17, 0, FireEvent.FlagPose | FireEvent.FlagReticle,
                    new(3, 4, 5), Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, new(0, 1), SourceLife: 3);
                byte[] compact = Enumerable.Repeat((byte)0xcc, 1201).ToArray();
                int size = CompactIntentCodec.Write(intent, compact);
                Check(size == 139 + 74 * count && size + 25 <= 1200 && (count == 0 || size + 25 == 1200)
                    && compact[size - 1] == packed && compact[size - 3] == 0xef && compact[size - 2] == 0xbe
                    && compact[size] == 0xcc && CompactIntentCodec.TryRead(compact.AsSpan(0, size), out decoded)
                    && decoded.WallVisualCue == wall && decoded.MantleVisualPhase == mantle
                    && decoded.GrappleRequestNonce == 0xbeef && decoded.FireEventCount == count,
                    "compact idle/full14 combat retains exact1200 MTU and existing nonce/fire identity");
            }
            foreach (bool active in new[] { false, true })
            {
                var grapple = active ? Active : PlayerGrappleState.Inactive(0xbeef);
                byte[] mechanics = new byte[19]; grapple.TryWrite(mechanics, out _);
                byte[] target = Enumerable.Repeat((byte)0xcc, 23).ToArray();
                Check(ParkourWallVisualCodec.TryWriteGrapple(grapple, wall, target, out int written)
                    && written == 19 && target[0] == (byte)((active ? 1 : 0) | wall << 4)
                    && target.AsSpan(1, 18).SequenceEqual(mechanics.AsSpan(1))
                    && target.AsSpan(19).IndexOfAnyExcept((byte)0xcc) < 0
                    && ParkourWallVisualCodec.TryReadGrapple(target.AsSpan(0, 19), out var read, out byte cue)
                    && read == grapple && cue == wall,
                    "separate accepted wall cue leaves all mechanical nonce/anchor/length bytes unchanged");
                Check(wall == 0 ? PlayerGrappleState.TryRead(target.AsSpan(0, 19), out _)
                    : !PlayerGrappleState.TryRead(target.AsSpan(0, 19), out _),
                    "mechanical grapple codec still rejects every nonmechanical flag bit");
                byte[] player = new byte[192]; new PlayerState { Grapple = grapple,
                    MantleVisualPhase = mantle, WallVisualCue = wall }.Write(player);
                var state = PlayerState.Read(player);
                Check(player[173] == target[0] && player[174] == 0xef && player[175] == 0xbe
                    && player[114] == (byte)(mantle << 4) && player.AsSpan(174, 18).SequenceEqual(mechanics.AsSpan(1))
                    && state.Grapple == grapple && state.MantleVisualPhase == mantle && state.WallVisualCue == wall,
                    "literal canonical173 FLAGS and174/175 NONCE offsets cannot alias wall with nonce");
            }
        }
        Check(Active.TryWrite(new byte[19], out _) && golden[0] == 1,
            "independent mechanical golden retains its single active bit");
        byte[] actualGolden = new byte[19]; Active.TryWrite(actualGolden, out _);
        Check(actualGolden.SequenceEqual(golden), "independent published mechanical19-byte golden unchanged");
    }

    private static void InvalidBytes()
    {
        for (int invalid = 13; invalid <= 255; invalid++)
        {
            byte wall = (byte)invalid;
            Check(!ParkourWallVisualCodec.Valid(wall), "all undeclared wall fields rejected " + invalid);
            byte[] untouched = Enumerable.Repeat((byte)0xa5, 1300).ToArray();
            var intent = Intent(9, wall);
            Check(Refused(() => intent.Write(untouched.AsSpan(0, 1299))) && untouched.All(x => x == 0xa5)
                && Refused(() => CompactIntentCodec.Write(intent, untouched)) && untouched.All(x => x == 0xa5),
                "invalid requested wall preflight is atomic for canonical and compact");
            Check(Refused(() => new PlayerState { WallVisualCue = wall }.Write(untouched))
                && untouched.All(x => x == 0xa5), "invalid accepted wall canonical write is atomic");
            Check(!ParkourWallVisualCodec.TryWriteGrapple(Active, wall, untouched, out int wrote)
                && wrote == 0 && untouched.All(x => x == 0xa5), "packed grapple invalid wall leaves caller bytes untouched");
        }
        for (int flags = 0; flags <= 255; flags++)
        {
            byte[] bytes = new byte[19]; Active.TryWrite(bytes, out _); bytes[0] = (byte)flags;
            bool valid = (flags & 14) == 0 && (flags >> 4) <= 12;
            // Inactive anchor bytes deliberately remain active and must fail.
            valid &= (flags & 1) != 0;
            bool ok = ParkourWallVisualCodec.TryReadGrapple(bytes, out var read, out byte cue);
            Check(ok == valid && (ok ? read == Active && cue == flags >> 4 : read == default && cue == 0),
                "packed flags reject reserved low bits/cues and invalid inactive payload transactionally " + flags);
        }
        foreach (int length in Enumerable.Range(0, 19).Append(20))
            Check(!ParkourWallVisualCodec.TryReadGrapple(new byte[length], out _, out _), "packed exact19-byte width " + length);
        byte[] shortTarget = Enumerable.Repeat((byte)0xa5, 18).ToArray();
        Check(!ParkourWallVisualCodec.TryWriteGrapple(Active, 4, shortTarget, out int written)
            && written == 0 && shortTarget.All(x => x == 0xa5), "short packed write preflight is atomic");
        for (byte cue = 13; cue <= 15; cue++)
        for (byte mantle = 0; mantle <= 15; mantle++)
        {
            byte[] bytes = new byte[1299]; Intent(mantle, 0).Write(bytes); bytes[1298] = (byte)(cue << 4 | mantle);
            Check(Refused(() => IntentPacket.Read(bytes)), "canonical reserved packed wall cue refused");
            byte[] compact = new byte[1200]; int size = CompactIntentCodec.Write(Intent(mantle, 0), compact);
            compact[size - 1] = bytes[1298];
            Check(!CompactIntentCodec.TryRead(compact.AsSpan(0, size), out var rejected)
                && rejected.WallVisualCue == 0 && rejected.GrappleRequestNonce == 0 && rejected.Frame == 0,
                "compact reserved wall nibble refuses without installing any partial intent");
        }
    }

    private static void LanesAndClock()
    {
        byte[] canonical = ParkourAnimationWireTests.Snapshot(9, wall: 6);
        var lanes = new NetReplicationLanes(); lanes.Prepare(canonical);
        var receiver = new NetReplicationReceiver(); var players = new PlayerState[8];
        Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 7, 8), "actual slow lane accepts packed cue");
        Check(receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 7, 8, out _)
            && players[0].WallVisualCue == 6 && players[0].WallVisualRevision == lanes.SlowRevision
            && players[0].MantleVisualPhase == 9, "live decode combines independent fast mantle and matching-life slow wall/revision");
        byte[] assembled = new byte[4096]; int size = receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), assembled, 7, 8);
        Check(size == canonical.Length && assembled.AsSpan(0, size).SequenceEqual(canonical), "canonical assembly retains wall packed bits exactly for replay/bootstrap");
        var missing = new NetReplicationReceiver(); missing.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 7, 8, out _);
        Check(players[0].WallVisualCue == 0 && players[0].WallVisualRevision == 0 && players[0].MantleVisualPhase == 9,
            "missing slow state cannot invent a wall cue or erase fast mantle");
        byte[] malformed = lanes.Slow.AsSpan(0, lanes.SlowLength).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(10), lanes.SlowRevision + 1);
        malformed[15 + NetReplicationLanes.GrappleOffset] = 0xd0;
        Check(!receiver.Receive(PacketType.PlayerSlowState, malformed, 7, 8) && receiver.SlowRevision == lanes.SlowRevision,
            "invalid newer slow cue rejects before replacing accepted revision");
        Check(!receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 7, 8),
            "duplicate slow revision never renews accepted receipt");

        Headless.Enter(); const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var clock = typeof(Scene).GetField("_frameCount", hidden)!;
        var reset = typeof(PlayerEntity).GetMethod("ModResetParkourGrappleForLife", hidden)!;
        var foreground = GameState.Current; var registry = PlayerEntity.LegacyRegistry; var random = Rng.Current;
        Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            scene.GameState.MovementModel = MovementModel.Parkour;
            var player = scene.Players.Items[1]; player.IsBot = false; player.Hunter = Hunter.Samus; player.Health = 100;
            player.LoadFlags = LoadFlags.Active | LoadFlags.SlotActive | LoadFlags.Spawned;
            Vector3 position = player.Position, speed = player.Speed;
            for (uint elapsed = 0; elapsed <= 35; elapsed++)
            {
                clock.SetValue(scene, 1000UL + elapsed);
                new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = 100 + elapsed, PlayerCount = 1 }.Write(canonical);
                lanes.Prepare(canonical); // Do NOT deliver refreshed slow: model dropped slow UDP.
                receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 7, 8, out var header);
                player.ModApplyReplicatedWallVisual(players[0].WallVisualCue, header.Frame, slowRevision: players[0].WallVisualRevision);
                Check(player.ModPublishedWallVisualCue == (elapsed <= 30 ? 6 : 0),
                    "fresh fast snapshots cannot renew same cached slow revision at age " + elapsed);
            }
            Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 7, 8), "later accepted slow revision can renew a maintained cue");
            receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 7, 8, out var current);
            player.ModApplyReplicatedWallVisual(players[0].WallVisualCue, current.Frame, slowRevision: players[0].WallVisualRevision);
            Check(player.ModPublishedWallVisualCue == 6, "new slow revision renews cue even with same fast frame");
            player.ModApplyReplicatedWallVisual(12, 1); clock.SetValue(scene, 1066UL);
            player.ModApplyReplicatedWallVisual(6, 999, slowRevision: players[0].WallVisualRevision);
            Check(player.ModPublishedWallVisualCue == 0, "switching full/slow source domains cannot erase cached revision and resurrect it");
            reset.Invoke(player, null); player.ModApplyReplicatedWallVisual(5, 1, slowRevision: 1);
            Check(player.ModPublishedWallVisualCue == 5, "new life reset retires all prior wall source domains");
            Check(player.Position == position && player.Speed == speed && player.ModGrappleGeometryQueryCount == 0
                && player.ModGrappleForceCount == 0, "presentation leases never move body or query/apply grapple mechanics");
        }
        finally
        {
            if (scene != null) { scene.DoCleanup(); scene.UnloadGl(); }
            GameState.Current = foreground; PlayerEntity.LegacyRegistry = registry; Rng.Current = random;
        }
        byte[] newLife = canonical.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(newLife.AsSpan(23 + 50), 4);
        lanes.Prepare(newLife); receiver.TryDecodeLive(lanes.Fast.AsSpan(0, lanes.FastLength), players, 7, 8, out _);
        Check(players[0].WallVisualCue == 0 && players[0].WallVisualRevision == 0,
            "cached slow wall state is fenced by life as well as slot/generation");
    }

    private static void History()
    {
        for (byte mantle = 0; mantle < 16; mantle++)
        {
            byte[] intent = new byte[2 + 1299]; intent[0] = (byte)PacketType.SlotIntent; Intent(mantle, 0).Write(intent.AsSpan(2));
            byte[] source = intent.ToArray(); intent[^1] |= 0xf0;
            var normalized = ReplayIdentityCompatibility.NormalizeLegacyParkourPose(intent, 84);
            var decoded = IntentPacket.Read(normalized[2..]);
            Check(normalized.SequenceEqual(source) && decoded.MantleVisualPhase == mantle && decoded.WallVisualCue == 0
                && decoded.GrappleRequestNonce == 0xbeef && intent[^1] == (byte)(0xf0 | mantle),
                "old84 reserved request wall nibble clears without erasing any mantle/nonce fact or mutating archive");
            byte[] payload = ParkourAnimationWireTests.Snapshot(mantle);
            byte[] packet = new byte[1 + payload.Length]; packet[0] = (byte)PacketType.Snapshot; payload.CopyTo(packet, 1);
            byte[] expected = packet.ToArray(); packet[24 + 173] |= 0xf0;
            normalized = ReplayIdentityCompatibility.NormalizeLegacyParkourPose(packet, 84);
            var state = PlayerState.Read(normalized.Slice(24, 192));
            Check(normalized.SequenceEqual(expected) && state.MantleVisualPhase == mantle && state.WallVisualCue == 0,
                "old84 accepted wallNone preserves all16 mantle facts and unchanged snapshot bytes");
            byte[] current = new byte[2 + 1299]; current[0] = (byte)PacketType.SlotIntent; Intent(mantle, 12).Write(current.AsSpan(2));
            Check(ReplayIdentityCompatibility.NormalizeLegacyParkourPose(current, 85).SequenceEqual(current),
                "current85 packed wall cue remains an exact recorded fact");
        }
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
            foreach (byte mantle in new byte[] { 0, 1, 9, 15 })
            for (byte wall = 0; wall < 13; wall++)
            {
                byte[] bytes = ParkourAnimationWireTests.Seed(mantle, custom ? context : null, wall).CaptureCheckpoint().Bytes.ToArray();
                var restored = new ReplayReplicaState(); Restore(restored, bytes);
                Check(bytes[4] == (custom ? 17 : 16) && bytes[6] == 85 && bytes[7] == (custom ? 119 : 87)
                    && restored.CaptureCheckpoint().Bytes.SequenceEqual(bytes) && restored.TryGetPlayer(0, out var player)
                    && player.WallVisualCue == wall && player.MantleVisualPhase == mantle
                    && restored.TryGetIntent(0, out var intent) && intent.WallVisualCue == wall && intent.MantleVisualPhase == mantle,
                    "current85 same16/17 native/custom checkpoint retains independent accepted/requested wall and mantle facts");
                int at = Protocol83CheckpointFixtures.FirstActorOffset(bytes), wallAt = at + 7 + 173, intentAt = at + 7 + 192 + 1 + 1298;
                for (byte bad = 13; bad <= 15; bad++)
                foreach (int offset in new[] { wallAt, intentAt })
                {
                    byte[] corrupt = bytes.ToArray(); corrupt[offset] = (byte)(bad << 4 | (corrupt[offset] & 15));
                    Check(Refused(() => Restore(restored, corrupt)) && restored.CaptureCheckpoint().Bytes.SequenceEqual(bytes),
                        "malformed accepted/requested wall checkpoint refuses transactionally");
                }
                if (wall != 0)
                {
                    Check(Refused(() => Protocol84CheckpointFixtures.ToProtocol84(bytes))
                        && Refused(() => Protocol83CheckpointFixtures.ToProtocol83(bytes)), "historical projectors cannot discard nonzero wall facts");
                    continue;
                }
                byte[] old = Protocol84CheckpointFixtures.ToProtocol84(bytes);
                Check(old.Length == bytes.Length && old[4] == bytes[4] && old[7] == bytes[7] && old[6] == 84,
                    "authentic retained84 projection keeps16/17 features and all actor/tail widths");
                var historical = new ReplayReplicaState(); Restore(historical, old);
                Check(historical.CaptureCheckpoint().Bytes.SequenceEqual(bytes) && historical.TryGetPlayer(0, out player)
                    && player.MantleVisualPhase == mantle && player.WallVisualCue == 0
                    && historical.TryGetIntent(0, out intent) && intent.MantleVisualPhase == mantle && intent.WallVisualCue == 0,
                    "old84 checkpoint retains mantle while defaulting only newly reserved wall presentation");
                byte[] oldReserved = old.ToArray(); oldReserved[wallAt] |= 0xf0; oldReserved[intentAt] |= 0xf0;
                var oldReservedOwner = new ReplayReplicaState(); Restore(oldReservedOwner, oldReserved);
                Check(oldReservedOwner.CaptureCheckpoint().Bytes.SequenceEqual(bytes),
                    "old84 reserved wall13..15 nibbles normalize before current reader validation and preserve mantle exactly");
                byte[] acceptedOnly = bytes.ToArray(); acceptedOnly[wallAt] |= 0x10;
                byte[] requestedOnly = bytes.ToArray(); requestedOnly[intentAt] |= 0x10;
                Check(Refused(() => Protocol84CheckpointFixtures.ToProtocol84(acceptedOnly))
                    && Refused(() => Protocol84CheckpointFixtures.ToProtocol84(requestedOnly)),
                    "retained84 projector independently refuses accepted and requested wall loss");
                if (mantle == 0)
                    Check(Refused(() => Protocol83CheckpointFixtures.ToProtocol83(acceptedOnly))
                        && Refused(() => Protocol83CheckpointFixtures.ToProtocol83(requestedOnly)), "retained83 projector independently refuses accepted and requested wall loss");
            }
        }
    }
}
