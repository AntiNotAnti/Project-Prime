using System;
using System.Linq;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class TurretEventTests
{
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        _checks++; Console.WriteLine($"TURRET PASS {name}");
    }
    public static int Run(string room)
    {
        if (!ServerSim.Available(out string reason)) { Console.Error.WriteLine(reason); return 1; }
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Names[i] = $"TURRET{i}"; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            var owner = PlayerEntity.Players[0];
            var scene = owner.OwningScene;
            owner.ModArmWeapon(BeamType.PowerBeam);
            NetSession.RemoteIntentValid[0] = false; NetFireEvents.Reset();
            var turret = new HalfturretEntity(owner, scene);
            turret.EquipInfo.Weapon = scene.WeaponRules[(int)BeamType.Battlehammer];
            turret.EquipInfo.Beams = owner.EquipInfo.Beams;
            turret.EquipInfo.InfiniteAmmo = true;
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
            Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, owner.Position + Vector3.UnitY,
                Vector3.UnitY, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                "actual turret author spawn succeeds");
            var authored = new IntentPacket { Frame = NetSession.NetFrame }; NetFireEvents.Fill(ref authored, 0);
            Check(authored.FireEventCount == 1 && authored.FireEvents[0].Kind == FireEventKind.TurretFire
                && authored.FireEvents[0].Weapon == (byte)BeamType.Battlehammer
                && owner.CurrentWeapon == BeamType.PowerBeam,
                "turret event captures Battlehammer equipment while owner selects Power Beam");
            var fire = authored.FireEvents[0];
            NetFireEvents.Reset();
            var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId,
                AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(0),
                LifeId = NetPlayerLifecycle.Get(0), Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame,
                HasFireEvents = true, FireEventCount = 1 };
            carrier.FireEvents[0] = fire;
            NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true;
            Check(NetFireEvents.Accept(0, carrier), "remote turret event admitted");
            NetFireEvents.Prepare(owner, carrier);
            Check(NetFireEvents.CanFireTurret(owner) && !NetFireEvents.HasPending(owner)
                && NetFireEvents.CaptureLedger(0).Accepted == 0,
                "turret policy stays distinct from discrete ledger and player trigger");
            foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
            Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, owner.Position + Vector3.UnitY,
                Vector3.UnitY, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                "selected remote turret event spawns through actual owner");
            Check(!NetFireEvents.CanFireTurret(owner), "successful turret commit consumes selected event");
            Check(NetFireEvents.Accept(0, carrier), "duplicate turret carrier accepted as history");
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner)
                && BeamProjectileEntity.Spawn(turret, turret.EquipInfo, owner.Position + Vector3.UnitY,
                    Vector3.UnitY, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) == BeamResultFlags.NoSpawn,
                "duplicate turret event cannot emit a second projectile");
            carrier.FireEventCount = 2;
            carrier.FireEvents[0] = fire with { ShotId = fire.ShotId + 1 };
            carrier.FireEvents[1] = fire with { ShotId = fire.ShotId + 2, Kind = FireEventKind.PressFire,
                Weapon = (byte)BeamType.PowerBeam };
            Check(NetFireEvents.Accept(0, carrier), "pending turret and discrete event admitted together");
            NetFireEvents.Prepare(owner, carrier);
            Check(NetFireEvents.CanFireTurret(owner) && NetFireEvents.CaptureLedger(0).Pending == 1,
                "fixture selects turret while discrete event remains retained");
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            var ledger = NetFireEvents.CaptureLedger(0);
            Check(ledger.Pending == 1 && !NetFireEvents.CanFireTurret(owner),
                "respawn clears selected turret policy and preserves discrete event");
            carrier.LifeId = NetPlayerLifecycle.Get(0); carrier.FireEventCount = 0;
            NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true;
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner), "old-life pending turret cannot reselect after respawn");
            Check(sim.StepFailures == 0, "turret fixture has no engine step failures");
            Console.WriteLine($"PASS: {_checks} turret event assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"TURRET FAIL {ex}"); return 1; }
        finally { NetFireEvents.Reset(); NetSession.RemoteIntentValid[0] = false; sim.Stop(); }
    }
}
