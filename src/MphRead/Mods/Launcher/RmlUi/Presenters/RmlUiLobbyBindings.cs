using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Host;

// Copied session facts only. Both platform adapters can hydrate the same
// documents without owning another session clock or manufacturing roster data.
public static class RmlUiLobbyBindings
{
    private static readonly string[] TeamColors = { "ORANGE", "GREEN", "BLUE", "VIOLET" };

    private static void PresentSlot(Action<string, string> text, Action<string, bool> flag,
        LobbySnapshot snapshot, LobbyPresentation model, int index, LobbyPlayerSnapshot? occupant)
    {
        string prefix = $"slot{index}_";
        bool occupied = occupant.HasValue;
        var player = occupant.GetValueOrDefault();
        var row = occupied ? model.Players.FirstOrDefault(p => p.Player.Slot == player.Slot) : null;
        bool teamsEnabled = occupied && !player.IsSpectator && model.Teams.Length > 0;
        bool team = teamsEnabled && player.Team >= 0 && player.Team < model.Teams.Length;
        flag(prefix + "occupied", occupied);
        flag(prefix + "ready", occupied && player.Ready);
        flag(prefix + "local", occupied && player.Slot == snapshot.LocalSlot);
        flag(prefix + "can_select", occupied || model.CanAddBot);
        flag(prefix + "has_team", teamsEnabled);
        flag(prefix + "can_team", row?.CanChangeTeam == true);
        for (int t = 0; t < 4; t++) flag(prefix + "team" + t, team && player.Team == t);
        text(prefix + "team", teamsEnabled ? (team ? TeamColors[player.Team] : "AUTO") + (row?.CanChangeTeam == true ? " >" : "") : "");
        text(prefix + "name", occupied ? (String.IsNullOrWhiteSpace(player.Name) ? $"PLAYER {player.Slot + 1}" : player.Name).ToUpperInvariant() : "OPEN SLOT");
        text(prefix + "hunter", occupied ? player.Hunter.ToString().ToUpperInvariant() + (player.IsBot ? " // BOT" : "") : model.CanAddBot ? "+ ADD BOT" : "WAITING FOR PLAYER");
        text(prefix + "state", occupied ? player.IsSpectator ? "SPECTATING" : player.Ready ? "READY" : "WAITING" : "");
    }

    public static void Present(RmlUiLauncherPages pages, LobbySnapshot snapshot)
    {
        pages.SetBool("lobby_mode", true);
        pages.SetText("lobby_chat_history",snapshot.Chat.Length==0?"No messages yet.":string.Join("\n",snapshot.Chat.TakeLast(6)));
        var players = snapshot.Players.OrderByDescending(player => player.Slot == snapshot.LocalSlot).ToArray();
        LobbyPresentation model = LobbyPresentation.From(snapshot);
        for (int index = 0; index < 8; index++)
            PresentSlot((id, value) => pages.SetText(id, value), (id, value) => pages.SetBool(id, value),
                snapshot, model, index, index < players.Length ? players[index] : null);
        pages.SetText("lobby_name", String.IsNullOrWhiteSpace(snapshot.Context?.ServerName) ? "MULTIPLAYER LOBBY" : snapshot.Context.ServerName.ToUpperInvariant());
        pages.SetText("lobby_player_count", $"{players.Length} / {snapshot.MaxPlayers}");
        pages.SetText("lobby_ready_count", $"{players.Count(player => player.Ready)} READY");
        bool localReady = players.Any(player => player.Slot == snapshot.LocalSlot && player.Ready);
        pages.SetBool("lobby_local_ready", localReady);
        pages.SetBool("lobby_require_ready", (snapshot.RuleFlags & LobbyRuleFlags.RequireReady) != 0);
        pages.SetBool("lobby_owner", snapshot.IsOwner);
        pages.SetBool("lobby_starting", snapshot.Phase == SessionPhase.Starting);
        pages.SetText("lobby_countdown_number", snapshot.CountdownSeconds > 0 ? Math.Max(1, (int)Math.Ceiling(snapshot.CountdownSeconds)).ToString() : "GET READY");
        pages.SetText("lobby_countdown_detail", snapshot.CountdownSeconds > 0 ? "MATCH STARTING" : snapshot.StartStage == StartStage.Synchronizing ? "SYNCHRONIZING PLAYERS" : "LOADING MAP");
        pages.SetBool("lobby_can_start", model.CanStart);
        pages.SetText("lobby_ready_action", localReady ? "UNREADY" : "READY");
        var local = players.FirstOrDefault(player => player.Slot == snapshot.LocalSlot);
        pages.SetText("lobby_local_hunter", (players.Any(player => player.Slot == snapshot.LocalSlot) ? local.Hunter : snapshot.LocalHunter).ToString().ToUpperInvariant());
        if (snapshot.Match is { } match)
        {
            pages.SetText("lobby_map", match.RoomKey.ToUpperInvariant());
            pages.SetText("lobby_mode_name", match.Mode.ToString().ToUpperInvariant());
            pages.SetText("lobby_format", match.Format.ToString().ToUpperInvariant());
            pages.SetText("lobby_time", match.TimeLimitSeconds == 0 ? "UNLIMITED" : LobbyRuleEditValues.DurationText(match.TimeLimitSeconds));
            pages.SetText("lobby_score", match.PointGoal == 0 ? "UNLIMITED" : LobbyRuleEditValues.GoalText(match.Mode, match.PointGoal));
        }
        else { pages.SetText("lobby_map", "WAITING FOR MAP"); pages.SetText("lobby_mode_name", "MULTIPLAYER"); pages.SetText("lobby_format", "PENDING"); }
        string status = snapshot.CommandError.Length > 0 ? snapshot.CommandError : snapshot.Message;
        if (String.IsNullOrWhiteSpace(status)) status = snapshot.Phase == SessionPhase.Starting
            ? snapshot.CountdownSeconds > 0 ? $"MATCH STARTING // {Math.Max(1, (int)Math.Ceiling(snapshot.CountdownSeconds))}" : "SYNCHRONIZING MATCH"
            : "CONNECTED // " + (snapshot.Context?.Endpoint ?? "WAITING FOR PLAYERS");
        pages.SetText("lobby_status", status.ToUpperInvariant());
    }
}
