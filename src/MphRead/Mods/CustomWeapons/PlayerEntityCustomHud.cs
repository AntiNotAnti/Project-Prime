using System;
using System.Globalization;
using MphRead.Mods;
using MphRead.Hud;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    internal void ModPlayCustomEmptyCue()=>_scene.PlayCustomRifleCue(this,CustomRifleCue.Empty);
    private bool ModDrawCustomAmmo(bool classic)
    {
        var hud=_scene.CustomWeaponHud(this);
        if(!hud.Visible)return false;
        if(IsAltForm||IsMorphing||IsUnmorphing)return true;
        bool profile=HudProfiles.Runtime.Mode==HudMode.Custom;
        var style=HudProfiles.Runtime.Ammo;
        float aspect=HudAspectFix;
        float left=classic?Math.Clamp(_hudObjects.AmmoBarPosX+_objShiftX,2*aspect,Math.Max(2*aspect,256-60*aspect)):256-60*aspect;
        float top=classic?Math.Clamp(_hudObjects.AmmoBarPosY+_objShiftY,1,170):170;
        using var layout=UseHudLayout(2,left,top);
        if(!profile||style.Background)_scene.DrawHudFlatBox(left,top,left+58*aspect,top+20,new Vector4(0,0,0,.55f*Features.HudOpacity));
        float fraction=hud.Ready?Math.Clamp(hud.State.Magazine/(float)Math.Max(1,hud.MagazineCapacity),0,1):1;
        var color=profile&&hud.Ready?MeterColor(style,fraction):new Vector4(.8f,.95f,1,1);
        color.W=Features.HudOpacity;
        var ink=ProInk(color);
        Span<char> buffer=stackalloc char[24];
        scoped ReadOnlySpan<char> ammo="-- / --";
        if(hud.Ready&&buffer.TryWrite(CultureInfo.InvariantCulture,$"{hud.State.Magazine:00} / {hud.State.Reserve:00}",out int written))ammo=buffer[..written];
        if(!profile||style.Number)DrawText2D(left+55*aspect,top+1,Align.Right,0,ammo,ink,
            alpha:Features.HudOpacity,scale:.65f*(profile?style.NumberScale:1));
        var feedback=_scene.CustomFeedback(this);
        ReadOnlySpan<char> status=!hud.Ready?"M7 PENDING":feedback.Denied>0?"M7 REFUSED":feedback.Empty>0?"M7 EMPTY":hud.State.Phase==CustomWeaponPhase.Reloading?"M7 RELOADING"
            :hud.State.Magazine==0?"M7 EMPTY":hud.Optic==1?"M7 2.5X":hud.Optic==2?"M7 4X":"M7";
        if(!profile||style.Icon)
        {
            // Original flat vector silhouette; no native weapon sprite alias.
            float ix=left+2*aspect,iy=top+11;var icon=new Vector4(.24f,.9f,1,Features.HudOpacity);
            _scene.DrawHudFlatBox(ix,iy,ix+5*aspect,iy+2,icon);
            _scene.DrawHudFlatBox(ix+5*aspect,iy+.5f,ix+9*aspect,iy+1,icon);
            _scene.DrawHudFlatBox(ix+1*aspect,iy-1,ix+4*aspect,iy-.2f,icon);
            _scene.DrawHudFlatBox(ix+1*aspect,iy+2,ix+2.5f*aspect,iy+3,icon);
            DrawText2D(left+13*aspect,top+10,Align.Left,0,status,ink,alpha:Features.HudOpacity,scale:.4f);
        }
        if((!profile||style.Gauge)&&hud.Ready)
        {
            using var gauge=UseHudLayout(profile&&HudProfiles.Runtime.IndependentGauges?13:-1,left+2*aspect,top+17);
            ProBar(left+2*aspect,top+17,54*(profile?style.GaugeScale:1),profile?style.GaugeThickness:2,
                fraction,color,profile&&style.Vertical);
        }
        return true;
    }
    private void ModDrawCustomReticle(float x,float y)
    {
        if(!HudElementVisible(0))return;
        bool customCrosshair=Features.CustomCrosshair;
        float pulse=HudProfiles.Runtime.ReduceMotion?0:_scene.CustomFeedback(this).FirePulse/2f;
        if(customCrosshair)
        {
            _scene.DrawCustomCrosshair(GetCrosshairColor(),x,y,-1,false);
            if(pulse<=0||!HudProfiles.Runtime.Crosshair.Enabled)return;
        }
        // Original precision reticle: open center, range ticks and cosmetic pulse.
        float cx=x*256,cy=y*192,aspect=HudAspectFix;
        using var layout=UseHudLayout(0,128,96);
        var color=new Vector4(.8f,.95f,1,Features.ReticleOpacity*(customCrosshair?HudProfiles.Runtime.Crosshair.Opacity:1));
        _scene.DrawHudFlatBox(cx-(6+pulse)*aspect,cy-.5f,cx-(2+pulse)*aspect,cy+.5f,color);
        _scene.DrawHudFlatBox(cx+(2+pulse)*aspect,cy-.5f,cx+(6+pulse)*aspect,cy+.5f,color);
        _scene.DrawHudFlatBox(cx-.5f*aspect,cy-6-pulse,cx+.5f*aspect,cy-2-pulse,color);
        _scene.DrawHudFlatBox(cx-.5f*aspect,cy+2+pulse,cx+.5f*aspect,cy+6+pulse,color);
        if(!customCrosshair)
        {
            _scene.DrawHudFlatBox(cx-.35f*aspect,cy-.35f,cx+.35f*aspect,cy+.35f,color);
            _scene.DrawHudFlatBox(cx-2*aspect,cy+8,cx+2*aspect,cy+8.5f,color);
            _scene.DrawHudFlatBox(cx-1*aspect,cy+11,cx+1*aspect,cy+11.5f,color);
        }
    }
}
