using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class UiScreenModelTests
{
    [Fact]
    public async Task RankedUnavailableIsExplicitAndDoesNotBecomeQuickPlay()
    {
        var services = new UiScreenServices();

        Assert.False(services.Play.Ranked.Available);
        Assert.Contains("verified", services.Play.Ranked.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", services.Play.Ranked.Reason, StringComparison.OrdinalIgnoreCase);

        UiActionResult result = await services.Play.StartRankedAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("Quick", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Practice", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponsiveScreenLayoutsKeepInformationArchitectureAtEveryBreakpoint()
    {
        Assert.Equal(UiLayoutMode.Compact, UiBreakpoints.FromWidth(719));
        Assert.Equal(UiLayoutMode.Medium, UiBreakpoints.FromWidth(720));
        Assert.Equal(UiLayoutMode.Wide, UiBreakpoints.FromWidth(1100));

        Assert.Equal(1, UiScreenLayout.BrowserColumns(UiLayoutMode.Compact));
        Assert.Equal(2, UiScreenLayout.BrowserColumns(UiLayoutMode.Medium));
        Assert.Equal(2, UiScreenLayout.BrowserColumns(UiLayoutMode.Wide));
        Assert.Equal(1, UiScreenLayout.CardColumns(UiLayoutMode.Compact));
        Assert.Equal(2, UiScreenLayout.CardColumns(UiLayoutMode.Medium));
        Assert.Equal(3, UiScreenLayout.CardColumns(UiLayoutMode.Wide));
    }

    [Fact]
    public void ServerSearchTrimsCaseAndComposesWithFiltersWithoutMutatingSource()
    {
        UiServerEntry matching = Server("Alpha Arena", "alpha.example", "Sanctorus", "Battle",
            players: 3, maxPlayers: 8, ping: 42, favorite: true);
        UiServerEntry full = Server("Alpha Full", "full.example", "Sanctorus", "Battle",
            players: 8, maxPlayers: 8, ping: 12, favorite: true);
        UiServerEntry incompatible = Server("Alpha Old", "old.example", "Sanctorus", "Battle",
            players: 1, maxPlayers: 8, ping: 20, compatible: false, favorite: true);
        UiServerEntry otherMode = Server("Alpha Duel", "duel.example", "Sanctorus", "Duel",
            players: 1, maxPlayers: 8, ping: 5, favorite: true);
        UiServerEntry otherSearch = Server("Bravo Arena", "bravo.example", "Alinos", "Battle",
            players: 2, maxPlayers: 8, ping: 10, favorite: true);
        UiServerEntry[] source = [matching, full, incompatible, otherMode, otherSearch];
        string[] sourceIds = source.Select(entry => entry.Id).ToArray();
        var filter = new UiServerFilter("  ALPHA  ", "Battle", HideFull: true,
            HideIncompatible: true, MaxPing: 50, UiServerSort.Ping, UiServerGroup.Favorites);

        IReadOnlyList<UiServerEntry> selected = UiServerSelection.Apply(source, filter);

        Assert.Equal([matching], selected);
        Assert.Equal(sourceIds, source.Select(entry => entry.Id));
        Assert.Equal(5, source.Length);
    }

    [Fact]
    public void ServerSelectionSortsUnknownPingLastAndUsesStableNameTieBreak()
    {
        UiServerEntry zulu = Server("Zulu", "zulu", "Map", "Battle", players: 1,
            maxPlayers: 8, ping: 20);
        UiServerEntry alpha = Server("alpha", "alpha", "Map", "Battle", players: 1,
            maxPlayers: 8, ping: 20);
        UiServerEntry unknown = Server("Unknown", "unknown", "Map", "Battle", players: 8,
            maxPlayers: 8, ping: -1);

        IReadOnlyList<UiServerEntry> selected = UiServerSelection.Apply(
            [zulu, unknown, alpha], new UiServerFilter("", null, false, false, 0,
                UiServerSort.Ping, UiServerGroup.All));

        Assert.Equal([alpha, zulu, unknown], selected);
    }

    [Fact]
    public async Task ThumbnailControllerReceivesCancellationAndDoesNotRequireSynchronousLoading()
    {
        var controller = new BlockingMapController();
        using var cancellation = new CancellationTokenSource();
        Task<byte[]?> load = controller.LoadThumbnailAsync("map-1", cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await load);
        Assert.Equal("map-1", controller.LastMapId);
        Assert.Equal(1, controller.RequestCount);
    }

    [Fact]
    public async Task ThumbnailCacheSharesInFlightLoadsAndEvictsLeastRecentlyUsedEntries()
    {
        var cache = new BoundedAsyncCache<string, byte[]>(capacity: 2,
            maximumConcurrentLoads: 2);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int loads = 0;

        Task<byte[]> Load(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref loads);
            started.TrySetResult(true);
            return WaitForValue(cancellationToken);
        }

        async Task<byte[]> WaitForValue(CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken);
            return [7];
        }

        Task<byte[]> first = cache.GetAsync("a", Load);
        await started.Task;
        Task<byte[]> second = cache.GetAsync("a", Load);
        release.TrySetResult(true);

        Assert.Equal(new byte[] { 7 }, await first);
        Assert.Equal(new byte[] { 7 }, await second);
        Assert.Equal(1, loads);

        await cache.GetAsync("b", _ => Task.FromResult(new byte[] { 2 }));
        await cache.GetAsync("a", _ => Task.FromResult(new byte[] { 9 }));
        await cache.GetAsync("c", _ => Task.FromResult(new byte[] { 3 }));
        Assert.Equal(2, cache.Count);
        Assert.Equal(new byte[] { 4 }, await cache.GetAsync("b",
            _ => Task.FromResult(new byte[] { 4 })));
    }

    [Fact]
    public async Task ThumbnailCacheCancellationDoesNotPublishAnEntry()
    {
        var cache = new BoundedAsyncCache<string, byte[]>(capacity: 2,
            maximumConcurrentLoads: 1);
        using var cancellation = new CancellationTokenSource();

        Task<byte[]> load = cache.GetAsync("cancelled", async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [1];
        }, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await load);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ServerBrowserModelPublishesReadyAndEmptyStatesFromAuthoritativeQuery()
    {
        UiServerEntry server = Server("Arena", "arena", "Sanctorus", "Battle",
            players: 2, maxPlayers: 8, ping: 25);
        var controller = new RecordingServerController([server]);
        using var model = new ServerBrowserScreenModel(controller);

        await model.RefreshAsync();

        Assert.Equal(UiLoadState.Ready, model.Status.State);
        Assert.Equal([server], model.Visible);
        Assert.Same(server, model.Selected);

        controller.Entries = [];
        await model.RefreshAsync();

        Assert.Equal(UiLoadState.Empty, model.Status.State);
        Assert.Empty(model.Visible);
        Assert.Null(model.Selected);
    }

    [Fact]
    public async Task ServerBrowserModelExposesModesAndUpdatesFavoriteFilteringImmediately()
    {
        UiServerEntry battle = Server("Battle", "battle", "Sanctorus", "Battle",
            players: 2, maxPlayers: 8, ping: 25);
        UiServerEntry duel = Server("Duel", "duel", "Alinos", "Duel",
            players: 2, maxPlayers: 2, ping: 20, favorite: true);
        var controller = new RecordingServerController([battle, duel]);
        using var model = new ServerBrowserScreenModel(controller);

        await model.RefreshAsync();

        Assert.Equal(["Battle", "Duel"], model.AvailableModes);
        model.Select(battle);
        Assert.True(model.ToggleFavorite());
        Assert.Equal((battle.Id, true), controller.LastFavorite);
        Assert.True(model.Selected?.Favorite);

        model.ApplyFilter(model.Filter with { Group = UiServerGroup.Favorites });
        Assert.Equal([duel.Id, battle.Id], model.Visible.Select(entry => entry.Id));

        model.Select(battle with { Favorite = true });
        Assert.False(model.ToggleFavorite());
        Assert.Equal([duel.Id], model.Visible.Select(entry => entry.Id));
    }

    [Fact]
    public async Task MapPickerModelUsesBoundedThumbnailCacheAndCancelsOnDispose()
    {
        UiMapEntry first = new("one", "One", "Battle", "2-8", false, null);
        UiMapEntry second = new("two", "Two", "Battle", "2-8", false, null);
        var controller = new RecordingMapController([first, second]);
        using var model = new MapPickerScreenModel(controller, cacheCapacity: 1,
            concurrentLoads: 1);

        await model.LoadAsync();
        byte[]? firstImage = await model.ThumbnailAsync(first);
        byte[]? cachedFirstImage = await model.ThumbnailAsync(first);

        Assert.Equal(firstImage, cachedFirstImage);
        Assert.Equal(1, controller.ThumbnailRequests);
        Assert.Equal(1, model.CachedThumbnailCount);

        await model.ThumbnailAsync(second);
        Assert.Equal(2, controller.ThumbnailRequests);
        Assert.Equal(1, model.CachedThumbnailCount);
    }

    [Fact]
    public async Task HunterLicenseModelAppendsCursorPagesAndKeepsHistoryWhenNextPageFails()
    {
        HunterLicenseMatch first = Match("One", "Battle");
        HunterLicenseMatch second = Match("Two", "Battle");
        var controller = new RecordingHunterController(
            new HunterLicenseMatchPage([first], "next"),
            new InvalidOperationException("page unavailable"));
        using var model = new HunterLicenseScreenModel(controller);

        await model.SelectTabAsync(HunterLicenseTab.Matches);
        Assert.Equal(UiLoadState.Ready, model.Status.State);
        Assert.Single(model.Matches);
        Assert.True(model.CanLoadMore);

        await model.LoadMoreAsync();
        Assert.Equal(UiLoadState.Failed, model.Status.State);
        Assert.True(model.Status.CanRetry);
        Assert.Single(model.Matches);
        Assert.True(model.CanLoadMore);

        controller.NextPage = new HunterLicenseMatchPage([second], null);
        controller.NextPageError = null;
        await model.LoadMoreAsync();

        Assert.Equal(UiLoadState.Ready, model.Status.State);
        Assert.Equal(2, model.Matches.Length);
        Assert.Equal(first, model.Matches[0]);
        Assert.Equal(second, model.Matches[1]);
        Assert.False(model.CanLoadMore);
    }

    [Fact]
    public async Task HunterLicenseModelExposesEmptyAndFailedPageStates()
    {
        var controller = new RecordingHunterController(new HunterLicenseMatchPage([], null),
            new HttpRequestException("offline"));
        using var model = new HunterLicenseScreenModel(controller);

        await model.SelectTabAsync(HunterLicenseTab.Matches);
        Assert.Equal(UiLoadState.Empty, model.Status.State);
        Assert.False(model.CanLoadMore);

        controller.NextPage = new HunterLicenseMatchPage([Match("One", "Battle")], "next");
        controller.InitialPageError = new HttpRequestException("offline");
        await model.SelectTabAsync(HunterLicenseTab.Matches);
        Assert.Equal(UiLoadState.Failed, model.Status.State);
        Assert.True(model.Status.CanRetry);
    }

    [Fact]
    public async Task ReplayDeleteRequiresConfirmationAndRemovesOnlyConfirmedReplay()
    {
        ReplayMetadata first = Replay("one", "one.dem");
        ReplayMetadata second = Replay("two", "two.dem");
        var library = new RecordingReplayController([first, second]);
        var confirmation = new RecordingReplayConfirmation(confirm: false);
        using var model = new ReplayLibraryScreenModel(library, confirmation);

        await model.LoadAsync();
        UiActionResult canceled = await model.DeleteSelectedAsync();
        Assert.False(canceled.Succeeded);
        Assert.Empty(library.Deleted);
        Assert.Equal(2, model.Replays.Count);

        confirmation.Confirm = true;
        UiActionResult deleted = await model.DeleteSelectedAsync();
        Assert.True(deleted.Succeeded);
        Assert.Equal([first], library.Deleted);
        Assert.Equal([second], model.Replays);
    }

    [Fact]
    public void PrivateMatchValidationReportsFieldsAndPreservesTheDraft()
    {
        var draft = new PrivateMatchDraft
        {
            Name = " ",
            MapId = null,
            Mode = "",
            MaxPlayers = 9,
            Bots = 10,
            Password = new string('x', 65)
        };

        IReadOnlyList<string> errors = draft.Validate();

        Assert.Contains("Match name", errors[0], StringComparison.Ordinal);
        Assert.Contains("Choose a map.", errors);
        Assert.Contains("Choose a game mode.", errors);
        Assert.Contains("Player limit", errors[3], StringComparison.Ordinal);
        Assert.Contains("Bot count", errors[4], StringComparison.Ordinal);
        Assert.Contains("Password", errors[5], StringComparison.Ordinal);
        Assert.Null(draft.MapId);
        Assert.Equal(9, draft.MaxPlayers);
        Assert.Equal(10, draft.Bots);
    }

    [Fact]
    public void PrivateMatchValidationAcceptsTheServerSupportedBoundaryValues()
    {
        var draft = new PrivateMatchDraft
        {
            Name = "  Friends  ",
            MapId = "sanctorus",
            Mode = "Battle",
            MaxPlayers = 8,
            Bots = 8,
            Password = new string('p', 64)
        };

        Assert.Empty(draft.Validate());
    }

    [Fact]
    public void HunterLicensePagesPreserveCursorAndRepresentEndOfHistory()
    {
        HunterLicenseMatch first = Match("Sanctorus", "Battle");
        HunterLicenseMatchPage page = new([first], "cursor-2");
        HunterLicenseMatchPage end = new([], null);

        Assert.Single(page.Matches);
        Assert.Equal(first.MatchId, page.Matches[0].MatchId);
        Assert.Equal("cursor-2", page.NextCursor);
        Assert.Empty(end.Matches);
        Assert.Null(end.NextCursor);
        Assert.Equal(5, Enum.GetValues<HunterLicenseTab>().Length);
    }

    [Fact]
    public async Task ReplayDeleteConfirmationCarriesExactReplayIdentityAndCancellation()
    {
        ReplayMetadata replay = Replay("replay-7", "match-7.dem");
        var confirmation = new RecordingReplayConfirmation(confirm: true);
        using var cancellation = new CancellationTokenSource();

        Assert.True(await confirmation.ConfirmDeleteAsync(replay, cancellation.Token));
        Assert.Same(replay, confirmation.Replay);
        Assert.Equal(cancellation.Token, confirmation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => confirmation.ConfirmDeleteAsync(replay, cancellation.Token));
    }

    [Fact]
    public void SettingsCategoriesAreCompleteAndAccessibilityValuesStayIndependent()
    {
        Assert.Equal(new[]
        {
            SettingsCategory.Gameplay,
            SettingsCategory.Controls,
            SettingsCategory.Video,
            SettingsCategory.Audio,
            SettingsCategory.HUD,
            SettingsCategory.Radar,
            SettingsCategory.Network,
            SettingsCategory.Accessibility,
            SettingsCategory.Account
        }, Enum.GetValues<SettingsCategory>());

        var preferences = new AccessibilityPreferences
        {
            UiScale = 1.4,
            LargeText = true,
            ReducedMotion = true,
            HoldActions = false,
            ColorVisionMode = UiColorVisionMode.Deuteranopia,
            SafeArea = 32
        };

        Assert.Equal(1.4, preferences.UiScale);
        Assert.True(preferences.LargeText);
        Assert.True(preferences.ReducedMotion);
        Assert.False(preferences.HoldActions);
        Assert.Equal(UiColorVisionMode.Deuteranopia, preferences.ColorVisionMode);
        Assert.Equal(32, preferences.SafeArea);
        Assert.True(preferences.TextSize(UiTypography.TextBody) >= UiTypography.TextMinimum);
    }

    [Fact]
    public void SettingsDescriptorCarriesCategoryPlatformConflictAndRestartSemantics()
    {
        var descriptor = new SettingDescriptor("video.vsync", "VSync", "Synchronize frames.",
            SettingsCategory.Video, true, RestartRequired: true, Platform: SettingPlatform.Desktop,
            Conflict: null);

        Assert.Equal(SettingsCategory.Video, descriptor.Category);
        Assert.Equal(SettingPlatform.Desktop, descriptor.Platform);
        Assert.True(descriptor.RestartRequired);
        Assert.Null(descriptor.Conflict);
    }

    [Fact]
    public void AsyncScreenStateSeparatesLoadingEmptyOfflineReadyAndFailure()
    {
        var state = new AsyncScreenState();
        Assert.Equal(UiLoadState.Idle, state.State);
        Assert.False(state.CanRetry);

        state.Loading("Loading servers…");
        AssertState(state, UiLoadState.Loading, "Loading servers…", canRetry: false);
        state.Empty("No servers found.");
        AssertState(state, UiLoadState.Empty, "No servers found.", canRetry: true);
        state.Offline();
        AssertState(state, UiLoadState.Offline, "This service is offline.", canRetry: true);
        state.Failed("Could not load servers.");
        AssertState(state, UiLoadState.Failed, "Could not load servers.", canRetry: true);
        state.Ready();
        AssertState(state, UiLoadState.Ready, string.Empty, canRetry: false);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), "was canceled")]
    [InlineData(typeof(HttpRequestException), "unavailable")]
    [InlineData(typeof(TimeoutException), "too long")]
    [InlineData(typeof(InvalidOperationException), "could not be completed")]
    public void AsyncScreenFailuresUseFriendlyOperationSpecificText(Type errorType, string expected)
    {
        Exception error = Activator.CreateInstance(errorType) as Exception
            ?? throw new InvalidOperationException("Could not create test exception.");

        string message = AsyncScreenState.FriendlyFailure(error, "Loading servers");

        Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Loading servers", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FocusPathsAreStableFromHomeThroughPlayAndModalBack()
    {
        var policy = new UiFocusNavigationPolicy();
        policy.ConnectVertical(["home:play", "play:quick", "play:ranked", "play:private"], wrap: false);

        AssertMove(policy, "home:play", UiFocusDirection.Down, "play:quick");
        AssertMove(policy, "play:quick", UiFocusDirection.Down, "play:ranked");
        AssertMove(policy, "play:ranked", UiFocusDirection.Down, "play:private");
        Assert.False(policy.TryMove("play:private", UiFocusDirection.Down, out _));

        var router = new UiRouter();
        UiNavigationChangedEventArgs? changed = null;
        router.Changed += (_, e) => changed = e;
        router.Navigate(UiRoute.Play, sourceFocusKey: "home:play");
        router.OpenModal("ranked-unavailable", returnFocusKey: "play:ranked");
        Assert.True(router.GoBack());
        Assert.Equal(UiRoute.Play, router.CurrentRoute);
        Assert.Equal("play:ranked", changed?.FocusKey);
    }

    private static UiServerEntry Server(string name, string endpoint, string map, string mode,
        int players, int maxPlayers, int ping, bool compatible = true, bool favorite = false,
        bool recent = false)
        => new("id-" + endpoint, name, endpoint, map, mode, players, maxPlayers, 0, 0, ping,
            "Lobby", null, "Classic", false, false, compatible, favorite, recent,
            false, true, "Enhanced", true, false);

    private static HunterLicenseMatch Match(string map, string mode)
        => new(Guid.NewGuid(), DateTimeOffset.UtcNow, map, mode, "Win", "7-3", "Pending");

    private static ReplayMetadata Replay(string id, string fileName)
        => new(id, fileName, DateTimeOffset.UtcNow, "Sanctorus", "Battle",
            TimeSpan.FromMinutes(7), 2, "Win", Compatible: true, RecoveredTail: false);

    private static void AssertState(AsyncScreenState state, UiLoadState expectedState,
        string expectedMessage, bool canRetry)
    {
        Assert.Equal(expectedState, state.State);
        Assert.Equal(expectedMessage, state.Message);
        Assert.Equal(canRetry, state.CanRetry);
    }

    private static void AssertMove(UiFocusNavigationPolicy policy, string from,
        UiFocusDirection direction, string expected)
    {
        Assert.True(policy.TryMove(from, direction, out string target));
        Assert.Equal(expected, target);
    }

    private sealed class BlockingMapController : IMapCatalogController
    {
        public string? LastMapId { get; private set; }
        public int RequestCount { get; private set; }

        public Task<IReadOnlyList<UiMapEntry>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UiMapEntry>>([]);

        public async Task<byte[]?> LoadThumbnailAsync(string mapId,
            CancellationToken cancellationToken)
        {
            LastMapId = mapId;
            RequestCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class RecordingMapController : IMapCatalogController
    {
        private readonly IReadOnlyList<UiMapEntry> _maps;

        public RecordingMapController(IReadOnlyList<UiMapEntry> maps) => _maps = maps;

        public int ThumbnailRequests { get; private set; }

        public Task<IReadOnlyList<UiMapEntry>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(_maps);

        public Task<byte[]?> LoadThumbnailAsync(string mapId,
            CancellationToken cancellationToken)
        {
            ThumbnailRequests++;
            return Task.FromResult<byte[]?>([(byte)mapId.Length]);
        }
    }

    private sealed class RecordingServerController : IServerBrowserController
    {
        public RecordingServerController(IReadOnlyList<UiServerEntry> entries)
            => Entries = entries;

        public IReadOnlyList<UiServerEntry> Entries { get; set; }
        public (string Id, bool Favorite)? LastFavorite { get; private set; }

        public Task<IReadOnlyList<UiServerEntry>> QueryAsync(CancellationToken cancellationToken)
            => Task.FromResult(Entries);

        public Task<UiActionResult> JoinAsync(UiServerEntry server, bool spectate,
            CancellationToken cancellationToken)
            => Task.FromResult(UiActionResult.Success());

        public void SetFavorite(UiServerEntry server, bool favorite)
            => LastFavorite = (server.Id, favorite);
        public void CopyEndpoint(UiServerEntry server) { }
    }

    private sealed class RecordingHunterController : IHunterLicenseController
    {
        public RecordingHunterController(HunterLicenseMatchPage initialPage,
            Exception? nextPageError)
        {
            InitialPage = initialPage;
            NextPageError = nextPageError;
        }

        public HunterLicenseMatchPage InitialPage { get; set; }
        public HunterLicenseMatchPage? NextPage { get; set; }
        public Exception? InitialPageError { get; set; }
        public Exception? NextPageError { get; set; }

        public Task<HunterLicenseOverview> LoadOverviewAsync(CancellationToken cancellationToken)
            => Task.FromException<HunterLicenseOverview>(new InvalidOperationException());

        public Task<IReadOnlyList<HunterLicenseStat>> LoadStatsAsync(HunterLicenseTab tab,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<HunterLicenseStat>>([]);

        public Task<HunterLicenseMatchPage> LoadMatchesAsync(string? cursor,
            CancellationToken cancellationToken)
        {
            if (cursor is null && InitialPageError is not null)
                return Task.FromException<HunterLicenseMatchPage>(InitialPageError);
            if (cursor is null) return Task.FromResult(InitialPage);
            if (NextPageError is not null)
                return Task.FromException<HunterLicenseMatchPage>(NextPageError);
            return Task.FromResult(NextPage ?? new HunterLicenseMatchPage([], null));
        }
    }

    private sealed class RecordingReplayController : IReplayLibraryController
    {
        private readonly IReadOnlyList<ReplayMetadata> _replays;

        public RecordingReplayController(IReadOnlyList<ReplayMetadata> replays)
            => _replays = replays;

        public List<ReplayMetadata> Deleted { get; } = [];

        public Task<IReadOnlyList<ReplayMetadata>> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult(_replays);

        public Task<UiActionResult> PlayAsync(ReplayMetadata replay,
            CancellationToken cancellationToken)
            => Task.FromResult(UiActionResult.Success());

        public Task<UiActionResult> DeleteAsync(ReplayMetadata replay,
            CancellationToken cancellationToken)
        {
            Deleted.Add(replay);
            return Task.FromResult(UiActionResult.Success("Deleted"));
        }

        public Task<UiActionResult> ImportAsync(CancellationToken cancellationToken)
            => Task.FromResult(UiActionResult.Success());

        public Task<UiActionResult> ExportAsync(ReplayMetadata replay,
            CancellationToken cancellationToken)
            => Task.FromResult(UiActionResult.Success());
    }

    private sealed class RecordingReplayConfirmation : IReplayDeleteConfirmation
    {
        public RecordingReplayConfirmation(bool confirm) => _confirm = confirm;

        public bool Confirm { get => _confirm; set => _confirm = value; }
        public ReplayMetadata? Replay { get; private set; }
        public CancellationToken Token { get; private set; }

        private bool _confirm;

        public async Task<bool> ConfirmDeleteAsync(ReplayMetadata replay,
            CancellationToken cancellationToken)
        {
            Replay = replay;
            Token = cancellationToken;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return _confirm;
        }
    }
}
