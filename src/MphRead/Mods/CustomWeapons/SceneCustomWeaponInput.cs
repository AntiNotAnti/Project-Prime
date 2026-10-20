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
            if(!NetSession.Active)return scene.OfflineWeapons?.SubmitInput(player,kind,optic,out sequence)==true;
            if(!NetSession.TrySubmitCustomWeaponControl(kind,optic,out var request))return false;
            sequence=request.Sequence;return true;
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
    internal void SampleCustomWeaponInput(bool allowed)
    {
        // Public local input never takes over a server, bot or private replica.
        if(Services.IsReplica||DemoPlayback.IsActive||NetSession.IsAuthority)return;
        int slot=NetSession.Active?NetSession.LocalSlot:0;
        if((uint)slot>=Players.Items.Count)return;
        var player=Players.Items[slot];if(player.IsBot)return;
        var projection=NetSession.Active?NetSession.CustomWeaponInputProjection:OfflineWeapons?.InputProjection(player);
        if(projection==null&&_customInput==null){player.ModTakeNativeSelectionIntent();return;}
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
        var controls=player.Controls;
        bool native=player.ModTakeNativeSelectionIntent()
            ||controls.PowerBeam.IsPressed||controls.Missile.IsPressed||controls.VoltDriver.IsPressed
            ||controls.Battlehammer.IsPressed||controls.Imperialist.IsPressed||controls.Judicator.IsPressed
            ||controls.Magmaul.IsPressed||controls.ShockCoil.IsPressed||controls.OmegaCannon.IsPressed
            ||controls.AffinitySlot.IsPressed||controls.NextWeapon.IsPressed||controls.PrevWeapon.IsPressed
            ||player.ModNativeWheelReleaseIntent;
        bool inputAllowed=allowed&&!Mods.SpectatorMode.IsSpectating&&GamepadContexts.Current==GamepadContext.Gameplay
            &&player.Health>0&&!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing
            &&player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector,out var facts)&&facts.CanUseWeapon;
        _customInput.Sample(new(inputAllowed,controls.CustomWeapon.IsPressed,native,controls.Shoot.IsPressed,
            controls.CustomReload.IsPressed,controls.Zoom.IsPressed,controls.Zoom.IsDown,controls.CustomOptic.IsPressed),_customInputSink!);
        player.ModSynchronizeCustomNativeSuppression();
    }
}
