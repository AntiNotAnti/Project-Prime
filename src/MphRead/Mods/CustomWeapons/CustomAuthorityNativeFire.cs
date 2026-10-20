using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal sealed partial class CustomAuthorityCombatAdapter
{
    private readonly CustomNativeFireGate?[] _nativeFireGates=new CustomNativeFireGate?[8];
    private readonly WeaponOwnerContext[] _nativeGateOwners=new WeaponOwnerContext[8];
    private readonly ushort[] _nativeGateGenerations=new ushort[8];
    CustomNativeSelection ICustomNativeSelectionPolicy.ReadNativeSelection(WeaponOwnerContext owner,uint source)
        =>ReadNativeSourceSelection(owner,source);
    bool ICustomNativeSelectionPolicy.TryReleaseNativeSelection(WeaponOwnerContext owner,uint source)
        =>TryReleaseNativeSourceSelection(owner,source);
    internal bool HasNativeFireGate(int slot)=>(uint)slot<8&&_nativeFireGates[slot]!=null;
    internal bool HasNativeSelectionHistory(WeaponOwnerContext owner)=>owner.IsValid&&Find(owner)?.Journal!=null;
    private bool RequiresNativeAdmission(WeaponOwnerContext owner)
        =>HasNativeSelectionHistory(owner)||_nativeGateOwners[owner.Slot]==owner||_sourceBoundary?.IsConfirmed(owner)==true;
    internal bool FilterNativeIntent(byte slot,in IntentPacket input,ulong connection,out IntentPacket filtered)
    {
        filtered=default;
        if(slot>=8||connection==0||input.MatchId!=_match||input.AuthorityEpoch!=_epoch
            ||input.SlotGeneration==0||input.LifeId==0||!NetFireEvents.Validate(input))return false;
        var owner=new WeaponOwnerContext(input.MatchId,input.AuthorityEpoch,slot,input.SlotGeneration,input.LifeId);
        if(_nativeGateGenerations[slot]!=owner.Generation)
        {_nativeFireGates[slot]=new(slot,this);_nativeGateGenerations[slot]=owner.Generation;_nativeGateOwners[slot]=default;}
        var gate=_nativeFireGates[slot]??=new(slot,this);
        if(input.LifeId==NetPlayerLifecycle.Get(slot))_nativeGateOwners[slot]=owner;
        filtered=input;filtered.FireEventCount=0;filtered.FireEvents=default;
        // Modern custom-capable sources cannot evade the event gate by choosing
        // the legacy held-button emission path. Physical buttons remain intact.
        filtered.HasFireEvents=true;
        for(int i=0;i<input.FireEventCount;i++)
            if(gate.Offer(input,input.FireEvents[i],connection,Now)==CustomNativeFireDecision.Released)
                filtered.FireEvents[filtered.FireEventCount++]=input.FireEvents[i];
        return true;
    }
    internal bool TryTakeNativeIntent(int slot,out IntentPacket input,out ulong connection,out ShotKey key)
    {
        input=default;connection=0;key=default;
        return (uint)slot<8&&_nativeFireGates[slot]?.TryTake(Now,out input,out connection,out key)==true;
    }
    internal bool ReleaseNativeIntent(in ShotKey key,ulong connection)
        =>(uint)key.ShooterSlot<8&&_nativeFireGates[key.ShooterSlot]?.Release(key,connection,Now)==true;
    internal void RejectNativeIntent(in ShotKey key)
    {if((uint)key.ShooterSlot<8)_nativeFireGates[key.ShooterSlot]?.Reject(key);}
    internal void ConfirmNativeIntent(in ShotKey key,in FireEvent fire)
    {if((uint)key.ShooterSlot<8)_nativeFireGates[key.ShooterSlot]?.Confirm(key,fire);}
    internal void FilterAdmittedNativeEvents(int slot,ref IntentPacket intent)
    {
        if((uint)slot>=8||_nativeFireGates[slot] is not {} gate)return;
        var original=intent.FireEvents;int count=intent.FireEventCount;
        intent.FireEvents=default;intent.FireEventCount=0;
        for(int i=0;i<count;i++)
        {
            var fire=original[i];var key=new ShotKey(intent.AuthorityEpoch,intent.MatchId,slot,intent.SlotGeneration,
                fire.SourceLife!=0?fire.SourceLife:intent.LifeId,fire.ShotId);
            var decision=gate.ReadAdmitted(key,out var retained);
            // A newcomer outside custom readiness keeps the native-only path.
            // Known offers require successful native intake before publication.
            if(retained.ShotId==0||decision==CustomNativeFireDecision.Released&&retained==fire)
                intent.FireEvents[intent.FireEventCount++]=fire;
        }
    }
    internal CustomNativeFireDecision ReadNativeClaim(int slot,in HitClaimPacket claim)
    {
        // Bomb/alt-form damage has separate actual-form ownership. The victim's
        // halfturret flag is never a shooter exemption for ordinary native beams.
        if(claim.Beam>(byte)BeamType.OmegaCannon)return CustomNativeFireDecision.Released;
        if((uint)slot>=8||claim.MatchId!=_match||claim.AuthorityEpoch!=_epoch
            ||claim.ShooterGeneration==0||claim.ShooterLifeId==0)return CustomNativeFireDecision.Rejected;
        var owner=new WeaponOwnerContext(claim.MatchId,claim.AuthorityEpoch,(byte)slot,claim.ShooterGeneration,claim.ShooterLifeId);
        var key=new ShotKey(claim.AuthorityEpoch,claim.MatchId,slot,claim.ShooterGeneration,claim.ShooterLifeId,claim.ShotId);
        if(NetSession.TryGetAuthorityTurretEmission(key,out var turret))
            return Compatible(turret,claim)?CustomNativeFireDecision.Released:CustomNativeFireDecision.Rejected;
        var gate=_nativeFireGates[slot];
        if(gate==null)return RequiresNativeAdmission(owner)
            ?CustomNativeFireDecision.Pending:CustomNativeFireDecision.Released;
        var decision=gate.ReadAdmitted(key,out var fire);
        if(fire.ShotId==0&&!RequiresNativeAdmission(owner))
            return CustomNativeFireDecision.Released;
        if(decision!=CustomNativeFireDecision.Released)return decision;
        return Compatible(fire,claim)?CustomNativeFireDecision.Released:CustomNativeFireDecision.Rejected;
    }
    private static bool Compatible(in FireEvent fire,in HitClaimPacket claim)
    {
        if(fire.Weapon!=claim.Beam||claim.LaunchFrame!=fire.AckFrame)return false;
        if((fire.Kind==FireEventKind.ContinuousTick)!=((claim.Flags&HitClaimPacket.FlagContinuousTick)!=0))
            return false;
        // Frame is the continuous source phase, unlike LaunchFrame (world ACK).
        if(fire.Kind==FireEventKind.ContinuousTick&&fire.ContinuousPhase!=claim.Frame)
            return false;
        return true;
    }
}

internal static class CustomNativeClaimSource
{
    internal static void Confirm(in ShotKey key,in FireEvent fire)
    {
        if(NetSession.IsAuthority&&(uint)key.ShooterSlot<PlayerEntity.Players.Count)
            PlayerEntity.Players[key.ShooterSlot].OwningScene.CustomCombat?.ConfirmNativeIntent(key,fire);
    }
    internal static void FilterAdmitted(int slot,ref IntentPacket intent)
    {
        if(NetSession.IsAuthority&&(uint)slot<PlayerEntity.Players.Count)
            PlayerEntity.Players[slot].OwningScene.CustomCombat?.FilterAdmittedNativeEvents(slot,ref intent);
    }
    internal static CustomNativeFireDecision Read(int slot,in HitClaimPacket claim)
    {
        if(!NetSession.IsAuthority||(uint)slot>=PlayerEntity.Players.Count)return CustomNativeFireDecision.Released;
        return PlayerEntity.Players[slot].OwningScene.CustomCombat?.ReadNativeClaim(slot,claim)??CustomNativeFireDecision.Released;
    }
}
