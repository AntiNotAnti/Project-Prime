using System;
using MphRead.Formats;

namespace MphRead.Entities;

/// <summary>A small deterministic utility layer for native-authoritative bot map control.</summary>
internal enum BotSquadRole : byte
{
    Vanguard, Anchor, Flanker
}

internal static class BotStrategicPolicy
{
    internal static bool IsNodeMode(GameMode mode)
        => mode is GameMode.NodesTeams or GameMode.DefenderTeams or GameMode.HardpointTeams;

    internal static BotSquadRole RoleForOrdinal(int ordinal, bool objectiveMode)
    {
        int rank = Math.Max(0, ordinal) % 3;
        if (objectiveMode)
        {
            return rank switch
            {
                0 => BotSquadRole.Anchor,
                1 => BotSquadRole.Vanguard,
                _ => BotSquadRole.Flanker
            };
        }
        return rank switch
        {
            0 => BotSquadRole.Vanguard,
            1 => BotSquadRole.Flanker,
            _ => BotSquadRole.Anchor
        };
    }

    internal static bool ShouldDisengage(bool teamMode, int visibleEnemies,
        int nearbyAllies, float healthFraction)
    {
        if (!teamMode || !float.IsFinite(healthFraction)) return false;
        return visibleEnemies >= 2 && visibleEnemies > nearbyAllies + 1
            && healthFraction < .72f;
    }

    internal static float TargetBonus(BotSquadRole role, bool objectiveThreat, bool recentAttacker)
    {
        if (!objectiveThreat && !recentAttacker) return 0f;
        return role switch
        {
            BotSquadRole.Anchor => objectiveThreat ? 15f : 2f,
            BotSquadRole.Flanker => recentAttacker ? 10f : 5f,
            _ => objectiveThreat ? 8f : 6f
        };
    }

    internal static float FocusPenalty(bool objectiveCarrier,
        int alliesAlreadyEngaged, int botLevel)
    {
        if (alliesAlreadyEngaged <= 0 || botLevel < 2) return 0f;
        float cost = objectiveCarrier ? 3f : 4f + botLevel * 2f;
        return Math.Min(objectiveCarrier ? 10f : 26f, alliesAlreadyEngaged * cost);
    }

    internal static float NodeAssignmentPenalty(int claims, bool hardpoint, bool contested)
    {
        int desired = hardpoint ? (contested ? 3 : 2) : (contested ? 2 : 1);
        return Math.Max(0, claims - desired + 1) * (hardpoint ? 14f : 24f);
    }

    internal static float NodeScore(BotSquadRole role, float distance,
        bool owned, bool contested, bool hostileOccupier, int teammateClaims,
        int nearbyTeammates, bool hardpoint)
    {
        if (!float.IsFinite(distance) || distance < 0) return float.NegativeInfinity;
        float score = owned ? (role == BotSquadRole.Anchor ? 33f : 4f) : 44f;
        if (contested) score += 37f;
        if (hostileOccupier) score += 19f;
        if (role == BotSquadRole.Flanker && owned && !contested) score -= 18f;
        if (role == BotSquadRole.Anchor && owned && !contested) score += 9f;
        score -= Math.Clamp(teammateClaims, 0, 7) * (hardpoint ? 3f : 10f);
        score -= NodeAssignmentPenalty(teammateClaims, hardpoint, contested);
        score -= Math.Clamp(nearbyTeammates, 0, 7) * (hardpoint ? 2f : 7f);
        // Keep nearby objectives preferable without ignoring all reachable
        // distant nodes on larger arenas.
        float travelPenalty = Math.Min(distance, 9f) * 2.6f
            + Math.Max(0f, distance - 9f) * 0.35f;
        return score - travelPenalty;
    }

    internal static bool PickupAllowedForMode(ItemType type, bool weaponLocked)
    {
        if (!weaponLocked) return true;
        // In locked-weapon modes, native rules own the loadout. A bot may
        // still recover health but must not abandon the match to fetch a
        // weapon/ammo/powerup that its active ruleset cannot use.
        return type is ItemType.HealthSmall or ItemType.HealthMedium
            or ItemType.HealthBig or ItemType.EnergyTank;
    }

    internal static float PickupScore(ItemType type, float healthFraction,
        bool alreadyOwned, float uaFraction, float missileFraction)
    {
        if (!float.IsFinite(healthFraction) || !float.IsFinite(uaFraction)
            || !float.IsFinite(missileFraction))
        {
            return float.NegativeInfinity;
        }
        healthFraction = Math.Clamp(healthFraction, 0f, 1f);
        uaFraction = Math.Clamp(uaFraction, 0f, 1f);
        missileFraction = Math.Clamp(missileFraction, 0f, 1f);
        return type switch
        {
            ItemType.HealthBig => (1 - healthFraction) * 92f,
            ItemType.HealthMedium => (1 - healthFraction) * 63f,
            ItemType.HealthSmall => (1 - healthFraction) * 40f,
            ItemType.EnergyTank => (1 - healthFraction) * 100f,
            ItemType.DoubleDamage => 72f,
            ItemType.OmegaCannon => 82f,
            ItemType.Cloak => 45f,
            ItemType.MissileBig => (1 - missileFraction) * 46f,
            ItemType.MissileSmall => (1 - missileFraction) * 28f,
            ItemType.UABig => (1 - uaFraction) * 44f,
            ItemType.UASmall => (1 - uaFraction) * 27f,
            ItemType.VoltDriver or ItemType.Battlehammer or ItemType.Imperialist
                or ItemType.Judicator or ItemType.Magmaul or ItemType.ShockCoil
                or ItemType.AffinityWeapon => alreadyOwned ? 8f : 49f,
            _ => float.NegativeInfinity
        };
    }
}
