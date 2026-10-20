using MphRead.Mods.Network;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _customNativeSelectionIntent,_customNativeSuppressed;
    internal void ModNoteNativeSelectionIntent()=>_customNativeSelectionIntent=true;
    internal bool ModTakeNativeSelectionIntent()
    {bool value=_customNativeSelectionIntent;_customNativeSelectionIntent=false;return value;}
    internal bool ModNativeWheelReleaseIntent=>Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)
        &&!Controls.WeaponMenu.IsDown&&!Input.StylusWeaponMenuDown&&WeaponSelection!=BeamType.None;
    internal bool ModCustomOwnsBipedControls=>!_scene.Services.IsReplica&&!IsAltForm
        &&(!NetSession.Active||NetSession.IsClient&&!NetSession.IsAuthority&&SlotIndex==NetSession.LocalSlot)
        &&_scene.CustomInputSuppressesNative(this);
    internal void ModSynchronizeCustomNativeSuppression()
    {
        bool suppressed=ModCustomOwnsBipedControls;
        if(suppressed&&!_customNativeSuppressed)
        {
            if(EquipInfo.ChargeLevel!=0){StopBeamChargeSfx(CurrentWeapon);EquipInfo.ChargeLevel=0;}
            Flags2&=~PlayerFlags2.Shooting;_powerBeamAutofire=0;
            if(EquipInfo.Zoomed)UpdateZoom(false);
            NetContinuousTargeting.ResetPlayer(this);
        }
        _customNativeSuppressed=suppressed;
    }
}
