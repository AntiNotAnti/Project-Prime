using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

internal sealed partial class OfflineCustomWeaponRuntime : ICustomPickupFunding
{
    private readonly record struct PendingAmmo(uint Tick,int Amount);
    private sealed partial class Slot
    {
        internal uint OwnedFrom;
        internal uint? PickupAutoSelect;
        internal ulong PickupRevision;
        internal readonly PendingAmmo[] PickupAmmo = new PendingAmmo[CustomSourceActionJournal.PickupFundingCapacity];
        internal int PickupAmmoCount;
    }
    private readonly CustomPickupLedger? _pickups;
    private PlayerEntity? _pickupActor;
    private bool _pickupWasFirst;
    internal CustomPickupLedger? Pickups => _pickups;
    private static bool Before(uint a,uint b) => unchecked((int)(a-b))<0;
    private static ulong NewOfflineEpoch()
    {
        // Identity only: never consume or seed gameplay simulation RNG.
        Span<byte> bytes=stackalloc byte[8];ulong epoch;
        do { RandomNumberGenerator.Fill(bytes);epoch=BinaryPrimitives.ReadUInt64LittleEndian(bytes); } while(epoch==0);
        return epoch;
    }
    private bool BindPickupLife(PlayerEntity player)
    {
        if (_availabilityPlayer!=player || player.OwningScene!=_scene || player.Health<=0 || !player.ModIsInPlay
            || player.Flags2.TestFlag(PlayerFlags2.Spectating) || _slots[0]!=null) return false;
        var owner = new WeaponOwnerContext(1,_offlineEpoch,0,1,1);
        if (!_registry.Inventory(0).BindLife(owner,Tick)) return false;
        var pool = new BeamProjectileEntity[_definition.Mechanics.MaxActiveProjectiles];
        for (int i=0;i<pool.Length;i++) pool[i]=new(_scene);
        _slots[0]=new() { Player=player,Owner=owner,Selected=false,
            Equip=new EquipInfo(CustomPrecisionAdapter.Build(_definition.Mechanics),pool) { InfiniteAmmo=true } };
        return true;
    }
    private static void ClearPickupFunding(Slot slot)
    { slot.PickupAutoSelect=null;Array.Clear(slot.PickupAmmo);slot.PickupAmmoCount=0; }
    private void ApplyPickupFunding(Slot slot)
    {
        if (slot.State==null) return;
        int used=0;
        while (used<slot.PickupAmmoCount&&!Before(Tick,slot.PickupAmmo[used].Tick))
        {
            var grant=slot.PickupAmmo[used++];slot.State.AddPickupReserve(grant.Tick,grant.Amount);
        }
        if (used==0) return;
        Array.Copy(slot.PickupAmmo,used,slot.PickupAmmo,0,slot.PickupAmmoCount-used);
        Array.Clear(slot.PickupAmmo,slot.PickupAmmoCount-used,used);slot.PickupAmmoCount-=used;
    }
    private WeaponStateSnapshot ProjectPickupFunding(Slot slot,uint tick)
    {
        var value=slot.State!.Snapshot();
        for (int i=0;i<slot.PickupAmmoCount;i++)
        {
            var grant=slot.PickupAmmo[i];if (Before(tick,grant.Tick)) break;
            value=WeaponRuntimeState.Project(value,_definition.Mechanics,grant.Tick);
            value=value with { Reserve=Math.Min(_definition.Mechanics.MaximumReserve,value.Reserve+grant.Amount) };
        }
        return WeaponRuntimeState.Project(value,_definition.Mechanics,tick);
    }
    bool ICustomPickupFunding.TryFund(CustomPickupItemKey item,in CustomPickupContact contact,WeaponRuntimeId weapon,
        int reserveRefill,out CustomPickupFundingResult funded)
    {
        funded=default;
        if (!IsEligible || _pickupActor is not { } player || _availabilityPlayer!=player
            || _slots[0] is not {Alive:true} slot || slot.Player!=player || slot.Owner!=contact.Owner
            || player.Health<=0 || !player.ModIsInPlay || player.Flags2.TestFlag(PlayerFlags2.Spectating)
            || contact.SourceFrame!=Tick || contact.AuthorityFrame!=Tick || weapon.Value!=10
            || _pickups==null || item.World!=_pickups.World || slot.PickupRevision==ulong.MaxValue
            || slot.PickupAmmoCount==slot.PickupAmmo.Length) return false;
        uint effective=unchecked(Tick+1);bool first=slot.State==null;
        if (first)
        {
            var inventory=_registry.Inventory(0);
            if (!inventory.Grant(_definition.Identity,effective)) return false;
            inventory.TryGetState(_definition.Identity,out var state);slot.State=state;
            slot.OwnedFrom=effective;slot.Selected=false;slot.PickupAutoSelect=effective;
        }
        else slot.PickupAmmo[slot.PickupAmmoCount++]=new(effective,reserveRefill);
        slot.PickupRevision++;_pickupWasFirst=first;
        funded=new(ProjectPickupFunding(slot,effective),slot.PickupRevision);return true;
    }
    internal void ProcessPickupContact(PlayerEntity player)
    {
        if (_pickups==null || _pickupActor!=null || !IsEligible || _availabilityPlayer!=player) return;
        if (_slots[0]==null) BindPickupLife(player);
        if (_slots[0] is not { } slot) return;
        Observe(slot);if (!slot.Alive || !_pickups.Advance(Tick)) return;
        var pose=player.ModWeaponPickupContactPose(Tick,Tick);
        var contact=new CustomPickupContact(slot.Owner,Tick,Tick,pose.Position,pose.SpherePosition,pose.Radius,
            pose.MinHeight,pose.MaxHeight,pose.Alt,pose.Alive,pose.CanPickup);
        if (!contact.IsValid || !contact.Alive || !contact.CanPickup) return;
        _pickupActor=player;
        try
        {
            for (int i=0;i<_pickups.Layout.Count;i++)
            {
                if (!_pickups.TryState(i,out var item) || !item.Available
                    || !contact.Reaches(_pickups.Layout.Placements[i].Position)) continue;
                var result=_pickups.TryCollect(item.Key,contact,this,out var outcome);
                if (result==CustomPickupCollectResult.Fault) { Dispose();return; }
                if (result!=CustomPickupCollectResult.Collected) continue;
                player.ModPlayCustomPickupCue(_pickupWasFirst);
                if (!_pickups.AcknowledgePublication(outcome.Revision))
                    throw new InvalidOperationException("Offline pickup outcome was not retained.");
            }
        }
        finally { _pickupActor=null; }
    }
    // Evaluated once against fresh local controls on the next fixed input phase.
    // A blocked acquisition never creates a delayed auto-selection retry.
    internal void ApplyPickupAutoSelection(PlayerEntity player,bool allowed,bool explicitSelection)
    {
        if (_availabilityPlayer!=player || _slots[0] is not { } slot
            || slot.PickupAutoSelect is not { } tick || Before(Tick,tick)) return;
        slot.PickupAutoSelect=null;
        if (!allowed || explicitSelection || Resolve(player)!=slot || player.IsAltForm || player.IsMorphing
            || player.IsUnmorphing || player.Flags2.TestFlag(PlayerFlags2.Shooting) || player.Controls.Shoot.IsDown
            || !player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector,out var facts) || !facts.CanUseWeapon
            || !player.ModCustomPickupOutranksCurrent()) return;
        if (slot.State!.SetEquipped(Tick,true)==WeaponActionResult.Accepted) slot.Selected=true;
    }

    internal void DrawPickups()
    {
        if (!IsEligible || Headless.Active || _pickups==null) return;
        for (int i=0;i<_pickups.Layout.Count;i++)
        {
            if (!_pickups.TryState(i,out var item) || !item.Available) continue;
            CustomPickupPresentation.Draw(_scene,_pickups.Layout.Placements[i].Position);
        }
    }

}
