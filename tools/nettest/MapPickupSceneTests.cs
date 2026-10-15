using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Uses native item/spawner/contact code with fixture placement and clocks.
// Independent client prediction, impairment and replay are covered separately
// by --pickup-network/--pickup-readiness/--pickup-replay/--pickup-claim-scene.
internal static class MapPickupSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool good, string message)
    { if (!good) throw new InvalidOperationException(message); _checks++; }
    private static T Read<T>(PlayerEntity player, string name) =>
        (T)typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(player)!;
    private static void Field(PlayerEntity player, string name, int value)
    {
        var field = typeof(PlayerEntity).GetField(name, Hidden)!;
        field.SetValue(player, Convert.ChangeType(value, field.FieldType));
    }
    private static void Session(string name, object value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static void Retire(ItemInstanceEntity item)
    { item.Destroy(); item.OwningScene.RemoveEntity(item); }

    internal static int Run(string room)
    {
        _checks = 0;
        var sim = new ServerSim(); bool script = NetTestScript.Enabled;
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            NetTestScript.Enabled = false;
            Check(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }), "native pickup authority starts");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)Hunter.Samus; roster.Names[0] = "MAP pickup fixture";
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            var scene = PlayerEntity.Players[0].OwningScene; var player = scene.Players.Items[0];
            Session(nameof(NetSession.Role), NetRole.Host); Session(nameof(NetSession.LocalSlot), 0);
            scene.GameState.BalancedMode = false; scene.GameState.EnhancedHunters = false;
            scene.GameState.PrimeHunter = -1; scene.GameState.NoImperialist = false;
            Check(player.ModIsInPlay && player.Health > 0 && NetSession.IsAuthority, "native admitted local authority body");
            ItemSpawnEntity? template = null;
            foreach (var candidate in scene.GetItemSpawnEntities()) { template = candidate; break; }
            if (template == null) throw new InvalidOperationException("Native room has no item spawner template.");
            foreach (var item in scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
            short id = 29000;
            foreach (ItemType type in MapBuilder.MultiplayerItems.OrderBy(t => (int)t))
            foreach (ushort interval in new ushort[] { 1, 7, 300 })
            {
                NetPowerUpLedger.BeginRoom();
                Session(nameof(NetSession.NetFrame), NetSession.NetFrame + 10);
                player.ModForceForm(false); player.ModPlaceAt(new Vector3(0, 1000, 0));
                player.Health = 100; Field(player, "_healthMax", 199);
                foreach (string timer in new[] { "_doubleDmgTimer", "_cloakTimer", "_deathaltTimer" }) Field(player, timer, 0);
                Array.Clear(Read<int[]>(player, "_ammo")); player.AvailableWeapons.ClearAll();
                object data = template.Data;
                void Set(string name, object value) => typeof(ItemSpawnEntityData).GetField(name)!.SetValue(data, value);
                Set(nameof(ItemSpawnEntityData.Header), new EntityDataHeader(template.Data.Header.Type, id++,
                    player.Position, Vector3.UnitY, Vector3.UnitZ));
                Set(nameof(ItemSpawnEntityData.ItemType), type); Set(nameof(ItemSpawnEntityData.HasBase), (byte)0);
                Set(nameof(ItemSpawnEntityData.Enabled), (byte)1); Set(nameof(ItemSpawnEntityData.ParentId), -1);
                Set(nameof(ItemSpawnEntityData.NotifyEntityId), (short)-1); Set(nameof(ItemSpawnEntityData.CollectedMessage), Message.None);
                Set(nameof(ItemSpawnEntityData.SpawnDelay), (ushort)0); Set(nameof(ItemSpawnEntityData.SpawnInterval), interval);
                Set(nameof(ItemSpawnEntityData.MaxSpawnCount), (ushort)2);
                var spawn = new ItemSpawnEntity((ItemSpawnEntityData)data, "", scene); scene.AddEntity(spawn);
                spawn.Process();
                var first = spawn.Item ?? throw new InvalidOperationException("Missing authored item " + type);
                Check(first.ItemType == type && first.DespawnTimer == -1 && spawn.ModHealthState.SpawnCount == 1,
                    $"{type}/{interval}: native authored spawn has unlimited lifetime and one incarnation");
                player.ModPlaceAt(first.Position);
                typeof(PlayerEntity).GetMethod("PickUpItems", Hidden)!.Invoke(player, null);
                Check(first.DespawnTimer == 0, $"{type}/{interval}: native contact consumes supported authored item");
                VerifyGrant(player, type);
                Check(!first.Process() && spawn.Item == null, $"{type}/{interval}: collected item releases spawner reference");
                Retire(first);
                for (int tick = 1; tick < interval * 2; tick++)
                { spawn.Process(); Check(spawn.Item == null, $"{type}/{interval}: no respawn before native cooldown tick {tick}"); }
                spawn.Process();
                var second = spawn.Item ?? throw new InvalidOperationException("Missing native respawn " + type);
                Check(second != first && second.ItemType == type && spawn.ModHealthState.SpawnCount == 2,
                    $"{type}/{interval}: exact 60Hz respawn boundary creates fresh incarnation");
                second.DespawnTimer = 0; second.Process(); Retire(second);
                for (int tick = 0; tick <= interval * 2; tick++) spawn.Process();
                Check(spawn.Item == null && spawn.ModHealthState.SpawnCount == 2, $"{type}/{interval}: max spawn quota is respected");
                scene.RemoveEntity(spawn);
            }
            var dropped = ItemSpawnEntity.SpawnItem(ItemType.UASmall, player.Position, player.NodeRef, 3, scene)!;
            Check(dropped.Process() && dropped.DespawnTimer == 2 && dropped.Process() && dropped.DespawnTimer == 1,
                "finite dropped pickup survives until authored lifetime boundary");
            Check(!dropped.Process() && dropped.DespawnTimer == 0, "finite dropped pickup expires at exact fixed tick"); Retire(dropped);
            Check(sim.StepFailures == 0, "native authority warmup has no simulation failures");
            Console.WriteLine($"MAP PICKUP SCENE PASS: {_checks} checks; all {MapBuilder.MultiplayerItems.Count} supported types, native grant/contact, 1/7/300 interval respawn, quota and dropped lifetime.");
            Console.WriteLine("Scope: real native adapter with fixture placement/clocks. Independent clients, devices and live map acceptance remain separate.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MAP PICKUP SCENE FAIL: " + ex); return 1; }
        finally { sim.Stop(); NetTestScript.Enabled = script; }
    }

    private static void VerifyGrant(PlayerEntity p, ItemType type)
    {
        if (type is ItemType.HealthSmall or ItemType.HealthMedium or ItemType.HealthBig)
        {
            // The legacy native enum names do not sort by the amount: index
            // zero (HealthMedium) grants 30, index one (HealthSmall) grants 60.
            int amount = type == ItemType.HealthMedium ? 30 : type == ItemType.HealthSmall ? 60 : 100;
            Check(p.Health == Math.Min(199, 100 + amount), type + ": native health amount and cap");
        }
        else if (type is ItemType.UASmall or ItemType.UABig or ItemType.MissileSmall or ItemType.MissileBig or ItemType.AffinityWeapon)
        {
            int slot = type is ItemType.UASmall or ItemType.UABig ? 0 : 1;
            int amount = type is ItemType.UABig or ItemType.MissileBig ? 100 : 50;
            Check(Read<int[]>(p, "_ammo")[slot] == Math.Min(Read<int[]>(p, "_ammoMax")[slot], amount), type + ": native ammo amount and cap");
        }
        else if (type is ItemType.DoubleDamage or ItemType.Cloak or ItemType.Deathalt)
        {
            string timer = type == ItemType.DoubleDamage ? "_doubleDmgTimer" : type == ItemType.Cloak ? "_cloakTimer" : "_deathaltTimer";
            Check(Convert.ToInt32(typeof(PlayerEntity).GetField(timer, Hidden)!.GetValue(p)) == 1800, type + ": native timed grant lasts 1800 ticks");
        }
        else Check(p.AvailableWeapons[Enum.Parse<BeamType>(type.ToString())], type + ": native weapon grant is selectable");
    }
}
