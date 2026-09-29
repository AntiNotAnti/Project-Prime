using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Multiplayer;

/// <summary>Inspect the same entity layer and resource profile used by room construction.</summary>
public static class MapModeCapabilities
{
    public static bool Supports(string roomKey, GameMode mode, MatchWorldProfile profile, out string reason, int? configuredPlayers = null)
    {
        if (mode == GameMode.InstaGib) mode = GameMode.Battle;
        if (!profile.IsValid) { reason = "Invalid multiplayer world profile."; return false; }
        if (!Metadata.RoomMetadata.TryGetValue(roomKey, out var room) || room.EntityPath == null || room.FirstHunt)
        { reason = $"{roomKey} has no supported multiplayer entity layer."; return false; }
        var custom = CustomRooms.Definitions.FirstOrDefault(d => String.Equals(d.Name, roomKey, StringComparison.OrdinalIgnoreCase));
        if (custom != null && MapModeValidator.WhyUnsupported(custom, mode,
            configuredPlayers ?? custom.Capabilities?.MinPlayers ?? 1) is string unsupported)
        { reason = unsupported; return false; }
        try
        {
            int layer = SceneSetup.GetMultiplayerEntityLayer(mode, profile.EntityLayerPlayers, profile.Resources);
            var entities = MapResourceRules.Resolve(room, profile.Resources,
                Read.GetEntities(room.EntityPath, layer, false, allowHook: true));
            if (mode is GameMode.Hardpoint or GameMode.HardpointTeams && custom != null
                && (custom.HardpointOrder == null || custom.HardpointOrder.Any(id => !entities.Any(e => e is Entity<NodeDefenseEntityData> node && node.Data.Header.EntityId == id))))
            { reason = "Hardpoint order references an objective missing from the selected layer."; return false; }
            return SupportsEntities(roomKey, mode, entities, out reason);
        }
        catch (Exception ex) when (ex is System.IO.IOException or ProgramException or KeyNotFoundException)
        { reason = $"Cannot inspect {roomKey}: {ex.Message}"; return false; }
    }

    public static bool Supports(MapDefinition map, GameMode mode, int players, out string reason)
    {
        if (mode == GameMode.InstaGib) mode = GameMode.Battle;
        reason = MapModeValidator.WhyUnsupported(map, mode, players) ?? "";
        if (reason.Length != 0) return false;
        if (map.NativeRoom is { PreserveEntities: true } native)
        {
            if (!Metadata.RoomMetadata.TryGetValue(native.Room, out var room) || room.EntityPath == null)
            { reason = $"{map.Name} has an unknown native source room."; return false; }
            try
            {
                var profile = MatchWorldProfile.Resolve(players);
                int layer = SceneSetup.GetMultiplayerEntityLayer(mode, profile.EntityLayerPlayers, profile.Resources);
                var entities = Read.GetEntities(room.EntityPath, layer, false, allowHook: true)
                    .Where(e => !native.EditableSpawns || e.Type != EntityType.PlayerSpawn).ToList();
                foreach (var spawn in map.Spawns)
                {
                    byte[] bytes = new byte[43];
                    bytes[41] = 1; bytes[42] = unchecked((byte)spawn.Team);
                    var data = Read.ReadStruct<PlayerSpawnEntityData>(bytes);
                    var entry = Read.ReadStruct<EntityEntry>(new byte[24]);
                    entities.Add(new Entity<PlayerSpawnEntityData>(entry, EntityType.PlayerSpawn, 0, data, data.Header));
                }
                return SupportsEntities(map.Name, mode, entities, out reason);
            }
            catch (Exception ex) when (ex is System.IO.IOException or ProgramException or KeyNotFoundException)
            { reason = $"Cannot inspect {map.Name}: {ex.Message}"; return false; }
        }
        if (map.Spawns.Count == 0) { reason = $"{map.Name} has no player spawns."; return false; }
        if (mode is GameMode.BattleTeams or GameMode.SurvivalTeams or GameMode.KillConfirmedTeams
            && (!map.Spawns.Any(s => s.Team == 0) || !map.Spawns.Any(s => s.Team == 1)))
        { reason = $"{map.Name} requires spawns for both teams."; return false; }
        if (mode is GameMode.Battle or GameMode.BattleTeams or GameMode.Survival or GameMode.SurvivalTeams or GameMode.PrimeHunter or GameMode.GunGame or GameMode.KillConfirmed or GameMode.KillConfirmedTeams)
        { reason = ""; return true; }
        reason = $"{map.Name} does not supply {mode} objective entities.";
        return false;
    }

    internal static bool SupportsEntities(string name, GameMode mode, IReadOnlyList<Entity> entities, out string reason)
    {
        string? missing = null;
        var spawns = entities.OfType<Entity<PlayerSpawnEntityData>>().Where(e => e.Data.Active != 0).ToArray();
        if (spawns.Length == 0) missing = "active player spawns";
        else if (mode == GameMode.Capture)
        {
            var bases = entities.OfType<Entity<FlagBaseEntityData>>().Select(e => e.Data.TeamId).ToHashSet();
            var flags = entities.OfType<Entity<OctolithFlagEntityData>>().Select(e => e.Data.TeamId).ToHashSet();
            if (!bases.Contains(0) || !bases.Contains(1)) missing = "bases for both teams";
            else if (!flags.Contains(0) || !flags.Contains(1)) missing = "Octoliths for both teams";
            else if (!spawns.Any(e => e.Data.TeamIndex == 0) || !spawns.Any(e => e.Data.TeamIndex == 1))
                missing = "spawns for both teams";
        }
        else if (mode == GameMode.Headhunter)
        {
            if (!entities.Any(e => e.Type == EntityType.FlagBase)) missing = "a token banking base";
        }
        else if (mode == GameMode.Relic)
        {
            if (entities.Count(e => e.Type == EntityType.OctolithFlag) != 1) missing = "exactly one neutral Octolith";
        }
        else if (mode is GameMode.Bounty or GameMode.BountyTeams)
        {
            if (!entities.Any(e => e.Type == EntityType.OctolithFlag)) missing = "a neutral Octolith";
            else if (!entities.Any(e => e.Type == EntityType.FlagBase)) missing = "an Octolith deposit base";
        }
        else if (mode is GameMode.Hardpoint or GameMode.HardpointTeams)
        {
            if (entities.Count(e => e.Type == EntityType.NodeDefense) < 2) missing = "at least two NodeDefense objectives";
        }
        else if (mode is GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams)
        {
            if (!entities.Any(e => e.Type == EntityType.NodeDefense)) missing = "NodeDefense objectives";
        }
        else if (mode is not (GameMode.Battle or GameMode.BattleTeams or GameMode.Survival or GameMode.SurvivalTeams or GameMode.PrimeHunter or GameMode.GunGame or GameMode.KillConfirmed or GameMode.KillConfirmedTeams))
            missing = "a supported multiplayer mode";
        reason = missing == null ? "" : $"{name} cannot run {mode}: missing {missing}.";
        return missing == null;
    }
}
