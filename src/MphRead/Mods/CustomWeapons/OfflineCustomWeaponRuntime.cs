using MphRead.Mods.Movement;
using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

/// <summary>Scene-owned offline custom inventory. Public availability captures
/// only the local human and starts unowned; explicit developer grants remain
/// available to fixtures. Native slots and replay reconstruction stay separate.</summary>
internal sealed partial class OfflineCustomWeaponRuntime : IDisposable
{
    private readonly record struct LocalInput(uint Sequence,CustomWeaponActionKind Kind,byte Optic,uint Tick,WeaponOwnerContext Owner);
    private sealed partial class Slot
    {
        internal required PlayerEntity Player;
        internal required EquipInfo Equip;
        internal required WeaponOwnerContext Owner;
        internal WeaponRuntimeState? State;
        internal ushort Life = 1;
        internal bool Alive = true, Emitting, Selected;
        internal uint InputSequence,SettledInputSequence,SelectionDenied,OpticDenied;
        internal readonly LocalInput[] Inputs=new LocalInput[16];
        internal int InputCount;
    }
    private readonly Scene _scene;
    private readonly PlayerEntity? _availabilityPlayer;
    private readonly WeaponRegistry _registry;
    private readonly WeaponDefinition _definition;
    private readonly ulong _offlineEpoch=1;
    internal WeaponDigest Manifest { get; }
    private readonly Slot?[] _slots = new Slot[8];
    private bool _disposed;
    internal WeaponMechanics Mechanics=>_definition.Mechanics;
    internal WeaponDefinition Definition=>_definition;
    internal bool TryPeek(PlayerEntity player,out WeaponStateSnapshot state,out bool selected)
    {
        state=default;selected=false;
        if(!IsEligible||player.OwningScene!=_scene||(uint)player.SlotIndex>=8
            ||_slots[player.SlotIndex] is not {Alive:true,State:not null} slot||slot.Player!=player||player.Health<=0
            ||Before(Tick,slot.OwnedFrom))return false;
        state=slot.State.Snapshot();selected=slot.Selected;return true;
    }
    internal ulong AcceptedShots { get; private set; }
    internal CustomImpactFact? LastImpact { get; private set; }
    internal bool IsEligible => !_disposed && !NetSession.Active && !_scene.Services.IsReplica
        && !DemoPlayback.IsActive && !_scene.IsReplayLab && _scene.AimTrainer == null
        && CustomWeaponMatchPolicy.TryAdmit(_scene,out _)
        && (_availabilityPlayer==null || _scene.Players.Items.Count>0
            && ReferenceEquals(_scene.Players.Items[0],_availabilityPlayer) && !_availabilityPlayer.IsBot);
    private uint Tick => (uint)_scene.FrameCount;
    internal OfflineCustomWeaponRuntime(Scene scene, bool developerOptIn, bool publicAvailability=false, WeaponDefinition? definition=null)
    {
        _scene = scene;
        if (!developerOptIn || !IsEligible) throw new InvalidOperationException("Custom rifle requires explicit offline Battle developer opt-in.");
        if (publicAvailability)
        {
            if (scene.Players.Items.Count==0 || scene.Players.Items[0].IsBot
                || scene.Players.Items[0].OwningScene!=scene || scene.Players.Items[0].SlotIndex!=0)
                throw new InvalidOperationException("Riftline M7 availability requires the local human player.");
            _availabilityPlayer=scene.Players.Items[0];
            _offlineEpoch=NewOfflineEpoch();
        }
        foreach (var enemy in scene.GetEnemyInstanceEntities())
            throw new InvalidOperationException("Custom campaign/enemy damage is unavailable in this prototype.");
        _definition = definition ?? RiftlineM7.Create();
        var manifest = new CustomWeaponManifest(new[] { _definition }); Manifest = manifest.Digest;
        // Validate the authored layout before publishing the scene registry.
        var placements = publicAvailability ? StockRiftlinePickups.ForRoom(scene.Room?.Meta.Name ?? "") : null;
        if (placements != null && definition != null)
            for (int i = 0; i < placements.Length; i++)
                placements[i] = placements[i] with { ReserveRefill = Math.Min(placements[i].ReserveRefill, _definition.Mechanics.MaximumReserve) };
        if (publicAvailability)
            foreach (var map in Mods.MapGen.CustomRooms.Definitions)
                if (string.Equals(map.Name,scene.Room?.Meta.Name,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Custom-map M7 pickup layouts are not available yet.");
        _registry = scene.FreezeCustomWeaponRegistry(new[] { _definition });
        if (placements != null)
        {
            var layout = new CustomPickupLayout(Mods.MapGen.MapContentIdentity.BuiltIn(scene.Room!.Meta.Name),
                manifest,_registry,placements);
            _pickups = new(layout,new(new(1,_offlineEpoch,1),Manifest,layout.Digest),Tick);
        }
    }
    internal bool Grant(PlayerEntity player) => _availabilityPlayer==null && GrantCore(player,selected:true);
    private bool GrantCore(PlayerEntity player,bool selected)
    {
        if (!IsEligible || player.IsBot || player.OwningScene != _scene || player.Health <= 0
            || (_availabilityPlayer!=null && !ReferenceEquals(player,_availabilityPlayer))
            || player.Flags2.TestFlag(PlayerFlags2.Spectating) || (uint)player.SlotIndex >= 8) return false;
        if (_slots[player.SlotIndex] != null) return false;
        var inventory = _registry.Inventory(player.SlotIndex);
        if (!inventory.BindLife(new(1,1,(byte)player.SlotIndex,1,1),Tick) || !inventory.Grant(_definition.Identity,Tick)) return false;
        inventory.TryGetState(_definition.Identity,out var state);
        var pool = new BeamProjectileEntity[_definition.Mechanics.MaxActiveProjectiles];
        for (int i=0; i<pool.Length; i++) pool[i] = new(_scene);
        _slots[player.SlotIndex] = new Slot { Player = player, Owner = state!.Owner, State = state, Selected = selected, OwnedFrom = Tick,
            Equip = new EquipInfo(CustomPrecisionAdapter.Build(_definition.Mechanics),pool) { InfiniteAmmo = true } };
        state!.SetEquipped(Tick,selected);
        return true;
    }
    private Slot? Resolve(PlayerEntity player)
    {
        if (!IsEligible || player.OwningScene != _scene || (uint)player.SlotIndex >= 8) return null;
        var slot = _slots[player.SlotIndex];
        if (slot == null || slot.Player != player) return null;
        Observe(slot);
        return slot.Alive && slot.State != null && !Before(Tick,slot.OwnedFrom) ? slot : null;
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
            if (slot.Alive)
            {
                ExpireInputs(slot);slot.State?.Invalidate(WeaponCancellation.Death);slot.Selected=false;
                ClearPickupFunding(slot);
                // Already emitted public flights retain their original life provenance.
                if (_availabilityPlayer==null) RetireBeams(slot);
            }
            slot.Alive = false; return;
        }
        if (!slot.Alive)
        {
            ushort nextLife=unchecked((ushort)(slot.Life+1));if(nextLife==0)nextLife=1;
            var inventory = _registry.Inventory(slot.Player.SlotIndex);
            var owner = new WeaponOwnerContext(1,_offlineEpoch,(byte)slot.Player.SlotIndex,1,nextLife);
            if (!inventory.BindLife(owner,Tick)) return;
            slot.Life=nextLife;
            slot.Owner=owner;slot.State=null;slot.PickupRevision=0;
            if (_availabilityPlayer==null)
            {
                if (!inventory.Grant(_definition.Identity,Tick)) return;
                inventory.TryGetState(_definition.Identity,out var state);slot.State=state;slot.OwnedFrom=Tick;
            }
            slot.Alive = true;
            slot.Selected=false;slot.InputSequence=slot.SettledInputSequence=slot.SelectionDenied=slot.OpticDenied=0;
            slot.InputCount=0;Array.Clear(slot.Inputs);
        }
        ApplyPickupFunding(slot);
        if (slot.State == null || Before(Tick,slot.OwnedFrom)) return;
        bool biped = !slot.Player.IsAltForm && !slot.Player.IsMorphing && !slot.Player.IsUnmorphing;
        bool usable=biped&&slot.Player.ModTryCaptureCustomPrecombatFacts(slot.Player.ModGunVector,out var facts)&&facts.CanUseWeapon;
        slot.State.SetEquipped(Tick,usable&&slot.Selected);
        slot.State.Advance(Tick);
    }
    internal void Step()
    {
        if (_disposed) return;
        if (!IsEligible) { Dispose(); return; }
        _pickups?.Advance(Tick);
        if (_availabilityPlayer is { } local && _slots[0]==null) BindPickupLife(local);
        foreach (var slot in _slots) if (slot != null)
        {
            // A source that never reached its post-aim biped phase cannot fire
            // later after thaw/respawn. No delayed retry crosses input steps.
            ExpireInputs(slot);Observe(slot);
        }
    }
    internal WeaponRuntimeState? State(PlayerEntity player) => Resolve(player)?.State;
    internal CustomInputProjection? InputProjection(PlayerEntity player)
        =>Resolve(player) is {} slot?new(slot.Owner,slot.Selected,(byte)slot.State!.Snapshot().Optic,
            slot.SettledInputSequence,slot.SelectionDenied,slot.OpticDenied):null;
    internal bool SubmitInput(PlayerEntity player,CustomWeaponActionKind kind,byte optic,out uint sequence)
    {
        sequence=0;var slot=Resolve(player);
        if(slot==null||slot.InputSequence==uint.MaxValue||slot.InputCount==slot.Inputs.Length
            ||(byte)kind<(byte)CustomWeaponActionKind.Fire||(byte)kind>(byte)CustomWeaponActionKind.Unequip)return false;
        sequence=++slot.InputSequence;
        slot.Inputs[slot.InputCount++]=new(sequence,kind,optic,Tick,slot.Owner);
        return true;
    }
    private void CompleteResult(Slot slot,in LocalInput input,WeaponActionResult result)
    {
        _scene.ApplyCustomFeedback(new(default,default,input.Owner,input.Sequence,input.Kind,result,result==WeaponActionResult.Accepted));
        slot.SettledInputSequence=input.Sequence;
        if(result==WeaponActionResult.Accepted)return;
        if(input.Kind is CustomWeaponActionKind.Equip or CustomWeaponActionKind.Unequip)slot.SelectionDenied=input.Sequence;
        if(input.Kind is CustomWeaponActionKind.Reload or CustomWeaponActionKind.Optic)slot.OpticDenied=input.Sequence;
    }
    private void ExpireInputs(Slot slot)
    {
        for(int i=0;i<slot.InputCount;i++)CompleteResult(slot,slot.Inputs[i],WeaponActionResult.InvalidLife);
        slot.InputCount=0;Array.Clear(slot.Inputs);
    }
    internal void CompleteInput(PlayerEntity player)
    {
        if(!IsEligible||player.OwningScene!=_scene||(uint)player.SlotIndex>=8
            ||_slots[player.SlotIndex] is not {} slot||slot.Player!=player||slot.InputCount==0)return;
        Observe(slot);
        if(!slot.Alive||slot.State==null||Before(Tick,slot.OwnedFrom)||slot.InputCount==0)return;
        int count=slot.InputCount;slot.InputCount=0;
        for(int i=0;i<count;i++)
        {
            var input=slot.Inputs[i];slot.Inputs[i]=default;
            bool usable=input.Tick==Tick&&input.Owner==slot.Owner
                &&player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector,out var facts)&&facts.CanUseWeapon;
            WeaponActionResult result;
            switch(input.Kind)
            {
                case CustomWeaponActionKind.Equip:
                    result=usable?slot.State.SetEquipped(Tick,true):WeaponActionResult.InvalidLife;
                    if(result==WeaponActionResult.Accepted)slot.Selected=true;break;
                case CustomWeaponActionKind.Unequip:
                    result=usable?slot.State.SetEquipped(Tick,false):WeaponActionResult.InvalidLife;
                    if(result==WeaponActionResult.Accepted)slot.Selected=false;break;
                case CustomWeaponActionKind.Fire:
                    result=usable?Fire(player,input.Sequence,player.ModGunVector):WeaponActionResult.InvalidLife;break;
                case CustomWeaponActionKind.Reload:
                    result=usable?Reload(player,input.Sequence):WeaponActionResult.InvalidLife;break;
                default:
                    result=usable?Optic(player,input.Sequence,input.Optic):WeaponActionResult.InvalidLife;break;
            }
            CompleteResult(slot,input,result);
        }
    }
    internal WeaponActionResult Reload(PlayerEntity player,uint sequence)
        => Resolve(player)?.State!.TryReload(Tick,sequence) ?? WeaponActionResult.InvalidLife;
    internal WeaponActionResult Optic(PlayerEntity player,uint sequence,int optic)
        => Resolve(player)?.State!.TrySetOptic(Tick,sequence,optic) ?? WeaponActionResult.InvalidLife;
    internal WeaponActionResult Fire(PlayerEntity player,uint shotId,Vector3 direction)
    {
        var slot = Resolve(player); if (slot == null) return WeaponActionResult.InvalidLife;
        float length = direction.LengthSquared;
        bool finite = float.IsFinite(length) && length > .000001f && length < 1e6f
            && float.IsFinite(player.Position.X) && float.IsFinite(player.Position.Y) && float.IsFinite(player.Position.Z);
        bool capacity = false;
        foreach (var beam in slot.Equip.Beams) if (beam.Lifespan <= 0) { capacity = true; break; }
        var before = slot.State!.Snapshot();
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
            var origin = player.Position + Vector3.UnitY * (TraversalStance.CustomMuzzleOffset(player.ModCrouched) + (player.ModTucked ? ParkourStance.TuckRise : 0));
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
            && slot.State != null && source.Owner == slot.Owner && source.Sequence == slot.State.Snapshot().LastAcceptedShotId;
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
            if (slot != null) { slot.State?.Invalidate(WeaponCancellation.MatchChanged); RetireBeams(slot); }
        _pickups?.Retire();
        Array.Clear(_slots);
    }
}
