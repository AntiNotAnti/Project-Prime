#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render.Characters;

/// <summary>Asset-only compressed texture upload/readback, shared by desktop and Android diagnostics.</summary>
internal static class CharacterTextureProbe
{
    internal static string Verify(string root)
    {
        CharacterModelPack pack=CharacterModelPack.Load(root,mobile:true);
        var images=new Dictionary<(string,TextureAssetChannel),CharacterEmbeddedAlbedo>();
        foreach (Hunter hunter in Enum.GetValues<Hunter>())
        foreach (CharacterModelPart part in Enum.GetValues<CharacterModelPart>())
        for (int lod=0;lod<=1;lod++)
        {
            if (!pack.TryResolve(hunter,part,lod,out var asset)) continue;
            if (asset.Skinning==CharacterSkinningMode.Weighted4)
                foreach(var p in CharacterWeightedModelLoader.Load(asset).Primitives) Collect(p.Albedo,p.MaterialMaps);
            else foreach(var p in CharacterRigidModelLoader.Load(asset).Primitives) Collect(p.Albedo,p.MaterialMaps);
        }
        void Add(CharacterEmbeddedAlbedo? image,TextureAssetChannel channel)
        {
            if (image!=null) images.TryAdd((Convert.ToHexString(SHA256.HashData(image.Image)),channel),image);
        }
        void Collect(CharacterEmbeddedAlbedo? albedo,CharacterEmbeddedMaterialMaps? maps)
        {
            Add(albedo,TextureAssetChannel.Albedo);
            if(albedo?.Recolors!=null)foreach(var image in albedo.Recolors.Values)Add(image,TextureAssetChannel.Albedo);
            if(maps==null)return;
            if (!maps.RuntimeEncoded) throw new InvalidDataException("Probe expects preconverted mobile maps.");
            Add(maps.Normal,TextureAssetChannel.Normal);Add(maps.MetallicRoughness,TextureAssetChannel.Material);Add(maps.Emissive,TextureAssetChannel.Emissive);
        }
        if(images.Count==0)throw new InvalidDataException("No embedded mobile images.");
        using var residency=new TextureAssetManager(GraphicsApi.GenTexture,GraphicsApi.DeleteTexture);
        int color=GraphicsApi.GenTexture(),framebuffer=GraphicsApi.GenFramebuffer();
        var checks=new List<object>();int compressed=0,maxError=0,samples=0;double squaredError=0;
        var format=ModernGraphicsCompat.PreferredCharacterTextureCompression;
        try
        {
            GraphicsApi.BindTexture(TextureTarget.Texture2D,color);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,32,32,0,PixelFormat.Rgba,PixelType.UnsignedByte,new byte[32*32*4]);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,color,0);
            GraphicsApi.Viewport(0,0,32,32);GraphicsApi.Disable(EnableCap.DepthTest);GraphicsApi.Disable(EnableCap.Blend);GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.MatrixMode(MatrixMode.Projection);GraphicsApi.LoadIdentity();GraphicsApi.MatrixMode(MatrixMode.Modelview);GraphicsApi.LoadIdentity();
            foreach(var pair in images)
            {
                var image=pair.Value;var channel=pair.Key.Item2;
                using var encoded=new MemoryStream(image.Image,writable:false);
                var prepared=PreparedTextureCodec.Decode(encoded,pair.Key.Item1,TextureAssetClass.Hunter,channel,1024);
                compressed+=prepared is Ktx2TextureAsset ? 1 : 0;
                int binding=residency.Upload(pair.Key.Item1,()=>new MemoryStream(image.Image,writable:false),TextureAssetClass.Hunter,channel,false,out int width,out int height);
                if(binding==0)throw new InvalidOperationException("Mobile texture admission/upload failed.");
                using var referenceStream=new MemoryStream(image.Image,writable:false);
                var reference=PreparedTextureCodec.DecodeRgba(referenceStream,"reference",TextureAssetClass.Hunter,channel,1024);
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);GraphicsApi.BindTexture(TextureTarget.Texture2D,binding);GraphicsApi.Enable(EnableCap.Texture2D);GraphicsApi.Color4(1f,1f,1f,1f);
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                // Sample a spatial grid too: four constant-UV points can miss damaged blocks.
                foreach (int mip in new[]{0,prepared is Ktx2TextureAsset ? Math.Max(0,(int)Math.Log2(width/32)) : 0}.Distinct())
                {
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)(mip==0 ? TextureMinFilter.Nearest : TextureMinFilter.LinearMipmapLinear));
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)(mip==0 ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
                GraphicsApi.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, 1);
                using var mipStream=new MemoryStream(image.Image,writable:false);
                var gridReference=mip==0 ? reference : Ktx2TextureAsset.DecodeRgba(mipStream,"mip-reference",TextureAssetClass.Hunter,channel,1024,mip);
                GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                GraphicsApi.Begin(PrimitiveType.Quads);
                foreach (var p in new[]{(-1f,-1f),(1f,-1f),(1f,1f),(-1f,1f)})
                {
                    GraphicsApi.TexCoord2((p.Item1+1)*.5f,(p.Item2+1)*.5f);
                    GraphicsApi.Vertex3(p.Item1,p.Item2,0);
                }
                GraphicsApi.End();
                var grid=new byte[32*32*4];GraphicsApi.ReadPixels(0,0,32,32,PixelFormat.Rgba,PixelType.UnsignedByte,grid);
                for(int gy=0;gy<32;gy++)for(int gx=0;gx<32;gx++)
                {
                    int rw=gridReference.Width,rh=gridReference.Height;
                    int offset=((gy*rh/32+rh/64)*rw+gx*rw/32+rw/64)*4;
                    // UV v=0 is on the quad's GL bottom row, matching encoded row zero.
                    int actual=(gy*32+gx)*4,error=0;
                    for(int c=0;c<4;c++)
                    {
                        int delta=grid[actual+c]-gridReference.Pixels[offset+c];error=Math.Max(error,Math.Abs(delta));
                        if(c<3)squaredError+=delta*delta;
                    }
                    maxError=Math.Max(maxError,error);samples++;
                    int ceiling=format==GpuTextureCompressionFormat.Etc2Rgba8 ? 64 : 32;
                    if(error>ceiling)throw new InvalidOperationException($"Compressed grid error {error}: {channel}, {width}, mip {mip}, grid {gx},{gy}; GPU={string.Join(',',grid.Skip(actual).Take(4))} reference={string.Join(',',gridReference.Pixels.Skip(offset).Take(4))}");
                }
                }
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                foreach (var point in new[]{(.25f,.25f),(.75f,.25f),(.25f,.75f),(.75f,.75f)})
                {
                    int baseX=(int)(point.Item1*width),baseY=(int)(point.Item2*height);
                    GraphicsApi.ClearColor(0,0,0,1);GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                    GraphicsApi.Begin(PrimitiveType.Quads);
                    foreach (var p in new[]{(-1f,-1f),(1f,-1f),(1f,1f),(-1f,1f)})
                    {
                        GraphicsApi.TexCoord2((baseX+(p.Item1+1)*2)/width,(baseY+(p.Item2+1)*2)/height);
                        GraphicsApi.Vertex3(p.Item1,p.Item2,0);
                    }
                    GraphicsApi.End();var block=new byte[32*32*4];GraphicsApi.ReadPixels(0,0,32,32,PixelFormat.Rgba,PixelType.UnsignedByte,block);
                    for (int subY=0;subY<4;subY++) for (int subX=0;subX<4;subX++)
                    {
                        int x=baseX+subX,y=baseY+subY,actual=((subY*8+4)*32+subX*8+4)*4;
                        int offset=(y*width+x)*4,error=0;for(int c=0;c<4;c++)
                        {
                            int delta=block[actual+c]-reference.Pixels[offset+c];error=Math.Max(error,Math.Abs(delta));
                            if(c<3)squaredError+=delta*delta;
                        }
                        maxError=Math.Max(maxError,error);samples++;
                        int ceiling=format==GpuTextureCompressionFormat.Etc2Rgba8 ? 64 : 32;
                        if(error>ceiling)throw new InvalidOperationException($"Compressed block error {error}: {channel}, {width}, {x},{y}");
                    }
                }
                checks.Add(new{channel=channel.ToString(),width,height,encodedBytes=image.Image.Length,prepared=prepared.GetType().Name});
            }
            long resident=residency.ResidentBytes;int count=residency.ResidentCount;
            double rmsError=Math.Sqrt(squaredError/(samples*3));
            if(rmsError>8)throw new InvalidOperationException("Aggregate compressed RGB error too high: "+rmsError);
            residency.Clear();if(residency.ResidentBytes!=0||residency.ResidentCount!=0)throw new InvalidOperationException("Texture release accounting failed.");
            return JsonSerializer.Serialize(new{pass=true,compression=format.ToString(),images=images.Count,compressed,samples,maxError,rmsError,residentBytes=resident,residentCount=count,releasedBytes=residency.ResidentBytes,checks,scope="Asset-only upload/sample/readback and release on active GPU. ETC2 max channel delta ceiling 64, other formats 32; all formats RMS <=8/255. Does not measure game frame pacing or process/driver residency."},new JsonSerializerOptions{WriteIndented=true});
        }
        finally{GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,0);GraphicsApi.DeleteFramebuffer(framebuffer);GraphicsApi.DeleteTexture(color);}
    }
}
#endif
