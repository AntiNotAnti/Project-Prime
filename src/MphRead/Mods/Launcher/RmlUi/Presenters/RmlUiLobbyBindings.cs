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
    public static void Present(RmlUiLauncherPages pages, LobbySnapshot snapshot)
    {
        pages.SetBool("lobby_mode", true);
        var players = snapshot.Players.OrderByDescending(player => player.Slot == snapshot.LocalSlot).ToArray();
        for (int index = 0; index < 8; index++)
        {
            bool occupied = index < players.Length;
            LobbyPlayerSnapshot player = occupied ? players[index] : default;
            pages.SetBool($"slot{index}_occupied", occupied);
            pages.SetBool($"slot{index}_ready", occupied && player.Ready);
            pages.SetBool($"slot{index}_local", occupied && player.Slot == snapshot.LocalSlot);
            pages.SetText($"slot{index}_name", occupied ? (String.IsNullOrWhiteSpace(player.Name) ? $"PLAYER {player.Slot + 1}" : player.Name).ToUpperInvariant() : "");
            pages.SetText($"slot{index}_hunter", occupied ? player.Hunter.ToString().ToUpperInvariant() : "");
            pages.SetText($"slot{index}_state", occupied ? player.IsSpectator ? "SPECTATING" : player.Ready ? "READY" : "WAITING" : "");
        }
        LobbyPresentation model = LobbyPresentation.From(snapshot);
        pages.SetText("lobby_name", String.IsNullOrWhiteSpace(snapshot.Context?.ServerName) ? "MULTIPLAYER LOBBY" : snapshot.Context.ServerName.ToUpperInvariant());
        pages.SetText("lobby_player_count", $"{players.Length} / {snapshot.MaxPlayers}");
        pages.SetText("lobby_ready_count", $"{players.Count(player => player.Ready)} READY");
        bool localReady = players.Any(player => player.Slot == snapshot.LocalSlot && player.Ready);
        pages.SetBool("lobby_local_ready", localReady);
        pages.SetBool("lobby_require_ready", (snapshot.RuleFlags & LobbyRuleFlags.RequireReady) != 0);
        pages.SetBool("lobby_owner", snapshot.IsOwner);
        pages.SetBool("lobby_starting", snapshot.Phase == SessionPhase.Starting);
        pages.SetBool("lobby_can_start", model.CanStart);
        pages.SetText("lobby_ready_action", localReady ? "UNREADY" : "READY");
        var local = players.FirstOrDefault(player => player.Slot == snapshot.LocalSlot);
        pages.SetText("lobby_local_hunter", (players.Any(player => player.Slot == snapshot.LocalSlot) ? local.Hunter : snapshot.LocalHunter).ToString().ToUpperInvariant());
        if (snapshot.Match is { } match)
        {
            pages.SetText("lobby_map", match.RoomKey.ToUpperInvariant());
            pages.SetText("lobby_mode_name", match.Mode.ToString().ToUpperInvariant());
            pages.SetText("lobby_format", match.Format.ToString().ToUpperInvariant());
        }
        else { pages.SetText("lobby_map", "WAITING FOR MAP"); pages.SetText("lobby_mode_name", "MULTIPLAYER"); pages.SetText("lobby_format", "PENDING"); }
        string status = snapshot.CommandError.Length > 0 ? snapshot.CommandError : snapshot.Message;
        if (String.IsNullOrWhiteSpace(status)) status = snapshot.Phase == SessionPhase.Starting
            ? snapshot.CountdownSeconds > 0 ? $"MATCH STARTING // {Math.Max(1, (int)Math.Ceiling(snapshot.CountdownSeconds))}" : "SYNCHRONIZING MATCH"
            : "CONNECTED // " + (snapshot.Context?.Endpoint ?? "WAITING FOR PLAYERS");
        pages.SetText("lobby_status", status.ToUpperInvariant());
    }
}
