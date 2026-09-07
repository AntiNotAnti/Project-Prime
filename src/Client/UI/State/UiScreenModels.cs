using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.State;

public readonly record struct UiActionResult(bool Succeeded, string Message = "")
{
    public static UiActionResult Success(string message = "") => new(true, message);
    public static UiActionResult Failure(string message) => new(false, message);
}

public sealed record UiAccountIdentity(string DisplayName, string RankTitle, int? RankingPoints,
    bool IsGuest);

public interface IHomeScreenController
{
    UiAccountIdentity Identity { get; }
    event Action? IdentityChanged { add { } remove { } }
}

public sealed record RankedAvailability(bool Available, string Reason)
{
    public static RankedAvailability Unavailable(string reason) => new(false, reason);
}

public interface IPlayScreenController
{
    RankedAvailability Ranked { get; }
    Task<UiActionResult> QuickPlayAsync(CancellationToken cancellationToken);
    Task<UiActionResult> StartRankedAsync(CancellationToken cancellationToken);
    Task<UiActionResult> StartPracticeAsync(CancellationToken cancellationToken);
}

public enum UiServerSort { Ping, Population }
public enum UiServerGroup { All, Favorites, Recent }

public readonly record struct UiServerFilter(string Search, string? Mode, bool HideFull,
    bool HideIncompatible, int MaxPing, UiServerSort Sort, UiServerGroup Group);

public sealed record UiServerEntry(string Id, string Name, string Endpoint, string Map, string Mode,
    int Players, int MaxPlayers, int Bots, int Spectators, int Ping, string Phase,
    TimeSpan? TimeRemaining, string Ruleset, bool Verified, bool Ranked, bool Compatible,
    bool Favorite, bool Recent, bool FriendlyFire, bool Radar, string SpawnPolicy,
    bool LateJoin, bool Private, bool HasSessionState = false,
    ServerJoinDisposition JoinDisposition = ServerJoinDisposition.JoinNow, int LobbyPlayers = 0,
    int LobbyObservers = 0, int ReadyPlayers = 0, bool RankedLocked = false,
    bool TournamentLocked = false)
{
    public bool Full => HasSessionState
        ? JoinDisposition == ServerJoinDisposition.Full
        : MaxPlayers > 0 && Players >= MaxPlayers;

    public bool CanJoin => Compatible && !Full && (!HasSessionState
        || JoinDisposition is ServerJoinDisposition.JoinNow or ServerJoinDisposition.JoinLobby);

    public string JoinLabel => !Compatible ? "Closed" : HasSessionState
        ? JoinDisposition switch
        {
            ServerJoinDisposition.JoinNow => "Join now",
            ServerJoinDisposition.JoinLobby => "Join lobby",
            ServerJoinDisposition.Spectate => "Spectate",
            ServerJoinDisposition.WaitForNextMatch => "Wait for next match",
            ServerJoinDisposition.Full => "Full",
            _ => "Closed"
        }
        : Full ? "Full" : "Join now";
}

public interface IServerBrowserController
{
    Task<IReadOnlyList<UiServerEntry>> QueryAsync(CancellationToken cancellationToken);
    Task<UiActionResult> JoinAsync(UiServerEntry server, bool spectate,
        CancellationToken cancellationToken);
    void SetFavorite(UiServerEntry server, bool favorite);
    void CopyEndpoint(UiServerEntry server);
}

