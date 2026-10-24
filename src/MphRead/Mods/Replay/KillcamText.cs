#if MPHREAD_AVALONIA
using System;
using System.Linq;
using System.Collections.Generic;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;
using MphRead.Formats;
using SkiaSharp;

namespace MphRead.Mods.Replay;

/// <summary>One immutable CPU glyph set; each replay scene owns its texture bindings.
/// Text changes never rasterize or upload another copy of an already-used glyph.</summary>
internal sealed class KillcamText
{
    private const int Cell = 48;
    private sealed record Glyph(ColorRgba[] Pixels, float Advance);
    private static readonly Lazy<Glyph[]> Glyphs = new(Build);
    private readonly Scene _scene;
    private readonly Dictionary<string,int> _fontTextures=new();
    private readonly int[] _textures = new int[95];
    internal KillcamText(Scene scene) { _scene = scene; _ = Glyphs.Value; }
    private static Glyph[] Build()
    {
        var assembly = typeof(KillcamText).Assembly;
        string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Roboto-Bold.ttf", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var face = SKTypeface.FromStream(stream);
        using var font = new SKFont(face, 32) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var bitmap = new SKBitmap(Cell, Cell, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        var glyphs = new Glyph[95];
        for (int i = 0; i < glyphs.Length; i++)
        {
            string character = ((char)(i + 32)).ToString();
            canvas.Clear(SKColors.Transparent);
            canvas.DrawText(character, 1, -font.Metrics.Ascent, font, paint);
            var pixels = new ColorRgba[Cell * Cell];
            for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
                pixels[y * Cell + x] = new(244, 249, 255, bitmap.GetPixel(x, y).Alpha);
            glyphs[i] = new(pixels, font.MeasureText(character) / 4);
        }
        return glyphs;
    }
    internal void Draw(float x, float y, string text, float scale, float alpha, float right)
    {
        var style=HudProfiles.Runtime.Typography.Get("game.replay");
        if(style.Enabled){DrawCustom(x,y,text,scale,alpha,right,style);return;}
        foreach (char character in text)
        {
            int index = character is >= ' ' and <= '~' ? character - 32 : '?' - 32;
            var glyph = Glyphs.Value[index];
            float advance = glyph.Advance * scale;
            if (x + advance > right) break;
            if (character != ' ')
            {
                if (_textures[index] == 0) _textures[index] = _scene.BindGetTexture(glyph.Pixels, Cell, Cell, sceneColor: false);
                _scene.DrawHudTexture(x, y, x + Cell / 4f * scale, y + Cell / 4f * scale, _textures[index], alpha);
            }
            x += advance;
        }
    }
    private void DrawCustom(float x,float y,string text,float scale,float alpha,float right,HudTextStyle style)
    {
        var atlas=HudFonts.Get(style.Font);
        if(!_fontTextures.TryGetValue(style.Font,out int texture))
        {
            var pixels=new ColorRgba[atlas.Alpha.Length];
            for(int i=0;i<pixels.Length;i++)pixels[i]=new(255,255,255,atlas.Alpha[i]);
            texture=_scene.BindGetTexture(pixels,HudFonts.AtlasWidth,HudFonts.AtlasHeight,sceneColor:false);_fontTextures.Add(style.Font,texture);
        }
        float unit=scale/6*style.Size*HudProfiles.Runtime.TextScale;
        var rgb=style.MatchPalette?HudProfiles.Runtime.AccentInk:style.Ink;
        var tint=style.FontOnlyOverride?new Vector4(244/255f,249/255f,1,1):new Vector4(rgb.X,rgb.Y,rgb.Z,1);
        foreach(char c in text)
        {
            int n=c is >= ' ' and <= '~'?c-32:'?'-32;
            float advance=atlas.Advances[n]*unit;if(x+advance>right)break;
            var uv=new Vector4(n%16/16f,n/16/6f,(n%16+1)/16f,(n/16+1)/6f);
            float left=x-HudFonts.PaddingX*unit,top=y-HudFonts.PaddingY*unit;
            if(style.Outline>0)
            {
                var oc=style.OutlineInk;var outline=new Vector4(oc.X,oc.Y,oc.Z,1);
                for(int i=0;i<8;i++)
                {
                    float angle=i*MathF.PI/4,dx=MathF.Cos(angle)*style.Outline*scale/3,dy=MathF.Sin(angle)*style.Outline*scale/3;
                    _scene.DrawHudTexture(left+dx,top+dy,left+HudFonts.CellSize*unit+dx,top+HudFonts.CellSize*unit+dy,texture,alpha*style.Opacity,true,uv,outline);
                }
            }
            _scene.DrawHudTexture(left,top,left+HudFonts.CellSize*unit,top+HudFonts.CellSize*unit,texture,alpha*style.Opacity,true,uv,tint);
            x+=advance;
        }
    }

}
#endif
