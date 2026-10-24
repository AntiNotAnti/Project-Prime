using System;
using System.Collections.Generic;
using MphRead.Hud;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    private string? _hudTextOverride;
    private readonly Dictionary<string,int> _hudFontBindings=new();
    private Vector2 DrawStyledHudText(float x,float y,Align align,ReadOnlySpan<char> text,float size,
        HudTextStyle style,ColorRgba? semantic,float alpha,float unit=1f/3)
    {
        if(text.IndexOf('\n')>=0)
        {
            float row=0;var end=new Vector2(x,y);
            while(text.Length>0)
            {
                int next=text.IndexOf('\n');if(next<0)next=text.Length;
                end=DrawStyledHudText(x,y+row,align,text[..next],size,style,semantic,alpha,unit);
                row+=size*64/48*style.Size*unit*HudProfiles.Runtime.TextScale;
                if(next==text.Length)break;text=text[(next+1)..];
            }
            return end;
        }
        var atlas=HudFonts.Get(style.Font);
        if(!_hudFontBindings.TryGetValue(style.Font,out int binding))
        {
            var pixels=new ColorRgba[atlas.Alpha.Length];
            for(int i=0;i<pixels.Length;i++)pixels[i]=new(255,255,255,atlas.Alpha[i]);
            binding=_scene.BindGetTexture(pixels,HudFonts.AtlasWidth,HudFonts.AtlasHeight,sceneColor:false);_hudFontBindings.Add(style.Font,binding);
        }
        float sy=size/48*style.Size*unit*HudProfiles.Runtime.TextScale;
        float sx=sy*(_scene.Size.Y*256f/(_scene.Size.X*192f));
        float width=0;foreach(char raw in text) { if(raw=='\0')break;if(raw=='\n')continue;width+=atlas.Advances[Math.Clamp(raw-32,0,95)]*sx; }
        if(style.FitToScreen && width>0)
        {
            float fit=Math.Min(1,_scene.HudTextSpace(x,align)/width);
            sx*=fit;sy*=fit;width*=fit;
        }
        if(align is Align.Center or Align.PadCenter)x-=width/2;
        else if(align==Align.Right)x-=width;
        float start=x;
        var rgb=(style.MatchPalette?HudProfiles.Runtime.AccentInk:style.Ink);
        var ink=new Vector4(rgb.X,rgb.Y,rgb.Z,1);
        if(style.PreserveSemanticColors && semantic is {} c)ink=new(c.Red/255f,c.Green/255f,c.Blue/255f,1);
        var oc=style.OutlineInk;var outline=new Vector4(oc.X,oc.Y,oc.Z,1);
        foreach(char raw in text)
        {
            if(raw=='\0')break;
            if(raw=='\n'){x=start;y+=64*sy;continue;}
            int n=raw is >= ' ' and <= '\x7f'?raw-32:'?'-32;
            var uv=new Vector4(n%16/16f,n/16/6f,(n%16+1)/16f,(n/16+1)/6f);
            float left=x-HudFonts.PaddingX*sx,top=y-HudFonts.PaddingY*sy;
            if(style.Outline>0)
            {
                float dx=style.Outline*unit*(_scene.Size.Y*256f/(_scene.Size.X*192f)),dy=style.Outline*unit;
                for(int j=0;j<8;j++)
                { float a=j*MathF.PI/4;float ox=MathF.Cos(a)*dx,oy=MathF.Sin(a)*dy;
                  _scene.DrawHudTexture(left+ox,top+oy,left+HudFonts.CellSize*sx+ox,top+HudFonts.CellSize*sy+oy,binding,alpha*style.Opacity,true,uv,outline); }
            }
            _scene.DrawHudTexture(left,top,left+HudFonts.CellSize*sx,top+HudFonts.CellSize*sy,binding,alpha*style.Opacity,true,uv,ink);
            x+=atlas.Advances[n]*sx;
        }
        return new(x,y);
    }
}
