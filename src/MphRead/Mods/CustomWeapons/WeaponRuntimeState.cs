using System;

namespace MphRead.Mods.CustomWeapons;

public readonly record struct WeaponOwnerContext
{
    public ushort MatchId { get; }
    public ulong AuthorityEpoch { get; }
    public byte Slot { get; }
    public ushort Generation { get; }
    public ushort Life { get; }
    public bool IsValid => MatchId != 0 && AuthorityEpoch != 0 && Slot < 8 && Generation != 0 && Life != 0;
    public WeaponOwnerContext(ushort matchId, ulong authorityEpoch, byte slot, ushort generation, ushort life)
    {
        if (matchId == 0 || authorityEpoch == 0 || slot >= 8 || generation == 0 || life == 0)
            throw new ArgumentException("Complete weapon owner/life identity required.");
        MatchId = matchId; AuthorityEpoch = authorityEpoch; Slot = slot; Generation = generation; Life = life;
    }
}
public enum CustomWeaponPhase : byte { Invalid, Unequipped, Ready, BoltCycling, Empty, Reloading }
public enum WeaponActionResult : byte { Accepted, InvalidLife, StaleFrame, Duplicate, Unequipped, Cooldown, Empty, Reloading, NoReserve, Full, CapacityUnavailable, InvalidOptic }
public enum WeaponCancellation : byte { Death, Respawn, Despawn, Spectator, OwnerChanged, MatchChanged, RulesChanged }
public readonly record struct WeaponStateSnapshot(WeaponOwnerContext Owner, WeaponIdentity Weapon,
    uint Tick, CustomWeaponPhase Phase, int Magazine, int Reserve, int Optic,
    uint FireReadyTick, uint ReloadCompleteTick, uint LastAcceptedShotId, uint LastAcceptedShotTick);

/// <summary>Authority-owned deterministic rules only. Call fire after reserving an
/// emitter; rejected capacity records the request without spending or producing facts.</summary>
public sealed class WeaponRuntimeState
{
    public WeaponDefinition Definition { get; }
    public WeaponOwnerContext Owner { get; }
    private bool _valid = true, _equipped, _reloading, _hasFired;
    private uint _tick, _ready, _reloadEnd, _fireRequest, _reloadRequest, _opticRequest, _acceptedId, _acceptedTick;
    private int _magazine, _reserve, _optic;
    public WeaponRuntimeState(WeaponDefinition definition, WeaponOwnerContext owner, uint tick)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!owner.IsValid) throw new ArgumentException("Valid owner required.", nameof(owner));
        Definition = definition; Owner = owner; _tick = tick;
        _magazine = definition.Mechanics.MagazineCapacity; _reserve = definition.Mechanics.InitialReserve;
    }
    private static bool Reached(uint frame, uint deadline) => unchecked((int)(frame - deadline)) >= 0;
    private static bool NewRequest(uint sequence, ref uint highWater)
    {
        if (sequence == 0 || highWater != 0 && unchecked((int)(sequence - highWater)) <= 0) return false;
        highWater = sequence; return true;
    }
    public WeaponActionResult Advance(uint tick)
    {
        if (!_valid) return WeaponActionResult.InvalidLife;
        if (!Reached(tick,_tick)) return WeaponActionResult.StaleFrame;
        _tick = tick;
        if (_reloading && Reached(tick,_reloadEnd))
        {
            _reloading = false;
            int transfer = Math.Min(Definition.Mechanics.MagazineCapacity - _magazine,_reserve);
            _magazine += transfer; _reserve -= transfer;
        }
        return WeaponActionResult.Accepted;
    }
    public WeaponActionResult SetEquipped(uint tick, bool equipped)
    {
        var advance = Advance(tick); if (advance != WeaponActionResult.Accepted) return advance;
        if (_equipped == equipped) return WeaponActionResult.Accepted;
        _equipped = equipped;
        if (!equipped) { _reloading = false; _reloadEnd = 0; _optic = 0; }
        // Preserve bolt/cadence deadlines across swaps. Morph uses this same
        // unequip/cancel contract, never a fresh full-magazine state.
        return WeaponActionResult.Accepted;
    }
    public WeaponActionResult TryFire(uint tick, uint shotId, bool emitterCapacityAvailable)
    {
        if (!_valid) return WeaponActionResult.InvalidLife;
        if (!NewRequest(shotId,ref _fireRequest)) return WeaponActionResult.Duplicate;
        var advance = Advance(tick); if (advance != WeaponActionResult.Accepted) return advance;
        if (!_equipped) return WeaponActionResult.Unequipped;
        if (_reloading) return WeaponActionResult.Reloading;
        if (_hasFired && !Reached(tick,_ready)) return WeaponActionResult.Cooldown;
        if (_magazine == 0) return WeaponActionResult.Empty;
        if (!emitterCapacityAvailable) return WeaponActionResult.CapacityUnavailable;
        _magazine--; _hasFired = true; _ready = unchecked(tick + (uint)Definition.Mechanics.ShotCooldownTicks);
        _acceptedId = shotId; _acceptedTick = tick;
        return WeaponActionResult.Accepted;
    }
    public WeaponActionResult TryReload(uint tick, uint sequence)
    {
        if (!_valid) return WeaponActionResult.InvalidLife;
        if (!NewRequest(sequence,ref _reloadRequest)) return WeaponActionResult.Duplicate;
        var advance = Advance(tick); if (advance != WeaponActionResult.Accepted) return advance;
        if (!_equipped) return WeaponActionResult.Unequipped;
        if (_reloading) return WeaponActionResult.Reloading;
        if (_magazine == Definition.Mechanics.MagazineCapacity) return WeaponActionResult.Full;
        if (_reserve == 0) return WeaponActionResult.NoReserve;
        _reloading = true; _optic = 0;
        _reloadEnd = unchecked(tick + (uint)Definition.Mechanics.ReloadTicks);
        return WeaponActionResult.Accepted;
    }
    public WeaponActionResult TrySetOptic(uint tick, uint sequence, int optic)
    {
        if (!_valid) return WeaponActionResult.InvalidLife;
        if (!NewRequest(sequence,ref _opticRequest)) return WeaponActionResult.Duplicate;
        var advance = Advance(tick); if (advance != WeaponActionResult.Accepted) return advance;
        if (optic < 0 || optic > Definition.Mechanics.OpticMagnificationsMilli.Count) return WeaponActionResult.InvalidOptic;
        if (!_equipped) return WeaponActionResult.Unequipped;
        if (_reloading && optic != 0) return WeaponActionResult.Reloading;
        // Optics may change during a bolt cycle but can never change its deadline.
        _optic = optic; return WeaponActionResult.Accepted;
    }
    public void Invalidate(WeaponCancellation reason)
    { _valid = false; _equipped = false; _reloading = false; _reloadEnd = 0; _optic = 0; }
    public WeaponStateSnapshot Snapshot()
    {
        var phase = !_valid ? CustomWeaponPhase.Invalid : !_equipped ? CustomWeaponPhase.Unequipped
            : _reloading ? CustomWeaponPhase.Reloading : _hasFired && !Reached(_tick,_ready) ? CustomWeaponPhase.BoltCycling
            : _magazine == 0 ? CustomWeaponPhase.Empty : CustomWeaponPhase.Ready;
        return new(Owner,Definition.Identity,_tick,phase,_magazine,_reserve,_optic,_ready,_reloadEnd,_acceptedId,_acceptedTick);
    }
}
