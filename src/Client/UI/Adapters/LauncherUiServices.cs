using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;

namespace MphRead.Mods.UI.Adapters;

/// <summary>Connects the G6 screen contracts to the existing launcher and session services.</summary>
internal sealed class LauncherUiServices : IDisposable
{
    private readonly LauncherAccountController _account;
    private readonly LauncherReplayConfirmation _confirmation;
    private readonly CancellationTokenSource _lifetime = new();

    public LauncherUiServices(ClientUiRuntime runtime, MenuSettings settings,
        IReadOnlyList<string> rooms, bool isAndroid)
    {
        _account = new LauncherAccountController();
        var browser = new LauncherServerBrowserController(runtime);
        var maps = new LauncherMapCatalogController(rooms);
        var play = new LauncherPlayController(runtime, browser, rooms, settings);
        var privateMatches = new LauncherPrivateMatchController(runtime, settings);
        var replay = new LauncherReplayController(runtime);
        _confirmation = new LauncherReplayConfirmation(runtime.State.Router, runtime.Shell);
        var lobby = new CoordinatorLobbyAdapter(runtime.Coordinator,
            new ReliableLobbyMutationTransport(runtime.Coordinator), leave: runtime.LeaveSession);
        Lobby = lobby;
        Services = new UiScreenServices
        {
            Home = _account,
            Account = _account,
            HunterLicense = _account,
            Play = play,
            Servers = browser,
            Maps = maps,
            PrivateMatches = privateMatches,
            Replays = replay,
            ReplayConfirmation = _confirmation,
            Settings = new LauncherSettingsController(settings, runtime.State),
            Lobby = lobby,
            PostMatch = new CoordinatorPostMatchAdapter(runtime.Coordinator, rating: _account, actions:
                new LauncherPostMatchActions(runtime, lobby)),
            IsAndroid = isAndroid
        };
        // Restoring is best-effort and never delays shell construction. The account controller
        // reports failures as UI state and raises IdentityChanged when protected refresh material
        // resolves to a current Hunter License.
        _ = _account.RestoreAsync(_lifetime.Token);
    }

    public UiScreenServices Services { get; }
    public CoordinatorLobbyAdapter Lobby { get; }

    public void Dispose()
    {
        _lifetime.Cancel();
        _account.Dispose();
        _confirmation.Cancel();
        _lifetime.Dispose();
    }
}

internal sealed class ReliableLobbyMutationTransport : ILobbyMutationTransport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);
    private readonly ClientSessionCoordinator _coordinator;

    public ReliableLobbyMutationTransport(ClientSessionCoordinator coordinator)
        => _coordinator = coordinator;

    public Task<LobbyMutationReceipt> SendAsync(LobbyRequestPacket request,
        CancellationToken cancellationToken)
    {
        byte[] payload = new byte[LobbyRequestPacket.Size];
        request.Write(payload);
        return SendCoreAsync(ReliableEventType.LobbyRequest, payload, request.RequestId,
            chat: false, cancellationToken);
    }

    public Task<LobbyMutationReceipt> SendChatAsync(LobbyChatRequestPacket request,
        CancellationToken cancellationToken)
    {
        byte[] payload = new byte[LobbyChatRequestPacket.Size];
        request.Write(payload);
        return SendCoreAsync(ReliableEventType.LobbyChat, payload, request.RequestId,
            chat: true, cancellationToken);
    }

    private async Task<LobbyMutationReceipt> SendCoreAsync(ReliableEventType type, byte[] payload,
        uint requestId, bool chat, CancellationToken cancellationToken)
    {
        NetClient client = _coordinator.Session?.Client
            ?? throw new InvalidOperationException("The lobby session is no longer connected.");
        NetConnection connection = client.Connection
            ?? throw new InvalidOperationException("The lobby connection is unavailable.");
        if (!connection.Reliable.TryEnqueue(type, payload, out _))
            throw new InvalidOperationException("The reliable lobby queue is full. Try again shortly.");

        DateTimeOffset deadline = DateTimeOffset.UtcNow + ResponseTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _coordinator.Poll();
            LobbyFeedbackPacket? feedback = client.LobbyFeedback is { RequestId: var feedbackId }
                && feedbackId == requestId ? client.LobbyFeedback : null;
            LobbyChatPacket? replyChat = chat && client.LobbyChat is { RequestId: var chatId }
                && chatId == requestId ? client.LobbyChat : null;
            LobbySnapshotPacket? snapshot = client.LobbySnapshot;
            if (feedback is not null || replyChat is not null)
                return new LobbyMutationReceipt(snapshot, feedback, replyChat);
            await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The server did not confirm the lobby action.");
    }
}

