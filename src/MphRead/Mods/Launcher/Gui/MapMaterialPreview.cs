using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui
{
    internal static class MapMaterialPreview
    {
        public static (Bitmap Bitmap,string Details) Create(MapDefinition definition,MapMaterial material, int animationFrame = 0)
        {
            ColorRgba[] pixels;int width,height;string details;
            string? selectedTexture = material.Texture;
            int selectedFlipbookImage = 0;
            if (material.Animation is { FlipbookFrames: { Count: > 0 } frames } flipbook
                && flipbook.FlipbookHoldFrames > 0 && flipbook.LoopFrames > 0)
            {
                int localFrame = ((animationFrame + flipbook.PhaseFrames) % flipbook.LoopFrames + flipbook.LoopFrames) % flipbook.LoopFrames;
                selectedFlipbookImage = (localFrame / flipbook.FlipbookHoldFrames) % (frames.Count + 1);
                if (selectedFlipbookImage > 0) selectedTexture = frames[selectedFlipbookImage - 1];
            }
            if(selectedTexture is {} path)
            {
                var texture=MapTexturePack.Load(MapAssets.Read(definition,path),path).Entries.Single();
                width=texture.Width;height=texture.Height;
                pixels=texture.Pixels.Select(p=>new ColorRgba(texture.Palette[p])).ToArray();
                details=$"Custom · {width} × {height} · Palette8Bit";
            }
            else
            {
                var model=Read.GetRoomModelForExport(definition.TextureSource);
                if(material.SourceMaterial<0||material.SourceMaterial>=model.Materials.Count)throw new ArgumentException("Choose an existing source material.");
                var source=model.Materials[material.SourceMaterial];
                if(source.TextureId<0||source.PaletteId<0)
                {
                    int red=Math.Max((int)source.Diffuse.Red,source.Ambient.Red);
                    int green=Math.Max((int)source.Diffuse.Green,source.Ambient.Green);
                    int blue=Math.Max((int)source.Diffuse.Blue,source.Ambient.Blue);
                    pixels=new[]{new ColorRgba((byte)(red*255/31),(byte)(green*255/31),(byte)(blue*255/31),
                        (byte)(source.Alpha*255/31))};
                    width=height=1;
                    details=$"{definition.TextureSource}\n{source.Name} · flat native material · no texture";
                }
                else
                {
                    var recolor=model.Recolors[0];var texture=recolor.Textures[source.TextureId];
                    width=texture.Width;height=texture.Height;
                    pixels=recolor.GetPixels(source.TextureId,source.PaletteId).ToArray();
                    details=$"{definition.TextureSource}\n{source.Name} · {width} × {height} · {texture.Format}";
                }
            }
            float offsetU = 0, offsetV = 0, rotation = 0, scaleU = 1, scaleV = 1;
            if (material.Animation is { UvScroll: { Length: 2 } scroll, UvScale: { Length: 2 } scale,
                UvScalePulse: { Length: 2 } pulse } animation && animation.LoopFrames > 0)
            {
                int localFrame = ((animationFrame + animation.PhaseFrames) % animation.LoopFrames + animation.LoopFrames) % animation.LoopFrames;
                float seconds = localFrame / (float)MapUvAnimation.NativeFramesPerSecond;
                float pulsePhase = MathF.Tau * localFrame / animation.LoopFrames;
                offsetU = scroll[0] * seconds;
                offsetV = scroll[1] * seconds;
                rotation = animation.UvRotationDegreesPerSecond * seconds * MathF.PI / 180f;
                scaleU = scale[0] + pulse[0] * MathF.Sin(pulsePhase);
                scaleV = scale[1] + pulse[1] * MathF.Sin(pulsePhase);
                details += $"\nUV scroll {scroll[0]:0.###}, {scroll[1]:0.###} tiles/s";
                if (animation.UvRotationDegreesPerSecond != 0)
                    details += $" · rotate {animation.UvRotationDegreesPerSecond:0.##}°/s";
                if (scale[0] != 1 || scale[1] != 1 || pulse[0] != 0 || pulse[1] != 0)
                    details += $" · scale {scale[0]:0.##}, {scale[1]:0.##} ± {pulse[0]:0.##}, {pulse[1]:0.##}";
                details += $" · {animation.LoopFrames / 30f:0.##} s loop";
            }
            if (material.Animation?.FlipbookFrames is { Count: > 0 } previewFrames)
                details += $"\nFlipbook image {selectedFlipbookImage + 1}/{previewFrames.Count + 1}";
            if (material.Alpha is { } alpha) details += $"\nAlpha {alpha}/31";
            if (material.TwoSided) details += " · two-sided";

            var bitmap=new WriteableBitmap(new PixelSize(64,64),new Avalonia.Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
            using(var buffer=bitmap.Lock())
            {
                var row=new byte[64*4];
                for(int y=0;y<64;y++)
                {
                    for(int x=0;x<64;x++)
                    {
                        float u=x/64f+offsetU;
                        float v=y/64f+offsetV;
                        if(rotation!=0)
                        {
                            u+=0.5f;v+=0.5f;
                            float cos=MathF.Cos(rotation),sin=MathF.Sin(rotation);
                            (u,v)=(cos*u-sin*v,sin*u+cos*v);
                            u-=0.5f;v-=0.5f;
                        }
                        u*=scaleU;v*=scaleV;
                        int sourceX=Wrap((int)MathF.Floor(u*width),width);
                        int sourceY=Wrap((int)MathF.Floor(v*height),height);
                        var pixel=pixels[sourceY*width+sourceX];
                        row[x*4]=pixel.Blue;row[x*4+1]=pixel.Green;row[x*4+2]=pixel.Red;row[x*4+3]=pixel.Alpha;
                    }
                    Marshal.Copy(row,0,buffer.Address+y*buffer.RowBytes,row.Length);
                }
            }
            return(bitmap,details);
        }

        private static int Wrap(int value,int size)
        {
            int wrapped=value%size;
            return wrapped<0?wrapped+size:wrapped;
        }
    }
}
