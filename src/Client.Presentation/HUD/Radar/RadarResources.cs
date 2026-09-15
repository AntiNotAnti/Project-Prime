using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

/// <summary>Small, testable description of a resource supplied to the radar.</summary>
public readonly record struct RadarResourceCandidate(
    ItemType ItemType,
    Vector3 Position,
    int SourceId = -1,
    bool IsSpawner = false,
    bool Active = true,
    bool HasLiveItem = false,
    int OwnerId = -1,
    uint RespawnTicks = 0);

/// <summary>Maps only known retail/current item identities to radar categories.</summary>
public static class RadarResourceClassifier
{
    public static bool TryClassify(ItemType itemType, out RadarResource resource)
    {
        resource = itemType switch
        {
            ItemType.VoltDriver or ItemType.Battlehammer or ItemType.Imperialist
                or ItemType.Judicator or ItemType.Magmaul or ItemType.ShockCoil
                => RadarResource.Weapon,
            ItemType.AffinityWeapon => RadarResource.AffinityWeapon,
            ItemType.UASmall or ItemType.UABig or ItemType.MissileExpansion
                or ItemType.MissileSmall or ItemType.MissileBig or ItemType.UAExpansion
                or ItemType.PickWpnMissile => RadarResource.Ammo,
            ItemType.HealthSmall or ItemType.HealthMedium or ItemType.HealthBig
                or ItemType.EnergyTank => RadarResource.Health,
            ItemType.DoubleDamage or ItemType.Cloak or ItemType.Deathalt
                or ItemType.ArtifactKey => RadarResource.Powerup,
            ItemType.OmegaCannon => RadarResource.OmegaCannon,
            _ => RadarResource.None
        };
        return resource != RadarResource.None;
    }

    public static bool IsKnown(RadarResource resource)
        => resource is RadarResource.Weapon or RadarResource.Ammo
            or RadarResource.Health or RadarResource.Powerup
            or RadarResource.AffinityWeapon or RadarResource.OmegaCannon;
}

/// <summary>
/// Admits authoritative resource/spawner records into the bounded radar frame.
/// Profile switches are intentionally absent here; they are presentation-only
/// filters applied after this authoritative policy boundary.
/// </summary>
public static class RadarResourceCollector
{
    public static int Append(RadarFrame frame, ResourceRadarPolicy policy,
        IEnumerable<RadarResourceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(candidates);
        if (policy == ResourceRadarPolicy.Disabled) return 0;

        Span<int> seenSources = stackalloc int[WorldPacket.Capacity];
        int seenCount = 0;
        int admitted = 0;
        foreach (RadarResourceCandidate candidate in candidates)
        {
            if (TryAppend(frame, policy, candidate, seenSources, ref seenCount))
                admitted++;
        }
        return admitted;
    }

    /// <summary>
    /// Builds candidates from the scene after its committed world replica has
    /// been applied. It never reads network records or predicts missing items.
    /// </summary>
    public static int AppendCommittedScene(RadarFrame frame, Scene scene,
        ResourceRadarPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (policy == ResourceRadarPolicy.Disabled
            || !CanReadCommittedScene(scene))
        {
            return 0;
        }

        Span<int> seenSources = stackalloc int[WorldPacket.Capacity];
        int seenCount = 0;
        int admitted = 0;
        if (policy != ResourceRadarPolicy.SpawnLocations)
        {
            foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities())
            {
                // Replica items use -1 while live; a zero timer is the pending
                // retirement state and must not leave a stale marker behind.
                if (item.DespawnTimer == 0) continue;
                var candidate = new RadarResourceCandidate(item.ItemType,
                    item.Position, item.Id,
                    IsSpawner: false, Active: true, HasLiveItem: true,
                    OwnerId: item.Owner?.Id ?? -1);
                if (TryAppend(frame, policy, candidate, seenSources,
                        ref seenCount))
                    admitted++;
            }
        }

        if (policy is ResourceRadarPolicy.SpawnLocations
            or ResourceRadarPolicy.AvailableWithRespawn)
        {
            foreach (ItemSpawnEntity spawner in scene.GetItemSpawnEntities())
            {
                bool hasLiveItem = spawner.Item is { DespawnTimer: not 0 };
                var candidate = new RadarResourceCandidate(
                    spawner.Data.ItemType, spawner.Position,
                    spawner.Id, IsSpawner: true, Active: spawner.Active,
                    HasLiveItem: hasLiveItem,
                    RespawnTicks: spawner.ServerRespawnTicks);
                if (TryAppend(frame, policy, candidate, seenSources,
                        ref seenCount))
                    admitted++;
            }
        }
        return admitted;
    }

    internal static bool CanReadCommittedScene(Scene scene)
        => !scene.RequiresCommittedReplicatedWorldState
            || scene.HasCommittedReplicatedWorldState;

    private static bool TryAppend(RadarFrame frame, ResourceRadarPolicy policy,
        in RadarResourceCandidate candidate, Span<int> seenSources,
        ref int seenCount)
    {
        if (!RadarResourceClassifier.TryClassify(candidate.ItemType,
                out RadarResource resource)
            || !Finite(candidate.Position)
            || candidate.IsSpawner && !candidate.Active)
        {
            return false;
        }

        bool include = policy switch
        {
            ResourceRadarPolicy.SpawnLocations => candidate.IsSpawner,
            ResourceRadarPolicy.AvailableResources => !candidate.IsSpawner
                && candidate.HasLiveItem,
            ResourceRadarPolicy.AvailableWithRespawn => candidate.IsSpawner
                ? !candidate.HasLiveItem : candidate.HasLiveItem,
            _ => false
        };
        if (!include) return false;

        // Entity ids are globally unique in a committed scene. A live item
        // owned by a spawner uses the owner id, so a malformed/duplicated
        // spawner record cannot create a second marker for that pickup.
        int identity = candidate.OwnerId >= 0
            ? candidate.OwnerId : candidate.SourceId;
        if (identity >= 0)
        {
            for (int i = 0; i < seenCount; i++)
            {
                if (seenSources[i] == identity) return false;
            }
            // The committed world packet is bounded to this span. Fail closed
            // if a synthetic caller exceeds that authority boundary rather
            // than admitting a contact we can no longer deduplicate.
            if (seenCount == seenSources.Length) return false;
            seenSources[seenCount++] = identity;
        }

        return frame.AddApproved(new RadarContact(RadarContactType.Resource,
            candidate.Position, -1, RadarObjective.None, 1,
            Resource: resource, RespawnTicks: candidate.RespawnTicks));
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
