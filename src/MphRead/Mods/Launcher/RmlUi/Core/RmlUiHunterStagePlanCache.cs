using System;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// Render-thread cache of immutable presentation facts. Compares roster values,
/// not a revision promise or mutable array identity, before reusing a plan.
/// </summary>
public sealed class RmlUiHunterStagePlanCache
{
    private RmlUiHunterStagePlan? _plan;

    public RmlUiHunterStagePlan Read(HubSnapshot hub, RmlUiLobbyView lobby, RmlUiAdminSnapshot? roster)
    {
        if (_plan == null || !Matches(_plan, hub, lobby, roster))
            _plan = RmlUiHunterStagePlan.Build(hub, lobby, roster);
        return _plan;
    }

    public void Clear() => _plan = null;

    private static bool Matches(RmlUiHunterStagePlan plan, HubSnapshot hub,
        RmlUiLobbyView lobby, RmlUiAdminSnapshot? roster)
    {
        Hunter hero = (uint)hub.DisplayHunter < 7 ? hub.DisplayHunter : Hunter.Samus;
        if (plan.GameFilesReady != hub.GameFilesReady || plan.Lobby != lobby.Active
            || plan.Starting != (lobby.Active && lobby.Starting)
            || plan.HomeHunter != hero || plan.HomeSuit != Math.Clamp(hub.Suit, 0, 3))
            return false;
        if (!lobby.Active || !hub.GameFilesReady || roster == null
            || roster.MatchId == 0 || roster.AuthorityEpoch == 0 || roster.Players == null)
            return plan.Count == 0 && plan.MatchId == 0 && plan.AuthorityEpoch == 0;
        if (plan.MatchId != roster.MatchId || plan.AuthorityEpoch != roster.AuthorityEpoch)
            return false;

        // Match Ordered's filter-before-duplicate-rejection semantics without
        // constructing a GroupBy, List, array or new plan on an unchanged frame.
        Span<int> counts = stackalloc int[8];
        counts.Clear();
        foreach (var actor in roster.Players)
            if (Valid(actor) && counts[actor.Slot] < 2) counts[actor.Slot]++;
        int count = 0;
        for (int slot = 0; slot < 8; slot++) if (counts[slot] == 1) count++;
        if (count != plan.Count) return false;
        bool localFirst = roster.LocalSlot < 8 && counts[roster.LocalSlot] == 1;
        foreach (var actor in roster.Players)
        {
            if (!Valid(actor) || counts[actor.Slot] != 1) continue;
            bool local = actor.Slot == roster.LocalSlot;
            int index = 0;
            if (!local)
            {
                index = localFirst ? 1 : 0;
                for (int slot = 0; slot < actor.Slot; slot++)
                    if (slot != roster.LocalSlot && counts[slot] == 1) index++;
            }
            var cached = plan.Formation[index];
            int team = roster.TeamCount is > 0 and <= 4
                && actor.Team >= 0 && actor.Team < roster.TeamCount ? actor.Team : -1;
            if (cached.NetSlot != actor.Slot || cached.Generation != actor.Generation
                || cached.Hunter != actor.Hunter || cached.Suit != actor.Suit
                || cached.Ready != actor.Ready || cached.Local != local || cached.Team != team
                || !String.Equals(cached.Name, actor.Name ?? "", StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool Valid(RmlUiAdminPlayer actor) => actor.Slot < 8 && actor.Generation > 0
        && (uint)actor.Hunter < 7 && actor.Suit < 4;
}
