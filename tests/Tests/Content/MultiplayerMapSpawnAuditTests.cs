using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
[Trait("RequiresGameContent", "true")]
public sealed class MultiplayerMapSpawnAuditTests : IDisposable
{
    private readonly IDisposable _content;

    public MultiplayerMapSpawnAuditTests()
    {
        string data = FindAmhe1();
        _content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");
    }

    public static IEnumerable<object[]> MultiplayerRoomsAndLayers()
    {
        (MatchMode Mode, int[] PlayerCounts)[] layers =
        [
            (MatchMode.Battle, [2, 3, 4]),
            (MatchMode.TeamBattle, [4]),
            (MatchMode.Survival, [2]),
            (MatchMode.TeamSurvival, [4]),
            (MatchMode.Capture, [4]),
            (MatchMode.Bounty, [2, 3, 4]),
            (MatchMode.TeamBounty, [4]),
            (MatchMode.Nodes, [2, 3, 4]),
            (MatchMode.TeamNodes, [4]),
            (MatchMode.Defender, [2]),
            (MatchMode.TeamDefender, [4]),
            (MatchMode.PrimeHunter, [2, 3, 4])
        ];
        foreach (RoomMetadata room in Metadata.RoomList.Where(room =>
            room.Multiplayer && room.Id is >= 93 and <= 118).OrderBy(room => room.Id))
        {
            foreach ((MatchMode mode, int[] playerCounts) in layers)
                foreach (int playerCount in playerCounts)
                    yield return [room.Name, mode, playerCount];
        }
    }

    [Theory]
    [MemberData(nameof(MultiplayerRoomsAndLayers))]
    public void EveryAuthoredMultiplayerLayerHasSafeActiveSpawns(string room,
        MatchMode mode, int playerCount)
    {
        using Scene scene = Open(room, mode, playerCount);
        var spawns = new List<PlayerSpawnEntity>();
        foreach (PlayerSpawnEntity spawn in scene.GetPlayerSpawnEntities())
            spawns.Add(spawn);
        PlayerSpawnEntity[] active = spawns.Where(spawn => spawn.IsActive).ToArray();

        if (active.Length == 0)
        {
            // Retail omits unsupported map/mode layers (for example Capture
            // on Gorea Prison). Battle is the universal baseline and must
            // always remain authored for every multiplayer room.
            Assert.NotEqual(MatchMode.Battle, mode);
            return;
        }
        Assert.Equal(active.Length, active.Select(spawn => spawn.Id).Distinct().Count());
        Assert.All(active, spawn =>
        {
            Assert.NotEqual(NodeRef.None, spawn.NodeRef);
            Assert.True(VectorMath.IsFinite(spawn.Position));
            Assert.True(VectorMath.IsFinite(spawn.FacingVector));
            Assert.True(VectorMath.IsFinite(spawn.UpVector));
        });
        PlayerSpawnEntity[] safe = active.Where(spawn =>
            SpawnGeometry.IsSafe(scene, spawn)).ToArray();
        Assert.NotEmpty(safe);

        foreach (SpawnPolicy policy in new[] { SpawnPolicy.Classic,
            SpawnPolicy.Enhanced })
        {
            scene.Match.ApplyRules(new MatchRules(mode, room,
                maxPlayers: 8, spawnPolicy: policy));
            foreach (PlayerEntity player in scene.GetPlayerEntities())
                player.Health = 0;
            foreach (PlayerSpawnEntity spawn in active) spawn.Cooldown = 0;
            scene.SpawnDirector.Reset((uint)(room.GetHashCode(StringComparison.Ordinal)
                ^ playerCount ^ ((int)mode << 8) ^ (int)policy));
            int teamsToCheck = mode.IsTeamMode() ? 2 : 1;
            for (int team = 0; team < teamsToCheck; team++)
            {
                PlayerEntity requester = scene.Players[team];
                requester.TeamIndex = mode.IsTeamMode() ? team : -1;
                PlayerSpawnEntity selected = Assert.IsType<PlayerSpawnEntity>(
                    scene.SpawnDirector.Select(requester));
                Assert.Contains(selected, safe);
                foreach (PlayerSpawnEntity spawn in active) spawn.Cooldown = 0;
            }
        }
    }

