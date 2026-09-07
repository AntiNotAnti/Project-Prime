using System;
using MphRead.Mods.Network;

namespace MphRead.Combat
{
    public readonly record struct DamageHistoryEntry(CombatActor Source, uint Tick, ushort Amount, byte Weapon, string Text);

    /// <summary>Facts received for one local life only; no visibility or world-state queries.</summary>
    public sealed partial class DamageHistory
    {
        public const int Capacity = 8;
        private readonly DamageHistoryEntry[] _entries = new DamageHistoryEntry[Capacity];
        public CombatActor Life { get; private set; } = CombatActor.None;
        public int Count { get; private set; }
        public DamageHistoryEntry this[int index] => index >= 0 && index < Count ? _entries[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public void Bind(CombatActor life)
        {
            if (Life == life) return;
            Life = life;
            Count = 0;
            Array.Clear(_entries);
        }
        public void Add(in CombatEvent value, string source, string weapon)
        {
            if (!Life.IsValid || value.Target != Life || value.Amount == 0 || value.Kind != CombatEventKind.Damage) return;
            if (Count == Capacity) Array.Copy(_entries, 1, _entries, 0, --Count);
            _entries[Count++] = new(value.Actor, value.Tick, value.Amount, value.Weapon, $"{source}  {weapon}  {value.Amount}");
        }
    }
}
