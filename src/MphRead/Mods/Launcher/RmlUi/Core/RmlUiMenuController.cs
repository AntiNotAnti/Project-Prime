using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>One directory candidate. A directory hint is never an admitted network session.</summary>
public readonly record struct RmlUiServerCandidate(string Name, string Endpoint,
    int Players, int Capacity, int Ping, bool Compatible);
public readonly record struct RmlUiDirectory(bool Answered, string Message,
    RmlUiServerCandidate[] Servers);
public sealed record RmlUiSocialHomeState(bool Available, bool Connected,
    string[] Friends, int IncomingRequests)
{
    public bool PresenceVerified { get; init; }
    public int SharingPresence { get; init; }
    public string[] FriendActivities { get; init; } = Array.Empty<string>();
    public bool[] FriendJoinable { get; init; } = Array.Empty<bool>();
    public bool InvitationsVerified { get; init; }
    public int IncomingInvitations { get; init; }

    public static RmlUiSocialHomeState Project(NativeSocialDirectory? friends,
        NativePresenceDirectory? presence, int incomingRequests, DateTimeOffset now)
    {
        if (friends == null || !NativeSocialFriendsController.ValidPrimeId(friends.SelfPrimeId)
            || friends.People == null || friends.People.Length > 1024
            || friends.People.Any(p => p == null || !NativeSocialFriendsController.ValidPrimeId(p.PrimeId)
                || String.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Length > 128))
            return new(true, false, Array.Empty<string>(), 0);
        NativePresenceDirectory? directory = NativePresenceController.ValidDirectory(presence)
            && presence!.SelfPrimeId == friends.SelfPrimeId
            && ReferenceEquals(presence.AccountWitness, friends.AccountWitness) ? presence : null;
        var activity = directory?.People.Where(p => p.LastSeen <= now
                && p.LastSeen.AddSeconds(45) > now)
            .ToDictionary(p => p.PrimeId, StringComparer.OrdinalIgnoreCase);
        var rows = friends.People.Where(p => p.Relation == NativeSocialRelation.Friend
                && NativeSocialFriendsController.ValidPrimeId(p.PrimeId))
            .DistinctBy(p => p.PrimeId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => activity?.ContainsKey(p.PrimeId) == true)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(100).ToArray();
        return new(true, true, rows.Select(p => p.DisplayName).ToArray(), Math.Max(0, incomingRequests))
        {
            PresenceVerified = directory != null,
            SharingPresence = rows.Count(p => activity?.ContainsKey(p.PrimeId) == true),
            FriendJoinable = rows.Select(p => activity?.TryGetValue(p.PrimeId, out var shared) == true && shared.Joinable).ToArray(),
            FriendActivities = rows.Select(p => activity?.TryGetValue(p.PrimeId, out var shared) == true
                ? shared.Activity switch
                {
                    "in_match" => "IN MATCH",
                    "spectating" => "SPECTATING",
                    "lobby" => "IN LOBBY // OPEN SOCIAL",
                    "menu" => "IN MENUS",
                    _ => "ONLINE"
                }
                : directory == null ? "PRESENCE UNAVAILABLE" : "PRESENCE NOT SHARED").ToArray()
        };
    }
}
public readonly record struct RmlUiJoinResult(bool Joined, LaunchPlan Plan, string Error);
public readonly record struct RmlUiLobbyPlayer(string Name, string Hunter, bool Ready,
    bool Local, bool Bot);
/// <summary>Verified lobby identity snapshot. Never fabricate a locally accepted hunter.</summary>
public readonly record struct RmlUiHunterDraft(
    ushort MatchId, ulong AuthorityEpoch, Hunter Hunter, byte Suit,
    Hunter[] Allowed, bool CommandPending);

public readonly record struct RmlUiLobbyView(
    bool Active, bool Starting, bool Owner, bool RequireReady, bool LocalReady,
    bool CanReady, bool CanStart, string Name, string Map, string Mode, string Format,
    string Status, int MaxPlayers, RmlUiLobbyPlayer[] Players);

/// <summary>
/// Presentation-neutral seam. Only the established launcher/session services implement
/// it; the RmlUi view must not own a second lobby network pump or game simulation.
/// </summary>
public interface IRmlUiMenuServices
{
    HubSnapshot Hub();
    string BuildVersion();
    RmlUiSocialHomeState SocialHome() => new(false, false, Array.Empty<string>(), 0);
    NativePartyView PartyHome() => new(false, false, "PARTY NOT CONNECTED", 0, null);
    RmlUiLobbyView Lobby();
    NewsDispatch? FeaturedNews() => null;
    Task<RmlUiDirectory> Browse(CancellationToken token);
    Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token);
    Task<RmlUiJoinResult> JoinRandom(string endpoint, string playerName, CancellationToken token)
        => Join(endpoint, playerName, token);
    bool CanAcceptRandomJoin(LaunchPlan plan) => true;
    void NoteRandomJoined(LaunchPlan plan) { }
    void DiscardRandomJoin(LaunchPlan plan) { }
    void CancelRandomJoin() { }

    Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token);
    Task<RmlUiJoinResult> JoinParty(NativePartyJoinTarget target, string playerName, CancellationToken token)
        => Task.FromResult(new RmlUiJoinResult(false, default, "PARTY TRAVEL IS UNAVAILABLE ON THIS PLATFORM"));
    bool CanAcceptPartyJoin(NativePartyJoinTarget target, LaunchPlan plan) => false;
    void NotePartyJoined(NativePartyJoinTarget target) { }
    void DiscardPartyJoin(NativePartyJoinTarget target, LaunchPlan plan) { }
    void CancelPartyJoin() { }
    Task<RmlUiJoinResult> JoinSocial(NativeSocialJoinTarget target, string playerName, CancellationToken token)
        => Task.FromResult(new RmlUiJoinResult(false, default, "SOCIAL LOBBY JOIN IS UNAVAILABLE"));
    bool CanAcceptSocialJoin(NativeSocialJoinTarget target, LaunchPlan plan) => false;
    void NoteSocialJoined(NativeSocialJoinTarget target) { }
    void DiscardSocialJoin(NativeSocialJoinTarget target, LaunchPlan plan) { }
    void CancelSocialJoin() { }
    NativePartyQueueSnapshot CapturePartyQueue() => NativePartyQueueSnapshot.Empty;
    bool CommandPartyQueue(NativePartyQueueSnapshot expected, NativePartyQueueCommand command) => false;
    Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token);
    bool CanAcceptCreatedLobby(LaunchPlan plan) => false;
    void NoteCreatedLobby(LaunchPlan plan) { }
    void DiscardCreatedLobby(LaunchPlan plan) { }
    void CancelCreatedLobby() { }
    bool AcceptJoin(LaunchPlan plan);
    bool OpenCreateLobby();
    bool ConfigureGameFiles() => false;
    int LobbyPlayerCapacity(string roomKey) => 8;
    bool NavigateTo(RmlUiRouteArgument destination);
    bool OpenStudio();
    bool SetReady();
    bool StartMatch();
    bool LeaveLobby();
    bool NextHunter();
    bool NextSuit();
    RmlUiHunterDraft? ReadHunterDraft() => null;
    bool ApplyHunterDraft(Hunter hunter, byte suit, ushort matchId, ulong authorityEpoch) => false;
    RmlUiAdminSnapshot? ReadAdmin() => null;
    bool SubmitAdmin(RmlUiAdminRequest request) => false;
    RmlUiLobbyChrome ReadLobbyChrome() => RmlUiLobbyChrome.Empty;
    bool SendLobbyChat(string message) => false;
    bool CycleLobbyTeam(int presentationIndex) => false;
    Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
        => Task.FromResult("Offline Battle is unavailable on this platform.");
    bool LaunchOffline(RmlUiOfflineRequest request) => false;
    RmlUiOfflineRules? ReadOfflineRules() => null;
    bool SaveOfflineRules(RmlUiOfflineRules expected, RmlUiOfflineRules next, out string error)
    { error = "LOCAL RULES ARE UNAVAILABLE ON THIS PLATFORM"; return false; }
    bool OfflineRuleSavesAreAsync => false;
    Task<RmlUiOfflineRulesSaveResult> SaveOfflineRulesAsync(RmlUiOfflineRulesChange change, CancellationToken token)
        => Task.FromResult(new RmlUiOfflineRulesSaveResult(change, false, "LOCAL RULES OWNER UNAVAILABLE", null));
    AimTrainerDefinition TrainingDefaults() => AimTrainerDefinition.Default;
    bool LaunchTraining(AimTrainerDefinition drill, Hunter hunter, int suit) => false;
    RmlUiAdventureSlot[] ReadAdventureSlots() => Array.Empty<RmlUiAdventureSlot>();
    bool CanManageAdventureSaves => false;
    bool ManageAdventureSave(RmlUiAdventureSaveChange change, out string status)
    { status = "SAVE MANAGEMENT OWNER UNAVAILABLE"; return false; }
    bool AdventureSaveChangesAreAsync => false;
    Task<RmlUiAdventureSaveResult> ManageAdventureSaveAsync(RmlUiAdventureSaveChange change, CancellationToken token)
        => Task.FromResult(new RmlUiAdventureSaveResult(change, false, "SAVE MANAGEMENT OWNER UNAVAILABLE"));
    bool LaunchAdventure(RmlUiAdventureSlot slot, bool newGame, Hunter hunter) => false;
    bool CanOpenCampaignShip(byte slot) => false;
    bool OpenCampaignShip(byte slot, Hunter hunter) => false;
    Task<RmlUiReplayEntry[]> ReadTheatreAsync(CancellationToken token)
        => Task.FromResult(Array.Empty<RmlUiReplayEntry>());
    Task<string> ValidateReplayAsync(RmlUiReplayEntry entry, CancellationToken token)
        => Task.FromResult("Replay verification is unavailable.");
    bool LaunchReplay(RmlUiReplayEntry entry) => false;
    bool LaunchReplay(RmlUiReplayEntry entry, bool openEditor)
        => !openEditor && LaunchReplay(entry);
    bool ReplayEditingAllowed => true;
    bool OpenStudioReplay(RmlUiReplayEntry entry, out string error)
    { error = "PRIME STUDIO IS UNAVAILABLE"; return false; }
    bool ToggleReplayFavorite(RmlUiReplayEntry entry) => false;
    Task<RmlUiReplayWorkResult> WorkReplayAsync(RmlUiReplayWorkRequest request, CancellationToken token)
        => Task.FromResult(new RmlUiReplayWorkResult(false, "REPLAY OPERATION UNAVAILABLE", Array.Empty<RmlUiReplayEntry>()));
    bool RevealReplayFolder() => false;
    bool OpenLobbyDetails();
    RmlUiRuleDraft? ReadRules();
    IReadOnlyList<string> BuiltInArenaKeys();
    IReadOnlyList<string> OfflineArenaKeys() => BuiltInArenaKeys();
    // Default for other frontends and offline fixtures; only a verified installed
    // package catalog should override this with mutable community choices.
    IReadOnlyList<string> InstalledCommunityArenaKeys() => Array.Empty<string>();
    Task<IReadOnlyList<RmlUiArenaChoice>> ReadMapPickerCatalogAsync(CancellationToken token)
        => Task.FromResult<IReadOnlyList<RmlUiArenaChoice>>(InstalledCommunityArenaKeys()
            .Select(room => new RmlUiArenaChoice(room, RmlUiArenaCategory.Community)).ToArray());
    RmlUiArenaPreview[] ReadMapPickerPreviews(IReadOnlyList<string> rooms) => Array.Empty<RmlUiArenaPreview>();
    bool SubmitRules(RmlUiRuleDraft draft, out string error);
    void Quit();
}

public sealed record RmlUiMenuSnapshot(long Revision,
    IReadOnlyDictionary<string, RmlUiBindingValue> Bindings);

/// <summary>
/// Owner-thread Home/Play/Lobby coordinator. Background discovery/admission returns
/// immutable results through an epoch-fenced queue. Lobby data is only read from the
/// authoritative service, never created or locally acknowledged by this model.
/// </summary>
public sealed class RmlUiMenuController : IDisposable
{
    private enum Job { Browse, Quick, Join, Create, Offline, TheatreRead, TheatreWatch, TheatreWork, AdventureSave, OfflineRulesSave }
    private readonly record struct Completion(long Epoch, Job Kind, object? Data, string? Error);
    private sealed record PartyJoinCompletion(NativePartyJoinTarget Target, RmlUiJoinResult Result);
    private sealed record SocialJoinCompletion(NativeSocialJoinTarget Target, RmlUiJoinResult Result,
        Func<bool> Alive, Action<bool, string> Completed);
    private readonly IRmlUiMenuServices _services;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Completion> _completed = new();
    private readonly object _completionGate = new();
    private readonly RmlUiServerCandidate[] _rows = new RmlUiServerCandidate[8];
    private CancellationTokenSource? _work;
    private RmlUiRuleEditor? _rules;
    private RmlUiLobbyAdminController? _admin;
    private RmlUiOfflineDraft? _offline;
    private RmlUiOfflineRuleEditor? _offlineRules;
    private RmlUiReplayEntry? _deleteReplay;
    private RmlUiOfflineRequest? _offlineInFlight;
    private bool _offlineLaunched;
    private RmlUiTrainingDraft? _training;
    private RmlUiAdventureDraft? _adventure;
    private RmlUiAdventureSaveChange? _adventureSaveInFlight;
    private RmlUiOfflineRulesChange? _offlineRulesSaveInFlight;
    private RmlUiTheatreIndex? _theatre;
    private RmlUiReplayEntry? _watchInFlight;
    private bool _watchEditorInFlight;
    private bool _trainingOptionsOpen, _trainingRoute, _trainingLaunched;
    private RmlUiMapPickerModel? _mapPicker;
    private Task<IReadOnlyList<RmlUiArenaChoice>>? _mapCatalogWork;
    private CancellationTokenSource? _mapCatalogCancel;
    private string _mapCatalogStatus = "";
    private RmlUiHunterDraft? _hunterDraft;
    private Hunter _hunterChoice;
    private byte _hunterSuit;
    private string _hunterStatus = "";
    private bool _mapOpenedRules;
    public bool MapPickerOpen => _mapPicker != null;
    public bool HunterOpen => _hunterDraft.HasValue;
    internal int HunterDraftSuit => _hunterSuit;
    public bool AdminOpen => _admin is { Closed: false };
    public bool OfflineOpen => _offline != null;
    public bool TrainingOptionsOpen => _trainingOptionsOpen;
    public bool AdventureOpen => _adventure != null;
    public bool TheatreOpen => _theatre != null;
    private bool _rulesSubmitted;
    private string _rulesStatus = "VIEWING SERVER RULES";
    private long _joiningEpoch;
    private long _epoch, _revision;
    private long _nextPoll;
    private bool _disposed, _busy, _joining, _creating, _browsing, _createOpen, _createDedicated, _dirty = true;
    private bool _randomSearch;
    private long _randomStarted;
    private string _randomPlayerName = "";
    private readonly Func<long> _searchClock;
    private readonly Func<CancellationToken, Task> _searchDelay;
    private bool _joiningParty;
    private bool _joiningSocial;
    private NativePartyQueueSnapshot _partyQueue = NativePartyQueueSnapshot.Empty;
    private string _createMap = "";
    private NativeCommunityHostSelection? _createCommunity;
    private int _createMaxPlayers = 8;
    private GameMode _createMode = GameMode.Battle;
    private MatchFormat _createFormat = MatchFormat.FreeForAll;
    private string _status = "READY TO DEPLOY";
    private int _rowCount;
    private RmlUiMenuSnapshot _snapshot =
        new(0, new ReadOnlyDictionary<string, RmlUiBindingValue>(
            new Dictionary<string, RmlUiBindingValue>()));

