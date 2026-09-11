using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class PrimeShellControllerTests
{
    [Fact]
    public void UpdateActionUsesThePublicDistributionFeed()
    {
        Assert.True(MphRead.Mods.Update.UpdateCheck.IsConfigured);
        Assert.True(MphRead.Mods.Update.Updater.Configured);
        Assert.Equal("AntiNotAnti/Project-Prime-Releases",
            MphRead.Mods.Branding.UpdateRepository);
        Assert.Contains("AntiNotAnti/Project-Prime-Releases/releases",
            MphRead.Mods.Update.UpdateCheck.ReleasesPage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LobbyStartEligibilityRequiresAnExplicitMapConfiguration()
    {
        LobbyStartEligibility result = LobbyStartEligibility.Evaluate(LobbySnapshotFor(
            mapKey: "", members: [Member(ready: true)]));

        Assert.False(result.CanStart);
        Assert.Contains("Choose a map first", result.Message, StringComparison.Ordinal);
        Assert.Contains("resets readiness", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LobbyStartEligibilityRequiresEveryPlayerToBeReady()
    {
        LobbyStartEligibility result = LobbyStartEligibility.Evaluate(LobbySnapshotFor(
            mapKey: "MP1 SANCTORUS", members: [Member(ready: false)]));

        Assert.False(result.CanStart);
        Assert.Contains("All players must be Ready", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LobbyStartEligibilityRequiresBothTeamsAndAccountsForBots()
    {
        LobbyStartEligibility missingTeam = LobbyStartEligibility.Evaluate(LobbySnapshotFor(
            mapKey: "MP1 SANCTORUS", mode: MatchMode.TeamBattle,
            members: [Member(team: 0, ready: true)]));
        Assert.False(missingTeam.CanStart);
        Assert.Contains("both teams", missingTeam.Message, StringComparison.Ordinal);

        LobbyStartEligibility botTeam = LobbyStartEligibility.Evaluate(LobbySnapshotFor(
            mapKey: "MP1 SANCTORUS", mode: MatchMode.TeamBattle, botCount: 1,
            members: [Member(team: 0, ready: true)]));
        Assert.True(botTeam.CanStart);
    }

    [Fact]
    public void LobbyStartEligibilityIgnoresObserversForReadinessAndTeams()
    {
        LobbyStartEligibility result = LobbyStartEligibility.Evaluate(LobbySnapshotFor(
            mapKey: "MP1 SANCTORUS", members:
            [
                Member(team: 0, ready: true),
                Member(team: 1, ready: false, observer: true)
            ]));

        Assert.True(result.CanStart);
    }

    private static LobbySnapshot LobbySnapshotFor(string mapKey, MatchMode mode = MatchMode.Battle,
        int botCount = 0, params LobbyMember[] members)
    {
        Guid owner = members.Length == 0 ? Guid.NewGuid() : members[0].SessionId;
        return new LobbySnapshot(Guid.NewGuid(), "Room", LobbyVisibility.Public, owner,
            LobbyPhase.Open, 1, 8, 16, members.ToImmutableArray(), [], mapKey, mode, BotCount: botCount);
    }

    private static LobbyMember Member(byte team = 0, bool ready = false, bool observer = false)
    {
        Guid session = Guid.NewGuid();
        return new(session, Guid.NewGuid(), "Hunter", Hunter.Samus, team, ready, observer);
    }

    [Fact]
    public void ModelPreviewCatalogUsesOnlyCanonicalHunterAndWeaponAssets()
    {
        string[] hunterModels =
        [
            "Samus_lod0", "Kanden_lod0", "Trace_lod0", "Sylux_lod0",
            "Nox_lod0", "Spire_lod0", "Weavel_lod0"
        ];
        for (int i = 0; i < hunterModels.Length; i++)
        {
            Assert.True(ModelPreviewCatalog.TryHunter((Hunter)i, out ModelPreviewSpec? spec));
            Assert.NotNull(spec);
            Assert.Equal(hunterModels[i], spec.ModelName);
            Assert.True(ModelPreviewCatalog.TryWorkerKey(spec.WorkerKey, out ModelPreviewSpec? parsed));
            Assert.Equal(spec, parsed);
        }

        foreach (BeamType beam in Enum.GetValues<BeamType>())
        {
            bool supported = beam is >= BeamType.PowerBeam and <= BeamType.OmegaCannon;
            Assert.Equal(supported, ModelPreviewCatalog.TryWeapon(beam, out _));
        }
        Assert.False(ModelPreviewCatalog.TryWorkerKey("hunter:guardian", out _));
        Assert.False(ModelPreviewCatalog.TryWorkerKey("weapon:enemy", out _));
        Assert.False(ModelPreviewCatalog.TryWorkerKey("../../arbitrary", out _));
    }

    [Fact]
    public void ModelPreviewCachePathIsVersionedAndContentScoped()
    {
        Assert.True(ModelPreviewCatalog.TryHunter(Hunter.Samus, out ModelPreviewSpec? spec));
        string first = ModelPreviewCatalog.PathFor(spec!, "AMHE1", "abc");
        string same = ModelPreviewCatalog.PathFor(spec!, "AMHE1", "abc");
        string changed = ModelPreviewCatalog.PathFor(spec!, "AMHE1", "def");

        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
        Assert.Equal("hunters", new DirectoryInfo(Path.GetDirectoryName(first)!).Name);
        Assert.StartsWith($"samus-v{ModelPreviewCatalog.RendererVersion}-",
            Path.GetFileName(first), StringComparison.Ordinal);
        Assert.EndsWith(".png", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelPreviewCacheRejectsTruncatedPngAndAcceptsCompletePng()
    {
        string path = Path.Combine(Path.GetTempPath(), $"prime-preview-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            Assert.False(ModelPreviewGenerator.IsUsable(path));

            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            Assert.True(ModelPreviewGenerator.IsUsable(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void ModelPreviewWorkerLaunchIncludesEntryAssemblyForDotnetHost()
    {
        Assert.True(ModelPreviewCatalog.TryHunter(Hunter.Samus, out ModelPreviewSpec? spec));
        var managed = ModelPreviewGenerator.CreateWorkerStartInfo(
            spec!, "/usr/local/share/dotnet/dotnet", "/tmp/ProjectPrime.dll");
        Assert.Equal("/tmp/ProjectPrime.dll", managed.ArgumentList[0]);
        Assert.Equal("-modelpreview", managed.ArgumentList[1]);
        Assert.Equal("hunter:samus", managed.ArgumentList[2]);

        var appHost = ModelPreviewGenerator.CreateWorkerStartInfo(
            spec!, "/tmp/ProjectPrime", "/tmp/ProjectPrime.dll");
        Assert.Equal("-modelpreview", appHost.ArgumentList[0]);
    }

    [Fact]
    public void NavigatorKeepsBoundedHistoryAndBackStopsAtRoot()
    {
        var navigator = new PrimeNavigator(maximumHistory: 2);
        navigator.Navigate(PrimeRoute.Play);
        navigator.Navigate(PrimeRoute.Armory);
        navigator.Navigate(PrimeRoute.Theatre);

        Assert.Equal(2, navigator.HistoryCount);
        Assert.Equal(PrimeRoute.Play, navigator.History[0]);
        Assert.True(navigator.GoBack());
        Assert.Equal(PrimeRoute.Armory, navigator.CurrentRoute);
        Assert.True(navigator.GoBack());
        Assert.Equal(PrimeRoute.Play, navigator.CurrentRoute);
        Assert.False(navigator.GoBack());
    }

    [Fact]
    public void HandoffGateDeduplicatesAndCancellationInvalidatesSameKey()
    {
        var key = new PlayHandoffKey(Guid.NewGuid(), Guid.NewGuid(), 7, 1);
        var gate = new PlayHandoffGate();

        Assert.True(gate.TryBegin(key));
        Assert.False(gate.TryBegin(key));
        gate.Cancel();
        Assert.True(gate.TryBegin(key));
        Assert.True(gate.TryComplete(key));
        Assert.False(gate.TryComplete(key));
        Assert.False(gate.IsActive);

        var rejoin = key with { Nonce = 8, Generation = 2 };
        Assert.True(gate.TryBegin(rejoin));
        Assert.True(gate.IsCurrent(rejoin));
    }

    [Fact]
    public async Task ReenablingHandoffFromChangedHandlerDoesNotRepublish()
    {
        using var shell = new PrimeShellState();
        await using var online = new ClientOnlineRuntime(enabled: false);
        await using var controller = new PlayController(shell, onlineRuntime: online);
        await using var node = new NodeControlClient(Guid.NewGuid());
        typeof(PlayController).GetMethod("Observe", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [node]);
        long revision = controller.State.Revision;
        int notifications = 0;
        controller.Changed += (_, _) =>
        {
            if (++notifications == 1) controller.SetHandoffEnabled(true);
        };

        controller.SetHandoffEnabled(true);

        Assert.Equal(1, notifications);
        Assert.Equal(revision + 1, controller.State.Revision);
    }

    [Fact]
    public void HostedMapCatalogExposesOnlyOrdinalLocalIntersection()
    {
        var unknown = PlayController.ResolveMapCatalog(["zeta", "alpha"], null);
        Assert.Equal(NodeMapCatalogState.Unknown, unknown.State);
        Assert.Empty(unknown.Available);

        var empty = PlayController.ResolveMapCatalog(["zeta", "alpha"], []);
        Assert.Equal(NodeMapCatalogState.Empty, empty.State);
        Assert.Empty(empty.Available);

        var disjoint = PlayController.ResolveMapCatalog(["zeta", "alpha"], ["remote"]);
        Assert.Equal(NodeMapCatalogState.Disjoint, disjoint.State);
        Assert.Empty(disjoint.Available);

        var intersection = PlayController.ResolveMapCatalog(["zeta", "alpha", "zeta", "beta"], ["beta", "zeta"]);
        Assert.Equal(NodeMapCatalogState.Available, intersection.State);
        Assert.Equal(new[] { "beta", "zeta" }, intersection.Available);
    }

    [Fact]
    public void LobbyTimeEditorAcceptsDefaultSecondsAndMinuteNotation()
    {
        Assert.True(PrimeShellView.TryParseLobbyTimeLimit("Default", out int? defaultValue));
        Assert.Null(defaultValue);
        Assert.True(PrimeShellView.TryParseLobbyTimeLimit("90", out int? seconds));
        Assert.Equal(90, seconds);
        Assert.True(PrimeShellView.TryParseLobbyTimeLimit("10:00", out int? minutes));
        Assert.Equal(600, minutes);
        Assert.True(PrimeShellView.TryParseLobbyTimeLimit("60:00", out int? maximum));
        Assert.Equal(3600, maximum);
        Assert.False(PrimeShellView.TryParseLobbyTimeLimit("60:01", out _));
        Assert.False(PrimeShellView.TryParseLobbyTimeLimit("1:99", out _));
    }

    [Fact]
    public void LobbyPointEditorAcceptsDefaultAndProtocolSafeRange()
    {
        Assert.True(PrimeShellView.TryParseLobbyPointGoal("Default", out int? defaultValue));
        Assert.Null(defaultValue);
        Assert.True(PrimeShellView.TryParseLobbyPointGoal("7", out int? pointGoal));
        Assert.Equal(7, pointGoal);
        Assert.True(PrimeShellView.TryParseLobbyPointGoal("65535", out int? maximum));
        Assert.Equal(ushort.MaxValue, maximum);
        Assert.False(PrimeShellView.TryParseLobbyPointGoal("0", out _));
        Assert.False(PrimeShellView.TryParseLobbyPointGoal("65536", out _));
        Assert.False(PrimeShellView.TryParseLobbyPointGoal("7.5", out _));
    }

    [Fact]
    public void LobbyConfigurationRejectsBotsBeyondRemainingCapacityAndInvalidTime()
    {
        LobbySnapshot lobby = LobbySnapshotFor("MP1 SANCTORUS", members:
        [
            Member(ready: true),
            Member(ready: true)
        ]);

        PlayController.ValidateLobbyConfiguration(lobby, MatchMode.Battle, 6, 3600, 65535);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayController.ValidateLobbyConfiguration(lobby, MatchMode.Battle, 7, 600, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayController.ValidateLobbyConfiguration(lobby, MatchMode.Battle, 0, 0, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayController.ValidateLobbyConfiguration(lobby, MatchMode.Battle, 0, 3601, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayController.ValidateLobbyConfiguration(lobby, MatchMode.Battle, 0, 600, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayController.ValidateLobbyConfiguration(lobby, MatchMode.Battle, 0, 600, 65536));
    }

    [Fact]
    public async Task RankingsRejectsRpHunterFilterAndHighlightsOnlyLoadedPlayer()
    {
        PlayerId player = new(Guid.NewGuid());
        var query = new RankingQueryFake(player);
        using var shell = new PrimeShellState();
        using var rankings = new RankingsController(shell, query);

        rankings.SetMetric("kills");
        rankings.SetHunterFilter(Hunter.Samus);
        Assert.Throws<ArgumentException>(() => rankings.SetMetric("rp"));
        await rankings.LoadAsync();

        Assert.Single(rankings.State.Rows);
        Assert.True(rankings.State.Rows[0].IsCurrentPlayer);
        Assert.Equal("kills", query.LastMetric);
        Assert.Equal(Hunter.Samus, query.LastHunter);
    }

    [Fact]
    public async Task LicenseCacheInvalidatesOnIdentityChange()
    {
        PlayerId player = new(Guid.NewGuid());
        var query = new CareerQueryFake(player);
        using var shell = new PrimeShellState();
        using var license = new HunterLicenseController(shell, query);

        await license.LoadOverviewAsync();
        Assert.Equal(1, query.LicenseCalls);
        await license.LoadOverviewAsync();
        Assert.Equal(1, query.LicenseCalls);

        shell.SetIdentity(player, "Pilot", emailEligible: true);
        await license.LoadOverviewAsync();
        Assert.Equal(2, query.LicenseCalls);
    }

    [Fact]
    public async Task LicenseDropsAnInFlightResponseAfterIdentityChange()
    {
        PlayerId player = new(Guid.NewGuid());
        var query = new DeferredCareerQueryFake(player);
        using var shell = new PrimeShellState();
        using var license = new HunterLicenseController(shell, query);

        Task load = license.LoadOverviewAsync();
        shell.SetIdentity(new PlayerId(Guid.NewGuid()), "Other Pilot", emailEligible: true);
        query.Complete();
        await load;

        Assert.Null(license.State.License);
        Assert.Null(license.State.Career);
    }

    [Fact]
    public async Task GatewayRestoresOnceAndAuthFailureDoesNotSelectGuest()
    {
        var account = new GatewayAccountFake { FailSignIn = true };
        using var shell = new PrimeShellState();
        await using var gateway = new GatewayController(shell,
            _ => Task.FromResult<IPrimeGatewayAccount>(account));

        Assert.False(await gateway.SignInAsync("pilot@example.test", "secret"));
        Assert.False(gateway.State.GuestSelected);
        Assert.Equal(GatewayPhase.Failed, gateway.State.Phase);

        Assert.False(await gateway.RestoreAsync());
        Assert.False(await gateway.RestoreAsync());
        Assert.Equal(1, account.RestoreCalls);
    }

    [Fact]
    public async Task GuestAdmissionRequiresTheExplicitGatewayCommand()
    {
        var account = new GatewayAccountFake();
        using var shell = new PrimeShellState();
        await using var gateway = new GatewayController(shell,
            _ => Task.FromResult<IPrimeGatewayAccount>(account));

        Assert.False(shell.HasNetworkIdentity);
        Assert.True(await gateway.UseGuestAsync());
        Assert.True(shell.GuestSelected);
        Assert.True(shell.HasNetworkIdentity);
        Assert.False(shell.SignedIn);

        Assert.True(await gateway.SignOutAsync());
        Assert.False(shell.HasNetworkIdentity);
    }

    [Fact]
    public async Task AuthenticatedIdentityDoesNotOverwriteLocalGuestDisplayName()
    {
        string previous = MphRead.Mods.Launcher.LauncherPrefs.PlayerName;
        try
        {
            MphRead.Mods.Launcher.LauncherPrefs.PlayerName = "Local Guest";
            var account = new GatewayAccountFake();
            using var shell = new PrimeShellState();
            await using var gateway = new GatewayController(shell,
                _ => Task.FromResult<IPrimeGatewayAccount>(account));

            Assert.True(await gateway.SignInAsync("pilot@example.test", "secret"));

            Assert.True(shell.SignedIn);
            Assert.Equal("Pilot", shell.DisplayName);
            Assert.Equal("Local Guest", MphRead.Mods.Launcher.LauncherPrefs.PlayerName);
        }
        finally
        {
            MphRead.Mods.Launcher.LauncherPrefs.PlayerName = previous;
        }
    }

    [Fact]
    public async Task RankingsDropsAResponseFromAnOlderQuery()
    {
        PlayerId player = new(Guid.NewGuid());
        var query = new DeferredRankingQueryFake(player);
        using var shell = new PrimeShellState();
        using var rankings = new RankingsController(shell, query);

        Task<LeaderboardPage?> oldLoad = rankings.LoadAsync();
        rankings.SetMetric("kills");
        query.Complete();

        Assert.Null(await oldLoad);
        Assert.Empty(rankings.State.Rows);
        Assert.Equal("kills", rankings.State.Metric);
    }

    [Fact]
    public async Task TheatreUsesTheProvidedLocalLibraryForLoadDeleteAndImport()
    {
        var library = new ReplayLibraryFake();
        using var theatre = new TheatreController(library);

        await theatre.LoadAsync();
        Assert.Single(theatre.State.Replays);
        Assert.True(await theatre.DeleteAsync(theatre.State.Replays[0]));
        Assert.Empty(theatre.State.Replays);

        Assert.True(await theatre.ImportAsync("incoming.fpreplay"));
        Assert.Single(theatre.State.Replays);
        Assert.Equal("imported.fpreplay", theatre.State.Replays[0].FileName);
    }

    private sealed class RankingQueryFake : IPrimeRankingQueries
    {
        private readonly PlayerId _player;
        public RankingQueryFake(PlayerId player) => _player = player;
        public string BackendScope => "https://backend.test/";
        public PlayerId? PlayerId => _player;
        public string? LastMetric { get; private set; }
        public Hunter? LastHunter { get; private set; }

        public Task<LeaderboardPage> GetLeaderboardAsync(string metric, string? cursor,
            Hunter? hunter, CancellationToken cancellationToken)
        {
            LastMetric = metric;
            LastHunter = hunter;
            return Task.FromResult(new LeaderboardPage(metric, "official", null,
                ImmutableArray.Create(new LeaderboardEntry(_player, "Pilot", 8, 2, 3, 4, 4, 42)), null,
                hunter));
        }
    }

    private sealed class CareerQueryFake : IPrimeCareerQueries
    {
        private readonly PlayerId _player;
        public CareerQueryFake(PlayerId player) => _player = player;
        public string BackendScope => "https://backend.test/";
        public PlayerId? PlayerId => _player;
        public int LicenseCalls { get; private set; }

        public Task<HunterLicense> GetLicenseAsync(PlayerId player, CancellationToken cancellationToken)
        {
            LicenseCalls++;
            return Task.FromResult(new HunterLicense(player, "Pilot", 0,
                DateTimeOffset.UnixEpoch, Points: 12, Tier: 1));
        }

        public Task<CareerSummary> GetCareerAsync(PlayerId player, CancellationToken cancellationToken)
            => Task.FromResult(new CareerSummary("official", null,
                new CareerTotals(0, 0, 0, 0, 0, 0, 0, 0, null, 0, null, null, 0, 0, null, null),
                null, null, null, null, null, null, 0, "inactive",
                new CareerRatingSummary(0, 1, "Bounty Hunter", 40, null, "PairwiseNormalizedV1")));

        public Task<MatchHistoryPage> GetHistoryAsync(PlayerId player, long? before,
            CancellationToken cancellationToken)
            => Task.FromResult(new MatchHistoryPage(ImmutableArray<MatchHistoryEntry>.Empty, null));

        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DeferredRankingQueryFake : IPrimeRankingQueries
    {
        private readonly PlayerId _player;
        private readonly TaskCompletionSource<LeaderboardPage> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeferredRankingQueryFake(PlayerId player) => _player = player;
        public string BackendScope => "https://backend.test/";
        public PlayerId? PlayerId => _player;

        public Task<LeaderboardPage> GetLeaderboardAsync(string metric, string? cursor,
            Hunter? hunter, CancellationToken cancellationToken) => _result.Task;

        public void Complete() => _result.TrySetResult(new LeaderboardPage("rp", "official", null,
            ImmutableArray.Create(new LeaderboardEntry(_player, "Old Pilot", 10, 1, 1, 0, 1, 10)),
            null, null));
    }

    private sealed class DeferredCareerQueryFake : IPrimeCareerQueries
    {
        private readonly PlayerId _player;
        private readonly TaskCompletionSource<HunterLicense> _license =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeferredCareerQueryFake(PlayerId player) => _player = player;
        public string BackendScope => "https://backend.test/";
        public PlayerId? PlayerId => _player;

        public Task<HunterLicense> GetLicenseAsync(PlayerId player,
            CancellationToken cancellationToken) => _license.Task;

        public Task<CareerSummary> GetCareerAsync(PlayerId player,
            CancellationToken cancellationToken) => throw new InvalidOperationException("stale request continued");

        public Task<MatchHistoryPage> GetHistoryAsync(PlayerId player, long? before,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Complete() => _license.TrySetResult(new HunterLicense(_player,
            "Old Pilot", 0, DateTimeOffset.UnixEpoch));
    }

    private sealed class GatewayAccountFake : IPrimeGatewayAccount
    {
        public Uri Backend => new("https://backend.test/");
        public bool IsSignedIn { get; private set; }
        public AccountIdentity? Identity { get; private set; }
        public bool FailSignIn { get; init; }
        public int RestoreCalls { get; private set; }

        public Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return Task.FromResult(false);
        }

        public Task SignInAsync(string email, string password, CancellationToken cancellationToken)
        {
            if (FailSignIn) throw new InvalidOperationException("invalid credentials");
            IsSignedIn = true;
            Identity = new AccountIdentity(new PlayerId(Guid.NewGuid()), true, true);
            return Task.CompletedTask;
        }

        public Task<AccountRegistration> RegisterAsync(string email, string password, string displayName,
            CancellationToken cancellationToken)
            => Task.FromResult(new AccountRegistration(new PlayerId(Guid.NewGuid()), true));

        public Task ConfirmEmailAsync(PlayerId playerId, string code, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResendConfirmationAsync(string email, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = false;
            Identity = null;
            return Task.CompletedTask;
        }

        public Task<HunterLicense> GetLicenseAsync(PlayerId player, CancellationToken cancellationToken)
            => Task.FromResult(new HunterLicense(player, "Pilot", 0, DateTimeOffset.UnixEpoch));

        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ReplayLibraryFake : IPrimeReplayLibrary
    {
        private readonly List<PrimeReplayEntry> _replays =
        [
            new("one", "/virtual/one.fpreplay", "one.fpreplay", "Alinos Gateway",
                new DateTime(2026, 1, 1), 1024)
        ];

        public string Directory => "/virtual";
        public bool SupportsImport => true;
        public bool SupportsExport => true;
        public bool SupportsRename => true;
        public bool SupportsReveal => false;
        public IReadOnlyList<PrimeReplayEntry> List() => _replays.ToArray();
        public bool Delete(PrimeReplayEntry replay) => _replays.RemoveAll(item => item.Id == replay.Id) == 1;
        public bool Rename(PrimeReplayEntry replay, string fileName) => false;
        public bool Export(PrimeReplayEntry replay, string destinationPath) => false;
        public bool Reveal(PrimeReplayEntry replay) => false;

        public bool Import(string sourcePath, out PrimeReplayEntry? imported)
        {
            imported = new PrimeReplayEntry("imported", "/virtual/imported.fpreplay",
                "imported.fpreplay", "", new DateTime(2026, 1, 2), 2048);
            _replays.Add(imported);
            return true;
        }
    }
}
