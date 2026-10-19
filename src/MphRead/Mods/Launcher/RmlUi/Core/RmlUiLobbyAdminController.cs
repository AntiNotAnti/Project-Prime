using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

public enum RmlUiAdminOperation
{
    Kick, TransferOwner, CloseLobby, AddBot, UpdateBot, RemoveBot,
    SetTeam, SetHandicap, ToggleTeamLock, RetryMap, SendChat
}

public readonly record struct RmlUiAdminPlayer(
    byte Slot, ushort Generation, string Name, Hunter Hunter, byte Suit,
    sbyte Team, bool Ready, bool Bot, byte BotLevel, byte Handicap, ushort Ping);

/// <summary>Immutable copy from the existing authoritative lobby roster.</summary>
public sealed record RmlUiAdminSnapshot(
    ushort MatchId, ulong AuthorityEpoch, ushort SessionRevision,
    bool Owner, byte OwnerSlot, byte LocalSlot, bool CommandPending,
    bool TeamsLocked, byte TeamCount, byte MaxPlayers,
    bool CanRetryMap, string MapStatus, string CommandStatus,
    Hunter[] AllowedHunters, RmlUiAdminPlayer[] Players, string[] Chat);

/// <summary>Versioned request. The receiving live service rechecks every field.</summary>
public readonly record struct RmlUiAdminRequest(
    RmlUiAdminOperation Operation,
    ushort MatchId, ulong AuthorityEpoch, ushort SessionRevision,
    byte TargetSlot = byte.MaxValue, ushort TargetGeneration = 0,
    Hunter Hunter = Hunter.Random, byte Suit = 0, byte BotLevel = 1,
    sbyte Team = -1, byte Handicap = 0, string Text = "");

/// <summary>
/// Owner-thread-only projection and action controller. Does not mutate a
/// roster or drive any network session. A confirmation captures the *exact*
/// occupant generation, match identity and server revision.
/// </summary>
public sealed class RmlUiLobbyAdminController
{
    private readonly IRmlUiMenuServices _services;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ushort _matchId;
    private readonly ulong _epoch;
    private byte _slot = byte.MaxValue;
    private ushort _generation;
    private Hunter _hunter = Hunter.Random;
    private byte _suit;
    private byte _level = 1;
    private RmlUiAdminRequest? _confirmation;
    private string _confirmText = "";
    private string _status = "SELECT A PLAYER TO MANAGE";
    private bool _closed;

    public bool Closed => _closed;
    public bool ConfirmationOpen => _confirmation.HasValue;

