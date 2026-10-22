using System;

namespace MphRead.Mods.Network;

/// <summary>Fixed open-addressed per attacker/victim partitions. Backward-shift deletion
/// keeps failed lookups short after expiration and consumption, without tombstones.</summary>
public sealed class NetRescueIndex
{
    private const int Partition = 512, Pairs = 64;
    private struct Entry
    {
        public ShotKey Key;
        public ushort VictimGeneration, VictimLife;
        public uint At;
        public int Owed;
    }
    private readonly Entry[] _entries = new Entry[Partition * Pairs];
    public int Count { get; private set; }
    public long LookupProbes { get; private set; }
    public int LookupHighWater { get; private set; }
    private static int Home(in ShotKey key)
    {
        unchecked
        {
            ulong h = key.AuthorityEpoch ^ ((ulong)key.MatchId << 32) ^ ((ulong)key.Generation << 16) ^ key.LifeId;
            h ^= key.ShotId * 0x9e3779b97f4a7c15UL;
            h = (h ^ (h >> 30)) * 0xbf58476d1ce4e5b9UL;
            return (int)(h ^ (h >> 32)) & (Partition - 1);
        }
    }
    private void Delete(int start, int offset)
    {
        int hole = offset; _entries[start + hole] = default; Count--;
        for (int n = 1; n < Partition; n++)
        {
            int scan = (offset + n) & (Partition - 1);
            ref var entry = ref _entries[start + scan];
            if (entry.Owed == 0) break;
            int home = Home(entry.Key);
            if (((hole - home) & (Partition - 1)) < ((scan - home) & (Partition - 1)))
            { _entries[start + hole] = entry; entry = default; hole = scan; }
        }
    }
    private int Find(int attacker, int victim, in ShotKey key, ushort generation, ushort life, uint now)
    {
        int start = (attacker * 8 + victim) * Partition, home = Home(key);
        int probes = 0;
        for (int n = 0; n < Partition;)
        {
            int offset = (home + n) & (Partition - 1); probes++;
            ref var entry = ref _entries[start + offset];
            if (entry.Owed == 0) { Record(probes); return ~ (start + offset); }
            if (unchecked(now - entry.At) > 720 || entry.VictimGeneration != generation || entry.VictimLife != life)
            {
                Delete(start, offset);
                // Deletion can shift another entry into this cell. Bound cleanup
                // independently; at most a partition can be expired on one lookup.
                if (probes < 2 * Partition) continue;
                break;
            }
            if (entry.Key == key) { Record(probes); return start + offset; }
            n++;
        }
        Record(probes); return int.MinValue;
    }
    private void Record(int probes) { LookupProbes += probes; LookupHighWater = Math.Max(LookupHighWater, probes); }
    public bool CanInsert(int attacker, int victim, in ShotKey key, ushort generation, ushort life, uint now)
        => Find(attacker, victim, key, generation, life, now) != int.MinValue;
    public bool Insert(int attacker, int victim, in ShotKey key, ushort generation, ushort life, uint now)
    {
        int index = Find(attacker, victim, key, generation, life, now);
        if (index == int.MinValue) return false;
        if (index < 0) { index = ~index; Count++; }
        ref var entry = ref _entries[index];
        entry.Key = key; entry.VictimGeneration = generation; entry.VictimLife = life; entry.At = now; entry.Owed++;
        return true;
    }
    public bool Consume(int attacker, int victim, in ShotKey key, ushort generation, ushort life, uint now)
    {
        int index = Find(attacker, victim, key, generation, life, now);
        if (index < 0) return false;
        if (_entries[index].Owed == 1) Delete(index / Partition * Partition, index % Partition);
        else _entries[index].Owed--;
        return true;
    }
    public void ForgetSlot(int slot, bool preserveFlights)
    {
        for (int pair = 0; pair < Pairs; pair++)
            if (pair % 8 == slot || !preserveFlights && pair / 8 == slot)
            {
                int start = pair * Partition;
                for (int i = 0; i < Partition; i++) if (_entries[start + i].Owed > 0) Count--;
                Array.Clear(_entries, start, Partition);
            }
    }
    public void Reset() { Array.Clear(_entries); Count = 0; LookupProbes = 0; LookupHighWater = 0; }
    public void Validate()
    {
        int count = 0; foreach (var entry in _entries) if (entry.Owed > 0) count++;
        if (count != Count) throw new InvalidOperationException("Rescue index count mismatch");
    }
}