internal sealed class LauncherAccountController : IHomeScreenController, IAccountScreenController,
    IHunterLicenseController, IPostMatchRatingSource, IDisposable
{
    private UiAccountSnapshot _snapshot = new(UiAccountPhase.Guest, "Guest", string.Empty,
        false, false, "Configure a Backend address in Settings to sign in.");
    private HunterLicense? _license;

    public UiAccountSnapshot Snapshot => _snapshot;
    public event Action? IdentityChanged;
    public UiAccountIdentity Identity => _license is { } license
        ? new UiAccountIdentity(license.DisplayName, license.Title, license.Points, false)
        : new UiAccountIdentity(LauncherPrefs.PlayerName, "Guest", null, true);

    public async Task<UiActionResult> RestoreAsync(CancellationToken cancellationToken)
    {
        _snapshot = _snapshot with { Phase = UiAccountPhase.Restoring, Message = "Restoring session…" };
        try
        {
            AccountSession session = await SessionAsync(cancellationToken).ConfigureAwait(false);
            if (!await session.RestoreAsync(cancellationToken).ConfigureAwait(false))
            {
                _snapshot = new(UiAccountPhase.Guest, "Guest", string.Empty, false, false,
                    "No saved session was found.");
                return UiActionResult.Success(_snapshot.Message);
            }
            return await RefreshIdentityAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { return Fail(error, "Session restore"); }
    }

    public async Task<UiActionResult> SignInAsync(string email, string password,
        CancellationToken cancellationToken)
    {
        _snapshot = _snapshot with { Phase = UiAccountPhase.SigningIn, Message = "Signing in…" };
        try
        {
            AccountSession session = await SessionAsync(cancellationToken).ConfigureAwait(false);
            await session.SignInAsync(email, password, cancellationToken).ConfigureAwait(false);
            return await RefreshIdentityAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { return Fail(error, "Sign in"); }
    }

    public async Task<UiActionResult> SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (AccountSessions.Current is { } session)
                await session.SignOutAsync(cancellationToken).ConfigureAwait(false);
            _license = null;
            _snapshot = new(UiAccountPhase.Guest, "Guest", string.Empty, false, false);
            IdentityChanged?.Invoke();
            return UiActionResult.Success();
        }
        catch (Exception error) { return Fail(error, "Sign out"); }
    }

    public async Task<HunterLicenseOverview> LoadOverviewAsync(CancellationToken cancellationToken)
    {
        AccountSession session = SignedIn();
        HunterLicense license = await session.GetLicenseAsync(session.Identity!.PlayerId,
            cancellationToken).ConfigureAwait(false);
        CareerSummary career = await session.GetCareerAsync(session.Identity.PlayerId,
            cancellationToken).ConfigureAwait(false);
        _license = license;
        CareerTotals totals = career.Totals;
        return new HunterLicenseOverview(license.DisplayName, license.Title, license.Points,
            license.NextThreshold, career.MostPlayedHunter?.Key, totals.Wins, totals.LossCount,
            totals.WinRatio ?? 0, totals.KillDeathRatio ?? 0,
            TimeSpan.FromSeconds(totals.PlayedTicks / 60d), career.FavoriteMap?.Key,
            career.FavoriteWeapon?.Key, career.FavoriteMode?.Key, totals.LongestKillStreak,
            totals.LongestWinStreak, null);
    }

    public async Task<IReadOnlyList<HunterLicenseStat>> LoadStatsAsync(HunterLicenseTab tab,
        CancellationToken cancellationToken)
    {
        AccountSession session = SignedIn();
        CareerSummary career = await session.GetCareerAsync(session.Identity!.PlayerId,
            cancellationToken).ConfigureAwait(false);
        CareerTotals totals = career.Totals;
        return tab switch
        {
            HunterLicenseTab.Hunters => Choices("Hunter", career.MostPlayedHunter,
                career.BestHunter),
            HunterLicenseTab.Weapons => Choices("Weapon", career.FavoriteWeapon),
            HunterLicenseTab.Maps => Choices("Map", career.FavoriteMap, career.BestMap),
            _ => new[]
            {
                new HunterLicenseStat("kills", "Kills", totals.Kills.ToString(CultureInfo.InvariantCulture)),
                new HunterLicenseStat("deaths", "Deaths", totals.Deaths.ToString(CultureInfo.InvariantCulture)),
                new HunterLicenseStat("assists", "Assists", totals.Assists.ToString(CultureInfo.InvariantCulture)),
                new HunterLicenseStat("damage", "Damage", totals.Damage.ToString(CultureInfo.InvariantCulture))
            }
        };
    }

    public async Task<HunterLicenseMatchPage> LoadMatchesAsync(string? cursor,
        CancellationToken cancellationToken)
    {
        AccountSession session = SignedIn();
        long? before = cursor is null ? null : Int64.Parse(cursor, CultureInfo.InvariantCulture);
        MatchHistoryPage page = await session.GetHistoryAsync(session.Identity!.PlayerId, before,
            cancellationToken).ConfigureAwait(false);
        return new HunterLicenseMatchPage(page.Entries.Select(entry => new HunterLicenseMatch(
            entry.MatchId, entry.EndedAt, entry.RoomKey, entry.Mode.ToString(),
            entry.Outcome.ToString(), $"{entry.Kills} K / {entry.Deaths} D / {entry.Assists} A",
            entry.RatingStatus)).ToImmutableArray(),
            page.NextCursor?.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<(int Delta, int Points)?> AwaitAsync(uint matchId,
        CancellationToken cancellationToken)
    {
        // Wire match IDs and official report UUIDs are separate identities. The authenticated
        // license projection explicitly confirms completion by advancing its transaction marker.
        _ = matchId;
        if (_license is not { } before || AccountSessions.Current is not { IsSignedIn: true } session
            || session.Identity is null)
            return null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HunterLicense updated = await session.GetLicenseAsync(session.Identity.PlayerId,
                cancellationToken).ConfigureAwait(false);
            _license = updated;
            if (CompletedRating(before, updated) is { } completed)
            {
                IdentityChanged?.Invoke();
                return completed;
            }
            if (attempt < 5)
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    internal static (int Delta, int Points)? CompletedRating(HunterLicense before,
        HunterLicense updated)
        => updated.LastOfficialMatchId is { } matchId
            && matchId != before.LastOfficialMatchId
            && updated.LastOfficialDelta is { } delta
                ? (delta, updated.Points)
                : null;

    private static IReadOnlyList<HunterLicenseStat> Choices(string label,
        params CareerChoice?[] choices) => choices.Where(choice => choice is not null)
        .Select((choice, index) => new HunterLicenseStat($"{label}:{index}",
            index == 0 ? $"Favorite {label}" : $"Best {label}", choice!.Key,
            $"{choice.Samples} matches")).ToArray();

    private static AccountSession SignedIn() => AccountSessions.Current is { IsSignedIn: true } session
        ? session : throw new InvalidOperationException("Sign in to view your Hunter License.");

    private static async Task<AccountSession> SessionAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(LauncherPrefs.BackendAddress, UriKind.Absolute, out Uri? backend)
            || !AccountSession.IsAllowedBackend(backend))
            throw new InvalidOperationException("Configure a valid HTTPS Backend address in Settings first.");
        return await AccountSessions.ConfigureAsync(backend, restore: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UiActionResult> RefreshIdentityAsync(AccountSession session,
        CancellationToken cancellationToken)
    {
        AccountIdentity identity = session.Identity
            ?? throw new InvalidOperationException("The restored account has no identity.");
        _license = await session.GetLicenseAsync(identity.PlayerId, cancellationToken)
            .ConfigureAwait(false);
        LauncherPrefs.PlayerName = _license.DisplayName;
        LauncherPrefs.Save();
        _snapshot = new(UiAccountPhase.SignedIn, _license.DisplayName, identity.PlayerId.ToString(),
            identity.EmailConfirmed, identity.EmailEligibleForOfficialPlay);
        IdentityChanged?.Invoke();
        return UiActionResult.Success();
    }

    private UiActionResult Fail(Exception error, string operation)
    {
        string message = AsyncScreenState.FriendlyFailure(error, operation);
        _snapshot = _snapshot with { Phase = error is System.Net.Http.HttpRequestException
            ? UiAccountPhase.Offline : UiAccountPhase.Failed, Message = message };
        return UiActionResult.Failure(message);
    }

    public void Dispose() => IdentityChanged = null;
}

internal sealed class LauncherServerBrowserController : IServerBrowserController
{
    private readonly ClientUiRuntime _runtime;
    private readonly ServerBrowserPreferences _preferences = ServerBrowserPreferences.Load();

    public LauncherServerBrowserController(ClientUiRuntime runtime) => _runtime = runtime;

    public async Task<IReadOnlyList<UiServerEntry>> QueryAsync(CancellationToken cancellationToken)
    {
        MasterListResult list = await Task.Run(() => NetMasterClient.Query(
            LauncherPrefs.MasterHost, LauncherPrefs.MasterPort), cancellationToken).ConfigureAwait(false);
        if (!list.Answered) throw new InvalidOperationException(list.IncompatibilityReason);
        if (!list.Compatible) throw new InvalidOperationException(list.IncompatibilityReason);
        using var slots = new SemaphoreSlim(4);
        Task<UiServerEntry>[] probes = list.Servers.Select(async listing =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ServerStatus status = await Task.Run(() => NetStatus.Query(listing.Address,
                    listing.Port, allowJoinProbe: false), cancellationToken).ConfigureAwait(false);
                return Map(listing, status);
            }
            finally { slots.Release(); }
        }).ToArray();
        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    public async Task<UiActionResult> JoinAsync(UiServerEntry server, bool spectate,
        CancellationToken cancellationToken)
    {
        if (!TryEndpoint(server.Endpoint, out string host, out int port))
            return UiActionResult.Failure("This server endpoint is invalid.");
        if (_runtime.Coordinator.Session is not null) _runtime.LeaveSession();
        bool joined = await NetLaunch.JoinAsync(host, port, LauncherPrefs.PlayerName,
            Hunters.Resolve(LauncherPrefs.LastHunter), cancel: cancellationToken,
            observer: spectate).ConfigureAwait(false);
        if (!joined) return UiActionResult.Failure(NetLaunch.LastJoinError);
        LauncherPrefs.ServerAddress = host;
        LauncherPrefs.ServerPort = port;
        LauncherPrefs.LastKind = (int)LaunchKind.Online;
        LauncherPrefs.Save();
        _preferences.Visited(server.Endpoint);
        _preferences.Save();
        _runtime.Joined(hosted: false);
        return UiActionResult.Success();
    }

    public void SetFavorite(UiServerEntry server, bool favorite)
    {
        if (_preferences.IsFavorite(server.Endpoint) != favorite)
            _preferences.ToggleFavorite(server.Endpoint);
        _preferences.Save();
    }

    public void CopyEndpoint(UiServerEntry server)
    {
        TopLevel? top = TopLevel.GetTopLevel(_runtime.Shell);
        if (top?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(server.Endpoint);
    }

    private UiServerEntry Map(MasterListing listing, ServerStatus status)
    {
        string endpoint = listing.Endpoint;
        return new UiServerEntry(endpoint,
            status.ServerName.Length > 0 ? status.ServerName : listing.ServerName,
            endpoint, status.RoomKey.Length > 0 ? status.RoomKey : listing.RoomKey,
            status.Mode.ToString(), status.Players, status.MaxPlayers, status.Bots,
            status.Observers, status.Latency, status.HasSessionState ? status.Phase.ToString() : "Online",
            status.TimeRemaining < 0 ? null : TimeSpan.FromSeconds(status.TimeRemaining),
            status.RulesetPreset.ToString(), status.ServerId != Guid.Empty,
            status.RankingEligibility == RankingEligibility.VerifiedServerOnly, status.Compatible,
            _preferences.IsFavorite(endpoint), _preferences.Recent.Contains(endpoint),
            status.FriendlyFire, status.PlayerRadar, status.SpawnPolicy.ToString(),
            status.LateJoinPolicy != LateJoinPolicy.Disabled, status.RequiresTicket,
            status.HasSessionState, status.JoinDisposition, status.LobbyPlayers,
            status.LobbyObservers, status.ReadyPlayers, status.RankedLocked,
            status.TournamentLocked);
    }

    internal static bool TryEndpoint(string endpoint, out string host, out int port)
    {
        host = endpoint.Trim();
        port = NetConfig.DefaultPort;
        int split = host.LastIndexOf(':');
        if (split > 0 && Int32.TryParse(host[(split + 1)..], out int parsed)
            && parsed is > 0 and <= UInt16.MaxValue)
        {
            port = parsed;
            host = host[..split];
        }
        return host.Length > 0;
    }
}

internal sealed class LauncherPlayController : IPlayScreenController
{
    private readonly ClientUiRuntime _runtime;
    private readonly LauncherServerBrowserController _browser;
    private readonly IReadOnlyList<string> _rooms;
    private readonly MenuSettings _settings;

    public LauncherPlayController(ClientUiRuntime runtime, LauncherServerBrowserController browser,
        IReadOnlyList<string> rooms, MenuSettings settings)
    {
        _runtime = runtime;
        _browser = browser;
        _rooms = rooms;
        _settings = settings;
    }

    public RankedAvailability Ranked => RankedAvailability.Unavailable(
        "Ranked requires a verified public server selected from Server Browser.");

    public async Task<UiActionResult> QuickPlayAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<UiServerEntry> servers = await _browser.QueryAsync(cancellationToken)
            .ConfigureAwait(false);
        UiServerEntry? best = servers.Where(server => server.CanJoin && !server.Full)
            .OrderBy(server => server.Ping < 0
                ? Int32.MaxValue : server.Ping).FirstOrDefault();
        return best is null ? UiActionResult.Failure("No compatible public server is available.")
            : await _browser.JoinAsync(best, spectate: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<UiActionResult> StartRankedAsync(CancellationToken cancellationToken)
        => Task.FromResult(UiActionResult.Failure(Ranked.Reason));

    public async Task<UiActionResult> StartPracticeAsync(CancellationToken cancellationToken)
    {
        if (_rooms.Count == 0) return UiActionResult.Failure("No playable maps are installed.");
        if (_runtime.Coordinator.Session is not null) _runtime.LeaveSession();
        string room = _settings.RoomKey.Length > 0 && _rooms.Contains(_settings.RoomKey)
            ? _settings.RoomKey : _rooms[0];
        bool started;
        if (NetHostSession.Available)
        {
            started = await Task.Run(() => NetHostSession.StartAndJoin(
                _settings.FriendlyFire == "on", LauncherPrefs.HostPort, LauncherPrefs.PlayerName,
                Hunters.Resolve(LauncherPrefs.LastHunter), room, GameMode.Battle, 7 * 60, 7,
                maxPlayers: 4, practice: true), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            MatchRules rules = MapRotation.SingleMatch(room, GameMode.Battle, 7 * 60, 7)
                .Current.ToMatchRules(4, _settings.FriendlyFire == "on");
            HostedGame game = await Task.Run(() => NetMasterClient.RequestGame(
                LauncherPrefs.MasterHost, LauncherPrefs.MasterPort, rules,
                LobbyPolicy.PrivateHosted, botMinimumParticipants: 4,
                botSkill: LauncherPrefs.BotLevel, maxObservers: 0, observerDelaySeconds: 0,
                serverName: $"{LauncherPrefs.PlayerName}'s practice", practice: true),
                cancellationToken).ConfigureAwait(false);
            if (!game.Started) return UiActionResult.Failure(game.Reason);
            started = await NetLaunch.JoinAsync(game.Host, game.Port, LauncherPrefs.PlayerName,
                Hunters.Resolve(LauncherPrefs.LastHunter), cancel: cancellationToken,
                ownerCapability: game.OwnerToken).ConfigureAwait(false);
        }
        if (!started) return UiActionResult.Failure(NetLaunch.LastJoinError.Length > 0
            ? NetLaunch.LastJoinError : NetHostSession.LastError ?? "Practice could not start.");
        _runtime.Joined(hosted: true);
        return UiActionResult.Success();
    }
}

internal sealed class LauncherMapCatalogController : IMapCatalogController
{
    private readonly IReadOnlyList<string> _rooms;
    public LauncherMapCatalogController(IReadOnlyList<string> rooms) => _rooms = rooms;
    public Task<IReadOnlyList<UiMapEntry>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<UiMapEntry>>(_rooms.Select(room =>
        {
            (RoomMetadata? metadata, _) = Metadata.GetRoomByName(room);
            string name = metadata?.InGameName ?? room;
            bool custom = MapGen.CustomRooms.Definitions.Any(definition => definition.Name == room);
            return new UiMapEntry(room, name, "All multiplayer modes", "2–8", custom, null);
        }).ToArray());
    }

    public async Task<byte[]?> LoadThumbnailAsync(string mapId, CancellationToken cancellationToken)
    {
        string path = ThumbnailGenerator.PathFor(mapId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false) : null;
    }
}

internal sealed class LauncherPrivateMatchController : IPrivateMatchController
{
    private readonly ClientUiRuntime _runtime;
    public LauncherPrivateMatchController(ClientUiRuntime runtime, MenuSettings settings)
    {
        _runtime = runtime;
        _ = settings;
    }

    public PrivateMatchCapabilities Capabilities => new(
        AlwaysListed: LauncherPrefs.HostOnMaster || !NetHostSession.Available,
        SupportsPassword: false);

    public async Task<UiActionResult> CreateAsync(PrivateMatchDraft draft,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = draft.Validate();
        if (errors.Count > 0) return UiActionResult.Failure(String.Join(" ", errors));
        if (!String.IsNullOrEmpty(draft.Password))
            return UiActionResult.Failure("Password admission is not supported by the current lobby protocol.");
        if (Capabilities.AlwaysListed && !draft.PublicListing)
            return UiActionResult.Failure("Directory-hosted lobbies are always listed.");
        if (!TryBuildRules(draft, out MatchRules? rules, out string ruleError))
            return UiActionResult.Failure(ruleError);
        var policy = new LobbyPolicy(LobbyPolicyKind.PersistentLobby, draft.ReadyRequired,
            minimumPlayers: 1, hostMayForceStart: true);
        if (_runtime.Coordinator.Session is not null) _runtime.LeaveSession();
        bool joined;
        if (Capabilities.AlwaysListed)
        {
            HostedGame game = await Task.Run(() => NetMasterClient.RequestGame(
                LauncherPrefs.MasterHost, LauncherPrefs.MasterPort, rules!, policy,
                Math.Min(draft.MaxPlayers, draft.Bots + 1), draft.BotSkill,
                draft.MaxSpectators, 0, draft.Name, practice: draft.Practice),
                cancellationToken).ConfigureAwait(false);
            if (!game.Started) return UiActionResult.Failure(game.Reason);
            joined = await NetLaunch.JoinAsync(game.Host, game.Port, LauncherPrefs.PlayerName,
                Hunters.Resolve(LauncherPrefs.LastHunter), cancel: cancellationToken,
                ownerCapability: game.OwnerToken).ConfigureAwait(false);
        }
        else
        {
            (string Host, int Port, string Name)? listing = draft.PublicListing
                ? (LauncherPrefs.MasterHost, LauncherPrefs.MasterPort, draft.Name) : null;
            joined = await Task.Run(() => NetHostSession.StartAndJoin(
                rules!.FriendlyFire, LauncherPrefs.HostPort, LauncherPrefs.PlayerName,
                Hunters.Resolve(LauncherPrefs.LastHunter), rules.RoomKey,
                rules.Mode.ToLegacyMode(), (float)(rules.TimeLimit?.TotalSeconds ?? 0),
                rules.LegacyPointGoal,
                draft.MaxPlayers, listing, botMinimumParticipants:
                    Math.Min(draft.MaxPlayers, draft.Bots + 1),
                botSkill: draft.BotSkill, configuredRules: rules, lobbyPolicy: policy,
                maxObservers: draft.MaxSpectators), cancellationToken).ConfigureAwait(false);
        }
        if (!joined) return UiActionResult.Failure(NetLaunch.LastJoinError.Length > 0
            ? NetLaunch.LastJoinError : NetHostSession.LastError ?? "The private lobby could not start.");
        LauncherPrefs.Bots = draft.Bots;
        LauncherPrefs.LastKind = (int)LaunchKind.Host;
        LauncherPrefs.Save();
        _runtime.Joined(hosted: true);
        return UiActionResult.Success();
    }

    internal static bool TryBuildRules(PrivateMatchDraft draft, out MatchRules? rules,
        out string error)
    {
        rules = null;
        error = string.Empty;
        try
        {
            if (!Enum.TryParse(draft.Mode, true, out MatchMode mode)
                || !Enum.TryParse(draft.Preset, true, out RulesetPreset preset)
                || !Enum.TryParse(draft.RadarPolicy, true, out RadarPolicy radar)
                || !Enum.TryParse(draft.SpawnPolicy, true, out SpawnPolicy spawn)
                || !Enum.TryParse(draft.OvertimePolicy, true, out OvertimePolicy overtime)
                || !Enum.TryParse(draft.LateJoinPolicy, true, out LateJoinPolicy lateJoin))
            {
                error = "Choose supported match rules.";
                return false;
            }
            rules = new MatchRules(mode, draft.MapId!, draft.MaxPlayers,
                draft.TimeLimitSeconds == 0 ? null : TimeSpan.FromSeconds(draft.TimeLimitSeconds),
                draft.ScoreGoal,
                draft.ObjectiveTimeSeconds == 0 ? null : TimeSpan.FromSeconds(draft.ObjectiveTimeSeconds),
                draft.StartingLives, draft.FriendlyFire, draft.AffinityWeapons,
                radar == RadarPolicy.Enabled, damageLevel: draft.DamageLevel,
                spawnPolicy: spawn, overtimePolicy: overtime, lateJoinPolicy: lateJoin,
                rulesetPreset: preset, rankingEligibility: RankingEligibility.Unranked,
                radarPolicy: radar);
            MatchLifecycle.ValidateRules(rules);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException or OverflowException)
        {
            error = exception.Message;
            rules = null;
            return false;
        }
    }
}

internal sealed class LauncherReplayController : IReplayLibraryController
{
    private readonly ClientUiRuntime _runtime;
    public LauncherReplayController(ClientUiRuntime runtime) => _runtime = runtime;

    public Task<IReadOnlyList<ReplayMetadata>> LoadAsync(CancellationToken cancellationToken)
        => Task.Run<IReadOnlyList<ReplayMetadata>>(() => DemoLibrary.List().Select(demo =>
        {
            using DemoReader? reader = DemoReader.Open(demo.Path);
            bool compatible = reader is not null && DemoFile.IsSupportedProtocol(reader.ProtocolVersion);
            return new ReplayMetadata(demo.Path, demo.FileName, demo.Recorded, demo.Room,
                "Recorded", reader is null ? TimeSpan.Zero : TimeSpan.FromSeconds(reader.LastFrame / 60d),
                0, null, compatible, reader?.RecoveredTail == true);
        }).ToArray(), cancellationToken);

    public Task<UiActionResult> PlayAsync(ReplayMetadata replay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(replay.Id)) return Task.FromResult(UiActionResult.Failure("The replay file no longer exists."));
        _runtime.RequestReplay(replay.Id);
        return Task.FromResult(UiActionResult.Success());
    }

    public Task<UiActionResult> DeleteAsync(ReplayMetadata replay, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(replay.Id);
            return UiActionResult.Success();
        }, cancellationToken);

    public async Task<UiActionResult> ImportAsync(CancellationToken cancellationToken)
    {
        TopLevel? top = TopLevel.GetTopLevel(_runtime.Shell);
        if (top?.StorageProvider is not { } storage)
            return UiActionResult.Failure("A file picker is unavailable on this platform.");
        IReadOnlyList<IStorageFile> selected = await storage.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import replay", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Prime Hunters replay")
                    { Patterns = ["*" + DemoFile.Extension] }]
            });
        IStorageFile? file = selected.FirstOrDefault();
        if (file is null) return UiActionResult.Failure("Import canceled.");
        string? source = file.TryGetLocalPath();
        if (source is null) return UiActionResult.Failure("The selected replay is not a local file.");
        Directory.CreateDirectory(DemoLibrary.Directory);
        string destination = Path.Combine(DemoLibrary.Directory, Path.GetFileName(source));
        await using FileStream from = File.OpenRead(source);
        await using FileStream to = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
        return UiActionResult.Success();
    }

    public Task<UiActionResult> ExportAsync(ReplayMetadata replay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LogShare.Current is not { } share)
            return Task.FromResult(UiActionResult.Failure("Sharing is unavailable on this platform."));
        return Task.FromResult(share.Share(replay.Id, replay.FileName, out string error)
            ? UiActionResult.Success() : UiActionResult.Failure(error));
    }
}

