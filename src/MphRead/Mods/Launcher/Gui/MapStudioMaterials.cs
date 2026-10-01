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
        // Runtime native textures have an observed model/texture/palette identity.
        // Authored .tex identities need a package-level contract before overrides are exposed.
        if (material.Texture != null || !GameFiles.Ready) return;
        try
        {
            var model = Read.GetRoomModelForExport(definition.TextureSource);
            if (material.SourceMaterial < 0 || material.SourceMaterial >= model.Materials.Count) return;
            var original = model.Materials[material.SourceMaterial];
            if (original.TextureId < 0 || original.PaletteId < 0) return;
            var key = MaterialAssetKey.ForModel(model, original.TextureId, original.PaletteId, 0);
            var texture = model.Recolors[0].Textures[original.TextureId];
            MaterialInventory.Observe(key, texture.Width, texture.Height, model.Name);
            panel.Children.Add(Text("LOCAL MATERIAL PACK · " + key.Value));
            panel.Children.Add(Text("Local source-model override. Generated maps may use a different runtime key. Channel images show assignments; the viewport uses the shared material shader with studio lighting."));
            var resolved = TextureReplacementPack.Resolve(model.Name, original.TextureId, original.PaletteId, 0, key);
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
