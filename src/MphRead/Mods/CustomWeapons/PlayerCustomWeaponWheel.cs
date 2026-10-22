using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Sound;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private CustomWeaponWheel? _customWheel;
    private CustomInputProjection _customWheelIdentity;
    private ushort _customWheelEnabled;
    private bool _customWheelEnding,_customWheelExplicit,_customWheelCancelled,_customWheelNativeHold;
    private ulong _customWheelEndFrame;
    private CustomCycleTarget? _customWheelRelease;
    // Read only after fixed-step update; drawing never queries live resources.
    internal int ModCustomWheelCount=>_customWheel?.Count??0;
    internal int ModCustomWheelSelection=>_customWheel?.Selection??-1;
    private HudSpriteBounds CustomWheelZone()
        =>StylusZone.Enabled&&!GamepadInput.WheelHeld
            ?new(StylusZone.Left*_scene.Size.X,StylusZone.Top*_scene.Size.Y,
                StylusZone.Width*_scene.Size.X,StylusZone.Height*_scene.Size.Y)
            :new(0,0,_scene.Size.X,_scene.Size.Y);
    private HudSpriteBounds CustomWheelHudBounds(int index)
    {
        var raw=CustomWeaponWheel.Bounds(index,_customWheel!.Count,CustomWheelZone());
        return new(raw.Left/Math.Max(1,_scene.Size.X)*256,raw.Top/Math.Max(1,_scene.Size.Y)*192,
            raw.Width/Math.Max(1,_scene.Size.X)*256,raw.Height/Math.Max(1,_scene.Size.Y)*192);
    }
    internal HudSpriteBounds ModCustomWheelPixelBounds(int index)
    {
        if(_customWheel==null||(uint)index>=_customWheel.Count)return default;
        var raw=CustomWheelHudBounds(index);
        return _scene.GetHudFlatBoxBounds(raw.Left,raw.Top,raw.Left+raw.Width,raw.Top+raw.Height);
    }
    private void ModClearCustomWheel()
    {_customWheel=null;_customWheelEnabled=0;_customWheelEnding=false;_customWheelRelease=null;_customWheelCancelled=false;_customWheelNativeHold=false;}
    internal void ModCancelCustomWheelHold()
    {if(_customWheel!=null||_customWheelCancelled){_customWheel=null;_customWheelEnabled=0;_customWheelRelease=null;_customWheelCancelled=true;}}
    private bool ModUpdateCustomWheel()
    {
        if(_customWheelCancelled)return true;
        // Acquisition during an already-open native hold cannot move its
        // targets under the pointer. The acquired layout starts next hold.
        if(_customWheelNativeHold)return false;
        if(!_scene.TryCustomSelectionFrame(this,out var projection,out var state,out bool selected))
        {
            if(_customWheel==null){_customWheelNativeHold=true;return false;}
            ModCancelCustomWheelHold();return true;
        }
        if(_customWheel!=null&&!_scene.ModCustomSelectionMatches(this,_customWheelIdentity))
        {ModCancelCustomWheelHold();return true;}
        if(_customWheel==null)
        {
            Span<BeamType> beams=stackalloc BeamType[6];
            for(int i=0;i<6;i++)beams[i]=(BeamType)_weaponSelectInsts[i].CurrentFrame;
            BeamType fallback=CanCycleToWeapon(CurrentWeapon)?CurrentWeapon:BeamType.PowerBeam;
            _customWheel=new(beams,GamepadOptions.WheelOrder,fallback,selected);_customWheelIdentity=projection;
        }
        _customWheelEnabled=0;
        for(int i=0;i<_customWheel.Count;i++)
        {
            var entry=_customWheel[i];
            if(entry.Custom?state.Magazine>0||state.Reserve>0:CanCycleToWeapon(entry.Native))
                _customWheelEnabled|=(ushort)(1<<i);
        }
        int previous=_customWheel.Selection;
        if(GamepadInput.WheelHeld)
        {
            var stick=GamepadInput.AimStick;
            _customWheel.Point(CustomWeaponWheel.Direction(stick.X,stick.Y,GamepadOptions.WheelThreshold,_customWheel.Count),_customWheelEnabled);
        }
        else if(WeaponWheel.Absolute)
        {
            int choice=-1;
            for(int i=0;i<_customWheel.Count;i++)
                if(ModCustomWheelPixelBounds(i).Contains(Input.PointerX,Input.PointerY)){choice=i;break;}
            _customWheel.Point(choice,_customWheelEnabled);
        }
        else _customWheel.Drag(Input.MouseDeltaY,_scene.Size.Y/10f,_customWheelEnabled);
        if(previous!=_customWheel.Selection)_soundSource.PlayFreeSfx(SfxId.HUD_WEAPON_SWITCH2);
        return true;
    }
    // Called before the ordinary native intent capture. Retains the release for
    // EndWeaponMenu in this same frame; no draw or pointer callback submits it.
    internal bool ModResolveCustomWheelRelease(bool explicitSelection,bool allowed,out CustomCycleTarget? target)
    {
        target=null;
        if(!allowed)ModCancelCustomWheelHold();
        if(_customWheel==null&&!_customWheelCancelled||!Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)||ModCustomCycleMenuDown)return false;
        _customWheelEnding=true;_customWheelEndFrame=_scene.FrameCount;_customWheelExplicit=explicitSelection;
        if(allowed&&!explicitSelection&&_customWheel!=null&&_scene.ModCustomSelectionMatches(this,_customWheelIdentity)
            &&_customWheel.Selection is >=0 and var index&&(_customWheelEnabled&(1<<index))!=0)
        {
            var entry=_customWheel[index];
            bool eligible=entry.Custom
                ?_scene.TryCustomSelectionFrame(this,out _,out var fresh,out _)&&(fresh.Magazine>0||fresh.Reserve>0)
                :CanCycleToWeapon(entry.Native);
            if(eligible)target=entry;
        }
        _customWheelRelease=target;return true;
    }
    private bool ModEndCustomWheel(out bool selected)
    {
        selected=false;
        if(!_customWheelEnding||_customWheelEndFrame!=_scene.FrameCount)return false;
        bool explicitSelection=_customWheelExplicit;
        var target=_customWheelRelease;
        if(_scene.ModCustomSelectionMatches(this,_customWheelIdentity)&&target is {Custom:false} native
            &&CanCycleToWeapon(native.Native)&&CurrentWeapon!=native.Native)TryEquipWeapon(native.Native);
        // A neutral/stale acquired-wheel release cancels rather than selecting
        // whichever native fallback happened to remain in WeaponSelection.
        selected=!explicitSelection;ModClearCustomWheel();return true;
    }
    private void ModDrawCustomWheel()
    {
        if(_customWheel==null)return;
        float extent=Math.Min(CustomWheelZone().Width,CustomWheelZone().Height);
        float scale=extent/Math.Max(1,_scene.Size.Y)*.65f;
        for(int i=0;i<_customWheel.Count;i++)
        {
            var rect=CustomWheelHudBounds(i);bool enabled=(_customWheelEnabled&(1<<i))!=0;
            bool selected=i==_customWheel.Selection;
            _scene.DrawHudFlatBox(rect.Left,rect.Top,rect.Left+rect.Width,rect.Top+rect.Height,
                selected?new Vector4(.08f,.55f,.65f,.95f):enabled?new Vector4(.08f,.16f,.2f,.88f):new Vector4(.08f,.08f,.08f,.7f));
            var entry=_customWheel[i];string label=entry.Custom?(_scene.LocalWeaponPackage?.Project.Name??"M7"):entry.Native switch
            {BeamType.PowerBeam=>"POWER BEAM",BeamType.Missile=>"MISSILE",BeamType.VoltDriver=>"VOLT DRIVER",
             BeamType.Battlehammer=>"BATTLEHAMMER",BeamType.Imperialist=>"IMPERIALIST",BeamType.Judicator=>"JUDICATOR",
             BeamType.Magmaul=>"MAGMAUL",BeamType.ShockCoil=>"SHOCK COIL",BeamType.OmegaCannon=>"OMEGA CANNON",_=>""};
            float labelScale=_scene.LocalWeaponPackage!=null&&entry.Custom?Math.Min(scale,rect.Width/Math.Max(1,label.Length*8)):scale;
            DrawText2D(rect.Left+rect.Width/2,rect.Top+(rect.Height-8*labelScale)/2,Align.Center,0,label,
                color:enabled?new ColorRgba(255,255,255,255):new ColorRgba(110,110,110,255),scale:labelScale,alpha:Features.HudOpacity);
        }
    }
}
