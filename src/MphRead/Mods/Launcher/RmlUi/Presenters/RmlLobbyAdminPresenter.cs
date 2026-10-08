#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Presenters
{
    /// <summary>Native view of the shared controller. It never owns or pumps a connection.</summary>
    internal sealed class RmlLobbyAdminPresenter : IDisposable
    {
        private readonly RmlUiHost _host;
        private readonly LobbySessionController _controller;
        private readonly Guid _lifetime;
        private RmlUiDocumentToken _document;
        private byte _selectedSlot = byte.MaxValue;
        private ushort _selectedGeneration;
        private Hunter _botHunter = Hunter.Random;
        private byte _botSuit, _botLevel = 1;
        private LobbyIntent? _confirmation;
        private string _confirmationText = "", _message = "", _focusAfterUpdate = "";
        private long _revision;
        private bool _disposed;
        public RmlUiDocumentToken Document => _document;
        public bool Active => !_disposed && _host.IsAlive(_document);
        public event Action? Closed;

        public RmlLobbyAdminPresenter(RmlUiHost host, LobbySessionController controller)
        {
            _host = host; _controller = controller; _lifetime = controller.Snapshot().Lifetime;
        }
        public void Open()
        {
            _host.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(RmlLobbyAdminPresenter));
            if (!Active) _document = _host.OpenDocument("pages/lobby/admin.rml", RmlUiDocumentLayer.Modal);
            Select(_controller.Snapshot().LocalSlot); Update();
            _host.FocusDocument(_document, "admin_close");
        }
        public void Update()
        {
            if (!Active) return;
            LobbySnapshot state = _controller.Snapshot();
            if (state.Lifetime != _lifetime || state.Closed || !state.Active) { Dispose(); return; }
            LobbyPresentation model = LobbyPresentation.From(state);
            var selected = model.Players.FirstOrDefault(row => row.Player.Slot == _selectedSlot && row.Player.Generation == _selectedGeneration);
            if (_selectedSlot != byte.MaxValue && selected == null)
            {
                _selectedSlot = byte.MaxValue;
                _message = "The selected player left or changed slots. Select a player again.";
            }
            var values = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
            void Text(string id, string value) => values[id] = RmlUiBindingValue.FromText(value);
            void Bool(string id, bool value) => values[id] = RmlUiBindingValue.FromBoolean(value);
            void Enabled(string id, bool value) => Bool("disabled:" + id, !value || _confirmation.HasValue);
            Text("admin_title", state.Context?.ServerName ?? "MULTIPLAYER LOBBY");
            Text("admin_status", state.CommandError.Length > 0 ? state.CommandError : _message.Length > 0 ? _message
                : state.CommandPending ? "WAITING FOR SERVER ACKNOWLEDGEMENT"
                : $"{model.CombatantCount} COMBATANTS // {model.SpectatorCount} SPECTATORS // OWNER SLOT {state.OwnerSlot + 1}");
            for (byte slot = 0; slot < 8; slot++)
            {
                var row = model.Players.FirstOrDefault(player => player.Player.Slot == slot);
                Bool("visible:admin_slot" + slot, row != null);
                Enabled("admin_slot" + slot, row != null);
                Bool($"class:admin_slot{slot}:selected", row != null && row.Player.Slot == _selectedSlot && row.Player.Generation == _selectedGeneration);
                Text($"admin_player{slot}_name", row?.Player.Name ?? "OPEN SLOT");
                Text($"admin_player{slot}_details", row == null ? "" : $"{row.Player.Hunter} // SUIT {row.Player.Color + 1} // {(row.Player.IsSpectator ? "SPECTATOR" : row.Player.Team >= 0 ? "TEAM " + (char)('A' + row.Player.Team) : "FFA")} // {(row.Player.Ready ? "READY" : "NOT READY")} // {row.Player.Ping}ms{(row.IsOwner ? " // OWNER" : "")}{(row.Player.IsBot ? " // BOT" : "")}");
            }
            Text("admin_selected_name", selected?.Player.Name ?? "SELECT A CONNECTED PLAYER");
            Text("admin_selected_details", selected == null ? "" : $"DAMAGE REDUCTION {selected.Player.DamageReduction}% // MAP {selected.Player.MapAvailability}");
            Text("admin_handicap", "DAMAGE REDUCTION: " + (selected?.Player.DamageReduction ?? 0) + "%");
            Enabled("admin_team", selected?.CanChangeTeam == true);
            Enabled("admin_team_auto", selected?.CanChangeTeam == true);
            Enabled("admin_handicap", selected?.CanSetHandicap == true);
            Enabled("admin_kick", selected?.CanKick == true); Enabled("admin_transfer", selected?.CanTransfer == true);
            Enabled("admin_bot_add", model.CanAddBot); Enabled("admin_bot_apply", selected?.CanConfigureBot == true); Enabled("admin_bot_remove", selected?.CanRemoveBot == true);
            Enabled("admin_bot_hunter", model.CanConfigureRules); Enabled("admin_bot_suit", model.CanConfigureRules); Enabled("admin_bot_level", model.CanConfigureRules);
            Text("admin_bot_hunter", "BOT HUNTER: " + _botHunter); Text("admin_bot_suit", "BOT SUIT: " + (_botSuit + 1));
            Text("admin_bot_level", "DIFFICULTY: " + new[] { "EASY", "NORMAL", "HARD", "ELITE" }[_botLevel]);
            Text("admin_teams", model.Teams.Length == 0 ? "FREE FOR ALL // TEAM SELECTION UNAVAILABLE"
                : String.Join("\n", model.Teams.Select(team => $"{team.Name}: {team.Occupants} / {team.Capacity} // {team.Available} OPEN")));
            for (int index = 0; index < 4; index++) {
                Bool("visible:admin_team" + index, index < model.Teams.Length);
                Text("admin_team" + index, index < model.Teams.Length ? $"{model.Teams[index].Name} // {model.Teams[index].Occupants}/{model.Teams[index].Capacity}" : "");
                Enabled("admin_team" + index, selected != null && model.CanAssignTeam(selected.Player.Slot, (sbyte)index));
                Bool($"class:admin_team{index}:selected", selected?.Player.Team == index);
            }
            Text("admin_team_lock", model.TeamsLocked ? "UNLOCK TEAMS" : "LOCK TEAMS");
            Enabled("admin_team_lock", model.CanConfigureRules && model.Teams.Length > 0);
            Text("admin_map_status", $"{state.MapState} // {state.MapMessage}");
            Text("admin_rotation_state", model.MapRotationUnavailableReason);
            Enabled("admin_map_retry", state.Active && !state.Closed);
            Enabled("admin_start", model.CanStart); Enabled("admin_close_lobby", model.CanCloseLobby);
            Text("admin_permission_reason", model.StartReason.Length > 0 ? model.StartReason
                : !state.IsOwner ? "OWNER ACTIONS ARE UNAVAILABLE. YOUR OWN TEAM AND ROLE REMAIN AVAILABLE WHEN UNLOCKED."
                : "ADMIN CHANGES ARE APPLIED BY THE SERVER. DESTRUCTIVE ACTIONS REQUIRE CONFIRMATION.");
            Text("admin_chat_history", state.Chat.Length == 0 ? "NO MESSAGES" : String.Join("\n", state.Chat.TakeLast(64)));
            Text("admin_chat_hint", $"MAX {model.ChatLimit} SERVER BYTES // {(model.UnicodeChatAvailable ? "UNICODE" : "BASIC LATIN CHARACTERS ONLY")}");
            Enabled("admin_chat_send", state.Active);
            Bool("disabled:admin_chat_input", _confirmation.HasValue);
            Bool("visible:admin_confirmation", _confirmation.HasValue);
            Text("admin_confirmation_text", _confirmationText);
            Bool("disabled:admin_close", _confirmation.HasValue);
            _host.Present(new(_document, ++_revision, values));
            if (_focusAfterUpdate.Length > 0) { _host.FocusDocument(_document, _focusAfterUpdate); _focusAfterUpdate = ""; }
        }
        private void Select(int slot)
        {
            if (slot < 0 || slot > 7) return;
            var state = _controller.Snapshot();
            var player = state.Players.FirstOrDefault(row => row.Slot == slot);
            if (!state.Players.Any(row => row.Slot == slot)) return;
            _selectedSlot = player.Slot; _selectedGeneration = player.Generation;
            if (player.IsBot) { _botHunter = player.Hunter; _botSuit = player.Color; _botLevel = player.BotLevel; }
        }
        private LobbyIntent? Target(LobbyIntentKind kind)
        {
            var state = _controller.Snapshot();
            if (!state.Players.Any(player => player.Slot == _selectedSlot && player.Generation == _selectedGeneration))
            { _message = "Select a current player before performing this action."; return null; }
            return _controller.Intent(kind, _selectedSlot);
        }
        private void Dispatch(LobbyIntent intent)
        {
            LobbyActionResult result = _controller.Dispatch(intent);
            _message = result.Accepted ? "" : result.Message;
        }
        private void Confirm(LobbyIntentKind kind, string text)
        {
            LobbyIntent? intent = kind == LobbyIntentKind.CloseLobby ? _controller.Intent(kind) : Target(kind);
            if (!intent.HasValue) return;
            _confirmation = intent; _confirmationText = text + "\nTHE SELECTED PLAYER MUST STILL BE IN THIS LOBBY WHEN THE ACTION APPLIES.";
            Update(); _host.FocusDocument(_document, "admin_cancel");
        }
        private void CancelConfirmation()
        {
            _confirmation = null; _confirmationText = ""; _focusAfterUpdate = "admin_close";
        }
        public bool Back()
        {
            _host.VerifyOwnerThread();
            if (!Active) return false;
            if (_confirmation.HasValue) { CancelConfirmation(); Update(); }
            else Dispose();
            return true;
        }
        public bool Handle(RmlUiIntent intent)
        {
            if (!Active || intent.Document != _document) return false;
            var state = _controller.Snapshot(); var model = LobbyPresentation.From(state);
            if (_confirmation.HasValue && intent.Kind is not (RmlUiIntentKind.LobbyAdminConfirm or RmlUiIntentKind.LobbyAdminCancel)) return true;
            switch (intent.Kind)
            {
                case RmlUiIntentKind.LobbyPlayerSelect: Select(intent.Argument); break;
                case RmlUiIntentKind.LobbyAdminClose: Dispose(); return true;
                case RmlUiIntentKind.LobbyAdminCancel: CancelConfirmation(); break;
                case RmlUiIntentKind.LobbyAdminConfirm:
                    if (_confirmation is { } pending) { _confirmation = null; Dispatch(pending); _focusAfterUpdate = "admin_close"; } break;
                case RmlUiIntentKind.LobbyKick: Confirm(LobbyIntentKind.KickPlayer, "KICK THE SELECTED PLAYER?"); break;
                case RmlUiIntentKind.LobbyTransferOwner: Confirm(LobbyIntentKind.TransferOwner, "TRANSFER LOBBY OWNERSHIP TO THE SELECTED PLAYER?"); break;
                case RmlUiIntentKind.LobbyClose: Confirm(LobbyIntentKind.CloseLobby, "CLOSE THIS LOBBY AND DISCONNECT EVERY PLAYER?"); break;
                case RmlUiIntentKind.LobbyBotRemove: Confirm(LobbyIntentKind.RemoveBot, "REMOVE THE SELECTED BOT?"); break;
                case RmlUiIntentKind.LobbyBotHunterNext:
                    var pool = new[] { Hunter.Random }.Concat(model.AllowedHunters).ToArray();
                    _botHunter = pool[(Array.IndexOf(pool, _botHunter) + 1) % pool.Length]; break;
                case RmlUiIntentKind.LobbyBotSuitNext: _botSuit = (byte)((_botSuit + 1) % 4); break;
                case RmlUiIntentKind.LobbyBotLevelNext: _botLevel = (byte)((_botLevel + 1) % 4); break;
                case RmlUiIntentKind.LobbyBotAdd: Dispatch(_controller.Intent(LobbyIntentKind.AddBot) with { Hunter = _botHunter, Color = _botSuit, BotLevel = _botLevel }); break;
                case RmlUiIntentKind.LobbyBotConfigure:
                    if (Target(LobbyIntentKind.UpdateBot) is { } bot) Dispatch(bot with { Hunter = _botHunter, Color = _botSuit, BotLevel = _botLevel }); break;
                case RmlUiIntentKind.LobbyHandicapNext:
                    if (Target(LobbyIntentKind.SetHandicap) is { } handicap) {
                        var player = state.Players.First(row => row.Slot == handicap.TargetSlot);
                        Dispatch(handicap with { DamageReduction = (byte)((player.DamageReduction + PlayerHandicap.Step) % (PlayerHandicap.MaxDamageReduction + PlayerHandicap.Step)) });
                    } break;
                case RmlUiIntentKind.LobbyTeamSelect:
                    if (Target(LobbyIntentKind.SetTeam) is { } choice) Dispatch(choice with { Team = (sbyte)intent.Argument }); break;
                case RmlUiIntentKind.LobbyTeamsAuto:
                    if (Target(LobbyIntentKind.SetTeam) is { } auto) Dispatch(auto with { Team = -1 }); break;
                case RmlUiIntentKind.LobbyTeamNext:
                    if (Target(LobbyIntentKind.SetTeam) is { } team) {
                        var player = state.Players.First(row => row.Slot == team.TargetSlot);
                        bool found = false;
                        for (int step = 1; step <= model.Teams.Length; step++) {
                            sbyte next = (sbyte)((Math.Max(0, (int)player.Team) + step) % Math.Max(1, model.Teams.Length));
                            if (model.CanAssignTeam(player.Slot, next)) { Dispatch(team with { Team = next }); found = true; break; }
                        }
                        if (!found) _message = "No eligible team has capacity for this player.";
                    } break;
                case RmlUiIntentKind.LobbyTeamLockToggle:
                    Dispatch(_controller.Intent(LobbyIntentKind.UpdateRules) with { Match = state.Match, RuleFlags = state.RuleFlags ^ LobbyRuleFlags.LockTeams }); break;
                case RmlUiIntentKind.LobbyStart: Dispatch(_controller.Intent(LobbyIntentKind.StartMatch)); break;
                case RmlUiIntentKind.LobbyMapRetry: Dispatch(_controller.Intent(LobbyIntentKind.RetryMap)); break;
                case RmlUiIntentKind.LobbyChatSend:
                    var result = _controller.Dispatch(_controller.Intent(LobbyIntentKind.SendChat) with { Text = _host.ReadField(_document, "admin_chat_input") });
                    _message = result.Message; if (result.Accepted) _host.SetField(_document, "admin_chat_input", ""); break;
                default: return false;
            }
            Update(); return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _host.VerifyOwnerThread();
            _disposed = true; _confirmation = null;
            if (_host.IsAlive(_document)) _host.CloseDocument(_document);
            Closed?.Invoke();
        }
    }
}
#endif
