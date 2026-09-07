using System;
using MphRead.Mods.Network;

namespace MphRead.Combat
{
    public readonly record struct DamageContribution(CombatActor Actor, uint Tick, int Damage, int Unhealed);

    /// <summary>Pure assist eligibility. Identity/team eligibility is fixed by authority at admission.</summary>
    public static class AssistPolicy
    {
        public static int EligibleDamage(in DamageContribution contribution, uint tick, uint windowTicks)
        {
            uint age = unchecked(tick - contribution.Tick);
            return age <= windowTicks ? contribution.Damage : contribution.Unhealed;
        }

    }

    /// <summary>One life, at most 64 recent damage facts. Overflow replaces the oldest admitted fact. Healing consumes oldest outstanding damage first.
    /// Recent damage retains its short assist window after healing; old damage qualifies only while unhealed.</summary>
    public sealed class DamageContributionLedger
    {
        private readonly DamageContribution[] _entries = new DamageContribution[64];
        private int _next;
        public CombatActor Target { get; private set; } = CombatActor.None;
        public void Reset(CombatActor target = default)
        {
            Target = target.IsValid ? target : CombatActor.None;
            Array.Clear(_entries);
            _next = 0;
        }
        public void Add(CombatActor target, CombatActor actor, uint tick, int damage, bool hostile)
        {
            if (!target.IsValid) return;
            if (Target != target) Reset(target);
            if (!hostile || !actor.IsValid || actor.Slot == target.Slot || damage <= 0) return;
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Actor.Slot == actor.Slot && _entries[i].Actor != actor) _entries[i] = default;
            _entries[_next] = new(actor, tick, damage, damage);
            _next = (_next + 1) % _entries.Length;
        }
        public void Heal(CombatActor target, int amount)
        {
            if (Target != target || amount <= 0) return;
            for (int offset = 0; offset < _entries.Length && amount > 0; offset++)
            {
                int index = (_next + offset) % _entries.Length;
                int healed = Math.Min(amount, _entries[index].Unhealed);
                _entries[index] = _entries[index] with { Unhealed = _entries[index].Unhealed - healed };
                amount -= healed;
            }
        }
        public int Collect(CombatActor killer, uint tick, int minimumDamage, uint windowTicks,
            Span<CombatActor> output)
        {
            if (minimumDamage < 1 || windowTicks > int.MaxValue) throw new ArgumentOutOfRangeException();
            Span<int> totals = stackalloc int[8];
            Span<CombatActor> identities = stackalloc CombatActor[8];
            totals.Clear();
            identities.Clear();
            foreach (DamageContribution contribution in _entries)
            {
                if (!contribution.Actor.IsValid || contribution.Actor.Slot == killer.Slot || contribution.Actor.Slot == Target.Slot) continue;
                int slot = contribution.Actor.Slot;
                // Replacement connections cannot inherit an earlier identity's contribution.
                if (identities[slot] != contribution.Actor && identities[slot].IsValid) continue;
                identities[slot] = contribution.Actor;
                int amount = AssistPolicy.EligibleDamage(contribution, tick, windowTicks);
                totals[slot] = (int)Math.Min(int.MaxValue, (long)totals[slot] + amount);
            }
            int count = 0;
            if (!killer.IsValid || killer.Slot == Target.Slot) return 0;
            for (int slot = 0; slot < 8; slot++)
                if (identities[slot].IsValid && totals[slot] >= minimumDamage)
                {
                    if (count >= output.Length) throw new ArgumentException("Assist output is too small.", nameof(output));
                    output[count++] = identities[slot];
                }
            return count;
        }
    }
}
