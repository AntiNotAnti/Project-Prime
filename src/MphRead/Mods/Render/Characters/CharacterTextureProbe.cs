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
    private static double GridChannel(ModernTextureAsset image,int gx,int gy,int channel,bool linear)
    {
        double u=(gx+.5)/32,v=(gy+.5)/32;
        if (!linear)
        {
            int x=Math.Min(image.Width-1,(int)(u*image.Width)),y=Math.Min(image.Height-1,(int)(v*image.Height));
            return image.Pixels[(y*image.Width+x)*4+channel];
        }
        // Normalized sampling places texel centers at (index + .5) / extent.
        // Probe uploads use ClampToEdge, matching the clamped neighbor indices.
        double sx=u*image.Width-.5,sy=v*image.Height-.5;
        int x0=(int)Math.Floor(sx),y0=(int)Math.Floor(sy);
        double fx=sx-x0,fy=sy-y0;
        int xa=Math.Clamp(x0,0,image.Width-1),xb=Math.Clamp(x0+1,0,image.Width-1);
        int ya=Math.Clamp(y0,0,image.Height-1),yb=Math.Clamp(y0+1,0,image.Height-1);
        double top=image.Pixels[(ya*image.Width+xa)*4+channel]*(1-fx)+image.Pixels[(ya*image.Width+xb)*4+channel]*fx;
        double bottom=image.Pixels[(yb*image.Width+xa)*4+channel]*(1-fx)+image.Pixels[(yb*image.Width+xb)*4+channel]*fx;
        return top*(1-fy)+bottom*fy;
    }

    internal static string Verify(string root)
    {
        CharacterModelPack pack=CharacterModelPack.Load(root,mobile:true);
        var images=new Dictionary<(string,TextureAssetChannel),CharacterEmbeddedAlbedo>();
        var physicalMaterialImages=new HashSet<string>();
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
            if (maps.PhysicalOrm && maps.MetallicRoughness != null)
                physicalMaterialImages.Add(Convert.ToHexString(SHA256.HashData(maps.MetallicRoughness.Image)));
            Add(maps.Normal,TextureAssetChannel.Normal);Add(maps.MetallicRoughness,TextureAssetChannel.Material);Add(maps.Emissive,TextureAssetChannel.Emissive);
        }
        if(images.Count==0)throw new InvalidDataException("No embedded mobile images.");
        using var residency=new TextureAssetManager(GraphicsApi.GenTexture,GraphicsApi.DeleteTexture);
        int color=GraphicsApi.GenTexture(),framebuffer=GraphicsApi.GenFramebuffer();
        var checks=new List<object>();int compressed=0,maxError=0,samples=0,authoredRgbaImages=0,authoredRgbaLevels=0;double squaredError=0;
        int unalignedBlockExtentImages=0;
        var residentBytesByPreparedType=new Dictionary<string,long>();
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
                int cap=TextureAssetManager.DimensionLimit(TextureAssetClass.Hunter,channel);
                using var encoded=new MemoryStream(image.Image,writable:false);
                var prepared=PreparedTextureCodec.Decode(encoded,pair.Key.Item1,TextureAssetClass.Hunter,channel,cap);
                compressed+=prepared is Ktx2TextureAsset ? 1 : 0;
                long beforeResident=residency.ResidentBytes;
                int binding=residency.Upload(pair.Key.Item1,()=>new MemoryStream(image.Image,writable:false),TextureAssetClass.Hunter,channel,false,out int width,out int height);
                if(binding==0)throw new InvalidOperationException("Mobile texture admission/upload failed.");
                var sampling=TextureSamplingPolicy.ResolveModern(TextureAssetClass.Hunter,channel);
                bool mipmaps=sampling.Mipmaps && prepared.MipmapsAvailable;
                if(residency.ResidentBytes-beforeResident != prepared.EstimateGpuBytes(mipmaps))
                    throw new InvalidOperationException("Texture admission byte accounting differs from prepared resident levels.");
                string preparedType=prepared.GetType().Name;
                residentBytesByPreparedType[preparedType]=residentBytesByPreparedType.GetValueOrDefault(preparedType)+prepared.EstimateGpuBytes(mipmaps);
                if(prepared is RgbaMipTextureAsset npot && npot.PreparationReason == "unaligned-block-extent") unalignedBlockExtentImages++;
                int exactRgbaLevels=0;
                if(prepared is RgbaMipTextureAsset authored && ModernGraphicsCompat.Active)
                {
                    exactRgbaLevels=ModernGraphicsCompat.VerifyRgbaMipTextureForCheck(binding,authored,mipmaps);
                    authoredRgbaImages++;authoredRgbaLevels+=exactRgbaLevels;
                }
                using var referenceStream=new MemoryStream(image.Image,writable:false);
                var reference=prepared is RgbaMipTextureAsset rgba
                    ? ModernTextureAsset.FromRgba("reference",TextureAssetClass.Hunter,channel,rgba.Width,rgba.Height,rgba.Mips[0].Data)
                    : PreparedTextureCodec.DecodeRgba(referenceStream,"reference",TextureAssetClass.Hunter,channel,cap);
                bool physicalOrm=channel==TextureAssetChannel.Material && physicalMaterialImages.Contains(pair.Key.Item1);
                if (physicalOrm)
                    for(int at=3;at<reference.Pixels.Length;at+=4)
                        if(reference.Pixels[at] != 0)
                            throw new InvalidDataException("Preconverted ORM material alpha must be the zero encoding marker.");
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);GraphicsApi.BindTexture(TextureTarget.Texture2D,binding);GraphicsApi.Enable(EnableCap.Texture2D);GraphicsApi.Color4(1f,1f,1f,1f);
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                // Sample a spatial grid too: four constant-UV points can miss damaged blocks.
                double implicitLod=prepared is Ktx2TextureAsset compressedAsset && mipmaps
                    ? Math.Clamp(Math.Log2(Math.Max(width,height)/32.0),0,compressedAsset.Mips.Length-1) : 0;
                var gridChecks=new List<object>();
                foreach (double lod in new[]{0.0,implicitLod}.Distinct())
                {
                int mip=(int)Math.Floor(lod),nextMip=(int)Math.Ceiling(lod);
                bool linear=lod > 0;
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)(linear ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Nearest));
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)(linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
                GraphicsApi.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, 1);
                using var mipStream=new MemoryStream(image.Image,writable:false);
                // A compressed prepared asset currently keeps source level 0;
                // dimension-capped character assets instead use rgba.Mips[0].
                // Decode explicit KTX source levels without another resize.
                int sourceBaseLevel=prepared is RgbaMipTextureAsset baseRgba ? baseRgba.SourceBaseLevel : 0;
                var gridReference=!linear ? reference : Ktx2TextureAsset.DecodeRgba(mipStream,"mip-reference",TextureAssetClass.Hunter,channel,ModernTextureAsset.MaximumDimension,sourceBaseLevel+mip);
                using var nextStream=new MemoryStream(image.Image,writable:false);
                var nextReference=nextMip == mip ? gridReference : Ktx2TextureAsset.DecodeRgba(nextStream,"next-mip-reference",TextureAssetClass.Hunter,channel,ModernTextureAsset.MaximumDimension,sourceBaseLevel+nextMip);
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
                    // UV v=0 is on the quad's GL bottom row, matching encoded row zero.
                    int actual=(gy*32+gx)*4,error=0;
                    var expected=new int[4];
                    for(int c=0;c<4;c++)
                    {
                        double first=GridChannel(gridReference,gx,gy,c,linear),second=GridChannel(nextReference,gx,gy,c,linear);
                        expected[c]=(int)Math.Round(first+(second-first)*(lod-mip));
                        int delta=grid[actual+c]-expected[c];error=Math.Max(error,Math.Abs(delta));
                        if(c<3)squaredError+=delta*delta;
                    }
                    maxError=Math.Max(maxError,error);samples++;
                    int ceiling=format==GpuTextureCompressionFormat.Etc2Rgba8 ? 64 : 32;
                    if(error>ceiling)throw new InvalidOperationException($"Compressed grid error {error}: {channel}, {width}x{height}, lod {lod}, source levels {sourceBaseLevel+mip}/{sourceBaseLevel+nextMip}, grid {gx},{gy}; GPU={string.Join(',',grid.Skip(actual).Take(4))} reference={string.Join(',',expected)}");
                }
                gridChecks.Add(new{lod,linear,sourceLevel=sourceBaseLevel+mip,nextSourceLevel=sourceBaseLevel+nextMip});
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
                checks.Add(new{channel=channel.ToString(),width,height,encodedBytes=image.Image.Length,prepared=prepared.GetType().Name,
                    physicalOrm,materialEncoding=channel==TextureAssetChannel.Material ? (physicalOrm ? "orm-alpha-zero" : "legacy-specular-roughness") : "not-material",
                    residentBytes=prepared.EstimateGpuBytes(mipmaps),exactRgbaLevels,gridChecks,
                    preparationReason=prepared is RgbaMipTextureAsset reason ? reason.PreparationReason : "native-block-or-raster",
                    sourceBaseLevel=prepared is RgbaMipTextureAsset bounded ? bounded.SourceBaseLevel : 0});
            }
            long resident=residency.ResidentBytes;int count=residency.ResidentCount;
            double rmsError=Math.Sqrt(squaredError/(samples*3));
            if(rmsError>8)throw new InvalidOperationException("Aggregate compressed RGB error too high: "+rmsError);
            residency.Clear();if(residency.ResidentBytes!=0||residency.ResidentCount!=0)throw new InvalidOperationException("Texture release accounting failed.");
            if(residentBytesByPreparedType.Values.Sum() != resident)throw new InvalidOperationException("Mixed compressed/RGBA resident type accounting differs from total admission.");
            return JsonSerializer.Serialize(new{pass=true,compression=format.ToString(),images=images.Count,physicalOrmImages=physicalMaterialImages.Count,compressed,authoredRgbaImages,authoredRgbaLevels,unalignedBlockExtentImages,samples,maxError,rmsError,residentBytes=resident,residentBytesByPreparedType,residentCount=count,releasedBytes=residency.ResidentBytes,checks,scope="Asset-only upload/sample/readback and release on active GPU. Modern authored RGBA assets additionally copy every uploaded mip back and require byte-exact equality; admission bytes equal the uploaded level sum, separately counted for compressed/RGBA/raster. Opt-in ORM images verify the zero companion-alpha marker separately from albedo opacity. Unaligned compressed base extents preserve exact aspect/UV/artwork using authored RGBA fallback. ETC2 max channel delta ceiling 64, other formats 32; all formats RMS <=8/255. Does not measure game frame pacing or process/driver residency."},new JsonSerializerOptions{WriteIndented=true});
        }
        finally{GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,0);GraphicsApi.DeleteFramebuffer(framebuffer);GraphicsApi.DeleteTexture(color);}
    }
}
#endif