internal sealed class LauncherReplayConfirmation : IReplayDeleteConfirmation
{
    private readonly UiRouter _router;
    private readonly Control _shell;
    private TaskCompletionSource<bool>? _pending;
    public LauncherReplayConfirmation(UiRouter router, Control shell) { _router = router; _shell = shell; }

    public Task<bool> ConfirmDeleteAsync(ReplayMetadata replay, CancellationToken cancellationToken)
    {
        Cancel();
        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var yes = new PrimaryButton { Content = "Delete", AccessibleName = $"Delete {replay.FileName}" };
        var no = new SecondaryButton { Content = "Cancel", AccessibleName = "Cancel replay deletion" };
        var panel = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = $"Delete {replay.FileName}?" },
            new WrapPanel { Children = { yes, no } }
        }};
        yes.Click += (_, _) => Complete(true);
        no.Click += (_, _) => Complete(false);
        cancellationToken.Register(Cancel);
        _router.OpenModal("delete-replay", panel, "replays:delete");
        _ = _shell;
        return _pending.Task;
    }

    private void Complete(bool value)
    {
        TaskCompletionSource<bool>? pending = _pending;
        _pending = null;
        _router.CloseModal();
        pending?.TrySetResult(value);
    }

    public void Cancel() => Complete(false);
}

