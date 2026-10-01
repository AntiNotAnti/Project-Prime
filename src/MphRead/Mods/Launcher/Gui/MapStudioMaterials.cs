using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
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
            var key = MaterialAssetKey.Model(model.Name, original.TextureId, original.PaletteId, 0);
            var texture = model.Recolors[0].Textures[original.TextureId];
            MaterialInventory.Observe(key, texture.Width, texture.Height, model.Name);
            panel.Children.Add(Text("LOCAL MATERIAL PACK · " + key.Value));
            panel.Children.Add(Text("Local source-model override. Generated maps may use a different runtime key. Channel previews show the assigned image."));
            var resolved = TextureReplacementPack.Resolve(model.Name, original.TextureId, original.PaletteId, 0);
            foreach (var channel in new[] { ("albedo", resolved?.Albedo), ("normal", resolved?.Normal),
                ("specularRoughness", resolved?.SpecularRoughness), ("emissive", resolved?.Emissive) })
            {
                string kind = channel.Item1;
                panel.Children.Add(Text(kind + ": " + (channel.Item2 is { } map ? $"{map.Width} × {map.Height} · {Path.GetFileName(map.Path)}" : "Original / absent")));
                void Assign(string? source)
                {
                    try
                    {
                        MaterialPackAuthoring.Assign(TextureReplacementPack.Root, key, kind, source);
                        TextureReplacementPack.Reload();
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
                        preview = (new Bitmap(image.Path), kind); _materialPreviewCache[cacheKey] = preview;
                    }
                    panel.Children.Add(new Image { Source = preview.Bitmap, Width = 96, Height = 96 });
                }
            }
            AddButton(panel, "Refresh material pack", () => { TextureReplacementPack.Reload(); _viewport?.RefreshMaterialPreview(); MaterialInspector(); });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or ProgramException)
        { panel.Children.Add(Text("Enhanced material unavailable: " + ex.Message)); }
    }
}
