using System;

namespace MphRead.Mods.Render;

internal static class HudGlyphTextureCacheCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[hudglyphcheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        var red = new ColorRgba(255, 1, 2, 255);
        var transparent = new ColorRgba(7, 8, 9, 0);
        ColorRgba[] image = { red, transparent, new(3, 4, 5, 128), new(6, 7, 8, 254) };
        var cache = new HudGlyphTextureCache(8, 128);
        Check(!cache.TryGet(image, 2, 2, out int missing) && missing == 0,
            "unknown glyph has no invented binding");
        Check(cache.CanStore(image, 2, 2) && cache.Store(image, 2, 2, 41)
            && cache.Count == 1 && cache.Bytes == 16,
            "stored RGBA image accounts only its exact resident pixels");
        Check(cache.TryGet((ColorRgba[])image.Clone(), 2, 2, out int exact) && exact == 41,
            "exact decoded pixels reuse binding across different array objects");
        for (int channel = 0; channel < 4; channel++)
        {
            var changed = (ColorRgba[])image.Clone();
            ColorRgba original = changed[2];
            changed[2] = new ColorRgba((byte)(original.Red + (channel == 0 ? 1 : 0)),
                (byte)(original.Green + (channel == 1 ? 1 : 0)),
                (byte)(original.Blue + (channel == 2 ? 1 : 0)),
                (byte)(original.Alpha + (channel == 3 ? 1 : 0)));
            Check(!cache.TryGet(changed, 2, 2, out _), "changed RGBA channel " + channel + " never aliases cached glyph");
        }
        Check(!cache.TryGet(image, 1, 4, out _) && !cache.TryGet(image, 4, 1, out _),
            "same texels with different dimensions retain distinct images");
        Check(cache.Store(image, 1, 4, 42) && cache.TryGet(image, 1, 4, out int tall) && tall == 42,
            "dimension-specific bindings coexist");
        var originalImage = (ColorRgba[])image.Clone();
        image[0] = new ColorRgba(1, 2, 3, 4);
        Check(!cache.TryGet(image, 2, 2, out _)
            && cache.TryGet(originalImage, 2, 2, out int snapshot) && snapshot == 41,
            "mutating a decoder buffer cannot modify cached pixel snapshots");
        ColorRgba[] oversized = { red, transparent, originalImage[2], originalImage[3], new(10, 11, 12, 13) };
        Check(cache.TryGet(oversized, 2, 2, out int beforeTail) && beforeTail == 41,
            "oversized backing buffer tail is outside glyph identity");
        oversized[4] = new ColorRgba(99, 98, 97, 96);
        Check(cache.TryGet(oversized, 2, 2, out int afterTail) && afterTail == 41,
            "unused tail mutation never changes cache lookup");
        var limited = new HudGlyphTextureCache(1, 16);
        Check(limited.Store(originalImage, 2, 2, 71)
            && !limited.CanStore(image, 2, 2) && !limited.Store(image, 2, 2, 72)
            && limited.TryGet(originalImage, 2, 2, out int preserved) && preserved == 71,
            "entry exhaustion preserves existing immutable binding without eviction");
        var bytes = new HudGlyphTextureCache(8, 15);
        Check(!bytes.CanStore(originalImage, 2, 2) && !bytes.Store(originalImage, 2, 2, 1)
            && bytes.Count == 0 && bytes.Bytes == 0,
            "byte budget rejects oversize image without partial allocation");
        var zero = new HudGlyphTextureCache(0, 0);
        Check(!zero.CanStore(originalImage, 2, 2) && !zero.Store(originalImage, 2, 2, 1),
            "zero budgets use fallback");
        var invalid = new HudGlyphTextureCache();
        Check(!invalid.Store(originalImage, 2, 2, 0) && !invalid.Store(originalImage, 2, 2, -1)
            && invalid.Count == 0 && invalid.Bytes == 0,
            "invalid texture bindings are never published");
        Check(!invalid.CanStore(originalImage, 0, 2) && !invalid.CanStore(originalImage, -1, 2)
            && !invalid.CanStore(originalImage, 3, 2)
            && !invalid.CanStore(originalImage, int.MaxValue, int.MaxValue),
            "invalid dimensions and arithmetic bounds fail closed");
        Check(!cache.CanStore(originalImage, 2, 2) && !cache.Store(originalImage, 2, 2, 99)
            && cache.TryGet(originalImage, 2, 2, out int unchanged) && unchanged == 41,
            "duplicate store cannot replace an image used by an earlier draw");
        var otherScene = new HudGlyphTextureCache();
        Check(!otherScene.TryGet(originalImage, 2, 2, out _)
            && otherScene.Store(originalImage, 2, 2, 101)
            && cache.TryGet(originalImage, 2, 2, out int firstContext) && firstContext == 41,
            "independent scene and context owners never share GL bindings");
        cache.Clear();
        Check(cache.Count == 0 && cache.Bytes == 0 && !cache.TryGet(originalImage, 2, 2, out _),
            "unload clear forgets pixels and old context bindings");
        Check(cache.Store(originalImage, 2, 2, 201)
            && cache.TryGet(originalImage, 2, 2, out int rebuilt) && rebuilt == 201,
            "fresh context can rebuild same pixels with a new binding");
        return failures == 0 ? 0 : 1;
    }
}
