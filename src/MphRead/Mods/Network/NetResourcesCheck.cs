using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats.Collision;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Offline native pickup/fire fixtures and bounded resource reconciliation.
/// No socket, authored attack packet, or damage claim is created.</summary>
public static class NetResourcesCheck
{
    private static int _checks;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("RESOURCE PASS " + message);
    }
    private static void Frame(uint frame) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame);
    private static void Client(bool client)
    {
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, client ? NetRole.Client : NetRole.Server);
        typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, !client);
        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, client ? 0 : -1);
    }
    private static int[] Ammo(PlayerEntity player) => (int[])typeof(PlayerEntity).GetField("_ammo", PrivateInstance)!.GetValue(player)!;

    public static int Run(string? assetDirectory = null)
    {
        _checks = 0; Headless.Enter();
        if (assetDirectory != null) System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(assetDirectory));
        Paths.UpdatePaths(); Paths.ChooseMphPath();
        var roster = RosterPacket.Create();
        roster.Count = 1; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)Hunter.Samus;
        roster.Teams[0] = -1; roster.Names[0] = "RESOURCE FIXTURE";
        var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 1,
            Phase = SessionPhase.InMatch, WorldProfile = MatchWorldProfile.Resolve(1),
            Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, TimeLimitSeconds = 600 } };
        var sim = new ServerSim();
        try
        {
            Check(sim.Start("MP1 SANCTORUS", GameMode.Battle, 1, _ => { }, () => { }, roster, session), "native fixture loads");
            PlayerEntity.Players[0].OwningScene.GameState.PointGoal = 1000;
            for (int i = 0; i < 120; i++) sim.Step();
            var player = PlayerEntity.Players[0]; var scene = player.OwningScene;
            Check(player.ModIsInPlay && sim.StepFailures == 0, "normal authority actor spawns");
            Check(typeof(NetSession).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) == null,
                "fixture has no network transport");
            var pickupMethod = typeof(PlayerEntity).GetMethod("PickUpItems", PrivateInstance)!;
            Ammo(player)[0] = Ammo(player)[1] = 0;
            ItemSpawnEntity? spawn = null;
            foreach (var candidate in scene.GetItemSpawnEntities())
                if (candidate.Item is { DespawnTimer: not 0 } item && !MapResourceRules.IsHealth(item.ItemType))
                { spawn = candidate; break; }
            Check(spawn?.Item != null, "native room contains a nonhealth resource spawn");
            player.Position = spawn!.Item!.Position.AddY(-0.65f); player.ModRefreshVolume();
            var beforePickup = player.CaptureNetworkResources(ReplayActorRef.Capture(player), NetSession.NetFrame);
            pickupMethod.Invoke(player, null);
            var afterPickup = player.CaptureNetworkResources(ReplayActorRef.Capture(player), NetSession.NetFrame);
            Check(!spawn.ModHealthState.Available && beforePickup != afterPickup,
                "authority native pickup consumes availability and grants resources");

            // Include a native door with a real portal/collision instance even
            // when this multiplayer room has no authored door entities.
            var doorData = new DoorEntityData(new((ushort)EntityType.Door, 30000, player.Position,
                Vector3.UnitY, Vector3.UnitZ), null, 0, DoorType.Standard, 255, 0, 0, 0, 0, null, null);
            var door = new DoorEntity(doorData, "", scene);
            scene.AddEntity(door); door.SetUpPort("", "");
            door.ConnectorCollision = new CollisionInstance("fixture", scene.Room!.RoomCollision[0].Info, isEntity: true);
            door.Flags = DoorFlags.Loaded | DoorFlags.Open; door.ConnectorInactive = true;
            door.Portal!.Active = true; door.ConnectorCollision.Active = false;
            var dropPosition = player.Position + Vector3.UnitX * 8;
            var sourceDrop = ItemSpawnEntity.SpawnItem(ItemType.UASmall, dropPosition,
                scene.Room.GetNodeRefByPosition(dropPosition), 600, scene);
            Check(sourceDrop != null, "normal native resource drop exists");
            var world = ReplayAuthorityWorld.Capture(scene, 1, 1, NetSession.NetFrame);
            ReplayResourceState authority = world.Resources[0];
            var decoded = ReplayAuthorityWorld.Decode(world.Encode());
            Check(decoded.Resources[0] == authority && decoded.Drops.Length == world.Drops.Length,
                "version5 bootstrap resources and complete world facts round trip");
            Client(true); Frame(1000); player.ResetNetworkResources();
            Check(scene.CameraMode == CameraMode.Roam && !player.IsMainPlayer && player.ModOwnsLocalNetworkResources,
                "admitted local owner retains resource prediction in headless Roam camera");
            Ammo(player)[0] = Ammo(player)[1] = 0;
            spawn.ModApplyNetworkHealthState(spawn.ModHealthState with { Available = true }, feedback: false);
            door.Flags = DoorFlags.Closed; door.ConnectorInactive = false;
            door.Portal.Active = false; door.ConnectorCollision.Active = true;
            world.Tick = 1000; world.Resources[0] = authority with { AcknowledgedFrame = 1000 };
            NetObjectiveSync.Apply(scene, world, bootstrap: true);
            Check(player.ModAmmo.Ua == authority.Ua && player.ModAmmo.Missiles == authority.Missiles
                && !spawn.ModHealthState.Available, "join/bootstrap restores authority ammo and consumed spawn");
            player.ModSetAmmo(4000, 4000);
            Check(player.ModAmmo == (authority.Ua, authority.Missiles),
                "older carrier ammo cannot overwrite a known authority resource baseline");
            spawn.ModApplyNetworkHealthState(spawn.ModHealthState with { Available = true }, feedback: false);
            var puppet = scene.Players.Items[1]; int puppetHealth = puppet.Health;
            puppet.Health = 99; puppet.Position = spawn.Item!.Position.AddY(-0.65f);
            pickupMethod.Invoke(puppet, null);
            Check(!puppet.ModOwnsLocalNetworkResources && spawn.ModHealthState.Available,
                "client remote puppet cannot consume a native resource pickup");
            Ammo(puppet)[0] = 19;
            puppet.NotePredictedResourceSpend(20, Ammo(puppet)[1], 0, omega: false);
            Check((int)typeof(PlayerEntity).GetField("_resourceSpendCursor", PrivateInstance)!.GetValue(puppet)! == 0,
                "remote puppet cannot author a local resource cost ledger");
            puppet.Health = puppetHealth;
            scene.SideScene = true; pickupMethod.Invoke(player, null);
            Check(!player.ModOwnsLocalNetworkResources && spawn.ModHealthState.Available,
                "preview side scene cannot author or consume live owner resources");
            scene.SideScene = false;
            spawn.ModApplyNetworkHealthState(spawn.ModHealthState with { Available = false }, feedback: false);
            Check(door.Flags == (DoorFlags.Loaded | DoorFlags.Open) && door.ConnectorInactive
                && door.Portal.Active && !door.ConnectorCollision.Active, "live authority door portal/collision facts converge");
            var drops = new List<ItemInstanceEntity>();
            foreach (var item in scene.GetItemInstanceEntities())
                if (item.Owner == null && item.TokenId == 0 && item.DespawnTimer > 0) drops.Add(item);
            Check(drops.Count == world.Drops.Length && drops.All(i => i.Id < -1),
                "live drops replace provisional local identities exactly once");
            var liveDrop = drops[0]; liveDrop.Position += Vector3.UnitX; liveDrop.DespawnTimer--;
            world.Tick++; NetObjectiveSync.Apply(scene, world);
            Check(liveDrop.Position == world.Drops[0].Position && liveDrop.DespawnTimer == world.Drops[0].DespawnTimer,
                "next authority world corrects an existing drop by stable identity");
            liveDrop.DespawnTimer--; int remaining = liveDrop.DespawnTimer;
            NetObjectiveSync.Apply(scene, world);
            Check(liveDrop.DespawnTimer == remaining, "duplicate cached world cannot restart native drop timer");
            var originalDrop = world.Drops[0];
            ItemType replacementType = originalDrop.Type == ItemType.MissileSmall ? ItemType.UASmall : ItemType.MissileSmall;
            world.Tick++; world.Drops[0] = originalDrop with { Type = replacementType };
            NetObjectiveSync.Apply(scene, world);
            Check(scene.TryGetEntity(-2 - originalDrop.Identity, out var immutableDrop)
                && ReferenceEquals(immutableDrop, liveDrop) && liveDrop.ItemType == originalDrop.Type,
                "same authority epoch keeps an existing drop identity immutable");
            NetObjectiveSync.Reset(); world.Tick++;
            NetObjectiveSync.Apply(scene, world, bootstrap: true);
            Check(scene.TryGetEntity(-2 - originalDrop.Identity, out var replacedDrop)
                && replacedDrop is ItemInstanceEntity replacement && replacement.ItemType == replacementType
                && !ReferenceEquals(replacedDrop, liveDrop),
                "fresh authority baseline can reuse a drop identity with a different native type");
            int identityCopies = 0;
            foreach (var item in scene.GetItemInstanceEntities())
                if (item.Id == -2 - originalDrop.Identity) identityCopies++;
            NetObjectiveSync.Apply(scene, world);
            Check(identityCopies == 1 && scene.TryGetEntity(-2 - originalDrop.Identity, out var sameDrop)
                && ReferenceEquals(sameDrop, replacedDrop), "baseline replacement creates exactly one stable native drop");
            NetObjectiveSync.Reset(); world.Tick++; world.Drops[0] = originalDrop;
            NetObjectiveSync.Apply(scene, world, bootstrap: true);

            // Native fire must enter the ledger through successful Spawn.
            player.ResetNetworkResources(); authority = authority with { Ua = 200, Missiles = 50,
                AcknowledgedFrame = 1100, DoubleDamage = 10, Cloak = 10, Deathalt = 10 };
            Frame(1100); player.ModArmWeapon(BeamType.Missile); player.ApplyNetworkResources(authority);
            typeof(PlayerEntity).GetField("_timeSinceShot", PrivateInstance)!.SetValue(player, (ushort)1000);
            player.Controls.Shoot.IsDown = player.Controls.Shoot.IsPressed = true;
            player.ModSetAim(Vector3.UnitZ);
            typeof(PlayerEntity).GetMethod("UpdateAimVecs", PrivateInstance)!.Invoke(player, null);
            Frame(1101); int missiles = player.ModAmmo.Missiles;
            Check((bool)typeof(PlayerEntity).GetMethod("TryFireWeapon", PrivateInstance)!.Invoke(player, null)!,
                "ordinary local native missile actually spawns");
            int spent = missiles - player.ModAmmo.Missiles;
            Check(spent > 0, "native successful shot spends finite ammo");
            player.ApplyNetworkResources(authority);
            Check(player.ModAmmo.Missiles == missiles - spent, "old authority ACK cannot refill pending native shot");
            var grant = authority with { Weapons = (ushort)(authority.Weapons | (1 << (int)BeamType.VoltDriver)),
                Weapon2 = (byte)BeamType.VoltDriver, Cloak = 0 };
            player.ApplyNetworkResources(grant);
            Check(player.ModOwnsAttackWeapon(BeamType.VoltDriver) && !player.Flags2.TestFlag(PlayerFlags2.Cloaking),
                "ordinary pending fire does not defer unrelated inventory or cloak expiry");
            var resourceWorld = ReplayAuthorityWorld.Capture(scene, 1, 1, 1102);
            resourceWorld.Resources[0] = grant with { DoubleDamage = 10, Deathalt = 10 };
            NetObjectiveSync.Apply(scene, resourceWorld);
            typeof(PlayerEntity).GetField("_doubleDmgTimer", PrivateInstance)!.SetValue(player, (ushort)9);
            typeof(PlayerEntity).GetField("_deathaltTimer", PrivateInstance)!.SetValue(player, (ushort)9);
            NetObjectiveSync.Apply(scene, resourceWorld);
            var timers = player.CaptureNetworkResources(ReplayActorRef.Capture(player), 0);
            Check(timers.DoubleDamage == 9 && timers.Deathalt == 9,
                "duplicate cached world does not top up native powerup timers");
            player.ApplyNetworkResources(grant with { Missiles = missiles - spent, AcknowledgedFrame = 1101 });
            Check(player.ModAmmo.Missiles == missiles - spent, "acknowledged native shot settles without double spending");

            // Deterministic ledger stress uses native bounded pools. The native
            // launch above separately verifies the real shot hook into it.
            player.ResetNetworkResources(); Frame(1200);
            var pool = grant with { Ua = 200, Missiles = 50, AcknowledgedFrame = 1200 };
            player.ApplyNetworkResources(pool);
            for (uint i = 1; i <= 130; i++)
            {
                Frame(1200 + i); int ua = Ammo(player)[0]; Ammo(player)[0]--;
                player.NotePredictedResourceSpend(ua, Ammo(player)[1], 0, omega: false);
            }
            player.ApplyNetworkResources(pool);
            Check(player.ModAmmo.Ua == 70, "bounded prediction overflow cannot refill lost unacknowledged costs");
            player.ApplyNetworkResources(pool with { Ua = 198, AcknowledgedFrame = 1202 });
            Check(player.ModAmmo.Ua == 70, "ACK crossing overflow watermark reconstructs retained cost balance");
            player.ApplyNetworkResources(pool with { Ua = 70, AcknowledgedFrame = 1330 });
            Check(player.ModAmmo.Ua == 70, "complete ACK recovery settles bounded prediction history");

            player.ResetNetworkResources(); Frame(1400);
            var pickupBaseline = player.CaptureNetworkResources(ReplayActorRef.Capture(player), 1400) with { Ua = 0, Missiles = 0 };
            world.Tick = 1400; world.Resources[0] = pickupBaseline;
            world.Pickups = world.Pickups.Select(p => p.Id == spawn.Id
                ? p with { State = p.State with { Available = true } } : p).ToArray();
            NetObjectiveSync.Apply(scene, world);
            player.Position = spawn.Item!.Position.AddY(-0.65f); player.ModRefreshVolume(); Frame(1401);
            pickupMethod.Invoke(player, null);
            var predictedPickup = player.CaptureNetworkResources(ReplayActorRef.Capture(player), 1401);
            Check(!spawn.ModHealthState.Available && predictedPickup != (pickupBaseline with { AcknowledgedFrame = 1401 }),
                "post-bootstrap local native pickup grants resources immediately");
            world.Tick++; NetObjectiveSync.Apply(scene, world); spawn.Process();
            Check(!spawn.ModHealthState.Available
                && player.CaptureNetworkResources(predictedPickup.Actor, 1401) == predictedPickup,
                "older ACK cannot refill a predicted pickup or regrant its spawn");
            world.Tick++; world.Resources[0] = predictedPickup with { AcknowledgedFrame = 1402 };
            world.Pickups = world.Pickups.Select(p => p.Id == spawn.Id
                ? p with { State = p.State with { Available = false } } : p).ToArray();
            NetObjectiveSync.Apply(scene, world);
            Check(!spawn.ModHealthState.Available
                && player.CaptureNetworkResources(predictedPickup.Actor, 1401) == predictedPickup,
                "authority pickup ACK converges native inventory and availability once");

            // Normal authority death creates a genuinely newer life. A previous
            // bootstrap's resource actor must then fail the existing life fence.
            var previousActor = ReplayActorRef.Capture(player);
            Client(false); player.Controls.ClearAll(); player.TakeDamage(0, DamageFlags.Death, null, source: null);
            for (int i = 0; i < PlayerEntity.RespawnTime; i++) sim.Step();
            Check(player.Health > 0 && NetPlayerLifecycle.Get(0) == NetLifecycleTracker.Next(previousActor.Life),
                "normal authority respawn advances resource life");
            Client(true); player.ResetNetworkResources(); Ammo(player)[0] = 7; Ammo(player)[1] = 8;
            world.Tick = NetSession.NetFrame; world.Resources[0] = pool with { Actor = previousActor, Ua = 200, Missiles = 50 };
            NetObjectiveSync.Apply(scene, world, bootstrap: true);
            Check(player.ModAmmo == (7, 8), "old-life resource actor cannot apply to new native spawn");
            spawn.ModApplyNetworkHealthState(spawn.ModHealthState with { Available = true }, feedback: false);
            player.Position = spawn.Item!.Position.AddY(-0.65f); player.ModRefreshVolume();
            pickupMethod.Invoke(player, null);
            Check(!spawn.ModHealthState.Available, "new native life can predict an ordinary pickup");
            world.Resources[0] = pool with { Actor = previousActor,
                AcknowledgedFrame = NetSession.NetFrame + 100 };
            world.Pickups = world.Pickups.Select(p => p.Id == spawn.Id
                ? p with { State = p.State with { Available = true } } : p).ToArray();
            world.Tick++; NetObjectiveSync.Apply(scene, world); spawn.Process();
            Check(!spawn.ModHealthState.Available,
                "old-life ACK cannot release a new-life pickup prediction across clock offsets");
            world.Tick++; world.Drops = []; NetObjectiveSync.Apply(scene, world);
            bool anyDrop = false;
            foreach (var item in scene.GetItemInstanceEntities())
                anyDrop |= item.Owner == null && item.TokenId == 0 && item.DespawnTimer > 0;
            Check(!anyDrop, "removed authority drops disappear from client world");
            Console.WriteLine($"PASS: {_checks} native resource/world checks"); return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL: resources: " + ex); return 1; }
        finally { sim.Stop(); }
    }
}
