using System;
using System.Linq;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core
{
    /// <summary>
    /// Owner-thread lobby lifecycle shared by native and legacy presentation.
    /// The clock belongs to the engine/adapter; this service creates no timer.
    /// </summary>
    public sealed class LobbySessionController : IDisposable
    {
        private readonly ILobbySessionBackend _backend;
        private readonly LobbyContext? _context;
        private readonly Guid _lifetime = Guid.NewGuid();
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private LobbySnapshot _snapshot;
        private LobbyIntent? _submittedRules;
        private string _rulesError = "";
        private string _commandError = "";
        private bool _closed, _suspended, _closingLobby, _matchRequestIssued, _startAfterSave, _pumping;
        private bool _gameplayOwnsPump;
        private long? _lastPumpToken;
        private long _nextPumpToken;
        private double _rulesSentAt;
        private LobbyPumpOwner _pumpOwner;
        public event EventHandler<LaunchPlan>? MatchRequested;
        public event EventHandler<string>? Closed;
        public event Action<uint, MatchDefinition>? RulesConfirmed;
        public bool IsSuspended => _suspended;
        public bool IsClosed => _closed;
        public LobbyPumpOwner PumpOwner => _pumpOwner;
        public MatchDefinition? PendingRuleMatch => _submittedRules?.Match;

        public LobbySessionController(LobbyContext? context = null)
            : this(new NetLobbySessionBackend(), context) { }

        public LobbySessionController(ILobbySessionBackend backend, LobbyContext? context = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _context = context;
            _snapshot = Decorate(_backend.Capture()) with { Version = 1 };
        }

        public LobbySnapshot Snapshot()
        {
            AssertOwnerThread();
            LobbySnapshot next = Decorate(_backend.Capture());
            bool unchanged = next.Players.SequenceEqual(_snapshot.Players)
                && next.Chat.SequenceEqual(_snapshot.Chat)
                && (next with { Players = _snapshot.Players, Chat = _snapshot.Chat, Version = _snapshot.Version }) == _snapshot;
            if (!unchanged) _snapshot = next with { Version = _snapshot.Version + 1 };
            return _snapshot;
        }

        private LobbySnapshot Decorate(LobbySnapshot state) => state with
        {
            Lifetime = _lifetime, Context = _context, Closed = _closed, Suspended = _suspended,
            RulesPending = _submittedRules.HasValue, RulesError = _rulesError,
            CommandError = _commandError,
            ShouldLoadMatch = _backend.ShouldLoadMatch
        };

        public LobbyIntent Intent(LobbyIntentKind kind) => new(_lifetime, Snapshot().SessionRevision, kind);

        public void TransferPumpOwnership(LobbyPumpOwner owner)
        {
            AssertOwnerThread();
            if (_pumpOwner == owner) return;
            _pumpOwner = owner;
            _lastPumpToken = null;
        }

        public bool PumpOnce(LobbyPumpOwner owner)
        {
            AssertOwnerThread();
            return PumpOnce(owner, ++_nextPumpToken);
        }
        public bool PumpOnce(long frameToken) => PumpOnce(_pumpOwner, frameToken);

        /// <summary>Duplicate/reentrant callbacks and callbacks from the detached presenter cannot pump.</summary>
        public bool PumpOnce(LobbyPumpOwner owner, long frameToken)
        {
            AssertOwnerThread();
            if (_closed || _pumping || owner != _pumpOwner || (_lastPumpToken.HasValue && frameToken <= _lastPumpToken.Value)) return false;
            // InMatch is the server phase; a late join may still be prewarming.
            // Only the engine's explicit local-scene handoff yields this clock.
            if (_gameplayOwnsPump) return false;
            _lastPumpToken = frameToken;
            _nextPumpToken = Math.Max(_nextPumpToken, frameToken);
            _pumping = true;
            try
            {
                _backend.Pump();
                LobbySnapshot state = Snapshot();
                if (_backend.Refused || _backend.TimedOut || !state.Active)
                {
                    Leave(_closingLobby && !state.Active ? "Lobby closed."
                        : _backend.Refused ? _backend.RefusedMessage : "The connection to the server was lost.");
                    return true;
                }
                ObserveRules(state);
                if (_closed) return true;
                state = Snapshot();
                // A server may abort its loading barrier while this client is
                // still prewarming, before any local scene exists. Rearm that
                // cancelled handoff so the next start can load normally.
                if (state.Phase == SessionPhase.Lobby && _matchRequestIssued)
                {
                    _matchRequestIssued = false;
                    _suspended = false;
                }
                if (_closingLobby && !state.CommandPending && state.Message.Length > 0) _closingLobby = false;
                if (_startAfterSave && !_submittedRules.HasValue && !state.CommandPending)
                {
                    _startAfterSave = false;
                    if (_rulesError.Length == 0 && state.CanEdit)
                        _commandError = _backend.SendCommand(Intent(LobbyIntentKind.StartMatch))
                            ? "" : "The lobby is busy. Retry after the server responds.";
                }
                RequestMatchLoadIfNeeded();
                return true;
            }
            finally { _pumping = false; }
        }

        private void ObserveRules(LobbySnapshot state)
        {
            if (_submittedRules is not { } submitted) return;
            if (state.Match == submitted.Match && state.RuleFlags == submitted.RuleFlags)
            {
                _submittedRules = null;
                _rulesError = "";
                _backend.RulesAccepted(submitted.Match!.Value);
                RulesConfirmed?.Invoke(submitted.DraftVersion, submitted.Match.Value);
            }
            else if (!state.CommandPending && state.Message.Length > 0)
            {
                _submittedRules = null;
                _rulesError = state.Message;
                _startAfterSave = false;
            }
            else if (_backend.Clock - _rulesSentAt > (submitted.Match!.Value.MapIdentity.IsCustom ? 180 : 8))
            {
                _submittedRules = null;
                _rulesError = "The server did not confirm the rule changes. Review or retry.";
                _startAfterSave = false;
            }
        }

        private void RequestMatchLoadIfNeeded()
        {
            if (_closed || _matchRequestIssued || !_backend.ShouldLoadMatch || MatchRequested is not { } handler) return;
            LobbySnapshot state = Snapshot();
            if (state.Match is not { } match) return;
            _matchRequestIssued = true;
            _startAfterSave = false;
            _suspended = true;
            handler(this, new LaunchPlan { Kind = LaunchKind.Online, Hunter = state.LocalHunter,
                PlayerName = state.PlayerName, RoomKey = match.RoomKey, Mode = match.Mode,
                MatchRules = match, Lobby = _context, Spectate = state.PreferSpectator });
        }

        public LobbyActionResult Dispatch(LobbyIntent intent)
        {
            AssertOwnerThread();
            LobbyActionResult result = DispatchCore(intent);
            _commandError = result.Accepted ? "" : result.Message;
            return result;
        }

        private LobbyActionResult DispatchCore(LobbyIntent intent)
        {
            if (_closed || intent.Lifetime != _lifetime) return LobbyActionResult.Reject("This lobby is no longer active.");
            if (!Enum.IsDefined(intent.Kind)) return LobbyActionResult.Reject("Unknown lobby action.");
            LobbySnapshot state = Snapshot();
            if (intent.Kind == LobbyIntentKind.Leave) { Leave(intent.Text); return LobbyActionResult.Ok; }
            if (!state.Active || !state.Persistent) return LobbyActionResult.Reject("No active lobby.");
            if (intent.Kind == LobbyIntentKind.RetryMap) { _backend.RetryMap(); return LobbyActionResult.Ok; }
            if (intent.Kind == LobbyIntentKind.SendChat)
            {
                if (String.IsNullOrWhiteSpace(intent.Text)) return LobbyActionResult.Reject("Enter a message.");
                _backend.SendChat(intent.Text); return LobbyActionResult.Ok;
            }
            if (state.Phase != SessionPhase.Lobby) return LobbyActionResult.Reject("The lobby is starting or playing a match.");
            if (intent.ExpectedRevision != state.SessionRevision) return LobbyActionResult.Reject("The lobby changed. Review and retry.");
            if (intent.Kind == LobbyIntentKind.StartMatch && _submittedRules.HasValue)
            {
                if (!state.IsOwner) return LobbyActionResult.Reject("Only the lobby owner can start a match.");
                _startAfterSave = true; return LobbyActionResult.Ok;
            }
            if (state.CommandPending) return LobbyActionResult.Reject("Waiting for server acknowledgement.");
            switch (intent.Kind)
            {
                case LobbyIntentKind.ToggleSpectator:
                    _backend.SetSpectator(!state.PreferSpectator); return LobbyActionResult.Ok;
                case LobbyIntentKind.Identify:
                    if (!HunterRules.Pool(state.Match?.LowTier ?? false).Contains(intent.Hunter) || intent.Color > 3)
                        return LobbyActionResult.Reject("Invalid hunter or suit.");
                    _backend.Identify(intent.Hunter, intent.Color); return LobbyActionResult.Ok;
                case LobbyIntentKind.NextHunter:
                    Hunter[] pool = HunterRules.Pool(state.Match?.LowTier ?? false).ToArray();
                    if (pool.Length == 0) return LobbyActionResult.Reject("No available hunters.");
                    int index = Array.IndexOf(pool, state.LocalHunter);
                    _backend.Identify(pool[index < 0 ? 0 : (index + 1) % pool.Length], state.LocalColor);
                    return LobbyActionResult.Ok;
                case LobbyIntentKind.NextSuit:
                    _backend.Identify(state.LocalHunter, (byte)((state.LocalColor + 1) & 3)); return LobbyActionResult.Ok;
                case LobbyIntentKind.ToggleReady:
                    if (state.LocalSlot < 0) return LobbyActionResult.Reject("Waiting for a player slot.");
                    break;
                case LobbyIntentKind.SetTeam:
                    if (state.Match is not { } teamMatch || !GameState.IsTeamMode(teamMatch.Mode)
                        || intent.Team < -1 || intent.Team >= LobbyRules.ResolveTeamLayout(teamMatch).TeamCount
                        || !state.Players.Any(player => player.Slot == intent.TargetSlot && !player.IsSpectator)
                        || (!state.IsOwner && (intent.TargetSlot != state.LocalSlot || state.RuleFlags.HasFlag(LobbyRuleFlags.LockTeams))))
                        return LobbyActionResult.Reject("Team selection is unavailable.");
                    break;
                default:
                    if (!state.CanEdit) return LobbyActionResult.Reject("Only the lobby owner can perform this action.");
                    if (intent.Kind == LobbyIntentKind.UpdateRules)
                    {
                        if (intent.Match is not { } match) return LobbyActionResult.Reject("No rule draft.");
                        LobbyActionResult validation = _backend.ValidateRules(match);
                        if (!validation.Accepted) return validation;
                    }
                    if (intent.Kind is LobbyIntentKind.KickPlayer or LobbyIntentKind.TransferOwner)
                    {
                        if (intent.TargetSlot == state.LocalSlot || !state.Players.Any(player => player.Slot == intent.TargetSlot && !player.IsBot))
                            return LobbyActionResult.Reject("Select another player.");
                    }
                    if (intent.Kind == LobbyIntentKind.SetHandicap
                        && (!state.Players.Any(player => player.Slot == intent.TargetSlot)
                            || intent.DamageReduction > PlayerHandicap.MaxDamageReduction
                            || intent.DamageReduction % PlayerHandicap.Step != 0))
                        return LobbyActionResult.Reject("Select a player and a valid damage reduction.");
                    if (intent.Kind is LobbyIntentKind.UpdateBot or LobbyIntentKind.RemoveBot
                        && !state.Players.Any(player => player.Slot == intent.TargetSlot && player.IsBot))
                        return LobbyActionResult.Reject("Select an existing bot.");
                    if (intent.Kind is LobbyIntentKind.AddBot or LobbyIntentKind.UpdateBot
                        && (intent.Color > 3 || intent.BotLevel > 3
                            || (intent.Hunter != Hunter.Random && !HunterRules.Allowed(intent.Hunter, state.Match?.LowTier ?? false))))
                        return LobbyActionResult.Reject("Invalid bot hunter, suit or difficulty.");
                    break;
            }
            if (!_backend.SendCommand(intent)) return LobbyActionResult.Reject("The lobby is busy. Retry after the server responds.");
            if (intent.Kind == LobbyIntentKind.CloseLobby) _closingLobby = true;
            if (intent.Kind == LobbyIntentKind.UpdateRules)
            {
                _submittedRules = intent; _rulesSentAt = _backend.Clock; _rulesError = "";
            }
            return LobbyActionResult.Ok;
        }

        public void Resume()
        {
            AssertOwnerThread();
            if (_closed) return;
            _suspended = false;
            _gameplayOwnsPump = false;
            if (Snapshot().Phase == SessionPhase.Lobby) _matchRequestIssued = false;
        }
        public void Suspend() { AssertOwnerThread(); _suspended = true; }
        public void ClearRuleError() { AssertOwnerThread(); _rulesError = ""; }
        /// <summary>Called only after the engine has created the local match scene and taken the network clock.</summary>
        public void YieldPumpToGameplay() { AssertOwnerThread(); _gameplayOwnsPump = true; }
        public void Leave(string reason = "")
        {
            AssertOwnerThread();
            if (_closed) return;
            _closed = true; _submittedRules = null; _startAfterSave = false; _rulesError = ""; _commandError = "";
            _backend.Stop();
            Closed?.Invoke(this, reason);
        }
        /// <summary>
        /// Invalidate a controller after the engine has stopped or replaced its
        /// connection. Retirement must never stop a subsequently joined lobby.
        /// </summary>
        public void Retire()
        {
            AssertOwnerThread();
            _closed = true; _submittedRules = null; _startAfterSave = false; _rulesError = ""; _commandError = "";
            MatchRequested = null; Closed = null; RulesConfirmed = null;
        }
        public void Dispose() => Leave();
        private void AssertOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Lobby session operations must run on their owner thread.");
        }
    }
}
