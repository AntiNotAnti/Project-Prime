using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private void ClearEnhancedPreviews()
    {
        // Release edited thumbnails promptly instead of retaining every prior assignment
        // until the document closes. These are downsampled presentation resources.
        foreach (string key in _materialPreviewCache.Keys.Where(k => k.StartsWith("material-pack/", StringComparison.Ordinal)).ToArray())
        {
            _materialPreviewCache[key].Bitmap.Dispose();
            _materialPreviewCache.Remove(key);
        }
    }

    private void EnhancedMaterialControls(StackPanel panel, MapDefinition definition, MapMaterial material)
    {
        if (definition.MapId == Guid.Empty || material.Id == Guid.Empty) return;
        try
        {
            var key = MaterialAssetKey.Authored(definition.MapId, material.Id);
            int width, height;
            if (material.Texture is { } path)
            {
                var texture = MapTexturePack.Load(MapAssets.Read(definition, path), path).Entries.Single();
                width = texture.Width; height = texture.Height;
            }
            else
            {
                if (!GameFiles.Ready) return;
                var model = Read.GetRoomModelForExport(definition.TextureSource);
                if (material.SourceMaterial < 0 || material.SourceMaterial >= model.Materials.Count) return;
                var original = model.Materials[material.SourceMaterial];
                if (original.TextureId < 0 || original.PaletteId < 0) return;
                var texture = model.Recolors[0].Textures[original.TextureId];
                width = texture.Width; height = texture.Height;
            }
            MaterialInventory.Observe(key, width, height, definition.Name, definition.Name);
            panel.Children.Add(Text("LOCAL MATERIAL PACK · " + key.Value));
            panel.Children.Add(Text("This map/material identity follows the authored surface into compiled and community maps. Assignments stay local; sharing a map does not include this material pack."));
            var resolved = TextureReplacementPack.ResolveExplicit(key);
            foreach (var channel in new[] { ("albedo", resolved?.Albedo), ("normal", resolved?.Normal),
                ("specularRoughness", resolved?.SpecularRoughness), ("emissive", resolved?.Emissive) })
            {
                string kind = channel.Item1;
                string semantics = kind switch
                {
                    "normal" => "tangent-space XYZ",
                    "specularRoughness" => "red = specular; green = roughness",
                    "emissive" => "RGB emission",
                    _ => "RGBA base color"
                };
                panel.Children.Add(Text(semantics));
                panel.Children.Add(Text(kind + ": " + (channel.Item2 is { } map ? $"{map.Width} × {map.Height} · {Path.GetFileName(map.Path)}" : "Original / absent")));
                void Assign(string? source)
                {
                    try
                    {
                        MaterialPackAuthoring.Assign(TextureReplacementPack.Root, key, kind, source);
                        TextureReplacementPack.Reload();
                        ClearEnhancedPreviews();
                        _viewport?.RefreshMaterialPreview();
                        MaterialInspector();
                        _status.Text = "Local material pack saved. Texture replacements must be enabled in graphics settings.";
                    }
                    catch (Exception ex) { Failure(ex); }
                }
                AddButton(panel, "Browse " + kind, () => Browse("Choose " + kind + " PNG", false, path => Assign(path), ".png"));
                AddButton(panel, "Clear " + kind, () => Assign(null));
                if (channel.Item2 is { } image)
                {
                    string cacheKey = "material-pack/" + image.Path + "/" + File.GetLastWriteTimeUtc(image.Path).Ticks;
                    if (!_materialPreviewCache.TryGetValue(cacheKey, out var preview))
                    {
                        MaterialPack.ContainedPath(TextureReplacementPack.Root,
                            Path.GetRelativePath(TextureReplacementPack.Root, image.Path).Replace(Path.DirectorySeparatorChar, '/'));
                        MaterialPack.ValidateImage(image.Path);
                        using var stream = File.OpenRead(image.Path);
                        preview = (image.Width >= image.Height
                            ? Bitmap.DecodeToWidth(stream, Math.Min(image.Width, 96))
                            : Bitmap.DecodeToHeight(stream, Math.Min(image.Height, 96)), kind); _materialPreviewCache[cacheKey] = preview;
                    }
                    panel.Children.Add(new Image { Source = preview.Bitmap, Width = 96, Height = 96 });
                }
            }
            AddButton(panel, "Refresh material pack", () => { TextureReplacementPack.Reload(); ClearEnhancedPreviews(); _viewport?.RefreshMaterialPreview(); MaterialInspector(); });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or ProgramException)
        { panel.Children.Add(Text("Enhanced material unavailable: " + ex.Message)); }
    }
}
