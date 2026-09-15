using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Hud.Radar;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class RadarFeatureTests
{
    [Fact]
    public void TransformedVolumeAnchorCoversEveryPlayableHunterAndStaysHorizontal()
    {
        int index = 0;
        foreach (Hunter hunter in PlayableHunterCatalog.All)
        {
            Vector3 actor = new(index * 3, 20 + index, -index * 2);
            Vector3 transformedCenter = actor + new Vector3(0, .5f + index, 0);
            Vector3 anchor = RadarAnchorResolver.Resolve(actor,
                new CollisionVolume(transformedCenter, 1));

            Assert.Equal(transformedCenter, anchor);
            Assert.Equal(actor.X, anchor.X);
            Assert.Equal(actor.Z, anchor.Z);
            index++;
        }
        Assert.Equal(PlayableHunterCatalog.Count, index);
    }

    [Fact]
    public void MorphAnchorUsesTheActiveVolumeWithoutASecondElevationCue()
    {
        Vector3 actor = new(4, 10, -8);
        CollisionVolume bipedDuringMorph = new(actor + new Vector3(0, .75f, 0), .8f);

        Assert.Equal(actor + new Vector3(0, .75f, 0),
            RadarAnchorResolver.Resolve(actor, bipedDuringMorph));
        Assert.Equal(actor,
            RadarAnchorResolver.Resolve(actor, default));
    }

    [Fact]
    public void AnchorUsesTheCenterOfEveryTransformedVolumeKindAndRejectsNonfiniteInput()
    {
        Vector3 actor = new(40, 10, -20);
        CollisionVolume box = new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            new Vector3(2, 3, 4), 2, 4, 6);
        CollisionVolume cylinder = new(Vector3.UnitY, new Vector3(1, 2, 3),
            1, 4);
        CollisionVolume sphere = new(new Vector3(8, 9, 10), 1);

        Assert.Equal(new Vector3(3, 5, 7), RadarAnchorResolver.Resolve(actor, box));
        Assert.Equal(new Vector3(1, 4, 3), RadarAnchorResolver.Resolve(actor, cylinder));
        Assert.Equal(new Vector3(8, 9, 10), RadarAnchorResolver.Resolve(actor, sphere));
        Assert.Equal(Vector3.Zero, RadarAnchorResolver.Resolve(
            new Vector3(float.NaN, float.PositiveInfinity, 0), default));
    }

    [Fact]
    public void ResourceClassifierFailsClosedAndMapsAllKnownCategories()
    {
        Assert.False(RadarResourceClassifier.TryClassify(ItemType.None, out _));
        Assert.False(RadarResourceClassifier.TryClassify((ItemType)99, out _));
        Assert.True(RadarResourceClassifier.TryClassify(ItemType.VoltDriver,
            out RadarResource weapon));
        Assert.Equal(RadarResource.Weapon, weapon);
        Assert.True(RadarResourceClassifier.TryClassify(ItemType.MissileBig,
            out RadarResource ammo));
        Assert.Equal(RadarResource.Ammo, ammo);
        Assert.True(RadarResourceClassifier.TryClassify(ItemType.HealthSmall,
            out RadarResource health));
        Assert.Equal(RadarResource.Health, health);
        Assert.True(RadarResourceClassifier.TryClassify(ItemType.Cloak,
            out RadarResource powerup));
        Assert.Equal(RadarResource.Powerup, powerup);
        Assert.True(RadarResourceClassifier.TryClassify(ItemType.AffinityWeapon,
            out RadarResource affinity));
        Assert.Equal(RadarResource.AffinityWeapon, affinity);
        Assert.True(RadarResourceClassifier.TryClassify(ItemType.OmegaCannon,
            out RadarResource omega));
        Assert.Equal(RadarResource.OmegaCannon, omega);
    }

    [Fact]
    public void ResourcePolicyAdmitsOnlyItsAuthoritativeCategoryAndDeduplicatesSpawners()
    {
        var candidates = new[]
        {
            new RadarResourceCandidate(ItemType.VoltDriver, new Vector3(1, 0, 0),
                SourceId: 10, IsSpawner: false, HasLiveItem: true, OwnerId: 20),
            new RadarResourceCandidate(ItemType.VoltDriver, new Vector3(1, 0, 0),
                SourceId: 20, IsSpawner: true, Active: true, HasLiveItem: true),
            new RadarResourceCandidate(ItemType.HealthSmall, new Vector3(2, 0, 0),
                SourceId: 30, IsSpawner: true, Active: true, HasLiveItem: false,
                RespawnTicks: 12),
            new RadarResourceCandidate(ItemType.DoubleDamage, new Vector3(3, 0, 0),
                SourceId: 31, IsSpawner: false, HasLiveItem: true),
            new RadarResourceCandidate((ItemType)99, new Vector3(4, 0, 0))
        };

        var disabled = NewFrame();
        Assert.Equal(0, RadarResourceCollector.Append(disabled,
            ResourceRadarPolicy.Disabled, candidates));

        var spawnLocations = NewFrame();
        Assert.Equal(2, RadarResourceCollector.Append(spawnLocations,
            ResourceRadarPolicy.SpawnLocations, candidates));
        Assert.Equal(2, spawnLocations.Contacts.Length);
        Assert.All(spawnLocations.Contacts.ToArray(),
            contact => Assert.Equal(RadarContactType.Resource, contact.Type));

        var available = NewFrame();
        Assert.Equal(2, RadarResourceCollector.Append(available,
            ResourceRadarPolicy.AvailableResources, candidates));
        Assert.DoesNotContain(available.Contacts.ToArray(),
            contact => contact.RespawnTicks != 0);

        var withRespawn = NewFrame();
        Assert.Equal(3, RadarResourceCollector.Append(withRespawn,
            ResourceRadarPolicy.AvailableWithRespawn, candidates));
        Assert.Contains(withRespawn.Contacts.ToArray(),
            contact => contact.RespawnTicks == 12
                && contact.Resource == RadarResource.Health);
        Assert.DoesNotContain(withRespawn.Contacts.ToArray(),
            contact => contact.Position == new Vector3(1, 0, 0)
                && contact.RespawnTicks != 0);
    }

    [Fact]
    public void SceneResourceCollectorGatesReplicasButAllowsLocalAuthority()
    {
        using Scene scene = Scene.CreateHeadless();
        RadarFrame frame = NewFrame();

        Assert.True(RadarResourceCollector.CanReadCommittedScene(scene));
        scene.RequiresCommittedReplicatedWorldState = true;
        Assert.False(scene.HasCommittedReplicatedWorldState);
        Assert.False(RadarResourceCollector.CanReadCommittedScene(scene));
        Assert.Equal(0, RadarResourceCollector.AppendCommittedScene(frame,
            scene, ResourceRadarPolicy.AvailableWithRespawn));
        Assert.Empty(frame.Contacts.ToArray());

        scene.HasCommittedReplicatedWorldState = true;
        Assert.True(RadarResourceCollector.CanReadCommittedScene(scene));
    }

    [Fact]
    public void ResourcePresentationFlagsColorsAndShapesRoundTrip()
    {
        RadarProfile source = new()
        {
            ShowResources = false,
            ShowWeapons = false,
            ShowAmmo = true,
            ShowHealth = false,
            ShowPowerups = true,
            ColorPreset = RadarColorPreset.Custom,
            Colors = new RadarColors { Weapon = new RadarColor(1, 2, 3, 4) }
        };
        RadarProfile restored = RadarProfileSerializer.Import(
            RadarProfileSerializer.Export(source));

        Assert.False(restored.ShowResources);
        Assert.False(restored.ShowWeapons);
        Assert.True(restored.ShowAmmo);
        Assert.False(restored.ShowHealth);
        Assert.Equal(new RadarColor(1, 2, 3, 4), restored.Colors.Weapon);
        RadarContact weapon = new(RadarContactType.Resource, Vector3.Zero, -1,
            RadarObjective.None, 1, Resource: RadarResource.Weapon);
        Assert.False(RadarPresentationPolicy.IsVisible(restored, weapon));
        Assert.Equal(RadarMarkerShape.Square,
            RadarPresentationPolicy.MarkerShape(weapon));
        Assert.Equal(RadarResource.Weapon,
            weapon.Resource);
    }

    [Fact]
    public void ResourcePriorityRemainsBelowPlayersAndObjectives()
    {
        RadarContact resource = new(RadarContactType.Resource, Vector3.Zero, -1,
            RadarObjective.None, 1, Resource: RadarResource.Ammo);
        RadarContact enemy = new(RadarContactType.Enemy, Vector3.Zero, 0,
            RadarObjective.None, 1);
        RadarContact objective = new(RadarContactType.Objective, Vector3.Zero, -1,
            RadarObjective.Flag, 1);

        Assert.True(RadarPresentationPolicy.Priority(resource)
            < RadarPresentationPolicy.Priority(enemy));
        Assert.True(RadarPresentationPolicy.Priority(resource)
            < RadarPresentationPolicy.Priority(objective));

        int ammo = RadarPresentationPolicy.Priority(resource);
        int health = RadarPresentationPolicy.Priority(resource with
        {
            Resource = RadarResource.Health
        });
        int weapon = RadarPresentationPolicy.Priority(resource with
        {
            Resource = RadarResource.Weapon
        });
        int powerup = RadarPresentationPolicy.Priority(resource with
        {
            Resource = RadarResource.Powerup
        });
        Assert.True(ammo < health && health < weapon && weapon < powerup);
        Assert.Equal(powerup, RadarPresentationPolicy.Priority(resource with
        {
            Resource = RadarResource.OmegaCannon
        }));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    public void RadarRasterizerStaysBoundedAcrossSupportedResolutions(int resolution)
    {
        RadarPolygon polygon = new(new[]
        {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1),
            new Vector2(-1, 1)
        }, 0, RadarSurfaceKind.Floor);
        RadarMapGeometry geometry = RadarMapBuilder.BuildGeometry(new[] { polygon });
        RadarMap map = RadarMapRasterizer.Rasterize(geometry, resolution,
            RadarMapRasterLayers.Outline);

        Assert.Equal(resolution, map.Width);
        Assert.Equal(resolution, map.Height);
        Assert.All(map.Floors, floor => Assert.Equal(resolution * resolution,
            floor.Pixels.Count));
    }

    [Fact]
    public void RadarPresetAndResourceMatrixHasFiniteReadablePresentationValues()
    {
        foreach (RadarPreset preset in Enum.GetValues<RadarPreset>())
        {
            RadarProfile profile = RadarProfile.Create(preset).Normalize();
            Assert.True(float.IsFinite(profile.Scale));
            Assert.True(float.IsFinite(profile.Range));
            Assert.True(float.IsFinite(profile.ElevationThreshold));
        }

        RadarProfile readable = RadarProfile.Create(RadarPreset.Accessibility);
        RadarContact enemy = new(RadarContactType.Enemy, Vector3.Zero, 0,
            RadarObjective.None, 1);
        foreach (RadarResource resource in new[]
        {
            RadarResource.Weapon, RadarResource.Ammo,
            RadarResource.Health, RadarResource.Powerup,
            RadarResource.AffinityWeapon, RadarResource.OmegaCannon
        })
        {
            RadarContact contact = enemy with
            {
                Type = RadarContactType.Resource,
                Resource = resource
            };
            Assert.True(RadarPresentationPolicy.IsVisible(readable, contact));
            RadarMarkerShape expectedShape = resource switch
            {
                RadarResource.Weapon or RadarResource.AffinityWeapon
                    => RadarMarkerShape.Square,
                RadarResource.Ammo => RadarMarkerShape.Diamond,
                RadarResource.Health => RadarMarkerShape.Triangle,
                _ => RadarMarkerShape.DoubleDiamond
            };
            Assert.Equal(expectedShape, RadarPresentationPolicy.MarkerShape(contact));
            Assert.False(String.IsNullOrEmpty(RadarWidget.Symbol(contact)));
            Vector4 color = RadarPresentationPolicy.Color(readable, contact,
                RadarElevation.Same);
            Assert.True(float.IsFinite(color.X) && float.IsFinite(color.Y)
                && float.IsFinite(color.Z) && float.IsFinite(color.W));
            Assert.True(RadarPresentationPolicy.Priority(contact)
                < RadarPresentationPolicy.Priority(enemy));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(256)]
    public void RadarContactDensityNeverExceedsTheFixedCapacity(int density)
    {
        RadarFrame frame = NewFrame();
        for (int i = 0; i < density; i++)
        {
            Assert.True(frame.AddApproved(new(RadarContactType.Resource,
                new Vector3(i, 0, 0), -1, RadarObjective.None, 1,
                Resource: RadarResource.Ammo)) || i >= RadarFrame.Capacity);
        }

        Assert.InRange(frame.Contacts.Length, 0, RadarFrame.Capacity);
        Assert.Equal(Math.Max(0, density - RadarFrame.Capacity), frame.Dropped);
    }

    [Fact]
    public void MatchRulesResourcePolicyAppendsByteAndLegacyReadersDefaultOff()
    {
        MatchRules rules = new(MatchMode.Battle, "RADAR",
            resourceRadarPolicy: ResourceRadarPolicy.AvailableWithRespawn);
        byte[] bytes = new byte[MatchRulesWire.Size];
        MatchRulesWire.Write(bytes, rules);

        Assert.Equal(86, MatchRulesWire.Size);
        Assert.Equal(85, MatchRulesWire.LegacyProtocol24Size);
        Assert.Equal(84, MatchRulesWire.LegacyProtocol23Size);
        Assert.Equal((byte)ResourceRadarPolicy.AvailableWithRespawn, bytes[85]);
        Assert.True(MatchRulesWire.TryRead(bytes, out MatchRules current));
        Assert.Equal(rules, current);

        Assert.True(MatchRulesWire.TryReadLegacyProtocol24(
            bytes.AsSpan(0, MatchRulesWire.LegacyProtocol24Size),
            out MatchRules legacy24));
        Assert.Equal(ResourceRadarPolicy.Disabled, legacy24.ResourceRadarPolicy);
        Assert.True(MatchRulesWire.TryReadLegacyProtocol23(
            bytes.AsSpan(0, MatchRulesWire.LegacyProtocol23Size),
            out MatchRules legacy23));
        Assert.Equal(ResourceRadarPolicy.Disabled, legacy23.ResourceRadarPolicy);

        bytes[85] = 4;
        Assert.False(MatchRulesWire.TryRead(bytes, out _));
    }

    [Fact]
    public void RoomCollisionRevisionChangesOnlyThroughMutationsAndFingerprintTracksContent()
    {
        using Scene scene = Scene.CreateHeadless();
        var room = new RoomEntity(scene);
        CollisionInstance first = Collision("first");
        CollisionInstance second = Collision("second");

        ulong initial = room.RadarGeometryRevision;
        Assert.True(room.AddRoomCollision(first));
        Assert.Equal(initial + 1, room.RadarGeometryRevision);
        ulong unchanged = room.RadarGeometryRevision;
        Assert.False(room.SetRoomCollisionActive(first, first.Active));
        Assert.Equal(unchanged, room.RadarGeometryRevision);
        Assert.True(room.SetRoomCollisionActive(first, false));
        ulong afterActive = room.RadarGeometryRevision;
        Assert.Equal(unchanged + 1, afterActive);
        Assert.True(room.SetRoomCollisionTranslation(first, new Vector3(1, 2, 3)));
        ulong afterTranslation = room.RadarGeometryRevision;
        Assert.Equal(afterActive + 1, afterTranslation);
        Assert.True(room.ReplaceRoomCollision(first, second));
        ulong afterReplace = room.RadarGeometryRevision;
        Assert.Equal(afterTranslation + 1, afterReplace);
        Assert.True(room.RemoveRoomCollision(second));
        Assert.Equal(afterReplace + 1, room.RadarGeometryRevision);
        Assert.Equal(0, room.RoomCollision.Count);

        room.SetupRoomCollision(first);
        ulong afterSetup = room.RadarGeometryRevision;
        room.SetupRoomCollision(second);
        Assert.Equal(afterSetup + 1, room.RadarGeometryRevision);
        Assert.Same(second, room.RoomCollision[0]);

        CollisionInstance fingerprintA = Collision("same");
        CollisionInstance fingerprintB = Collision("same");
        int before = RadarMapBuilder.ComputeCollisionFingerprint(new[] { fingerprintA });
        fingerprintB.Translation = new Vector3(4, 0, 0);
        int after = RadarMapBuilder.ComputeCollisionFingerprint(new[] { fingerprintB });
        Assert.NotEqual(before, after);
    }

    private static RadarFrame NewFrame()
    {
        var frame = new RadarFrame();
        frame.Begin(Vector3.Zero, Vector3.UnitZ, 1);
        return frame;
    }

    private static CollisionInstance Collision(string name)
    {
        var info = new MphCollisionInfo(default,
            Array.Empty<Vector3Fx>(), Array.Empty<Vector4Fx>(),
            Array.Empty<ushort>(), Array.Empty<CollisionData>(),
            Array.Empty<ushort>(), Array.Empty<CollisionEntry>(),
            Array.Empty<Portal>());
        return new CollisionInstance(name, info, isEntity: false);
    }
}
