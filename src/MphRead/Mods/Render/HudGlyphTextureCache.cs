using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render
{

    /// <summary>
    /// Immutable HUD images, owned by one scene. Exact pixels, not mutable font or
    /// palette identities, prove reuse. Entries stay alive until scene unload so a
    /// HUD instance may retain its binding between draws. Budget exhaustion uses
    /// that instance's separate mutable texture instead of recycling a live entry.
    /// </summary>
    internal sealed class HudGlyphTextureCache
    {
        private readonly record struct Key(int Width, int Height, ulong Hash);
        private sealed record Entry(int Binding, ColorRgba[] Pixels);
        private readonly Dictionary<Key, Entry> _entries = new();
        private readonly int _maxEntries;
        private readonly long _maxBytes;
        internal int Count => _entries.Count;
        internal long Bytes { get; private set; }

        internal HudGlyphTextureCache(int maxEntries = 1024, long maxBytes = 4 * 1024 * 1024)
        { _maxEntries = maxEntries; _maxBytes = maxBytes; }

        private static bool MakeKey(ReadOnlySpan<ColorRgba> pixels, int width, int height,
            out Key key, out int count)
        {
            key = default; count = 0;
            long size = (long)width * height;
            if (width <= 0 || height <= 0 || size > pixels.Length) return false;
            count = (int)size;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < count; i++)
                hash = unchecked((hash ^ pixels[i].ToUint()) * 1099511628211UL);
            key = new Key(width, height, hash);
            return true;
        }

        internal bool TryGet(ReadOnlySpan<ColorRgba> pixels, int width, int height, out int binding)
        {
            binding = 0;
            if (!MakeKey(pixels, width, height, out Key key, out int count)
                || !_entries.TryGetValue(key, out Entry? entry)) return false;
            // A hash collision must never substitute different texels.
            for (int i = 0; i < count; i++)
                if (pixels[i] != entry.Pixels[i]) return false;
            binding = entry.Binding;
            return true;
        }

        internal bool CanStore(ReadOnlySpan<ColorRgba> pixels, int width, int height)
            => MakeKey(pixels, width, height, out Key key, out int count)
            && _entries.Count < _maxEntries && (long)count * 4 <= _maxBytes - Bytes
            && !_entries.ContainsKey(key);

        internal bool Store(ReadOnlySpan<ColorRgba> pixels, int width, int height, int binding)
        {
            if (binding <= 0 || !CanStore(pixels, width, height)
                || !MakeKey(pixels, width, height, out Key key, out int count)) return false;
            _entries.Add(key, new Entry(binding, pixels[..count].ToArray()));
            Bytes += (long)count * 4;
            return true;
        }

        internal void Clear() { _entries.Clear(); Bytes = 0; }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private static readonly bool _hudGlyphCacheEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("HUD_GLYPHS");
        private static readonly bool _hudSpriteCacheEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("HUD_SPRITES");
        private readonly Mods.Render.HudGlyphTextureCache _hudGlyphTextures = new();
        private readonly Mods.Render.HudGlyphTextureCache _hudSpriteTextures = new();
        internal int HudGlyphTextureCount => _hudGlyphTextures.Count;
        internal long HudGlyphTextureBytes => _hudGlyphTextures.Bytes;
        internal int HudSpriteTextureCount => _hudSpriteTextures.Count;
        internal long HudSpriteTextureBytes => _hudSpriteTextures.Bytes;

        internal bool TryGetHudGlyphTexture(ColorRgba[] pixels, int width, int height, out int binding)
            => TryGetHudImageTexture(pixels, width, height, glyph: true, out binding);
        internal bool TryGetHudSpriteTexture(ColorRgba[] pixels, int width, int height, out int binding)
            => TryGetHudImageTexture(pixels, width, height, glyph: false, out binding);

        private bool TryGetHudImageTexture(ColorRgba[] pixels, int width, int height, bool glyph, out int binding)
        {
            binding = 0;
            if (!(glyph ? _hudGlyphCacheEnabled : _hudSpriteCacheEnabled)) return false;
            var cache = glyph ? _hudGlyphTextures : _hudSpriteTextures;
            if (cache.TryGet(pixels, width, height, out binding))
            {
                Mods.Render.OpenGlPassProfiler.Count(glyph ? Mods.Render.OpenGlFrameCounter.HudGlyphCacheHits
                    : Mods.Render.OpenGlFrameCounter.HudSpriteCacheHits);
                return true;
            }
            Mods.Render.OpenGlPassProfiler.Count(glyph ? Mods.Render.OpenGlFrameCounter.HudGlyphCacheMisses
                : Mods.Render.OpenGlFrameCounter.HudSpriteCacheMisses);
            if (!cache.CanStore(pixels, width, height))
            {
                Mods.Render.OpenGlPassProfiler.Count(glyph ? Mods.Render.OpenGlFrameCounter.HudGlyphCacheFallbacks
                    : Mods.Render.OpenGlFrameCounter.HudSpriteCacheFallbacks);
                return false;
            }
            binding = BindGetTexture(pixels, width, height, sceneColor: false);
            if (cache.Store(pixels, width, height, binding)) return true;
            ReleaseTexture(binding);
            binding = 0;
            return false;
        }
    }
}
