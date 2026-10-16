using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>One directory candidate. A directory hint is never an admitted network session.</summary>
public readonly record struct RmlUiServerCandidate(string Name, string Endpoint,
    int Players, int Capacity, int Ping, bool Compatible);
public readonly record struct RmlUiDirectory(bool Answered, string Message,
    RmlUiServerCandidate[] Servers);
public readonly record struct RmlUiJoinResult(bool Joined, LaunchPlan Plan, string Error);
public readonly record struct RmlUiLobbyPlayer(string Name, string Hunter, bool Ready,
    bool Local, bool Bot);
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
    bool AcceptJoin(LaunchPlan plan);
    bool OpenCreateLobby();
    bool NavigateTo(RmlUiRouteArgument destination);
    bool OpenStudio();
    bool SetReady();
    bool StartMatch();
    bool LeaveLobby();
    bool NextHunter();
    bool NextSuit();
    bool OpenLobbyDetails();
    RmlUiRuleDraft? ReadRules();
    IReadOnlyList<string> BuiltInArenaKeys();
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
    private enum Job { Browse, Quick, Join }
    private readonly record struct Completion(long Epoch, Job Kind, object? Data, string? Error);
    private readonly IRmlUiMenuServices _services;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Completion> _completed = new();
    private readonly RmlUiServerCandidate[] _rows = new RmlUiServerCandidate[8];
    private CancellationTokenSource? _work;
    private RmlUiRuleEditor? _rules;
    private bool _rulesSubmitted;
    private string _rulesStatus = "VIEWING SERVER RULES";
    private long _joiningEpoch;
    private long _epoch, _revision;
    private long _nextPoll;
    private bool _disposed, _busy, _joining, _browsing, _dirty = true;
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
    public (string Time, string Goal)? RuleInputs =>
        _rules is { } editor ? (editor.TimeInput(), editor.GoalInput()) : null;

    public bool Dispatch(RmlUiIntentKind kind, int argument = 0,
        string address = "", string playerName = "", string ruleTime = "", string ruleGoal = "")
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
                _browsing = true;
                Begin(Job.Quick, "FINDING A COMPATIBLE SERVER",
                    token => _services.QuickMatch(token).ContinueWith<object?>(
                        task => task.GetAwaiter().GetResult(), token));
                return true;
            case RmlUiIntentKind.PlayBrowse:
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
                Cancel();
                _browsing = false;
                Status("READY TO DEPLOY");
                return true;
            case RmlUiIntentKind.PlayCreateOpen:
                if (_services.OpenCreateLobby())
                {
                    Cancel();
                    _browsing = false;
                    Status("OPENING LOBBY CREATION");
                    return true;
                }
                Status("Lobby creation is unavailable until setup is complete.");
                return false;
            case RmlUiIntentKind.Navigate:
                if (argument == (int)RmlUiRouteArgument.Play)
                {
                    _browsing = true;
                    Dirty();
                    return true;
                }
                if (_services.NavigateTo((RmlUiRouteArgument)argument))
                {
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
            case RmlUiIntentKind.LobbyRulesOpen:
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
            case RmlUiIntentKind.LobbyAdminOpen:
            case RmlUiIntentKind.LobbyClassic:
                return LobbyAction(_services.OpenLobbyDetails,
                    "Opened the remaining authoritative lobby controls.");
            case RmlUiIntentKind.LobbyLeave:
                if (_services.LeaveLobby())
                {
                    CloseRules();
                    Cancel();
                    _browsing = false;
                    Status("LEFT THE LOBBY");
                    return true;
                }
                return false;
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
            if (item.Kind == Job.Join && item.Epoch == _joiningEpoch) _joining = false;
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
                    var joined = (RmlUiJoinResult)item.Data!;
                    if (!joined.Joined)
                        Status(string.IsNullOrEmpty(joined.Error) ? "The server refused this connection." : joined.Error);
                    else if (_services.AcceptJoin(joined.Plan))
                    {
                        _browsing = false;
                        Status("JOINED. WAITING FOR AUTHORITATIVE LOBBY STATE.");
                    }
                    else Status("Unable to complete the current client handoff.");
                    break;
            }
        }

        // The result of UpdateMatch and the authoritative SessionState may arrive
        // in different packets. Never show local rule edits as accepted until
        // the same authority advances revision AND echoes the exact payload.
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

        long now = Stopwatch.GetTimestamp();
        if (!_dirty && now < _nextPoll) return _snapshot;
        _nextPoll = now + Stopwatch.Frequency / 8;
        _dirty = false;
        var bindings = Project(_services.Hub(), _services.BuildVersion(), _services.Lobby());
        if (!Same(_snapshot.Bindings, bindings))
            _snapshot = new(++_revision, new ReadOnlyDictionary<string, RmlUiBindingValue>(bindings));
        return _snapshot;
    }

    private void CloseRules()
    {
        _rules = null;
        _rulesSubmitted = false;
        _rulesStatus = "VIEWING SERVER RULES";
        Dirty();
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
        Flag("play_browser_mode", _browsing && !lobby.Active);
        Flag("play_create_mode", false);
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
        Flag("rules_owner", lobby.Active && lobby.Owner);
        Flag("rules_dirty", _rules?.Dirty ?? false);
        Flag("rules_pending", _rulesSubmitted);
        Text("rules_status", _rulesStatus);
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
        Cancel();
        while (_completed.TryDequeue(out _)) { }
    }
}
