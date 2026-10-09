using System;
using MphRead.Mods.Multiplayer;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Network
{
    public sealed partial class DedicatedServer
    {
        private ServerSessionPolicy _sessionPolicy;
        public ServerSessionPolicy SessionPolicy { get => _sessionPolicy; init => _sessionPolicy = value; }
        public MatchFormat Format { get; init; } = MatchFormat.Auto;
        public bool RequireReady { get; private set; } = false;
        public bool AllowJoinInProgress { get; private set; } = true;
        public Guid OwnerToken { get; set; }
        public void SetSessionOptions(bool requireReady, bool allowJoinInProgress)
        { RequireReady = requireReady; AllowJoinInProgress = allowJoinInProgress; }
        private SessionPhase _phase = SessionPhase.InMatch;
        private MatchDefinition _lobbyMatch, _frozenMatch;
        private MatchWorldProfile _frozenWorldProfile;
        public bool LockTeams { get; private set; }
        private ushort _sessionRevision = 1;
        private ushort _mapGeneration = 1;
        private void InvalidateMapReadiness()
        {
            _mapGeneration = NetLifecycleTracker.Next(_mapGeneration);
            foreach(var peer in _peers) {peer.PreparedMap=default;peer.MapAvailability=MapAvailabilityState.Unknown;peer.LastMapAvailability=double.NegativeInfinity;}
        }
        private uint _lobbyOwnerClientId;
        // Only a lobby owner authenticated with the launcher-generated owner token
        // may terminate the server process itself. An ordinary first-player owner on
        // a persistent dedicated server may close/reset the current lobby, not the daemon.
        private uint _processOwnerClientId;
        private readonly NetMatchStart _start = new();
        private bool _checkingLoadBarrier;
        private double _lastStartCommitBroadcast;
        private MatchStartIdentity CurrentStartIdentity => new(_matchId, _authorityEpoch, _start.Identity.StartGeneration);

        private MatchDefinition CurrentDefinition => SessionPolicy == ServerSessionPolicy.Lobby
            ? (_phase == SessionPhase.Lobby ? _lobbyMatch : _frozenMatch)
            : DefinitionFor(_rotation.Current);

        private MatchDefinition DefinitionFor(RotationEntry entry) => new MatchDefinition()
        {
            RoomKey = entry.RoomKey, MapIdentity = NetworkMapIdentity.ForRoom(entry.RoomKey), Mode = entry.Mode, Format = Format,
            TimeLimitSeconds = (ushort)Math.Clamp(entry.TimeLimit, 0, ushort.MaxValue),
            PointGoal = (ushort)Math.Clamp(entry.PointGoal, 0, ushort.MaxValue),
            FriendlyFire = FriendlyFire, AffinityWeapons = AffinityWeapons, EnhancedHunters = EnhancedHunters, ShadowFreeze = ShadowFreeze,
            HideOpponentHealth = true, DisablePowerups = true, SpawnProtection = SpawnProtection,
            Fiesta = Fiesta, OneInTheChamber = OneInTheChamber, InstaGib = InstaGib, LowTier = LowTier, NoImperialist = NoImperialist,
            BalancedMode = BalancedMode,
            OctolithAutoReset = Mods.Multiplayer.MatchModifierRules.UsesOctolith(entry.Mode) && OctolithAutoReset
        }.NormalizeLegacy();

        private void InvalidateLobbyReady()
        {
            foreach (Peer peer in _peers) peer.LobbyReady = false;
        }

        private void TouchLobbyRevision(string reason)
        {
            ushort previous = _sessionRevision++;
            Log($"[lobby] revision {previous} -> {_sessionRevision}: {reason}");
            BroadcastSessionState();
            BroadcastRoster();
        }

        private void SetPhase(SessionPhase phase)
        {
            Log($"[lobby] phase {_phase} -> {phase}, match {_matchId}");
            _phase = phase;
            Telemetry.ProductionTelemetry.Emit(new(Telemetry.TelemetryEventType.Lifecycle, NetSession.NetFrame, Result: 128 + (int)phase));
            TouchLobbyRevision("phase changed");
        }

        private SessionStatePacket BuildSessionState()
        {
            var availability = new MapAvailabilityState[8];
            byte ownerSlot = byte.MaxValue;
            NetworkMapIdentity required = CurrentDefinition.MapIdentity;
            foreach (Peer peer in _peers)
            {
                if (peer.ClientId != 0 && peer.ClientId == _lobbyOwnerClientId)
                    ownerSlot = (byte)peer.SlotIndex;
                if ((uint)peer.SlotIndex < availability.Length
                    && peer.PreparedMap == required)
                    availability[peer.SlotIndex] = peer.MapAvailability;
            }

            return new SessionStatePacket
            {
                Phase = _phase, Policy = SessionPolicy, Revision = _sessionRevision,
                MatchId = _matchId, AuthorityEpoch = _authorityEpoch,
                MapGeneration = _mapGeneration,
                OwnerSlot = _lobbyOwnerClientId == 0 ? byte.MaxValue : ownerSlot,
                MaxPlayers = (byte)_maxPlayers, Match = CurrentDefinition,
                MapDownloadSource = CurrentDefinition.MapIdentity.IsCustom ? MapDownloadSource : "",
                StockGameplayHash = !CurrentDefinition.MapIdentity.IsCustom && !_controlPlaneOnlyForTests
                    ? _stockGameplayHashes.GetValueOrDefault(CurrentDefinition.RoomKey) : default,
                MapAvailability = availability,
                WorldProfile = SessionPolicy == ServerSessionPolicy.Lobby && _phase != SessionPhase.Lobby
                    ? _frozenWorldProfile : LobbyRules.ResolveWorldProfile(CurrentDefinition, _maxPlayers),
                RuleFlags = (CurrentDefinition.HideOpponentHealth
                        ? LobbyRuleFlags.HideOpponentHealth : 0)
                    | (RequireReady ? LobbyRuleFlags.RequireReady : 0)
                    | (AllowJoinInProgress ? LobbyRuleFlags.AllowJoinInProgress : 0)
                    | (LockTeams ? LobbyRuleFlags.LockTeams : 0),
                ExpectedParticipants = _start.Expected,
                LoadedParticipants = _start.Loaded,
                StartGeneration = _start.Identity.StartGeneration,
                StartStage = _start.Stage,
                WorldReadyParticipants = _start.WorldReady,
                StartCountdownMilliseconds = _start.RemainingMilliseconds(_now)
            };
        }

        private void BroadcastSessionState(int copies = 1)
        {
            copies = Math.Clamp(copies, 1, 3);
            var state = BuildSessionState();
            if (_sim != null) NetSession.ApplySessionState(state);
            state.Write(_scratch);
            foreach (Peer peer in _peers)
                _transport?.Send(peer.EndPoint, PacketType.SessionState,
                    _scratch.AsSpan(0, SessionStatePacket.Size), immediateCopies: copies);
        }

        private sbyte ChooseTeam(MatchDefinition match, Peer? exclude = null)
        {
            TeamLayout layout = LobbyRules.ResolveTeamLayout(match);
            Span<int> counts = stackalloc int[4];
            counts.Clear();
            foreach (Peer peer in _peers)
                if (!peer.Spectating && peer != exclude && peer.TeamIndex >= 0 && peer.TeamIndex < layout.TeamCount) counts[peer.TeamIndex]++;
            foreach (var bot in _bots)
                if (bot.TeamIndex >= 0 && bot.TeamIndex < layout.TeamCount) counts[bot.TeamIndex]++;
            return TeamRules.ChooseTeam(layout, counts);
        }

        private void NormalizeTeams()
        {
            foreach (Peer peer in _peers) peer.TeamIndex = -1;
            foreach (var bot in _bots) bot.TeamIndex = -1;
            foreach (Peer peer in _peers)
                if (!peer.Spectating) peer.TeamIndex = ChooseTeam(CurrentDefinition, peer);
            foreach (var bot in _bots) bot.TeamIndex = ChooseTeam(CurrentDefinition);
        }

        private void ClaimOwner(Peer peer, ReadOnlySpan<byte> hello)
        {
            if (SessionPolicy != ServerSessionPolicy.Lobby || _lobbyOwnerClientId != 0 || peer.ClientId == 0) return;
            bool tokenMatches = OwnerToken != Guid.Empty && hello.Length >= 22
                && new Guid(hello.Slice(6, 16)) == OwnerToken;
            if (OwnerToken != Guid.Empty && !tokenMatches) return;
            _lobbyOwnerClientId = peer.ClientId;
            if (tokenMatches)
                _processOwnerClientId = peer.ClientId;
            OwnerToken = Guid.Empty;
            if (!_controlPlaneOnlyForTests)
            {
                BeginServerHotPathPrewarm();
                Mods.RoomPrewarm.Begin(_lobbyMatch.RoomKey);
            }
            TouchLobbyRevision($"owner = slot {peer.SlotIndex}");
        }

        private void HandleLobbyCommand(ReceivedPacket packet, double now)
        {
            Peer? peer = Find(packet.Sender);
            if (peer == null || !LobbyCommandPacket.TryRead(packet.Payload, out var command)) return;
            peer.LastSeen = now;
            if (!peer.Commands.TryGetValue(command.CommandId, out var result))
            {
                if (TryPrepareLobbyMap(peer, command)) return;
                var code = ExecuteLobbyCommand(peer, command, now, out string reason);
                result = CacheLobbyResult(peer, command, code, reason);
            }
            ReplyLobbyCommand(peer, command, result);
        }

        private LobbyCommandResultPacket CacheLobbyResult(Peer peer, LobbyCommandPacket command,
            LobbyResultCode code, string reason)
        {
            var result = new LobbyCommandResultPacket { CommandId = command.CommandId,
                ResultCode = code, CurrentRevision = _sessionRevision, Reason = reason };
            if (peer.Commands.Count >= 64) peer.Commands.Remove(peer.CommandOrder.Dequeue());
            peer.Commands.Add(command.CommandId, result);
            peer.CommandOrder.Enqueue(command.CommandId);
            if (code != LobbyResultCode.Ok) Log($"[lobby] slot {peer.SlotIndex} {command.Type} denied: {reason}");
            return result;
        }

        private void ReplyLobbyCommand(Peer peer, LobbyCommandPacket command, LobbyCommandResultPacket result)
        {
            result.Write(_scratch);
            _transport?.Send(peer.EndPoint, PacketType.LobbyCommandResult, _scratch.AsSpan(0, LobbyCommandResultPacket.Size));
            if (result.ResultCode == LobbyResultCode.Ok && command.Type == LobbyCommandType.CloseLobby)
            {
                // Acknowledge first. Otherwise the owner sees a disconnect and cannot
                // distinguish a successful close from the server simply disappearing.
                CloseLobbySession(peer);
                return;
            }
            // Also repairs a lost state/roster even if the original command succeeded.
            BroadcastSessionState();
            BroadcastRoster();
        }

        private LobbyResultCode ValidateLobbyCommandAccess(Peer peer, LobbyCommandPacket command, out string reason)
        {
            reason = "";
            bool botCommand = command.Type is LobbyCommandType.AddBot or LobbyCommandType.RemoveBot or LobbyCommandType.UpdateBot;
            bool returnCommand = command.Type == LobbyCommandType.ReturnToLobby
                && _phase is SessionPhase.Starting or SessionPhase.InMatch or SessionPhase.PostMatch;
            if (SessionPolicy != ServerSessionPolicy.Lobby || (_phase != SessionPhase.Lobby
                && !(botCommand && _phase == SessionPhase.InMatch && _matchEndedAt < 0)
                && !returnCommand))
            { reason = "Wait until the server returns to the lobby."; return LobbyResultCode.InvalidPhase; }
            bool owner = peer.ClientId == _lobbyOwnerClientId && peer.ClientId != 0;
            if (command.Type is not LobbyCommandType.SetReady and not LobbyCommandType.SetTeam && !owner)
            { reason = "Only the lobby owner can do that."; return LobbyResultCode.NotOwner; }
            if (command.ExpectedRevision != _sessionRevision)
            { reason = "The lobby changed. Review the updated settings and try again."; return LobbyResultCode.StaleRevision; }
            return LobbyResultCode.Ok;
        }

        private LobbyResultCode ExecuteLobbyCommand(Peer peer, LobbyCommandPacket command, double now, out string reason)
        {
            var access = ValidateLobbyCommandAccess(peer, command, out reason);
            if (access != LobbyResultCode.Ok) return access;
            bool owner = peer.ClientId == _lobbyOwnerClientId && peer.ClientId != 0;
            switch (command.Type)
            {
                case LobbyCommandType.AddBot:
                case LobbyCommandType.UpdateBot:
                    var botResult = ConfigureBot(command, command.Type == LobbyCommandType.UpdateBot, out reason);
                    if (botResult != LobbyResultCode.Ok) return botResult;
                    break;
                case LobbyCommandType.RemoveBot:
                    if (!RemoveBot(command.TargetSlot))
                    { reason = "That bot has left."; return LobbyResultCode.TargetNotFound; }
                    break;
                case LobbyCommandType.SetReady:
                    if (peer.Spectating)
                    { reason = "Spectators do not ready for combat."; return LobbyResultCode.InvalidConfiguration; }
                    peer.LobbyReady = command.Ready;
                    break;
                case LobbyCommandType.SetTeam:
                    var teamBot = FindBot(command.TargetSlot);
                    if (teamBot != null)
                    {
                        if (!owner) { reason = "Only the owner can move a bot."; return LobbyResultCode.NotOwner; }
                        command.Hunter = teamBot.Hunter; command.Color = teamBot.Color; command.BotLevel = teamBot.BotLevel;
                        var moveResult = ConfigureBot(command, update: true, out reason);
                        if (moveResult != LobbyResultCode.Ok) return moveResult;
                        break;
                    }
                    Peer? target = _peers.Find(p => p.SlotIndex == command.TargetSlot);
                    if (target == null) { reason = "That player has left."; return LobbyResultCode.TargetNotFound; }
                    if (target.Spectating)
                    { reason = "Spectators do not occupy a combat team."; return LobbyResultCode.InvalidTeam; }
                    if (target != peer && !owner) { reason = "Only the owner can move another player."; return LobbyResultCode.NotOwner; }
                    if (LockTeams && !owner) { reason = "Team changes are locked by the owner."; return LobbyResultCode.NotOwner; }
                    sbyte requestedTeam = command.TeamIndex == -1 ? ChooseTeam(_lobbyMatch, target) : command.TeamIndex;
                    if (command.TeamIndex < -1 || requestedTeam < 0 || requestedTeam >= LobbyRules.TeamCount(_lobbyMatch))
                    { reason = "Choose a team for the current format."; return LobbyResultCode.InvalidTeam; }
                    if (TeamOccupants(requestedTeam, target.SlotIndex) >= LobbyRules.TeamCapacity(_lobbyMatch, requestedTeam))
                    { reason = "That team is full."; return LobbyResultCode.TeamFull; }
                    target.TeamIndex = requestedTeam;
                    target.LobbyReady = false;
                    break;
                case LobbyCommandType.SetHandicap:
                    if (!PlayerHandicap.IsValid(command.DamageReduction))
                    { reason = "Choose a damage reduction from 0% to 50% in 10% steps."; return LobbyResultCode.InvalidConfiguration; }
                    var handicapBot = FindBot(command.TargetSlot);
                    if (handicapBot != null)
                    {
                        handicapBot.DamageReduction = command.DamageReduction;
                        break;
                    }
                    Peer? handicapTarget = _peers.Find(p => p.SlotIndex == command.TargetSlot);
                    if (handicapTarget == null)
                    { reason = "That player has left."; return LobbyResultCode.TargetNotFound; }
                    handicapTarget.DamageReduction = command.DamageReduction;
                    handicapTarget.LobbyReady = false;
                    break;
                case LobbyCommandType.UpdateMatch:
                    var proposed = command.Configuration.Match;
                    var valid = LobbyRules.ValidateDefinition(proposed, out reason);
                    if (valid != LobbyResultCode.Ok) return valid;
                    string? room = ResolveRoomKey(proposed.RoomKey);
                    if (room == null) { reason = "The server does not have that map."; return LobbyResultCode.MapUnavailable; }
                    TeamLayout proposedLayout = LobbyRules.ResolveTeamLayout(proposed);
                    if (proposedLayout.TeamCount > 0 && (proposedLayout.TotalPlayers < CombatantCount
                        || (LobbyRules.ExactTeams(proposed) && proposedLayout.TotalPlayers > _maxPlayers)))
                    { reason = "The layout must fit the connected roster and server player limit."; return LobbyResultCode.InvalidConfiguration; }
                    bool topologyChanged = proposedLayout != LobbyRules.ResolveTeamLayout(_lobbyMatch);
                    NetworkMapIdentity required;
                    try
                    {
                        NetworkMapIdentity.StageRoom(room);
                        required = NetworkMapIdentity.ForRoom(room);
                        if (!required.IsCustom && !_controlPlaneOnlyForTests) RememberStockGameplayHash(room);
                    }
                    catch (Exception ex) { reason = ex.Message; return LobbyResultCode.MapUnavailable; }
                    if (proposed.MapIdentity.IsCustom && proposed.MapIdentity != required)
                    { reason = "The server does not have the requested package version."; return LobbyResultCode.MapUnavailable; }
                    if (_bots.Count > 0 && !BotMapAvailable(proposed with { RoomKey = room, MapIdentity = required }, out reason))
                        return LobbyResultCode.MapUnavailable;
                    if (_lobbyMatch.MapIdentity != required || _lobbyMatch.RoomKey != room) InvalidateMapReadiness();
                    _lobbyMatch = proposed with { RoomKey = room, MapIdentity = required };
                    if (!_controlPlaneOnlyForTests) Mods.RoomPrewarm.Begin(_lobbyMatch.RoomKey);
                    RequireReady = command.Configuration.RequireReady;
                    AllowJoinInProgress = command.Configuration.AllowJoinInProgress;
                    LockTeams = command.Configuration.LockTeams;
                    foreach (var participant in _peers) participant.Hunter = (byte)HunterRules.Sanitize((Hunter)participant.Hunter, _lobbyMatch.LowTier);
                    foreach (var bot in _bots) bot.Hunter = (byte)HunterRules.Sanitize((Hunter)bot.Hunter, _lobbyMatch.LowTier);
                    if (topologyChanged) NormalizeTeams();
                    InvalidateLobbyReady();
                    break;
                case LobbyCommandType.StartMatch:
                    var start = LobbyRules.Validate(_lobbyMatch, BuildRoster(), RequireReady, out reason);
                    if (start != LobbyResultCode.Ok) return start;
                    if (!BeginLobbyMatch(_lobbyMatch, now, out reason))
                        return LobbyResultCode.MapUnavailable;
                    break;
                case LobbyCommandType.ReturnToLobby:
                    if (_phase == SessionPhase.Lobby)
                    { reason = "The server is already in the lobby."; return LobbyResultCode.InvalidPhase; }
                    // A persistent lobby is one session-wide phase. There is no
                    // honest client-only lobby while everyone else remains in
                    // the same active match, so the owner action deliberately
                    // unwinds the match for every connected participant.
                    EnterLobby(_frozenMatch);
                    return LobbyResultCode.Ok;
                case LobbyCommandType.CloseLobby:
                    // The actual close runs after the command result is sent.
                    break;
                case LobbyCommandType.KickPlayer:
                case LobbyCommandType.TransferOwner:
                    Peer? selected = _peers.Find(p => p.SlotIndex == command.TargetSlot);
                    if (selected == null || selected == peer) { reason = "Choose another connected player."; return LobbyResultCode.TargetNotFound; }
                    if (command.Type == LobbyCommandType.TransferOwner)
                    {
                        bool transfersProcess = _processOwnerClientId != 0
                            && _processOwnerClientId == peer.ClientId;
                        _lobbyOwnerClientId = selected.ClientId;
                        if (transfersProcess)
                        {
                            // A launcher-created lobby belongs to the session, not
                            // permanently to the first player. Hand the process
                            // lifetime to the new owner too, or Close Lobby would
                            // leave an ownerless child server behind.
                            _processOwnerClientId = selected.ClientId;
                        }
                    }
                    else
                    {
                        SendRefusal(selected.EndPoint, RefusedPacket.ReasonKicked);
                        Remove(selected, "removed by lobby owner");
                    }
                    break;
            }
            TouchLobbyRevision($"slot {peer.SlotIndex}: {command.Type}");
            return LobbyResultCode.Ok;
        }

        /// <summary>
        /// Stop every piece of per-match server state before the persistent
        /// session becomes a lobby again. ServerSim.Stop owns the static
        /// NetSession teardown; the replay writer and verdict callback live
        /// outside it and have to be finalized explicitly.
        /// </summary>
        private void StopLobbyMatchRuntime(bool matchEnded)
        {
            NetImpactDiagnostics.ExportServerRequested(_sim, LoopDiagnostics);
            NetImpactDiagnostics.ExportRequested();
            if (!matchEnded) AbandonCareerMatch();
            ServerReplayRecorder.Stop(matchEnded);
            _sim?.Stop(preserveRoomPrewarm: true);
            _sim = null;
            _lastSnapshotLength = 0;
            NetHitClaims.VerdictSink = null;
        }

        /// <summary>
        /// Build and publish a new match while retaining the lobby session/socket.
        /// Only the lobby owner's Start Match enters here; completed lobby matches
        /// return to the lobby and wait for another explicit start.
        /// </summary>
        private bool BeginLobbyMatch(MatchDefinition match, double now, out string reason)
        {
            reason = "";
            if (match.MapIdentity.IsCustom)
            {
                Peer? waiting = _peers.FirstOrDefault(p => p.PreparedMap != match.MapIdentity || p.MapAvailability != MapAvailabilityState.Ready);
                if (waiting != null) { reason = $"Waiting for {waiting.Name} to prepare {match.RoomKey} ({waiting.MapAvailability})."; return false; }
                if (!MapGen.CustomRooms.Installed.HasExact(match.MapIdentity.Content(match.RoomKey)))
                { reason = "The server's installed map package changed."; return false; }
            }
            if (!_controlPlaneOnlyForTests)
            {
                MapGen.CustomRooms.GenerateMissing(match.RoomKey);
                if (!Mods.Multiplayer.MapModeCapabilities.Supports(match.RoomKey, match.Mode,
                    LobbyRules.ResolveWorldProfile(match, _maxPlayers), out reason)) return false;
            }
            StopLobbyMatchRuntime(matchEnded: false);
            _botAssistedMatch = _bots.Count > 0;
            _frozenMatch = match;
            _frozenWorldProfile = LobbyRules.ResolveWorldProfile(_frozenMatch, _maxPlayers);

            foreach (Peer connected in _peers)
            { connected.LastIntentFrame = 0; connected.HasIntentFrame = false; }
            _matchEndedAt = -1;
            byte participants = 0;
            foreach (Peer participant in _peers)
            {
                participants |= (byte)(1 << participant.SlotIndex);
                participant.MatchReady = participant.SceneLoaded = false;
                participant.LoadStartedAt = now; participant.FirstBootstrapAt = -1; participant.LateJoin = false;
                participant.BootstrapLength = 0;
                participant.MatchLoadStage = MatchLoadStage.None;
                participant.MatchLoadProgressAt = 0;
                participant.SlowLoadLogged = false;
            }

            // Publish Starting before the server's own synchronous room build.
            // Clients can now load in parallel with the authority instead of
            // paying server load time and client load time back-to-back.
            double buildStarted = NetSession.Clock;
            _matchId = NetLifecycleTracker.Next(_matchId);
            _start.Begin(_matchId, _authorityEpoch, participants);
            _lastStartCommitBroadcast = 0;
            SetPhase(SessionPhase.Starting);
            // StartSimulation is synchronous. Send independent wire copies of
            // the same reliable event so one lost datagram does not delay a
            // client's parallel load until the worker's retransmission timer.
            BroadcastSessionState(copies: 3);
            try
            {
                StartSimulation();
            }
            catch (Exception ex)
            {
                _sim?.Stop(preserveRoomPrewarm: true);
                _sim = null;
                Log($"[lobby] map load failed: {ex.Message}");
                reason = "The server could not load this map.";
                // Keep the advanced match id. Clients may already have observed
                // it and correctly reject a rollback to the previous id.
                EnterLobby(_frozenMatch);
                return false;
            }

            // Preserve the full client grace period even when the authority's
            // own cold load was expensive.
            double buildSeconds = NetSession.Clock - buildStarted;

            double afterBuild = now + buildSeconds;
            // The server itself was blocked during StartSimulation and could not
            // perform its normal liveness cadence. Give the frozen participants
            // a fresh connection-health window here as well as a fresh load
            // barrier window; otherwise a >30 s cold authority load can make the
            // next loop time out clients for silence the server caused.
            foreach (Peer participant in _peers)
                if ((participants & (1 << participant.SlotIndex)) != 0)
                    participant.LastSeen = Math.Max(participant.LastSeen, afterBuild);
            _start.AuthorityReady(afterBuild);
            TouchLobbyRevision("authority ready; waiting for loaded participants");
            _now = Math.Max(_now, afterBuild);
            SyncSimulationState(afterBuild);
            CheckLoadBarrier(afterBuild);
            Log($"[lobby] authority loaded {_frozenMatch.RoomKey} in {buildSeconds:0.00}s; "
                + $"waiting for slots mask {_start.Expected:X2}");
            return true;
        }

        private void EnterLobby(MatchDefinition match)
        {
            bool matchEnded = _matchEndedAt >= 0;
            StopLobbyMatchRuntime(matchEnded);
            CancelMapVote(_now);
            NetworkMapIdentity.StageRoom(match.RoomKey);
            NetworkMapIdentity identity = NetworkMapIdentity.ForRoom(match.RoomKey);
            if (!identity.IsCustom && !_controlPlaneOnlyForTests) RememberStockGameplayHash(match.RoomKey);
            if (_lobbyMatch.MapIdentity != identity || _lobbyMatch.RoomKey != match.RoomKey) InvalidateMapReadiness();
            _lobbyMatch = match with { MapIdentity = identity };
            if (!_controlPlaneOnlyForTests) Mods.RoomPrewarm.Begin(_lobbyMatch.RoomKey);
            _matchEndedAt = -1;
            _start.Reset();
            _lastStartCommitBroadcast = 0;
            CloseBallot();
            BroadcastMapChoices();
            InvalidateLobbyReady();

            // Normalize against the match players will actually configure next.
            SessionPhase previous = _phase;
            _phase = SessionPhase.Lobby;
            NormalizeTeams();
            _phase = previous;
            SetPhase(SessionPhase.Lobby);
        }

        /// <summary>
        /// Close the current persistent lobby for every connected player.
        /// A launcher-authenticated owner also owns the local server process and
        /// may terminate it. On a standalone dedicated server, closing a lobby
        /// resets it to an empty lobby so the first player to join cannot kill
        /// the daemon.
        /// </summary>
        private void CloseLobbySession(Peer owner)
        {
            bool stopProcess = _processOwnerClientId != 0
                && owner.ClientId == _processOwnerClientId;
            Log($"[lobby] slot {owner.SlotIndex} closed the lobby"
                + (stopProcess ? " and its local server" : ""));

            foreach (Peer connected in _peers)
            {
                _transport?.Send(connected.EndPoint, PacketType.Bye, ReadOnlySpan<byte>.Empty);
                _transport?.RetireConnection(connected.EndPoint);
            }

            StopLobbyMatchRuntime(matchEnded: _matchEndedAt >= 0);
            CancelMapVote(_now);
            CloseBallot();
            _rotation.ClearPending();
            _peers.Clear();
            RemoveAllBots();
            _lobbyOwnerClientId = 0;
            _processOwnerClientId = 0;
            _start.Reset();
            _matchEndedAt = -1;

            if (stopProcess)
            {
                _running = false;
                return;
            }

            // Standalone dedicated lobby: close this room, then present a fresh,
            // ownerless lobby to the next connection.
            _authorityEpoch++;
            _matchId = NetLifecycleTracker.Next(_matchId);
            _lobbyMatch = DefinitionFor(_rotation.Current);
            _frozenMatch = _lobbyMatch;
            _frozenWorldProfile = default;
            _phase = SessionPhase.Lobby;
            _sessionRevision = NetLifecycleTracker.Next(_sessionRevision);
        }

        private void HandleMapAvailability(ReceivedPacket packet, double now)
        {
            Peer? peer = Find(packet.Sender);
            if (peer == null || !MapAvailabilityPacket.TryRead(packet.Payload, out var report)
                || report.MatchId != _matchId || report.AuthorityEpoch != _authorityEpoch || report.Map != CurrentDefinition.MapIdentity
                || report.Generation != _mapGeneration || peer.LastMapAvailabilitySequence != 0 && !NetLifecycleTracker.Newer(report.Sequence,peer.LastMapAvailabilitySequence)) return;
            if (now - peer.LastMapAvailability < .1) return;
            peer.LastMapAvailability = now; peer.LastMapAvailabilitySequence = report.Sequence;
            bool changed = peer.PreparedMap != report.Map || peer.MapAvailability != report.State;
            peer.PreparedMap = report.Map; peer.MapAvailability = report.State; peer.LastSeen = now;
            if (changed) BroadcastSessionState();
        }

        private void HandleMatchLoaded(ReceivedPacket packet, double now)
        {
            Peer? peer = Find(packet.Sender);
            MatchStartIdentity identity;
            bool spectating = false;
            bool refreshBootstrap = false;
            if (MatchLoadedRolePacket.TryRead(packet.Payload, out var roleLoaded))
            {
                identity = roleLoaded.Identity;
                spectating = roleLoaded.Spectating;
                refreshBootstrap = roleLoaded.RefreshBootstrap;
            }
            else if (MatchLoadedPacket.TryRead(packet.Payload, out var legacyLoaded))
            {
                // Asset-free harnesses may still emit the bare identity. A
                // protocol-36 game client always uses MatchLoadedRolePacket.
                identity = legacyLoaded.Identity;
            }
            else return;
            if (peer == null || identity != CurrentStartIdentity
                || _phase is not (SessionPhase.Starting or SessionPhase.InMatch)) return;
            if (CurrentDefinition.MapIdentity.IsCustom && (peer.PreparedMap != CurrentDefinition.MapIdentity || peer.MapAvailability != MapAvailabilityState.Ready)) return;
            _ = TrySetPeerRole(peer, spectating); // Still bootstrap the retained valid role.
            if (refreshBootstrap)
            {
                // Resume preserves this admission and transport fence, but must
                // acknowledge a newly captured world before authoring gameplay.
                peer.MatchReady = false;
                peer.BootstrapLength = 0;
                peer.FirstBootstrapAt = -1;
            }
            peer.LastSeen = now; peer.SceneLoaded = true;
            if (_phase == SessionPhase.Starting) _start.MarkLoaded(peer.SlotIndex, identity);
            SendBootstrap(peer, now);
            TouchLobbyRevision($"slot {peer.SlotIndex} synchronizing");
            CheckLoadBarrier(now);
        }

        private void HandleMatchLoadProgress(ReceivedPacket packet, double now)
        {
            Peer? peer = Find(packet.Sender);
            if (peer == null || !MatchLoadProgressPacket.TryRead(packet.Payload, out var progress)
                || progress.Identity != CurrentStartIdentity || _phase != SessionPhase.Starting
                || (_start.Expected & (1 << peer.SlotIndex)) == 0) return;
            peer.LastSeen = now;
            peer.MatchLoadProgressAt = now;
            if (progress.Stage <= peer.MatchLoadStage) return;
            peer.MatchLoadStage = progress.Stage;
            Log($"[lobby] slot {peer.SlotIndex} loading: {progress.Stage}");
        }

        private void BroadcastStartCommit(double now, bool force = false)
        {
            if (_start.Stage != StartStage.Countdown || _transport == null) return;
            if (!force && now - _lastStartCommitBroadcast < 0.10) return;
            _lastStartCommitBroadcast = now;
            var commit = new MatchStartCommitPacket(_matchId, _authorityEpoch,
                _start.Identity.StartGeneration, _start.RemainingMilliseconds(now));
            commit.Write(_scratch);
            for (int i = 0; i < _peers.Count; i++)
                if ((_start.Expected & (1 << _peers[i].SlotIndex)) != 0)
                    _transport.Send(_peers[i].EndPoint, PacketType.MatchStartCommit,
                        _scratch.AsSpan(0, MatchStartCommitPacket.Size));
        }

        private void CheckLoadBarrier(double now)
        {
            PumpBootstraps(now);
            if (_phase != SessionPhase.Starting || _checkingLoadBarrier) return;
            _checkingLoadBarrier = true;
            try
            {
                byte slow = _start.MissingAtSlowDeadline(now);
                for (int i = 0; i < _peers.Count; i++)
                {
                    Peer peer = _peers[i];
                    if ((slow & (1 << peer.SlotIndex)) == 0 || peer.SlowLoadLogged) continue;
                    peer.SlowLoadLogged = true;
                    string stage = peer.MatchLoadStage == MatchLoadStage.None
                        ? "no progress reported" : peer.MatchLoadStage.ToString();
                    Log($"[lobby] slot {peer.SlotIndex} is waiting at {_start.Stage} (scene={peer.SceneLoaded}, world={peer.MatchReady}) after "
                        + $"{NetMatchStart.SlowLoadSeconds:0}s ({stage}); keeping the client in the barrier");
                }

                byte missing = _start.MissingAtDeadline(now);
                // The old 15-second deadline disconnected healthy cold loaders.
                // It is now only a warning; this hard boundary is for genuinely
                // stuck clients and is deliberately much longer.
                for (int i = _peers.Count - 1; i >= 0; i--)
                    if ((missing & (1 << _peers[i].SlotIndex)) != 0)
                    {
                        Peer timedOut = _peers[i];
                        new LoadFailurePacket(_matchId, _authorityEpoch, _start.Identity.StartGeneration,
                            _slotGenerations[timedOut.SlotIndex], RefusedPacket.ReasonLoadTimeout).Write(_scratch);
                        _transport?.Send(timedOut.EndPoint, PacketType.Refused, _scratch.AsSpan(0, LoadFailurePacket.Size));
                        Remove(_peers[i], "match load hard timeout");
                    }

                if (_phase != SessionPhase.Starting) return;
                bool advanced = _start.Advance(now);
                if (advanced && _start.Stage == StartStage.Synchronizing)
                { TouchLobbyRevision($"synchronizing authoritative world expected={_start.Expected:X2} loaded={_start.Loaded:X2} ready={_start.WorldReady:X2}"); advanced = _start.Advance(now); }
                if (_start.Stage == StartStage.Countdown)
                {
                    if (advanced) TouchLobbyRevision("all participants ready; countdown started");
                    // SessionState is reliable state, but a retransmission carries
                    // the old remaining value. A fresh disposable commitment every
                    // 100 ms keeps the release edge tight under loss/jitter.
                    BroadcastStartCommit(now, force: advanced);
                    return;
                }
                if (!advanced || _start.Stage != StartStage.InMatch) return;
                _matchStarted = now;
                SetPhase(SessionPhase.InMatch);
                BroadcastSessionState();
                BroadcastMatchState(now);
            }
            finally { _checkingLoadBarrier = false; }
        }

        private void HandleMatchLoadFailed(ReceivedPacket packet)
        {
            Peer? peer = Find(packet.Sender);
            if (peer == null || !MatchLoadFailedPacket.TryRead(packet.Payload, out var failed)
                || failed.Identity != CurrentStartIdentity) return;
            Remove(peer, $"could not load match: {failed.Reason}");
        }

        private void ReturnToLobby()
        {
            // Persistent lobbies keep the selected map until the host changes it.
            // Rotation remains a policy of continuous dedicated servers only.
            EnterLobby(_frozenMatch);
        }

        private void LobbyPeerRemoved(Peer peer)
        {
            _start.Remove(peer.SlotIndex);

            bool processOwned = _processOwnerClientId != 0;
            bool processOwnerLeft = peer.ClientId != 0
                && _processOwnerClientId == peer.ClientId;

            if (_lobbyOwnerClientId == peer.ClientId)
                _lobbyOwnerClientId = _peers.Count > 0 ? _peers[0].ClientId : 0;

            if (processOwnerLeft)
            {
                // The oldest remaining peer becomes both kinds of owner. Without
                // this, a hosted lobby survives its creator but nobody can ever
                // close the child process that created it.
                _processOwnerClientId = _lobbyOwnerClientId;
            }

            if (SessionPolicy == ServerSessionPolicy.Lobby && _peers.Count == 0)
            {
                RemoveAllBots();
                _lobbyOwnerClientId = 0;
                _processOwnerClientId = 0;

                if (processOwned)
                {
                    // Launcher/directory-hosted lobby: there is no useful empty
                    // session to preserve. Stop immediately so Shutdown sends the
                    // directory Farewell and the parent can reclaim its port.
                    Log("[lobby] last player left hosted lobby; stopping server");
                    StopLobbyMatchRuntime(matchEnded: _matchEndedAt >= 0);
                    CancelMapVote(_now);
                    CloseBallot();
                    _rotation.ClearPending();
                    _start.Reset();
                    _matchEndedAt = -1;
                    _running = false;
                    return;
                }

                if (_phase != SessionPhase.Lobby)
                {
                    // A standalone persistent lobby stays available, but an
                    // abandoned Starting/InMatch/PostMatch world must not wait
                    // for the next player. Reset it now so a future Hello lands
                    // in a clean lobby rather than somebody else's dead match.
                    Log("[lobby] last player left; resetting abandoned match");
                    EnterLobby(_frozenMatch);
                    return;
                }
            }

            TouchLobbyRevision($"slot {peer.SlotIndex} left; owner {_lobbyOwnerClientId}");
            if (_phase == SessionPhase.Starting)
            {
                if (LobbyRules.Validate(_frozenMatch, BuildRoster(), false, out string why) != LobbyResultCode.Ok)
                {
                    Log($"[lobby] start cancelled: {why}");
                    EnterLobby(_frozenMatch);
                }
                else CheckLoadBarrier(_now);
            }
        }
    }
}
