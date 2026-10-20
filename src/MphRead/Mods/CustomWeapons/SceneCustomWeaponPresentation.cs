using System;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

namespace MphRead;

internal readonly record struct CustomWeaponHudState(bool Visible,bool Ready,WeaponStateSnapshot State,byte Optic,int MagazineCapacity);

public partial class Scene
{
    private CustomWeaponFeedback? _customFeedback;
    private readonly record struct PublicFact(CustomReplicaEvent Fact,uint Tick);
    private PublicFact[]? _customPublicFacts;
    private int _customPublicFactIndex;
    internal CustomFeedbackView CustomFeedback(PlayerEntity player)=>ReferenceEquals(player,_customInputPlayer)
        ?_customFeedback?.View??default:default;
    internal void ApplyCustomFeedback(in CustomPresentationResult result)
    {
        if(_customFeedback?.Apply(result)==true&&!Mods.Headless.Active&&_customInputPlayer is {} player)player.ModPlayCustomEmptyCue();
    }
    private void StepCustomWeaponFeedback()
    {
        _customFeedback?.Step();
        if(NetSession.IsClient&&!NetSession.IsAuthority&&!Services.IsReplica)
        {
            bool overflow=NetSession.TakeCustomPresentationOverflowed();
            if(overflow)_customFeedback?.CancelCosmetics();
            for(int n=0;n<64&&NetSession.TryTakeCustomPresentationResult(out var result);n++)
                if(!overflow)ApplyCustomFeedback(result);
            // Drain once on the simulation owner. Retain bounded recent public
            // facts for presentation, without feeding them into native combat.
            for(int n=0;n<CustomReplicaConsumer.Capacity&&NetSession.TryTakeCustomReplicaFact(out var fact);n++)
            {
                _customPublicFacts??=new PublicFact[64];
                _customPublicFacts[_customPublicFactIndex++%64]=new(fact,(uint)FrameCount);
            }
            if(_customPublicFactIndex>=64)_customPublicFactIndex%=64;
        }
        if(_customPublicFacts!=null)for(int i=0;i<_customPublicFacts.Length;i++)
            if(unchecked((uint)FrameCount-_customPublicFacts[i].Tick)>60)_customPublicFacts[i]=default;
    }
    private CustomReplicaSlot[]? _customPresentationSlots;
    private CustomWeaponHudState[]? _customHudStates;
    // Draws only read this fixed-step value. They cannot advance offline reload,
    // source time, control results or a recipient queue.
    internal CustomWeaponHudState CustomWeaponHud(PlayerEntity player)
        =>_customHudStates!=null&&(uint)player.SlotIndex<8?_customHudStates[player.SlotIndex]:default;
    private void ReleaseCustomWeaponPresentation()
    {
        // Constructor-failed scenes can own a partially populated player table.
        // Cleanup must release existing scopes without dereferencing empty slots.
        foreach(var player in Players.Items)player?.ModSetCustomScope(default,false,0);
        _customPresentationSlots=null;_customHudStates=null;_customFeedback=null;_customPublicFacts=null;_customPublicFactIndex=0;
    }
    internal void UpdateCustomWeaponPresentation()
    {
        if(NetSession.IsAuthority||Services.IsReplica||DemoPlayback.IsActive)
        {
            if(_customHudStates!=null||_customFeedback!=null)ReleaseCustomWeaponPresentation();
            return;
        }
        bool online=NetSession.Active,ready=online&&NetSession.CustomWeaponReplicaReady;
        if(OfflineWeapons==null&&!ready&&_customHudStates==null)return;
        _customHudStates??=new CustomWeaponHudState[8];Array.Clear(_customHudStates);
        bool publicState=false;
        if(ready)
        {
            _customPresentationSlots??=new CustomReplicaSlot[8];
            publicState=NetSession.TryCopyCustomReplicaState(_customPresentationSlots,out _);
        }
        var mechanics=online?NetSession.CustomWeaponPresentationMechanics:OfflineWeapons?.Mechanics;
        foreach(var player in Players.Items)
        {
            int slot=player.SlotIndex;if((uint)slot>=8)continue;
            WeaponStateSnapshot state=default;bool known=false,selected=false;
            if(!online&&OfflineWeapons?.TryPeek(player,out state,out selected)==true)known=true;
            else if(ready&&slot==NetSession.LocalSlot&&NetSession.CustomWeaponInputProjection is {HasWeapon:true} local
                &&NetSession.CustomWeaponPresentationState is {HasWeapon:true} own&&own.State.Owner==local.Owner)
            {known=true;state=own.State;selected=local.Selected;}
            else if(ready&&publicState&&_customPresentationSlots![slot] is {Occupied:true,HasWeapon:true,Spectating:false} other
                &&NetPlayerLifecycle.Matches(slot,other.Generation,other.Life)&&other.State.Owner.Life==other.Life)
            {known=true;state=other.State;selected=other.Selected.IsValid;}
            bool localPrediction=ReferenceEquals(player,_customInputPlayer)&&_customInput?.Available==true;
            if(known&&localPrediction)selected=_customInput!.Selected;
            byte optic=known?(byte)state.Optic:(byte)0;
            if(known&&localPrediction)optic=_customInput!.PresentationOptic;
            bool visible=known&&selected||ReferenceEquals(player,_customInputPlayer)&&_customInput?.SuppressNative==true;
            _customHudStates[slot]=new(visible,known,state,optic,mechanics?.MagazineCapacity??0);
            int magnification=optic>0&&mechanics!=null&&optic<=mechanics.OpticMagnificationsMilli.Count
                ?mechanics.OpticMagnificationsMilli[optic-1]:1000;
            player.ModSetCustomScope(known?state.Owner:default,known&&selected,optic,magnification,
                online?NetSession.ConfirmedWorldBootstrap??default:default,online?NetSession.CustomWeaponPresentationManifest:default);
        }
    }
}
