using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>Fixed storage; entries expire by simulation tick, never wall time.</summary>
    public sealed class SpawnDangerHistory
    {
        public const int DeathCapacity = 64;
        public const int SpawnCapacity = 128;
        private readonly Death[] _deaths = new Death[DeathCapacity];
        private readonly Use[] _uses = new Use[SpawnCapacity];
        private int _deathNext;
        private int _useNext;
        public int DeathCount { get; private set; }
        public int SpawnCount { get; private set; }
        private readonly record struct Death(Vector3 Position, ulong Tick);
        private readonly record struct Use(int SpawnId, int Slot, ulong Tick);

        public void Reset()
        {
            DeathCount = SpawnCount = _deathNext = _useNext = 0;
        }

        public void RecordDeath(Vector3 position, ulong tick)
        {
            _deaths[_deathNext] = new Death(position, tick);
            _deathNext = (_deathNext + 1) % DeathCapacity;
            DeathCount = Math.Min(DeathCount + 1, DeathCapacity);
        }

        public void RecordSpawn(int spawnId, int slot, ulong tick)
        {
            _uses[_useNext] = new Use(spawnId, slot, tick);
            _useNext = (_useNext + 1) % SpawnCapacity;
            SpawnCount = Math.Min(SpawnCount + 1, SpawnCapacity);
        }

        public float DeathDanger(Vector3 position, ulong tick)
        {
            float danger = 0;
            for (int i = 0; i < DeathCount; i++)
            {
                Death death = _deaths[i];
                if (tick < death.Tick || tick - death.Tick >= 10 * SimTicks.Hz) { continue; }
                float distance = (position - death.Position).LengthSquared;
                if (distance < 100)
                    danger += (1 - distance / 100) * (1 - (tick - death.Tick) / (10f * SimTicks.Hz));
            }
            return danger;
        }

        public float RecentUse(int spawnId, int slot, ulong tick, out bool repeated)
        {
            float danger = 0;
            repeated = false;
            bool foundPlayerUse = false;
            for (int age = 0; age < SpawnCount; age++)
            {
                Use use = _uses[(_useNext - 1 - age + SpawnCapacity) % SpawnCapacity];
                if (tick < use.Tick || tick - use.Tick >= 10 * SimTicks.Hz) { continue; }
                if (!foundPlayerUse && use.Slot == slot)
                {
                    foundPlayerUse = true;
                    repeated = use.SpawnId == spawnId;
                }
                if (use.SpawnId == spawnId)
                    danger += 1 - (tick - use.Tick) / (10f * SimTicks.Hz);
            }
            return danger;
        }
    }
}