    [Fact]
    public void FourTeamHarvesterCanSelectForEveryConfiguredTeam()
    {
        using Scene scene = Open("MP2 HARVESTER", MatchMode.TeamBattle,
            playerCount: 4);
        scene.Match.ApplyRules(new MatchRules(MatchMode.TeamBattle,
            "MP2 HARVESTER", maxPlayers: 8, spawnPolicy: SpawnPolicy.Enhanced,
            teamCount: 4));
        foreach (PlayerEntity player in scene.GetPlayerEntities()) player.Health = 0;
        PlayerEntity requester = scene.Players[0];

        for (int team = 0; team < 4; team++)
        {
            requester.TeamIndex = team;
            foreach (PlayerSpawnEntity spawn in scene.GetPlayerSpawnEntities())
                spawn.Cooldown = 0;
            scene.SpawnDirector.Reset((uint)(100 + team));
            PlayerSpawnEntity selected = Assert.IsType<PlayerSpawnEntity>(
                scene.SpawnDirector.Select(requester));
            Assert.True(SpawnGeometry.IsSafe(scene, selected));
            Assert.Equal(team >= 2,
                scene.SpawnDirector.LastSelection!.Value.TeamFallback);
        }
    }

    [Theory]
    [InlineData("MP11 BREAKTHROUGH", 4, 6)]
    [InlineData("MP12 SIC TRANSIT", 3, 9)]
    public void FourPlayerBattleLayerHasAuthoredHealthAndSpawnCoverage(
        string room, int minimumHealth, int minimumSpawns)
    {
        using Scene scene = Open(room, MatchMode.Battle, playerCount: 4);

        Assert.True(HealthCount(scene) >= minimumHealth);
        var spawns = new List<PlayerSpawnEntity>();
        foreach (PlayerSpawnEntity spawn in scene.GetPlayerSpawnEntities())
            spawns.Add(spawn);
        Assert.True(spawns.Count >= minimumSpawns);
        Assert.All(spawns, spawn =>
        {
            Assert.True(spawn.IsActive);
            Assert.NotEqual(NodeRef.None, spawn.NodeRef);
            Assert.True(VectorMath.IsFinite(spawn.Position));
            Assert.True(VectorMath.IsFinite(spawn.FacingVector));
        });
    }

    [Theory]
    [InlineData("MP11 BREAKTHROUGH")]
    [InlineData("MP12 SIC TRANSIT")]
    public void FourPlayerLayerIsRicherThanTheTwoPlayerFfaLayer(string room)
    {
        using Scene twoPlayer = Open(room, MatchMode.Battle, playerCount: 2);
        using Scene fourPlayer = Open(room, MatchMode.Battle, playerCount: 4);

        Assert.True(HealthCount(fourPlayer) > HealthCount(twoPlayer));
    }

    [Theory]
    [InlineData("MP11 BREAKTHROUGH")]
    [InlineData("MP12 SIC TRANSIT")]
    public void TwoVersusTwoTeamBattleLayerIncludesHealth(string room)
    {
        using Scene scene = Open(room, MatchMode.TeamBattle, playerCount: 4);
        Assert.True(HealthCount(scene) > 0);
    }

    private static Scene Open(string room, MatchMode mode, int playerCount)
    {
        Scene scene = Scene.CreateHeadless();
        scene.LoadServerRoom(room, mode.ToLegacyMode(), players: 8,
            roomPlayerCount: playerCount);
        return scene;
    }

    private static int HealthCount(Scene scene)
    {
        int count = 0;
        foreach (ItemSpawnEntity item in scene.GetItemSpawnEntities())
            if (item.Data.ItemType is ItemType.HealthSmall or ItemType.HealthBig)
                count++;
        return count;
    }

    private static string FindAmhe1()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? [Directory.GetCurrentDirectory(), AppContext.BaseDirectory]
            : [configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory];
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "AMHE1");
                if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                    && Directory.Exists(Path.Combine(candidate, "models"))
                    && Directory.Exists(Path.Combine(candidate, "levels")))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
    }

    public void Dispose() => _content.Dispose();
}
