using System;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Deferred work after all players have moved. Action T is a completed
    /// snapshot boundary: advance T+1 through N, then skip the ordinary N step.
    /// Current N colliders are live; earlier colliders are immutable history.
    /// </summary>
    public sealed class ProjectileCatchUp
    {
        public const int Capacity = 512;
        private readonly ServerCombat _combat;
        private readonly Entry[] _entries = new Entry[Capacity];
        private int _read, _count;
        private bool _draining;
        private readonly record struct Entry(BeamProjectileEntity Beam, uint Generation, uint FirstTick);
        internal uint? CollisionTick { get; private set; }
        public long ProjectilesCaughtUp { get; private set; }
        public long Steps { get; private set; }
        public long Collisions { get; private set; }
        public long QueueDrops { get; private set; }
        public uint MaxSteps { get; private set; }
        public int Pending => _count - _read;

        internal ProjectileCatchUp(ServerCombat combat) => _combat = combat;

        internal void Enqueue(BeamProjectileEntity beam, bool inherited)
        {
            if (!_combat.ProjectileCatchUpEnabled || !beam.CombatShot.IsValid
                || beam.TimingMode != LagCompensationMode.ProjectileCatchUp) return;
            // A child born during later ordinary simulation is already at
            // present time; inheriting attribution must not restart catch-up.
            if (inherited && !_draining) return;
            uint rewind = Math.Min(beam.CombatShot.RewindTicks, LagCompensationPolicy.MaxProjectileFastForwardTicks);
            if (rewind == 0) return;
            // A child born at collision K receives only K+1..N. Enqueueing,
            // rather than recursively stepping, bounds both work and stack depth.
            uint first = _draining && inherited && CollisionTick.HasValue
                ? unchecked(CollisionTick.Value + 1) : unchecked(_combat.Tick - rewind + 1);
            if (unchecked(_combat.Tick - first) >= 0x80000000u) return;
            if (_count == Capacity) { QueueDrops++; return; }
            beam.CatchUpPending = true;
            _entries[_count++] = new(beam, beam.Generation, first);
        }

        public void Drain()
        {
            if (_draining) throw new InvalidOperationException("Projectile catch-up cannot recurse.");
            _draining = true;
            try
            {
                while (_read < _count)
                {
                    Entry entry = _entries[_read];
                    _entries[_read++] = default;
                    BeamProjectileEntity beam = entry.Beam;
                    if (beam.Generation != entry.Generation) continue;
                    beam.CatchUpPending = false;
                    uint steps = 0;
                    for (uint tick = entry.FirstTick; unchecked(_combat.Tick - tick) < LagCompensationPolicy.MaxProjectileFastForwardTicks; tick++)
                    {
                        if (beam.Generation != entry.Generation || beam.Lifespan <= 0 || beam.Flags.TestFlag(BeamFlags.Collided)) break;
                        CollisionTick = tick;
                        bool alive = beam.ProcessCatchUpStep(out bool collided);
                        Steps++; steps++;
                        if (collided) Collisions++;
                        if (beam.Generation != entry.Generation) break;
                        if (!alive) { beam.RemoveAfterCatchUp(); break; }
                        if (collided) break;
                        if (tick == _combat.Tick) break;
                    }
                    if (steps > 0) ProjectilesCaughtUp++;
                    MaxSteps = Math.Max(MaxSteps, steps);
                }
            }
            finally
            {
                CollisionTick = null;
                _draining = false;
                for (int i = _read; i < _count; i++)
                    if (_entries[i].Beam.Generation == _entries[i].Generation) _entries[i].Beam.CatchUpPending = false;
                Array.Clear(_entries, 0, _count);
                _read = _count = 0;
            }
        }

        public void Clear()
        {
            if (_draining) throw new InvalidOperationException("Cannot reset active projectile catch-up.");
            for (int i = _read; i < _count; i++)
                if (_entries[i].Beam.Generation == _entries[i].Generation) _entries[i].Beam.CatchUpPending = false;
            Array.Clear(_entries);
            _read = _count = 0;
            ProjectilesCaughtUp = Steps = Collisions = QueueDrops = 0;
            MaxSteps = 0;
            CollisionTick = null;
        }
    }
}
