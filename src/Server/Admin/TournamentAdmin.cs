using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MphRead.Identity;
using MphRead.Mods.Network;
using MphRead.Replay;
using MphRead.Reporting;

namespace MphRead.Admin;

public sealed record AdminParticipant(ulong ConnectionId, byte Slot, string Name, byte Team, bool Observer, bool Ready);
public sealed record TournamentStatus(DateTimeOffset ServerTimeUtc, uint Tick, Guid? MatchId, MatchPhase Phase, bool RosterLocked,
    bool BetweenRoundsPaused, bool StartRequested, string? TournamentId, string? RoundId,
    string RoomKey, string Preset, AdminParticipant[] Participants, ServerReplayStatus? Replay);

public sealed class TournamentAdmin
{
    private readonly Func<ServerSimulation> _simulation;
    private readonly ServerNetwork _network;
    private readonly Func<string?, string?, MatchRules> _resolve;
    private readonly Action<MatchRules> _select;
    private readonly Func<ServerReplaySession?> _record;
    private readonly Func<AdminCommand, AdminCommandResult> _execute;
    private readonly HashSet<ulong> _checked = new(), _ready = new();
    private bool _startRequested, _paused = true, _selectionPending, _recordingRequired;
    private ServerReplaySession? _replay;
    private string? _tournamentId, _roundId;
    private TournamentStatus _status;
    private MatchParticipantLedger? _configuredLedger;
    private string? _configuredTournament, _configuredRound;
    private Guid? _configuredReplay;
    private uint _lastSnapshotTick;
    private MatchPhase _lastSnapshotPhase;
    public AdminCommandQueue Commands { get; } = new();
    public TournamentStatus Status => Volatile.Read(ref _status);
    public bool RotationAllowed => !_paused;
    public bool StartRequested => _startRequested && !_paused;

    public TournamentAdmin(Func<ServerSimulation> simulation, ServerNetwork network,
        Func<string?, string?, MatchRules> resolve, Action<MatchRules> select, Func<ServerReplaySession?> record)
    {
        _simulation = simulation; _network = network; _resolve = resolve; _select = select; _record = record; _execute = Execute;
        _status = Snapshot(0, null);
    }

    public void BeforeStep(uint tick, ServerReplaySession? replay)
    {
        _replay = replay;
        if (_recordingRequired && _replay == null) _replay = _record();
        bool changed = Commands.Drain(tick, _execute);
        var simulation = _simulation();
        int count = 0; bool ready = true;
        foreach (var peer in _network.Peers)
        {
            if (peer == null) continue;
            count++;
            if (!_checked.Contains(peer.Connection.Id) || !_ready.Contains(peer.Connection.Id)) ready = false;
        }
        ready &= count == _checked.Count;
        simulation.AdminMayStart = _startRequested && !_paused && ready && (!_recordingRequired || _replay?.Ready == true);
        if (simulation.Reports is { Started: false } ledger
            && (_configuredLedger != ledger || _configuredTournament != _tournamentId || _configuredRound != _roundId || _configuredReplay != _replay?.ReplayId))
        {
            ledger.ConfigureRoundIdentity(_roundId == null ? null : _tournamentId, _roundId, _replay?.ReplayId);
            _configuredLedger = ledger; _configuredTournament = _tournamentId; _configuredRound = _roundId; _configuredReplay = _replay?.ReplayId;
        }
        if (changed || _lastSnapshotPhase != simulation.Scene.Match.Phase || unchecked(tick - _lastSnapshotTick) >= 15)
        {
            Volatile.Write(ref _status, Snapshot(tick, _replay?.Status));
            _lastSnapshotTick = tick; _lastSnapshotPhase = simulation.Scene.Match.Phase;
        }
    }

    public void NewRound()
    {
        _startRequested = false; _paused = true; _checked.Clear(); _ready.Clear(); _roundId = null; _selectionPending = false;
    }

