using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// Immutable display-only facts from the authoritative multiplayer session.
/// Never treat a projected status or editable form as network authority.
/// </summary>
public readonly record struct RmlUiLobbyChrome(
    string Time, string Score, string ChatHistory, string ChatStatus,
    string FieldCaption, string MapPreviewStatus,
    string CountdownNumber, string CountdownDetail)
{
    public string MapPreviewPath { get; init; } = "";

    public static readonly RmlUiLobbyChrome Empty = new(
        "WAITING", "WAITING", "NO MESSAGES", "NOT CONNECTED",
        "WAITING FOR SERVER", "MAP PREVIEW PENDING",
        "", "");
}

/// <summary>
/// The same local-front presentation order for model pads, lobby nameplates,
/// the roster selector and team commands. A presentation index is *never* a
/// network slot and must be resolved against a fresh server snapshot.
/// </summary>
public static class RmlUiLobbyDisplayOrder
{
    public static RmlUiAdminPlayer[] Ordered(RmlUiAdminSnapshot? roster)
    {
        if (roster == null || roster.Players == null) return Array.Empty<RmlUiAdminPlayer>();
        return roster.Players
            .Where(p => p.Slot < 8 && p.Generation > 0
                && (uint)p.Hunter < 7 && p.Suit < 4)
            .GroupBy(p => p.Slot)
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .OrderByDescending(p => p.Slot == roster.LocalSlot)
            .ThenBy(p => p.Slot)
            .Take(8).ToArray();
    }

    public static RmlUiAdminPlayer? Resolve(RmlUiAdminSnapshot? roster, int index)
    {
        if (index < 0 || index >= 8) return null;
        var ordered = Ordered(roster);
        return index < ordered.Length ? ordered[index] : null;
    }

    public static bool CanChangeTeam(RmlUiAdminSnapshot? roster, RmlUiAdminPlayer? actor)
        => roster != null && actor.HasValue && !roster.CommandPending
            && roster.TeamCount > 0
            && (roster.Owner || (!roster.TeamsLocked && actor.Value.Slot == roster.LocalSlot));

    // Empty display rows advertise capacity, never a particular network slot.
    // The existing server command chooses and validates the free slot.
    public static bool CanAddBot(RmlUiLobbyView lobby, RmlUiAdminSnapshot? roster, int index)
        => lobby.Active && !lobby.Starting && lobby.Owner && roster is { Owner: true, CommandPending: false }
            && roster.MatchId != 0 && roster.AuthorityEpoch != 0
            && roster.MaxPlayers is > 0 and <= 8 && lobby.MaxPlayers == roster.MaxPlayers
            && roster.Players != null && roster.Players.Length < roster.MaxPlayers
            && index >= Math.Max(Ordered(roster).Length, lobby.Players?.Length ?? 0)
            && index >= 0 && index < roster.MaxPlayers;

    public static sbyte NextTeam(RmlUiAdminSnapshot state, RmlUiAdminPlayer actor)
        => (sbyte)((Math.Max(-1, (int)actor.Team) + 1) % state.TeamCount);

    public static string TeamLabel(int team, bool hasTeams)
        => hasTeams ? team is >= 0 and <= 3
            ? "TEAM " + (char)('A' + team) : "AUTO" : "";
}
