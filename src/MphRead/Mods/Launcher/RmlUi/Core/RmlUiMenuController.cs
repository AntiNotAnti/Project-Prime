using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>One directory candidate. A directory hint is never an admitted network session.</summary>
public readonly record struct RmlUiServerCandidate(string Name, string Endpoint,
    int Players, int Capacity, int Ping, bool Compatible);
public readonly record struct RmlUiDirectory(bool Answered, string Message,
    RmlUiServerCandidate[] Servers);
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
    RmlUiLobbyView Lobby();
    Task<RmlUiDirectory> Browse(CancellationToken token);
    Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token);
    Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token);
    Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token);
    bool AcceptJoin(LaunchPlan plan);
    bool OpenCreateLobby();
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
    Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
        => Task.FromResult("Offline Battle is unavailable on this platform.");
    bool LaunchOffline(RmlUiOfflineRequest request) => false;
    AimTrainerDefinition TrainingDefaults() => AimTrainerDefinition.Default;
    bool LaunchTraining(AimTrainerDefinition drill, Hunter hunter, int suit) => false;
    RmlUiAdventureSlot[] ReadAdventureSlots() => Array.Empty<RmlUiAdventureSlot>();
    bool LaunchAdventure(RmlUiAdventureSlot slot, bool newGame, Hunter hunter) => false;
    bool OpenLobbyDetails();
    RmlUiRuleDraft? ReadRules();
    IReadOnlyList<string> BuiltInArenaKeys();
    // Default for other frontends and offline fixtures; only a verified installed
    // package catalog should override this with mutable community choices.
    IReadOnlyList<string> InstalledCommunityArenaKeys() => Array.Empty<string>();
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
    private enum Job { Browse, Quick, Join, Create, Offline }
    private readonly record struct Completion(long Epoch, Job Kind, object? Data, string? Error);
    private readonly IRmlUiMenuServices _services;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Completion> _completed = new();
    private readonly RmlUiServerCandidate[] _rows = new RmlUiServerCandidate[8];
    private CancellationTokenSource? _work;
    private RmlUiRuleEditor? _rules;
    private RmlUiLobbyAdminController? _admin;
    private RmlUiOfflineDraft? _offline;
    private RmlUiOfflineRequest? _offlineInFlight;
    private bool _offlineLaunched;
    private RmlUiTrainingDraft? _training;
    private RmlUiAdventureDraft? _adventure;
    private bool _trainingOptionsOpen, _trainingRoute, _trainingLaunched;
    private RmlUiMapPickerModel? _mapPicker;
    private RmlUiHunterDraft? _hunterDraft;
    private Hunter _hunterChoice;
    private byte _hunterSuit;
    private string _hunterStatus = "";
    private bool _mapOpenedRules;
    public bool MapPickerOpen => _mapPicker != null;
    public bool HunterOpen => _hunterDraft.HasValue;
    public bool AdminOpen => _admin is { Closed: false };
    public bool OfflineOpen => _offline != null;
    public bool TrainingOptionsOpen => _trainingOptionsOpen;
    public bool AdventureOpen => _adventure != null;
    private bool _rulesSubmitted;
    private string _rulesStatus = "VIEWING SERVER RULES";
    private long _joiningEpoch;
    private long _epoch, _revision;
    private long _nextPoll;
    private bool _disposed, _busy, _joining, _creating, _browsing, _createOpen, _createDedicated, _dirty = true;
    private string _createMap = "";
    private GameMode _createMode = GameMode.Battle;
    private MatchFormat _createFormat = MatchFormat.FreeForAll;
    private string _status = "READY TO DEPLOY";
    private int _rowCount;
    private RmlUiMenuSnapshot _snapshot =
        new(0, new ReadOnlyDictionary<string, RmlUiBindingValue>(
            new Dictionary<string, RmlUiBindingValue>()));

    public RmlUiMenuController(IRmlUiMenuServices services)
        => _services = services ?? throw new ArgumentNullException(nameof(services));
    public RmlUiMenuSnapshot Snapshot => _snapshot;
    public bool Busy => _busy;
    public bool Joining => _joining;
    public bool Creating => _creating;
    public (string Time, string Goal)? RuleInputs =>
        _rules is { } editor ? (editor.TimeInput(), editor.GoalInput()) : null;

    public bool Dispatch(RmlUiIntentKind kind, int argument = 0,
        string address = "", string playerName = "", string ruleTime = "", string ruleGoal = "",
        string lobbyName = "", string adminChat = "", string arenaSearch = "")
    {
        Owner();
        if (!RmlUiIntentRegistry.IsValid(kind, argument)) return false;
        if (_joining && kind is not (RmlUiIntentKind.PlayCancel or RmlUiIntentKind.Quit))
        {
            Status("Connection in progress. Wait for admission or cancel.");
            return false;
        }
        switch (kind)
        {
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
                _createOpen = false;
                _browsing = true;
                Begin(Job.Quick, "FINDING A COMPATIBLE SERVER",
                    token => _services.QuickMatch(token).ContinueWith<object?>(
                        task => task.GetAwaiter().GetResult(), token));
                return true;
            case RmlUiIntentKind.PlayBrowse:
                _createOpen = false;
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
                if (_creating)
                {
                    Status("The authoritative server allocation is in progress.");
                    return false;
                }
                Cancel();
                _browsing = false;
                _createOpen = false;
                Status("READY TO DEPLOY");
                return true;
            case RmlUiIntentKind.PlayCreateOpen:
                if (!_services.Hub().GameFilesReady)
                    return _services.OpenCreateLobby();
                string[] maps = _services.BuiltInArenaKeys().ToArray();
                if (maps.Length == 0)
                    return _services.OpenCreateLobby();
                _createMap = maps.Contains(_createMap) ? _createMap : maps[0];
                _createOpen = true;
                _browsing = true;
                Status("CHOOSE A MAP, GAME TYPE AND HOST");
                return true;
            case RmlUiIntentKind.PlayNextMap:
                string[] available = _services.BuiltInArenaKeys().ToArray();
                if (available.Length == 0) { Status("NO BUILT-IN ARENAS AVAILABLE"); return false; }
                int at = Array.IndexOf(available, _createMap);
                _createMap = available[(at + 1) % available.Length];
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
                    _createMap, _createMode, _createFormat, hub.DisplayHunter, _createDedicated);
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
                if (argument == (int)RmlUiRouteArgument.Adventure)
                    return OpenAdventure();
                if (argument == (int)RmlUiRouteArgument.Offline)
                {
                    _adventure = null;
                    return OpenOffline(training: false);
                }
                if (argument == (int)RmlUiRouteArgument.Training)
                {
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
                    Cancel();
                    _browsing = false;
                    Dirty();
                    return true;
                }
                Status("Finish the current activity before navigating.");
                return false;
            case RmlUiIntentKind.OpenStudio:
                return _services.OpenStudio();
            case RmlUiIntentKind.LobbyReady:
                if (!_services.Lobby().CanReady) return false;
                return LobbyAction(_services.SetReady, "Ready request sent to server.");
            case RmlUiIntentKind.LobbyStart:
                if (!_services.Lobby().CanStart) return false;
                return LobbyAction(_services.StartMatch, "Start request sent to server.");
            case RmlUiIntentKind.AdventureSelectSlot:
                if (_adventure == null || !_adventure.Select(argument)) return false;
                Dirty(); return true;
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
                if (_adventure == null) return false;
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
                bool acceptedNew = _services.LaunchAdventure(expected, true, _adventure.Hunter);
                if (acceptedNew) _adventure.LaunchedSuccessfully();
                else Status("SAVE FILE CHANGED OR LAUNCH REFUSED // REFRESH SAVES");
                Dirty();
                return acceptedNew;
            case RmlUiIntentKind.OfflineChoice:
                if (_offline == null || _busy || !RmlUiIntentRegistry.IsValid(kind, argument))
                    return false;
                if (argument >= 7)
                {
                    if (_training == null || (argument >= 13 && !_trainingOptionsOpen)) return false;
                    bool changed = _training.Choice(argument);
                    if (changed) Dirty();
                    return changed;
                }
                bool offlineChanged = _offline.Change(argument);
                if (offlineChanged) Dirty();
                return offlineChanged;
            case RmlUiIntentKind.OfflineOpenArena:
                if (_offline == null || _busy || _trainingOptionsOpen || !_offline.OpenArena()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineCancelArena:
                if (_offline == null || !_offline.CloseArena()) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineArenaSearch:
                if (_offline == null || !_offline.SearchArena(arenaSearch)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineArenaPage:
                if (_offline == null || !_offline.ArenaTurn(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineSelectArena:
                if (_offline == null || !_offline.SelectArena(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineLaunchMatch:
                if (_offline == null || _busy || _offlineLaunched || _offline.ArenaOpen || _trainingOptionsOpen || !_offline.Valid) return false;
                RmlUiOfflineRequest offlineRequest = _offline.Current;
                Begin(Job.Offline, "VALIDATING LOCAL MATCH AND MAP CAPABILITIES",
                    async token => await _services.ValidateOfflineAsync(offlineRequest, token)
                        .ConfigureAwait(false));
                _offlineInFlight = offlineRequest;
                return true;
            case RmlUiIntentKind.OfflineOpenTrainingOptions:
                if (_training == null || _offline == null || _offline.ArenaOpen || _trainingOptionsOpen) return false;
                _trainingOptionsOpen = true;
                Dirty();
                return true;
            case RmlUiIntentKind.OfflineCloseTrainingOptions:
                if (!_trainingOptionsOpen) return false;
                _trainingOptionsOpen = false;
                Dirty();
                return true;
            case RmlUiIntentKind.OfflineTrainingToggle:
                if (!_trainingOptionsOpen || _training == null
                    || !_training.Toggle(argument)) return false;
                Dirty(); return true;
            case RmlUiIntentKind.OfflineLaunchTraining:
                if (_training == null || _offline == null || _busy || _offline.ArenaOpen
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
                // Full advanced offline rules still use their established
                // transactional workspace until rules are migrated separately.
                if (!_services.NavigateTo(RmlUiRouteArgument.Offline)) return false;
                _offline = null;
                _training = null;
                _trainingOptionsOpen = false;
                Cancel();
                Dirty();
                return true;
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
                _mapPicker = null;
                if (_mapOpenedRules) CloseRules();
                _mapOpenedRules = false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyMapSelect:
                if (_mapPicker == null || _rules == null) return false;
                string? room = _mapPicker.Choice(argument);
                if (room == null) return false;
                string mapError = "";
                bool chosen = ChangeRules(editor => editor.TrySelectMap(room, out mapError));
                if (!chosen)
                {
                    if (mapError.Length > 0) { _rulesStatus = mapError; Dirty(); }
                    return false;
                }
                _mapPicker = null;
                _mapOpenedRules = false;
                Dirty();
                return true;
            case RmlUiIntentKind.LobbyAdminOpen:
                return OpenAdmin();
            case RmlUiIntentKind.LobbyPlayerSelect:
                return _admin != null
                    ? AdminAction(kind, argument, adminChat)
                    : OpenAdmin(argument);
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
            case RmlUiIntentKind.LobbyChatSend:
                return AdminAction(kind, argument, adminChat);
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
        while (_completed.TryDequeue(out Completion item))
        {
            // An older cancelled join must never end ownership of a newer one.
            if (item.Kind is Job.Join or Job.Create && item.Epoch == _joiningEpoch)
            {
                _joining = false;
                _creating = false;
            }
            if (item.Epoch != _epoch || _disposed) continue;
            _busy = false;
            _work = null;
            if (item.Error != null)
            {
                Status(item.Error);
                continue;
            }
            switch (item.Kind)
            {
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
                    if (item.Data is RmlUiServerCandidate selected)
                        Connect(selected.Endpoint, "");
                    else Status("No compatible public lobby is available.");
                    break;
                case Job.Join:
                case Job.Create:
                    var joined = (RmlUiJoinResult)item.Data!;
                    if (!joined.Joined)
                        Status(string.IsNullOrEmpty(joined.Error) ? "The server refused this connection." : joined.Error);
                    else if (_services.AcceptJoin(joined.Plan))
                    {
                        _browsing = false;
                        _createOpen = false;
                        Status("JOINED. WAITING FOR AUTHORITATIVE LOBBY STATE.");
                    }
                    else Status("Unable to complete the current client handoff.");
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
        _mapPicker = null;
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

    private bool OpenAdventure()
    {
        if (_services.Lobby().Active || _joining || _creating) return false;
        if (!_services.Hub().GameFilesReady)
            return _services.NavigateTo(RmlUiRouteArgument.Adventure);
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
        if (_adventure is not { } draft || draft.Launched) return false;
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

    private bool OpenOffline(bool training)
    {
        if (_services.Lobby().Active || _joining || _creating) return false;
        if (!_services.Hub().GameFilesReady)
            return _services.NavigateTo(RmlUiRouteArgument.Offline);
        if (_offline != null)
        {
            _trainingRoute = training;
            Dirty();
            return true;
        }
        var hub = _services.Hub();
        _offline = new RmlUiOfflineDraft(_services.BuiltInArenaKeys(),
            hub.DisplayHunter, hub.Suit, 1, 1);
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
                ? "SELECT A MAP // APPLY RULES TO SUBMIT TO SERVER"
                : "VIEW ONLY // THE LOBBY OWNER CONTROLS MAP SELECTION";
        }
        _mapPicker = new(_services.BuiltInArenaKeys(), _services.InstalledCommunityArenaKeys());
        _mapOpenedRules = created;
        Dirty();
        return true;
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
            _completed.Enqueue(new(epoch, kind, result, null));
        }
        catch (OperationCanceledException)
        {
            _completed.Enqueue(new(epoch, kind, null, "Request cancelled."));
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            _completed.Enqueue(new(epoch, kind, null, e.Message));
        }
        finally { cts.Dispose(); }
    }

    private void Cancel()
    {
        _offlineInFlight = null;
        _epoch++;
        try { _work?.Cancel(); }
        catch (ObjectDisposedException) { /* A completed worker retired its CTS. */ }
        _work = null;
        _busy = false;
        Dirty();
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

        Text("player_name", hub.PlayerName);
        Text("profile_state", hub.GameFilesReady ? "LOCAL PROFILE" : "SETUP REQUIRED");
        Text("hunter_name", hub.DisplayHunter.ToString().ToUpperInvariant());
        Text("game_data_state", hub.GameFilesReady ? "READY" : "NOT CONFIGURED");
        Text("build_version", version);
        Text("renderer_name", "OPENGL // RMLUI 6.3");
        Flag("lobby_mode", lobby.Active);
        Flag("multiplayer_mode", _browsing && !lobby.Active);
        Flag("adventure_mode", _adventure != null && !lobby.Active);
        Flag("adventure_confirmation_open", _adventure?.Confirming == true);
        Flag("adventure_can_resume", _adventure?.CanResume == true);
        Flag("adventure_can_new_run", _adventure?.CanStart == true);
        Text("adventure_hunter", _adventure?.Hunter.ToString().ToUpperInvariant() ?? "");
        Text("adventure_selected_slot", _adventure == null ? ""
            : $"SAVE SLOT {_adventure.SelectedSlot:00}");
        Text("adventure_save_detail", _adventure == null ? ""
            : _adventure.Describe(_adventure.Selected));
        Text("adventure_launch_status", _adventure?.Status ?? "");
        Flag("adventure_error_visible", _adventure?.Selected.Unreadable == true);
        Text("adventure_error_text", _adventure?.Selected.Unreadable == true
            ? "THIS SAVE IS UNREADABLE. ARCHIVE IT IN THE EXISTING SAVE MANAGER." : "");
        Text("adventure_file_problem", "");
        Text("adventure_overwrite_title", _adventure?.PendingOverwrite is { } pending
            ? $"REPLACE SAVE SLOT {pending.Slot:00}?" : "");
        Text("adventure_confirmation_error_text", "");
        for (int slot = 1; slot <= AdventureSave.SlotCount; slot++)
        {
            RmlUiAdventureSlot info = _adventure?.Slot(slot) ?? default;
            Text("adventure_slot" + slot + "_label", "SLOT " + slot.ToString("00")
                + " // " + (_adventure?.Describe(info) ?? "UNAVAILABLE"));
            Flag("adventure_slot" + slot + "_selected", _adventure?.SelectedSlot == slot);
        }
        Flag("offline_mode", _offline != null && !lobby.Active);
        Flag("offline_training_mode", _offline != null && _trainingRoute && !lobby.Active);
        Flag("offline_training_open", _offline != null && _trainingOptionsOpen && !lobby.Active);
        Flag("offline_arena_open", _offline?.ArenaOpen == true);
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
        Text("offline_summary", _offline == null ? "" :
            _offline.Mode + " // " + _offline.Bots + " // " + _offline.Difficulty);
        Text("offline_error", _offline == null ? "" : _status);
        Text("offline_availability", _offline?.Valid == true
            ? "READY // VALIDATION RUNS BEFORE LAUNCH" : "NO INSTALLED BUILT-IN ARENAS");
        Flag("offline_can_start", _offline?.Valid == true && !_busy && !_offlineLaunched);
        Flag("offline_training_can_start", _training != null && !_trainingLaunched
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
        Text("play_create_mode_name", MatchTypeCatalog.BaseLabel(_createMode));
        Text("play_create_host", _createDedicated ? "LOCAL // DEDICATED" : "HOSTED // ONLINE");
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
        Flag("lobby_require_ready", lobby.Active && lobby.RequireReady);
        Flag("lobby_local_ready", lobby.Active && lobby.LocalReady);
        Flag("lobby_starting", lobby.Active && lobby.Starting);
        Flag("lobby_rules_open", lobby.Active && _rules != null);
        Flag("lobby_map_open", lobby.Active && _mapPicker != null);
        Flag("lobby_hunter_open", lobby.Active && _hunterDraft.HasValue);
        Text("hunter_identity_name", _hunterDraft.HasValue
            ? _hunterChoice.ToString().ToUpperInvariant() + " // SUIT " + (_hunterSuit + 1) : "");
        Text("hunter_status", _hunterStatus);
        Text("hunter_restriction", _hunterDraft is { } selection && selection.Allowed.Length < 7
            ? "SERVER RESTRICTS AVAILABLE HUNTERS FOR THIS MATCH" : "CHOOSE YOUR HUNTER");
        Flag("hunter_apply_enabled", _hunterDraft is { CommandPending: false }
            && CanPickHunter(_hunterChoice));
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
        Flag("map_empty", _mapPicker == null || _mapPicker.Count == 0);
        Flag("map_can_previous", _mapPicker?.HasPrevious == true);
        Flag("map_can_next", _mapPicker?.HasNext == true);
        Text("map_page", _mapPicker == null ? "" : $"{_mapPicker.Page + 1} / {_mapPicker.Pages}");
        Text("map_status", !lobby.Owner ? "VIEW ONLY // THE LOBBY OWNER SELECTS MAPS"
            : _rulesSubmitted ? "WAITING FOR SERVER CONFIRMATION"
            : "CHOOSE AN INSTALLED MAP // APPLY IN THE RULES PANEL");
        Text("map_empty_text", _mapPicker?.Category == 1
            ? "NO VERIFIED COMMUNITY MAPS INSTALLED // DOWNLOAD FROM COMMUNITY"
            : "NO BUILT-IN MAPS AVAILABLE");
        for (int i = 0; i < RmlUiMapPickerModel.PageSize; i++)
        {
            string? room = _mapPicker?.Choice(i);
            Flag("map_choice" + i + "_visible", room != null);
            Flag("map_choice" + i + "_enabled", room != null && lobby.Owner && !_rulesSubmitted);
            Text("map_choice" + i + "_name", room ?? "");
            Text("map_choice" + i + "_hint", room == null ? ""
                : room.Equals(_rules?.Draft.Match.RoomKey, StringComparison.OrdinalIgnoreCase)
                    ? "SELECTED // CURRENT DRAFT"
                : _mapPicker?.Category == 1 ? "INSTALLED PACKAGE" : "BUILT-IN ARENA");
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
            for (int i = 0; i < 16; i++)
                Text("rules_toggle" + i, rules.IsOn(i) ? "ON" : "OFF");
        }
        Text("lobby_name", lobby.Active ? lobby.Name : "");
        Text("lobby_map", lobby.Active ? lobby.Map : "");
        Text("lobby_mode_name", lobby.Active ? lobby.Mode : "");
        Text("lobby_format", lobby.Active ? lobby.Format : "");
        Text("lobby_player_count", (lobby.Players?.Length ?? 0) + " / " + lobby.MaxPlayers);
        Text("lobby_ready_count", (lobby.Players?.Count(p => p.Ready) ?? 0) + " READY");
        Text("lobby_ready_action", lobby.LocalReady ? "UNREADY" : "READY");
        Text("lobby_local_hunter", hub.DisplayHunter.ToString().ToUpperInvariant());
        Text("lobby_status", lobby.Active ? lobby.Status : "");
        for (int i = 0; i < 8; i++)
        {
            var row = i < (lobby.Players?.Length ?? 0) ? lobby.Players[i] : default;
            Flag("slot" + i + "_occupied", i < (lobby.Players?.Length ?? 0));
            Flag("slot" + i + "_ready", row.Ready);
            Flag("slot" + i + "_local", row.Local);
            Text("slot" + i + "_name", row.Name ?? "");
            Text("slot" + i + "_hunter", row.Hunter ?? "");
            Text("slot" + i + "_state", row.Bot ? "BOT" : row.Ready ? "READY" : "WAITING");
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
        _disposed = true;
        _admin?.Close();
        _admin = null;
        Cancel();
        while (_completed.TryDequeue(out _)) { }
    }
}
