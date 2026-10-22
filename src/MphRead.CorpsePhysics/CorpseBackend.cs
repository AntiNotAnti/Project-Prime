using System.Numerics;

namespace MphRead.CorpsePhysics;

// Owns only copied poses and bounded visual identities. No engine/entity/model
// reference is permitted at this boundary. All operations use one owner thread.
public abstract class CorpseBackend : IRagdollPresentationBackend
{
    private static long _nextWorld;
    private readonly ulong _world;
    protected sealed class Slot
    {
        internal CorpseSpawnDescriptor? Spawn;
        internal uint Generation;
        internal readonly CorpsePose[] Previous = new CorpsePose[CorpseRig.PartCount];
        internal readonly CorpsePose[] Current = new CorpsePose[CorpseRig.PartCount];
        internal bool Active => Spawn != null;
    }
    private struct Seen { internal CorpseKey Key; internal uint Tick; }
    protected readonly Slot[] Slots = Enumerable.Range(0, 8).Select(_ => new Slot()).ToArray();
    private readonly Seen[] _seen = new Seen[64];
    private readonly int _capacity;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _hasTick, _disposed;
    protected bool Faulted;
    protected uint Tick;
    protected int Active;
    protected ulong Steps, Spawned, Retired, Refused, Faults;
    public abstract string Name { get; }
    protected virtual long PoolBytes => 0;
    protected virtual int PhysicalBodies => 0;
    protected virtual int Joints => 0;
    public CorpseStatistics Statistics
    { get { OwnerThread(); return new(Active, PhysicalBodies, Joints, PoolBytes, Steps, Spawned, Retired, Refused, Faults); } }
    protected CorpseBackend(CorpseQuality quality)
    {
        long world = Interlocked.Increment(ref _nextWorld);
        if (world <= 0) throw new InvalidOperationException("Corpse world identity exhausted.");
        _world = (ulong)world; _capacity = Limits.Capacity(quality);
    }
    protected void Owner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OwnerThread();
    }
    protected void OwnerThread()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Corpse world owner thread changed.");
    }
    public bool TrySpawn(CorpseSpawnDescriptor spawn, out CorpseHandle handle)
    {
        Owner(); ArgumentNullException.ThrowIfNull(spawn); handle = default;
        if (Faulted || Active >= _capacity || _hasTick && spawn.SpawnTick != Tick)
        { Refused++; return false; }
        int freeSeen = -1, freeSlot = -1;
        uint now = _hasTick ? Tick : spawn.SpawnTick;
        for (int i = 0; i < _seen.Length; i++)
        {
            if (_seen[i].Key == spawn.Key) { Refused++; return false; }
            if (!_seen[i].Key.IsValid || unchecked(now - _seen[i].Tick) > 240) freeSeen = i;
        }
        for (int i = 0; i < _capacity; i++) if (!Slots[i].Active && Slots[i].Generation < uint.MaxValue) { freeSlot = i; break; }
        if (freeSeen < 0 || freeSlot < 0) { Refused++; return false; }
        for (int i = 0; i < CorpseRig.PartCount; i++)
            if (!spawn.WorldPart(i).IsValid) { Refused++; return false; }
        var slot = Slots[freeSlot];
        ++slot.Generation;
        for (int i = 0; i < CorpseRig.PartCount; i++) slot.Previous[i] = slot.Current[i] = spawn.WorldPart(i);
        slot.Spawn = spawn; Active++;
        try
        {
            OnSpawn(freeSlot, spawn);
            _seen[freeSeen] = new() { Key = spawn.Key, Tick = now };
            if (!_hasTick) { Tick = spawn.SpawnTick; _hasTick = true; }
            handle = new(_world, (byte)freeSlot, slot.Generation); Spawned++; return true;
        }
        catch { DisableAfterFault(); return false; }
    }
    public bool Step(uint fixedTick)
    {
        Owner(); if (Faulted) return false;
        if (!_hasTick) { Tick = fixedTick; _hasTick = true; return false; }
        if (_hasTick && Tick == fixedTick) return false;
        if (_hasTick && unchecked(fixedTick - Tick) != 1)
        { Clear(); Tick = fixedTick; _hasTick = true; return false; }
        Tick = fixedTick; _hasTick = true;
        try
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].Spawn is {} spawn)
                {
                    if (unchecked(fixedTick - spawn.SpawnTick) >= spawn.Lifetime) { RetireSlot(i); continue; }
                    Slots[i].Current.CopyTo(Slots[i].Previous, 0);
                }
            OnStep(); Steps++;
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].Spawn is {} birth)
                    foreach (var pose in Slots[i].Current)
                        if (!pose.IsValid || pose.Position.Y < birth.Origin.Position.Y - 12)
                        { RetireSlot(i); break; }
            return true;
        }
        catch { DisableAfterFault(); return false; }
    }
    public bool Sample(CorpseHandle handle, float alpha, Span<CorpsePose> destination)
    {
        Owner();
        if (Faulted || !handle.IsValid || handle.World != _world || destination.Length < CorpseRig.PartCount || !Slots[handle.Slot].Active
            || Slots[handle.Slot].Generation != handle.Generation) return false;
        var slot = Slots[handle.Slot]; alpha = Limits.Alpha(alpha);
        for (int i = 0; i < CorpseRig.PartCount; i++) destination[i] = CorpsePose.Interpolate(slot.Previous[i], slot.Current[i], alpha);
        return true;
    }
    public bool Retire(CorpseHandle handle)
    {
        Owner(); if (Faulted || !handle.IsValid || handle.World != _world || !Slots[handle.Slot].Active || Slots[handle.Slot].Generation != handle.Generation) return false;
        try { RetireSlot(handle.Slot); return true; }
        catch { DisableAfterFault(); return false; }
    }
    private void RetireSlot(int i)
    {
        if (!Slots[i].Active) return;
        OnRetire(i); Slots[i].Spawn = null; Active--; Retired++;
    }
    private void DisableAfterFault()
    { Faulted = true; Faults++; try { Clear(); } catch { Faults++; } }
    public void Clear()
    {
        Owner(); Exception? failure = null;
        for (int i = 0; i < Slots.Length; i++)
            try { RetireSlot(i); } catch (Exception error) { failure ??= error; }
        Array.Clear(_seen); _hasTick = false;
        if (failure != null) { Faulted = true; throw failure; }
    }
    protected abstract void OnSpawn(int slot, CorpseSpawnDescriptor spawn);
    protected abstract void OnStep();
    protected abstract void OnRetire(int slot);
    protected virtual void OnDispose() { }
    public void Dispose()
    {
        if (_disposed) return; Owner();
        try { Clear(); }
        finally
        {
            try { OnDispose(); foreach (var slot in Slots) slot.Spawn = null; Active = 0; }
            finally { _disposed = true; }
        }
        GC.SuppressFinalize(this);
    }
}

