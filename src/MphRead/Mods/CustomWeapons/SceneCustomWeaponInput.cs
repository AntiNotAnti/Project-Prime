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
                if(submitted)scene._customFeedback?.Submitted(kind,sequence);
                return submitted;
            }
            if(!NetSession.TrySubmitCustomWeaponControl(kind,optic,out var request))return false;
            sequence=request.Sequence;scene._customFeedback?.Submitted(kind,sequence);return true;
        }
    }
    private CustomWeaponInputController? _customInput;
    private CustomInputSink? _customInputSink;
    private PlayerEntity? _customInputPlayer;
    private ShotKey _customInputLife;
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
        return current is {} value&&value.Owner==staged.Owner&&value.Bootstrap==staged.Bootstrap&&value.Manifest==staged.Manifest;
    }
    private bool TryCustomCycleState(PlayerEntity player,in CustomInputProjection projection,out WeaponStateSnapshot state)
    {
        state=default;
        if(!NetSession.Active)return OfflineWeapons?.TryPeek(player,out state,out _)==true&&state.Owner==projection.Owner;
        if(NetSession.CustomWeaponPresentationState is not {} message||message.State.Owner!=projection.Owner
            ||message.Bootstrap!=projection.Bootstrap||message.Manifest!=projection.Manifest)return false;
        state=message.State;return true;
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
            ||controls.AffinitySlot.IsPressed
            ||player.ModNativeWheelReleaseIntent;
        if(!NetSession.Active)OfflineWeapons?.ApplyPickupAutoSelection(player,
            allowed&&!Mods.SpectatorMode.IsSpectating&&GamepadContexts.Current==GamepadContext.Gameplay,
            native||controls.NextWeapon.IsPressed||controls.PrevWeapon.IsPressed||controls.CustomWeapon.IsPressed);
        var projection=NetSession.Active?NetSession.CustomWeaponInputProjection:OfflineWeapons?.InputProjection(player);
        if(!NetSession.Active&&projection==null)
        {
            // Offline absence is a definite empty life, not an unacknowledged
            // online selection. A dead owner's uncertainty cannot block the
            // native weapon after an empty respawn.
            ReleaseCustomWeaponInput();player.ModSynchronizeCustomNativeSuppression();return;
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
