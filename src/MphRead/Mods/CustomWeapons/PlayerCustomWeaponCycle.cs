using MphRead.Mods.CustomWeapons;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _customCyclePending;
    private ulong _customCycleFrame;
    private CustomInputProjection _customCycleProjection;
    internal bool ModCustomCycleMenuDown=>Controls.WeaponMenu.IsDown||Input.StylusWeaponMenuDown;
    private CustomCycleTarget _customCycleTarget;

    internal CustomCycleTarget ModStageCustomCycle(in CustomInputProjection projection,bool selected,bool hasAmmo)
    {
        ushort eligible=0;
        for(int i=0;i<_weaponOrder.Length;i++)
            if(CanCycleToWeapon(_weaponOrder[i]))eligible|=(ushort)(1<<i);
        var target=CustomWeaponCycle.Resolve(_weaponOrder,eligible,CurrentWeapon,selected,hasAmmo,
            Controls.ScrollAllWeapons,Controls.NextWeapon.IsPressed);
        _customCyclePending=true;_customCycleFrame=_scene.FrameCount;_customCycleProjection=projection;_customCycleTarget=target;
        return target;
    }
    private bool ModApplyCustomCycle()
    {
        if(!_customCyclePending||_customCycleFrame!=_scene.FrameCount)return false;
        _customCyclePending=false;
        // Consume this physical edge once. Recheck life before native mutation;
        // a death/readiness change must never carry a choice into another life.
        if(_scene.ModCustomSelectionMatches(this,_customCycleProjection)&&!_customCycleTarget.Custom
            &&CurrentWeapon!=_customCycleTarget.Native)
            TryEquipWeapon(_customCycleTarget.Native);
        return true;
    }
}