    public RmlUiMenuController(IRmlUiMenuServices services,
        Func<CancellationToken, Task>? searchDelay = null, Func<long>? searchClock = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _searchDelay = searchDelay ?? (token => Task.Delay(2000, token));
        _searchClock = searchClock ?? (() => Environment.TickCount64);
    }
    public RmlUiMenuSnapshot Snapshot => _snapshot;
    public bool Busy => _busy;
    public bool Joining => _joining;
    public bool OpenCreateLobbyForInstalled(NativeCommunityHostSelection selection)
    {
        Owner();
        if (_joining || _creating || _services.Lobby().Active || !_services.Hub().GameFilesReady)
        { Status("LEAVE THE CURRENT SESSION OR FINISH CONNECTING BEFORE HOSTING"); return false; }
        int capacity = Math.Clamp(_services.LobbyPlayerCapacity(selection.Identity.RoomKey), 2, 8);
        var probe = new LobbyCreationRequest("Project Prime lobby", "Hunter", selection.Identity.RoomKey,
            GameMode.Battle, MatchFormat.Auto, Hunter.Samus, false, selection, capacity);
        if (!probe.ValidateForm(out string error)) { Status(error); return false; }
        Cancel();
        _theatre = null; _offline = null; _adventure = null;
        _createCommunity = selection;
        _createMap = selection.Identity.RoomKey;
        _createMaxPlayers = capacity;
        _createMode = GameMode.Battle; _createFormat = MatchFormat.Auto;
        _createDedicated = false; _createOpen = _browsing = true;
        Status("REVIEW THE INSTALLED COMMUNITY MAP, GAME TYPE AND HOST");
        return true;
    }
    public bool Creating => _creating;
    private enum SetupResume { None, Create, Offline, Training }
    private SetupResume _setupResume;
    private long _setupResumeEpoch;
    private bool RequestGameFiles(SetupResume target)
    {
        if (_joining || _creating || _services.Lobby().Active) return false;
        Cancel();
        bool accepted = _services.ConfigureGameFiles();
        if (accepted) { _setupResume = target; _setupResumeEpoch = _epoch; }
        Status(accepted ? "GAME FILE SETUP REQUIRED // FINISH SETUP TO CONTINUE" : "NATIVE GAME FILE SETUP UNAVAILABLE");
        return accepted;
    }
    public void GameFilesSetupClosed(bool installed)
    {
        Owner();
        var target = _setupResume;
        long epoch = _setupResumeEpoch;
        _setupResume = SetupResume.None;
        if (target == SetupResume.None || !installed || epoch != _epoch || _services.Lobby().Active
            || _joining || _creating || !_services.Hub().GameFilesReady) return;
        if (target == SetupResume.Create) Dispatch(RmlUiIntentKind.PlayCreateOpen);
        else OpenOffline(target == SetupResume.Training);
    }
    public (string Time, string Goal)? RuleInputs =>
        _rules is { } editor ? (editor.TimeInput(), editor.GoalInput()) : null;
    public RmlUiOfflineRules? OfflineRuleInputs => _offlineRules?.Draft;
    public RmlUiReplayInputs TheatreInputs => _theatre?.Selected is { } entry
        ? new(entry.Title, entry.Tags, entry.Collections) : new();

    /// <summary>Shell calls this after releasing playback and its scene, before restoring the launcher.</summary>
    public bool ReplayPlaybackClosed()
    {
        if (_disposed) return false;
        Owner();
        if (_busy || _theatre is not { Launched: true } library) return false;
        library.MarkReturned();
        Status("REPLAY CLOSED // SELECT A RECORDING TO WATCH OR EDIT");
        return true;
    }

