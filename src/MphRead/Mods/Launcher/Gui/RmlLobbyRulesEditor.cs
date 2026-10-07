#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// RmlUi's renderer-independent multiplayer rule draft. No Avalonia
    /// control is consulted: the authoritative SessionStatePacket is the
    /// source, LobbyRules/MatchModifierRules validate, and the existing
    /// UpdateMatch lobby command is the only way to publish changes.
    ///
    /// A draft remains local until Apply. A rejected/stale update retains the
    /// draft and explains the server's answer rather than pretending it saved.
    /// </summary>
    internal sealed class RmlLobbyRulesEditor
    {
        internal const int ToggleCount = 16;
        private readonly string[] _rooms;
        private MatchDefinition _draft;
        private LobbyRuleFlags _flags;
        private MatchDefinition _baselineMatch;
        private LobbyRuleFlags _baselineFlags;
        private MatchDefinition _submittedMatch;
        private LobbyRuleFlags _submittedFlags;
        private ushort _baselineRevision;
        private long _sentAt;
        private bool _open;
        private bool _dirty;
        private bool _pending;
        private bool _publishPending;
        private string _status = "";
        private string _timeText = "";
        private string _goalText = "";

        internal bool IsOpen => _open;

        internal RmlLobbyRulesEditor(IReadOnlyList<string> rooms)
        {
            _rooms = rooms
                .Where(room => !String.IsNullOrWhiteSpace(room))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal void Open()
        {
            if (!NetSession.Active || !NetSession.PersistentLobby
                || NetSession.ServerSession is not { } session)
                return;
            _open = true;
            if (!_pending)
                ResetFrom(session, "SERVER RULES LOADED");
            _publishPending = true;
            RmlUiPrototype.SetMenuBool("lobby_rules_open", true);
            RmlUiPrototype.SetFieldValue("rules_time", _timeText);
            RmlUiPrototype.SetFieldValue("rules_goal", _goalText);
            Publish();
        }

        internal void Close()
        {
            _open = false;
            _dirty = false; // unsaved changes are discarded, not transmitted
            RmlUiPrototype.SetMenuBool("lobby_rules_open", false);
        }

        internal void Tick()
        {
            if ((!_open && !_pending)
                || NetSession.ServerSession is not { } session
                || !NetSession.Active || !NetSession.PersistentLobby)
            {
                if (_open) Close();
                return;
            }

            if (_pending)
            {
                if (session.Match == _submittedMatch
                    && session.RuleFlags == _submittedFlags)
                {
                    _pending = false;
                    _dirty = false;
                    ResetFrom(session, "RULES SAVED // SERVER CONFIRMED");
                    RmlUiPrototype.SetFieldValue("rules_time", _timeText);
                    RmlUiPrototype.SetFieldValue("rules_goal", _goalText);
                }
                else if (!NetSession.LobbyCommandPending
                    && !String.IsNullOrWhiteSpace(NetSession.LobbyMessage))
                {
                    _pending = false;
                    SetStatus("SERVER REJECTED CHANGES // " + NetSession.LobbyMessage);
                }
                else if (Environment.TickCount64 - _sentAt > 8000)
                {
                    _pending = false;
                    SetStatus("NO SERVER CONFIRMATION // REVIEW OR RETRY");
                }
            }
            else if (!_dirty && (session.Match != _draft || session.RuleFlags != _flags))
            {
                ResetFrom(session, "SERVER RULES UPDATED");
                RmlUiPrototype.SetFieldValue("rules_time", _timeText);
                RmlUiPrototype.SetFieldValue("rules_goal", _goalText);
            }

            if (_open)
            {
                if (session.Phase != SessionPhase.Lobby)
                    Close();
                else if (_publishPending)
                    Publish();
            }
        }

        internal void CycleMode()
        {
            if (!CanEdit()) return;
            int index = MatchTypeCatalog.BaseModeIndex(_draft.Mode);
            MatchTypeDefinition type = MatchTypeCatalog.GameTypes[
                (index + 1) % MatchTypeCatalog.GameTypes.Length];
            MatchFormat format = MatchTypeCatalog.NormalizeFormat(type, _draft.Format);
            if (format == MatchFormat.Custom && !_draft.CustomTeams.IsValid)
                format = MatchFormat.Auto;
            GameMode mode = type.Resolve(format);
            _draft = _draft with
            {
                Mode = mode, Format = format,
                PointGoal = MatchGoalRules.DefaultValue(mode),
                TimeLimitSeconds = mode == GameMode.OneInTheChamber
                    ? (ushort)0 : _draft.TimeLimitSeconds,
                VanillaDuelResources = format == MatchFormat.OneVsOne
                    && mode == GameMode.BattleTeams && _draft.VanillaDuelResources,
                OctolithAutoReset = MatchModifierRules.UsesOctolith(mode)
                    && _draft.OctolithAutoReset
            };
            _goalText = LobbyRuleEditValues.GoalText(mode, _draft.PointGoal);
            _timeText = LobbyRuleEditValues.DurationText(_draft.TimeLimitSeconds);
            RmlUiPrototype.SetFieldValue("rules_goal", _goalText);
            RmlUiPrototype.SetFieldValue("rules_time", _timeText);
            MarkDirty();
        }

        internal void CycleFormat()
        {
            if (!CanEdit()) return;
            MatchFormat current = _draft.Format;
            int index = MatchTypeCatalog.MatchupIndex(current);
            MatchTypeDefinition type = MatchTypeCatalog.GameTypes[
                MatchTypeCatalog.BaseModeIndex(_draft.Mode)];
            for (int step = 1; step <= MatchTypeCatalog.Matchups.Length; step++)
            {
                MatchFormat requested = MatchTypeCatalog.Matchups[
                    (index + step) % MatchTypeCatalog.Matchups.Length].Format;
                MatchFormat actual = MatchTypeCatalog.NormalizeFormat(type, requested);
                if (actual == current) continue;
                GameMode mode = type.Resolve(actual);
                MatchDefinition candidate = _draft with
                {
                    Mode = mode, Format = actual,
                    CustomTeams = _draft.CustomTeams.IsValid
                        ? _draft.CustomTeams : new TeamLayout(2, 2, 2),
                    VanillaDuelResources = actual == MatchFormat.OneVsOne
                        && mode == GameMode.BattleTeams && _draft.VanillaDuelResources
                };
                if (LobbyRules.ValidateDefinition(candidate, out _)
                    != LobbyResultCode.Ok) continue;
                _draft = candidate;
                MarkDirty();
                return;
            }
            SetStatus("NO OTHER COMPATIBLE MATCHUP");
        }

        internal void CycleMap()
        {
            if (!CanEdit()) return;
            if (_rooms.Length == 0)
            {
                SetStatus("NO LOCAL ARENAS AVAILABLE");
                return;
            }
            string[] choices = _rooms
                .Append(_draft.RoomKey)
                .Concat(CustomRooms.Definitions.Select(d => d.Name))
                .Where(name => !String.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int current = Array.FindIndex(choices, key =>
                String.Equals(key, _draft.RoomKey, StringComparison.OrdinalIgnoreCase));
            int max = NetSession.ServerSession?.MaxPlayers ?? 8;

            for (int step = 1; step < choices.Length; step++)
            {
                string room = choices[(Math.Max(current, 0) + step) % choices.Length];
                MatchDefinition candidate;
                try
                {
                    candidate = _draft with
                    {
                        RoomKey = room,
                        MapIdentity = NetworkMapIdentity.ForRoom(room)
                    };
                }
                catch (Exception) { continue; }

                int mapPlayers = LobbyRules.ExactTeams(candidate)
                    ? LobbyRules.ResolveTeamLayout(candidate).TotalPlayers : max;
                if (LobbyRules.ValidateDefinition(candidate, out _)
                    != LobbyResultCode.Ok
                    || !MapModeCapabilities.Supports(room, candidate.Mode,
                        LobbyRules.ResolveWorldProfile(candidate, max), out _,
                        mapPlayers))
                    continue;

                _draft = candidate;
                MarkDirty();
                return;
            }
            SetStatus("NO OTHER COMPATIBLE ARENA");
        }

        internal void Toggle(int index)
        {
            if (!CanEdit() || index < 0 || index >= ToggleCount) return;
            switch (index)
            {
                case 0: _flags ^= LobbyRuleFlags.RequireReady; break;
                case 1: _flags ^= LobbyRuleFlags.AllowJoinInProgress; break;
                case 2: _flags ^= LobbyRuleFlags.LockTeams; break;
                case 3: _draft = _draft with { HideOpponentHealth = !_draft.HideOpponentHealth }; break;
                case 4: _draft = _draft with { DisablePowerups = !_draft.DisablePowerups }; break;
                case 5: _draft = _draft with { SpawnProtection = !_draft.SpawnProtection }; break;
                case 6: _draft = _draft with { AffinityWeapons = !_draft.AffinityWeapons }; break;
                case 7: _draft = _draft with { FriendlyFire = !_draft.FriendlyFire }; break;
                case 8: _draft = _draft with { BalancedMode = !_draft.BalancedMode }; break;
                case 9: _draft = _draft with { InstaGib = !_draft.InstaGib,
                    NoImperialist = _draft.InstaGib && _draft.NoImperialist }; break;
                case 10: _draft = _draft with { LowTier = !_draft.LowTier }; break;
                case 11: _draft = _draft with { NoImperialist = !_draft.NoImperialist,
                    InstaGib = _draft.NoImperialist && _draft.InstaGib }; break;
                case 12: _draft = _draft with { Fiesta = !_draft.Fiesta }; break;
                case 13: _draft = _draft with { VanillaDuelResources = !_draft.VanillaDuelResources }; break;
                case 14: _draft = _draft with { ShadowFreeze = !_draft.ShadowFreeze }; break;
                case 15: _draft = _draft with { OctolithAutoReset = !_draft.OctolithAutoReset }; break;
            }
            MarkDirty();
        }

        internal void Apply(string time, string goal)
        {
            if (!CanEdit()) return;
            if (NetSession.ServerSession is not { } session)
            {
                SetStatus("LOBBY DATA IS NOT AVAILABLE");
                return;
            }
            if (session.Revision != _baselineRevision
                && (session.Match != _baselineMatch
                    || session.RuleFlags != _baselineFlags))
            {
                SetStatus("SERVER RULES CHANGED // CANCEL AND REOPEN");
                return;
            }

            if (!LobbyRuleEditValues.TryDuration(time, allowZero: true, out ushort seconds))
            {
                SetStatus("TIME LIMIT MUST BE MINUTES OR M:SS (7:00)");
                return;
            }
            if (!LobbyRuleEditValues.TryGoal(_draft.Mode, goal,
                out ushort points, out string goalError))
            {
                SetStatus(goalError);
                return;
            }

            MatchDefinition match = _draft with
            {
                TimeLimitSeconds = _draft.Mode == GameMode.OneInTheChamber
                    ? (ushort)0 : seconds,
                PointGoal = points,
                OctolithAutoReset = MatchModifierRules.UsesOctolith(_draft.Mode)
                    && _draft.OctolithAutoReset,
                VanillaDuelResources = _draft.Format == MatchFormat.OneVsOne
                    && _draft.Mode == GameMode.BattleTeams
                    && _draft.VanillaDuelResources
            };
            try
            {
                match = match with
                {
                    MapIdentity = String.Equals(match.RoomKey, session.Match.RoomKey,
                        StringComparison.OrdinalIgnoreCase)
                        ? session.Match.MapIdentity
                        : NetworkMapIdentity.ForRoom(match.RoomKey)
                };
            }
            catch (Exception ex)
            {
                SetStatus("MAP IDENTITY ERROR // " + ex.Message);
                return;
            }

            if (LobbyRules.ValidateDefinition(match, out string reason) != LobbyResultCode.Ok
                || !MatchModifierRules.Validate(match, out reason))
            {
                SetStatus(reason);
                return;
            }
            int max = session.MaxPlayers;
            int participants = NetSession.LobbyRoster().Count;
            TeamLayout layout = LobbyRules.ResolveTeamLayout(match);
            if (layout.TeamCount > 0
                && (layout.TotalPlayers < participants
                    || (LobbyRules.ExactTeams(match) && layout.TotalPlayers > max)))
            {
                SetStatus("MATCHUP MUST FIT CONNECTED PLAYERS AND SERVER CAPACITY");
                return;
            }
            int mapPlayers = LobbyRules.ExactTeams(match) ? layout.TotalPlayers : max;
            if (!MapModeCapabilities.Supports(match.RoomKey, match.Mode,
                LobbyRules.ResolveWorldProfile(match, max),
                out reason, mapPlayers))
            {
                SetStatus(reason);
                return;
            }

            LobbyRuleFlags flags = _flags;
            if (!GameState.IsTeamMode(match.Mode))
                flags &= ~LobbyRuleFlags.LockTeams;
            if (match.HideOpponentHealth)
                flags |= LobbyRuleFlags.HideOpponentHealth;
            else
                flags &= ~LobbyRuleFlags.HideOpponentHealth;

            if (match == session.Match && flags == session.RuleFlags)
            {
                _dirty = false;
                SetStatus("NO CHANGES TO SAVE");
                return;
            }

            SessionStatePacket updated = session;
            updated.Match = match;
            updated.RuleFlags = flags;
            if (!NetSession.SendLobbyCommand(LobbyCommandType.UpdateMatch,
                configuration: updated))
            {
                SetStatus("LOBBY IS BUSY // RETRY WHEN SERVER ACKNOWLEDGES");
                return;
            }

            _pending = true;
            _sentAt = Environment.TickCount64;
            _submittedMatch = match;
            _submittedFlags = flags;
            _draft = match;
            _flags = flags;
            _timeText = LobbyRuleEditValues.DurationText(match.TimeLimitSeconds);
            _goalText = LobbyRuleEditValues.GoalText(match.Mode, match.PointGoal);
            SetStatus("SAVING RULES // WAITING FOR SERVER CONFIRMATION");
        }

        private bool CanEdit()
        {
            if (!_open || !NetSession.IsInLobby || !NetSession.CanEditLobby)
            {
                SetStatus("ONLY THE LOBBY OWNER CAN EDIT RULES");
                return false;
            }
            if (_pending || NetSession.LobbyCommandPending)
            {
                SetStatus("WAITING FOR SERVER ACKNOWLEDGEMENT");
                return false;
            }
            return true;
        }

        private void ResetFrom(SessionStatePacket session, string message)
        {
            _draft = session.Match;
            _flags = session.RuleFlags;
            _baselineMatch = session.Match;
            _baselineFlags = session.RuleFlags;
            _baselineRevision = session.Revision;
            _dirty = false;
            _timeText = LobbyRuleEditValues.DurationText(session.Match.TimeLimitSeconds);
            _goalText = LobbyRuleEditValues.GoalText(session.Match.Mode,
                session.Match.PointGoal);
            SetStatus(message);
        }

        private void MarkDirty()
        {
            _dirty = true;
            SetStatus("UNSAVED RULES // PRESS APPLY TO SEND");
        }

        private void SetStatus(string message)
        {
            _status = String.IsNullOrWhiteSpace(message) ? "" : message.Trim();
            _publishPending = true;
        }

        private string OnOff(int index)
        {
            bool enabled = index switch
            {
                0 => _flags.HasFlag(LobbyRuleFlags.RequireReady),
                1 => _flags.HasFlag(LobbyRuleFlags.AllowJoinInProgress),
                2 => _flags.HasFlag(LobbyRuleFlags.LockTeams),
                3 => !_draft.HideOpponentHealth,
                4 => !_draft.DisablePowerups,
                5 => _draft.SpawnProtection,
                6 => _draft.AffinityWeapons,
                7 => _draft.FriendlyFire,
                8 => _draft.BalancedMode,
                9 => _draft.InstaGib,
                10 => _draft.LowTier,
                11 => _draft.NoImperialist,
                12 => _draft.Fiesta,
                13 => _draft.VanillaDuelResources,
                14 => _draft.ShadowFreeze,
                15 => _draft.OctolithAutoReset,
                _ => false
            };
            return enabled ? "ON" : "OFF";
        }

        private void Publish()
        {
            if (!_open) return;
            _publishPending = false;
            RmlUiPrototype.SetMenuBool("rules_owner", NetSession.CanEditLobby);
            RmlUiPrototype.SetMenuBool("rules_dirty", _dirty);
            RmlUiPrototype.SetMenuBool("rules_pending", _pending);
            RmlUiPrototype.SetMenuText("rules_map", _draft.RoomKey.ToUpperInvariant());
            RmlUiPrototype.SetMenuText("rules_mode",
                MatchTypeCatalog.BaseLabel(_draft.Mode).ToUpperInvariant());
            RmlUiPrototype.SetMenuText("rules_format",
                MatchTypeCatalog.MatchupLabel(_draft.Mode, _draft.Format).ToUpperInvariant());
            RmlUiPrototype.SetMenuText("rules_goal_label",
                LobbyRuleEditValues.GoalLabel(_draft.Mode));
            RmlUiPrototype.SetMenuText("rules_status", _status.ToUpperInvariant());
            for (int i = 0; i < ToggleCount; i++)
                RmlUiPrototype.SetMenuText($"rules_toggle{i}", OnOff(i));
        }
    }
}
#endif
