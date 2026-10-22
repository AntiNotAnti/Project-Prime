using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Network;

internal readonly record struct NcxObserverLife(ulong Epoch, ushort Match, int Slot, ushort Generation, ushort Life);
internal readonly record struct NcxObserverState(NcxObserverLife Life, bool Alive);

// Report-time diagnostic only: observing a live participant and its replicated
// native source emission does not establish a legal hit, payment or pixels.
internal static class NcxObserverEvidence
{
    internal static bool HasEvidence(bool enabled, bool observerRole, int localSlot, long frames,
        ulong epoch, ushort match, long overflow, int capacity,
        IEnumerable<NcxObserverState> states, IEnumerable<ShotKey> committed, IEnumerable<ShotKey> offered)
    {
        if (!enabled || !observerRole || localSlot != 2 || frames < 60 || epoch == 0 || match == 0
            || overflow != 0 || capacity <= 0) return false;
        var alive = new HashSet<NcxObserverLife>();
        int count = 0;
        foreach (var state in states)
        {
            if (++count > capacity) return false;
            var life = state.Life;
            if (state.Alive && life.Epoch == epoch && life.Match == match && life.Slot is 0 or 1
                && life.Generation != 0 && life.Life != 0) alive.Add(life);
        }
        count = 0;
        foreach (var key in offered)
        {
            if (++count > capacity || key.ShooterSlot == localSlot) return false;
        }
        int participants = 0;
        count = 0;
        foreach (var key in committed)
        {
            // Continue scanning after both participants matched: a later own
            // emission must still invalidate the no-fire observer role.
            if (++count > capacity || key.ShooterSlot == localSlot) return false;
            if (key.AuthorityEpoch != epoch || key.MatchId != match || key.ShooterSlot is not (0 or 1)
                || key.Generation == 0 || key.LifeId == 0 || key.ShotId == 0) continue;
            var life = new NcxObserverLife(key.AuthorityEpoch, key.MatchId, key.ShooterSlot, key.Generation, key.LifeId);
            if (alive.Contains(life)) participants |= 1 << key.ShooterSlot;
        }
        return participants == 3;
    }
}

internal static partial class NetCampaignEvidence
{
    internal static bool NcxObserverHasEvidence => NcxObserverEvidence.HasEvidence(
        Enabled, HitRig.NcxObserverRole, NetSession.LocalSlot, _frames,
        NetSession.AuthorityEpoch, NetSession.CurrentMatchId,
        // General overflow covers the bounded States, Shots and Offered sets.
        // An opted-in timeline must not hide dropped or unkeyed observations.
        _overflow != 0 || (_shotTimeline?.Overflow ?? 0) != 0 || (_shotTimeline?.UnkeyedRows ?? 0) != 0 ? 1 : 0,
        RowLimit,
        States.Select(state => new NcxObserverState(new(state.Epoch, state.Match, state.Slot,
            state.Generation, state.Life), state.Hp > 0)),
        Shots.Where(shot => shot.Stage == "committed").Select(shot => shot.Key), Offered);
}
