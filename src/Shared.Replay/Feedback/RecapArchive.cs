using System;
using MphRead.Mods.Network;

namespace MphRead.Combat
{
    /// <summary>Completed local lives, bounded independently of the current-life history.</summary>
    public sealed partial class RecapArchive
    {
        public const int Capacity = 16;
        public sealed class LifeRecap
        {
            private readonly DamageHistoryEntry[] _damage = new DamageHistoryEntry[DamageHistory.Capacity];
            public CombatActor Life { get; internal set; }
            public uint DeathTick { get; internal set; }
            public string Heading { get; internal set; } = "";
            public string Weapon { get; internal set; } = "Unknown";
            public ushort FinalDamage { get; internal set; }
            public string Final { get; internal set; } = "";
            internal bool HasKill;
            public int Count { get; internal set; }
            public DamageHistoryEntry this[int index] => index >= 0 && index < Count ? _damage[index] : throw new ArgumentOutOfRangeException(nameof(index));
            internal void Add(DamageHistoryEntry entry)
            {
                if (Count == DamageHistory.Capacity) Array.Copy(_damage, 1, _damage, 0, --Count);
                _damage[Count++] = entry;
            }
            internal void Reset()
            {
                Life = CombatActor.None; DeathTick = 0; Heading = Final = ""; Weapon = "Unknown";
                FinalDamage = 0; Count = 0; HasKill = false; Array.Clear(_damage);
            }
            internal void Format() => Final = FinalDamage > 0 ? $"{Weapon}  {FinalDamage} damage" : Weapon;
        }
        private readonly LifeRecap[] _lives = new LifeRecap[Capacity];
        public int Count { get; private set; }
        public uint Revision { get; private set; }
        public LifeRecap this[int index] => index >= 0 && index < Count ? _lives[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public RecapArchive() { for (int i = 0; i < Capacity; i++) _lives[i] = new(); }
        public void Clear() { Count = 0; foreach (var life in _lives) life.Reset(); Revision++; }
        private LifeRecap? Find(CombatActor actor)
        { for (int i = 0; i < Count; i++) if (_lives[i].Life == actor) return _lives[i]; return null; }
        public void Capture(CombatActor actor, DamageHistory history, CombatFeedbackState state, uint tick, string? killWeapon = null)
        {
            if (!actor.IsValid || !state.Dead) return;
            LifeRecap? life = Find(actor);
            if (life == null)
            {
                if (Count == Capacity)
                {
                    life = _lives[0]; Array.Copy(_lives, 1, _lives, 0, Capacity - 1); _lives[Capacity - 1] = life;
                    life.Reset(); Count--;
                }
                life = _lives[Count++]; life.Life = actor;
            }
            life.DeathTick = tick;
            life.Count = 0;
            for (int i = 0; i < history.Count; i++) life.Add(history[i]);
            if (killWeapon != null) { life.HasKill = true; life.Weapon = killWeapon; }
            else if (!life.HasKill && history.Count > 0) life.Weapon = CombatFeedback.WeaponName(history[history.Count - 1].Weapon);
            if (killWeapon != null || !life.HasKill) life.Heading = state.RecapHeading;
            life.FinalDamage = state.FinalDamage; life.Format(); Revision++;
        }
        public void ApplyLate(in CombatEvent value, string source, string weapon)
        {
            LifeRecap? life = Find(value.Target);
            if (life == null || value.Kind != CombatEventKind.Damage || value.Amount == 0) return;
            life.Add(new(value.Actor, value.Tick, value.Amount, value.Weapon, $"{source}  {weapon}  {value.Amount}"));
            if (value.Health == 0)
            {
                life.FinalDamage = value.Amount;
                if (!life.HasKill) { life.Weapon = weapon; life.Heading = $"Eliminated by {source}"; }
                life.Format();
            }
            Revision++;
        }
        public void ApplyLate(in KillEvent value, string source, string weapon)
        {
            LifeRecap? life = Find(value.Victim);
            if (life == null) return;
            life.HasKill = true; life.Heading = $"Eliminated by {source}"; life.Weapon = weapon; life.Format(); Revision++;
        }
    }
}
