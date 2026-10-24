using System;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Render.Hud;

internal static class HudAssetChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Require(bool okay, string detail)
        {
            if (!okay) throw new InvalidOperationException(detail);
            Console.WriteLine("HUD ASSET PASS " + detail); checks++;
        }
        void Reject(Action action, string detail)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, detail);
        }
        try
        {
            Require(HudAssetReader.DsFont.Alpha.Length == 768 * 384, "DS atlas present in this compiled assembly");
            Require(HudAssetReader.DsFont.Advances.Length == 96, "DS glyph advances complete");
            Require(HudAssetReader.DsFont.Alpha.Any(v => v != 0), "DS atlas contains visible glyphs");
            Require(HudAssetReader.DsBackground.Length == 768 * 576 * 3, "DS background present and complete");
            foreach (var entry in HudFonts.All)
            {
                var atlas = HudFonts.Get(entry.Id);
                Require(atlas.Advances.Length == 96 && atlas.Alpha.Length == HudFonts.AtlasWidth * HudFonts.AtlasHeight
                    && atlas.Alpha.Any(v => v != 0), entry.Id + " present and decoded through runtime loader");
            }
            Reject(() => HudAssetReader.ReadAtlas(null, "missing", 1, 1), "Missing streams rejected with asset diagnostic (not BinaryReader(null))");
            using var shortData = new MemoryStream(new byte[383]);
            Reject(() => HudAssetReader.ReadAtlas(shortData, "short", 1, 1), "Truncated atlas rejected");
            using var trailing = new MemoryStream(new byte[386]);
            Reject(() => HudAssetReader.ReadAtlas(trailing, "trailing", 1, 1), "Trailing atlas bytes rejected");
            var bytes = new byte[385];
            BitConverter.GetBytes(float.NaN).CopyTo(bytes, 0);
            using var invalidAdvance = new MemoryStream(bytes);
            Reject(() => HudAssetReader.ReadAtlas(invalidAdvance, "nan", 1, 1), "Invalid glyph metrics rejected");
            bytes = new byte[385]; bytes[384] = 255;
            using var valid = new MemoryStream(bytes);
            Require(HudAssetReader.ReadAtlas(valid, "valid", 1, 1).Alpha[0] == 255, "Exact valid payload accepted");
            Console.WriteLine($"HUD ASSET CHECK PASSED: {checks} assertions");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("HUD ASSET CHECK FAILED: " + e); return 1; }
    }
}
