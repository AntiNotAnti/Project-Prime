using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

/// <summary>Explicit developer-only offline fixture. No native slots, transport,
/// prediction, replay reconstruction or publication is enabled by this prototype.</summary>
internal sealed class OfflineCustomWeaponRuntime : IDisposable
{
    private sealed class Slot
    {
        internal required PlayerEntity Player;
        internal required EquipInfo Equip;
        internal required WeaponRuntimeState State;
        internal ushort Life = 1;
        internal bool Alive = true, Emitting;
    }
    private readonly Scene _scene;
    private readonly WeaponRegistry _registry;
    private readonly WeaponDefinition _definition;
    private readonly Slot?[] _slots = new Slot[8];
    private bool _disposed;
    internal ulong AcceptedShots { get; private set; }
    internal CustomImpactFact? LastImpact { get; private set; }
    internal bool IsEligible => !_disposed && !NetSession.Active && !_scene.Services.IsReplica
        && !DemoPlayback.IsActive && !_scene.IsReplayLab && _scene.AimTrainer == null
        && _scene.GameState.Mode == GameMode.Battle && !_scene.GameState.InstaGib
        && !_scene.GameState.OneInTheChamber && !_scene.GameState.NoImperialist && !_scene.GameState.BalancedMode;
    private uint Tick => (uint)_scene.FrameCount;
    internal OfflineCustomWeaponRuntime(Scene scene, bool developerOptIn)
    {
        _scene = scene;
        if (!developerOptIn || !IsEligible) throw new InvalidOperationException("Custom rifle requires explicit offline Battle developer opt-in.");
        foreach (var enemy in scene.GetEnemyInstanceEntities())
            throw new InvalidOperationException("Custom campaign/enemy damage is unavailable in this prototype.");
        _definition = RiftlineM7.Create();
        _registry = scene.FreezeCustomWeaponRegistry(new[] { _definition });
    }
    internal bool Grant(PlayerEntity player)
    {
        if (!IsEligible || player.OwningScene != _scene || player.Health <= 0 || (uint)player.SlotIndex >= 8) return false;
        if (_slots[player.SlotIndex] != null) return false;
        var inventory = _registry.Inventory(player.SlotIndex);
        if (!inventory.BindLife(new(1,1,(byte)player.SlotIndex,1,1),Tick) || !inventory.Grant(_definition.Identity,Tick)) return false;
        inventory.TryGetState(_definition.Identity,out var state);
        var pool = new BeamProjectileEntity[_definition.Mechanics.MaxActiveProjectiles];
        for (int i=0; i<pool.Length; i++) pool[i] = new(_scene);
        _slots[player.SlotIndex] = new Slot { Player = player, State = state!,
            Equip = new EquipInfo(CustomPrecisionAdapter.Build(_definition.Mechanics),pool) { InfiniteAmmo = true } };
        state!.SetEquipped(Tick,true);
        return true;
    }
    private Slot? Resolve(PlayerEntity player)
    {
        if (!IsEligible || player.OwningScene != _scene || (uint)player.SlotIndex >= 8) return null;
        var slot = _slots[player.SlotIndex];
        if (slot == null || slot.Player != player) return null;
        Observe(slot);
        return slot.Alive ? slot : null;
    }
    private void RetireBeams(Slot slot)
    {
        foreach (var beam in slot.Equip.Beams) { beam.Destroy(); _scene.RemoveEntity(beam); }
    }
    private void Observe(Slot slot)
    {
        bool alive = slot.Player.Health > 0 && !slot.Player.Flags2.TestFlag(PlayerFlags2.Spectating);
        if (!alive)
        {
            if (slot.Alive) { slot.State.Invalidate(WeaponCancellation.Death); RetireBeams(slot); }
            slot.Alive = false; return;
        }
        if (!slot.Alive)
        {
            slot.Life = unchecked((ushort)(slot.Life+1)); if (slot.Life == 0) slot.Life = 1;
            var inventory = _registry.Inventory(slot.Player.SlotIndex);
            if (!inventory.BindLife(new(1,1,(byte)slot.Player.SlotIndex,1,slot.Life),Tick)
                || !inventory.Grant(_definition.Identity,Tick)) return;
            inventory.TryGetState(_definition.Identity,out var state); slot.State = state!; slot.Alive = true;
        }
        bool biped = !slot.Player.IsAltForm && !slot.Player.IsMorphing && !slot.Player.IsUnmorphing;
        slot.State.SetEquipped(Tick,biped);
        slot.State.Advance(Tick);
    }
    internal void Step()
    {
        if (_disposed) return;
        if (!IsEligible) { Dispose(); return; }
        foreach (var slot in _slots) if (slot != null) Observe(slot);
    }
    internal WeaponRuntimeState? State(PlayerEntity player) => Resolve(player)?.State;
    internal WeaponActionResult Reload(PlayerEntity player,uint sequence)
        => Resolve(player)?.State.TryReload(Tick,sequence) ?? WeaponActionResult.InvalidLife;
    internal WeaponActionResult Optic(PlayerEntity player,uint sequence,int optic)
        => Resolve(player)?.State.TrySetOptic(Tick,sequence,optic) ?? WeaponActionResult.InvalidLife;
    internal WeaponActionResult Fire(PlayerEntity player,uint shotId,Vector3 direction)
    {
        var slot = Resolve(player); if (slot == null) return WeaponActionResult.InvalidLife;
        float length = direction.LengthSquared;
        bool finite = float.IsFinite(length) && length > .000001f && length < 1e6f
            && float.IsFinite(player.Position.X) && float.IsFinite(player.Position.Y) && float.IsFinite(player.Position.Z);
        bool capacity = false;
        foreach (var beam in slot.Equip.Beams) if (beam.Lifespan <= 0) { capacity = true; break; }
        var before = slot.State.Snapshot();
        var result = slot.State.TryFire(Tick,shotId,capacity && finite);
        if (result != WeaponActionResult.Accepted) return result;
        var snapshot = slot.State.Snapshot();
        slot.Equip.CustomShot = CustomShotSource.ForOffline(_definition,snapshot.Owner,shotId);
        slot.Equip.Zoomed = snapshot.Optic != 0;
        direction = direction.Normalized();
        int spread = snapshot.Optic == 0 ? _definition.Mechanics.HipSpreadMilliDegrees : _definition.Mechanics.ScopedSpreadMilliDegrees;
        if (spread > 0)
        {
            uint seed = unchecked(shotId*747796405u+2891336453u);
            float angle = MathHelper.DegreesToRadians(spread/1000f) * MathF.Sqrt((seed & 65535)/65535f);
            float azimuth = ((seed >> 16)/65535f)*MathF.Tau;
            Vector3 right = MathF.Abs(direction.Y) < .99f ? Vector3.Cross(direction,Vector3.UnitY).Normalized() : Vector3.UnitX;
            Vector3 up = Vector3.Cross(right,direction).Normalized();
            direction = (direction*MathF.Cos(angle)+(right*MathF.Cos(azimuth)+up*MathF.Sin(azimuth))*MathF.Sin(angle)).Normalized();
        }
        slot.Emitting = true;
        try
        {
            // A finite engine-owned muzzle, never an arbitrary creator/client point.
            var origin = player.Position + new Vector3(0,.4f,0);
            var flags = BeamSpawnFlags.NoMuzzle;
            if (player.DoubleDamage) flags |= BeamSpawnFlags.DoubleDamage;
            else if (player.IsPrimeHunter) flags |= BeamSpawnFlags.PrimeHunter;
            var emitted = BeamProjectileEntity.SpawnWithEmission(player,slot.Equip,origin,direction,
                flags,player.NodeRef,_scene,out _);
            if (emitted == BeamResultFlags.NoSpawn)
            {
                slot.State.CancelUnemittedFire(shotId,before);
                return WeaponActionResult.CapacityUnavailable;
            }
            AcceptedShots++;
            return WeaponActionResult.Accepted;
        }
        finally { slot.Emitting = false; }
    }
    internal bool OwnsEmission(EntityBase owner,EquipInfo equip)
    {
        if (!IsEligible || owner is not PlayerEntity player || (uint)player.SlotIndex >= 8) return false;
        var slot = _slots[player.SlotIndex];
        return slot is { Emitting: true } && slot.Player == player && ReferenceEquals(slot.Equip,equip)
            && equip.CustomShot is { } source && source.Definition == _definition
            && source.Owner == slot.State.Owner && source.Sequence == slot.State.Snapshot().LastAcceptedShotId;
    }
    internal void Impact(CustomShotSource source,PlayerEntity victim,int before,int turretBefore,DamageFlags flags,Vector3 point)
    {
        if (!IsEligible || source.Definition.Identity != _definition.Identity) return;
        int bodyDamage = Math.Max(0,before-victim.Health);
        int turretDamage = Math.Max(0,turretBefore-(victim.Halfturret?.Health ?? 0));
        if (bodyDamage+turretDamage == 0) return;
        LastImpact = new(source.Definition.Identity,source.Identity,victim.SlotIndex,Tick,bodyDamage+turretDamage,
            turretDamage,flags.TestFlag(DamageFlags.Halfturret),
            flags.TestFlag(DamageFlags.Headshot),victim.Health == 0,point);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var slot in _slots)
            if (slot != null) { slot.State.Invalidate(WeaponCancellation.MatchChanged); RetireBeams(slot); }
        Array.Clear(_slots);
    }
}