internal sealed class LauncherPostMatchActions : IPostMatchActionSource
{
    private readonly ClientUiRuntime _runtime;
    private readonly CoordinatorLobbyAdapter _lobby;
    public LauncherPostMatchActions(ClientUiRuntime runtime, CoordinatorLobbyAdapter lobby)
    { _runtime = runtime; _lobby = lobby; }

    public Task<UiActionResult> InvokeAsync(PostMatchAction action,
        CancellationToken cancellationToken) => action switch
    {
        PostMatchAction.Rematch when _lobby.Snapshot is { } snapshot => _lobby.RequestAsync(
            new UiLobbyCommand(UiLobbyAction.Rematch), snapshot.Revision, cancellationToken),
        PostMatchAction.HunterLicense => Navigate(UiRoute.HunterLicense),
        PostMatchAction.NextMap => ReturnToLobby(),
        _ => Task.FromResult(UiActionResult.Failure("The server does not offer that action."))
    };

    private Task<UiActionResult> Navigate(UiRoute route)
    {
        _runtime.State.Router.Navigate(route);
        return Task.FromResult(UiActionResult.Success());
    }

    private Task<UiActionResult> ReturnToLobby()
    {
        if (!_runtime.Coordinator.ReturnToLobby())
            return Task.FromResult(UiActionResult.Failure(
                "The server has not opened the lobby for map selection yet."));
        _runtime.State.Router.Replace(UiRoute.Lobby);
        return Task.FromResult(UiActionResult.Success());
    }
}

