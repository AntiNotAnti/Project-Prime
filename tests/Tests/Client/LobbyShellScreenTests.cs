using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Adapters;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class LobbyShellScreenTests
{
    [Fact]
    public async Task LobbyRequestStaysPendingUntilNewerAuthoritativeRevisionArrives()
    {
        var controller = new RecordingLobbyController(Snapshot(7));
        var response = new TaskCompletionSource<UiActionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Response = response.Task;
        var model = new LobbyScreenModel(controller);

        Task<UiActionResult> request = model.RequestAsync(
            new UiLobbyCommand(UiLobbyAction.SetReady, Value: 1));
        await controller.RequestStarted.Task;

        Assert.Equal(UiLobbyAction.SetReady, model.PendingAction);
        Assert.Equal(7u, controller.ExpectedRevision);

        response.SetResult(UiActionResult.Success());
        UiActionResult pending = await request;

        Assert.False(pending.Succeeded);
        Assert.Contains("authoritative", pending.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending", pending.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(model.PendingAction);
        Assert.Equal(UiLoadState.Loading, model.Status.State);

        controller.Replace(Snapshot(8));
        Assert.Equal(8u, model.Snapshot!.Revision);
        Assert.Equal(UiLoadState.Ready, model.Status.State);

        controller.Replace(Snapshot(7));
        Assert.Equal(8u, model.Snapshot.Revision);
    }

    [Fact]
    public async Task LobbyRejectedFeedbackIsRetryableAndPreservesTheAuthoritativeSnapshot()
    {
        UiLobbySnapshot snapshot = Snapshot(12);
        var controller = new RecordingLobbyController(snapshot)
        {
            NextResult = UiActionResult.Failure("The server rejected the ready request.")
        };
        var model = new LobbyScreenModel(controller);

        UiActionResult result = await model.RequestAsync(
            new UiLobbyCommand(UiLobbyAction.SetReady, Value: 1));

        Assert.False(result.Succeeded);
        Assert.Equal("The server rejected the ready request.", result.Message);
        Assert.Equal(UiLoadState.Failed, model.Status.State);
        Assert.True(model.Status.CanRetry);
        Assert.Equal(12u, model.Snapshot!.Revision);
        Assert.Equal(1, controller.RequestCount);
    }

    [Fact]
    public async Task RankedAndTournamentControlsExposeExplicitDisabledReasons()
    {
        const string rankedReason = "Ranked requires a verified account and an active rating policy.";
        const string tournamentReason = "Tournament controls are locked by the administrator.";
        var controller = new RecordingLobbyController(Snapshot(3, new Dictionary<UiLobbyAction, string>
        {
            [UiLobbyAction.SetRule] = rankedReason,
            [UiLobbyAction.StartMatch] = tournamentReason
        }));
        var model = new LobbyScreenModel(controller);

        Assert.False(model.Snapshot!.IsEnabled(UiLobbyAction.SetRule));
        Assert.Equal(rankedReason, model.Snapshot.DisabledReason(UiLobbyAction.SetRule));
        Assert.False(model.Snapshot.IsEnabled(UiLobbyAction.StartMatch));
        Assert.Equal(tournamentReason, model.Snapshot.DisabledReason(UiLobbyAction.StartMatch));

        UiActionResult result = await model.RequestAsync(new UiLobbyCommand(UiLobbyAction.StartMatch));

        Assert.False(result.Succeeded);
        Assert.Equal(tournamentReason, result.Message);
        Assert.Equal(0, controller.RequestCount);
    }

    [Theory]
    [InlineData(UiLobbyAction.SetBotFillEnabled, 1, LobbyRequestType.SetBotFillEnabled)]
    [InlineData(UiLobbyAction.SetBotMinimumParticipants, 8,
        LobbyRequestType.SetBotMinimumParticipants)]
    [InlineData(UiLobbyAction.SetBotSkill, 2, LobbyRequestType.SetBotSkill)]
    public void BotControlsUseTheBoundedLobbyRequestContracts(UiLobbyAction action, int value,
        LobbyRequestType expectedType)
    {
        LobbyRequestPacket request = CoordinatorLobbyAdapter.BuildRequest(
            new UiLobbyCommand(action, value), Snapshot(4), requestId: 19);
        byte[] wire = new byte[LobbyRequestPacket.Size];

        request.Write(wire);

        Assert.True(LobbyRequestPacket.TryRead(wire, out LobbyRequestPacket decoded));
        Assert.Equal(expectedType, decoded.Type);
        Assert.Equal(value, decoded.Value);
        Assert.Equal(LobbyRuleField.None, decoded.Rule);
        Assert.Empty(decoded.Text);
    }

    [Fact]
    public void LocalMuteUsesPublishedIdentityRatherThanDisplayName()
    {
        var mute = new LobbyMuteState();
        UiLobbyChatLine first = CoordinatorLobbyAdapter.MapChat(new LobbyChatPacket(
            1, 2, 3, ConnectionId: 11, SenderIdentity: 101, Slot: 0, ChatScope.Lobby,
            LobbyChatSenderFlags.None, "Same name", "first"));
        UiLobbyChatLine second = CoordinatorLobbyAdapter.MapChat(new LobbyChatPacket(
            1, 2, 4, ConnectionId: 22, SenderIdentity: 202, Slot: 1, ChatScope.Lobby,
            LobbyChatSenderFlags.None, "Same name", "second"));
        mute.Observe(first);
        mute.Observe(second);

        Assert.False(mute.SetMuted(303, muted: true));
        Assert.True(mute.SetMuted(101, muted: true));
        Assert.Collection(mute.Visible([first, second]), line => Assert.Equal(second, line));
        UiMutedLobbySender muted = Assert.Single(mute.MutedSenders);
        Assert.Equal(101ul, muted.SenderIdentity);
        Assert.Equal("Same name", muted.Name);

        Assert.True(mute.SetMuted(101, muted: false));
        Assert.Collection(mute.Visible([first, second]),
            line => Assert.Equal(first, line), line => Assert.Equal(second, line));
    }

    [Fact]
    public void SameRevisionClientProjectionRefreshesWithoutAllowingRevisionRegression()
    {
        var controller = new RecordingLobbyController(Snapshot(6));
        var model = new LobbyScreenModel(controller);
        UiLobbySnapshot projected = Snapshot(6) with
        {
            Chat = ImmutableArray.Create(
                new UiLobbyChatLine("Hunter", "hello", false, false, false, 44))
        };

        controller.Replace(projected);

        Assert.Same(projected, model.Snapshot);
        controller.Replace(Snapshot(5));
        Assert.Same(projected, model.Snapshot);
    }

    [Fact]
    public void LobbyMemberStatusesRemainTextualForPresenceAndEligibilityStates()
    {
        var readyHost = new UiLobbyMember("Jarrett", "Noxus", 0, Ready: true,
            Loading: false, Observer: false, DisconnectedGrace: false, Bot: false,
            Host: true, Admin: true, RatingEligible: true, PingMs: 24, Local: true);
        var loadingBot = new UiLobbyMember("Bot", "Sylux", 1, Ready: false,
            Loading: true, Observer: false, DisconnectedGrace: true, Bot: true,
            Host: false, Admin: false, RatingEligible: false, PingMs: 0);
        var spectator = new UiLobbyMember("Watcher", "Trace", 0, Ready: false,
            Loading: false, Observer: true, DisconnectedGrace: true, Bot: false,
            Host: false, Admin: false, RatingEligible: true, PingMs: 41);

        Assert.Equal("Ready · Host · Admin · Rating eligible", readyHost.StatusLabel);
        Assert.Equal("Loading · Disconnected grace · Bot · Not rating eligible",
            loadingBot.StatusLabel);
        Assert.Equal("Spectator · Disconnected grace · Rating eligible", spectator.StatusLabel);
    }

    [Theory]
    [InlineData(UiLayoutMode.Compact, 1)]
    [InlineData(UiLayoutMode.Medium, 2)]
    [InlineData(UiLayoutMode.Wide, 3)]
    public void LobbyPanesUseStackedCompactAndSplitWideComposition(UiLayoutMode mode,
        int expectedColumns)
        => Assert.Equal(expectedColumns, LobbyScreenModel.SectionColumns(mode));

    [Fact]
    public void CompactNavigationUsesShortLicenseLabelWithoutChangingAccessibleRouteName()
    {
        Assert.Equal("License", UiRouteInfo.CompactLabel(UiRoute.HunterLicense));
        Assert.Equal("Hunter License", UiRouteInfo.Label(UiRoute.HunterLicense));
        Assert.Equal("Settings", UiRouteInfo.CompactLabel(UiRoute.Settings));
    }

    [Fact]
    public async Task PostMatchKeepsTheBoundedPlayerRowsWhileRatingMovesFromPendingToUpdated()
    {
        ImmutableArray<UiPostMatchRow> rows = Rows(MatchSummaryPacket.MaximumRows);
        UiPostMatchSummary pending = Summary(rows, RatingUpdateState.Pending);
        UiPostMatchSummary updated = pending with
        {
            RatingState = RatingUpdateState.Updated,
            RatingDelta = 24,
            RatingPoints = 1524
        };
        var controller = new RecordingPostMatchController(pending, updated);
        var model = new PostMatchScreenModel(controller);

        await model.RefreshRatingAsync();

        Assert.Equal(MatchSummaryPacket.MaximumRows, model.Summary!.Rows.Length);
        Assert.Equal(rows, model.Summary.Rows);
        Assert.Equal(RatingUpdateState.Updated, model.Summary.RatingState);
        Assert.Equal(24, model.Summary.RatingDelta);
        Assert.Equal(1524, model.Summary.RatingPoints);
        Assert.Equal(pending.MatchId, controller.RequestedMatchId);
    }

    [Fact]
    public async Task AccountRestoreAndSignInFailuresBecomeFriendlyOfflineResults()
    {
        var backend = new Uri("https://accounts.example.test/");
        var store = new MemoryOnlySessionStore();
        await store.WriteAsync(backend.AbsoluteUri,
            SecureSessionRecordCodec.Encode(backend.AbsoluteUri, "refresh-token"));

        using (var session = new AccountSession(backend, new ThrowingHandler(),
                   sessionStore: store))
        {
            var adapter = new AccountSessionUiAdapter(session);
            UiActionResult result = await adapter.RestoreAsync(CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(UiAccountPhase.Offline, adapter.Snapshot.Phase);
            Assert.Equal("Session restore is unavailable. Check your connection and try again.",
                result.Message);
            Assert.Equal(result.Message, adapter.Snapshot.Message);
        }

        using (var session = new AccountSession(backend, new ThrowingHandler(),
                   sessionStore: new MemoryOnlySessionStore()))
        {
            var adapter = new AccountSessionUiAdapter(session);
            UiActionResult result = await adapter.SignInAsync("hunter@example.test",
                "A-long-password-1!", CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(UiAccountPhase.Offline, adapter.Snapshot.Phase);
            Assert.Equal("Sign in is unavailable. Check your connection and try again.",
                result.Message);
            Assert.Equal(result.Message, adapter.Snapshot.Message);
        }
    }

    [Fact]
    public void FocusPolicyKeepsTheHomeToReadyLobbyPathPureAndPredictable()
    {
        var policy = new UiFocusNavigationPolicy();
        string[] path = ["home", "play", "private", "lobby", "hunter", "ready"];
        policy.ConnectHorizontal(path);

        for (int i = 0; i < path.Length - 1; i++)
        {
            Assert.True(policy.TryMove(path[i], UiFocusDirection.Right, out string target));
            Assert.Equal(path[i + 1], target);
        }

        Assert.False(policy.TryMove("ready", UiFocusDirection.Right, out _));
        Assert.True(policy.TryMove("ready", UiFocusDirection.Left, out string previous));
        Assert.Equal("hunter", previous);
    }

    private static UiLobbySnapshot Snapshot(uint revision,
        IReadOnlyDictionary<UiLobbyAction, string>? disabled = null)
        => new(42, revision, "Open", "Private", "Sanctorus", "Battle",
            "Classic · 8 players · No limit · FF Off · Radar On · Spawn Default · Late join Off",
            ImmutableArray<UiLobbyMember>.Empty, ImmutableArray<UiLobbyChatLine>.Empty,
            disabled ?? new Dictionary<UiLobbyAction, string>());

    private static UiPostMatchSummary Summary(ImmutableArray<UiPostMatchRow> rows,
        RatingUpdateState ratingState)
        => new(42, 8, 99, TimeSpan.FromMinutes(7), "Sanctorus", "Battle", "Completed",
            rows, ratingState);

    private static ImmutableArray<UiPostMatchRow> Rows(int count)
    {
        var rows = ImmutableArray.CreateBuilder<UiPostMatchRow>(count);
        for (int i = 0; i < count; i++)
        {
            rows.Add(new UiPostMatchRow(i + 1, $"Player {i + 1}", "Noxus", i % 2,
                Bot: i > 5, Points: 10 - i, Kills: i, Deaths: i / 2, Assists: 1,
                Damage: 100 + i, Headshots: i % 3, ObjectivePrimary: i,
                ObjectiveSecondary: 0, ObjectiveTertiary: 0));
        }
        return rows.MoveToImmutable();
    }

    private sealed class RecordingLobbyController(UiLobbySnapshot initial) : ILobbyScreenController
    {
        private UiLobbySnapshot _snapshot = initial;

        public UiLobbySnapshot? Snapshot => _snapshot;
        public event Action? Changed;
        public TaskCompletionSource<bool> RequestStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<UiActionResult>? Response { get; set; }
        public UiActionResult NextResult { get; set; } = UiActionResult.Success();
        public UiLobbyCommand? LastCommand { get; private set; }
        public uint ExpectedRevision { get; private set; }
        public int RequestCount { get; private set; }

        public async Task<UiActionResult> RequestAsync(UiLobbyCommand command,
            uint expectedRevision, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastCommand = command;
            ExpectedRevision = expectedRevision;
            RequestStarted.TrySetResult(true);
            if (Response is not null)
                return await Response.WaitAsync(cancellationToken);
            return NextResult;
        }

        public void Replace(UiLobbySnapshot snapshot)
        {
            _snapshot = snapshot;
            Changed?.Invoke();
        }
    }

    private sealed class RecordingPostMatchController(UiPostMatchSummary initial,
        UiPostMatchSummary updated) : IPostMatchScreenController
    {
        public UiPostMatchSummary? Summary { get; } = initial;
        public event Action? Changed;
        public uint RequestedMatchId { get; private set; }

        public Task<UiPostMatchSummary> AwaitRatingAsync(uint matchId,
            CancellationToken cancellationToken)
        {
            RequestedMatchId = matchId;
            Changed?.Invoke();
            return Task.FromResult(updated);
        }

        public Task<UiActionResult> InvokeAsync(PostMatchAction action,
            CancellationToken cancellationToken) => Task.FromResult(UiActionResult.Success());
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}
