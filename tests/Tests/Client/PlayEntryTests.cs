using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
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
        Assert.Equal(east, PlayController.SelectAutomaticNode([west, full, east], "East"));
        Assert.Equal(east, PlayController.SelectAutomaticNode([east, west, full], "East"));
        Assert.Null(PlayController.SelectAutomaticNode([full], "East"));
    }

    [Theory]
    [InlineData("Automatic")]
    [InlineData("")]
    public void AutomaticSentinelUsesDeterministicFallbackWithoutMatchingSentinelNodes(string region)
    {
        NodeListing sentinel = Node(1, "Automatic", 7);
        NodeListing fallback = Node(2, "us-east-1", 1);

        Assert.Equal(fallback, PlayController.SelectAutomaticNode([sentinel, fallback], region));
        Assert.Equal(fallback, PlayController.SelectAutomaticNode([fallback, sentinel], region));
    }

    [Fact]
    public async Task PreferredRegionUsesTheAuthoritativeLauncherPreference()
    {
        string previous = LauncherPrefs.PreferredRegion;
        try
        {
            LauncherPrefs.PreferredRegion = "Europe";
            using var shell = new PrimeShellState();
            await using var controller = new PlayController(shell);

            Assert.Equal("Europe", controller.PreferredRegion);
            controller.PreferredRegion = "Asia Pacific";
            Assert.Equal("Asia Pacific", LauncherPrefs.PreferredRegion);
            Assert.Equal("Asia Pacific", controller.PreferredRegion);
        }
        finally
        {
            LauncherPrefs.PreferredRegion = previous;
        }
    }

    [Fact]
    public void ObservedRegionSurvivesNormalizationSaveAndOrderIndependentSelection()
    {
        string previous = LauncherPrefs.PreferredRegion;
        try
        {
            LauncherPrefs.ObservePreferredRegions(["us-east-1", "eu-west-1"]);
            LauncherPrefs.PreferredRegion = "us-east-1";

            Assert.Equal("us-east-1", LauncherPrefs.PreferredRegion);
            Assert.Contains("preferred_region=us-east-1", LauncherPrefs.GetSaveLines());

            NodeListing preferred = Node(1, "us-east-1", 2);
            NodeListing fallback = Node(2, "eu-west-1", 1);
            Assert.Equal(preferred, PlayController.SelectAutomaticNode(
                [preferred, fallback], LauncherPrefs.PreferredRegion));
            Assert.Equal(preferred, PlayController.SelectAutomaticNode(
                [fallback, preferred], LauncherPrefs.PreferredRegion));
            Assert.Equal(fallback, PlayController.SelectAutomaticNode(
                [preferred, fallback], "US-EAST-1"));
        }
        finally
        {
            LauncherPrefs.PreferredRegion = previous;
        }
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

    [Fact]
    public async Task CancelEntryCancelsOnlyTheActiveEntryAndLeavesTheNodeSessionReferenceAlone()
    {
        using var shell = new PrimeShellState();
        await using var controller = new PlayController(shell);
        using var started = new ManualResetEventSlim();
        Task entry = controller.RunEntryAsync(async token =>
        {
            started.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(controller.State.Loading);
        Assert.True(controller.CancelEntry());
        Assert.False(controller.CancelEntry());
        await entry;

        Assert.False(controller.State.Loading);
        Assert.Contains("cancelled", controller.State.Message, StringComparison.OrdinalIgnoreCase);
        // CancelEntry must not call NodeSessions.DisconnectAsync or otherwise
        // alter the persistent control-session slot.
        Assert.Null(NodeSessions.Current);
    }

    [Fact]
    public void LobbyCommandBuildersCarryAuthoritativeIdentityAndRevision()
    {
        Guid lobbyId = Guid.NewGuid();
        Guid offerId = Guid.NewGuid();

        LobbyQueueJoin join = PlayController.CreateWaitlistJoinCommand(lobbyId, 17,
            LobbyQueueRequestedRole.Player, 1);
        Assert.Equal(lobbyId, join.LobbyId);
        Assert.Equal(17, join.ExpectedRevision);
        Assert.Equal(LobbyQueueRequestedRole.Player, join.RequestedRole);
        Assert.Equal((byte)1, join.RequestedTeam);

        Assert.Equal(new LobbyQueueLeave(lobbyId, 18),
            PlayController.CreateWaitlistLeaveCommand(lobbyId, 18));
        Assert.Equal(new LobbyQueueAccept(lobbyId, 19, offerId),
            PlayController.CreateWaitlistOfferCommand(lobbyId, 19, offerId, accept: true));
        Assert.Equal(new LobbyQueueDecline(lobbyId, 20, offerId),
            PlayController.CreateWaitlistOfferCommand(lobbyId, 20, offerId, accept: false));
        Assert.Equal(new LobbyRequestTeam(1, 21), PlayController.CreateTeamCommand(1, 21));
        Assert.Equal(new LobbyChat("hello", 22), PlayController.CreateChatCommand(22, "hello"));
    }

    [Fact]
    public void StructuredLobbyRulesAreForwardedWithoutLegacyProjectionFields()
    {
        LobbyRulesOptions rules = new(TimeLimitSeconds: 600, ScoreGoal: 7,
            DamageLevel: 2, FriendlyFire: true, AffinityWeapons: false);
        LobbyConfigure command = PlayController.CreateStructuredConfigureCommand(
            23, "MP1 SANCTORUS", MatchMode.Battle, 2, rules);

        Assert.Equal(23, command.ExpectedRevision);
        Assert.Equal("MP1 SANCTORUS", command.MapKey);
        Assert.Equal(MatchMode.Battle, command.Mode);
        Assert.Equal(2, command.BotCount);
        Assert.Equal(rules, command.Rules);
        Assert.Null(command.TimeLimitSeconds);
        Assert.Null(command.PointGoal);
    }

    [Fact]
    public void LobbyCreateBuilderCarriesExplicitSeatLayout()
    {
        LobbyCreate command = PlayController.CreateLobbyCommand("Arena", 4, 6,
            LobbySeatPolicy.NextMatchSeat);
        Assert.Equal("Arena", command.Name);
        Assert.Equal(4, command.PlayerLimit);
        Assert.Equal(6, command.ObserverLimit);
        Assert.Equal(LobbySeatPolicy.NextMatchSeat, command.SeatPolicy);
        Assert.Equal(DuelQueuePolicy.Fifo, command.DuelQueuePolicy);
    }

    [Fact]
    public void ChatValidationUsesTheNodeUtf8ByteLimit()
    {
        Assert.Equal(256, System.Text.Encoding.UTF8.GetByteCount(
            PlayController.CreateChatCommand(24, new string('é', 128)).Text));
        Assert.Throws<ArgumentException>(() => PlayController.CreateChatCommand(24,
            new string('é', 129)));
        Assert.Throws<ArgumentException>(() => PlayController.CreateChatCommand(24, "   "));
        Assert.Throws<ArgumentException>(() => PlayController.CreateChatCommand(24, "line\nfeed"));
    }

    [Fact]
    public async Task QueueOnlySnapshotRemainsVisibleWhenSelfIsNotAMember()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid(), lobbyId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Queued", nodeId,
            new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        LobbySnapshot snapshot = new(lobbyId, "Arena", LobbyVisibility.Public, Guid.NewGuid(),
            LobbyPhase.Open, 2, 1, 0, [], [], Waitlist: new LobbyWaitlistSnapshot(
                1, [new LobbyQueueEntrySummary(1, "Queued", LobbyQueueEntryState.Queued, 1)],
                IsSelfQueued: true, SelfState: LobbyQueueEntryState.Queued,
                SelfQueueSequence: 1));

        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, snapshot));

        Assert.Equal(snapshot.LobbyId, client.Lobby!.LobbyId);
        Assert.Equal(snapshot.Revision, client.Lobby.Revision);
        Assert.DoesNotContain(client.Lobby!.Members, member => member.SessionId == sessionId);
        Assert.True(client.Lobby.Waitlist!.IsSelfQueued);
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
