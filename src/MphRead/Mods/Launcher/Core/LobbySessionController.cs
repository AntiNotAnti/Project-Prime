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
        private (Hunter Hunter, byte Color, ushort Generation)? _requestedIdentity;
        private bool? _requestedSpectator;
        private double _identitySentAt, _identityRetryAt;
        private string _identityMessage = "";
        private double _spectatorSentAt, _spectatorRetryAt;
        private ushort _spectatorGeneration;
        private string _spectatorMessage = "";
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

        private LobbySnapshot Decorate(LobbySnapshot state)
        {
            LobbyPlayerSnapshot? local = state.Players.Where(player => player.Slot == state.LocalSlot)
                .Select(player => (LobbyPlayerSnapshot?)player).FirstOrDefault();
            return state with
            {
            Lifetime = _lifetime, Context = _context, Closed = _closed, Suspended = _suspended,
            RulesPending = _submittedRules.HasValue, RulesError = _rulesError,
            CommandError = _commandError,
            ShouldLoadMatch = _backend.ShouldLoadMatch,
            LocalHunter = _requestedIdentity?.Hunter ?? state.LocalHunter,
            LocalColor = _requestedIdentity?.Color ?? state.LocalColor,
            PreferSpectator = _requestedSpectator ?? state.PreferSpectator,
            IdentityPending = _requestedIdentity.HasValue, SpectatorPending = _requestedSpectator.HasValue,
            IdentityMessage = _identityMessage,
            SpectatorMessage = _spectatorMessage,
            AcknowledgedLocalHunter = local?.Hunter, AcknowledgedLocalColor = local?.Color,
            AcknowledgedSpectator = local?.IsSpectator
            };
        }

        public LobbyIntent Intent(LobbyIntentKind kind, byte targetSlot = byte.MaxValue)
        {
            LobbySnapshot state = Snapshot();
            ushort generation = state.Players.Where(player => player.Slot == targetSlot)
                .Select(player => player.Generation).FirstOrDefault();
            return new(_lifetime, state.SessionRevision, kind, TargetSlot: targetSlot,
                ExpectedRosterRevision: state.RosterRevision, TargetGeneration: generation);
        }

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
                ObserveIdentity(state);
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
                if (_startAfterSave && !_submittedRules.HasValue && !state.CommandPending
                    && !state.IdentityPending && !state.SpectatorPending)
                {
                    _startAfterSave = false;
                    if (_rulesError.Length == 0 && state.CanEdit)
                        Dispatch(Intent(LobbyIntentKind.StartMatch));
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

        private void ObserveIdentity(LobbySnapshot state)
        {
            LobbyPlayerSnapshot? local = state.Players.Where(player => player.Slot == state.LocalSlot)
                .Select(player => (LobbyPlayerSnapshot?)player).FirstOrDefault();
            if (_requestedIdentity is { } requested && local is { } player)
            {
                if (player.Generation != requested.Generation && requested.Generation != 0)
                {
                    _requestedIdentity = null;
                    _identityMessage = "The local player slot changed. Review Hunter selection.";
                }
                else if (player.Hunter == requested.Hunter && player.Color == requested.Color)
                {
                    _requestedIdentity = null;
                    _identityMessage = "Hunter and suit confirmed by server.";
                }
                else if (state.Phase == SessionPhase.Lobby && _backend.Clock - _identityRetryAt >= 1
                    && HunterRules.Allowed(requested.Hunter, state.Match?.LowTier ?? false))
                {
                    // Older roster packets can replace NetSession.LocalHunter.
                    // Retry through the existing Identify path until echoed.
                    _identityRetryAt = _backend.Clock;
                    _backend.Identify(requested.Hunter, requested.Color);
                }
            }
            if (_requestedIdentity.HasValue && (_backend.Clock - _identitySentAt > 8
                || !HunterRules.Allowed(_requestedIdentity.Value.Hunter, state.Match?.LowTier ?? false)))
            {
                _requestedIdentity = null;
                _identityMessage = "The server did not confirm this Hunter or suit. Review and retry.";
            }
            if (_requestedSpectator is { } spectator)
            {
                if (local is { } rolePlayer && _spectatorGeneration != 0 && rolePlayer.Generation != _spectatorGeneration)
                {
                    _requestedSpectator = null;
                    _spectatorMessage = "The local player slot changed. Review spectator selection.";
                }
                else if (local?.IsSpectator == spectator)
                {
                    _requestedSpectator = null;
                    _spectatorMessage = "Spectator role confirmed by server.";
                }
                else if (_backend.Clock - _spectatorSentAt > 8)
                {
                    _requestedSpectator = null;
                    _spectatorMessage = "The server did not confirm spectator selection. Review and retry.";
                }
                else if (state.Phase == SessionPhase.Lobby && _backend.Clock - _spectatorRetryAt >= 1)
                {
                    _spectatorRetryAt = _backend.Clock;
                    _backend.SetSpectator(spectator);
                }
            }
        }

        private void RequestIdentity(LobbySnapshot state, Hunter hunter, byte color)
        {
            ushort generation = state.Players.Where(player => player.Slot == state.LocalSlot)
                .Select(player => player.Generation).FirstOrDefault();
            _requestedIdentity = (hunter, color, generation);
            _identitySentAt = _identityRetryAt = _backend.Clock;
            _identityMessage = "Waiting for server to confirm Hunter and suit.";
            _backend.Identify(hunter, color);
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
                if (intent.Text.Length > ChatPacket.MaxTextBytes)
                    return LobbyActionResult.Reject($"Chat messages are limited to {ChatPacket.MaxTextBytes} characters.");
                if (intent.Text.Any(character => character < 32 || character > 126))
                    return LobbyActionResult.Reject("This server protocol supports printable ASCII chat only.");
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
            if (intent.Kind is LobbyIntentKind.SetTeam or LobbyIntentKind.SetHandicap or LobbyIntentKind.KickPlayer
                or LobbyIntentKind.TransferOwner or LobbyIntentKind.RemoveBot or LobbyIntentKind.UpdateBot)
            {
                LobbyPlayerSnapshot? target = state.Players.Where(player => player.Slot == intent.TargetSlot)
                    .Select(player => (LobbyPlayerSnapshot?)player).FirstOrDefault();
                if (target == null) return LobbyActionResult.Reject("That player has left. Reselect a player.");
                if (intent.ExpectedRosterRevision != state.RosterRevision
                    || target.Value.Generation != intent.TargetGeneration)
                    return LobbyActionResult.Reject("The roster changed. Reselect the player before retrying.");
            }
            switch (intent.Kind)
            {
                case LobbyIntentKind.ToggleSpectator:
                    if (_requestedSpectator.HasValue) return LobbyActionResult.Reject("Waiting for server to confirm spectator selection.");
                    if (state.LocalSlot < 0) return LobbyActionResult.Reject("Waiting for a player slot.");
                    _requestedSpectator = !state.PreferSpectator;
                    _spectatorSentAt = _spectatorRetryAt = _backend.Clock;
                    _spectatorGeneration = state.Players.Where(player => player.Slot == state.LocalSlot)
                        .Select(player => player.Generation).FirstOrDefault();
                    _spectatorMessage = "Waiting for server to confirm spectator selection.";
                    _backend.SetSpectator(_requestedSpectator.Value); return LobbyActionResult.Ok;
                case LobbyIntentKind.Identify:
                    if (!state.Players.Any(player => player.Slot == state.LocalSlot))
                        return LobbyActionResult.Reject("Waiting for the server's player roster.");
                    if (!HunterRules.Pool(state.Match?.LowTier ?? false).Contains(intent.Hunter) || intent.Color > 3)
                        return LobbyActionResult.Reject("Invalid hunter or suit.");
                    RequestIdentity(state, intent.Hunter, intent.Color); return LobbyActionResult.Ok;
                case LobbyIntentKind.NextHunter:
                    if (!state.Players.Any(player => player.Slot == state.LocalSlot))
                        return LobbyActionResult.Reject("Waiting for the server's player roster.");
                    Hunter[] pool = HunterRules.Pool(state.Match?.LowTier ?? false).ToArray();
                    if (pool.Length == 0) return LobbyActionResult.Reject("No available hunters.");
                    int index = Array.IndexOf(pool, state.LocalHunter);
                    RequestIdentity(state, pool[index < 0 ? 0 : (index + 1) % pool.Length], state.LocalColor);
                    return LobbyActionResult.Ok;
                case LobbyIntentKind.NextSuit:
                    if (!state.Players.Any(player => player.Slot == state.LocalSlot))
                        return LobbyActionResult.Reject("Waiting for the server's player roster.");
                    RequestIdentity(state, state.LocalHunter, (byte)((state.LocalColor + 1) & 3)); return LobbyActionResult.Ok;
                case LobbyIntentKind.ToggleReady:
                    if (state.LocalSlot < 0) return LobbyActionResult.Reject("Waiting for a player slot.");
                    if (!state.RuleFlags.HasFlag(LobbyRuleFlags.RequireReady))
                        return LobbyActionResult.Reject("This lobby does not require ready confirmation.");
                    if (state.PreferSpectator || state.SpectatorPending
                        || state.Players.Any(player => player.Slot == state.LocalSlot && player.IsSpectator))
                        return LobbyActionResult.Reject("Spectators do not ready for combat.");
                    break;
                case LobbyIntentKind.SetTeam:
                    if (state.Match is not { } teamMatch || !GameState.IsTeamMode(teamMatch.Mode)
                        || intent.Team < -1 || intent.Team >= LobbyRules.ResolveTeamLayout(teamMatch).TeamCount
                        || !state.Players.Any(player => player.Slot == intent.TargetSlot && !player.IsSpectator)
                        || (!state.IsOwner && (intent.TargetSlot != state.LocalSlot || state.RuleFlags.HasFlag(LobbyRuleFlags.LockTeams))))
                        return LobbyActionResult.Reject("Team selection is unavailable.");
                    if (intent.Team >= 0 && state.Players.Count(player => player.Slot != intent.TargetSlot
                        && !player.IsSpectator && player.Team == intent.Team) >= LobbyRules.TeamCapacity(teamMatch, intent.Team))
                        return LobbyActionResult.Reject("That team is full. Choose another team or Auto.");
                    break;
                default:
                    if (!state.CanEdit) return LobbyActionResult.Reject("Only the lobby owner can perform this action.");
                    if (intent.Kind == LobbyIntentKind.StartMatch)
                    {
                        LobbyPresentation presentation = LobbyPresentation.From(state);
                        if (!presentation.CanStart) return LobbyActionResult.Reject(presentation.StartReason);
                    }
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
                            || intent.DamageReduction > PlayerHandicap.MaxDamageReduction
                            || intent.DamageReduction % PlayerHandicap.Step != 0
                            || (intent.Hunter != Hunter.Random && !HunterRules.Allowed(intent.Hunter, state.Match?.LowTier ?? false))))
                        return LobbyActionResult.Reject("Invalid bot hunter, suit or difficulty.");
                    if (intent.Kind == LobbyIntentKind.AddBot && state.Players.Length >= state.MaxPlayers)
                        return LobbyActionResult.Reject("The lobby is full. Remove a bot or wait for a player to leave.");
                    if (intent.Kind is LobbyIntentKind.AddBot or LobbyIntentKind.UpdateBot)
                    {
                        if (state.Match is not { } botMatch || intent.Team < -1
                            || (intent.Team >= 0 && intent.Team >= LobbyRules.TeamCount(botMatch)))
                            return LobbyActionResult.Reject("Choose a team for the current match format.");
                        if (intent.Team >= 0 && state.Players.Count(player => player.Slot != intent.TargetSlot
                            && !player.IsSpectator && player.Team == intent.Team) >= LobbyRules.TeamCapacity(botMatch, intent.Team))
                            return LobbyActionResult.Reject("That team is full. Choose another team or Auto.");
                    }
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
            _requestedIdentity = null; _requestedSpectator = null; _identityMessage = "";
            _spectatorMessage = "";
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
            _requestedIdentity = null; _requestedSpectator = null; _identityMessage = "";
            _spectatorMessage = "";
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
