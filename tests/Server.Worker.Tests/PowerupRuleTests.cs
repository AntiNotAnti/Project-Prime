using MphRead.Entities;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class PowerupRuleTests
{
    [Fact]
    [Trait("RequiresGameContent", "true")]
    public void DisabledPowerupsRemoveOnlyMajorPowerups()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using IDisposable content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");

        ItemType[] blocked =
        [
            ItemType.DoubleDamage,
            ItemType.Cloak,
            ItemType.Deathalt,
            ItemType.OmegaCannon
        ];
        Assert.All(Enum.GetValues<ItemType>(), type =>
            Assert.Equal(blocked.Contains(type), ItemSpawnEntity.IsMajorPowerup(type)));

        const string room = "MP1 SANCTORUS";
        using var disabled = new ServerSimulation(new MatchRules(MatchMode.Battle,
            room, maxPlayers: 8, powerupsEnabled: false));
        Assert.Equal(0, MajorPowerupSpawnerCount(disabled.Scene));
        Assert.True(ItemSpawnerCount(disabled.Scene) > 0);
        Assert.NotNull(ItemSpawnEntity.SpawnItem(ItemType.HealthSmall,
            Vector3.Zero, NodeRef.None, despawnTime: 60, scene: disabled.Scene));
        Assert.NotNull(ItemSpawnEntity.SpawnItem(ItemType.UASmall,
            Vector3.Zero, NodeRef.None, despawnTime: 60, scene: disabled.Scene));
        Assert.NotNull(ItemSpawnEntity.SpawnItem(ItemType.VoltDriver,
            Vector3.Zero, NodeRef.None, despawnTime: 60, scene: disabled.Scene));
        foreach (ItemType type in blocked)
            Assert.Null(ItemSpawnEntity.SpawnItem(type, Vector3.Zero,
                NodeRef.None, despawnTime: 60, scene: disabled.Scene));
    }

    private static int ItemSpawnerCount(Scene scene)
    {
        int count = 0;
        foreach (ItemSpawnEntity _ in scene.GetItemSpawnEntities()) count++;
        return count;
    }

    private static int MajorPowerupSpawnerCount(Scene scene)
    {
        int count = 0;
        foreach (ItemSpawnEntity item in scene.GetItemSpawnEntities())
            if (ItemSpawnEntity.IsMajorPowerup(item.Data.ItemType)) count++;
        return count;
    }
}
