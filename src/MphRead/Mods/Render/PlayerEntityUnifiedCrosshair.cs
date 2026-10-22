using System;
using System.Collections.Generic;
using MphRead.Mods.Input;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;
public partial class PlayerEntity
{
    private IReadOnlyList<ColorRgba>? _reticleOriginalPalette, _reticleTintPalette;
    private Vector3? _reticleTint;
    private Vector2 DrawUnifiedCrosshair(Vector2 position)
    {
        var c=HudProfiles.Runtime.CrosshairFor((int)CurrentWeapon,_hudZoom);

        if(c.Motion==CrosshairMotion.Static) position=new(.5f,.5f);
        else if(c.Motion==CrosshairMotion.Dynamic && Features.FixedWeapon)
        {
            float w=Matrix.ProjectPosition(_aimPosition,CameraInfo.ViewMatrix,_scene.PerspectiveMatrix,out Vector2 projected);
            if(w>0 && float.IsFinite(projected.X)&&float.IsFinite(projected.Y))position=projected;
        }
        if(!c.Enabled || !HudElementVisible(0))return position;
        float charge=EquipInfo.Weapon.FullCharge>0 ? Math.Clamp(EquipInfo.ChargeLevel/(EquipInfo.Weapon.FullCharge*2f),0,1) : 0;
        float response=(1+(c.ChargeScale-1)*charge)*(_hudZoom?c.ZoomScale:1)*(_smallReticleTimer>0?c.ShotScale:1);
        if(!c.Native) { _scene.DrawCustomCrosshair(GetCrosshairColor(),position.X,position.Y,(int)CurrentWeapon,_hudZoom,response);return position; }
        _targetCircleInst.PositionX=position.X;_targetCircleInst.PositionY=position.Y;
        if(_targetCircleInst.PaletteData!=_reticleTintPalette)_reticleOriginalPalette=_targetCircleInst.PaletteData;
        Vector3? tint=c.NativeOriginalColors ? null : c.MatchHudPalette ? new(StylusZone.GuideRed,StylusZone.GuideGreen,StylusZone.GuideBlue) : c.HealthColor ? GetCrosshairColor() : new(c.Color.X,c.Color.Y,c.Color.Z);
        if(tint!=null && (tint!=_reticleTint || _reticleTintPalette==null))
        {
            var colors=new ColorRgba[_reticleOriginalPalette!.Count];
            for(int i=0;i<colors.Length;i++){var original=_reticleOriginalPalette[i];float light=Math.Max(original.Red,Math.Max(original.Green,original.Blue));var t=tint.Value;colors[i]=new((byte)(light*t.X),(byte)(light*t.Y),(byte)(light*t.Z),original.Alpha);}
            _reticleTintPalette=colors;
        }
        _reticleTint=tint;
        _targetCircleInst.SetPaletteData(tint==null?_reticleOriginalPalette!:_reticleTintPalette!,_scene);
        if(!c.NativeAnimations)_targetCircleInst.SetIndex(_hudZoom?2:0,_scene);
        _targetCircleInst.Alpha=c.Opacity*HudProfiles.Runtime[0].Opacity;
        float scale=c.NativeScale*response;
        if(c.MinimumVisibility)scale=Math.Max(scale,8f/Math.Max(1,_targetCircleInst.Width*_scene.Size.X/256f));
        _scene.DrawHudObject(_targetCircleInst,scale:scale,reticleTransform:new(c.WidthScale,c.HeightScale,c.Rotation),nativeThickness:c.NativeThickness);
        return position;
    }
}
