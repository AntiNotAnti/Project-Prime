using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class PlayerIsolationTests
{
    [Fact]
    public void ConstructingAndResettingAnotherScenePreservesBodiesCountsNamesAndResults()
    {
        using Scene first = new(headless: true);
        PlayerEntity player = first.Players[0];
        first.Players.MaxPlayers = 6;
        first.Players.ActiveCount = 1;
        first.Roster.Nicknames[0] = "First";
        player.Health = 73;
        player.TeamIndex = 1;
        player.LoadFlags = LoadFlags.Active;
        first.Match.Players[0].Points = 8;
        using Scene second = new(headless: true);
        second.Players.MaxPlayers = 2;
        second.Players.ActiveCount = 2;
        second.Roster.Nicknames[0] = "Second";
        second.Players[0].Health = 12;
        second.Match.Players[0].Points = 3;
        first.Match.CaptureResult(1);
        second.Match.CaptureResult(2);
        second.Players.Reset();

        Assert.Same(player, first.Players[0]);
        Assert.Same(first, player.Scene);
        Assert.Equal(73, player.Health);
        Assert.Equal(6, first.Players.MaxPlayers);
        Assert.Equal(1, first.Players.ActiveCount);
        Assert.Equal("First", first.Roster.Nicknames[0]);
        Assert.Equal("First", first.Match.Result!.Players[0].Nickname);
        Assert.Equal(8, first.Match.Result.Players[0].Points);
        Assert.Equal("Second", second.Match.Result!.Players[0].Nickname);
        Assert.Equal(3, second.Match.Result.Players[0].Points);
        Assert.Equal(0, second.Players.ActiveCount);
        Assert.Equal(2, second.Players.MaxPlayers);
        for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
        {
            Assert.NotSame(first.Players[slot], second.Players[slot]);
            Assert.Same(second, second.Players[slot].Scene);
        }
        Assert.Equal(-1, first.LocalPlayerSlot);
        Assert.Null(first.LocalPlayer);
        Assert.False(player.IsMainPlayer);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.LocalPlayerSlot = 0);
    }

    [Fact]
    public void SceneConstructionKeepsExistingLocalSelectionAndDefaultNamesIndependent()
    {
        using Scene first = new();
        first.LocalPlayerSlot = 3;
        first.Roster.Nicknames[3] = "Existing";
        using Scene second = new();
        Assert.Same(first.Players[3], first.LocalPlayer);
        Assert.Equal("Existing", first.Roster.Nicknames[3]);
        Assert.Equal("Player4", second.Roster.Nicknames[3]);
        Assert.Same(second.Players[0], second.LocalPlayer);
        first.InsertEntity(first.Players[3]);
        Assert.Throws<InvalidOperationException>(() => first.Players.Reset());
        Assert.Same(first.Players[3], first.LocalPlayer);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void SlotCreationUsesOnlyTheOwningScenesCapacityAndCursor()
    {
        using var content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(FindAmhe1(), "AMHE1");
        bool serverMode = Read.ServerMode;
        Read.ServerMode = true;
        try
        {
            using Scene first = new(headless: true);
            first.Players.MaxPlayers = 2;
            PlayerEntity? firstSlot = first.Players.Create(Hunter.Samus, 0);
            using Scene second = new(headless: true);
            second.Players.MaxPlayers = 1;
            Assert.Same(second.Players[0], second.Players.Create(Hunter.Spire, 0));
            Assert.Null(second.Players.Create(Hunter.Samus, 0));
            Assert.Same(first.Players[1], first.Players.Create(Hunter.Sylux, 0));
            Assert.Same(first.Players[0], firstSlot);
            Assert.Equal(Hunter.Samus, firstSlot!.Hunter);
            Assert.Equal(Hunter.Spire, second.Players[0].Hunter);
            Assert.Equal(2, first.Players.CreatedCount);
            Assert.Equal(1, second.Players.CreatedCount);
        }
        finally { Read.ServerMode = serverMode; }
    }

    [Fact]
    public void PlayerRegistryCannotReappearAsMutableStaticState()
    {
        Type player = typeof(PlayerEntity);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Assert.DoesNotContain(player.GetFields(flags), field => !field.IsLiteral &&
            (field.FieldType == typeof(PlayerEntity) || typeof(IEnumerable<PlayerEntity>).IsAssignableFrom(field.FieldType)
             || new[] { "PlayerCount", "MaxPlayers", "PlayersCreated", "MainPlayerIndex" }.Any(field.Name.Contains)));
        Assert.DoesNotContain(player.GetProperties(flags), property =>
            new[] { "Players", "Main", "PlayerCount", "MaxPlayers", "PlayersCreated", "MainPlayerIndex" }.Contains(property.Name));
        Assert.Null(player.Assembly.GetType("MphRead.GameState"));
    }
    private static string FindAmhe1()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
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
}
