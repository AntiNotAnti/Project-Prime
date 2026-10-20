using System;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal sealed partial class CustomAuthorityCombatAdapter : ICustomPickupFunding
{
    internal CustomPickupLedger? Pickups {get;private set;}
    private PlayerEntity? _pickupActor;
    // Freeze before the first owner binds, using the authority's match definition.
    internal void ConfigurePickups(MatchStartIdentity start,MapContentIdentity map)
    {
        if(!_pickupOnly||Pickups!=null||!IsEligible||Array.Exists(_lives,value=>value!=null)
            ||start.MatchId!=_match||start.AuthorityEpoch!=_epoch||start.StartGeneration==0)
            throw new InvalidOperationException("Pickup world must be frozen before owner binding.");
        var layout=new CustomPickupLayout(map,Manifest,_registry,StockRiftlinePickups.ForRoom(map.RoomKey));
        Pickups=new(layout,new(start,Manifest.Digest,layout.Digest),Now);
    }
    private bool CapturePickupInput(Life life,in CustomSourceInput input,in CustomSourceReadyContext context)
    {
        if(!_pickupOnly)return true;
        if(Pickups is not {} pickups)return false;
        life.Knowledge??=new(context.PeerIncarnation,pickups.Layout,pickups.World,context.Ready.Bootstrap,
            life.Owner,new(10),context.FirstSourceFrame,context.OpenedWorld);
        var captured=life.Knowledge.CaptureAcceptedInput(context.PeerIncarnation,context.Ready.Bootstrap,input.Owner,input.Frame,input.IngressWorld,input.Knowledge);
        return captured is CustomKnowledgeCapture.Accepted or CustomKnowledgeCapture.Duplicate;
    }
    // Authenticated genuine-current ingress only. First input is retained at
    // initial engine binding from its original boundary evidence, before grants.
    internal void CapturePickupIngress(in CustomSourceInput input)
    {
        if(!_pickupOnly||_current[input.Owner.Slot] is not {} life||life.Owner!=input.Owner
            ||_sourceBoundary?.TryGet(input.Owner,out var context)!=true)return;
        CapturePickupInput(life,input,context);
    }
    internal bool TryPickupKnowledge(WeaponOwnerContext owner,out CustomPickupKnowledgeGrant grant)
    {
        grant=default;if(Find(owner)?.Knowledge?.Grant is not {} value)return false;grant=value;return true;
    }
    internal void EchoPickupControl(ulong connection,in CustomControlRequest request)
        =>Find(request.Owner)?.Knowledge?.EchoControl(connection,request,request.Knowledge);
    internal bool AdmitsPickupAction(in CustomWeaponAction action,ulong connection,uint observedWorld)
        =>!_pickupOnly||Find(action.Owner) is {Journal:not null,Knowledge:not null} life
            &&life.Journal.TryKnowledge(action,out var knowledge)
            &&life.Knowledge.Inspect(connection,action,knowledge,observedWorld)==CustomKnowledgeAdmission.Accepted;
    bool ICustomPickupFunding.TryFund(CustomPickupItemKey item,in CustomPickupContact contact,WeaponRuntimeId weapon,int reserveRefill,
        out CustomPickupFundingResult funded)
    {
        funded=default;
        if(_pickupActor is not {} player||!IsEligible||!CurrentOwner(player,out var owner)||owner!=contact.Owner
            ||_current[owner.Slot] is not {Journal:not null,Knowledge:not null} life||life.Owner!=owner
            ||life.Journal.IsRetired||Pickups?.World!=item.World||!life.Sources.TryGet(owner,contact.SourceFrame,Now,out var sample)
            ||sample.Input.Frame!=life.Journal.ObservedSourceFrame||unchecked((int)(Now-sample.Input.IngressWorld))<0
            ||!life.Journal.TryFundPickup(item,owner,weapon,contact.SourceFrame,Now,reserveRefill,out var state))return false;
        funded=new(state,life.Journal.PickupRevision);return true;
    }
    internal void ProcessPickupContact(PlayerEntity player)
    {
        if(!_pickupOnly||Pickups is not {} pickups||_pickupActor!=null||!CurrentOwner(player,out var owner)
            ||_current[owner.Slot] is not {Journal:not null,Knowledge:not null} life||life.Owner!=owner
            ||life.Journal.IsRetired||Spectating(player)||NetSession.RemoteIntentAge(owner.Slot)>30
            ||!life.Sources.TryGet(owner,life.Journal.ObservedSourceFrame,Now,out var source)
            ||source.Input.Frame!=NetSession.RemoteIntents[owner.Slot].Frame||!pickups.Advance(Now))return;
        var pose=player.ModWeaponPickupContactPose(source.Input.Frame,Now);
        var contact=new CustomPickupContact(owner,source.Input.Frame,Now,pose.Position,pose.SpherePosition,pose.Radius,
            pose.MinHeight,pose.MaxHeight,pose.Alt,pose.Alive,pose.CanPickup);
        if(!contact.IsValid||!contact.Alive||!contact.CanPickup)return;
        _pickupActor=player;
        try
        {
            for(int i=0;i<pickups.Layout.Count;i++)
            {
                if(!pickups.TryState(i,out var item)||!item.Available||!contact.Reaches(pickups.Layout.Placements[i].Position)
                    ||_delivery?.CanCollectPickup(this,i)!=true)continue;
                var result=pickups.TryCollect(item.Key,contact,this,out var outcome);
                if(result==CustomPickupCollectResult.Fault){Dispose();return;}
                if(result!=CustomPickupCollectResult.Collected)continue;
                // Any unexpected trusted publication failure quarantines this
                // world. Never refund/reoffer a possibly funded incarnation.
                if(!life.Knowledge.Commit(outcome)||_delivery.PublishPickup(this,outcome)!=true
                    ||!pickups.AcknowledgePublication(outcome.Revision)){Dispose();return;}
            }
        }
        finally{_pickupActor=null;}
    }
}
