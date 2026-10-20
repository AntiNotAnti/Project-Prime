using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

namespace MphRead;

public partial class Scene
{
    private sealed class CustomInputSink(Scene scene,PlayerEntity player):ICustomWeaponInputSink
    {
        public bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence)
        {
            sequence=0;
            if(!NetSession.Active)
            {
                bool submitted=scene.OfflineWeapons?.SubmitInput(player,kind,optic,out sequence)==true;
                if(submitted){scene._customFeedback?.Submitted(kind,sequence);scene.SubmitCustomRiflePrediction(player,kind,sequence);}
                return submitted;
            }
            if(!NetSession.TrySubmitCustomWeaponControl(kind,optic,out var request))return false;
            sequence=request.Sequence;scene._customFeedback?.Submitted(kind,sequence);scene.SubmitCustomRiflePrediction(player,kind,sequence);return true;
        }
    }
    private CustomWeaponInputController? _customInput;
    private CustomInputSink? _customInputSink;
    private PlayerEntity? _customInputPlayer;
    private ShotKey _customInputLife;
    private WeaponOwnerContext _onlinePickupOwner;
    private ulong _onlinePickupRevision;
    private void ReleaseCustomWeaponInput()
    {
        _customInput?.Reset();_customInput=null;_customInputSink=null;_customInputPlayer=null;_customInputLife=default;
    }
    internal bool CustomInputSuppressesNative(PlayerEntity player)
        =>ReferenceEquals(_customInputPlayer,player)&&_customInput?.SuppressNative==true
                &&(!NetSession.Active||_customInputLife==ShotKey.For(player.SlotIndex,0))
            ||!NetSession.Active&&OfflineWeapons?.InputProjection(player)?.Selected==true
            ||NetSession.IsClient&&!NetSession.IsAuthority&&player.SlotIndex==NetSession.LocalSlot
                &&NetSession.CustomWeaponInputProjection?.Selected==true;
    internal bool ModCustomSelectionMatches(PlayerEntity player,in CustomInputProjection staged)
    {
        var current=!NetSession.Active?OfflineWeapons?.InputProjection(player)
            :player.SlotIndex==NetSession.LocalSlot?NetSession.CustomWeaponInputProjection:null;
        return current is {HasWeapon:true} value&&staged.HasWeapon&&value.Owner==staged.Owner&&value.Bootstrap==staged.Bootstrap&&value.Manifest==staged.Manifest;
    }
    private bool TryCustomCycleState(PlayerEntity player,in CustomInputProjection projection,out WeaponStateSnapshot state)
    {
        state=default;
        if(!projection.HasWeapon)return false;
        if(!NetSession.Active)return OfflineWeapons?.TryPeek(player,out state,out _)==true&&state.Owner==projection.Owner;
        if(NetSession.CustomWeaponPresentationState is not {HasWeapon:true} message||message.State.Owner!=projection.Owner
            ||message.Bootstrap!=projection.Bootstrap||message.Manifest!=projection.Manifest)return false;
        state=message.State;return true;
    }
    internal bool TryCustomSelectionFrame(PlayerEntity player,out CustomInputProjection projection,
        out WeaponStateSnapshot state,out bool selected)
    {
        projection=default;state=default;selected=false;
        if(player.IsBot||Services.IsReplica||DemoPlayback.IsActive||NetSession.IsAuthority||player.Health<=0
            ||player.IsAltForm||player.IsMorphing||player.IsUnmorphing||Mods.SpectatorMode.IsSpectating)return false;
        var current=!NetSession.Active?OfflineWeapons?.InputProjection(player)
            :player.SlotIndex==NetSession.LocalSlot?NetSession.CustomWeaponInputProjection:null;
        if(current is not {} value||!TryCustomCycleState(player,value,out state))return false;
        projection=value;selected=ReferenceEquals(player,_customInputPlayer)&&_customInput?.Available==true
            ?_customInput.Selected:value.Selected;return true;
    }
    internal void SampleCustomWeaponInput(bool allowed)
    {
        // Public local input never takes over a server, bot or private replica.
        if(Services.IsReplica||DemoPlayback.IsActive||NetSession.IsAuthority)return;
        int slot=NetSession.Active?NetSession.LocalSlot:0;
        if((uint)slot>=Players.Items.Count)return;
        var player=Players.Items[slot];if(player.IsBot)return;
        var controls=player.Controls;
        bool native=player.ModTakeNativeSelectionIntent()
            ||controls.PowerBeam.IsPressed||controls.Missile.IsPressed||controls.VoltDriver.IsPressed
            ||controls.Battlehammer.IsPressed||controls.Imperialist.IsPressed||controls.Judicator.IsPressed
            ||controls.Magmaul.IsPressed||controls.ShockCoil.IsPressed||controls.OmegaCannon.IsPressed
            ||controls.AffinitySlot.IsPressed;
        if(!NetSession.Active)OfflineWeapons?.ApplyPickupAutoSelection(player,
            allowed&&!Mods.SpectatorMode.IsSpectating&&GamepadContexts.Current==GamepadContext.Gameplay,
            native||player.ModNativeWheelReleaseIntent||controls.NextWeapon.IsPressed||controls.PrevWeapon.IsPressed||controls.CustomWeapon.IsPressed);
        var projection=NetSession.Active?NetSession.CustomWeaponInputProjection:OfflineWeapons?.InputProjection(player);
        if(!NetSession.Active&&projection==null||projection is {HasWeapon:false})
        {
            // Explicit absence is a definite empty life. Lost online readiness
            // remains uncertain, while an empty respawn must allow native fire.
            player.ModCancelCustomWheelHold();
            player.ModResolveCustomWheelRelease(false,false,out _);
            ReleaseCustomWeaponInput();_customFeedback?.Synchronize(null);player.ModSynchronizeCustomNativeSuppression();return;
        }
        if(projection==null&&_customInput==null)return;
        if(!ReferenceEquals(_customInputPlayer,player))
        {
            _customInput=new();_customInputPlayer=player;_customInputSink=new(this,player);_customInputLife=default;
        }
        if(NetSession.Active)
        {
            var life=ShotKey.For(slot,0);
            if(life!=_customInputLife){_customInput!.Reset();_customInputLife=life;}
        }
        _customInput!.Synchronize(projection);
        _customFeedback??=new();_customFeedback.Synchronize(projection);
        bool inputAllowed=allowed&&!Mods.SpectatorMode.IsSpectating&&GamepadContexts.Current==GamepadContext.Gameplay
            &&player.Health>0&&!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing
            &&player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector,out var facts)&&facts.CanUseWeapon;
        bool custom=controls.CustomWeapon.IsPressed;
        bool cycle=controls.NextWeapon.IsPressed||controls.PrevWeapon.IsPressed;
        if(NetSession.Active&&projection is {HasWeapon:true} pickup
            &&NetSession.CustomWeaponPresentationState is {Knowledge.IsPresent:true} acquired
            &&acquired.State.Owner==pickup.Owner&&(_onlinePickupOwner!=pickup.Owner||_onlinePickupRevision!=acquired.Knowledge.Revision))
        {
            bool first=_onlinePickupOwner!=pickup.Owner;
            _onlinePickupOwner=pickup.Owner;_onlinePickupRevision=acquired.Knowledge.Revision;
            player.ModPlayCustomPickupCue(first);
            if(first&&inputAllowed&&!native&&!custom&&!cycle&&!player.ModNativeWheelReleaseIntent&&!player.ModCustomCycleMenuDown
                &&!controls.Shoot.IsDown&&!player.Flags2.TestFlag(PlayerFlags2.Shooting)&&player.ModCustomPickupOutranksCurrent())custom=true;
        }
        bool wheelHandled=player.ModResolveCustomWheelRelease(native||custom||cycle,inputAllowed,out var wheel);
        if(!wheelHandled)native|=player.ModNativeWheelReleaseIntent;
        else if(wheel is {} choice){native|=!choice.Custom;custom|=choice.Custom;}
        if(cycle&&!native&&!custom&&inputAllowed&&projection is {} cycleOwner
            &&!player.ModCustomCycleMenuDown
            &&TryCustomCycleState(player,cycleOwner,out var cycleState))
        {
            var target=player.ModStageCustomCycle(cycleOwner,_customInput.Selected,
                cycleState.Magazine>0||cycleState.Reserve>0);
            native=!target.Custom;custom=target.Custom;
        }
        else native|=cycle;
        _customInput.Sample(new(inputAllowed,custom,native,controls.Shoot.IsPressed,
            controls.CustomReload.IsPressed,controls.Zoom.IsPressed,controls.Zoom.IsDown,controls.CustomOptic.IsPressed,Mods.InputSettings.CustomScopeHold),_customInputSink!);
        player.ModSynchronizeCustomNativeSuppression();
    }
}
