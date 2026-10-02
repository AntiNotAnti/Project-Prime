using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MphRead.Mods.MapGen;
using MphRead.Mods.MapEditor;
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

            ResolvedMaterial? packaged = MapMaterialAssetRegistry.Resolve(definition, key);
            ResolvedMaterial? local = TextureReplacementPack.ResolveLocalExplicit(key);
            string[] channels = { "albedo", "normal", "specularRoughness", "emissive" };

            panel.Children.Add(Text("PACKAGED HD MATERIAL · " + key.Value));
            panel.Children.Add(Text("These images travel inside the editable project and Community .ppmap. The native .tex material remains the universal fallback; each client chooses its own 1K/2K/4K/8K residency tier."));
            foreach (string kind in channels)
            {
                MaterialImage? image = Image(packaged, kind);
                string? relative = PathFor(material, kind);
                panel.Children.Add(Text(Semantics(kind)));
                panel.Children.Add(Text(kind + ": " + (image != null
                    ? $"{image.Width} × {image.Height} · {relative}"
                    : relative ?? "Original / absent")));

                void AssignPackaged(string? source)
                {
                    if (_document == null) return;
                    try
                    {
                        string? generated = null;
                        _document.Edit((source == null ? "Clear " : "Assign ") + "packaged " + kind, d =>
                        {
                            generated = MapMaterialAssetAuthoring.Assign(d, material.Id, kind, source);
                        }, MapChangeDomain.Material);
                        if (generated != null && _document.Project.Definition.BaseDirectory is { } root)
                            _document.RegisterGeneratedAsset(generated, root);
                        TextureReplacementPack.Reload();
                        ClearEnhancedPreviews();
                        _viewport?.RefreshMaterialPreview();
                        MaterialInspector();
                        _status.Text = source == null
                            ? "Packaged HD channel cleared; native fallback retained."
                            : "Packaged HD channel saved. It will travel with the Community map.";
                    }
                    catch (Exception ex) { Failure(ex); }
                }

                AddButton(panel, "Package " + kind + "…",
                    () => Browse("Choose packaged " + kind, false, p => AssignPackaged(p), ".png", ".jpg", ".jpeg"));
                if (relative != null) AddButton(panel, "Clear packaged " + kind, () => AssignPackaged(null));
                if (image != null) AddMaterialPreview(panel, image, "packaged-" + kind);
            }

            panel.Children.Add(Text("LOCAL OVERRIDE PACK"));
            panel.Children.Add(Text("Optional machine-local overrides take priority over the packaged channels and are never uploaded with a map."));
            foreach (string kind in channels)
            {
                MaterialImage? image = Image(local, kind);
                panel.Children.Add(Text(kind + ": " + (image is { } map
                    ? $"{map.Width} × {map.Height} · {Path.GetFileName(map.Path)}"
                    : "None")));

                void AssignLocal(string? source)
                {
                    try
                    {
                        MaterialPackAuthoring.Assign(TextureReplacementPack.Root, key, kind, source);
                        TextureReplacementPack.Reload();
                        ClearEnhancedPreviews();
                        _viewport?.RefreshMaterialPreview();
                        MaterialInspector();
                        _status.Text = "Local material override saved. HD texture replacements must be enabled in graphics settings.";
                    }
                    catch (Exception ex) { Failure(ex); }
                }

                AddButton(panel, "Browse local " + kind,
                    () => Browse("Choose local " + kind + " PNG", false, p => AssignLocal(p), ".png"));
                if (image != null) AddButton(panel, "Clear local " + kind, () => AssignLocal(null));
                if (image != null) AddMaterialPreview(panel, image, "local-" + kind);
            }
            AddButton(panel, "Refresh material sources", () =>
            {
                TextureReplacementPack.Reload(); ClearEnhancedPreviews();
                _viewport?.RefreshMaterialPreview(); MaterialInspector();
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or ProgramException)
        { panel.Children.Add(Text("Enhanced material unavailable: " + ex.Message)); }
    }

    private void AddMaterialPreview(StackPanel panel, MaterialImage image, string kind)
    {
        long stamp = image.IsFileBacked && File.Exists(image.Path) ? File.GetLastWriteTimeUtc(image.Path).Ticks : 0;
        string cacheKey = "material-pack/" + kind + "/" + image.Path + "/" + stamp;
        if (!_materialPreviewCache.TryGetValue(cacheKey, out var preview))
        {
            using var stream = image.OpenRead();
            preview = (image.Width >= image.Height
                ? Bitmap.DecodeToWidth(stream, Math.Min(image.Width, 96))
                : Bitmap.DecodeToHeight(stream, Math.Min(image.Height, 96)), kind);
            _materialPreviewCache[cacheKey] = preview;
        }
        panel.Children.Add(new Image { Source = preview.Bitmap, Width = 96, Height = 96 });
    }

    private static MaterialImage? Image(ResolvedMaterial? material, string channel) => channel switch
    {
        "albedo" => material?.Albedo,
        "normal" => material?.Normal,
        "specularRoughness" => material?.SpecularRoughness,
        "emissive" => material?.Emissive,
        _ => null
    };

    private static string? PathFor(MapMaterial material, string channel) => channel switch
    {
        "albedo" => material.Albedo,
        "normal" => material.Normal,
        "specularRoughness" => material.SpecularRoughness,
        "emissive" => material.Emissive,
        _ => null
    };

    private static string Semantics(string channel) => channel switch
    {
        "normal" => "tangent-space XYZ",
        "specularRoughness" => "red = specular; green = roughness",
        "emissive" => "RGB emission",
        _ => "RGBA base color"
    };

}