internal sealed class LauncherSettingsController : ISettingsScreenController
{
    private readonly MenuSettings _settings;
    private readonly AppShell.AppShellState _shell;
    private static readonly IReadOnlyDictionary<SettingsCategory, string[]> Keys =
        new Dictionary<SettingsCategory, string[]>
        {
            [SettingsCategory.Gameplay] = [nameof(MenuSettings.DamageLevel), nameof(MenuSettings.AffinityWeapons)],
            [SettingsCategory.Controls] = ["launcher.player-name", "launcher.hunter"],
            [SettingsCategory.Video] = [nameof(MenuSettings.ResolutionScale), nameof(MenuSettings.Lighting),
                nameof(MenuSettings.Fog), nameof(MenuSettings.TextureFiltering), nameof(MenuSettings.FrameRateCap)],
            [SettingsCategory.Audio] = [nameof(MenuSettings.MusicVolume), nameof(MenuSettings.SfxVolume),
                nameof(MenuSettings.FeedbackVolume), nameof(MenuSettings.Language)],
            [SettingsCategory.HUD] = [nameof(MenuSettings.HitMarkers), nameof(MenuSettings.ShowFps)],
            [SettingsCategory.Radar] = [nameof(MenuSettings.RadarStyle), nameof(MenuSettings.RadarOrientation)],
            [SettingsCategory.Network] = ["launcher.server", "launcher.directory"],
            [SettingsCategory.Accessibility] = ["accessibility.ui-scale", "accessibility.large-text",
                "accessibility.reduced-motion", "accessibility.safe-area"],
            [SettingsCategory.Account] = ["launcher.backend"]
        };

