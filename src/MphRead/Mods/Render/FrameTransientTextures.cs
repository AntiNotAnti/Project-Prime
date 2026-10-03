using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
    public partial class Scene
    {
        private sealed class FrameTransientTexture
        {
            internal int Texture;
            internal Vector2i Size;
            internal PixelInternalFormat Format;
            internal TextureMinFilter MinFilter;
            internal TextureMagFilter MagFilter;
            internal bool InUse;
            internal long LastUse;
        }

        // The frame graph owns these scratch color targets. A lease may move
        // between graph passes only after its previous pass has finished.
        // Keeping a tiny bounded free list avoids repeated native allocations on
        // resize/fullscreen transitions while still allowing true same-frame
        // aliasing when lifetimes do not overlap.
        private readonly List<FrameTransientTexture> _frameTransientTextures = new();
        private long _frameTransientTextureSerial;
        private long _frameTransientTextureHits;
        private long _frameTransientTextureMisses;
        private long _frameTransientTextureAliases;

        internal long FrameTransientTextureHits => _frameTransientTextureHits;
        internal long FrameTransientTextureMisses => _frameTransientTextureMisses;
        internal long FrameTransientTextureAliases => _frameTransientTextureAliases;

        private int AcquireFrameTransientTexture(
            Vector2i size, PixelInternalFormat format,
            TextureMinFilter minFilter, TextureMagFilter magFilter)
        {
            for (int i = 0; i < _frameTransientTextures.Count; i++)
            {
                FrameTransientTexture candidate = _frameTransientTextures[i];
                if (!candidate.InUse
                    && candidate.Size == size
                    && candidate.Format == format
                    && candidate.MinFilter == minFilter
                    && candidate.MagFilter == magFilter)
                {
                    candidate.InUse = true;
                    candidate.LastUse = ++_frameTransientTextureSerial;
                    _frameTransientTextureHits++;
                    _frameTransientTextureAliases++;
                    return candidate.Texture;
                }
            }

            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            PixelType type = format == PixelInternalFormat.Rgba16f
                ? PixelType.Float : PixelType.UnsignedByte;
            GL.TexImage2D(TextureTarget.Texture2D, 0, format,
                size.X, size.Y, 0, PixelFormat.Rgba, type, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)minFilter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)magFilter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            _frameTransientTextures.Add(new FrameTransientTexture
            {
                Texture = texture,
                Size = size,
                Format = format,
                MinFilter = minFilter,
                MagFilter = magFilter,
                InUse = true,
                LastUse = ++_frameTransientTextureSerial
            });
            _frameTransientTextureMisses++;
            return texture;
        }

        private void ReleaseFrameTransientTexture(ref int texture)
        {
            if (texture == 0)
                return;
            for (int i = 0; i < _frameTransientTextures.Count; i++)
            {
                FrameTransientTexture entry = _frameTransientTextures[i];
                if (entry.Texture == texture)
                {
                    entry.InUse = false;
                    entry.LastUse = ++_frameTransientTextureSerial;
                    texture = 0;
                    TrimFrameTransientTextures();
                    return;
                }
            }

            // A pre-pool or partially-created target must never leak.
            DeleteTexture(ref texture);
        }

        private void TrimFrameTransientTextures()
        {
            const int maxFreeTextures = 4;
            while (true)
            {
                int free = 0;
                int oldestIndex = -1;
                long oldestUse = long.MaxValue;
                for (int i = 0; i < _frameTransientTextures.Count; i++)
                {
                    FrameTransientTexture entry = _frameTransientTextures[i];
                    if (entry.InUse)
                        continue;
                    free++;
                    if (entry.LastUse < oldestUse)
                    {
                        oldestUse = entry.LastUse;
                        oldestIndex = i;
                    }
                }

                if (free <= maxFreeTextures || oldestIndex < 0)
                    break;
                FrameTransientTexture oldest = _frameTransientTextures[oldestIndex];
                int id = oldest.Texture;
                DeleteTexture(ref id);
                _frameTransientTextures.RemoveAt(oldestIndex);
            }
        }

        private void DisposeFrameTransientTextures()
        {
            foreach (FrameTransientTexture entry in _frameTransientTextures)
            {
                int texture = entry.Texture;
                DeleteTexture(ref texture);
            }
            _frameTransientTextures.Clear();
        }
    }
}
