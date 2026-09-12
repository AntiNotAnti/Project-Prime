using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.MapGen;

namespace ProjectPrime.MapPlatform.Tests;

public sealed class RuntimeRoomRegistryTests
{
    [Fact]
    public void FirstNameLookupAllocatesReservedRuntimeIdWithoutIdLookup()
    {
        var registry = new RuntimeRoomRegistry();
        InstalledMap map = Map("community.test-arena", "TEST ARENA");

        registry.Synchronize(Snapshot(map), Metadata);

        RuntimeRoomRegistration registration = registry.Require("test arena");
        Assert.Equal(RuntimeRoomRegistry.FirstCustomRoomId,
            registration.RuntimeId);
        Assert.Equal(registration.RuntimeId, registration.Metadata.Id);
        Assert.Equal(registration,
            registry.FindById(registration.RuntimeId));
    }

    [Fact]
    public void CatalogChangesNeverShiftOrReuseAllocatedRuntimeIds()
    {
        var registry = new RuntimeRoomRegistry();
        InstalledMap parallax = Map("community.parallax", "PARALLAX");
        InstalledMap testArena = Map("community.test-arena", "TEST ARENA");
        registry.Synchronize(Snapshot(parallax, testArena), Metadata);
        int parallaxId = registry.Require("PARALLAX").RuntimeId;
        int testArenaId = registry.Require("TEST ARENA").RuntimeId;

        InstalledMap earlier = Map("community.alpha", "ALPHA");
        registry.Synchronize(Snapshot(earlier, parallax, testArena), Metadata);

        Assert.Equal(parallaxId, registry.Require("PARALLAX").RuntimeId);
        Assert.Equal(testArenaId, registry.Require("TEST ARENA").RuntimeId);
        int earlierId = registry.Require("ALPHA").RuntimeId;
        Assert.True(earlierId > Math.Max(parallaxId, testArenaId));

        registry.Synchronize(Snapshot(earlier, testArena), Metadata);
        Assert.Null(registry.FindByName("PARALLAX"));
        Assert.Null(registry.FindById(parallaxId));
        Assert.Equal(parallax.ContentIdentity,
            registry.FindByName("PARALLAX",
                parallax.ContentIdentity)?.ContentIdentity);
        Assert.Null(registry.FindByName("TEST ARENA",
            parallax.ContentIdentity));

        InstalledMap later = Map("community.later", "LATER");
        registry.Synchronize(Snapshot(earlier, testArena, later), Metadata);
        Assert.True(registry.Require("LATER").RuntimeId > earlierId);
        Assert.Equal(testArenaId, registry.Require("TEST ARENA").RuntimeId);
    }

    [Fact]
    public void ExactVersionActivationRetainsRoomIdAndChangesContentIdentity()
    {
        var registry = new RuntimeRoomRegistry();
        InstalledMap current = Map("community.parallax", "PARALLAX",
            new MapVersion(2, 0, 0), '2');
        InstalledMap required = Map("community.parallax", "PARALLAX",
            new MapVersion(1, 0, 0), '1');
        registry.Synchronize(Snapshot(current), Metadata);
        int runtimeId = registry.Require("PARALLAX").RuntimeId;

        RuntimeRoomRegistration activated = registry.RegisterCustom(required,
            Metadata);

        Assert.Equal(runtimeId, activated.RuntimeId);
        Assert.Equal(required.ContentIdentity, activated.ContentIdentity);
        Assert.Equal(runtimeId, activated.Metadata.Id);
    }