// Standalone comparison/fallback proxy. The game's established native death
// animation remains its fallback until a separately qualified renderer adapter.
public sealed class ProceduralDeathFallback : CorpseBackend
{
    private readonly bool _fall;
    public override string Name => _fall ? "procedural-proxy" : "frozen-pose-baseline";
    public ProceduralDeathFallback(CorpseQuality quality, bool fall = true) : base(quality) => _fall = fall;
    protected override void OnSpawn(int slot, CorpseSpawnDescriptor spawn) { }
    protected override void OnRetire(int slot) { }
    protected override void OnStep()
    {
        if (!_fall) return;
        foreach (var slot in Slots)
            if (slot.Spawn is {} spawn)
            {
                float t = Math.Clamp(unchecked(Tick - spawn.SpawnTick) / 45f, 0, 1); t = t * t * (3 - 2 * t);
                Vector3 direction = spawn.AcceptedDirection; direction.Y = 0;
                if (direction.LengthSquared() < 1e-8f) direction = Vector3.UnitZ; else direction = Vector3.Normalize(direction);
                var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, direction));
                var rotation = Quaternion.CreateFromAxisAngle(axis, t * 1.1f);
                var pivot = spawn.WorldPart(0).Position;
                for (int i = 0; i < CorpseRig.PartCount; i++)
                {
                    var rest = spawn.WorldPart(i);
                    slot.Current[i] = new(pivot + Vector3.Transform(rest.Position - pivot, rotation) - Vector3.UnitY * (t * .35f),
                        Quaternion.Normalize(rotation * rest.Orientation));
                }
            }
    }
}
