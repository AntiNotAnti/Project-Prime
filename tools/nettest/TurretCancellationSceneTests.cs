using System;
using System.Linq;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Synthetic production-application fixture, not a live campaign observation.
// Same-key beams represent parent/child flights; this does not test native child generation.
internal static class TurretCancellationSceneTests
{
    private static void Check(bool ok, string label) => NetArchitectureTests.Check(ok, "Synthetic turret cancellation: " + label);
    private static BeamProjectileEntity Beam(Scene scene, ShotKey key, float lifespan = 3)
    {
        var beam = new BeamProjectileEntity(scene)
        {
            Position = new Vector3(1, 2, 3), Velocity = Vector3.UnitZ,
            Owner = scene.Players.Items[0], Lifespan = lifespan,
            ModLaunchKey = key, ModShotId = key.ShotId
        };
        scene.InsertEntity(beam);
        return beam;
    }
    private static byte[] Packet(TurretFireOutcome fact)
    {
        var bytes = new byte[1 + TurretFireOutcome.Size];
        bytes[0] = (byte)PacketType.TurretFireOutcome;
        fact.Write(bytes.AsSpan(1));
        return bytes;
    }
    public static int Run(string replayPath)
    {
        Headless.Enter();
        try
        {
            using var replay = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
            HealthShotTests.Session();
            Check(NetSession.WorldIsReady && !NetSession.FreezeGameplay, "production application gate is open");
            var key = new ShotKey(NetSession.AuthorityEpoch, NetSession.CurrentMatchId, 0, 9, 2, 123);
            var fire = new FireEvent(key.ShotId, 100, 90, 0, FireEventKind.TurretFire,
                (byte)BeamType.Battlehammer, 0, 0, FireEvent.FlagPose,
                Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, default, key.LifeId);
            var refused = new TurretFireOutcome(key, 130, TurretFireOutcomeStatus.Refused,
                TurretFireOutcomeReason.OwnerRetired, fire, default, default);
            var targets = Enumerable.Range(0, 3).Select(_ => Beam(replay.Scene, key)).ToArray();
            var ended = Beam(replay.Scene, key, 0);
            var endedOwner = ended.Owner;
            var controls = new[]
            {
                key with { AuthorityEpoch = key.AuthorityEpoch + 1 },
                key with { MatchId = (ushort)(key.MatchId + 1) },
                key with { ShooterSlot = 1 },
                key with { Generation = (ushort)(key.Generation + 1) },
                key with { LifeId = (ushort)(key.LifeId + 1) },
                key with { ShotId = key.ShotId + 1 }
            }.Select(k => Beam(replay.Scene, k)).ToArray();
            var owners = controls.Select(b => b.Owner).ToArray();
            byte[] packet = Packet(refused);
            HealthShotTests.Deliver(packet);
            Check(NetSession.PendingTurretOutcomes == 1, "received refusal queues once before scene application");
            Check(targets.All(b => b.Lifespan == 3), "packet receipt alone does not destroy predicted flight representatives");
            HealthShotTests.Deliver(packet);
            Check(NetSession.PendingTurretOutcomes == 1, "duplicate refusal is suppressed before application");
            NetSession.ApplyTurretOutcomes(replay.Scene);
            Check(targets.All(b => b.Lifespan == 0 && b.Owner == null), "all active exact-key parent/child representatives are destroyed");
            Check(controls.Select((b, i) => b.Lifespan == 3 && b.Owner == owners[i]).All(v => v),
                "epoch, match, slot, generation, life and shot controls survive");
            Check(ended.Lifespan == 0 && ended.Owner == endedOwner, "already-ended matching flight remains untouched");
            Check(NetSession.PendingTurretOutcomes == 0, "application drains pending refusal");
            // A replacement matching beam proves a repeated terminal cannot act
            // again after it has been consumed, rather than only checking dead beams.
            var replacement = Beam(replay.Scene, key);
            HealthShotTests.Deliver(packet);
            NetSession.ApplyTurretOutcomes(replay.Scene);
            Check(replacement.Lifespan == 3 && replacement.Owner != null
                && NetSession.PendingTurretOutcomes == 0,
                "duplicate after application cannot repeat cancellation");
            Console.WriteLine("PASS: synthetic asset-backed full-key turret cancellation fixture (no native child-generation claim)");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetSession.Stop(); }
    }
}