    public LauncherSettingsController(MenuSettings settings, AppShell.AppShellState shell)
    { _settings = settings; _shell = shell; }

    public IReadOnlyList<SettingDescriptor> Read(SettingsCategory category)
        => Keys[category].Select(key => new SettingDescriptor(key, Label(key), Description(key),
            category, ReadValue(key), RestartRequired: false)).ToArray();

    public Task<UiActionResult> ApplyAsync(string key, object? value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            WriteValue(key, value?.ToString() ?? string.Empty);
            Persist();
            return Task.FromResult(UiActionResult.Success());
        }
        catch (Exception error) { return Task.FromResult(UiActionResult.Failure(error.Message)); }
    }

    public Task<UiActionResult> ResetCategoryAsync(SettingsCategory category,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var defaults = new MenuSettings();
        foreach (string key in Keys[category]) ResetValue(key, defaults);
        Persist();
        return Task.FromResult(UiActionResult.Success());
    }

    public Task<UiActionResult> RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var defaults = new MenuSettings();
        foreach (PropertyInfo property in typeof(MenuSettings).GetProperties())
            property.SetValue(_settings, property.GetValue(defaults));
        InputSettings.Reset();
        _shell.Accessibility.UiScale = 1;
        _shell.Accessibility.LargeText = false;
        _shell.Accessibility.ReducedMotion = false;
        _shell.Accessibility.SafeArea = 0;
        Persist();
        return Task.FromResult(UiActionResult.Success());
    }

    private object? ReadValue(string key) => key switch
    {
        "launcher.player-name" => LauncherPrefs.PlayerName,
        "launcher.hunter" => LauncherPrefs.LastHunter,
        "launcher.server" => $"{LauncherPrefs.ServerAddress}:{LauncherPrefs.ServerPort}",
        "launcher.directory" => $"{LauncherPrefs.MasterHost}:{LauncherPrefs.MasterPort}",
        "launcher.backend" => LauncherPrefs.BackendAddress,
        "accessibility.ui-scale" => _shell.Accessibility.UiScale,
        "accessibility.large-text" => _shell.Accessibility.LargeText,
        "accessibility.reduced-motion" => _shell.Accessibility.ReducedMotion,
        "accessibility.safe-area" => _shell.Accessibility.SafeArea,
        _ => typeof(MenuSettings).GetProperty(key)?.GetValue(_settings)
    };

    private void WriteValue(string key, string value)
    {
        switch (key)
        {
            case "launcher.player-name":
                if (String.IsNullOrWhiteSpace(value) || value.Trim().Length > 16)
                    throw new ArgumentException("Player name must be 1–16 characters.");
                LauncherPrefs.PlayerName = value.Trim(); break;
            case "launcher.hunter":
                if (!Enum.TryParse(value, true, out Hunter hunter) || (int)hunter is < 0 or > (int)Hunter.Random)
                    throw new ArgumentException("Choose a playable Hunter or Random.");
                LauncherPrefs.LastHunter = hunter; break;
            case "launcher.server": SetEndpoint(value, master: false); break;
            case "launcher.directory": SetEndpoint(value, master: true); break;
            case "launcher.backend":
                if (value.Length > 0 && (!Uri.TryCreate(value, UriKind.Absolute, out Uri? backend)
                    || !AccountSession.IsAllowedBackend(backend)))
                    throw new ArgumentException("Use HTTPS, or loopback HTTP for local testing.");
                LauncherPrefs.BackendAddress = value.Trim(); break;
            case "accessibility.ui-scale": _shell.Accessibility.UiScale = Double.Parse(value, CultureInfo.InvariantCulture); break;
            case "accessibility.large-text": _shell.Accessibility.LargeText = Boolean.Parse(value); break;
            case "accessibility.reduced-motion": _shell.Accessibility.ReducedMotion = Boolean.Parse(value); break;
            case "accessibility.safe-area": _shell.Accessibility.SafeArea = Double.Parse(value, CultureInfo.InvariantCulture); break;
            default:
                PropertyInfo property = typeof(MenuSettings).GetProperty(key)
                    ?? throw new ArgumentException("Unknown setting.");
                property.SetValue(_settings, value.Trim()); break;
        }
    }

    private void ResetValue(string key, MenuSettings defaults)
    {
        if (key.StartsWith("accessibility.", StringComparison.Ordinal))
        {
            if (key.EndsWith("ui-scale", StringComparison.Ordinal)) _shell.Accessibility.UiScale = 1;
            else if (key.EndsWith("large-text", StringComparison.Ordinal)) _shell.Accessibility.LargeText = false;
            else if (key.EndsWith("reduced-motion", StringComparison.Ordinal)) _shell.Accessibility.ReducedMotion = false;
            else _shell.Accessibility.SafeArea = 0;
            return;
        }
        PropertyInfo? property = typeof(MenuSettings).GetProperty(key);
        if (property is not null) property.SetValue(_settings, property.GetValue(defaults));
    }

    private static void SetEndpoint(string text, bool master)
    {
        if (!LauncherServerBrowserController.TryEndpoint(text, out string host, out int port))
            throw new ArgumentException("Enter a host or host:port.");
        if (master) { LauncherPrefs.MasterHost = host; LauncherPrefs.MasterPort = port; }
        else { LauncherPrefs.ServerAddress = host; LauncherPrefs.ServerPort = port; }
    }

    private void Persist()
    {
        GameSettings.Apply(_settings);
        ClientSettings.CommitSettings(_settings);
        _shell.Accessibility.Save();
        LauncherPrefs.Save();
    }

    private static string Label(string key) => key.Split('.').Last()
        .Replace('-', ' ').Replace("Scale", " Scale", StringComparison.Ordinal)
        .Replace("Volume", " Volume", StringComparison.Ordinal);
    private static string Description(string key) => key.StartsWith("launcher.", StringComparison.Ordinal)
        ? "Launcher and session preference." : key.StartsWith("accessibility.", StringComparison.Ordinal)
            ? "Shared shell accessibility preference." : "Existing game setting.";
}