    public bool Dispatch(RmlUiIntentKind kind, int argument = 0,
        string address = "", string playerName = "", string ruleTime = "", string ruleGoal = "",
        string lobbyName = "", string adminChat = "", string arenaSearch = "", string theatreSearch = "",
        string offlinePoints = "", string offlineLimit = "", string offlineGoal = "",
        RmlUiReplayInputs theatreInputs = default)
    {
        Owner();
        if (!RmlUiIntentRegistry.IsValid(kind, argument)) return false;
        if ((_randomSearch || _joining || _creating) && kind is
            (RmlUiIntentKind.PlayQuick or RmlUiIntentKind.PlayBrowse or RmlUiIntentKind.PlayServer
            or RmlUiIntentKind.PlayJoin or RmlUiIntentKind.PlayCreateOpen or RmlUiIntentKind.PlayCreate)) return false;
        if (_adventureSaveInFlight.HasValue && kind is not (RmlUiIntentKind.AdventureCancelSaveChange
            or RmlUiIntentKind.AdventureRefresh or RmlUiIntentKind.Navigate or RmlUiIntentKind.Quit)) return false;
        if (_adventureSaveInFlight.HasValue && kind is RmlUiIntentKind.Navigate or RmlUiIntentKind.AdventureRefresh)
            CancelAdventureSave();
        if (_offlineRules != null && (_offline == null || _services.Lobby().Active))
        { if (_offlineRulesSaveInFlight.HasValue) Cancel(); _offlineRules = null; Dirty(); }
        if (_offlineRulesSaveInFlight.HasValue && kind is not
            (RmlUiIntentKind.OfflineCancelRules or RmlUiIntentKind.Quit)) return false;
        if (_offlineRules != null && kind is not (RmlUiIntentKind.OfflineRuleToggle
            or RmlUiIntentKind.OfflineDamage or RmlUiIntentKind.OfflineApplyRules
            or RmlUiIntentKind.OfflineCancelRules or RmlUiIntentKind.Quit)) return false;
        if (_joining && kind is not (RmlUiIntentKind.PlayCancel or RmlUiIntentKind.PlayQueueAction or RmlUiIntentKind.Quit))
        {
            Status("Connection in progress. Wait for admission or cancel.");
            return false;
        }
        switch (kind)
        {
            case RmlUiIntentKind.PlayQueueAction:
                return CommandPartyQueue(argument);
            case RmlUiIntentKind.StagePreview:
            case RmlUiIntentKind.HomeDrawerOpen:
            case RmlUiIntentKind.HomeDrawerClose:
                return true;
            case RmlUiIntentKind.StageSelect:
                if (argument is 2 or 3)
                {
                    Cancel();
                    _browsing = false;
                }
                Dirty();
                return true;
            case RmlUiIntentKind.PlayQuick:
                if (_services.Lobby().Active) return false;
                if (!_services.Hub().GameFilesReady)
                { Status("CONFIGURE GAME FILES BEFORE SEARCHING FOR A MATCH"); return false; }
                _createOpen = false;
                _browsing = true;
                StartRandomSearch(playerName, false);
                return true;
            case RmlUiIntentKind.PlayBrowse:
                _createOpen = false;
                _createCommunity = null;
                _browsing = true;
                Begin(Job.Browse, "CONTACTING THE SERVER DIRECTORY",
                    async token => await _services.Browse(token).ConfigureAwait(false));
                return true;
            case RmlUiIntentKind.PlayServer:
                if (argument >= _rowCount) { Status("That server is no longer listed."); return false; }
                return Connect(_rows[argument].Endpoint, playerName);
            case RmlUiIntentKind.PlayJoin:
                return Connect(address, playerName);
            case RmlUiIntentKind.PlayCancel:
                bool wasCreating = _creating;
                bool wasSearching = _randomSearch;
                Cancel();
                _browsing = wasSearching;
                _createOpen = false;
                Status(wasCreating ? "LOBBY JOIN CANCELLED // A COMMITTED DEDICATED ALLOCATION MAY FINISH"
                    : wasSearching ? "RANDOM MATCH SEARCH CANCELLED" : "READY TO DEPLOY");
                return true;
            case RmlUiIntentKind.CampaignShipAction:
                return argument == 10 && _adventure is { Confirming: false, Launched: false } adventure
                    && adventure.CanResume && _services.CanOpenCampaignShip(adventure.SelectedSlot)
                    && _services.OpenCampaignShip(adventure.SelectedSlot, adventure.Hunter);
            case RmlUiIntentKind.PlayCreateOpen:
                if (!_services.Hub().GameFilesReady)
                    return RequestGameFiles(SetupResume.Create);
                string[] maps = _services.BuiltInArenaKeys().ToArray();
                if (maps.Length == 0)
                {
                    _createOpen = false; _browsing = true;
                    Status("NO MPH ARENAS AVAILABLE // SET UP GAME FILES OR HOST AN INSTALLED COMMUNITY MAP");
                    return false;
                }
                _createMap = LauncherPrefs.PreferredMap(maps);
                _createCommunity = null; _createMaxPlayers = 8;
                _createOpen = true;
                _browsing = true;
                Status("CHOOSE A MAP, GAME TYPE AND HOST");
                return true;
            case RmlUiIntentKind.PlayNextMap:
                string[] available = _services.BuiltInArenaKeys().ToArray();
                if (available.Length == 0) { Status("NO BUILT-IN ARENAS AVAILABLE"); return false; }
                int at = Array.IndexOf(available, _createMap);
                _createMap = available[(at + 1) % available.Length];
                LauncherPrefs.RememberMap(_createMap);
                _createCommunity = null; _createMaxPlayers = 8;
                Dirty();
                return true;
            case RmlUiIntentKind.PlayNextMode:
                int currentType = MatchTypeCatalog.BaseModeIndex(_createMode);
                MatchTypeDefinition nextType = MatchTypeCatalog.GameTypes[
                    (currentType + 1) % MatchTypeCatalog.GameTypes.Length];
                _createFormat = MatchTypeCatalog.NormalizeFormat(nextType, _createFormat);
                _createMode = nextType.Resolve(_createFormat);
                Dirty();
                return true;
            case RmlUiIntentKind.PlayToggleHost:
                if (OperatingSystem.IsAndroid())
                {
                    Status("ANDROID REQUIRES A REMOTE HOST");
                    return false;
                }
                _createDedicated = !_createDedicated;
                Dirty();
                return true;
            case RmlUiIntentKind.PlayCreate:
                if (!_createOpen || _creating || _joining) return false;
                HubSnapshot hub = _services.Hub();
                var request = new LobbyCreationRequest(
                    String.IsNullOrWhiteSpace(lobbyName) ? "Project Prime lobby" : lobbyName.Trim(),
                    String.IsNullOrWhiteSpace(playerName) ? hub.PlayerName : playerName.Trim(),
                    _createMap, _createMode, _createFormat, hub.DisplayHunter, _createDedicated,
                    _createCommunity, _createMaxPlayers);
                if (!request.ValidateForm(out string failure))
                {
                    Status(failure);
                    return false;
                }
                Begin(Job.Create, "ALLOCATING AUTHORITATIVE LOBBY",
                    async token => await _services.CreateLobby(request, token).ConfigureAwait(false));
                _creating = true;
                _joining = true;
                _joiningEpoch = _epoch;
                return true;
            case RmlUiIntentKind.Navigate:
                if (argument == (int)RmlUiRouteArgument.Theatre)
                    return OpenTheatre();
                if (argument == (int)RmlUiRouteArgument.Home && _theatre != null)
                {
                    _theatre = null;
                    Cancel();
                    Dirty();
                    return true;
                }
                if (argument == (int)RmlUiRouteArgument.Adventure)
                {
                    _theatre = null;
                    return OpenAdventure();
                }
                if (argument == (int)RmlUiRouteArgument.Offline)
                {
                    _theatre = null;
                    _adventure = null;
                    return OpenOffline(training: false);
                }
                if (argument == (int)RmlUiRouteArgument.Training)
                {
                    _theatre = null;
                    _adventure = null;
                    return OpenOffline(training: true);
                }
                if (argument == (int)RmlUiRouteArgument.Home && _adventure != null)
                {
                    _adventure = null;
                    Cancel();
                    Dirty();
                    return true;
                }
                if (argument == (int)RmlUiRouteArgument.Home && _offline != null)
                {
                    _offline = null;
                    _offlineLaunched = false;
                    _training = null;
                    _trainingOptionsOpen = false;
                    _trainingLaunched = false;
                    Cancel();
                    Dirty();
                    return true;
                }
                if (argument == (int)RmlUiRouteArgument.Play)
                {
                    _theatre = null;
                    _adventure = null;
                    _offline = null;
                    _training = null;
                    _trainingOptionsOpen = false;
                    _browsing = true;
                    Dirty();
                    return true;
                }
                if (_services.NavigateTo((RmlUiRouteArgument)argument))
                {
                    _offline = null;
                    _offlineLaunched = false;
                    _training = null;
                    _trainingOptionsOpen = false;
                    _adventure = null;
                    _theatre = null;
                    Cancel();
                    _browsing = false;
                    Dirty();
                    return true;
                }
                Status("Finish the current activity before navigating.");
                return false;
            case RmlUiIntentKind.OpenStudio:
                return _services.OpenStudio();
            case RmlUiIntentKind.TheatreEntry:
                if (_theatre == null || _busy || !_theatre.Select(argument)) return false;
                _deleteReplay = null;
                Dirty(); return true;
            case RmlUiIntentKind.TheatreAction:
                return DispatchTheatreAction(argument, theatreSearch, theatreInputs);
            case RmlUiIntentKind.LobbyReady:
                if (!_services.Lobby().CanReady) return false;
                return LobbyAction(_services.SetReady, "Ready request sent to server.");
            case RmlUiIntentKind.LobbyStart:
                var start = _services.Lobby();
                if (!start.Active || !start.Owner || start.Starting || !start.CanStart) return false;
                return LobbyAction(_services.StartMatch, "Start request sent to server.");
            case RmlUiIntentKind.AdventureSelectSlot:
                if (_adventure == null || !_adventure.Select(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureCopyDestination:
                if (_adventure == null || !_adventure.SelectCopyDestination(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureCopy:
            case RmlUiIntentKind.AdventureArchive:
                if (_adventure == null || !_services.CanManageAdventureSaves
                    || !_adventure.RequestSaveChange(kind == RmlUiIntentKind.AdventureCopy ? RmlUiAdventureSaveAction.Copy : RmlUiAdventureSaveAction.Archive)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureCancelSaveChange:
                if (_adventureSaveInFlight.HasValue) { CancelAdventureSave(); return true; }
                if (_adventure?.PendingSaveChange == null || !_adventure.CancelConfirmation()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureConfirmCopy:
            case RmlUiIntentKind.AdventureConfirmArchive:
                if (_adventure?.ConfirmSaveChange(kind == RmlUiIntentKind.AdventureConfirmCopy
                    ? RmlUiAdventureSaveAction.Copy : RmlUiAdventureSaveAction.Archive) is not { } change) return false;
                if (_services.CanManageAdventureSaves && _services.AdventureSaveChangesAreAsync)
                {
                    Begin(Job.AdventureSave, "SAVE CHANGE PENDING // WAITING FOR EXISTING OWNER VERIFICATION",
                        async token => await _services.ManageAdventureSaveAsync(change, token).ConfigureAwait(false));
                    _adventureSaveInFlight = change;
                    _adventure.SaveChangePending(); Dirty();
                    return true; // Request accepted; only exact completion may report its result.
                }
                string saveStatus = "SAVE CHANGE REFUSED // ACTIVE OR PENDING SESSION; REFRESH SAVES";
                bool saved = _services.CanManageAdventureSaves && _services.ManageAdventureSave(change, out saveStatus);
                _adventure.SaveChangeFinished(_services.ReadAdventureSlots(), saveStatus);
                Dirty(); return saved;
            case RmlUiIntentKind.AdventureNextHunter:
                if (_adventure == null || !_adventure.NextHunter()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureRefresh:
                if (_adventure == null || !_adventure.Refresh(_services.ReadAdventureSlots()))
                    return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureContinue:
                return StartAdventure(newGame: false);
            case RmlUiIntentKind.AdventureNewRun:
                if (_adventure == null || !_services.Hub().GameFilesReady) return false;
                switch (_adventure.RequestNew())
                {
                    case RmlUiAdventureNewRun.Refused: return false;
                    case RmlUiAdventureNewRun.NeedsConfirmation:
                        Dirty(); return true;
                    default: return StartAdventure(newGame: true);
                }
            case RmlUiIntentKind.AdventureCancelNewRun:
                if (_adventure == null || !_adventure.CancelConfirmation()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.AdventureConfirmNewRun:
                if (_adventure?.Confirm() is not { } expected) return false;
                bool acceptedNew = _services.Hub().GameFilesReady && _services.LaunchAdventure(expected, true, _adventure.Hunter);
                if (acceptedNew) _adventure.LaunchedSuccessfully();
                else Status("SAVE FILE CHANGED OR LAUNCH REFUSED // REFRESH SAVES");
                Dirty();
                return acceptedNew;
            case RmlUiIntentKind.OfflineChoice:
                if (_offline == null || _busy || !RmlUiIntentRegistry.IsValid(kind, argument))
                    return false;
                // Choices 7-15 belong exclusively to training. Choice 16 is an
                // Offline Battle mechanics selector and must remain available there.
                if (_trainingRoute != (argument is >= 7 and <= 15)) return false;
                if (argument == 0) _offline.RefreshArenas(_services.OfflineArenaKeys());
                if (argument >= 7 && argument != 16)
                {
                    if (_training == null || (argument >= 13 && !_trainingOptionsOpen)) return false;
                    bool changed = _training.Choice(argument);
                    if (changed) Dirty();
                    return changed;
                }
                string previousMap = _offline.Map;
                bool offlineChanged = _offline.Change(argument);
                if (offlineChanged)
                {
                    if (!String.Equals(previousMap, _offline.Map, StringComparison.OrdinalIgnoreCase))
                        LauncherPrefs.RememberMap(_offline.Map);
                    Dirty();
                }
                return offlineChanged;
            case RmlUiIntentKind.OfflineOpenArena:
                if (_offline == null || _trainingRoute || _busy || _trainingOptionsOpen) return false;
                _offline.RefreshArenas(_services.OfflineArenaKeys());
                if (!_offline.OpenArena()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineCancelArena:
                if (_offline == null || !_offline.CloseArena()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineArenaSearch:
                if (_offline == null || _trainingRoute || !_offline.SearchArena(arenaSearch)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineArenaPage:
                if (_offline == null || _trainingRoute || !_offline.ArenaTurn(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineSelectArena:
                if (_offline == null || _trainingRoute || !_offline.SelectArena(argument)) return false;
                LauncherPrefs.RememberMap(_offline.Map);
                Dirty(); return true;
            case RmlUiIntentKind.OfflineLaunchMatch:
                if (_offline == null || _trainingRoute || _busy || _offlineLaunched || _offline.ArenaOpen || _trainingOptionsOpen || !_offline.Valid) return false;
                RmlUiOfflineRequest offlineRequest = _offline.Current;
                if (offlineRequest.Character != OfflineCharacterMode.Native)
                {
                    if (_services.ReadOfflineRules() is not { } practiceRules)
                    { Status("REOPEN OFFLINE SETUP TO READ CURRENT MATCH RULES"); return false; }
                    var practiceSettings = new MenuSettings();
                    practiceRules.CopyTo(practiceSettings);
                    try
                    {
                        _ = OfflineLaunch.Preview(practiceSettings, offlineRequest.RoomKey,
                            offlineRequest.Mode, offlineRequest.Hunter, offlineRequest.Suit,
                            offlineRequest.Bots, offlineRequest.BotLevel, offlineRequest.Character);
                    }
                    catch (ArgumentException ex) { Status(ex.Message); return false; }
                }
                Begin(Job.Offline, "VALIDATING LOCAL MATCH AND MAP CAPABILITIES",
                    async token => await _services.ValidateOfflineAsync(offlineRequest, token)
                        .ConfigureAwait(false));
                _offlineInFlight = offlineRequest;
                return true;
            case RmlUiIntentKind.OfflineOpenTrainingOptions:
                if (_training == null || _offline == null || !_trainingRoute || _offline.ArenaOpen || _trainingOptionsOpen) return false;
                _trainingOptionsOpen = true;
                Dirty();
                return true;
            case RmlUiIntentKind.OfflineCloseTrainingOptions:
                if (!_trainingOptionsOpen) return false;
                _trainingOptionsOpen = false;
                Dirty();
                return true;
            case RmlUiIntentKind.OfflineTrainingToggle:
                if (!_trainingRoute || !_trainingOptionsOpen || _training == null
                    || !_training.Toggle(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineLaunchTraining:
                if (_training == null || _offline == null || !_trainingRoute || _busy || _offline.ArenaOpen
                    || _trainingOptionsOpen || _trainingLaunched || _services.Lobby().Active) return false;
                if (!_services.LaunchTraining(_training.BuildForLaunch(),
                        _training.Hunter, _offline.Current.Suit))
                {
                    Status("TRAINING LAUNCH REFUSED // CHECK GAME FILES OR ACTIVE SESSION");
                    return false;
                }
                _trainingLaunched = true;
                Status("AIM LAB LAUNCH ACCEPTED");
                return true;
            case RmlUiIntentKind.OfflineOpenRules:
                if (_offline == null || _trainingRoute || _busy || _offline.ArenaOpen || _trainingOptionsOpen
                    || _services.Lobby().Active || _services.ReadOfflineRules() is not { } localRules) return false;
                _offlineRules = new(_offline.Current.Mode, localRules);
                Dirty(); return true;
            case RmlUiIntentKind.OfflineCancelRules:
                if (_offlineRules == null) return false;
                if (_offlineRulesSaveInFlight.HasValue)
                { Cancel(); Status("RULE SAVE REQUEST RETIRED // REOPEN RULES TO VERIFY; A COMMITTED WRITE IS NOT UNDONE"); }
                _offlineRules = null; Dirty(); return true;
            case RmlUiIntentKind.OfflineRuleToggle:
                if (_offlineRules == null || !_offlineRules.Toggle(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineDamage:
                if (_offlineRules == null || !_offlineRules.NextDamage()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineApplyRules:
                if (_offlineRules == null || _offline == null || _services.Lobby().Active) return false;
                if (!_offlineRules.Validate(offlinePoints, offlineLimit, offlineGoal)) { Dirty(); return false; }
                if (_services.OfflineRuleSavesAreAsync)
                {
                    var ruleChange = new RmlUiOfflineRulesChange(_offlineRules.Mode, _offlineRules.Original, _offlineRules.Draft);
                    Begin(Job.OfflineRulesSave, "LOCAL RULES SAVE PENDING",
                        async token => await _services.SaveOfflineRulesAsync(ruleChange, token).ConfigureAwait(false));
                    _offlineRulesSaveInFlight = ruleChange;
                    _offlineRules.Failed("LOCAL RULES SAVE PENDING // CANCEL REMAINS AVAILABLE");
                    Dirty(); return true;
                }
                if (!_services.SaveOfflineRules(_offlineRules.Original, _offlineRules.Draft, out string localError))
                { _offlineRules.Failed(localError); Dirty(); return false; }
                _offline.SetLowTier(_offlineRules.Draft.Enabled(8));
                _offlineRules = null;
                Status("LOCAL MATCH RULES SAVED"); return true;
            case RmlUiIntentKind.LobbyRulesOpen:
                if (_hunterDraft.HasValue || _admin is { Closed: false }) return false;
                var original = _services.ReadRules();
                if (!original.HasValue) return false;
                _rules = new(original.Value);
                _rulesStatus = _services.Lobby().Owner
                    ? "EDIT LOCALLY // APPLY TO SUBMIT TO THE SERVER"
                    : "VIEW ONLY // LOBBY OWNER CONTROLS SETTINGS";
                _rulesSubmitted = false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyRulesClose:
                CloseRules();
                return true;
            case RmlUiIntentKind.LobbyRulesToggle:
                return ChangeRules(e => e.Toggle(argument));
            case RmlUiIntentKind.LobbyRulesMap:
                return ChangeRules(e => e.NextMap(_services.BuiltInArenaKeys()));
            case RmlUiIntentKind.LobbyRulesMode:
                return ChangeRules(e => e.NextMode());
            case RmlUiIntentKind.LobbyRulesFormat:
                return ChangeRules(e => e.NextFormat());
            case RmlUiIntentKind.LobbyRulesApply:
                return ApplyRules(ruleTime, ruleGoal);
            case RmlUiIntentKind.LobbyMapOpen:
                return OpenMapPicker();
            case RmlUiIntentKind.LobbyMapCategory:
                if (_mapPicker == null || !_mapPicker.SelectCategory(argument)) return false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyMapPrevious:
                if (_mapPicker == null || !_mapPicker.PreviousPage()) return false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyMapNext:
                if (_mapPicker == null || !_mapPicker.NextPage()) return false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyMapClose:
                if (_mapPicker == null) return false;
                RetireMapPicker();
                if (_mapOpenedRules) CloseRules();
                _mapOpenedRules = false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyMapSelect:
                if (_mapPicker == null || _rules == null) return false;
                string? room = _mapPicker.Choice(argument);
                if (room == null) return false;
                if (_mapPicker.Detail(argument) is { Selectable: false }) return false;
                string mapError = "";
                bool chosen = ChangeRules(editor => editor.TrySelectMap(room, out mapError));
                if (!chosen)
                {
                    if (mapError.Length > 0) { _rulesStatus = mapError; Dirty(); }
                    return false;
                }
                // Save an intentional map choice separately from the server-managed rotation.
                LauncherPrefs.RememberMap(room);
                if (_mapOpenedRules)
                {
                    // A picker opened from the lobby is a map-only operation.
                    // Keep the authoritative limits/flags intact and submit via
                    // the existing revision-fenced UpdateMatch service.
                    if (!_rules.Validate(out mapError) || !_services.SubmitRules(_rules.Draft, out mapError))
                    {
                        _rulesStatus = String.IsNullOrEmpty(mapError)
                            ? "SERVER CANNOT ACCEPT THIS MAP RIGHT NOW." : mapError;
                        Dirty();
                        return false;
                    }
                    CloseRules();
                    Status("MAP CHANGE REQUEST SENT // WAITING FOR SERVER CONFIRMATION");
                    return true;
                }
                RetireMapPicker();
                _mapOpenedRules = false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyAdminOpen:
                return OpenAdmin();
            case RmlUiIntentKind.LobbyPlayerSelect:
                if (_admin != null) return AdminAction(kind, argument, adminChat);
                RmlUiAdminSnapshot? roster = _services.ReadAdmin();
                RmlUiAdminPlayer? selection = RmlUiLobbyDisplayOrder.Resolve(roster, argument);
                if (selection.HasValue) return OpenAdmin(selection.Value.Slot);
                if (!RmlUiLobbyDisplayOrder.CanAddBot(_services.Lobby(), roster, argument)) return false;
                bool added = _services.SubmitAdmin(new(RmlUiAdminOperation.AddBot,
                    roster!.MatchId, roster.AuthorityEpoch, roster.SessionRevision));
                if (added) Status("ADD BOT REQUEST SENT // AWAITING SERVER STATE");
                return added;
            case RmlUiIntentKind.LobbyAdminClose:
                if (_admin == null) return false;
                _admin.Close(); _admin = null; Dirty(); return true;
            case RmlUiIntentKind.LobbyAdminCancel:
            case RmlUiIntentKind.LobbyAdminConfirm:
            case RmlUiIntentKind.LobbyKick:
            case RmlUiIntentKind.LobbyTransferOwner:
            case RmlUiIntentKind.LobbyClose:
            case RmlUiIntentKind.LobbyBotAdd:
            case RmlUiIntentKind.LobbyBotConfigure:
            case RmlUiIntentKind.LobbyBotRemove:
            case RmlUiIntentKind.LobbyBotHunterNext:
            case RmlUiIntentKind.LobbyBotSuitNext:
            case RmlUiIntentKind.LobbyBotLevelNext:
            case RmlUiIntentKind.LobbyHandicapNext:
            case RmlUiIntentKind.LobbyTeamNext:
            case RmlUiIntentKind.LobbyTeamSelect:
            case RmlUiIntentKind.LobbyTeamsAuto:
            case RmlUiIntentKind.LobbyTeamLockToggle:
            case RmlUiIntentKind.LobbyMapRetry:
                return AdminAction(kind, argument, adminChat);
            case RmlUiIntentKind.LobbyChatSend:
                if (!_services.Lobby().Active || !_services.SendLobbyChat(adminChat)) return false;
                Status("CHAT REQUEST SENT // SERVER CONTROLS DELIVERY");
                return true;
            case RmlUiIntentKind.LobbySlotTeamNext:
                if (!_services.Lobby().Active || !_services.CycleLobbyTeam(argument)) return false;
                Status("TEAM CHANGE REQUEST SENT // AWAITING SERVER STATE");
                return true;
            case RmlUiIntentKind.LobbyClassic:
                return LobbyAction(_services.OpenLobbyDetails,
                    "Opened the remaining authoritative lobby controls.");
            case RmlUiIntentKind.LobbyLeave:
                if (_services.LeaveLobby())
                {
                    _hunterDraft = null;
                    _offline = null;
                    _admin?.Close(); _admin = null;
                    CloseRules();
                    Cancel();
                    _browsing = false;
                    Status("LEFT THE LOBBY");
                    return true;
                }
                return false;
            case RmlUiIntentKind.HunterOpen:
                return OpenHunterIdentity();
            case RmlUiIntentKind.HunterSelect:
                if (!_hunterDraft.HasValue || !CanPickHunter((Hunter)argument)) return false;
                _hunterChoice = (Hunter)argument;
                Dirty();
                return true;
            case RmlUiIntentKind.HunterRandom:
                if (_hunterDraft is not { } randomDraft
                    || !RmlUiLobbyHunterCards.TryRandom(randomDraft.Allowed,
                        (uint)Random.Shared.Next(), out Hunter randomHunter)) return false;
                _hunterChoice = randomHunter;
                Dirty();
                return true;
            case RmlUiIntentKind.HunterSuit:
                if (!_hunterDraft.HasValue || argument is < 0 or > 3) return false;
                _hunterSuit = (byte)argument;
                Dirty();
                return true;
            case RmlUiIntentKind.HunterApply:
                return SubmitHunterIdentity();
            case RmlUiIntentKind.HunterCancel:
                if (!_hunterDraft.HasValue) return false;
                _hunterDraft = null;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyNextHunter:
                return LobbyAction(_services.NextHunter, "Hunter choice submitted.");
            case RmlUiIntentKind.LobbyNextSuit:
                return LobbyAction(_services.NextSuit, "Suit choice submitted.");
            case RmlUiIntentKind.Quit:
                Cancel();
                _services.Quit();
                return true;
            default:
                // Deliberately no optimistic local changes to rules, administrative
                // commands, Community, Hunter License, replay or gameplay state.
                Status("This action still uses the existing launcher.");
                return false;
        }
    }

    public RmlUiMenuSnapshot Tick()
    {
        Owner();
        PollMapCatalog();
        if (_offlineRules != null && (_offline == null || _services.Lobby().Active))
        { if (_offlineRulesSaveInFlight.HasValue) Cancel(); _offlineRules = null; Dirty(); }
        while (_completed.TryDequeue(out Completion item))
        {
            // An older cancelled join must never end ownership of a newer one.
            if (item.Kind is Job.Join or Job.Create && item.Epoch == _joiningEpoch)
            {
                _joining = false;
                _creating = false;
                _joiningParty = false;
                _joiningSocial = false;
                _partyQueue = NativePartyQueueSnapshot.Empty;
            }
            if (item.Epoch != _epoch || _disposed)
            {
                DiscardPartyCompletion(item);
                continue;
            }
            _busy = false;
            _work = null;
            _activeMenuJob = null;
            if (item.Kind == Job.OfflineRulesSave)
            {
                var expected = _offlineRulesSaveInFlight;
                _offlineRulesSaveInFlight = null;
                if (_offlineRules != null && _offline != null && expected is { } change)
                {
                    var result = item.Data as RmlUiOfflineRulesSaveResult;
                    bool exact = item.Error == null && result?.Change == change;
                    bool verified = exact && result!.Success && result.Current == change.Next
                        && _services.ReadOfflineRules() == change.Next;
                    if (verified)
                    { _offline.SetLowTier(change.Next.Enabled(8)); _offlineRules = null; Status("LOCAL MATCH RULES SAVED"); }
                    else
                    {
                        string error = exact && !result!.Success && !String.IsNullOrWhiteSpace(result.Status)
                            ? result.Status : "RULE SAVE OUTCOME UNVERIFIED // REOPEN RULES TO VERIFY";
                        if (error.Length > 220) error = error[..220];
                        _offlineRules.Failed(error); Status(error);
                    }
                }
                continue;
            }
            if (item.Kind == Job.AdventureSave)
            {
                var expected = _adventureSaveInFlight;
                _adventureSaveInFlight = null;
                if (_adventure != null && expected.HasValue)
                {
                    var result = item.Data as RmlUiAdventureSaveResult;
                    var slots = _services.ReadAdventureSlots();
                    bool exact = item.Error == null && result is not null && result.Change == expected.Value;
                    bool readback = exact && result!.Success && RmlUiAdventureSaveIdentity.MatchesResult(expected.Value, slots);
                    string status = exact && (!result!.Success || readback) && !String.IsNullOrWhiteSpace(result.Status)
                        ? result.Status : "SAVE CHANGE OUTCOME UNVERIFIED // REFRESH SAVES; A COMMITTED WRITE IS NOT UNDONE";
                    _adventure.SaveChangeFinished(slots, status.Length <= 220 ? status : status[..220]);
                    Status(status);
                }
                continue;
            }
            if (item.Error != null)
            {
                if (item.Kind == Job.TheatreRead) _studioReplayPath = null;
                if (item.Kind == Job.Quick && _randomSearch) { StartRandomSearch("", true); continue; }
                Status(item.Error);
                continue;
            }
            switch (item.Kind)
            {
                case Job.TheatreWork:
                    if (_theatre != null && item.Data is RmlUiReplayWorkResult workResult)
                    {
                        if (workResult.Success) _theatre.Refresh(workResult.Entries);
                        Status(workResult.Status);
                    }
                    break;
                case Job.TheatreRead:
                    if (_theatre != null && item.Data is RmlUiReplayEntry[] entries)
                    {
                        _theatre.Refresh(entries);
                        Status("REPLAY LIBRARY LOADED // " + _theatre.TotalCount + " RECORDINGS");
                        if (_studioReplayPath is { } requested && requested.Epoch == _epoch)
                        {
                            _studioReplayPath = null;
                            if (!_theatre.SelectPath(requested.Path))
                                Status("REQUESTED REPLAY IS NOT A PLAYABLE ENTRY IN THE CURRENT LIBRARY");
                            else DispatchTheatreAction(18, "", default);
                        }
                    }
                    break;
                case Job.TheatreWatch:
                    string? replayError = (string?)item.Data;
                    RmlUiReplayEntry? checkedReplay = _watchInFlight;
                    bool checkedEditor = _watchEditorInFlight;
                    _watchInFlight = null;
                    _watchEditorInFlight = false;
                    if (!String.IsNullOrEmpty(replayError))
                        Status(replayError);
                    else if (checkedReplay is { } confirmed && _theatre is { } library
                        && library.SelectedUnchanged(confirmed) && _services.LaunchReplay(confirmed, checkedEditor))
                    {
                        library.MarkLaunched();
                        Status(checkedEditor ? "REPLAY HANDED OFF TO NATIVE EDITOR"
                            : "REPLAY HANDED OFF TO EXISTING CINEMATIC PLAYBACK");
                    }
                    else Status("REPLAY CHANGED OR GAME LAUNCH REFUSED");
                    break;
                case Job.Offline:
                    string? validation = (string?)item.Data;
                    RmlUiOfflineRequest? completedRequest = _offlineInFlight;
                    _offlineInFlight = null;
                    if (!String.IsNullOrEmpty(validation))
                        Status(validation);
                    else if (completedRequest is { } validated
                        && _offline is { } available && available.Current == validated
                        && !_offlineLaunched && _services.LaunchOffline(validated))
                    {
                        _offlineLaunched = true;
                        Status("OFFLINE MATCH SUBMITTED TO GAME LAUNCHER");
                    }
                    else
                        Status("OFFLINE MATCH LAUNCH REFUSED // CHECK CURRENT SESSION");
                    break;
                case Job.Browse:
                    var result = (RmlUiDirectory)item.Data!;
                    _rowCount = 0;
                    Array.Clear(_rows);
                    foreach (var entry in result.Servers
                        .Where(x => x.Compatible)
                        .OrderBy(x => x.Ping < 0 ? int.MaxValue : x.Ping)
                        .Take(8))
                        _rows[_rowCount++] = entry;
                    Status(result.Message);
                    break;
                case Job.Quick:
                    var randomJoin = (RmlUiJoinResult)item.Data!;
                    if (randomJoin.Joined && _services.CanAcceptRandomJoin(randomJoin.Plan) && _services.AcceptJoin(randomJoin.Plan))
                    {
                        _services.NoteRandomJoined(randomJoin.Plan);
                        _randomSearch = false;
                        _joining = false;
                        _browsing = false;
                        Status("JOINED. WAITING FOR AUTHORITATIVE LOBBY STATE.");
                    }
                    else
                    {
                        _services.DiscardRandomJoin(randomJoin.Plan);
                        StartRandomSearch("", true);
                    }
                    break;
                case Job.Join:
                case Job.Create:
                    if (item.Data is SocialJoinCompletion socialJoin)
                    {
                        ApplySocialJoin(socialJoin);
                        break;
                    }
                    var partyJoin = item.Data as PartyJoinCompletion;
                    var joined = partyJoin?.Result ?? (RmlUiJoinResult)item.Data!;
                    if (!joined.Joined)
                        Status(string.IsNullOrEmpty(joined.Error) ? "The server refused this connection." : joined.Error);
                    else if (partyJoin != null && !_services.CanAcceptPartyJoin(partyJoin.Target, joined.Plan))
                    {
                        _services.DiscardPartyJoin(partyJoin.Target, joined.Plan);
                        Status("PARTY DESTINATION OR ACCOUNT CHANGED // REFRESH PARTY");
                    }
                    else if (item.Kind == Job.Create && !_services.CanAcceptCreatedLobby(joined.Plan))
                    {
                        _services.DiscardCreatedLobby(joined.Plan);
                        Status("CREATED LOBBY CONNECTION CHANGED // RETRY");
                    }
                    else if (_services.AcceptJoin(joined.Plan))
                    {
                        _browsing = false;
                        _createOpen = false;
                        if (partyJoin != null) _services.NotePartyJoined(partyJoin.Target);
                        if (item.Kind == Job.Create) _services.NoteCreatedLobby(joined.Plan);
                        Status("JOINED. WAITING FOR AUTHORITATIVE LOBBY STATE.");
                    }
                    else
                    {
                        if (partyJoin != null) _services.DiscardPartyJoin(partyJoin.Target, joined.Plan);
                        if (item.Kind == Job.Create) _services.DiscardCreatedLobby(joined.Plan);
                        Status("Unable to complete the current client handoff.");
                    }
                    break;
            }
        }

        // The result of UpdateMatch and the authoritative SessionState may arrive
        // in different packets. Never show local rule edits as accepted until
        // the same authority advances revision AND echoes the exact payload.
        if (_hunterDraft is { } hunter)
        {
            RmlUiHunterDraft? live = _services.ReadHunterDraft();
            if (!live.HasValue || live.Value.MatchId != hunter.MatchId
                || live.Value.AuthorityEpoch != hunter.AuthorityEpoch
                || !_services.Lobby().Active)
            {
                _hunterDraft = null;
                Status("HUNTER SELECTION EXPIRED // REOPEN FROM LOBBY");
            }
            else if (live.Value.CommandPending != hunter.CommandPending
                || !live.Value.Allowed.SequenceEqual(hunter.Allowed))
            {
                _hunterDraft = live;
                Dirty();
            }
        }
        if (_rules is { } editor)
        {
            RmlUiRuleDraft? live = _services.ReadRules();
            if (!live.HasValue || live.Value.MatchId != editor.Source.MatchId
                || live.Value.AuthorityEpoch != editor.Source.AuthorityEpoch)
                CloseRules();
            else if (_rulesSubmitted && live.Value.SessionRevision != editor.Source.SessionRevision)
            {
                if (live.Value.Match == editor.Draft.Match && live.Value.Flags == editor.Draft.Flags)
                {
                    if (!String.Equals(editor.Source.Match.RoomKey, live.Value.Match.RoomKey,
                        StringComparison.OrdinalIgnoreCase))
                        LauncherPrefs.RememberMap(live.Value.Match.RoomKey);
                    CloseRules();
                    Status("RULE CHANGES CONFIRMED BY SERVER");
                }
                else
                {
                    _rulesSubmitted = false;
                    _rulesStatus = "THE SERVER CHANGED RULES. CLOSE AND REOPEN.";
                    Dirty();
                }
            }
        }

        if (_admin is { Closed: true }) { _admin = null; Dirty(); }
        var partyQueue = _joining && _joiningParty ? _services.CapturePartyQueue() : NativePartyQueueSnapshot.Empty;
        if (!ReferenceEquals(_partyQueue, partyQueue))
        {
            _partyQueue = partyQueue;
            Dirty();
        }
        long now = Stopwatch.GetTimestamp();
        if (!_dirty && now < _nextPoll) return _snapshot;
        _nextPoll = now + Stopwatch.Frequency / 8;
        _dirty = false;
        var bindings = Project(_services.Hub(), _services.BuildVersion(), _services.Lobby());
        bindings["lobby_admin_open"] = RmlUiBindingValue.FromBoolean(false);
        bindings["admin_confirmation_open"] = RmlUiBindingValue.FromBoolean(false);
        _admin?.Project(bindings);
        if (!Same(_snapshot.Bindings, bindings))
            _snapshot = new(++_revision, new ReadOnlyDictionary<string, RmlUiBindingValue>(bindings));
        return _snapshot;
    }

    private void CloseRules()
    {
        RetireMapPicker();
        _mapOpenedRules = false;
        _rules = null;
        _rulesSubmitted = false;
        _rulesStatus = "VIEWING SERVER RULES";
        Dirty();
    }

    private bool OpenHunterIdentity()
    {
        RmlUiLobbyView lobby = _services.Lobby();
        if (!lobby.Active || lobby.Starting || _mapPicker != null || _rules != null
            || _hunterDraft.HasValue || _admin is { Closed: false }) return false;
        RmlUiHunterDraft? live = _services.ReadHunterDraft();
        if (!live.HasValue || live.Value.MatchId == 0 || live.Value.AuthorityEpoch == 0
            || live.Value.Allowed == null || live.Value.Allowed.Length == 0) return false;
        _hunterDraft = live;
        _hunterChoice = live.Value.Hunter;
        _hunterSuit = live.Value.Suit;
        _hunterStatus = "SELECT A HUNTER AND SUIT // APPLY TO SEND YOUR IDENTITY";
        Dirty();
        return true;
    }

    private bool CanPickHunter(Hunter candidate) =>
        _hunterDraft is { } draft && draft.Allowed.Contains(candidate);

    private bool SubmitHunterIdentity()
    {
        if (_hunterDraft is not { } draft || !CanPickHunter(_hunterChoice)
            || _hunterSuit > 3) return false;
        RmlUiHunterDraft? current = _services.ReadHunterDraft();
        if (!current.HasValue || current.Value.CommandPending || current.Value.MatchId != draft.MatchId
            || current.Value.AuthorityEpoch != draft.AuthorityEpoch
            || !current.Value.Allowed.Contains(_hunterChoice))
        {
            _hunterStatus = "LOBBY IDENTITY CHANGED // REOPEN THE HUNTER SELECTOR";
            Dirty();
            return false;
        }
        if (!_services.ApplyHunterDraft(_hunterChoice, _hunterSuit,
                draft.MatchId, draft.AuthorityEpoch))
        {
            _hunterStatus = "THE SERVER IS NOT ACCEPTING IDENTITY CHANGES";
            Dirty();
            return false;
        }
        _hunterDraft = null;
        Status("HUNTER AND SUIT REQUEST SENT // SERVER VALIDATES");
        return true;
    }

    private bool OpenTheatre()
    {
        if (_services.Lobby().Active || _joining || _creating) return false;
        if (_theatre != null) return true;
        _offline = null; _training = null; _adventure = null;
        _deleteReplay = null;
        _theatre = new RmlUiTheatreIndex();
        _browsing = false;
        Begin(Job.TheatreRead, "SCANNING CURRENT REPLAY LIBRARY",
            async token => await _services.ReadTheatreAsync(token).ConfigureAwait(false));
        Dirty();
        return true;
    }

    private Job? _activeMenuJob;
    private (string Path, long Epoch)? _studioReplayPath;
    public void ReportStudioLaunch(bool accepted, string? error)
    {
        Owner();
        Status(accepted ? "PRIME STUDIO OPENED" : String.IsNullOrWhiteSpace(error)
            ? "PRIME STUDIO COULD NOT OPEN" : error);
    }
    public bool OpenStudioReplayPath(string logicalAbsolutePath, bool recover = false)
    {
        Owner();
        if (!_services.ReplayEditingAllowed)
        { Status("REPLAY EDITING REQUIRES PRIME STUDIO"); return false; }
        if (recover)
        { Status("REPLAY RECOVERY REQUIRES EXPLICIT REVIEW IN PRIME STUDIO"); return false; }
        if (String.IsNullOrWhiteSpace(logicalAbsolutePath) || logicalAbsolutePath.Length > 4096
            || logicalAbsolutePath.Contains('\0') || !Path.IsPathFullyQualified(logicalAbsolutePath)
            || !(Path.GetExtension(logicalAbsolutePath).Equals(".ppdemo", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(logicalAbsolutePath).Equals(".ppclip", StringComparison.OrdinalIgnoreCase)))
        { Status("OPEN AN ABSOLUTE LIBRARY .PPDEMO OR .PPCLIP PATH"); return false; }
        if (_services.Lobby().Active || _joining || _creating || _theatre?.Launched == true
            || _studioReplayPath.HasValue || _busy && _activeMenuJob != Job.TheatreRead)
        { Status("FINISH THE CURRENT ACTIVITY BEFORE OPENING THIS REPLAY"); return false; }
        string path;
        try { path = Path.GetFullPath(logicalAbsolutePath); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { Status("REPLAY LIBRARY PATH IS INVALID"); return false; }
        if (_theatre == null)
        {
            if (!OpenTheatre()) return false;
        }
        else if (!_busy)
        {
            _deleteReplay = null;
            Begin(Job.TheatreRead, "REFRESHING REPLAY LIBRARY FOR PRIME STUDIO",
                async token => await _services.ReadTheatreAsync(token).ConfigureAwait(false));
        }
        _studioReplayPath = (path, _epoch);
        return true;
    }

    private bool DispatchTheatreAction(int action, string search, RmlUiReplayInputs inputs)
    {
        if (_theatre is not { } library) return false;
        if (action == 10)
        {
            if (!_busy) return false;
            Cancel();
            Status("REPLAY OPERATION CANCELLED // REFRESH TO VERIFY FILE STATE");
            return true;
        }
        if (_busy || library.Launched) return false;
        if (action == 16)
        {
            if (!_deleteReplay.HasValue) return false;
            _deleteReplay = null; Dirty(); return true;
        }
        if (_deleteReplay.HasValue && action != 15) return false;
        bool changed = action switch
        {
            0 => library.SearchFor(search),
            23 => library.SearchFor(""),
            1 => library.NextFilter(),
            2 => library.NextSort(),
            3 => library.Turn(-1),
            4 => library.Turn(1),
            _ => false
        };
        if (changed) { _deleteReplay = null; Dirty(); return true; }

        switch (action)
        {
            case 5:
                Begin(Job.TheatreRead, "REFRESHING REPLAY LIBRARY",
                    async token => await _services.ReadTheatreAsync(token).ConfigureAwait(false));
                return true;
            case 6:
            case 18:
                if (library.Selected is not { Playable: true } selected) return false;
                if (action == 18 && !_services.ReplayEditingAllowed)
                {
                    if (!library.SelectedUnchanged(selected)) return false;
                    bool opened = _services.OpenStudioReplay(selected, out string error);
                    Status(opened ? "OPENED IN PRIME STUDIO // REPLAY LIBRARY REMAINS AVAILABLE"
                        : String.IsNullOrWhiteSpace(error) ? "PRIME STUDIO COULD NOT OPEN THIS REPLAY" : error);
                    return opened;
                }
                Begin(Job.TheatreWatch, "CHECKING REPLAY PROTOCOL AND MAP IDENTITY",
                    async token => await _services.ValidateReplayAsync(selected, token)
                        .ConfigureAwait(false));
                _watchInFlight = selected;
                _watchEditorInFlight = action == 18;
                return true;
            case 7:
                if (library.Selected is not { Playable: true } favorite
                    || !_services.ToggleReplayFavorite(favorite))
                {
                    Status("REPLAY CHANGED OR FAVORITE MARKER CANNOT BE SAVED");
                    return false;
                }
                Begin(Job.TheatreRead, "REFRESHING FAVORITE STATE",
                    async token => await _services.ReadTheatreAsync(token).ConfigureAwait(false));
                return true;
            case 8: return ReplayWork(RmlUiReplayWork.Validate, inputs);
            case 9: return _services.ReplayEditingAllowed && ReplayWork(RmlUiReplayWork.Recover, inputs);
            case 11:
                if (!_services.ReplayEditingAllowed) return false;
                if (library.Selected?.VirtualWitness != null)
                { Status("VIRTUAL CLIPS REFER TO THEIR SOURCE // OPEN REPLAY STUDIO FOR A MATERIALIZED EXPORT"); return false; }
                return ReplayWork(RmlUiReplayWork.Export, inputs);
            case 12:
                if (String.IsNullOrWhiteSpace(inputs.Name) || inputs.Name.Length > 100)
                { Status("USE A REPLAY NAME BETWEEN 1 AND 100 CHARACTERS"); return false; }
                return ReplayWork(RmlUiReplayWork.Rename, inputs);
            case 13:
                if ((inputs.Tags?.Length ?? 0) > 1024 || (inputs.Collections?.Length ?? 0) > 1024) return false;
                return ReplayWork(RmlUiReplayWork.Organize, inputs);
            case 14:
                if (library.Selected is not { } deleting) return false;
                _deleteReplay = deleting; Dirty(); return true;
            case 15:
                if (_deleteReplay is not { } confirmedDelete || !library.SelectedUnchanged(confirmedDelete)) return false;
                _deleteReplay = null;
                return ReplayWork(RmlUiReplayWork.Delete, inputs);
            case 17: return _services.RevealReplayFolder();
            case 19: // Original import action remains a compatible alias.
            case 20: // Authored Theatre import-path button.
                if (String.IsNullOrWhiteSpace(inputs.ImportPath) || inputs.ImportPath.Length > 4096)
                { Status("ENTER AN ABSOLUTE LOCAL REPLAY PATH TO IMPORT"); return false; }
                return ReplayWork(RmlUiReplayWork.Import, inputs);
            default:
                Status("OPEN FULL REPLAY STUDIO FOR THIS LIBRARY OPERATION");
                return false;
        }
    }

    private bool ReplayWork(RmlUiReplayWork work, RmlUiReplayInputs inputs)
    {
        if (_theatre is not { } library || _busy || library.Launched) return false;
        RmlUiReplayEntry? entry = library.Selected;
        if (work != RmlUiReplayWork.Import && !entry.HasValue) return false;
        if (work == RmlUiReplayWork.Recover && entry?.Recoverable != true) return false;
        var request = new RmlUiReplayWorkRequest(work, entry, inputs);
        Begin(Job.TheatreWork, "REPLAY " + work.ToString().ToUpperInvariant() + " IN PROGRESS",
            async token => await Task.Run(() => _services.WorkReplayAsync(request, token), token).ConfigureAwait(false));
        return true;
    }

    private bool OpenAdventure()
    {
        if (_services.Lobby().Active || _joining || _creating) return false;
        if (_adventure != null) return true;
        _offline = null;
        _training = null;
        _trainingOptionsOpen = false;
        _adventure = new RmlUiAdventureDraft(_services.ReadAdventureSlots(),
            _services.Hub().DisplayHunter);
        Cancel();
        _browsing = false;
        Dirty();
        return true;
    }

    private bool StartAdventure(bool newGame)
    {
        if (!_services.Hub().GameFilesReady) { Status("GAME FILES REQUIRED FOR CAMPAIGN LAUNCH // SAVE MANAGEMENT REMAINS AVAILABLE"); return false; }
        if (_adventure is not { } draft || draft.Launched || draft.Confirming) return false;
        RmlUiAdventureSlot expected = draft.Selected;
        if (expected.Unreadable || (!newGame && !expected.Used)
            || (newGame && expected.Used)) return false;
        if (!_services.LaunchAdventure(expected, newGame, draft.Hunter))
        {
            Status("SAVE FILE CHANGED OR LAUNCH REFUSED // REFRESH SAVES");
            return false;
        }
        draft.LaunchedSuccessfully();
        Status("ADVENTURE HANDOFF ACCEPTED");
        return true;
    }

    /// <summary>
    /// Re-arm the existing bot match setup only after the shell has retired
    /// the launched offline scene. Keep the selected arena, rules and Hunter
    /// rather than forcing the player through Play to create a new draft.
    /// </summary>
    public bool OfflineSessionClosed()
    {
        Owner();
        if (_offline == null || !_offlineLaunched || _joining || _creating || _busy
            || _services.Lobby().Active) return false;
        _offlineLaunched = false;
        _offline.RefreshArenas(_services.OfflineArenaKeys());
        Status("BOT MATCH ENDED // READY TO LAUNCH AGAIN");
        return true;
    }

    public bool TrainingSessionClosed()
    {
        Owner();
        if (_joining || _creating || _busy || _services.Lobby().Active) return false;
        if (_offline == null) return OpenOffline(training: true);
        _trainingLaunched = false;
        _offlineLaunched = false;
        _trainingOptionsOpen = false;
        _trainingRoute = true;
        _offline.RefreshArenas(_services.OfflineArenaKeys());
        Status("AIM LAB CLOSED // REVIEW YOUR DRILL OR LAUNCH AGAIN");
        return true;
    }

    private bool OpenOffline(bool training)
    {
        if (_services.Lobby().Active || _joining || _creating) return false;
        if (!_services.Hub().GameFilesReady)
            return RequestGameFiles(training ? SetupResume.Training : SetupResume.Offline);
        if (_offline != null)
        {
            if (_trainingRoute != training)
            {
                // Retire stale modals and validation before reusing the shared draft.
                Cancel();
                _offlineRules = null;
                _trainingOptionsOpen = false;
                if (_offline.ArenaOpen) _offline.CloseArena();
            }
            _offline.RefreshArenas(_services.OfflineArenaKeys());
            _trainingRoute = training;
            Dirty();
            return true;
        }
        var hub = _services.Hub();
        _offline = new RmlUiOfflineDraft(_services.OfflineArenaKeys(),
            hub.DisplayHunter, hub.Suit, 1, 1);
        _offline.SetLowTier(_services.ReadOfflineRules()?.Enabled(8) == true);
        _offlineLaunched = false;
        _training = new RmlUiTrainingDraft(_services.TrainingDefaults(), hub.DisplayHunter);
        _trainingOptionsOpen = false;
        _trainingRoute = training;
        _trainingLaunched = false;
        Cancel();
        _browsing = false;
        Dirty();
        return true;
    }

    private bool OpenAdmin(int selectedSlot = -1)
    {
        RmlUiLobbyView lobby = _services.Lobby();
        if (!lobby.Active || lobby.Starting || _hunterDraft.HasValue || _mapPicker != null)
            return false;
        RmlUiAdminSnapshot? snapshot = _services.ReadAdmin();
        if (snapshot == null) return false;
        if (_admin is { Closed: false }) return true;
        _admin = new RmlUiLobbyAdminController(_services, snapshot,
            selectedSlot >= 0 ? selectedSlot : snapshot.LocalSlot);
        Dirty();
        return true;
    }

    private bool AdminAction(RmlUiIntentKind kind, int argument, string chat)
    {
        if (_admin == null) return false;
        bool handled = _admin.Dispatch(kind, argument, chat);
        Dirty();
        return handled;
    }

    private bool OpenMapPicker()
    {
        RmlUiLobbyView lobby = _services.Lobby();
        if (!lobby.Active || lobby.Starting || _rulesSubmitted || _hunterDraft.HasValue
            || _admin is { Closed: false }) return false;
        bool created = _rules == null;
        if (created)
        {
            RmlUiRuleDraft? live = _services.ReadRules();
            if (!live.HasValue) return false;
            _rules = new(live.Value);
            _rulesStatus = lobby.Owner
                ? "CHOOSE A MAP // SELECT TO SEND TO SERVER"
                : "VIEW ONLY // THE LOBBY OWNER CONTROLS MAP SELECTION";
        }
        RetireMapPicker();
        _mapPicker = new(_services.BuiltInArenaKeys(), Array.Empty<string>());
        _mapCatalogCancel = new();
        _mapCatalogStatus = "VERIFYING INSTALLED MAPS IN BACKGROUND";
        try { _mapCatalogWork = _services.ReadMapPickerCatalogAsync(_mapCatalogCancel.Token); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { _mapCatalogStatus = "INSTALLED MAP CATALOG UNAVAILABLE // REOPEN TO RETRY"; }
        PollMapCatalog();
        _mapOpenedRules = created;
        Dirty();
        return true;
    }

    private void PollMapCatalog()
    {
        if (_mapCatalogWork is not { IsCompleted: true } work) return;
        _mapCatalogWork = null;
        try
        {
            var choices = work.GetAwaiter().GetResult();
            _mapPicker?.LoadInstalled(choices);
            _mapCatalogStatus = "";
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { _mapCatalogStatus = "INSTALLED MAP CATALOG UNAVAILABLE // REOPEN TO RETRY"; }
        _mapCatalogCancel?.Dispose(); _mapCatalogCancel = null; Dirty();
    }

    private void RetireMapPicker()
    {
        _mapPicker = null; _mapCatalogStatus = "";
        var cancel = _mapCatalogCancel; _mapCatalogCancel = null;
        var work = _mapCatalogWork; _mapCatalogWork = null;
        cancel?.Cancel();
        if (work is { IsCompleted: false })
            _ = work.ContinueWith(done => { _ = done.Exception; cancel?.Dispose(); }, TaskScheduler.Default);
        else { _ = work?.Exception; cancel?.Dispose(); }
    }

    private bool ChangeRules(Func<RmlUiRuleEditor, bool> modify)
    {
        if (_rules is not { } editor || _rulesSubmitted || !_services.Lobby().Owner)
            return false;
        RmlUiRuleDraft? live = _services.ReadRules();
        if (!live.HasValue || !editor.SameSession(live.Value))
        {
            _rulesStatus = "SESSION RULES CHANGED. CLOSE AND REOPEN.";
            Dirty();
            return false;
        }
        bool changed = modify(editor);
        if (changed)
        {
            _rulesStatus = "PENDING LOCAL CHANGES // APPLY WHEN READY";
            Dirty();
        }
        return changed;
    }

    private bool ApplyRules(string time, string goal)
    {
        if (_rules is not { } editor || _rulesSubmitted || !_services.Lobby().Owner)
            return false;
        RmlUiRuleDraft? live = _services.ReadRules();
        if (!live.HasValue || !editor.SameSession(live.Value))
        {
            _rulesStatus = "SESSION CHANGED. CLOSE AND REOPEN THE RULE EDITOR.";
            Dirty();
            return false;
        }
        if (!editor.TrySetLimits(time, goal, out string error) || !editor.Validate(out error)
            || !_services.SubmitRules(editor.Draft, out error))
        {
            _rulesStatus = String.IsNullOrEmpty(error)
                ? "SERVER CANNOT ACCEPT THESE RULES RIGHT NOW." : error;
            Dirty();
            return false;
        }
        _rulesSubmitted = true;
        _rulesStatus = "WAITING FOR SERVER RULE CONFIRMATION";
        Dirty();
        return true;
    }

    private bool LobbyAction(Func<bool> command, string message)
    {
        if (!_services.Lobby().Active) return false;
        if (!command()) { Status("The server is not accepting that command."); return false; }
        Status(message);
        return true;
    }

    public bool ConnectParty(NativePartyJoinTarget target, string playerName = "")
    {
        Owner();
        if (_busy || _joining || _creating || _services.Lobby().Active
            || !NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)) return false;
        _browsing = true;
        Begin(Job.Join, "VERIFYING PARTY DESTINATION AND SERVER ADMISSION",
            async token => new PartyJoinCompletion(target,
                await _services.JoinParty(target, playerName, token).ConfigureAwait(false)));
        _joining = true;
        _joiningEpoch = _epoch;
        _joiningParty = true;
        return true;
    }

    public bool ConnectSocial(NativeSocialJoinTarget target, string playerName,
        Func<bool> alive, Action<bool, string> completed)
    {
        Owner();
        if (_busy || _joining || _creating || _services.Lobby().Active || !alive()
            || !NativeSocialInvitationValidation.ValidTarget(target, DateTimeOffset.UtcNow)) return false;
        Begin(Job.Join, "VERIFYING SOCIAL DESTINATION AND SERVER ADMISSION", async token =>
        {
            RmlUiJoinResult result;
            try { result = await _services.JoinSocial(target, playerName, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { result = new(false, default, "SOCIAL JOIN CANCELLED"); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { result = new(false, default, "SOCIAL DESTINATION COULD NOT BE VERIFIED // REFRESH SOCIAL"); }
            return new SocialJoinCompletion(target, result, alive, completed);
        });
        _joining = _joiningSocial = true; _joiningEpoch = _epoch;
        return true;
    }

    public void CancelSocialConnection()
    {
        Owner();
        if (!_joiningSocial) return;
        Cancel(); _joining = false;
    }

    private void ApplySocialJoin(SocialJoinCompletion completion)
    {
        var result = completion.Result;
        bool alive = completion.Alive();
        bool accepted = false;
        string error = result.Error;
        if (alive && result.Joined && _services.CanAcceptSocialJoin(completion.Target, result.Plan))
        {
            accepted = _services.AcceptJoin(result.Plan);
            if (accepted) _services.NoteSocialJoined(completion.Target);
            else error = "UNABLE TO COMPLETE THE CLIENT HANDOFF // RETRY FROM SOCIAL";
        }
        else if (result.Joined) error = "SOCIAL ACCOUNT OR DESTINATION CHANGED // REFRESH SOCIAL";
        if (!accepted) _services.DiscardSocialJoin(completion.Target, result.Plan);
        if (!alive) return;
        Status(accepted ? "JOINED. WAITING FOR AUTHORITATIVE LOBBY STATE."
            : error.Length == 0 ? "SOCIAL LOBBY JOIN FAILED // REFRESH SOCIAL" : error);
        completion.Completed(accepted, accepted ? "" : _status);
    }

    private bool CommandPartyQueue(int argument)
    {
        if (!_joining || !_joiningParty || _partyQueue.AttemptId <= 0 || !_partyQueue.Active) return false;
        NativePartyQueueCommand command;
        if (argument == 1 && _partyQueue.CanAccept) command = NativePartyQueueCommand.Accept;
        else if (argument == 2 && _partyQueue.CanDecline) command = NativePartyQueueCommand.Decline;
        else if (argument == 3) command = NativePartyQueueCommand.Leave;
        else return false;
        if (!_services.CommandPartyQueue(_partyQueue, command))
        {
            Status("PARTY QUEUE CHANGED // WAIT FOR THE CURRENT SERVER STATE");
            return false;
        }
        Status(command == NativePartyQueueCommand.Accept ? "REQUESTING ADMISSION FOR THE RESERVED SEAT"
            : "PARTY QUEUE CANCELLATION REQUESTED // WAIT FOR SERVER CONFIRMATION");
        return true;
    }

    private void StartRandomSearch(string playerName, bool retry)
    {
        long started = retry ? _randomStarted : _searchClock();
        if (!retry) _randomPlayerName = playerName;
        string name = _randomPlayerName;
        Begin(Job.Quick, "SEARCHING FOR A RANDOM PUBLIC MATCH", async token =>
        {
            if (retry) await _searchDelay(token).ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var selected = await _services.QuickMatch(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (selected is { Compatible: true } candidate && candidate.Players < candidate.Capacity)
                {
                    var result = await _services.JoinRandom(candidate.Endpoint, name, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (result.Joined) return result;
                }
                await _searchDelay(token).ConfigureAwait(false);
            }
        });
        _randomStarted = started;
        _randomSearch = true;
        _joining = true;
    }

    private bool Connect(string address, string playerName)
    {
        if (_joining) return false;
        string endpoint = address.Trim();
        if (endpoint.Length == 0) { Status("Enter a server address or select a live server."); return false; }
        _browsing = true;
        Begin(Job.Join, "CONNECTING TO " + endpoint,
            async token => await _services.Join(endpoint, playerName, token).ConfigureAwait(false));
        _joining = true;
        _joiningEpoch = _epoch;
        return true;
    }

    private void Begin(Job kind, string message, Func<CancellationToken, Task<object?>> work)
    {
        Cancel();
        _busy = true;
        _activeMenuJob = kind;
        long epoch = _epoch;
        var cts = new CancellationTokenSource();
        _work = cts;
        Status(message);
        _ = Run(epoch, kind, cts, work);
    }

    private async Task Run(long epoch, Job kind, CancellationTokenSource cts,
        Func<CancellationToken, Task<object?>> work)
    {
        try
        {
            object? result = await work(cts.Token).ConfigureAwait(false);
            Publish(new(epoch, kind, result, null));
        }
        catch (OperationCanceledException)
        {
            Publish(new(epoch, kind, null, "Request cancelled."));
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            Publish(new(epoch, kind, null, kind == Job.TheatreWork
                ? "REPLAY FILE OPERATION FAILED // REFRESH TO VERIFY FILE STATE"
                : kind == Job.AdventureSave ? "SAVE CHANGE OUTCOME UNVERIFIED // REFRESH SAVES"
                : kind == Job.OfflineRulesSave ? "RULE SAVE OUTCOME UNVERIFIED // REOPEN RULES TO VERIFY" : e.Message));
        }
        finally { cts.Dispose(); }
    }

    private void Cancel()
    {
        _studioReplayPath = null;
        _activeMenuJob = null;
        _setupResume = SetupResume.None;
        // Preserve the existing pending lifetime of ordinary joins. Created
        // allocations now have exact client ownership and can retire immediately.
        if (_randomSearch || _creating) _joining = false;
        _creating = false;
        _randomSearch = false;
        _services.CancelRandomJoin();
        _services.CancelCreatedLobby();
        _services.CancelPartyJoin();
        _services.CancelSocialJoin();
        _joiningParty = false;
        _joiningSocial = false;
        _partyQueue = NativePartyQueueSnapshot.Empty;
        _offlineInFlight = null;
        _watchInFlight = null;
        _watchEditorInFlight = false;
        _adventureSaveInFlight = null;
        _offlineRulesSaveInFlight = null;
        _epoch++;
        try { _work?.Cancel(); }
        catch (ObjectDisposedException) { /* A completed worker retired its CTS. */ }
        _work = null;
        _busy = false;
        Dirty();
    }

    private void CancelAdventureSave()
    {
        Cancel();
        _adventure?.SaveChangeFinished(_services.ReadAdventureSlots(),
            "SAVE REQUEST RETIRED // REFRESH TO VERIFY; A COMMITTED WRITE IS NOT UNDONE");
    }

    private void Publish(Completion completion)
    {
        lock (_completionGate)
            if (!_disposed) _completed.Enqueue(completion);
    }

    private void DiscardPartyCompletion(Completion completion)
    {
        if (completion.Kind == Job.Create && completion.Data is RmlUiJoinResult created)
            _services.DiscardCreatedLobby(created.Plan);
        if (completion.Kind == Job.Quick && completion.Data is RmlUiJoinResult random)
            _services.DiscardRandomJoin(random.Plan);
        if (completion.Data is PartyJoinCompletion party)
            _services.DiscardPartyJoin(party.Target, party.Result.Plan);
        if (completion.Data is SocialJoinCompletion social)
        {
            _services.DiscardSocialJoin(social.Target, social.Result.Plan);
            if (!_disposed && social.Alive()) social.Completed(false, "SOCIAL JOIN CANCELLED");
        }
    }

    private void Status(string message)
    {
        _status = message.Length <= 220 ? message : message[..220];
        Dirty();
    }

    private void Dirty()
    {
        _dirty = true;
        _nextPoll = 0;
    }

    private Dictionary<string, RmlUiBindingValue> Project(HubSnapshot hub, string version, RmlUiLobbyView lobby)
    {
        var model = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
        void Text(string key, string value) => model[key] = RmlUiBindingValue.FromText(value ?? "");
        void Flag(string key, bool value) => model[key] = RmlUiBindingValue.FromBoolean(value);

        Flag("party_queue_open", _joining && _joiningParty && _partyQueue.Active);
        Text("party_queue_status", _partyQueue.Status);
        Text("party_queue_phase", _partyQueue.Phase.ToString().ToUpperInvariant());
        Text("party_queue_position", _partyQueue.Position > 0
            ? $"WAITLIST {_partyQueue.Position} / {_partyQueue.QueueLength}" : "SERVER VERIFICATION IN PROGRESS");
        Text("party_queue_seats", _partyQueue.ReservedCount > 0
            ? $"{_partyQueue.ReservedCount} SEATS RESERVED // ADMISSION STILL REQUIRED" : "NO SEAT RESERVED");
        Text("party_queue_seconds", Math.Max(0, _partyQueue.RemainingSeconds).ToString());
        Flag("party_queue_offer", _partyQueue.Phase is NativePartyQueuePhase.Reserved or NativePartyQueuePhase.Admitting);
        Flag("party_queue_can_accept", _partyQueue.Active && _partyQueue.CanAccept);
        Flag("party_queue_can_decline", _partyQueue.Active && _partyQueue.CanDecline);
        Flag("party_queue_can_leave", _partyQueue.Active);

        Text("player_name", hub.PlayerName);
        Text("profile_state", hub.GameFilesReady ? "LOCAL PROFILE" : "SETUP REQUIRED");
        Text("hunter_name", hub.DisplayHunter.ToString().ToUpperInvariant());
        Text("game_data_state", hub.GameFilesReady ? "READY" : "NOT CONFIGURED");
        Text("build_version", version);
        Text("renderer_name", "OPENGL // RMLUI 6.3");
        // Project the actual bundled News source, rather than leaving the
        // historical Home feature rail permanently blank.
        NewsDispatch? feature = _services.FeaturedNews();
        Flag("home_feature_available", feature != null);
        Text("home_feature_category", feature?.Category ?? "");
        Text("home_feature_title", feature?.Title ?? "");
        Text("home_feature_summary", feature?.Summary ?? "");
        NewsDispatch[] additionalNews = new BundledNewsProvider().Read()
            .Where(item => !String.Equals(item.Title, feature?.Title, StringComparison.Ordinal))
            .Take(2).ToArray();
        for (int news = 0; news < 2; news++)
        {
            bool present = news < additionalNews.Length;
            Flag("home_news" + news + "_visible", present);
            Text("home_news" + news + "_title", present ? additionalNews[news].Title : "");
            Text("home_news" + news + "_category", present ? additionalNews[news].Category : "");
        }
        Text("home_session_state", !lobby.Active ? "SOLO"
            : lobby.Starting ? "MATCH STARTING"
            : lobby.Owner ? "LOBBY OWNER" : "IN LOBBY");
        RmlUiSocialHomeState social = _services.SocialHome();
        Flag("home_social_ready", social.Available);
        Flag("home_social_rail_visible", true);
        bool pendingInvitations = social.InvitationsVerified && social.IncomingInvitations > 0;
        Flag("home_social_alert_visible", social.Connected && (social.IncomingRequests > 0 || pendingInvitations));
        Flag("home_party_preview_visible", false);
        Flag("home_friends_visible", social.Connected && social.Friends.Length > 0);
        Flag("home_social_empty_visible", !social.Connected || social.Friends.Length == 0);
        Text("home_party_state", social.Connected ? "FRIENDS // OPEN SOCIAL FOR PARTY"
            : "CONNECT FRIENDS AND REQUESTS");
        Text("home_social_summary", social.Connected
            ? social.Friends.Length + " FRIEND PROFILES // "
                + social.IncomingRequests + " PENDING REQUESTS"
                + (social.PresenceVerified ? " // VERIFIED SHARED PRESENCE" : " // PRESENCE UNAVAILABLE")
            : "OPEN SOCIAL TO CONNECT YOUR VERIFIED HUNTER ACCOUNT");
        Text("home_social_empty_text", social.Connected ? "NO FRIENDS YET // OPEN SOCIAL TO FIND HUNTERS"
            : social.Available ? "CONNECT THROUGH SOCIAL TO VERIFY FRIENDS AND PARTY"
            : "SOCIAL SERVICE UNAVAILABLE // OPEN SOCIAL FOR STATUS");
        Text("home_social_count", social.Connected
            ? social.Friends.Length + " FRIENDS" : "ACCOUNT UNAVAILABLE");
        Text("home_friends_online", social.Connected && social.PresenceVerified
            ? social.SharingPresence.ToString() : "UNKNOWN");
        Text("home_invites_count", social.Connected && social.InvitationsVerified ? social.IncomingInvitations.ToString() : "NOT SYNCED");
        Text("home_requests_count", social.Connected
            ? social.IncomingRequests.ToString() : "NOT SYNCED");
        Text("home_social_alert_text", pendingInvitations ? "PENDING GAME INVITATIONS" : "PENDING FRIEND REQUESTS");
        Text("home_friend_count", social.Connected ? social.Friends.Length.ToString() : "0");
        for (int friend = 0; friend < 3; friend++)
        {
            bool shown = social.Connected && friend < social.Friends.Length;
            Flag("home_friend" + friend + "_visible", shown);
            bool joinable = shown && friend < social.FriendJoinable.Length && social.FriendJoinable[friend];
            Flag("home_friend" + friend + "_joinable", joinable);
            Text("home_friend" + friend + "_name", shown ? social.Friends[friend] : "");
            Text("home_friend" + friend + "_activity", shown
                ? friend < social.FriendActivities.Length ? social.FriendActivities[friend] : "PRESENCE UNAVAILABLE" : "");
            Text("home_friend" + friend + "_status", shown ? joinable ? "JOINABLE // OPEN SOCIAL" : "FRIEND" : "");
        }
        // Home has four presentation seats. Membership comes only from the
        // existing verified Party owner; friends/presence never imply membership.
        NativePartyView partyView = _services.PartyHome();
        NativePartySnapshot? party = partyView.Connected
            && NativePartyAuthorityController.ValidSnapshot(partyView.Snapshot)
                ? partyView.Snapshot : null;
        NativePartyMember[] partyMembers = party?.Members.Where(m => !String.Equals(
                m.PrimeId, party.SelfPrimeId, StringComparison.OrdinalIgnoreCase))
            .Take(3).ToArray() ?? Array.Empty<NativePartyMember>();
        Text("home_party_count", (1 + partyMembers.Length) + " / 4");
        if (party?.PartyId.Length > 0) Text("home_session_state", "PARTY");
        Text("home_party_capacity_hint", party?.Members.Length > 4
            ? "+" + (party.Members.Length - 4) + " MORE MEMBERS // OPEN SOCIAL"
            : partyView.Busy ? "VERIFYING PARTY MEMBERSHIP"
            : "SELECT AN OPEN SLOT TO INVITE A FRIEND");
        for (int seat = 1; seat < 4; seat++)
        {
            NativePartyMember? member = partyMembers.ElementAtOrDefault(seat - 1);
            Text("home_party_slot" + seat + "_name", member?.DisplayName ?? "OPEN PARTY SLOT");
            Text("home_party_slot" + seat + "_detail", member == null ? "+ INVITE FRIEND" : member.Presence switch
            {
                NativePresence.Online => "ONLINE", NativePresence.InLobby => "IN LOBBY",
                NativePresence.InMatch => "IN MATCH", NativePresence.Offline => "OFFLINE",
                _ => "PRESENCE NOT SHARED"
            });
            Flag("home_party_slot" + seat + "_occupied", member != null);
        }
        Flag("lobby_mode", lobby.Active);
        Flag("multiplayer_mode", _browsing && !lobby.Active);
        Flag("theatre_mode", _theatre != null && !lobby.Active);
        Flag("theatre_busy", _theatre != null && _busy);
        Flag("theatre_can_previous", _theatre?.CanPrevious == true);
        Flag("theatre_can_next", _theatre?.CanNext == true);
        Text("theatre_summary", _theatre == null ? "" :
            _theatre.Count + " MATCHING // " + _theatre.TotalCount + " RECORDINGS");
        Text("theatre_page_number", _theatre == null ? "" :
            $"{_theatre.Page + 1} / {_theatre.Pages}");
        Text("theatre_filter_label", _theatre?.FilterLabel ?? "ALL REPLAYS");
        Text("theatre_sort_label", _theatre?.SortLabel ?? "NEWEST FIRST");
        Text("theatre_empty", _theatre?.Count == 0 ? "NO RECORDINGS MATCH THIS FILTER" : "");
        Text("theatre_status", _theatre == null ? "" : _status);
        Text("theatre_error", "");
        Text("theatre_launch_problem", "");
        Flag("theatre_empty_visible", _theatre?.Count == 0);
        Flag("theatre_status_visible", _theatre != null && _status.Length > 0);
        Flag("theatre_error_visible", false);
        Flag("theatre_launch_problem_visible", false);
        RmlUiReplayEntry? selectedReplay = _theatre?.Selected;
        Text("theatre_selected_title", selectedReplay?.Title ?? "SELECT A REPLAY");
        Text("theatre_metadata", selectedReplay is { } selection
            ? selection.Detail + "\n" + selection.Room : "CHOOSE A RECORDING FROM THE LIBRARY");
        Text("theatre_hero", selectedReplay is { } hero
            ? (hero.Clip ? "CLIP" : "FULL REPLAY") + " // " + hero.Room : "ARCHIVE STANDBY");
        Text("theatre_no_preview", _services.ReplayEditingAllowed
            ? "OPEN A REPLAY TO EDIT IN PRIME STUDIO" : "WATCH REPLAYS HERE // EDIT IN PRIME STUDIO");
        Text("theatre_studio", _services.ReplayEditingAllowed ? "EDIT REPLAY" : "OPEN IN PRIME STUDIO");
        Text("theatre_workflow_hint", _services.ReplayEditingAllowed
            ? "Edit a replay to use bookmarks, clips, camera keys and video export."
            : "Watch and organize recordings here. Open Prime Studio for replay editing and export.");
        Flag("theatre_advanced_tools", _services.ReplayEditingAllowed);
        Flag("theatre_watch_enabled", selectedReplay is { Playable: true }
            && _theatre?.Launched == false && !_busy);
        Flag("theatre_favorite_enabled", selectedReplay is { Playable: true }
            && !_busy);
        bool canEditReplay = selectedReplay != null && !_busy && _theatre?.Launched == false;
        Flag("theatre_edit_enabled", canEditReplay && !_deleteReplay.HasValue);
        Flag("theatre_export_enabled", _services.ReplayEditingAllowed && canEditReplay && !_deleteReplay.HasValue
            && selectedReplay?.VirtualWitness == null);
        Flag("theatre_recover_enabled", _services.ReplayEditingAllowed && canEditReplay && selectedReplay?.Recoverable == true && !_deleteReplay.HasValue);
        Flag("theatre_import_enabled", _theatre != null && !_busy && !_deleteReplay.HasValue);
        Flag("theatre_delete_confirmation", _deleteReplay.HasValue);
        Text("theatre_delete_prompt", _deleteReplay is { } pendingDelete
            ? "DELETE RECORDING AND ITS LIBRARY SIDECARS? // " + pendingDelete.Title : "");
        for (int row = 0; row < RmlUiTheatreIndex.PageSize; row++)
        {
            RmlUiReplayEntry? replay = _theatre?.Entry(row);
            Flag("theatre_entry" + row + "_present", replay.HasValue);
            Flag("theatre_entry" + row + "_selected",
                replay is { } candidate && selectedReplay is { } chosen
                && String.Equals(candidate.Path, chosen.Path, StringComparison.OrdinalIgnoreCase));
            Text("theatre_entry" + row + "_title", replay?.Title ?? "");
            Text("theatre_entry" + row + "_detail", replay?.Detail ?? "");
        }
        Flag("adventure_mode", _adventure != null && !lobby.Active);
        Flag("adventure_confirmation_open", _adventure?.Confirming == true);
        var saveChange = _adventure?.PendingSaveChange;
        Flag("adventure_management_confirmation", saveChange.HasValue);
        Flag("adventure_copy_confirmation", saveChange?.Action == RmlUiAdventureSaveAction.Copy);
        Flag("adventure_archive_confirmation", saveChange?.Action == RmlUiAdventureSaveAction.Archive);
        Flag("adventure_new_run_confirmation", _adventure?.PendingOverwrite != null);
        bool saveBusy = _adventureSaveInFlight.HasValue;
        Flag("adventure_save_busy", saveBusy);
        Flag("adventure_can_copy", !saveBusy && _adventure?.CanCopy == true && _services.CanManageAdventureSaves);
        Flag("adventure_can_archive", !saveBusy && _adventure?.CanArchive == true && _services.CanManageAdventureSaves);
        Text("adventure_copy_destination", "COPY INTO SLOT " + (_adventure?.CopyDestination ?? 0).ToString("00"));
        Text("adventure_confirmation_kind", saveChange.HasValue ? "ADVENTURE // SAVE MANAGEMENT" : "ADVENTURE // NEW RUN");
        Text("adventure_confirmation_description", saveChange is { } captured
            ? captured.Action == RmlUiAdventureSaveAction.Copy
                ? $"SOURCE SLOT {captured.Source.Slot:00} // {captured.Source.Fingerprint}\nDESTINATION SLOT {captured.Destination!.Value.Slot:00} // {captured.Destination.Value.Fingerprint}\nExact source bytes will be copied. Any occupied destination is preserved in an unused recoverable backup."
                : $"ARCHIVE SLOT {captured.Source.Slot:00} // {captured.Source.Fingerprint}\nThe slot will become empty. Exact bytes are moved into an unused recoverable backup; nothing is permanently deleted."
            : "Starting a new run replaces this slot's existing progress when saved.");
        Flag("adventure_can_resume", !saveBusy && _adventure?.CanResume == true && hub.GameFilesReady);
        Flag("adventure_ship_available", !saveBusy && _adventure?.CanResume == true && hub.GameFilesReady
            && _services.CanOpenCampaignShip(_adventure.SelectedSlot));
        Flag("adventure_can_new_run", !saveBusy && _adventure?.CanStart == true && hub.GameFilesReady);
        Flag("adventure_game_files_missing", _adventure != null && !hub.GameFilesReady);
        Text("adventure_hunter", _adventure?.Hunter.ToString().ToUpperInvariant() ?? "");
        Text("adventure_selected_slot", _adventure == null ? ""
            : $"SAVE SLOT {_adventure.SelectedSlot:00}");
        Text("adventure_save_detail", _adventure == null ? ""
            : _adventure.Describe(_adventure.Selected));
        Text("adventure_launch_status", _adventure?.Status ?? "");
        Flag("adventure_error_visible", _adventure?.Selected.Unreadable == true);
        Text("adventure_error_text", _adventure?.Selected.Unreadable == true
            ? "THIS SAVE IS UNREADABLE. ARCHIVE ITS EXACT BYTES BEFORE REPLACING IT." : "");
        Text("adventure_file_problem", "GAME FILES ARE REQUIRED TO LAUNCH CAMPAIGN // EXACT BYTE COPY AND ARCHIVE DO NOT REQUIRE THEM");
        Text("adventure_overwrite_title", _adventure?.PendingOverwrite is { } pending
            ? $"REPLACE SAVE SLOT {pending.Slot:00}?" : saveChange is { } proposed
                ? proposed.Action == RmlUiAdventureSaveAction.Copy ? $"COPY SLOT {proposed.Source.Slot:00} TO {proposed.Destination!.Value.Slot:00}?"
                    : $"ARCHIVE SAVE SLOT {proposed.Source.Slot:00}?" : "");
        Text("adventure_confirmation_error_text", "");
        for (int slot = 1; slot <= AdventureSave.SlotCount; slot++)
        {
            RmlUiAdventureSlot info = _adventure?.Slot(slot) ?? default;
            Text("adventure_slot" + slot + "_label", "SLOT " + slot.ToString("00")
                + " // " + (_adventure?.Describe(info) ?? "UNAVAILABLE"));
            Flag("adventure_slot" + slot + "_selected", _adventure?.SelectedSlot == slot);
            Flag("adventure_destination" + slot + "_selected", _adventure?.CopyDestination == slot);
            Flag("adventure_destination" + slot + "_disabled", saveBusy || _adventure == null || _adventure.Confirming || _adventure.Launched || _adventure.SelectedSlot == slot);
        }
        Flag("offline_mode", _offline != null && !lobby.Active);
        Flag("offline_training_mode", _offline != null && _trainingRoute && !lobby.Active);
        Flag("offline_training_open", _offline != null && _trainingOptionsOpen && !lobby.Active);
        Flag("offline_arena_open", _offline?.ArenaOpen == true);
        Flag("offline_rules_open", _offline != null && _offlineRules != null && !lobby.Active);
        Flag("offline_rules_busy", _offlineRulesSaveInFlight.HasValue);
        Text("offline_rules_error", _offlineRules?.Error ?? "");
        Flag("offline_point_goal_visible", _offlineRules?.PointGoalVisible == true);
        Flag("offline_time_limit_visible", _offlineRules?.TimeLimitVisible == true);
        Flag("offline_time_goal_visible", _offlineRules?.TimeGoalVisible == true);
        Flag("offline_chamber_help_visible", _offlineRules?.Mode == GameMode.OneInTheChamber);
        Flag("offline_fire_visible", _offlineRules?.FireVisible == true);
        Flag("offline_auto_reset_visible", _offlineRules?.AutoResetVisible == true);
        Flag("offline_movement_visible", _offlineRules?.MovementVisible == true);
        Text("offline_damage_value", "DAMAGE // " + (_offlineRules?.Draft.DamageLevel.ToUpperInvariant() ?? ""));
        for (int i = 0; i < RmlUiOfflineRuleEditor.Labels.Length; i++)
            Text("offline_rule" + i + "_label", RmlUiOfflineRuleEditor.Labels[i]
                + " // " + (i == 13 ? _offlineRules?.MovementLabel.ToUpperInvariant() ?? "CLASSIC"
                    : (i == 14 ? _offlineRules?.Draft.RiftlineM7Available == true
                        : _offlineRules?.Draft.Enabled(i) == true) ? "ON" : "OFF"));
        Flag("play_browser_mode", _browsing && !_createOpen && !lobby.Active);
        Flag("play_create_mode", _createOpen && !lobby.Active);
        Text("offline_arena_value", _offline?.Map ?? "");
        Text("offline_arena_page", _offline == null ? "" :
            $"{_offline.ArenaPage + 1} / {_offline.ArenaPageCount}");
        Flag("offline_arena_can_previous", _offline?.ArenaHasPrevious == true);
        Flag("offline_arena_can_next", _offline?.ArenaHasNext == true);
        Text("offline_arena_picker_error", _offline?.ArenaCount == 0
            ? "NO MATCHING INSTALLED ARENAS" : "");
        for (int i = 0; i < RmlUiOfflineDraft.ArenaPageSize; i++)
        {
            string? room = _offline?.ArenaChoice(i);
            Flag("offline_arena_row" + i + "_present", room != null);
            Text("offline_arena_name" + i, room ?? "");
            Text("offline_arena_reason" + i, room == null ? ""
                : room.Equals(_offline?.Map, StringComparison.OrdinalIgnoreCase)
                    ? "CURRENT ARENA" : "BUILT-IN INSTALLED MAP");
        }
        Text("offline_mode_value", _offline?.Mode ?? "");
        Text("offline_matchup_value", _offline?.Matchup ?? "");
        Text("offline_bots_value", _offline?.Bots ?? "");
        Text("offline_skill_value", _offline?.Difficulty ?? "");
        Text("offline_hunter_value", _offline?.HunterName ?? "");
        Text("offline_suit_value", _offline?.SuitName ?? "");
        Text("offline_character_value", _offline?.CharacterName ?? "");
        Text("offline_character_notice", _offline?.CharacterNotice ?? "");
        Text("offline_start_label", _offline?.Current.Character switch
        {
            OfflineCharacterMode.TrooperWallClingPractice => "START WALL-CLING PRACTICE",
            OfflineCharacterMode.TrooperScoutDronePractice => "START DRONE PRACTICE",
            OfflineCharacterMode.TrooperScoutDroneCombatPractice => "START DRONE COMBAT PRACTICE",
            OfflineCharacterMode.TrooperArsenalPractice => "START ARSENAL PRACTICE",
            _ => "INITIATE BOT SIMULATION"
        });
        Text("offline_summary", _offline == null ? "" :
            _offline.Mode + " // " + _offline.Bots + " // " + _offline.Difficulty);
        Text("offline_eyebrow_label", _trainingRoute ? "OPERATIONS // TRAINING" : "OPERATIONS // OFFLINE");
        Text("offline_heading_label", _trainingRoute ? "AIM LAB" : "OFFLINE BATTLE");
        Text("offline_description_label", _trainingRoute
            ? "Configure dedicated aim and movement drills."
            : "Build a local match with bots and custom rules.");
        Text("offline_error", _offline == null ? "" : _status);
        Text("offline_availability", _trainingRoute
            ? "PERSONAL TRAINING // CHOOSE A DRILL"
            : _offline?.Valid == true
                ? "READY // VALIDATION RUNS BEFORE LAUNCH" : "NO INSTALLED BUILT-IN ARENAS");
        Flag("offline_can_start", !_trainingRoute && _offline?.Valid == true
            && !_busy && !_offlineLaunched && _offlineRules == null);
        Flag("offline_training_can_start", _trainingRoute && _training != null && !_trainingLaunched
            && !lobby.Active && !_busy);
        Text("offline_training_hunter_value", _training?.HunterLabel ?? "");
        Text("offline_drill_value", _training?.DrillLabel ?? "");
        Text("offline_weapon_value", _training?.WeaponLabel ?? "");
        Text("offline_duration_value", _training?.DurationLabel ?? "");
        Text("offline_targets_value", _training?.TargetLabel ?? "");
        Text("offline_movement_value", _training?.MovementLabel ?? "");
        Text("offline_training_difficulty_value", _training?.DifficultyLabel ?? "");
        Text("offline_training_distance_value", _training?.DistanceLabel ?? "");
        Text("offline_training_scope_value", _training?.ScopeLabel ?? "");
        Flag("offline_training_can_targets", _training?.CanChangeTargets == true);
        Flag("offline_training_can_weapon", _training?.CanChangeWeapon == true);
        Flag("offline_training_can_headshots", _training?.CanChangeHeadshots == true);
        for (int i = 0; i < 5; i++)
            Text("offline_training_toggle" + i, _training?.ToggleLabel(i) ?? "");
        Text("play_create_map", _createMap);
        Text("play_create_capacity", _createMaxPlayers + " PLAYER SLOTS // ADVANCED RULES AFTER CREATION");
        Text("play_create_source", _createCommunity != null ? "COMMUNITY // SELECTED INSTALLED REVISION" : "MPH // BUILT-IN ARENA");
        Text("play_create_mode_name", MatchTypeCatalog.BaseLabel(_createMode));
        Text("play_create_host", _createDedicated ? "LOCAL // DEDICATED" : "HOSTED // ONLINE");
        Flag("play_random_search", _randomSearch);
        Flag("play_actions_locked", _randomSearch || _joining || _creating);
        Text("play_search_elapsed", $"SEARCHING // {Math.Max(0, _searchClock() - _randomStarted) / 1000}s // PUBLIC SESSIONS");
        Flag("play_busy", _busy);
        Flag("play_no_servers", _rowCount == 0);
        Text("play_status", lobby.Active ? lobby.Status : _status);
        Text("play_server_count", _rowCount.ToString() + " LIVE");
        for (int i = 0; i < _rows.Length; i++)
        {
            RmlUiServerCandidate row = _rows[i];
            Flag("play_server" + i + "_present", i < _rowCount);
            Text("play_server" + i + "_name", i < _rowCount ? row.Name : "");
            Text("play_server" + i + "_details", i < _rowCount
                ? row.Players + "/" + row.Capacity + " PLAYERS // "
                  + (row.Ping >= 0 ? row.Ping + " MS" : "PING UNKNOWN")
                : "");
        }
        Flag("lobby_owner", lobby.Active && lobby.Owner);
        Flag("lobby_can_start", lobby.Active && lobby.Owner && !lobby.Starting && lobby.CanStart);
        Flag("lobby_require_ready", lobby.Active && lobby.RequireReady);
        Flag("lobby_local_ready", lobby.Active && lobby.LocalReady);
        Flag("lobby_starting", lobby.Active && lobby.Starting);
        Flag("lobby_rules_open", lobby.Active && _rules != null);
        Flag("lobby_map_open", lobby.Active && _mapPicker != null);
        Flag("lobby_hunter_open", lobby.Active && _hunterDraft.HasValue);
        Text("hunter_identity_name", _hunterDraft.HasValue
            ? _hunterChoice.ToString().ToUpperInvariant() + " // SUIT " + (_hunterSuit + 1) : "");
        Text("hunter_status", _hunterStatus);
        Text("hunter_restriction", _hunterDraft is { } hunterSelection && hunterSelection.Allowed.Length < 7
            ? "SERVER RESTRICTS AVAILABLE HUNTERS FOR THIS MATCH" : "CHOOSE YOUR HUNTER");
        Flag("hunter_apply_enabled", _hunterDraft is { CommandPending: false }
            && CanPickHunter(_hunterChoice));
        Flag("hunter_random_allowed", _hunterDraft is { } randomSelection
            && RmlUiLobbyHunterCards.TryRandom(randomSelection.Allowed, 0, out _));
        for (int i = 0; i < 7; i++)
        {
            Flag("hunter_choice" + i + "_selected", _hunterDraft.HasValue && (int)_hunterChoice == i);
            Flag("hunter_choice" + i + "_allowed", CanPickHunter((Hunter)i));
        }
        for (int i = 0; i < 4; i++)
            Flag("hunter_suit" + i + "_selected", _hunterDraft.HasValue && _hunterSuit == i);
        Flag("rules_owner", lobby.Active && lobby.Owner);
        Flag("rules_dirty", _rules?.Dirty ?? false);
        Flag("rules_pending", _rulesSubmitted);
        Text("rules_status", _rulesStatus);
        Flag("map_vanilla_selected", _mapPicker?.Category == 0);
        Flag("map_community_selected", _mapPicker?.Category == 1);
        Flag("map_echoes_selected", _mapPicker?.Category == 2);
        Flag("map_halo_selected", _mapPicker?.Category == 3);
        Flag("map_quake_selected", _mapPicker?.Category == 4);
        Flag("map_empty", _mapPicker == null || _mapPicker.Count == 0);
        Flag("map_can_previous", _mapPicker?.HasPrevious == true);
        Flag("map_can_next", _mapPicker?.HasNext == true);
        Text("map_page", _mapPicker == null ? "" : $"{_mapPicker.Page + 1} / {_mapPicker.Pages}");
        Text("map_status", !lobby.Owner ? "VIEW ONLY // THE LOBBY OWNER SELECTS MAPS"
            : _rulesSubmitted ? "WAITING FOR SERVER CONFIRMATION"
            : _mapOpenedRules ? _rulesStatus
            : "CHOOSE AN INSTALLED MAP // APPLY IN THE RULES PANEL");
        string categoryName = (_mapPicker?.Category ?? 0) switch { 0 => "MPH", 2 => "ECHOES", 3 => "HALO", 4 => "QUAKE", _ => "COMMUNITY" };
        Text("map_empty_text", _mapCatalogStatus.Length > 0 && _mapPicker?.Category != 0
            ? _mapCatalogStatus : "NO " + categoryName + " MAPS INSTALLED");
        string[] previewRooms = Enumerable.Range(0, RmlUiMapPickerModel.PageSize)
            .Select(i => _mapPicker?.Choice(i) ?? "").ToArray();
        RmlUiArenaPreview[] previews = _services.ReadMapPickerPreviews(
            _mapPicker == null ? Array.Empty<string>() : previewRooms);
        for (int i = 0; i < RmlUiMapPickerModel.PageSize; i++)
        {
            string? room = _mapPicker?.Choice(i);
            Flag("map_choice" + i + "_visible", room != null);
            var detail = _mapPicker?.Detail(i);
            Flag("map_choice" + i + "_enabled", room != null && detail?.Selectable != false && lobby.Owner && !_rulesSubmitted);
            Text("map_choice" + i + "_name", room ?? "");
            Text("map_choice" + i + "_hint", room == null ? ""
                : room.Equals(_rules?.Draft.Match.RoomKey, StringComparison.OrdinalIgnoreCase)
                    ? "SELECTED // CURRENT DRAFT"
                : detail?.Hint ?? "INSTALLED PACKAGE");
            var preview = i < previews.Length ? previews[i] : default;
            Text("map_choice" + i + "_image", preview.Image ?? "");
            Flag("map_choice" + i + "_image_ready", !string.IsNullOrEmpty(preview.Image));
            Text("map_choice" + i + "_preview_status", preview.Status ?? "PREVIEW UNAVAILABLE");
        }
        if (_rules is { } rules)
        {
            Text("rules_map", rules.Draft.Match.RoomKey);
            Text("rules_mode", MphRead.Mods.Network.MatchTypeCatalog.BaseLabel(rules.Draft.Match.Mode));
            Text("rules_format", MphRead.Mods.Network.MatchTypeCatalog.MatchupLabel(
                rules.Draft.Match.Mode, rules.Draft.Match.Format));
            Text("rules_goal_label", MphRead.Mods.Network.MatchGoalRules.UsesLives(rules.Draft.Match.Mode)
                ? "SPARE LIVES" : MphRead.Mods.Network.MatchGoalRules.UsesTimeTarget(rules.Draft.Match.Mode)
                ? "CONTROL TIME" : "SCORE GOAL");
            for (int i = 0; i < RmlUiRuleEditor.ChoiceCount; i++)
                Text("rules_toggle" + i, i == 17 ? rules.MovementLabel.ToUpperInvariant() : rules.IsOn(i) ? "ON" : "OFF");
        }
        // Fill the authored Lobby's actual fields rather than leaving empty
        // placeholders. Every value here comes from server/session state.
        RmlUiLobbyChrome chrome = lobby.Active ? _services.ReadLobbyChrome()
            : RmlUiLobbyChrome.Empty;
        Text("lobby_time", chrome.Time);
        Text("lobby_score", chrome.Score);
        Text("lobby_chat_history", chrome.ChatHistory);
        Text("lobby_chat_status", chrome.ChatStatus);
        Text("lobby_map_preview_status", chrome.MapPreviewStatus);
        Text("lobby_map_image", chrome.MapPreviewPath);
        Flag("lobby_map_image_ready", !String.IsNullOrEmpty(chrome.MapPreviewPath));
        Text("lobby_field_caption", chrome.FieldCaption);
        Text("lobby_countdown_number", chrome.CountdownNumber);
        Text("lobby_countdown_detail", chrome.CountdownDetail);
        Text("lobby_brief_title", lobby.Active ? lobby.Name : "");
        Text("lobby_brief_count", lobby.Active
            ? (lobby.Players?.Length ?? 0) + "/" + lobby.MaxPlayers : "");
        Text("lobby_brief_detail", lobby.Active ? lobby.Map + " // " + lobby.Mode : "");
        RmlUiAdminSnapshot? chromeRoster = lobby.Active ? _services.ReadAdmin() : null;
        RmlUiAdminPlayer[] orderedRoster = RmlUiLobbyDisplayOrder.Ordered(chromeRoster);
        Text("lobby_name", lobby.Active ? lobby.Name : "");
        Text("lobby_map", lobby.Active ? lobby.Map : "");
        Text("lobby_mode_name", lobby.Active ? lobby.Mode : "");
        Text("lobby_format", lobby.Active ? lobby.Format : "");
        RmlUiLobbyPlayer[] lobbyPlayers = lobby.Players ?? Array.Empty<RmlUiLobbyPlayer>();
        Text("lobby_player_count", lobbyPlayers.Length + " / " + lobby.MaxPlayers);
        Text("lobby_ready_count", lobbyPlayers.Count(p => p.Ready) + " READY");
        Text("lobby_ready_action", lobby.LocalReady ? "UNREADY" : "READY");
        Text("lobby_local_hunter", hub.DisplayHunter.ToString().ToUpperInvariant());
        Text("lobby_status", lobby.Active ? lobby.Status : "");
        for (int i = 0; i < 8; i++)
        {
            var row = i < lobbyPlayers.Length ? lobbyPlayers[i] : default;
            Flag("slot" + i + "_occupied", i < lobbyPlayers.Length);
            Flag("slot" + i + "_ready", row.Ready);
            Flag("slot" + i + "_local", row.Local);
            Text("slot" + i + "_name", row.Name ?? "");
            Text("slot" + i + "_hunter", row.Hunter ?? "");
            Text("slot" + i + "_state", row.Bot ? "BOT" : row.Ready ? "READY" : "WAITING");
            RmlUiAdminPlayer? authoritativeRow = i < orderedRoster.Length
                ? orderedRoster[i] : null;
            bool hasTeam = chromeRoster != null && chromeRoster.TeamCount > 0;
            int team = authoritativeRow?.Team ?? -1;
            Flag("slot" + i + "_can_select", authoritativeRow.HasValue);
            Flag("slot" + i + "_can_add_bot", RmlUiLobbyDisplayOrder.CanAddBot(lobby, chromeRoster, i));
            Flag("slot" + i + "_has_team", authoritativeRow.HasValue && hasTeam);
            Flag("slot" + i + "_can_team",
                RmlUiLobbyDisplayOrder.CanChangeTeam(chromeRoster, authoritativeRow));
            Text("slot" + i + "_team", RmlUiLobbyDisplayOrder.TeamLabel(team, hasTeam));
            for (int t = 0; t < 4; t++)
                Flag("slot" + i + "_team" + t, authoritativeRow.HasValue
                    && hasTeam && team == t);
        }
        return model;
    }

    private static bool Same(IReadOnlyDictionary<string, RmlUiBindingValue> old,
        Dictionary<string, RmlUiBindingValue> next)
    {
        if (old.Count != next.Count) return false;
        foreach (var item in next)
            if (!old.TryGetValue(item.Key, out var value) || value != item.Value)
                return false;
        return true;
    }

    private void Owner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("RmlUi service results must be applied on the engine owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner();
        lock (_completionGate) _disposed = true;
        _admin?.Close();
        _admin = null;
        RetireMapPicker();
        Cancel();
        while (_completed.TryDequeue(out var completion)) DiscardPartyCompletion(completion);
    }
}