public static class UiServerSelection
{
    public static IReadOnlyList<UiServerEntry> Apply(IEnumerable<UiServerEntry> entries,
        UiServerFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string search = filter.Search?.Trim() ?? string.Empty;
        IEnumerable<UiServerEntry> selected = entries.Where(entry =>
            (search.Length == 0 || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Map.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Endpoint.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (filter.Mode is null || entry.Mode.Equals(filter.Mode, StringComparison.OrdinalIgnoreCase))
            && (!filter.HideFull || !entry.Full)
            && (!filter.HideIncompatible || entry.Compatible)
            && (filter.MaxPing <= 0 || entry.Ping >= 0 && entry.Ping <= filter.MaxPing)
            && (filter.Group == UiServerGroup.All
                || filter.Group == UiServerGroup.Favorites && entry.Favorite
                || filter.Group == UiServerGroup.Recent && entry.Recent));

        return (filter.Sort == UiServerSort.Population
            ? selected.OrderByDescending(entry => entry.Players).ThenBy(entry => Ping(entry))
            : selected.OrderBy(entry => Ping(entry)).ThenByDescending(entry => entry.Players))
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int Ping(UiServerEntry entry) => entry.Ping < 0 ? int.MaxValue : entry.Ping;
}

public sealed record UiMapEntry(string Id, string Name, string Mode, string RecommendedPlayers,
    bool IsCustom, string? Author);

public interface IMapCatalogController
{
    Task<IReadOnlyList<UiMapEntry>> ListAsync(CancellationToken cancellationToken);
    Task<byte[]?> LoadThumbnailAsync(string mapId, CancellationToken cancellationToken);
}

public sealed class PrivateMatchDraft
{
    public string Name { get; set; } = "Private Match";
    public string? MapId { get; set; }
    public string Mode { get; set; } = "Battle";
    public int MaxPlayers { get; set; } = 8;
    public int Bots { get; set; }
    public int BotSkill { get; set; } = 1;
    public int MaxSpectators { get; set; } = 4;
    public int TimeLimitSeconds { get; set; } = 7 * 60;
    public int ScoreGoal { get; set; } = 7;
    public int ObjectiveTimeSeconds { get; set; }
    public int StartingLives { get; set; }
    public bool FriendlyFire { get; set; }
    public bool AffinityWeapons { get; set; }
    public string RadarPolicy { get; set; } = nameof(MphRead.RadarPolicy.Classic);
    public string SpawnPolicy { get; set; } = nameof(MphRead.SpawnPolicy.Classic);
    public int DamageLevel { get; set; } = 1;
    public string OvertimePolicy { get; set; } = nameof(MphRead.OvertimePolicy.Disabled);
    public string LateJoinPolicy { get; set; } = nameof(MphRead.LateJoinPolicy.JoinImmediately);
    public string Preset { get; set; } = nameof(RulesetPreset.Classic);
    public bool Practice { get; set; }
    public bool ReadyRequired { get; set; } = true;
    public bool PublicListing { get; set; }
    public string? Password { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 48)
            errors.Add("Match name must be between 1 and 48 characters.");
        if (string.IsNullOrWhiteSpace(MapId)) errors.Add("Choose a map.");
        if (string.IsNullOrWhiteSpace(Mode)) errors.Add("Choose a game mode.");
        if (MaxPlayers is < 1 or > 8) errors.Add("Player limit must be between 1 and 8.");
        if (Bots < 0 || Bots > MaxPlayers) errors.Add("Bot count must fit within the player limit.");
        if (BotSkill is < 0 or > 2) errors.Add("Bot skill must be between 0 and 2.");
        if (MaxSpectators is < 0 or > 16) errors.Add("Spectator limit must be between 0 and 16.");
        if (TimeLimitSeconds is < 0 or > 86400) errors.Add("Time limit must be between 0 and 86400 seconds.");
        if (ScoreGoal is < 0 or > 1_000_000) errors.Add("Score goal is out of range.");
        if (ObjectiveTimeSeconds is < 0 or > 86400) errors.Add("Objective time is out of range.");
        if (StartingLives is < 0 or > 255) errors.Add("Starting lives must be between 0 and 255.");
        if (DamageLevel is < 0 or > 2) errors.Add("Damage level must be between 0 and 2.");
        bool parsedMode = Enum.TryParse(Mode, true, out MatchMode mode);
        if (!String.IsNullOrWhiteSpace(Mode) && !parsedMode) errors.Add("Choose a supported game mode.");
        if (!Enum.TryParse<RulesetPreset>(Preset, true, out RulesetPreset preset)) errors.Add("Choose a supported preset.");
        if (!Enum.TryParse<MphRead.RadarPolicy>(RadarPolicy, true, out _)) errors.Add("Choose a supported radar policy.");
        if (!Enum.TryParse<MphRead.SpawnPolicy>(SpawnPolicy, true, out _)) errors.Add("Choose a supported spawn policy.");
        if (!Enum.TryParse<MphRead.OvertimePolicy>(OvertimePolicy, true, out _)) errors.Add("Choose a supported overtime policy.");
        if (!Enum.TryParse<MphRead.LateJoinPolicy>(LateJoinPolicy, true, out _)) errors.Add("Choose a supported late-join policy.");
        if (parsedMode && preset == RulesetPreset.Duel && (mode != MatchMode.Battle || MaxPlayers != 2))
            errors.Add("Duel requires Battle mode and two players.");
        if (Password?.Length > 64) errors.Add("Password cannot exceed 64 characters.");
        return errors;
    }
}

public sealed record PrivateMatchCapabilities(bool AlwaysListed, bool SupportsPassword);

public interface IPrivateMatchController
{
    PrivateMatchCapabilities Capabilities => new(false, false);
    Task<UiActionResult> CreateAsync(PrivateMatchDraft draft,
        CancellationToken cancellationToken);
}

public enum HunterLicenseTab { Overview, Hunters, Weapons, Maps, Matches }
public sealed record HunterLicenseOverview(string DisplayName, string RankTitle, int RankingPoints,
    int? NextThreshold, string? FavoriteHunter, long Wins, long Losses, decimal WinRatio,
    decimal KillDeathRatio, TimeSpan PlayTime, string? FavoriteMap, string? FavoriteWeapon,
    string? FavoriteMode, long LongestKillStreak, long LongestWinStreak, string? WinEmblem);
public sealed record HunterLicenseStat(string Key, string Label, string Value, string? Detail = null);
public sealed record HunterLicenseMatch(Guid MatchId, DateTimeOffset EndedAt, string Map, string Mode,
    string Result, string Score, string RatingStatus);
public sealed record HunterLicenseMatchPage(ImmutableArray<HunterLicenseMatch> Matches,
    string? NextCursor);

public interface IHunterLicenseController
{
    Task<HunterLicenseOverview> LoadOverviewAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HunterLicenseStat>> LoadStatsAsync(HunterLicenseTab tab,
        CancellationToken cancellationToken);
    Task<HunterLicenseMatchPage> LoadMatchesAsync(string? cursor,
        CancellationToken cancellationToken);
}

public sealed record ReplayMetadata(string Id, string FileName, DateTimeOffset RecordedAt, string Map,
    string Mode, TimeSpan Duration, int Players, string? Result, bool Compatible,
    bool RecoveredTail);

public interface IReplayLibraryController
{
    Task<IReadOnlyList<ReplayMetadata>> LoadAsync(CancellationToken cancellationToken);
    Task<UiActionResult> PlayAsync(ReplayMetadata replay, CancellationToken cancellationToken);
    Task<UiActionResult> DeleteAsync(ReplayMetadata replay, CancellationToken cancellationToken);
    Task<UiActionResult> ImportAsync(CancellationToken cancellationToken);
    Task<UiActionResult> ExportAsync(ReplayMetadata replay, CancellationToken cancellationToken);
}

public interface IReplayDeleteConfirmation
{
    Task<bool> ConfirmDeleteAsync(ReplayMetadata replay, CancellationToken cancellationToken);
}

public enum SettingsCategory
{
    Gameplay, Controls, Video, Audio, HUD, Radar, Network, Accessibility, Account
}

public enum SettingPlatform { All, Desktop, Android }
public sealed record SettingDescriptor(string Key, string Label, string Description,
    SettingsCategory Category, object? Value, bool RestartRequired = false,
    SettingPlatform Platform = SettingPlatform.All, string? Conflict = null);

public interface ISettingsScreenController
{
    IReadOnlyList<SettingDescriptor> Read(SettingsCategory category);
    Task<UiActionResult> ApplyAsync(string key, object? value, CancellationToken cancellationToken);
    Task<UiActionResult> ResetCategoryAsync(SettingsCategory category,
        CancellationToken cancellationToken);
    Task<UiActionResult> RestoreDefaultsAsync(CancellationToken cancellationToken);
}

public enum PauseAction
{
    Resume, Settings, HunterLicense, MutePlayers, LeaveMatch, LeaveServer, Quit
}

public interface IPauseOverlayController
{
    bool IsAuthoritativeMatch { get; }
    Task<UiActionResult> InvokeAsync(PauseAction action, CancellationToken cancellationToken);
}

public sealed class UiScreenServices
{
    public IHomeScreenController Home { get; init; } = NullUiControllers.Instance;
    public IPlayScreenController Play { get; init; } = NullUiControllers.Instance;
    public IServerBrowserController Servers { get; init; } = NullUiControllers.Instance;
    public IMapCatalogController Maps { get; init; } = NullUiControllers.Instance;
    public IPrivateMatchController PrivateMatches { get; init; } = NullUiControllers.Instance;
    public IHunterLicenseController HunterLicense { get; init; } = NullUiControllers.Instance;
    public IReplayLibraryController Replays { get; init; } = NullUiControllers.Instance;
    public IReplayDeleteConfirmation ReplayConfirmation { get; init; } = NullUiControllers.Instance;
    public ISettingsScreenController Settings { get; init; } = NullUiControllers.Instance;
    public ILobbyScreenController Lobby { get; init; } = NullLobbyScreenController.Instance;
    public IPostMatchScreenController PostMatch { get; init; } = NullPostMatchScreenController.Instance;
    public IAccountScreenController Account { get; init; } = NullAccountScreenController.Instance;
    public bool IsAndroid { get; init; }
}

internal sealed class NullLobbyScreenController : ILobbyScreenController
{
    public static readonly NullLobbyScreenController Instance = new();
    public UiLobbySnapshot? Snapshot => null;
    public event Action? Changed { add { } remove { } }
    public Task<UiActionResult> RequestAsync(UiLobbyCommand command, uint expectedRevision,
        CancellationToken cancellationToken) => Task.FromResult(
            UiActionResult.Failure("Lobby controls are not connected yet."));
}

internal sealed class NullPostMatchScreenController : IPostMatchScreenController
{
    public static readonly NullPostMatchScreenController Instance = new();
    public UiPostMatchSummary? Summary => null;
    public event Action? Changed { add { } remove { } }
    public Task<UiPostMatchSummary> AwaitRatingAsync(uint matchId, CancellationToken cancellationToken)
        => Task.FromException<UiPostMatchSummary>(new InvalidOperationException("Rating is unavailable."));
    public Task<UiActionResult> InvokeAsync(PostMatchAction action, CancellationToken cancellationToken)
        => Task.FromResult(UiActionResult.Failure("Post-match controls are not connected yet."));
}

internal sealed class NullAccountScreenController : IAccountScreenController
{
    public static readonly NullAccountScreenController Instance = new();
    public UiAccountSnapshot Snapshot => new(UiAccountPhase.Guest, "Guest", string.Empty, false, false);
    public Task<UiActionResult> RestoreAsync(CancellationToken cancellationToken)
        => Task.FromResult(UiActionResult.Success("No saved session was found."));
    public Task<UiActionResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
        => Task.FromResult(UiActionResult.Failure("Account service is not configured."));
    public Task<UiActionResult> SignOutAsync(CancellationToken cancellationToken)
        => Task.FromResult(UiActionResult.Success());
}

internal sealed class NullUiControllers : IHomeScreenController, IPlayScreenController,
    IServerBrowserController, IMapCatalogController, IPrivateMatchController,
    IHunterLicenseController, IReplayLibraryController, IReplayDeleteConfirmation,
    ISettingsScreenController
{
    public static readonly NullUiControllers Instance = new();
    public UiAccountIdentity Identity { get; } = new("Guest", "Create Hunter License", null, true);
    public RankedAvailability Ranked { get; } = RankedAvailability.Unavailable(
        "Ranked requires a verified account, active rating policy, and secure eligible server.");

    public Task<UiActionResult> QuickPlayAsync(CancellationToken c) => Unavailable();
    public Task<UiActionResult> StartRankedAsync(CancellationToken c) => Unavailable();
    public Task<UiActionResult> StartPracticeAsync(CancellationToken c) => Unavailable();
    public Task<IReadOnlyList<UiServerEntry>> QueryAsync(CancellationToken c)
        => Task.FromResult<IReadOnlyList<UiServerEntry>>([]);
    public Task<UiActionResult> JoinAsync(UiServerEntry s, bool spectate, CancellationToken c) => Unavailable();
    public void SetFavorite(UiServerEntry server, bool favorite) { }
    public void CopyEndpoint(UiServerEntry server) { }
    public Task<IReadOnlyList<UiMapEntry>> ListAsync(CancellationToken c)
        => Task.FromResult<IReadOnlyList<UiMapEntry>>([]);
    public Task<byte[]?> LoadThumbnailAsync(string mapId, CancellationToken c)
        => Task.FromResult<byte[]?>(null);
    public Task<UiActionResult> CreateAsync(PrivateMatchDraft draft, CancellationToken c) => Unavailable();
    public Task<HunterLicenseOverview> LoadOverviewAsync(CancellationToken c)
        => Task.FromException<HunterLicenseOverview>(new System.Net.Http.HttpRequestException());
    public Task<IReadOnlyList<HunterLicenseStat>> LoadStatsAsync(HunterLicenseTab tab, CancellationToken c)
        => Task.FromResult<IReadOnlyList<HunterLicenseStat>>([]);
    public Task<HunterLicenseMatchPage> LoadMatchesAsync(string? cursor, CancellationToken c)
        => Task.FromResult(new HunterLicenseMatchPage([], null));
    public Task<IReadOnlyList<ReplayMetadata>> LoadAsync(CancellationToken c)
        => Task.FromResult<IReadOnlyList<ReplayMetadata>>([]);
    public Task<UiActionResult> PlayAsync(ReplayMetadata replay, CancellationToken c) => Unavailable();
    public Task<UiActionResult> DeleteAsync(ReplayMetadata replay, CancellationToken c) => Unavailable();
    public Task<UiActionResult> ImportAsync(CancellationToken c) => Unavailable();
    public Task<UiActionResult> ExportAsync(ReplayMetadata replay, CancellationToken c) => Unavailable();
    public Task<bool> ConfirmDeleteAsync(ReplayMetadata replay, CancellationToken c) => Task.FromResult(false);
    public IReadOnlyList<SettingDescriptor> Read(SettingsCategory category) => [];
    public Task<UiActionResult> ApplyAsync(string key, object? value, CancellationToken c) => Unavailable();
    public Task<UiActionResult> ResetCategoryAsync(SettingsCategory category, CancellationToken c) => Unavailable();
    public Task<UiActionResult> RestoreDefaultsAsync(CancellationToken c) => Unavailable();
    private static Task<UiActionResult> Unavailable()
        => Task.FromResult(UiActionResult.Failure("This action is not connected yet."));
}