    public RmlUiLobbyAdminController(IRmlUiMenuServices services, RmlUiAdminSnapshot current,
        int initialSlot = -1)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _matchId = current.MatchId;
        _epoch = current.AuthorityEpoch;
        if (_matchId == 0 || _epoch == 0) throw new ArgumentException("A live lobby is required.");
        if (initialSlot >= 0) Select(current, initialSlot);
    }

    private void Owner()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Native lobby administration belongs to its UI owner thread.");
    }

    private RmlUiAdminSnapshot? Current()
    {
        Owner();
        var value = _services.ReadAdmin();
        if (value == null || value.MatchId != _matchId || value.AuthorityEpoch != _epoch)
        {
            _closed = true;
            _confirmation = null;
            return null;
        }
        if (_slot != byte.MaxValue && !value.Players.Any(p => p.Slot == _slot && p.Generation == _generation))
        {
            _slot = byte.MaxValue;
            _confirmation = null;
            _status = "THE SELECTED PLAYER LEFT OR CHANGED SLOT. SELECT AGAIN.";
        }
        // Pending confirmations are invalid as soon as session revisions
        // advance, even when a new player occupies the same slot.
        if (_confirmation is { } pending && pending.SessionRevision != value.SessionRevision)
        {
            _confirmation = null;
            _status = "LOBBY CHANGED. REVIEW AND CONFIRM YOUR ACTION AGAIN.";
        }
        return value;
    }

    private RmlUiAdminPlayer? Selected(RmlUiAdminSnapshot state)
    {
        foreach (var row in state.Players)
            if (row.Slot == _slot && row.Generation == _generation) return row;
        return null;
    }

    private bool Select(RmlUiAdminSnapshot state, int slot)
    {
        if (slot is < 0 or >= 8) return false;
        RmlUiAdminPlayer? match = state.Players.FirstOrDefault(
            p => p.Slot == slot && p.Generation > 0);
        if (!match.HasValue || match.Value.Generation == 0) return false;
        RmlUiAdminPlayer player = match.Value;
        _slot = player.Slot;
        _generation = player.Generation;
        if (player.Bot)
        {
            _hunter = player.Hunter; _suit = player.Suit; _level = player.BotLevel;
        }
        _status = "SELECTED " + player.Name;
        return true;
    }

    private RmlUiAdminRequest Request(RmlUiAdminSnapshot state, RmlUiAdminOperation op,
        RmlUiAdminPlayer? target = null, sbyte team = -1,
        byte handicap = 0, string text = "") =>
        new(op, state.MatchId, state.AuthorityEpoch, state.SessionRevision,
            target?.Slot ?? byte.MaxValue, target?.Generation ?? (ushort)0,
            _hunter, _suit, _level, team, handicap, text);

    private static bool Destructive(RmlUiAdminOperation kind) =>
        kind is RmlUiAdminOperation.Kick or RmlUiAdminOperation.TransferOwner
            or RmlUiAdminOperation.CloseLobby or RmlUiAdminOperation.RemoveBot;

    private bool Submit(RmlUiAdminSnapshot state, RmlUiAdminRequest command)
    {
        if (state.CommandPending) { _status = "WAITING FOR SERVER ACKNOWLEDGEMENT"; return false; }
        if (command.SessionRevision != state.SessionRevision || command.MatchId != state.MatchId
            || command.AuthorityEpoch != state.AuthorityEpoch)
        { _status = "THE SERVER LOBBY CHANGED. RETRY THE ACTION."; return false; }
        if (command.TargetSlot != byte.MaxValue &&
            !state.Players.Any(p => p.Slot == command.TargetSlot
                && p.Generation == command.TargetGeneration))
        { _status = "THE TARGET PLAYER IS NO LONGER IN THIS SLOT."; return false; }
        bool accepted = _services.SubmitAdmin(command);
        _status = accepted ? "REQUEST SENT // WAITING FOR SERVER STATE" :
            "SERVER CANNOT ACCEPT THIS ACTION. CHECK YOUR PERMISSIONS.";
        return accepted;
    }

    public bool Dispatch(RmlUiIntentKind kind, int argument = 0, string chat = "")
    {
        Owner();
        if (_closed) return false;
        RmlUiAdminSnapshot? now = Current();
        if (now == null) return false;
        var state = now;
        RmlUiAdminPlayer? selected = Selected(state);
        if (_confirmation.HasValue && kind is not
            (RmlUiIntentKind.LobbyAdminConfirm or RmlUiIntentKind.LobbyAdminCancel))
            return false;

        switch (kind)
        {
            case RmlUiIntentKind.LobbyAdminClose:
                _closed = true; _confirmation = null; return true;
            case RmlUiIntentKind.LobbyAdminCancel:
                _confirmation = null; _confirmText = ""; return true;
            case RmlUiIntentKind.LobbyAdminConfirm:
                if (_confirmation is not { } pending) return false;
                _confirmation = null; _confirmText = "";
                return Submit(state, pending);
            case RmlUiIntentKind.LobbyPlayerSelect:
                return Select(state, argument);
            case RmlUiIntentKind.LobbyBotHunterNext:
            {
                Hunter[] pool = state.AllowedHunters.Concat(new[] { Hunter.Random }).Distinct().ToArray();
                if (pool.Length == 0) return false;
                int current = Array.IndexOf(pool, _hunter);
                _hunter = pool[(current + 1) % pool.Length];
                return true;
            }
            case RmlUiIntentKind.LobbyBotSuitNext:
                _suit = (byte)((_suit + 1) % 4); return true;
            case RmlUiIntentKind.LobbyBotLevelNext:
                _level = (byte)((_level + 1) % 4); return true;
        }

        RmlUiAdminRequest? request = kind switch
        {
            RmlUiIntentKind.LobbyKick when selected.HasValue
                => Request(state, RmlUiAdminOperation.Kick, selected),
            RmlUiIntentKind.LobbyTransferOwner when selected.HasValue
                => Request(state, RmlUiAdminOperation.TransferOwner, selected),
            RmlUiIntentKind.LobbyClose => Request(state, RmlUiAdminOperation.CloseLobby),
            RmlUiIntentKind.LobbyBotAdd => Request(state, RmlUiAdminOperation.AddBot),
            RmlUiIntentKind.LobbyBotConfigure when selected is { Bot: true }
                => Request(state, RmlUiAdminOperation.UpdateBot, selected),
            RmlUiIntentKind.LobbyBotRemove when selected is { Bot: true }
                => Request(state, RmlUiAdminOperation.RemoveBot, selected),
            RmlUiIntentKind.LobbyTeamSelect when selected.HasValue && argument < state.TeamCount
                => Request(state, RmlUiAdminOperation.SetTeam, selected, (sbyte)argument),
            RmlUiIntentKind.LobbyTeamsAuto when selected.HasValue
                => Request(state, RmlUiAdminOperation.SetTeam, selected, -1),
            RmlUiIntentKind.LobbyTeamNext when selected.HasValue && state.TeamCount > 0
                => Request(state, RmlUiAdminOperation.SetTeam, selected,
                    (sbyte)((Math.Max(-1, (int)selected.Value.Team) + 1) % state.TeamCount)),
            RmlUiIntentKind.LobbyHandicapNext when selected.HasValue
                => Request(state, RmlUiAdminOperation.SetHandicap, selected,
                    handicap: (byte)((selected.Value.Handicap + 10) % 60)),
            RmlUiIntentKind.LobbyTeamLockToggle
                => Request(state, RmlUiAdminOperation.ToggleTeamLock),
            RmlUiIntentKind.LobbyMapRetry => Request(state, RmlUiAdminOperation.RetryMap),
            RmlUiIntentKind.LobbyChatSend when !String.IsNullOrWhiteSpace(chat)
                => Request(state, RmlUiAdminOperation.SendChat, text: chat.Trim()),
            _ => null
        };
        if (!request.HasValue) return false;
        if (Destructive(request.Value.Operation))
        {
            if (!state.Owner || state.CommandPending) return false;
            _confirmation = request;
            _confirmText = request.Value.Operation switch
            {
                RmlUiAdminOperation.Kick => "KICK THE SELECTED PLAYER?",
                RmlUiAdminOperation.TransferOwner => "TRANSFER LOBBY OWNERSHIP?",
                RmlUiAdminOperation.RemoveBot => "REMOVE THE SELECTED BOT?",
                _ => "CLOSE THE LOBBY FOR EVERYONE?"
            };
            return true;
        }
        return Submit(state, request.Value);
    }

    public void Close() { Owner(); _closed = true; _confirmation = null; }

    public void Project(Dictionary<string, RmlUiBindingValue> model)
    {
        RmlUiAdminSnapshot? current = _closed ? null : Current();
        void Text(string name, string value) => model[name] = RmlUiBindingValue.FromText(value);
        void Flag(string name, bool value) => model[name] = RmlUiBindingValue.FromBoolean(value);
        Flag("lobby_admin_open", current != null && !_closed);
        if (current == null) return;
        var state = current;
        var selected = Selected(state);
        var selectedPlayer = selected.GetValueOrDefault();
        bool owner = state.Owner && !state.CommandPending && !_confirmation.HasValue;
        bool target = selected.HasValue;
        bool targetOwner = target && selectedPlayer.Slot == state.OwnerSlot;
        bool bot = target && selectedPlayer.Bot;

        Text("admin_title", "LOBBY // " + state.MatchId);
        Text("admin_status", !String.IsNullOrEmpty(state.CommandStatus)
            ? state.CommandStatus : _status);
        Text("admin_selected_name", selected?.Name ?? "SELECT A CONNECTED PLAYER");
        Text("admin_selected_details", target
            ? "SLOT " + (selectedPlayer.Slot + 1) + " // "
              + selectedPlayer.Hunter + " // SUIT " + (selectedPlayer.Suit + 1)
              + " // " + selectedPlayer.Ping + " MS"
            : "SELECT A CURRENT PLAYER TO CHANGE THEIR TEAM OR PERMISSIONS");
        Text("admin_handicap", "DAMAGE REDUCTION: " + (selected?.Handicap ?? 0) + "%");
        Text("admin_bot_hunter", "BOT HUNTER: " + _hunter);
        Text("admin_bot_suit", "BOT SUIT: " + (_suit + 1));
        Text("admin_bot_level", "DIFFICULTY: " +
            new[] { "EASY", "NORMAL", "HARD", "INSANE" }[_level]);
        Text("admin_teams", state.TeamCount == 0
            ? "FREE FOR ALL // TEAMS UNAVAILABLE" : state.TeamCount + " ACTIVE TEAMS");
        Text("admin_team_lock", state.TeamsLocked ? "UNLOCK TEAMS" : "LOCK TEAMS");
        Text("admin_map_status", state.MapStatus ?? "");
        Text("admin_rotation_state", state.CanRetryMap ? "MAP PREPARATION FAILED" : "SERVER MANAGES MAP ROTATION");
        Text("admin_permission_reason", state.Owner
            ? "DESTRUCTIVE ACTIONS REQUIRE CONFIRMATION // SERVER CONTROLS THE RESULT"
            : "ONLY THE OWNER MAY ADMINISTER OTHER PLAYERS");
        Text("admin_chat_history", state.Chat.Length == 0
            ? "NO MESSAGES" : String.Join("\n", state.Chat.TakeLast(32)));
        Text("admin_chat_hint", "MAX 96 UTF-8 BYTES // SERVER VALIDATES CHAT");
        Text("admin_confirmation_text", _confirmText);
        Flag("admin_confirmation_open", _confirmation.HasValue);

        for (int i = 0; i < 8; i++)
        {
            var player = state.Players.FirstOrDefault(p => p.Slot == i);
            bool occupied = state.Players.Any(p => p.Slot == i);
            Flag("admin_slot" + i + "_present", occupied);
            Flag("admin_slot" + i + "_selected", occupied && i == _slot
                && player.Generation == _generation);
            Text("admin_player" + i + "_name", occupied ? player.Name : "OPEN SLOT");
            Text("admin_player" + i + "_details", occupied
                ? player.Hunter + " // SUIT " + (player.Suit + 1)
                  + " // " + (player.Bot ? "BOT" : player.Ready ? "READY" : "WAITING")
                  + (player.Slot == state.OwnerSlot ? " // OWNER" : "")
                : "");
        }
        Flag("admin_team_enabled", target && state.TeamCount > 0
            && (owner || (!state.TeamsLocked && selected!.Value.Slot == state.LocalSlot)));
        Flag("admin_handicap_enabled", owner && target);
        Flag("admin_kick_enabled", owner && target && !bot && !targetOwner);
        Flag("admin_transfer_enabled", owner && target && !bot && !targetOwner);
        Flag("admin_bot_add_enabled", owner && state.Players.Length < state.MaxPlayers);
        Flag("admin_bot_apply_enabled", owner && bot);
        Flag("admin_bot_remove_enabled", owner && bot);
        Flag("admin_bot_options_enabled", owner);
        Flag("admin_team_lock_enabled", owner && state.TeamCount > 0);
        Flag("admin_map_retry_enabled", state.CanRetryMap && !state.CommandPending);
        Flag("admin_start_enabled", owner);
        Flag("admin_close_lobby_enabled", owner);
        Flag("admin_chat_send_enabled", !state.CommandPending);
        for (int i = 0; i < 4; i++)
        {
            Flag("admin_team" + i + "_present", i < state.TeamCount);
            Flag("admin_team" + i + "_enabled", i < state.TeamCount && target &&
                (owner || (!state.TeamsLocked && selected!.Value.Slot == state.LocalSlot)));
            Text("admin_team" + i + "_label", "TEAM " + (char)('A' + i));
            Flag("admin_team" + i + "_selected", target && selected!.Value.Team == i);
        }
    }
}
