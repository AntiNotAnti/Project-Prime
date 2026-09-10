using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class PlayEntryTests
{
    [Fact]
    public void DirectoryExpiresAtTwentyFiveSecondsAndRejectsFutureTimestamp()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch.AddHours(1);
        Assert.False(PlayController.IsDirectoryFresh(null, now));
        Assert.True(PlayController.IsDirectoryFresh(now.AddSeconds(-24), now));
        Assert.False(PlayController.IsDirectoryFresh(now.AddSeconds(-25), now));
        Assert.False(PlayController.IsDirectoryFresh(now.AddSeconds(1), now));
    }

    [Fact]
    public void AutomaticSelectionPrefersRegionAndIsIndependentOfDirectoryOrdering()
    {
        NodeListing west = Node(1, "West", 2);
        NodeListing east = Node(2, "East", 1);
        NodeListing full = Node(3, "East", 8);
        Assert.Equal(east, PlayController.SelectAutomaticNode([west, full, east], "east"));
        Assert.Equal(east, PlayController.SelectAutomaticNode([east, west, full], "east"));
        Assert.Null(PlayController.SelectAutomaticNode([full], "East"));
    }

    [Fact]
    public async Task QuickPlaySkipsUnavailableLobbiesAndUsesLaterPage()
    {
        LobbyListEntry target = Lobby(LobbyPhase.Open, 1, 0);
        var offsets = new List<int>();
        LobbyListEntry? found = await PlayController.FindQuickPlayLobbyAsync((offset, _) =>
        {
            offsets.Add(offset);
            return Task.FromResult(offset == 0
                ? new LobbyListSnapshot([Lobby(LobbyPhase.InMatch, 1, 0), Lobby(LobbyPhase.Open, 1, 3)], 16)
                : new LobbyListSnapshot([target], null));
        }, CancellationToken.None);
        Assert.Equal(target, found);
        Assert.Equal(new[] { 0, 16 }, offsets);
    }

    [Fact]
    public async Task QuickPlayBoundsPaginationAndStopsNonAdvancingCursor()
    {
        int calls = 0;
        Assert.Null(await PlayController.FindQuickPlayLobbyAsync((offset, _) =>
        {
            calls++;
            return Task.FromResult(new LobbyListSnapshot(ImmutableArray<LobbyListEntry>.Empty, offset + 16));
        }, CancellationToken.None));
        Assert.Equal(64, calls);
        calls = 0;
        Assert.Null(await PlayController.FindQuickPlayLobbyAsync((offset, _) =>
        {
            calls++;
            return Task.FromResult(new LobbyListSnapshot(ImmutableArray<LobbyListEntry>.Empty, offset));
        }, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task QuickPlayCancellationDoesNotReadAnotherPage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlayController.FindQuickPlayLobbyAsync(
            (_, _) => throw new Exception("Must not read"), cancellation.Token));
    }

    [Fact]
    public async Task EntryTimeoutClearsLoadingAndAllowsAnotherAction()
    {
        using var shell = new PrimeShellState();
        await using var controller = new PlayController(shell);
        await controller.RunEntryAsync(_ => throw new TimeoutException());
        Assert.False(controller.State.Loading);
        Assert.Contains("did not respond", controller.State.Message);
        bool ran = false;
        await controller.RunEntryAsync(_ => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
        Assert.False(controller.State.Loading);
    }

    [Fact]
    public async Task EntryCancellationClearsLoadingAndAllowsAnotherAction()
    {
        using var shell = new PrimeShellState();
        await using var controller = new PlayController(shell);
        using var cancellation = new CancellationTokenSource();
        await controller.RunEntryAsync(token =>
        {
            Assert.True(controller.State.Loading);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, cancellation.Token);
        Assert.False(controller.State.Loading);
        Assert.Contains("cancelled", controller.State.Message);
        await controller.RunEntryAsync(_ => Task.CompletedTask);
        Assert.False(controller.State.Loading);
    }

    [Theory]
    [InlineData("stale_revision")]
    [InlineData("capacity")]
    [InlineData("phase")]
    public async Task QuickPlayRefreshesAfterJoinRace(string code)
    {
        int reads = 0, joins = 0;
        Assert.True(await PlayController.JoinQuickPlayWithRetryAsync((_, _) =>
        {
            reads++;
            return Task.FromResult(new LobbyListSnapshot([Lobby(LobbyPhase.Open, 1, 0)], null));
        }, (_, _) => Task.FromResult(++joins == 1 ? Error(code)
            : new NodeControlEvent(1, "lobby.snapshot", 1, null, JsonSerializer.SerializeToElement(new { }))),
            CancellationToken.None));
        Assert.Equal(2, reads);
        Assert.Equal(2, joins);
    }

    [Fact]
    public async Task QuickPlayJoinRacesAreBounded()
    {
        int joins = 0;
        Assert.False(await PlayController.JoinQuickPlayWithRetryAsync((_, _) =>
            Task.FromResult(new LobbyListSnapshot([Lobby(LobbyPhase.Open, 1, 0)], null)),
            (_, _) => { joins++; return Task.FromResult(Error("phase")); }, CancellationToken.None));
        Assert.Equal(3, joins);
    }

    [Fact]
    public async Task BrowseCollectsLaterPagesAndDeduplicatesMovingListings()
    {
        LobbyListEntry first = Lobby(LobbyPhase.Open, 1, 0), second = Lobby(LobbyPhase.Open, 2, 0);
        LobbyListSnapshot result = await PlayController.LoadBrowseLobbiesAsync((offset, _) =>
            Task.FromResult(offset == 0 ? new LobbyListSnapshot([first], 16)
                : new LobbyListSnapshot([first, second], null)), CancellationToken.None);
        Assert.Equal(new[] { first, second }, result.Lobbies);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task BrowseStopsAtBoundAndReportsMoreResults()
    {
        int calls = 0;
        LobbyListSnapshot result = await PlayController.LoadBrowseLobbiesAsync((offset, _) =>
        {
            calls++;
            return Task.FromResult(new LobbyListSnapshot([Lobby(LobbyPhase.Open, 1, 0)], offset + 16));
        }, CancellationToken.None);
        Assert.Equal(64, calls);
        Assert.Equal(1024, result.NextOffset);
    }

    private static NodeControlEvent Error(string code)
        => new(1, "error", 1, null, JsonSerializer.SerializeToElement(new NodeControlError(code, "Race"), NodeJsonContext.Default.NodeControlError));

    private static LobbyListEntry Lobby(LobbyPhase phase, int players, int bots)
        => new(Guid.NewGuid(), "Lobby", phase, players, 4, 0, 1, BotCount: bots);

    private static NodeListing Node(int id, string region, int players)
        => new(new Guid(id, 0, 0, new byte[8]), "Server", region, "wss://localhost", 1,
            "build", "content", 8, players, 1, 0, "community", DateTimeOffset.UnixEpoch);
}
