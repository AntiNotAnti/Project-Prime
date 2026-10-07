using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
                if (!_services.GameFilesReady) return;
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
                    () => Browse("Choose packaged " + kind, false, p => AssignPackaged(p), ".png", ".jpg", ".jpeg", ".tga", ".ktx2"));
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
                    () => Browse("Choose local " + kind + " image", false, p => AssignLocal(p), ".png", ".ktx2"));
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


    private sealed class MaterialAnimationPreviewHandle
    {
        internal DispatcherTimer Timer { get; }
        internal Bitmap[] Frames { get; }
        internal MaterialAnimationPreviewHandle(DispatcherTimer timer, Bitmap[] frames)
        { Timer = timer; Frames = frames; }
    }

    private readonly System.Collections.Generic.List<MaterialAnimationPreviewHandle> _materialAnimationPreviews = new();

    private void ClearAnimatedMaterialPreviews()
    {
        foreach (MaterialAnimationPreviewHandle preview in _materialAnimationPreviews)
        {
            preview.Timer.Stop();
            foreach (Bitmap frame in preview.Frames) frame.Dispose();
        }
        _materialAnimationPreviews.Clear();
    }

    private void AddAnimatedMaterialPreview(StackPanel panel, MapDefinition definition, MapMaterial material)
    {
        if (material.Animation == null) return;
        const int frameCount = 20;
        const int nativeFramesPerPreviewStep = 3;
        var frames = new Bitmap[frameCount];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = MapMaterialPreview.Create(definition, material, i * nativeFramesPerPreviewStep).Bitmap;
        var image = new Image { Source = frames[0], Width = 96, Height = 96, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        panel.Children.Add(Text("LIVE MATERIAL PREVIEW"));
        panel.Children.Add(image);
        var pulseReadout = Text("");
        panel.Children.Add(pulseReadout);
        int frame = 0;
        void RefreshReadout()
        {
            if (material.Animation is { } animation
                && (animation.EmissiveIntensity != 1f || animation.EmissivePulse != 0f))
            {
                int nativeFrame = frame * nativeFramesPerPreviewStep;
                pulseReadout.Text = $"Emissive intensity · {MapUvAnimation.EmissiveIntensity(animation, nativeFrame):0.00}";
            }
            else pulseReadout.Text = "";
        }
        RefreshReadout();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            frame = (frame + 1) % frames.Length;
            image.Source = frames[frame];
            RefreshReadout();
        };
        timer.Start();
        _materialAnimationPreviews.Add(new MaterialAnimationPreviewHandle(timer, frames));
    }

    private void AnimatedMaterialControls(StackPanel panel, MapDefinition definition, MapMaterial material, int materialIndex)
    {
        panel.Children.Add(Text("ANIMATED MATERIAL"));
        panel.Children.Add(Text("Scroll is measured in texture tiles per second. Rotation uses degrees per second. Scale pulse oscillates around the base scale once per loop. Scroll and rotation endpoints must wrap seamlessly in the native MPH animation cycle."));

        var enabled = new CheckBox { Content = "Enable material animation", IsChecked = material.Animation != null };
        var twoSided = new CheckBox { Content = "Two-sided surface", IsChecked = material.TwoSided };
        var alpha = new TextBox { Text = material.Alpha?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", PlaceholderText = "Alpha 0–31 · blank = inherit/default" };

        float[] authoredScroll = material.Animation?.UvScroll is { Length: 2 } scrollValues ? scrollValues : new float[2];
        float[] authoredScale = material.Animation?.UvScale is { Length: 2 } scaleValues ? scaleValues : new[] { 1f, 1f };
        float[] authoredPulse = material.Animation?.UvScalePulse is { Length: 2 } pulseValues ? pulseValues : new float[2];

        var scrollX = new TextBox { Text = authoredScroll[0].ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Horizontal tiles/sec" };
        var scrollY = new TextBox { Text = authoredScroll[1].ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Vertical tiles/sec" };
        var rotation = new TextBox { Text = (material.Animation?.UvRotationDegreesPerSecond ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Rotation degrees/sec" };
        var scaleX = new TextBox { Text = authoredScale[0].ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Base scale X" };
        var scaleY = new TextBox { Text = authoredScale[1].ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Base scale Y" };
        var pulseX = new TextBox { Text = authoredPulse[0].ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Scale pulse X" };
        var pulseY = new TextBox { Text = authoredPulse[1].ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Scale pulse Y" };
        var emissiveIntensity = new TextBox { Text = (material.Animation?.EmissiveIntensity ?? 1f).ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Emissive intensity 0–1" };
        var emissivePulse = new TextBox { Text = (material.Animation?.EmissivePulse ?? 0f).ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Emissive pulse amplitude" };
        var loop = new TextBox { Text = (material.Animation?.LoopFrames ?? 3000).ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Loop frames · 30–6000" };
        var phase = new TextBox { Text = (material.Animation?.PhaseFrames ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Phase frames" };
        var hold = new TextBox { Text = (material.Animation?.FlipbookHoldFrames ?? 3).ToString(System.Globalization.CultureInfo.InvariantCulture), PlaceholderText = "Flipbook hold frames" };
        var flipbookFrames = material.Animation?.FlipbookFrames?.ToList() ?? new System.Collections.Generic.List<string>();
        var declaredAssets = definition.Assets ?? new System.Collections.Generic.List<MapAsset>();
        string[] nativeTextureAssets = declaredAssets
            .Where(asset => asset?.Kind == "texture" && asset.Path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset!.Path).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray();
        var framePicker = new ComboBox { ItemsSource = nativeTextureAssets, SelectedIndex = nativeTextureAssets.Length > 0 ? 0 : -1 };
        var frameSummary = Text(FlipbookSummary());

        string FlipbookSummary()
        {
            if (flipbookFrames.Count == 0) return "Flipbook: off · base texture only";
            return "Flipbook: base + " + flipbookFrames.Count + " frame(s) · "
                + String.Join(" → ", flipbookFrames.Select(System.IO.Path.GetFileName));
        }

        panel.Children.Add(enabled);
        panel.Children.Add(twoSided);
        panel.Children.Add(alpha);
        panel.Children.Add(Text("Horizontal / vertical scroll"));
        panel.Children.Add(scrollX);
        panel.Children.Add(scrollY);
        panel.Children.Add(Text("Rotation"));
        panel.Children.Add(rotation);
        panel.Children.Add(Text("Base UV scale X / Y"));
        panel.Children.Add(scaleX);
        panel.Children.Add(scaleY);
        panel.Children.Add(Text("Scale pulse amplitude X / Y"));
        panel.Children.Add(pulseX);
        panel.Children.Add(pulseY);
        panel.Children.Add(Text("Emissive intensity / pulse"));
        panel.Children.Add(emissiveIntensity);
        panel.Children.Add(emissivePulse);
        panel.Children.Add(Text("Loop / phase · native 30 Hz frames"));
        panel.Children.Add(loop);
        panel.Children.Add(phase);
        panel.Children.Add(Text("Flipbook frames · base texture is frame zero"));
        panel.Children.Add(framePicker);
        panel.Children.Add(hold);
        panel.Children.Add(frameSummary);
        AddButton(panel, "Add selected flipbook frame", () =>
        {
            if (framePicker.SelectedItem is not string frame) return;
            if (flipbookFrames.Count >= MapUvAnimation.MaxFlipbookImages)
            {
                _status.Text = "Flipbook already has the maximum 64 additional frames.";
                return;
            }
            flipbookFrames.Add(frame);
            enabled.IsChecked = true;
            frameSummary.Text = FlipbookSummary();
        });
        AddButton(panel, "Remove last flipbook frame", () =>
        {
            if (flipbookFrames.Count == 0) return;
            flipbookFrames.RemoveAt(flipbookFrames.Count - 1);
            frameSummary.Text = FlipbookSummary();
        });
        AddButton(panel, "Clear flipbook", () =>
        {
            flipbookFrames.Clear();
            frameSummary.Text = FlipbookSummary();
        });

        string[] presets =
        {
            "Still Water", "Slow Water", "Waterfall", "Lava", "Energy Flow",
            "Conveyor", "Portal Spin", "Hologram Pulse"
        };
        var preset = new ComboBox { ItemsSource = presets, SelectedIndex = 0 };
        panel.Children.Add(Text("Preset"));
        panel.Children.Add(preset);
        AddButton(panel, "Load preset", () =>
        {
            float x=0,y=0,turn=0,sx=1,sy=1,px=0,py=0,emit=1,emitPulse=0;
            bool sides=false;int? opacity=31;
            switch(preset.SelectedItem as string)
            {
                case "Slow Water": x=0.03f;y=0.01f;sides=true;opacity=26;break;
                case "Waterfall": y=-0.8f;sides=true;opacity=22;break;
                case "Lava": x=0.05f;y=0.02f;opacity=31;break;
                case "Energy Flow": x=0.6f;sides=true;opacity=24;break;
                case "Conveyor": x=1f;opacity=31;break;
                case "Portal Spin": turn=180f;sides=true;opacity=26;break;
                case "Hologram Pulse": px=0.08f;py=0.08f;emit=0.55f;emitPulse=0.40f;sides=true;opacity=23;break;
                default: sides=true;opacity=26;break;
            }
            enabled.IsChecked = true;
            scrollX.Text = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
            scrollY.Text = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            rotation.Text = turn.ToString(System.Globalization.CultureInfo.InvariantCulture);
            scaleX.Text = sx.ToString(System.Globalization.CultureInfo.InvariantCulture);
            scaleY.Text = sy.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pulseX.Text = px.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pulseY.Text = py.ToString(System.Globalization.CultureInfo.InvariantCulture);
            emissiveIntensity.Text = emit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            emissivePulse.Text = emitPulse.ToString(System.Globalization.CultureInfo.InvariantCulture);
            loop.Text = "3000";
            phase.Text = "0";
            twoSided.IsChecked = sides;
            alpha.Text = opacity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        });

        AddButton(panel, "Apply animation & surface", () =>
        {
            if (_document == null) return;
            try
            {
                int? opacity = String.IsNullOrWhiteSpace(alpha.Text) ? null
                    : int.Parse(alpha.Text!, System.Globalization.CultureInfo.InvariantCulture);
                MapMaterialAnimation? animation = null;
                if (enabled.IsChecked == true)
                {
                    animation = new MapMaterialAnimation
                    {
                        UvScroll = new[] { Number(scrollX.Text ?? "0"), Number(scrollY.Text ?? "0") },
                        UvRotationDegreesPerSecond = Number(rotation.Text ?? "0"),
                        UvScale = new[] { Number(scaleX.Text ?? "1"), Number(scaleY.Text ?? "1") },
                        UvScalePulse = new[] { Number(pulseX.Text ?? "0"), Number(pulseY.Text ?? "0") },
                        EmissiveIntensity = Number(emissiveIntensity.Text ?? "1"),
                        EmissivePulse = Number(emissivePulse.Text ?? "0"),
                        FlipbookFrames = flipbookFrames.ToList(),
                        FlipbookHoldFrames = int.Parse(hold.Text ?? "", System.Globalization.CultureInfo.InvariantCulture),
                        LoopFrames = int.Parse(loop.Text ?? "", System.Globalization.CultureInfo.InvariantCulture),
                        PhaseFrames = int.Parse(phase.Text ?? "", System.Globalization.CultureInfo.InvariantCulture)
                    };
                }
                _document.EditMaterial(materialIndex, value =>
                {
                    value.Alpha = opacity;
                    value.TwoSided = twoSided.IsChecked == true;
                    value.Animation = animation;
                });
                ClearEnhancedPreviews();
                _viewport?.RefreshMaterialPreview();
                _status.Text = animation == null ? "Material animation disabled." : "Animated material settings applied.";
                _ = Validate();
                MaterialInspector();
            }
            catch (Exception ex) { Failure(ex); }
        });

        AddAnimatedMaterialPreview(panel, definition, material);
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
