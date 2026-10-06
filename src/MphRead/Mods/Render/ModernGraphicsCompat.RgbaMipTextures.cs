#if !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        internal static void UploadRgbaMipTexture(RgbaMipTextureAsset asset,bool mipmaps)
        {
            ModernGraphicsCompat self=Current;
            int id=self._resources.BoundTexture(self._resources.ActiveTextureUnit);
            if (id == 0) throw new InvalidOperationException("No texture is bound.");
            RgbaTextureMip[] levels=mipmaps && asset.MipmapsAvailable ? asset.Mips : new[] {asset.Mips[0]};
            self._resources.RgbaMipTexImage2D(TextureTarget.Texture2D,levels);
            self.EnsureTexture(id);
        }

        private void UploadRgbaMipTexture(NativeTexture native,RgbaTextureMip[] levels,int mipCount)
        {
            if (levels.Length != mipCount)
                throw new InvalidDataException("Authored RGBA GPU mip count differs from prepared levels.");
            long start=PerformanceStart();
            FlushCommands();
            long uploaded=0;
            for (int level=0;level<mipCount;level++)
            {
                RgbaTextureMip mip=levels[level];
                if (mip.Width != Math.Max(1,native.Width >> level) || mip.Height != Math.Max(1,native.Height >> level)
                    || mip.Data.LongLength != checked((long)mip.Width*mip.Height*4))
                    throw new InvalidDataException($"Authored RGBA GPU mip {level} has invalid dimensions or bytes.");
                var destination=new ImageCopyTexture
                {Texture=native.Texture,Origin=new Origin3D(0,0,0),Aspect=TextureAspect.All,MipLevel=(uint)level};
                var layout=new TextureDataLayout {BytesPerRow=checked((uint)mip.Width*4u),RowsPerImage=(uint)mip.Height};
                var extent=new Extent3D((uint)mip.Width,(uint)mip.Height,1);
                fixed (byte* data=mip.Data)
                    _api.QueueWriteTexture(_queue,destination,data,(nuint)mip.Data.Length,layout,extent);
                uploaded+=mip.Data.LongLength;
            }
            if (start != 0)
            {
                _textureUploadBytes+=uploaded;
                _textureUploadMs+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        /// <summary>Opt-in asset probe: copies each uploaded mip back without sampling or color conversion.</summary>
        internal static int VerifyRgbaMipTextureForCheck(int id,RgbaMipTextureAsset asset,bool mipmaps)
        {
            ModernGraphicsCompat self=Current;
            NativeTexture native=self.EnsureTexture(id);
            int count=mipmaps && asset.MipmapsAvailable ? asset.Mips.Length : 1;
            if (native.Format != WgpuTextureFormat.Rgba8Unorm || native.MipCount != count
                || native.Width != asset.Width || native.Height != asset.Height)
                throw new InvalidOperationException("Authored RGBA native texture metadata differs from the prepared asset.");
            for (int level=0;level<count;level++)
            {
                RgbaTextureMip mip=asset.Mips[level];
                uint rowBytes=checked((uint)mip.Width*4u),paddedRow=(rowBytes+255u)&~255u;
                ulong total=checked((ulong)paddedRow*(uint)mip.Height);
                WgpuBuffer* readback=self._api.DeviceCreateBuffer(self._device.Device,new BufferDescriptor
                {Size=total,Usage=BufferUsage.CopyDst | BufferUsage.MapRead});
                if (readback == null) throw new InvalidOperationException("Authored RGBA probe buffer allocation failed.");
                bool mapped=false;
                try
                {
                    CommandEncoder* encoder=self.BeginCommands();
                    var source=new ImageCopyTexture {Texture=native.Texture,MipLevel=(uint)level,Aspect=TextureAspect.All};
                    var destination=new ImageCopyBuffer
                    {Buffer=readback,Layout=new TextureDataLayout {BytesPerRow=paddedRow,RowsPerImage=(uint)mip.Height}};
                    var extent=new Extent3D((uint)mip.Width,(uint)mip.Height,1);
                    self._api.CommandEncoderCopyTextureToBuffer(encoder,&source,&destination,&extent);
                    self.EndCommands(); self.FlushCommands();
                    _mapStatus=BufferMapAsyncStatus.Unknown;
                    self._api.BufferMapAsync(readback,MapMode.Read,0,(nuint)total,
                        new PfnBufferMapCallback((status,_)=>_mapStatus=status),null);
                    self._device.Native.DevicePoll(self._device.Device,true,null);
                    if (_mapStatus != BufferMapAsyncStatus.Success)
                        throw new InvalidOperationException($"Authored RGBA mip {level} readback map failed: {_mapStatus}.");
                    mapped=true;
                    byte* pixels=(byte*)self._api.BufferGetConstMappedRange(readback,0,(nuint)total);
                    if (pixels == null) throw new InvalidOperationException("Authored RGBA readback returned no mapped bytes.");
                    byte[] row=new byte[(int)rowBytes];
                    for (int y=0;y<mip.Height;y++)
                    {
                        Marshal.Copy((IntPtr)(pixels+y*paddedRow),row,0,row.Length);
                        if (!row.AsSpan().SequenceEqual(mip.Data.AsSpan(y*row.Length,row.Length)))
                            throw new InvalidOperationException($"Authored RGBA GPU bytes differ in mip {level}, row {y}.");
                    }
                }
                finally
                {
                    if (mapped) self._api.BufferUnmap(readback);
                    self._api.BufferRelease(readback);
                }
            }
            return count;
        }
    }
}
#endif