    [Fact]
    public void ConcurrentVersionLookupsShareRoomIdButRetainExactMetadata()
    {
        var registry = new RuntimeRoomRegistry();
        InstalledMap first = Map("community.parallax", "PARALLAX",
            new MapVersion(1, 0, 0), '1');
        InstalledMap second = Map("community.parallax", "PARALLAX",
            new MapVersion(2, 0, 0), '2');
        registry.Synchronize(Snapshot(first, second), Metadata);

        RuntimeRoomRegistration firstRoom = Assert.IsType<RuntimeRoomRegistration>(
            registry.FindByName("PARALLAX", first.ContentIdentity));
        RuntimeRoomRegistration secondRoom = Assert.IsType<RuntimeRoomRegistration>(
            registry.FindByName("PARALLAX", second.ContentIdentity));

        Assert.Equal(firstRoom.RuntimeId, secondRoom.RuntimeId);
        Assert.Equal(first.ContentIdentity, firstRoom.ContentIdentity);
        Assert.Equal(second.ContentIdentity, secondRoom.ContentIdentity);
        Parallel.For(0, 128, index =>
        {
            InstalledMap selected = index % 2 == 0 ? first : second;
            RuntimeRoomRegistration exact = registry.FindByName(
                "PARALLAX", selected.ContentIdentity)
                ?? throw new InvalidOperationException("Exact room disappeared.");
            Assert.Equal(selected.ContentIdentity, exact.ContentIdentity);
        });
    }

    [Fact]
    public void DifferentStableMapsMayShareLegacyRoomName()
    {
        var registry = new RuntimeRoomRegistry();
        InstalledMap first = Map("community.first", "NEW MAP", hash: '1');
        InstalledMap second = Map("community.second", "NEW MAP", hash: '2');
        registry.Synchronize(Snapshot(first, second), Metadata);

        RuntimeRoomRegistration firstRoom = Assert.IsType<RuntimeRoomRegistration>(
            registry.FindByName("NEW MAP", first.ContentIdentity));
        RuntimeRoomRegistration secondRoom = Assert.IsType<RuntimeRoomRegistration>(
            registry.FindByName("NEW MAP", second.ContentIdentity));

        Assert.NotEqual(firstRoom.RuntimeId, secondRoom.RuntimeId);
        Assert.Equal(first.ContentIdentity, firstRoom.ContentIdentity);
        Assert.Equal(second.ContentIdentity, secondRoom.ContentIdentity);
        Assert.Contains(registry.Require("NEW MAP"),
            new[] { firstRoom, secondRoom });
        Assert.Equal(2, registry.Snapshot.Rooms.Length);
        Assert.Single(registry.Snapshot.ByName);
    }

    [Fact]
    public void ConcurrentSynchronizationPublishesOneConflictFreeRegistration()
    {
        var registry = new RuntimeRoomRegistry();
        MapCatalogSnapshot snapshot = Snapshot(
            Map("community.concurrent", "CONCURRENT"));

        Parallel.For(0, 64, _ => registry.Synchronize(snapshot, Metadata));

        RuntimeRoomRegistration registration = registry.Require("CONCURRENT");
        Assert.Equal(RuntimeRoomRegistry.FirstCustomRoomId,
            registration.RuntimeId);
        Assert.Single(registry.Snapshot.Rooms);
        Assert.Single(registry.Snapshot.ById);
        Assert.Single(registry.Snapshot.ByName);
        Assert.Single(registry.Snapshot.ByContent);
    }

    private static MapCatalogSnapshot Snapshot(params InstalledMap[] maps)
        => new(1, [.. maps], []);

    private static InstalledMap Map(string stableId, string roomKey,
        MapVersion? version = null, char hash = 'a')
    {
        var project = new MapProject
        {
            StableId = stableId,
            Version = version ?? new MapVersion(1, 0, 0),
            Metadata = new MapProjectMetadata { Name = roomKey },
            Map = new MapDefinition { Name = roomKey }
        };
        return new InstalledMap(new MapContentIdentity(project.Identity,
            new string(hash, 64)), roomKey, "Tests", "", MapInstallSource.LocalProject,
            [MapMode.Battle], MapBuildState.NeedsBuild, null,
            $"/{stableId}.json", null, 0, null, null,
            ImmutableArray<MapDiagnostic>.Empty, project);
    }

    private static RoomMetadata Metadata(InstalledMap map, int id)
        => new(id, map.Project.Map.Name, map.DisplayName, "test", "Model.bin",
            "Anim.bin", "Collision.bin", null, "Ent.bin", "Node.bin", null,
            0, 0, 0, 0, false, false, new ColorRgb(0, 0, 0), 0, 0,
            new ColorRgb(0, 0, 0), default, new ColorRgb(0, 0, 0), default,
            100, -100, RoomSize.Large, multiplayer: true);
}
