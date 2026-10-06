#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// API-neutral texture/FBO/renderbuffer state for the WebGPU compatibility
    /// renderer. Object names intentionally follow OpenGL semantics so existing
    /// Scene caches remain valid.
    /// </summary>
    internal sealed class ModernGraphicsResourceState
    {
        internal sealed class TextureRecord
        {
            internal int Width;
            internal int Height;
            internal PixelInternalFormat InternalFormat;
            internal PixelFormat Format;
            internal PixelType Type;
            internal byte[]? Pixels;
            internal GpuTextureCompressionFormat CompressionFormat;
            internal CompressedTextureMip[]? CompressedMips;
            internal RgbaTextureMip[]? RgbaMips;
            internal int MinFilter = (int)TextureMinFilter.Nearest;
            internal int MagFilter = (int)TextureMagFilter.Nearest;
            internal int WrapS = (int)TextureWrapMode.Repeat;
            internal int WrapT = (int)TextureWrapMode.Repeat;
            internal bool FramebufferOrigin;
            internal bool HasMipmaps;
            internal int NativeMipCount = 1;
            internal bool MipmapsDirty;
            internal int Anisotropy = 1;
            internal bool SamplerDirty = true;
            internal bool Dirty = true;
        }

        internal sealed class RenderbufferRecord
        {
            internal int Width;
            internal int Height;
            internal RenderbufferStorage Format;
            internal bool Dirty = true;
        }

        internal sealed class FramebufferRecord
        {
            internal int ColorTexture;
            internal int ColorTexture1;
            internal int ColorTexture2;
            internal int DepthTexture;
            internal int DepthStencilTexture;
            internal int DepthRenderbuffer;
            internal int StencilRenderbuffer;
            internal int DepthStencilRenderbuffer;
        }

        private readonly Dictionary<int, TextureRecord> _textures = new();
        private readonly Dictionary<int, RenderbufferRecord> _renderbuffers = new();
        private readonly Dictionary<int, FramebufferRecord> _framebuffers = new();
        private readonly int[] _boundTextures = new int[32];

        private int _textureHighWater;
        private int _framebufferHighWater;
        private int _renderbufferHighWater;

        internal int ActiveTextureUnit { get; private set; }
        internal int DrawFramebuffer { get; private set; }
        internal int ReadFramebuffer { get; private set; }
        internal int BoundRenderbuffer { get; private set; }

        internal void InvalidateNativeResources()
        {
            foreach (var texture in _textures.Values)
            {
                texture.Dirty = texture.SamplerDirty = true;
                texture.MipmapsDirty = texture.CompressionFormat == GpuTextureCompressionFormat.None
                    && texture.HasMipmaps;
            }
            foreach (var renderbuffer in _renderbuffers.Values) renderbuffer.Dirty = true;
        }

        internal int GenTexture()
        {
            int id = ++_textureHighWater;
            _textures[id] = new TextureRecord();
            return id;
        }

        internal void DeleteTexture(int id)
        {
            _textures.Remove(id);
            for (int i = 0; i < _boundTextures.Length; i++)
            {
                if (_boundTextures[i] == id) _boundTextures[i] = 0;
            }
            foreach (FramebufferRecord framebuffer in _framebuffers.Values)
            {
                if (framebuffer.ColorTexture == id) framebuffer.ColorTexture = 0;
                if (framebuffer.ColorTexture1 == id) framebuffer.ColorTexture1 = 0;
                if (framebuffer.ColorTexture2 == id) framebuffer.ColorTexture2 = 0;
                if (framebuffer.DepthTexture == id) framebuffer.DepthTexture = 0;
                if (framebuffer.DepthStencilTexture == id) framebuffer.DepthStencilTexture = 0;
            }
        }

        internal bool IsTexture(int id) => id != 0 && _textures.ContainsKey(id);

        internal void ActiveTexture(TextureUnit unit)
        {
            int index = (int)unit - (int)TextureUnit.Texture0;
            if ((uint)index >= _boundTextures.Length)
                throw new ArgumentOutOfRangeException(nameof(unit), unit, "Only 32 texture units are supported.");
            ActiveTextureUnit = index;
        }

        internal void BindTexture(TextureTarget target, int id)
        {
            RequireTexture2D(target);
            if (id != 0) EnsureTexture(id);
            _boundTextures[ActiveTextureUnit] = id;
        }

        internal int BoundTexture(int unit) => (uint)unit < _boundTextures.Length ? _boundTextures[unit] : 0;

        internal TextureRecord Texture(int id)
        {
            return _textures.TryGetValue(id, out TextureRecord? record)
                ? record
                : throw new InvalidOperationException($"Unknown texture {id}.");
        }

        internal void TexParameter(TextureTarget target, TextureParameterName name, int value)
        {
            TextureRecord record = BoundTextureRecord(target);
            switch (name)
            {
            case TextureParameterName.TextureMinFilter:
                if (record.MinFilter == value) return;
                record.MinFilter = value;
                break;
            case TextureParameterName.TextureMagFilter:
                if (record.MagFilter == value) return;
                record.MagFilter = value;
                break;
            case TextureParameterName.TextureWrapS:
                if (record.WrapS == value) return;
                record.WrapS = value;
                break;
            case TextureParameterName.TextureWrapT:
                if (record.WrapT == value) return;
                record.WrapT = value;
                break;
            case (TextureParameterName)0x84FE: // GL_TEXTURE_MAX_ANISOTROPY_EXT
                value = Math.Clamp(value, 1, 16);
                if (record.Anisotropy == value) return;
                record.Anisotropy = value;
                break;
            default:
                return;
            }
            record.SamplerDirty = true;
        }

        internal void GenerateMipmap(GenerateMipmapTarget target)
        {
            if ((int)target != (int)TextureTarget.Texture2D)
                throw new NotSupportedException($"Mipmap target {target} is not supported.");
            TextureRecord record = BoundTextureRecord(TextureTarget.Texture2D);
            if (record.RgbaMips != null)
            {
                record.HasMipmaps=record.RgbaMips.Length > 1;
                record.MipmapsDirty=false; record.SamplerDirty=true;
                return;
            }
            if (record.CompressionFormat != GpuTextureCompressionFormat.None)
            {
                record.HasMipmaps = (record.CompressedMips?.Length ?? 0) > 1;
                record.MipmapsDirty = false;
                record.SamplerDirty = true;
                return;
            }
            record.HasMipmaps = true;
            record.MipmapsDirty = true;
            record.SamplerDirty = true;
        }

        internal void TexImage2D(TextureTarget target, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type, IntPtr pixels)
        {
            TextureRecord record = BoundTextureRecord(target);
            SetImageMetadata(record, internalFormat, width, height, format, type);
            if (pixels == IntPtr.Zero)
            {
                // Render targets and depth textures allocate storage without a
                // CPU upload. Do not force their pixel format through the
                // upload-byte calculator just to represent an empty image.
                record.Pixels = null;
                return;
            }
            int bytes = ImageByteCount(width, height, format, type);
            if (bytes == 0)
            {
                record.Pixels = null;
                return;
            }
            record.Pixels = new byte[bytes];
            Marshal.Copy(pixels, record.Pixels, 0, bytes);
        }

        internal void TexImage2D<T>(TextureTarget target, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            TextureRecord record = BoundTextureRecord(target);
            SetImageMetadata(record, internalFormat, width, height, format, type);
            record.Pixels = CopyStructArray(pixels);
        }

        internal void CompressedTexImage2D(TextureTarget target, int width, int height,
            GpuTextureCompressionFormat compressionFormat, CompressedTextureMip[] mips)
        {
            RequireTexture2D(target);
            if (compressionFormat == GpuTextureCompressionFormat.None || mips == null || mips.Length == 0)
                throw new ArgumentException("A compressed texture requires a format and at least one mip.");
            TextureRecord record = BoundTextureRecord(target);
            record.Width = Math.Max(0, width);
            record.Height = Math.Max(0, height);
            record.InternalFormat = PixelInternalFormat.Rgba8;
            record.Format = PixelFormat.Rgba;
            record.Type = PixelType.UnsignedByte;
            record.Pixels = null;
            record.CompressionFormat = compressionFormat;
            record.CompressedMips = (CompressedTextureMip[])mips.Clone();
            record.RgbaMips = null;
            record.FramebufferOrigin = false;
            record.HasMipmaps = mips.Length > 1;
            record.NativeMipCount = mips.Length;
            record.MipmapsDirty = false;
            record.SamplerDirty = true;
            record.Dirty = true;
        }

        internal void RgbaMipTexImage2D(TextureTarget target,RgbaTextureMip[] mips)
        {
            RequireTexture2D(target);
            if (mips == null || mips.Length == 0)
                throw new ArgumentException("An authored RGBA texture requires at least one mip.");
            TextureRecord record=BoundTextureRecord(target);
            SetImageMetadata(record,PixelInternalFormat.Rgba8,mips[0].Width,mips[0].Height,PixelFormat.Rgba,PixelType.UnsignedByte);
            record.Pixels=null;
            record.RgbaMips=(RgbaTextureMip[])mips.Clone();
            record.HasMipmaps=mips.Length > 1;
            record.NativeMipCount=mips.Length;
            record.SamplerDirty=true;
        }

        internal void TexSubImage2D(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, IntPtr pixels)
        {
            int bytes = ImageByteCount(width, height, format, type);
            byte[] source = new byte[bytes];
            if (pixels != IntPtr.Zero && bytes != 0) Marshal.Copy(pixels, source, 0, bytes);
            TexSubImage2DBytes(target, x, y, width, height, format, type, source);
        }

        internal void TexSubImage2D<T>(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            TexSubImage2DBytes(target, x, y, width, height, format, type, CopyStructArray(pixels));
        }

        internal void GetTexLevelParameter(TextureTarget target, int level, GetTextureParameter name, out int value)
        {
            if (level != 0) throw new NotSupportedException("Only base-level texture queries are used.");
            TextureRecord record = BoundTextureRecord(target);
            value = name switch
            {
                GetTextureParameter.TextureWidth => record.Width,
                GetTextureParameter.TextureHeight => record.Height,
                _ => 0
            };
        }

        internal int GenFramebuffer()
        {
            int id = ++_framebufferHighWater;
            _framebuffers[id] = new FramebufferRecord();
            return id;
        }

        internal void DeleteFramebuffer(int id)
        {
            _framebuffers.Remove(id);
            if (DrawFramebuffer == id) DrawFramebuffer = 0;
            if (ReadFramebuffer == id) ReadFramebuffer = 0;
        }

        internal bool IsFramebuffer(int id) => id != 0 && _framebuffers.ContainsKey(id);

        internal void BindFramebuffer(FramebufferTarget target, int id)
        {
            if (id != 0 && !_framebuffers.ContainsKey(id))
                throw new InvalidOperationException($"Unknown framebuffer {id}.");
            switch (target)
            {
            case FramebufferTarget.Framebuffer:
                DrawFramebuffer = id;
                ReadFramebuffer = id;
                break;
            case FramebufferTarget.DrawFramebuffer:
                DrawFramebuffer = id;
                break;
            case FramebufferTarget.ReadFramebuffer:
                ReadFramebuffer = id;
                break;
            default:
                throw new NotSupportedException($"Framebuffer target {target} is not supported.");
            }
        }

        internal bool IsFramebufferTexture(int texture)
        {
            if (texture == 0) return false;
            if (_textures.TryGetValue(texture, out var record) && record.FramebufferOrigin) return true;
            foreach (FramebufferRecord framebuffer in _framebuffers.Values)
            {
                if (framebuffer.ColorTexture == texture
                    || framebuffer.ColorTexture1 == texture
                    || framebuffer.ColorTexture2 == texture
                    || framebuffer.DepthTexture == texture
                    || framebuffer.DepthStencilTexture == texture)
                {
                    return true;
                }
            }
            return false;
        }

        internal FramebufferRecord Framebuffer(int id)
        {
            return _framebuffers.TryGetValue(id, out FramebufferRecord? record)
                ? record
                : throw new InvalidOperationException($"Unknown framebuffer {id}.");
        }

        internal void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment, int texture)
        {
            int framebuffer = BoundFramebuffer(target);
            if (framebuffer == 0)
                throw new InvalidOperationException("Cannot attach a texture to the default framebuffer.");
            if (texture != 0)
            {
                EnsureTexture(texture);
                // Attachment rotation (for example the three PBR targets) must
                // not forget the image's origin when it is later sampled.
                _textures[texture].FramebufferOrigin = true;
            }
            FramebufferRecord record = Framebuffer(framebuffer);
            switch (attachment)
            {
            case FramebufferAttachment.ColorAttachment0:
                record.ColorTexture = texture;
                break;
            case FramebufferAttachment.ColorAttachment1:
                record.ColorTexture1 = texture;
                break;
            case FramebufferAttachment.ColorAttachment2:
                record.ColorTexture2 = texture;
                break;
            case FramebufferAttachment.DepthAttachment:
                record.DepthTexture = texture;
                record.DepthRenderbuffer = 0;
                break;
            case FramebufferAttachment.DepthStencilAttachment:
                record.DepthStencilTexture = texture;
                record.DepthStencilRenderbuffer = 0;
                break;
            default:
                throw new NotSupportedException($"Framebuffer attachment {attachment} is not supported.");
            }
        }

        internal int GenRenderbuffer()
        {
            int id = ++_renderbufferHighWater;
            _renderbuffers[id] = new RenderbufferRecord();
            return id;
        }

        internal void DeleteRenderbuffer(int id)
        {
            _renderbuffers.Remove(id);
            if (BoundRenderbuffer == id) BoundRenderbuffer = 0;
            foreach (FramebufferRecord framebuffer in _framebuffers.Values)
            {
                if (framebuffer.DepthRenderbuffer == id) framebuffer.DepthRenderbuffer = 0;
                if (framebuffer.StencilRenderbuffer == id) framebuffer.StencilRenderbuffer = 0;
                if (framebuffer.DepthStencilRenderbuffer == id) framebuffer.DepthStencilRenderbuffer = 0;
            }
        }

        internal RenderbufferRecord Renderbuffer(int id)
        {
            return _renderbuffers.TryGetValue(id, out RenderbufferRecord? record)
                ? record
                : throw new InvalidOperationException($"Unknown renderbuffer {id}.");
        }

        internal void BindRenderbuffer(RenderbufferTarget target, int id)
        {
            if (target != RenderbufferTarget.Renderbuffer)
                throw new NotSupportedException($"Renderbuffer target {target} is not supported.");
            if (id != 0 && !_renderbuffers.ContainsKey(id))
                throw new InvalidOperationException($"Unknown renderbuffer {id}.");
            BoundRenderbuffer = id;
        }

        internal void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage format,
            int width, int height)
        {
            if (target != RenderbufferTarget.Renderbuffer || BoundRenderbuffer == 0)
                throw new InvalidOperationException("No renderbuffer is bound.");
            RenderbufferRecord record = _renderbuffers[BoundRenderbuffer];
            record.Format = format;
            record.Width = width;
            record.Height = height;
            record.Dirty = true;
        }

        internal void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbufferTarget, int renderbuffer)
        {
            if (renderbufferTarget != RenderbufferTarget.Renderbuffer)
                throw new NotSupportedException($"Renderbuffer target {renderbufferTarget} is not supported.");
            int framebuffer = BoundFramebuffer(target);
            if (framebuffer == 0)
                throw new InvalidOperationException("Cannot attach a renderbuffer to the default framebuffer.");
            if (renderbuffer != 0 && !_renderbuffers.ContainsKey(renderbuffer))
                throw new InvalidOperationException($"Unknown renderbuffer {renderbuffer}.");
            FramebufferRecord record = Framebuffer(framebuffer);
            switch (attachment)
            {
            case FramebufferAttachment.DepthAttachment:
                record.DepthRenderbuffer = renderbuffer;
                record.DepthTexture = 0;
                break;
            case FramebufferAttachment.StencilAttachment:
                record.StencilRenderbuffer = renderbuffer;
                break;
            case FramebufferAttachment.DepthStencilAttachment:
                record.DepthStencilRenderbuffer = renderbuffer;
                record.DepthStencilTexture = 0;
                break;
            default:
                throw new NotSupportedException($"Framebuffer attachment {attachment} is not supported.");
            }
        }

        internal FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
        {
            int id = BoundFramebuffer(target);
            if (id == 0) return FramebufferErrorCode.FramebufferComplete;
            FramebufferRecord record = Framebuffer(id);
            return record.ColorTexture != 0
                || record.ColorTexture1 != 0
                || record.ColorTexture2 != 0
                || record.DepthTexture != 0
                || record.DepthStencilTexture != 0
                || record.DepthRenderbuffer != 0
                || record.DepthStencilRenderbuffer != 0
                ? FramebufferErrorCode.FramebufferComplete
                : FramebufferErrorCode.FramebufferIncompleteMissingAttachment;
        }

        internal void GetFramebufferAttachmentParameter(FramebufferTarget target,
            FramebufferAttachment attachment, FramebufferParameterName name, out int value)
        {
            value = 0;
            if (name != FramebufferParameterName.FramebufferAttachmentDepthSize) return;
            int id = BoundFramebuffer(target);
            if (id == 0)
            {
                value = 24;
                return;
            }
            FramebufferRecord record = Framebuffer(id);
            if (attachment == FramebufferAttachment.DepthAttachment
                || attachment == FramebufferAttachment.DepthStencilAttachment)
            {
                value = record.DepthTexture != 0 || record.DepthStencilTexture != 0
                    || record.DepthRenderbuffer != 0 || record.DepthStencilRenderbuffer != 0
                    ? 24 : 0;
            }
        }

        private int BoundFramebuffer(FramebufferTarget target)
        {
            return target == FramebufferTarget.ReadFramebuffer ? ReadFramebuffer : DrawFramebuffer;
        }

        private TextureRecord BoundTextureRecord(TextureTarget target)
        {
            RequireTexture2D(target);
            int id = _boundTextures[ActiveTextureUnit];
            if (id == 0) throw new InvalidOperationException("No texture is bound.");
            return Texture(id);
        }

        private void EnsureTexture(int id)
        {
            if (id > _textureHighWater) _textureHighWater = id;
            if (!_textures.ContainsKey(id)) _textures[id] = new TextureRecord();
        }

        private static void RequireTexture2D(TextureTarget target)
        {
            if (target != TextureTarget.Texture2D)
                throw new NotSupportedException($"Texture target {target} is not supported.");
        }

        private static void SetImageMetadata(TextureRecord record, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type)
        {
            record.Width = Math.Max(0, width);
            record.Height = Math.Max(0, height);
            record.InternalFormat = internalFormat;
            record.Format = format;
            record.Type = type;
            record.CompressionFormat = GpuTextureCompressionFormat.None;
            record.CompressedMips = null;
            record.RgbaMips = null;
            record.FramebufferOrigin = false;
            record.HasMipmaps = false;
            record.NativeMipCount = 1;
            record.MipmapsDirty = false;
            record.Dirty = true;
        }

        private void TexSubImage2DBytes(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, byte[] source)
        {
            TextureRecord record = BoundTextureRecord(target);
            if (record.RgbaMips != null)
                throw new NotSupportedException("Sub-image updates are not supported for authored RGBA mip chains.");
            if (record.CompressionFormat != GpuTextureCompressionFormat.None)
                throw new NotSupportedException("Sub-image updates are not supported for block-compressed authored textures.");
            if (record.Format != format || record.Type != type)
            {
                // OpenGL allows conversion. The current Project Prime update
                // paths use the same source format they allocated with.
                throw new NotSupportedException("Texture sub-image format conversion is not required by Project Prime.");
            }
            // Validate the entire rectangle before mutating CPU storage. A row
            // that spills into the next row is still outside the texture.
            if (x < 0 || y < 0 || width < 0 || height < 0
                || x > record.Width || y > record.Height
                || width > record.Width - x || height > record.Height - y)
                throw new ArgumentOutOfRangeException(nameof(width), "Texture sub-image exceeds its allocation.");
            int pixelBytes = BytesPerPixel(format, type);
            if (source.Length < checked(width * height * pixelBytes))
                throw new ArgumentException("Texture sub-image source is too short.", nameof(source));
            if (width == 0 || height == 0) return;
            int destinationBytes = checked(record.Width * record.Height * pixelBytes);
            record.Pixels ??= new byte[destinationBytes];
            if (record.Pixels.Length < destinationBytes)
                Array.Resize(ref record.Pixels, destinationBytes);

            int sourceStride = checked(width * pixelBytes);
            int destinationStride = checked(record.Width * pixelBytes);
            for (int row = 0; row < height; row++)
            {
                int src = row * sourceStride;
                int dst = checked((y + row) * destinationStride + x * pixelBytes);
                if (src + sourceStride > source.Length || dst + sourceStride > record.Pixels.Length)
                    throw new ArgumentOutOfRangeException(nameof(source), "Texture sub-image exceeds its allocation.");
                System.Buffer.BlockCopy(source, src, record.Pixels, dst, sourceStride);
            }
            record.Dirty = true;
        }

        private static byte[] CopyStructArray<T>(T[] pixels) where T : struct
        {
            if (pixels.Length == 0) return Array.Empty<byte>();
            int bytes = checked(Marshal.SizeOf<T>() * pixels.Length);
            var output = new byte[bytes];
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                Marshal.Copy(handle.AddrOfPinnedObject(), output, 0, bytes);
            }
            finally
            {
                handle.Free();
            }
            return output;
        }

        private static int ImageByteCount(int width, int height, PixelFormat format, PixelType type)
        {
            return checked(Math.Max(0, width) * Math.Max(0, height) * BytesPerPixel(format, type));
        }

        internal static int BytesPerPixel(PixelFormat format, PixelType type)
        {
            if (format == PixelFormat.DepthStencil && type == PixelType.UnsignedInt248) return 4;
            int components = format switch
            {
                PixelFormat.Red => 1,
                PixelFormat.Rg => 2,
                PixelFormat.Rgb => 3,
                PixelFormat.Bgr => 3,
                PixelFormat.Rgba => 4,
                PixelFormat.Bgra => 4,
                _ => throw new NotSupportedException($"Pixel format {format} is not supported.")
            };
            int componentBytes = type switch
            {
                PixelType.UnsignedByte => 1,
                PixelType.Byte => 1,
                PixelType.UnsignedShort => 2,
                PixelType.Short => 2,
                PixelType.HalfFloat => 2,
                PixelType.UnsignedInt => 4,
                PixelType.Int => 4,
                PixelType.Float => 4,
                _ => throw new NotSupportedException($"Pixel type {type} is not supported.")
            };
            return checked(components * componentBytes);
        }
    }
}
#endif
