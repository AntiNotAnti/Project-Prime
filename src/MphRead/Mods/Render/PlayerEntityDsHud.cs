using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Hud;
using MphRead.Mods.Combat;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

// The approved concept is authored in 768x576 pixels per LCD. All decoration
// and controls pass through this one mapping; input still uses StylusZone.
public partial class PlayerEntity
{
    private readonly int[] _dsFonts = new int[4];
    private readonly int[] _dsIcons = new int[9];
    private float[]? _dsAdvances;
    private int _dsBackground;
    private string? _dsTint;
    private System.Numerics.Vector3 _dsBgInk, _dsWeaponInk;
    private static readonly Vector4 DsWhite = new(217/255f,237/255f,248/255f,235/255f);
    private string _dsDecoration="borders";
    private Vector4 DsBlue(float alpha = .80f)
    { var style=HudProfiles.Runtime.Decorations.Get(_dsDecoration);var c=style.Ink;return new(c.X,c.Y,c.Z,alpha*style.Opacity); }

    private Vector4 DsRect(bool bottom)
    {
        if (!bottom) return new(0, 0, 256, 192);
        var b = DsLayout.Bounds(_scene.Size.X, _scene.Size.Y).Bottom;
        return new(b.X * 256f / _scene.Size.X, b.Y * 192f / _scene.Size.Y,
            b.Z * 256f / _scene.Size.X, b.W * 192f / _scene.Size.Y);
    }
    private Vector2 DsPoint(float x, float y, bool bottom)
    {
        var r=DsRect(bottom); return new(r.X+x*r.Z/768, r.Y+y*r.W/576);
    }
    private float DsPixel(bool bottom) => bottom ? DsLayout.Bounds(_scene.Size.X,_scene.Size.Y).Bottom.W/576f : _scene.Size.Y/576f;
    private void DsLine(float x1,float y1,float x2,float y2,Vector4 color,float width=1,bool bottom=false)
    {
        var p=DsPoint(x1,y1,bottom); float unit=DsPixel(bottom);
        float stroke=width*unit*HudProfiles.Runtime.Decorations.Get(_dsDecoration).LineScale;
        color.W*=Math.Min(1,stroke);
        _scene.DrawFlatLine(p.X/256,p.Y/192,Vector2.Zero,new((x2-x1)*unit*DsLayout.HorizontalStretch,-(y2-y1)*unit),Math.Max(1f,stroke),color);
    }
    private void DsBox(float x,float y,float w,float h,Vector4 color,bool bottom=false)
    {
        var a=DsPoint(x,y,bottom);var b=DsPoint(x+w,y+h,bottom);
        _scene.DrawHudFlatBox(a.X,a.Y,b.X,b.Y,color);
    }
    private void DsRing(float x,float y,float radius,Vector4 color,float width=.8f,bool bottom=false)
    {
        var p=DsPoint(x,y,bottom);float unit=DsPixel(bottom);
        float stroke=width*unit*HudProfiles.Runtime.Decorations.Get(_dsDecoration).LineScale;
        color.W*=Math.Min(1,stroke/1.5f);
        _scene.DrawFlatRing(p.X/256,p.Y/192,Vector2.Zero,radius*unit,Math.Max(1.5f,stroke),color);
    }
    private void DsDisc(float x,float y,float radius,Vector4 color,bool bottom=false)
    {
        var p=DsPoint(x,y,bottom);_scene.DrawFlatDisc(p.X/256,p.Y/192,Vector2.Zero,radius*DsPixel(bottom),color);
    }
    private void DsPolygon(ReadOnlySpan<Vector2> points,Vector4 color,bool bottom=false)
    {
        Span<Vector2> scaled=stackalloc Vector2[points.Length];float unit=DsPixel(bottom);
        for(int i=0;i<points.Length;i++)scaled[i]=new(points[i].X*unit*DsLayout.HorizontalStretch,-points[i].Y*unit);
        var p=DsPoint(0,0,bottom);_scene.DrawFlatPolygon(p.X/256,p.Y/192,Vector2.Zero,scaled,color);
    }
    private void DsTexture(float x,float y,float w,float h,int binding,float alpha=1,bool bottom=false,Vector4? uv=null)
    {
        var a=DsPoint(x,y,bottom);var b=DsPoint(x+w,y+h,bottom);
        _scene.DrawHudTexture(a.X,a.Y,b.X,b.Y,binding,alpha,true,uv);
    }
    private void EnsureDsAssets()
    {
        var background=HudProfiles.Runtime.Decorations.Get("background");
        var weapon=HudProfiles.Runtime.Decorations.Get("weapons");
        if(_dsTint==StylusZone.GuideColor && _dsBgInk==background.Ink && _dsWeaponInk==weapon.Ink && _dsAdvances!=null)return;
        var font = HudAssetReader.DsFont;
        _dsAdvances = font.Advances;
        byte[] alpha = font.Alpha;
        ColorRgba[] ink={new((byte)(StylusZone.GuideRed*255),(byte)(StylusZone.GuideGreen*255),(byte)(StylusZone.GuideBlue*255),255), new(111,203,240,255),new(221,114,187,255),new(255,106,106,255)};
        var pixels=new ColorRgba[alpha.Length];
        for(int c=0;c<4;c++)
        {
            for(int i=0;i<pixels.Length;i++)pixels[i]=new(ink[c].Red,ink[c].Green,ink[c].Blue,alpha[i]);
            if(_dsFonts[c]==0)_dsFonts[c]=_scene.BindGetTexture(pixels,768,384,sceneColor:false);
            else _scene.BindTexture(pixels,768,384,_dsFonts[c],sceneColor:false);
        }
        byte[] rgb = HudAssetReader.DsBackground;
        pixels = new ColorRgba[768*576];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new(
            (byte)Math.Clamp(rgb[i*3]*background.Ink.X/(162/255f),0,255),
            (byte)Math.Clamp(rgb[i*3+1]*background.Ink.Y/(211/255f),0,255),
            (byte)Math.Clamp(rgb[i*3+2]*background.Ink.Z/(240/255f),0,255),255);
        if(_dsBackground==0)_dsBackground=_scene.BindGetTexture(pixels,768,576,sceneColor:false);
        else _scene.BindTexture(pixels,768,576,_dsBackground,sceneColor:false);
        for(int frame=0;frame<9;frame++)
        {
            var icon=_weaponListIcons[frame];if(icon==null)continue;
            int w=icon.Width,h=icon.Height,factor=8,ow=w*factor,oh=h*factor;
            var texture=new ColorRgba[ow*oh];
            bool Ink(int x,int y)
            {
                if(x<0||y<0||x>=ow||y>=oh)return false;x/=factor;y/=factor;
                int index=frame*w*h+y/8*(w/8)*64+x/8*64+y%8*8+x%8;
                return index<_weaponListSheetData.Count && _weaponListSheetData[index]!=0;
            }
            for(int y=0;y<oh;y++)for(int x=0;x<ow;x++)
            {
                bool center=Ink(x,y),edge=false;
                for(int dy=-2;dy<=2&&!edge;dy++)for(int dx=-2;dx<=2;dx++)if(Ink(x+dx,y+dy)!=center){edge=true;break;}
                texture[y*ow+x]=new((byte)(weapon.Ink.X*255),(byte)(weapon.Ink.Y*255),(byte)(weapon.Ink.Z*255),edge?(byte)255:center?(byte)41:(byte)0);
            }
            if(_dsIcons[frame]==0)_dsIcons[frame]=_scene.BindGetTexture(texture,ow,oh,sceneColor:false);
            else _scene.BindTexture(texture,ow,oh,_dsIcons[frame],sceneColor:false);
        }
        _dsTint=StylusZone.GuideColor;_dsBgInk=background.Ink;_dsWeaponInk=weapon.Ink;
    }
    private float DsText(float x,float y,ReadOnlySpan<char> text,float size,int color=0,bool bottom=false,float alpha=1,string styleId="touch.labels")
    {
        var style=HudProfiles.Runtime.Typography.Get(styleId);
        if(style.Enabled)
        {
            ColorRgba? semantic=color switch {1=>new(111,203,240,255),2=>new(221,114,187,255),3=>new(255,106,106,255),_=>null};
            var a=DsPoint(x,y,bottom);float pixel=DsRect(bottom).W/576f;
            var end=DrawStyledHudText(a.X,a.Y,Align.Left,text,size,style,semantic,alpha,pixel);
            return x+(end.X-a.X)/(DsRect(bottom).Z/768f);
        }
        EnsureDsAssets();float scale=size/48;
        foreach(char raw in text)
        {
            char c=raw is >= ' ' and <= '\x7f' ? raw : '?';int n=c-32;
            DsTexture(x,y,48*scale/DsLayout.HorizontalStretch,64*scale,_dsFonts[color],alpha,bottom,new((n%16)/16f,(n/16)/6f,(n%16+1)/16f,(n/16+1)/6f));
            x+=_dsAdvances![n]*scale/DsLayout.HorizontalStretch;
        }
        return x;
    }
    private void DsWeapon(BeamType weapon,float x,float y,float side,bool bottom=false,float alpha=1)
    {
        EnsureDsAssets();int n=(int)weapon;if(n<0||n>=_dsIcons.Length||_dsIcons[n]<=0)return;
        var b=_weaponListIconBounds[n];var icon=_weaponListIcons[n];float scale=side/Math.Max(b.Width,b.Height);
        DsTexture(x-b.CentreX*scale/DsLayout.HorizontalStretch,y-b.CentreY*scale,icon.Width*scale/DsLayout.HorizontalStretch,icon.Height*scale,_dsIcons[n],alpha*HudProfiles.Runtime.Decorations.Get("weapons").Opacity,bottom);
    }
    private void DrawDsBorder(bool bottom)
    {
        _dsDecoration="borders";
        ReadOnlySpan<Vector2> p=[new(20,5),new(748,5),new(762,19),new(762,557),new(748,570),new(20,570),new(5,555),new(5,20),new(20,5)];
        for(int i=1;i<p.Length;i++)DsLine(p[i-1].X,p[i-1].Y,p[i].X,p[i].Y,DsBlue(.686f),.9f,bottom);
        for(int side=0;side<2;side++)for(int end=0;end<2;end++)
        {
            float X(float x)=>side==0?x:768-x;float Y(float y)=>end==0?y:576-y;
            DsLine(X(11),Y(52),X(11),Y(23),DsBlue(.84f),1.5f,bottom);
            DsLine(X(11),Y(23),X(25),Y(11),DsBlue(.84f),1.5f,bottom);
            DsLine(X(25),Y(11),X(69),Y(11),DsBlue(.84f),1.5f,bottom);
        }
        foreach(int x in new ReadOnlySpan<int>([80,86,92,676,682,688]))DsLine(x,10,x+2,10,DsBlue(.49f),.8f,bottom);
    }
    private void DrawDsTopReadouts()
    {
        EnsureDsAssets();
        if(ModHudHealthVisible)
        {
            _dsDecoration="health";
            using var warningPresentation = _scene.PushHealthWarning(ModHudHealth <= 99);
            int hp=ModHudHealth;float blink=DsLayout.HealthBlink(hp,_scene.GlobalElapsedTime);
            var c=hp<=99?new Vector4(1,.42f,.42f,blink*.8f):DsBlue(.8f);
            DsPolygon([new(252,33),new(510,33),new(515,52),new(250,52),new(247,46)],new(.094f,.26f,.39f,.35f));
            ReadOnlySpan<Vector2> outline=[new(252,33),new(510,33),new(515,52),new(250,52),new(247,46),new(252,33)];
            for(int i=1;i<outline.Length;i++)DsLine(outline[i-1].X,outline[i-1].Y,outline[i].X,outline[i].Y,c,.8f);
            float fraction=Math.Clamp(hp/(float)Math.Max(1,(int)_healthMax),0,1);
            if(fraction>0)DsPolygon([new(254,36),new(254+251*fraction,36),new(254+256*fraction,49),new(251,49)],c);
            Span<char> number=stackalloc char[12];hp.TryFormat(number,out int len);DsText(521,31,number[..len],21,hp<=99?3:0,alpha:blink,styleId:"core.health");
        }
        if(!IsAltForm&&!IsMorphing&&!IsUnmorphing)
        {
            _dsDecoration="weapons";
            var info=EquipInfo.Weapon;Span<char> ammo=stackalloc char[12];
            if(info.AmmoCost==0)DsText(614,519,"--",37.5f,styleId:"core.ammo");
            else { int shots=ModAmmoForWeapon(CurrentWeapon,info)/info.AmmoCost;shots.TryFormat(ammo,out int len);DsText(614,519,ammo[..len],37.5f,styleId:"core.ammo"); }
            DsLine(666,521,666,551,DsBlue(.74f),.9f);DsWeapon(CurrentWeapon,712,536,53.75f);DsLine(606,558,746,558,DsBlue(.74f),.6f);
        }
        if(_scene.GameState.Multiplayer)
        {
            _dsDecoration="points";
            DsBox(296,540,48,14,DsBlue(.588f));
            DsText(352,530,FormatModeScore(_scene.Players.MainPlayerIndex),23.75f,styleId:"match.score");
        }
    }
    private void DrawDsKillFeed(IReadOnlyList<KillFeedEntry> entries)
    {
        int shown=0;
        foreach(var entry in entries)
        {
            if(entry.AgeSeconds>=5||shown>=5)continue;
            float y=349+shown*30;
            int killer=_scene.GameState.Teams ? (entry.KillerTeam==TeamIndex?1:2) : entry.KillerSlot==SlotIndex?1:2;
            int victim=_scene.GameState.Teams ? (entry.VictimTeam==TeamIndex?1:2) : entry.VictimSlot==SlotIndex?1:2;
            var name=entry.KillerName.AsSpan();if(name.Length>24)name=name[..24];
            float x=DsText(27,y,name,15,killer,styleId:"combat.killFeed");
            DsLine(x+12,y+11,x+26,y+11,DsBlue(.86f),1);
            DsPolygon([new(x+26,y+7),new(x+31,y+11),new(x+26,y+15)],DsBlue(.86f));
            name=entry.VictimName.AsSpan();if(name.Length>24)name=name[..24];DsText(x+42,y,name,15,victim,styleId:"combat.killFeed");shown++;
        }
    }
    private void DrawDsBottom()
    {
        EnsureDsAssets();DsTexture(0,0,768,576,_dsBackground,alpha:HudProfiles.Runtime.Decorations.Get("background").Opacity,bottom:true);
        _dsDecoration="controls";
        var seam=DsBlue(.29f);
        DsLine(20,22,112,22,seam,.7f,true);DsLine(112,22,130,40,seam,.7f,true);DsLine(130,40,168,40,seam,.7f,true);
        DsLine(20,22,20,434,seam,.7f,true);DsLine(20,434,38,452,seam,.7f,true);DsLine(38,452,38,550,seam,.7f,true);
        DsLine(747,232,747,395,seam,.7f,true);DsLine(60,558,483,558,seam,.7f,true);DsLine(483,558,503,538,seam,.7f,true);DsLine(503,538,578,538,seam,.7f,true);
        foreach(var original in StylusZone.Buttons)
        {
            var b=StylusZone.EffectiveButton(original);float x=b.X*3,y=b.Y*3,w=b.Width*3,h=b.Height*3;
            bool pressed=StylusZone.Contact&&StylusZone.Region==b.Region;
            var edge=DsBlue(pressed?1:.74f);var fill=DsBlue(pressed?.18f:.035f);
            if(b.Round){DsDisc(x,y,h/2,fill,true);DsRing(x,y,h/2,edge,.8f,true);}
            else {DsBox(x-w/2,y-h/2,w,h,fill,true);DsLine(x-w/2,y-h/2,x+w/2,y-h/2,edge,.8f,true);DsLine(x+w/2,y-h/2,x+w/2,y+h/2,edge,.8f,true);DsLine(x+w/2,y+h/2,x-w/2,y+h/2,edge,.8f,true);DsLine(x-w/2,y+h/2,x-w/2,y-h/2,edge,.8f,true);}
            switch(b.Region)
            {
                case StylusRegion.PowerBeam: DsRing(x,y,16.25f,DsBlue(),1.5f,true);break;
                case StylusRegion.Missile:
                    DsPolygon([new(x,y-23.75f),new(x+6.25f/DsLayout.HorizontalStretch,y-12.5f),new(x+6.25f/DsLayout.HorizontalStretch,y+6.25f),new(x+12.5f/DsLayout.HorizontalStretch,y+15),new(x+12.5f/DsLayout.HorizontalStretch,y+23.75f),new(x+3.75f/DsLayout.HorizontalStretch,y+17.5f),new(x,y+23.75f),new(x-3.75f/DsLayout.HorizontalStretch,y+17.5f),new(x-12.5f/DsLayout.HorizontalStretch,y+23.75f),new(x-12.5f/DsLayout.HorizontalStretch,y+15),new(x-6.25f/DsLayout.HorizontalStretch,y+6.25f),new(x-6.25f/DsLayout.HorizontalStretch,y-12.5f)],DsBlue(.73f),true);break;
                case StylusRegion.Weapons: DsWeapon(CurrentWeapon is BeamType.PowerBeam or BeamType.Missile ? _weaponSlots[2] : CurrentWeapon,x,y,47.5f,true);break;
                case StylusRegion.WeaponSelect:
                    float s=StylusZone.SelectSize;
                    for(int n=0;n<6;n++){float angle=n*MathF.PI/3;DsDisc(x+18.75f*s*MathF.Cos(angle)/DsLayout.HorizontalStretch,y+18.75f*s*MathF.Sin(angle),2*s,DsBlue(),true);}DsRing(x,y,6.25f*s,DsBlue(),1,true);break;
                case StylusRegion.AltForm:DsRing(x,y,17.5f,DsBlue(),1.2f,true);DsLine(x-11.25f/DsLayout.HorizontalStretch,y-12.5f,x+8.75f/DsLayout.HorizontalStretch,y+13.75f,DsBlue(),1,true);DsLine(x+11.25f/DsLayout.HorizontalStretch,y-12.5f,x-8.75f/DsLayout.HorizontalStretch,y+13.75f,DsBlue(),1,true);break;
            }
        }
        DrawDsRadar();DrawDsBorder(true);
        if(!_scene.GameState.MenuPause&&!Mods.PauseMenu.Open)DrawStylusCursor();
    }
    private void DrawDsRadar()
    {
        _dsDecoration="radar";
        if(!Radar.Enabled || _scene.GameState.Teams&&ShowScoreboard || _scene.CameraSequences.Current?.IsIntro==true
            || _scene.GameState.Multiplayer&&_scene.GameState.MatchState!=MatchState.InProgress)return;
        float s=DsLayout.RadarSize,r=65*s;
        var center=DsLayout.RadarCenter; float x=center.X,y=center.Y;
        foreach(float ring in new ReadOnlySpan<float>([22,43,65]))DsRing(x,y,ring*s,DsBlue(.74f),.65f,true);
        DsLine(x-r/DsLayout.HorizontalStretch,y,x+r/DsLayout.HorizontalStretch,y,DsBlue(.23f),.5f,true);DsLine(x,y-r,x,y+r,DsBlue(.23f),.5f,true);
        DsPolygon([new(x,y-7.5f*s),new(x-5*s,y+6.25f*s),new(x,y+2.5f*s),new(x+5*s,y+6.25f*s)],DsBlue(),true);
        var style=HudProfiles.Runtime.Radar;var basis=HudRadarProjection.BuildBasis(RadarVector(CameraInfo.Facing),style.Orientation);
        var origin=RadarVector(RadarPresentedPosition());
        foreach(var contact in CollectRadarContacts(style))
        {
            if(!HudRadarProjection.Project(contact,origin,basis,style,r,out var point))continue;
            // Contacts retain the existing reveal/range rules and team color.
            var c=contact.Color;DsDisc(x+point.Position.X/DsLayout.HorizontalStretch,y-point.Position.Y,2.5f*s,new(c.X,c.Y,c.Z,c.W*contact.Alpha),true);
        }
    }
}
