using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Bounded optimistic presentation only. Compact events lack shot identity: an
/// amount can possibly overlap a prediction, but can never settle or pay it.
/// </summary>
internal static class PredictionHealthProjection
{
    internal const int Capacity = 24;
    internal readonly record struct Pending(int Damage, ushort BodyCursor, ushort FormCursor,
        int BodyHealth, int FormHealth, byte Attacker, ushort AttackerGeneration, byte Beam);
    internal readonly record struct Accepted(ushort Sequence, ushort Damage);
    internal readonly record struct Result(int Health, int PendingDebit, int PossibleOverlap, bool Ambiguous);

    private static bool After(ushort value, ushort baseline) => NetLifecycleTracker.Newer(value, baseline);
    private static bool Within(ushort value, ushort baseline, ushort sample)
        => value != 0 && After(value, baseline) && (value == sample || After(sample, value));
    private static int Distance(ushort sample, ushort baseline)
    {
        int distance = unchecked((ushort)(sample - baseline));
        // Damage event sequences skip zero on wrap.
        return sample < baseline && sample != 0 ? distance - 1 : distance;
    }
    private static bool Compatible(in DamageEvent e, in Pending p)
        => e.AttackerSlot == p.Attacker && e.AttackerGeneration == p.AttackerGeneration && e.Beam == p.Beam;
    private static bool Same(in DamageEvent a, in DamageEvent b)
        => a.AttackerSlot == b.AttackerSlot && a.AttackerGeneration == b.AttackerGeneration
        && a.Beam == b.Beam && a.Damage == b.Damage && a.Flags == b.Flags;

    internal static Result Project(int canonicalHealth, ushort sample, in PlayerState state,
        ReadOnlySpan<Pending> pending, ReadOnlySpan<Accepted> accepted, bool form = false)
    {
        if (pending.Length > Capacity || accepted.Length > 128) throw new ArgumentOutOfRangeException(nameof(pending));
        Span<int> residual = stackalloc int[Capacity];
        Span<DamageEvent> events = stackalloc DamageEvent[PlayerState.DamageHistory];
        Span<bool> conflict = stackalloc bool[PlayerState.DamageHistory];
        conflict.Clear();
        int count = 0, debit = 0;
        for (int p = 0; p < pending.Length; p++) debit += residual[p] = Math.Max(0, pending[p].Damage);
        if (debit == 0 || canonicalHealth <= 0) return new(canonicalHealth, debit, 0, false);
        for (int i = 0; i < PlayerState.DamageHistory; i++)
        {
            var e = state.EventAt(i);
            if (e.EventId == 0 || (e.EventId != sample && !After(sample, e.EventId))) continue;
            int duplicate = -1;
            for (int j = 0; j < count; j++) if (events[j].EventId == e.EventId) { duplicate = j; break; }
            if (duplicate >= 0) { conflict[duplicate] |= !Same(events[duplicate], e); continue; }
            events[count++] = e;
        }
        // Earlier retained events go first, leaving later events for predictions
        // made after an earlier sample. No event amount is lent twice.
        for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
                if (unchecked((ushort)(sample - events[j].EventId)) > unchecked((ushort)(sample - events[i].EventId)))
                {
                    (events[i], events[j]) = (events[j], events[i]);
                    (conflict[i], conflict[j]) = (conflict[j], conflict[i]);
                }
        int overlap = 0;
        for (int i = 0; i < count; i++)
        {
            var e = events[i];
            bool excluded = false;
            foreach (var a in accepted) if (a.Sequence != 0 && a.Sequence == e.EventId) { excluded = true; break; }
            if (excluded || conflict[i]) continue;
            int available = e.Damage;
            for (int p = 0; p < pending.Length && available > 0; p++)
            {
                var prediction = pending[p];
                ushort cursor = form ? prediction.FormCursor : prediction.BodyCursor;
                if (!Within(e.EventId, cursor, sample) || !Compatible(e, prediction)) continue;
                int amount = Math.Min(available, residual[p]);
                available -= amount; residual[p] -= amount; overlap += amount;
            }
        }
        bool ambiguous = false;
        int unknownUsed = 0;
        // Newer anchors cover narrower suffix intervals. Allocate their missing
        // net drop first; an older anchor then subtracts that already-used suffix.
        for (int p = pending.Length - 1; p >= 0; p--)
        {
            var prediction = pending[p];
            ushort cursor = form ? prediction.FormCursor : prediction.BodyCursor;
            if (!After(sample, cursor)) continue;
            int retained = 0, knownDamage = 0;
            for (int i = 0; i < count; i++)
                if (Within(events[i].EventId, cursor, sample) && !conflict[i])
                { retained++; knownDamage += events[i].Damage; }
            if (retained == Distance(sample, cursor)) continue;
            ambiguous = true;
            // Exact accepted outcomes remain nonoverlapping even after feedback
            // consumption or compact-history eviction. Count each sequence once.
            for (int a = 0; a < accepted.Length; a++)
            {
                var outcome = accepted[a];
                if (!Within(outcome.Sequence, cursor, sample)) continue;
                bool present = false;
                for (int i = 0; i < count; i++)
                    if (events[i].EventId == outcome.Sequence && !conflict[i]) { present = true; break; }
                for (int i = 0; i < a && !present; i++) if (accepted[i].Sequence == outcome.Sequence) present = true;
                if (!present) knownDamage += outcome.Damage;
            }
            // Unknown history can explain at most the otherwise unexplained net
            // health drop since this prediction's own canonical anchor. A new
            // prediction at the current cursor cannot borrow an older missing hit.
            int anchor = form ? prediction.FormHealth : prediction.BodyHealth;
            int budget = Math.Max(0, anchor - canonicalHealth - knownDamage - unknownUsed);
            int possible = Math.Min(residual[p], budget);
            residual[p] -= possible; unknownUsed += possible; overlap += possible;
        }
        return new(Math.Max(1, canonicalHealth - (debit - overlap)), debit, overlap, ambiguous);
    }
}
