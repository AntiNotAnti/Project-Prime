using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed record MapViewportMaterial(string Key, int Width, int Height, ColorRgba[] Pixels,
    int CoordinateWidth = 0, int CoordinateHeight = 0, Render.Materials.ResolvedMaterial? Enhanced = null);

/// <summary>Decoded once on material invalidation; frames and selection reuse the same table.</summary>
public static class MapViewportMaterials
{
    public static MapViewportMaterial Checker { get; } = CreateChecker();
    private static MapViewportMaterial CreateChecker()
    {
        var pixels = Enumerable.Range(0,4096).Select(i => new ColorRgba((ushort)(((i % 64 / 16 + i / 64 / 16) % 2 == 0) ? 32767 : 8456))).ToArray();
        string[] glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111" };
        for (int cell = 0; cell < 16; cell++)
        {
            string label = (cell + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            int originX = cell % 4 * 16 + 4, originY = cell / 4 * 16 + 5;
            for (int digit = 0; digit < label.Length; digit++)
                for (int y = 0; y < 5; y++) for (int x = 0; x < 3; x++)
                    if (glyphs[label[digit] - '0'][y*3+x] == '1')
                        pixels[(originY+y)*64+originX+digit*4+x] = new ColorRgba((ushort)31);
        }
        return new("editor-uv-checker-v2",64,64,pixels);
    }
    public static IReadOnlyDictionary<(bool Imported, int Index), MapViewportMaterial> Resolve(MapDefinition definition)
    {
        var result = new Dictionary<(bool, int), MapViewportMaterial>();
        MapViewportMaterial Packed(MapTexturePack.Entry entry, string key) => new(key, entry.Width, entry.Height,
            entry.Pixels.Select(p => new ColorRgba(entry.Palette[p])).ToArray());
        int offset = 0;
        if (definition.Import != null)
        {
            try
            {
                var pack = definition.Import.LoadTexturePack();
                if (pack != null)
                {
                    offset = pack.Entries.Count;
                    for (int i = 0; i < pack.Entries.Count; i++)
                    {
                        var entry = pack.Entries[i];
                        // Key the decoded result by its actual content, not its mutable source path.
                        byte[] data = entry.Pixels.Concat(entry.Palette.SelectMany(BitConverter.GetBytes)).ToArray();
                        result[(true, i)] = Packed(entry, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) + $"/{entry.Width}/{entry.Height}");
                    }
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or ProgramException or ArgumentException) { }
        }
        Model? nativeModel = null;
        Model NativeModel() => nativeModel ??= Read.GetRoomModelForExport(definition.TextureSource);
        for (int i = 0; i < definition.Materials.Count; i++)
        {
            var material = definition.Materials[i];
            try
            {
                MapViewportMaterial texture;
                Render.Materials.ResolvedMaterial? sourceEnhanced = null;
                if (material.Texture is { } path)
                {
                    byte[] bytes = MapAssets.Read(definition, path);
                    texture = Packed(MapTexturePack.Load(bytes, path).Entries.Single(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
                }
                else
                {
                    var model = NativeModel();
                    if (material.SourceMaterial < 0 || material.SourceMaterial >= model.Materials.Count) continue;
                    var source = model.Materials[material.SourceMaterial];
                    if (source.TextureId < 0 || source.PaletteId < 0)
                    {
                        int red=Math.Max((int)source.Diffuse.Red,source.Ambient.Red);
                        int green=Math.Max((int)source.Diffuse.Green,source.Ambient.Green);
                        int blue=Math.Max((int)source.Diffuse.Blue,source.Ambient.Blue);
                        var pixel=new ColorRgba((byte)(red*255/31),(byte)(green*255/31),(byte)(blue*255/31),
                            (byte)(source.Alpha*255/31));
                        texture=new($"native-flat/{definition.TextureSource}/{material.SourceMaterial}/{red}/{green}/{blue}/{source.Alpha}",
                            1,1,new[]{pixel});
                    }
                    else
                    {
                        var recolor = model.Recolors[0]; var entry = recolor.Textures[source.TextureId];
                        texture = new($"native/{definition.TextureSource}/{material.SourceMaterial}", entry.Width, entry.Height,
                            recolor.GetPixels(source.TextureId, source.PaletteId).ToArray());
                        sourceEnhanced = Render.TextureReplacementPack.Resolve(model.Name, source.TextureId, source.PaletteId, 0,
                            Render.Materials.MaterialAssetKey.ForModel(model, source.TextureId, source.PaletteId, 0));
                    }
                }
                if (definition.MapId != Guid.Empty && material.Id != Guid.Empty)
                {
                    var key = Render.Materials.MaterialAssetKey.Authored(definition.MapId, material.Id);
                    var local = Render.TextureReplacementPack.ResolveLocalExplicit(key);
                    var packaged = Render.Materials.MapMaterialAssetRegistry.Resolve(definition, key);
                    var enhanced = local ?? packaged ?? sourceEnhanced;
                    if (enhanced?.Albedo is { } replacement)
                    {
                        try
                        {
                            byte[] rgba = Render.TextureReplacementPack.ReadRgba(replacement, out int width, out int height);
                            var pixels = new ColorRgba[rgba.Length / 4];
                            for (int pixel = 0; pixel < pixels.Length; pixel++)
                                pixels[pixel] = new(rgba[pixel*4], rgba[pixel*4+1], rgba[pixel*4+2], rgba[pixel*4+3]);
                            texture = new("material-pack/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rgba)) + $"/{width}/{height}",
                                width, height, pixels, texture.Width, texture.Height);
                        }
                        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException
                            or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                        { /* Preserve the source if a replacement disappears while editing. */ }
                    }
                    if (enhanced != null && (enhanced.Albedo != null || enhanced.Normal != null
                        || enhanced.SpecularRoughness != null || enhanced.Emissive != null))
                        texture = texture with { Enhanced = enhanced,
                            Key = texture.Key + "/material/" + enhanced.Key.Value + "/pack/" + Render.TextureReplacementPack.Revision };
                }
                result[(false, i)] = texture; result[(true, i + offset)] = texture;
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or ProgramException or ArgumentException or InvalidOperationException) { }
        }
        return result;
    }
}