    private AdminCommandResult Execute(AdminCommand command)
    {
        var simulation = _simulation(); var phase = simulation.Scene.Match.Phase;
        bool prestart = phase == MatchPhase.WaitingForPlayers;
        ServerPeer? peer = command.ConnectionId.HasValue
            ? _network.AllConnections.ToArray().FirstOrDefault(p => p?.Connection.Id == command.ConnectionId) : null;
        AdminCommandResult Reject(string message) => new(command.RequestId, "rejected", message);
        if (_selectionPending) return Reject("A selected map/rules transition is pending; wait for its new round.");
        switch (command.Kind)
        {
            case AdminCommandKind.LockRoster: _network.AdminRosterLocked = true; break;
            case AdminCommandKind.UnlockRoster: _network.AdminRosterLocked = false; break;
            case AdminCommandKind.AssignTeam:
                if (!prestart || peer == null || command.TeamIndex is not (0 or 1)) return Reject("Assign a live participant and team0 or1 before countdown.");
                _network.AssignAdminTeam(peer, (byte)command.TeamIndex.Value); _ready.Clear(); break;
            case AdminCommandKind.ForceSpectator:
                if (peer == null) return Reject("Connection is not present.");
                if (!_network.TryForceObserver(peer, out string reason)) return Reject(reason);
                _ready.Remove(peer.Connection.Id); break;
            case AdminCommandKind.SelectPreset:
                if (!prestart) return Reject("Map and rules changes require WaitingForPlayers; cancel countdown first.");
                _select(_resolve(command.RoomKey, command.Preset)); _selectionPending = true; _startRequested = false; _ready.Clear();
                return new(command.RequestId, "scheduled", "Validated map/rules transition is scheduled at this tick boundary.");
            case AdminCommandKind.ReadyCheck:
                if (!prestart) return Reject("Ready checks require WaitingForPlayers.");
                _checked.Clear(); _ready.Clear();
                foreach (var participant in _network.Peers) if (participant != null) _checked.Add(participant.Connection.Id);
                break;
            case AdminCommandKind.ConfirmReady:
                if (!prestart || peer == null || !_checked.Contains(peer.Connection.Id)) return Reject("Participant is not in the current prestart ready check.");
                _ready.Add(peer.Connection.Id); break;
            case AdminCommandKind.StartCountdown:
                if (!prestart || _checked.Count == 0 || !_checked.SetEquals(_ready)) return Reject("All participants in the current ready check must be confirmed.");
                if (_tournamentId == null || _roundId == null) return Reject("Set tournament and round identity before starting.");
                var humans = _network.Peers.ToArray().Where(p => p != null).ToArray();
                if (!_checked.SetEquals(humans.Select(p => p!.Connection.Id))
                    || humans.Any(p => p!.WaitingForNextMatch || p.Connection.State is not (NetConnectionState.Ready
                        or NetConnectionState.Playing or NetConnectionState.Lobby)))
                    return Reject("The checked roster changed or a participant is still loading.");
                if (!_network.LobbyAdmissionOpen)
                {
                    var bots = simulation.Bots.Participants.ToArray().Where(b => b != null).ToArray();
                    if (humans.Length + bots.Length < (simulation.Scene.Match.Rules.MaxPlayers == 1 ? 1 : 2)
                        || simulation.Scene.Match.Rules.Teams && humans.Select(p => (int)p!.TeamIndex)
                            .Concat(bots.Select(b => (int)b!.TeamIndex)).Distinct().Count() != 2)
                        return Reject("The configured mode does not yet have enough participants on its required teams.");
                }
                if (!simulation.ReportingMayStart || _recordingRequired && _replay?.Ready != true)
                    return Reject("Required result/replay storage is not ready.");
                _paused = false; _startRequested = true; break;
            case AdminCommandKind.CancelBeforeStart:
                if (phase is not (MatchPhase.WaitingForPlayers or MatchPhase.Countdown)) return Reject("A playing or completed match cannot be cancelled by prestart control.");
                _startRequested = false; _paused = true; _ready.Clear(); break;
            case AdminCommandKind.PauseBetweenRounds:
                _paused = true; break; // The active simulation continues; only the next start/rotation is held.
            case AdminCommandKind.Resume: _paused = false; break;
            case AdminCommandKind.Kick:
                if (peer == null) return Reject("Connection is not present.");
                _network.Remove(peer.ConnectionIndex, allowReconnect: false, reason: ParticipantExitReason.ExplicitLeave); break;
            case AdminCommandKind.Mute:
            case AdminCommandKind.Unmute:
                if (peer == null) return Reject("Connection is not present.");
                _network.SetAdminMuted(peer.Connection.Id, command.Kind == AdminCommandKind.Mute); break;
            case AdminCommandKind.ForceReplay:
                if (!prestart) return Reject("Mandatory replay must be enabled before countdown.");
                if ((_replay = _record()) == null) return Reject("Server replay directory is not configured or the previous recording is still closing.");
                _recordingRequired = true;
                return new(command.RequestId, "scheduled", "Mandatory recording is opening; inspect replay status before starting.");
            case AdminCommandKind.SetRoundIdentity:
                if (!prestart || command.TournamentId == null || command.RoundId == null
                    || !MatchReportV1.ValidExternalId(command.TournamentId) || !MatchReportV1.ValidExternalId(command.RoundId))
                    return Reject("Supply valid tournament and round identifiers before countdown.");
                _tournamentId = command.TournamentId; _roundId = command.RoundId; break;
            default: return Reject("Unknown command.");
        }
        return new(command.RequestId, "applied", "Command applied by the server owner.");
    }

    private TournamentStatus Snapshot(uint tick, ServerReplayStatus? replay)
    {
        var match = _simulation().Scene.Match;
        var participants = _network.AllConnections.ToArray().Where(p => p != null)
            .Select(p => new AdminParticipant(p!.Connection.Id, p.Slot, p.Name, p.TeamIndex, p.IsObserver, _ready.Contains(p.Connection.Id))).ToArray();
        return new(DateTimeOffset.UtcNow, tick, _simulation().Reports is { Started: true } ledger ? ledger.MatchId : null, match.Phase, _network.AdminRosterLocked, _paused,
            _startRequested, _tournamentId, _roundId, match.Rules.RoomKey, match.Rules.RulesetPreset.ToString(), participants, replay);
    }
}
