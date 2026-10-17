using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>One verified person occupying an authored stage pad.</summary>
public readonly record struct RmlUiStageHunter(
    byte NetSlot, ushort Generation, Hunter Hunter, int Suit,
    bool Ready, bool Local, string Name)
{
    public int Identity => (NetSlot + 1) * 65536 + Generation;
}

/// <summary>
/// Exact roster-to-stage presentation contract. The server retains ownership of
/// Hunter, suit and slot-generation facts; this merely puts the local player
/// on the front platform and the other occupied slots behind them.
/// </summary>
public sealed record RmlUiHunterStagePlan(
    bool GameFilesReady, bool Lobby, bool Starting,
    Hunter HomeHunter, int HomeSuit, RmlUiStageHunter[] Formation)
{
    public int Count => Formation.Length;
    public static RmlUiHunterStagePlan Build(HubSnapshot hub,
        RmlUiLobbyView lobby, RmlUiAdminSnapshot? authoritative)
    {
        bool files = hub.GameFilesReady;
        Hunter hero = (uint)hub.DisplayHunter < 7 ? hub.DisplayHunter : Hunter.Samus;
        int suit = Math.Clamp(hub.Suit, 0, 3);
        if (!files || !lobby.Active)
            return new(files, false, false, hero, suit, Array.Empty<RmlUiStageHunter>());

        // Never synthesize lobby model identities from display names. If the
        // versioned roster is absent or invalid, use empty pads until it arrives.
        if (authoritative == null || authoritative.MatchId == 0
            || authoritative.AuthorityEpoch == 0 || authoritative.Players == null)
            return new(files, true, lobby.Starting, hero, suit,
                Array.Empty<RmlUiStageHunter>());

        var found = new List<RmlUiStageHunter>(8);
        var slots = new HashSet<byte>();
        foreach (RmlUiAdminPlayer player in authoritative.Players)
        {
            if (player.Slot >= 8 || player.Generation == 0
                || (uint)player.Hunter >= 7 || player.Suit > 3
                || !slots.Add(player.Slot)) continue;
            found.Add(new(player.Slot, player.Generation, player.Hunter,
                player.Suit, player.Ready,
                player.Slot == authoritative.LocalSlot, player.Name ?? ""));
            if (found.Count == 8) break;
        }
        // Stable ordering and the exact formation geometry are shared with
        // the native shader and the RML lobby nameplates.
        RmlUiStageHunter[] ordered = found.OrderByDescending(x => x.Local)
            .ThenBy(x => x.NetSlot).Take(8).ToArray();
        return new(files, true, lobby.Starting, hero, suit, ordered);
    }
}
