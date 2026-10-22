using System;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>One verified person occupying an authored stage pad.</summary>
public readonly record struct RmlUiStageHunter(
    byte NetSlot, ushort Generation, Hunter Hunter, int Suit,
    bool Ready, bool Local, string Name)
{
    public int Identity => (NetSlot + 1) * 65536 + Generation;
    public int Team { get; init; } = -1;
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
    public ushort MatchId { get; init; }
    public ulong AuthorityEpoch { get; init; }
    public static RmlUiHunterStagePlan Build(HubSnapshot hub,
        RmlUiLobbyView lobby, RmlUiAdminSnapshot? authoritative)
    {
        bool files = hub.GameFilesReady;
        Hunter hero = (uint)hub.DisplayHunter < 7 ? hub.DisplayHunter : Hunter.Samus;
        int suit = Math.Clamp(hub.Suit, 0, 3);
        if (!lobby.Active)
            return new(files, false, false, hero, suit, Array.Empty<RmlUiStageHunter>());
        if (!files)
            return new(false, true, lobby.Starting, hero, suit,
                Array.Empty<RmlUiStageHunter>());

        // Never synthesize lobby model identities from display names. If the
        // versioned roster is absent or invalid, use empty pads until it arrives.
        if (authoritative == null || authoritative.MatchId == 0
            || authoritative.AuthorityEpoch == 0 || authoritative.Players == null)
            return new(files, true, lobby.Starting, hero, suit,
                Array.Empty<RmlUiStageHunter>());

        // Use the same duplicate rejection and local-first order as the labels
        // and team controls, including malformed authoritative snapshots.
        RmlUiAdminPlayer[] players = RmlUiLobbyDisplayOrder.Ordered(authoritative);
        var ordered = new RmlUiStageHunter[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            var player = players[i];
            ordered[i] = new(player.Slot, player.Generation, player.Hunter,
                player.Suit, player.Ready,
                player.Slot == authoritative.LocalSlot, player.Name ?? "")
            {
                Team = authoritative.TeamCount is > 0 and <= 4
                    && player.Team >= 0 && player.Team < authoritative.TeamCount ? player.Team : -1
            };
        }
        // Stable ordering and the exact formation geometry are shared with
        // the native shader and the RML lobby nameplates.
        return new(files, true, lobby.Starting, hero, suit, ordered)
        { MatchId = authoritative.MatchId, AuthorityEpoch = authoritative.AuthorityEpoch };
    }
}
