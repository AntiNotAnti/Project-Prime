using System;
using System.Linq;
using System.Reflection;
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
        _checks = 0;
        if (!ServerSim.Available(out string reason)) { Console.Error.WriteLine(reason); return 1; }
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        bool acceptedPlayback = NetFireEvents.AcceptedPlaybackEnabled;
        try
        {
            NetFireEvents.AcceptedPlaybackEnabled = false; // Keep legacy selection policy as an explicit control.
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1;
                roster.Hunters[i] = (byte)(i == 0 ? Hunter.Weavel : Hunter.Samus); roster.Names[i] = $"TURRET{i}"; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            var owner = PlayerEntity.Players[0];
            var scene = owner.OwningScene;
            owner.Health = 199; owner.ModStartFormSwitch(); owner.ModForceForm(true);
            Check(owner.Hunter == Hunter.Weavel && owner.IsAltForm
                && owner.Flags2.TestFlag(PlayerFlags2.Halfturret), "real Weavel native transition creates turret source");
            owner.ModArmWeapon(BeamType.PowerBeam);
            NetSession.RemoteIntentValid[0] = false; NetFireEvents.Reset();
            var turret = owner.Halfturret;
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
            NetFireEvents.AcceptedPlaybackEnabled = true;
            carrier.FireEventCount = 2;
            carrier.FireEvents[0] = fire with { ShotId = fire.ShotId + 1 };
            carrier.FireEvents[1] = fire with { ShotId = fire.ShotId + 2, Kind = FireEventKind.PressFire,
                Weapon = (byte)BeamType.PowerBeam };
            Check(NetFireEvents.Accept(0, carrier), "pending turret and discrete event admitted together");
            NetFireEvents.Prepare(owner, carrier);
            Check(NetFireEvents.CanFireTurret(owner) && NetFireEvents.CaptureLedger(0).Pending == 1,
                "authority turret retains native selection while discrete event remains retained");
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            var ledger = NetFireEvents.CaptureLedger(0);
            Check(ledger.Pending == 1 && !NetFireEvents.CanFireTurret(owner),
                "respawn clears selected turret policy and preserves discrete event");
            carrier.LifeId = NetPlayerLifecycle.Get(0); carrier.FireEventCount = 0;
            NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true;
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner), "old-life pending turret cannot reselect after respawn");
            AcceptedAfterUnmorph(owner);
            Check(sim.StepFailures == 0, "turret fixture has no engine step failures");
            Console.WriteLine($"PASS: {_checks} turret event assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"TURRET FAIL {ex}"); return 1; }
        finally { NetFireEvents.Reset(); NetFireEvents.AcceptedPlaybackEnabled = acceptedPlayback;
            NetSession.RemoteIntentValid[0] = false; sim.Stop(); }
    }

    private static void AcceptedAfterUnmorph(PlayerEntity owner)
    {
        var scene = owner.OwningScene;
        var turret = owner.Halfturret;
        NetFireEvents.AcceptedPlaybackEnabled = false;
        NetSession.RemoteIntentValid[owner.SlotIndex] = false; NetFireEvents.Reset();
        owner.Health = 199; owner.ModStartFormSwitch(); owner.ModForceForm(true);
        owner.ModArmWeapon(BeamType.PowerBeam);
        foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
        Vector3 origin = turret.Position + Vector3.UnitY;
        for (int i = 0; i < 2; i++)
            Check(BeamProjectileEntity.Spawn(turret, turret.EquipInfo, origin, Vector3.UnitY,
                BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn,
                $"native turret equipment authors delayed Battlehammer shot {i + 1}");
        var authored = new IntentPacket { Frame = NetSession.NetFrame };
        NetFireEvents.Fill(ref authored, owner.SlotIndex);
        Check(authored.FireEventCount == 2 && authored.FireEvents[0].Kind == FireEventKind.TurretFire
            && authored.FireEvents[1].Kind == FireEventKind.TurretFire
            && authored.FireEvents[0].HasPose && authored.FireEvents[1].HasPose,
            "native spawns produce two posed turret events");
        var fire = authored.FireEvents[0]; var oldLifeFire = authored.FireEvents[1];
        foreach (var beam in owner.EquipInfo.Beams) beam.Lifespan = 0;
        NetFireEvents.Reset(); NetFireEvents.AcceptedPlaybackEnabled = true;
        var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(owner.SlotIndex), LifeId = NetPlayerLifecycle.Get(owner.SlotIndex),
            Frame = NetSession.NetFrame, AckFrame = NetSession.NetFrame, HasFireEvents = true, FireEventCount = 1 };
        carrier.FireEvents[0] = fire;
        owner.ExitAltForm(); owner.ModForceForm(false);
        // Retire the inert entity without stepping the client playback queue first.
        scene.RemoveEntity(turret);
        owner.TimeSinceShot = 17;
        int health = owner.Health; var form = owner.ModCaptureForm(); BeamType weapon = owner.CurrentWeapon;
        EntityBase? target = turret.Target;
        var cooldown = typeof(HalfturretEntity).GetField("_cooldownTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? cooldownBefore = cooldown.GetValue(turret);
        bool OwnerUnchanged() => owner.Health == health && owner.ModCaptureForm().Equals(form)
            && owner.CurrentWeapon == weapon && owner.TimeSinceShot == 17 && ReferenceEquals(turret.Target, target)
            && Equals(cooldownBefore, cooldown.GetValue(turret)) && !ContainsEntity(scene, turret);
        Check(NetSession.IsAuthority && owner.ModSpawnAcceptedTurret(fire) == BeamResultFlags.NoSpawn
            && owner.EquipInfo.Beams.Count(beam => beam.Lifespan > 0) == 0 && OwnerUnchanged(),
            "authority cannot bypass native turret gates through accepted observer helper");
        NetRole role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
        void Session(string property, object value) => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
        try
        {
            Session(nameof(NetSession.Role), NetRole.Client);
            Session(nameof(NetSession.IsAuthority), false); Session(nameof(NetSession.LocalSlot), 1);
            NetSession.RemoteIntents[owner.SlotIndex] = carrier; NetSession.RemoteIntentValid[owner.SlotIndex] = true;
            Check(NetFireEvents.Accept(owner.SlotIndex, carrier)
                && NetFireEvents.CaptureLedger(owner.SlotIndex).Pending == 1,
                "observer turret event enters full-key accepted ledger after unmorph");
            var key = new ShotKey(carrier.AuthorityEpoch, carrier.MatchId, owner.SlotIndex,
                carrier.SlotGeneration, carrier.LifeId, fire.ShotId);
            NetFireEvents.Prepare(owner, carrier);
            Check(!NetFireEvents.CanFireTurret(owner), "observer ledger does not select native target or cooldown policy");
            NetFireEvents.DrainAccepted(owner);
            var emitted = owner.EquipInfo.Beams.Where(beam => beam.Lifespan > 0).ToArray();
            Check(emitted.Length == 1 && emitted[0].ModLaunchKey == key && emitted[0].ModShotId == fire.ShotId
                && emitted[0].Beam == BeamType.Battlehammer && ReferenceEquals(emitted[0].Owner, turret),
                "observer emits exactly one full-key Battlehammer projectile from retained halfturret source");
            Check(emitted[0].SpawnPosition == fire.Origin && emitted[0].Direction == fire.Direction,
                "observer turret projectile retains authored origin and direction after unmorph");
            Check(OwnerUnchanged(), "observer playback preserves body health form weapon target cadence and absent turret");
            Check(NetFireEvents.Accept(owner.SlotIndex, carrier), "repeated observer turret carrier is valid history");
            NetFireEvents.DrainAccepted(owner);
            Check(owner.EquipInfo.Beams.Count(beam => beam.Lifespan > 0) == 1,
                "duplicate observer turret event cannot emit another projectile");

            // Author a real next-life spawn, then resume the observer role before playback.
            Session(nameof(NetSession.IsAuthority), true);
            owner.Spawn(owner.Position, Vector3.UnitZ, Vector3.UnitY, owner.NodeRef, respawn: true);
            Session(nameof(NetSession.IsAuthority), false);
            owner.ModArmWeapon(BeamType.PowerBeam); owner.TimeSinceShot = 17;
            health = owner.Health; form = owner.ModCaptureForm(); weapon = owner.CurrentWeapon;
            target = turret.Target; cooldownBefore = cooldown.GetValue(turret);
            var oldLife = carrier;
            oldLife.LifeId = NetPlayerLifecycle.Get(owner.SlotIndex);
            oldLife.FireEvents[0] = oldLifeFire;
            NetSession.RemoteIntents[owner.SlotIndex] = oldLife;
            NetSession.RemoteIntentValid[owner.SlotIndex] = true;
            Check(oldLifeFire.SourceLife != oldLife.LifeId && NetFireEvents.Accept(owner.SlotIndex, oldLife)
                && NetFireEvents.CaptureLedger(owner.SlotIndex).Pending == 1,
                "actual prior-life turret event remains in observer accepted ledger after body respawn");
            NetFireEvents.DrainAccepted(owner);
            emitted = owner.EquipInfo.Beams.Where(beam => beam.Lifespan > 0).ToArray();
            Check(emitted.Length == 2 && emitted.Count(beam => beam.ModLaunchKey ==
                (key with { ShotId = oldLifeFire.ShotId }) && beam.ModLaunchKey.LifeId == oldLifeFire.SourceLife
                && ReferenceEquals(beam.Owner, turret) && beam.SpawnPosition == oldLifeFire.Origin
                && beam.Direction == oldLifeFire.Direction) == 1,
                "observer prior-life turret event emits distinct exact historical key and pose");
            Check(OwnerUnchanged() && !owner.IsAltForm,
                "prior-life observer playback preserves new biped body and never resurrects turret");
            Check(NetFireEvents.Accept(owner.SlotIndex, oldLife), "duplicate prior-life observer carrier is valid history");
            NetFireEvents.DrainAccepted(owner);
            Check(owner.EquipInfo.Beams.Count(beam => beam.Lifespan > 0) == 2 && OwnerUnchanged(),
                "duplicate prior-life turret playback preserves exact projectile count and new body state");
        }
        finally
        {
            Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority);
            Session(nameof(NetSession.LocalSlot), local);
        }
    }

    private static bool ContainsEntity(Scene scene, EntityBase entity)
    {
        foreach (var candidate in scene.Entities)
            if (ReferenceEquals(candidate, entity)) return true;
        return false;
    }
}
