using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// Runs in material-pack's real Windows GL context, with invented cartridge
// pixels. The assertions observe actual Scene jobs, GPU dimensions and mips.
internal static class TextureSettingsGpuCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(string packRoot)
    {
        var upscale = RenderOptions.TextureUpscale; var quality = RenderOptions.TextureQuality;
        bool replacements = RenderOptions.TextureReplacements, filtering = RenderOptions.TextureFiltering, mips = RenderOptions.TextureMipmaps;
        bool advanced = RenderOptions.AdvancedMaterials;
        string manifest = Path.Combine(packRoot, "materials.json");
        byte[] manifestBytes = File.ReadAllBytes(manifest);
        Scene? scene = null;
        try
        {
            RenderOptions.TextureUpscale = TextureUpscaleMode.Off; RenderOptions.TextureQuality = TextureAssetQuality.Low;
            RenderOptions.TextureReplacements = false; RenderOptions.TextureFiltering = RenderOptions.TextureMipmaps = false;
            scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            RawMaterial Raw(int id)
            {
                object raw = default(RawMaterial);
                foreach (var (name, value) in new[] { ("TextureId", id), ("PaletteId", 0), ("Alpha", 31) })
                {
                    FieldInfo field = typeof(RawMaterial).GetField(name)!;
                    field.SetValue(raw, Convert.ChangeType(value, field.FieldType));
                }
                byte[] nameBytes = new byte[64]; System.Text.Encoding.ASCII.GetBytes("settings-" + id).CopyTo(nameBytes, 0);
                typeof(RawMaterial).GetField("Name")!.SetValue(raw, nameBytes);
                return (RawMaterial)raw;
            }
            var recolor = new Recolor("settings", Enumerable.Range(0, 6).Select(_ => new Texture(TextureFormat.Palette8Bit, 2, 2)).ToArray(),
                new[] { default(Palette) }, Enumerable.Range(0, 6).Select(_ => (IReadOnlyList<TextureData>)new[] {
                    new TextureData(0, 255), new TextureData(0, 255), new TextureData(0, 255), new TextureData(0, 255) }).ToArray(),
                new IReadOnlyList<PaletteData>[] { new[] { new PaletteData(32767) } });
            var model = new Model("settings-jobs-fixture", false, default, Array.Empty<RawNode>(), Array.Empty<RawMesh>(),
                Enumerable.Range(0, 6).Select(Raw).ToArray(), Array.Empty<DisplayList>(), Array.Empty<IReadOnlyList<RenderInstruction>>(),
                new AnimationResults(), Array.Empty<Matrix4>(), new[] { recolor }, Array.Empty<int>(),
                Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>());
            typeof(Scene).GetMethod("InitTextures", Hidden)!.Invoke(scene, new object[] { model });
            scene.UpdateMaterials(model, 0);
            int[] bindings = model.Materials.Select(material => material.TextureBindingId).ToArray();
            void Check(bool value, string message) { if (!value) throw new Exception(message); }
            int Pending() => (int)typeof(Scene).GetProperty("PendingTextureSettingsJobs", Hidden)!.GetValue(scene)!;
            int Generation(string kind) => (int)typeof(Scene).GetProperty(kind + "TextureSettingsGeneration", Hidden)!.GetValue(scene)!;
            long Completed() => (long)typeof(Scene).GetProperty("TextureSettingsJobsCompleted", Hidden)!.GetValue(scene)!;
            void Refresh()
            {
                long before = Completed();
                typeof(Scene).GetMethod("RefreshTextureQuality", Hidden)!.Invoke(scene, null);
                Check(Completed() - before <= 1, "settings change performed more than one binding job in a picture");
            }
            int Width(int binding, int level = 0)
            {
                GL.BindTexture(TextureTarget.Texture2D, binding);
                GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureWidth, out int width);
                return width;
            }
            Check(bindings.Distinct().Count() == 6 && bindings.All(binding => Width(binding) == 2), "invented source textures initialized independently");
            RenderOptions.TextureUpscale = TextureUpscaleMode.Scale2x; RenderOptions.TextureQuality = TextureAssetQuality.High;
            Refresh();
            Check(Pending() == 5 && bindings.Count(binding => Width(binding) == 4) == 1,
                "first desired quality frame must reupload exactly one of six resident GPU textures");
            Check((TextureAssetQuality)typeof(Scene).GetProperty("AppliedTextureAssetQuality", Hidden)!.GetValue(scene)! == TextureAssetQuality.Low,
                "applied quality must not claim completion while five jobs remain");
            Check(Generation("Desired") > Generation("Applied"), "observable desired generation must lead applied work");
            RenderOptions.TextureUpscale = TextureUpscaleMode.Scale4x;
            Refresh();
            Check(Pending() == 5 && bindings.Count(binding => Width(binding) == 8) == 1,
                "rapid desired change must coalesce pending jobs instead of replaying obsolete uploads");
            int released = bindings[1];
            typeof(Scene).GetMethod("ReleaseTexture", Hidden)!.Invoke(scene, new object[] { released });
            int[] live = bindings.Where(binding => binding != released).ToArray();
            for (int i = 0; Pending() > 0 && i < 10; i++) Refresh();
            Check(Pending() == 0 && live.All(binding => Width(binding) == 8) && !GL.IsTexture(released),
                "stale released binding must retire without GL-name resurrection; live handles converge to latest quality");
            Check((TextureAssetQuality)typeof(Scene).GetProperty("AppliedTextureAssetQuality", Hidden)!.GetValue(scene)! == TextureAssetQuality.High,
                "applied quality changes after all jobs complete");
            Check(Generation("Desired") == Generation("Applied")
                && (long)typeof(Scene).GetProperty("TextureSettingsUploadBytes", Hidden)!.GetValue(scene)! > 0
                && double.IsFinite((double)typeof(Scene).GetProperty("WorstTextureSettingsJobMs", Hidden)!.GetValue(scene)!),
                "completed generation must publish finite time and positive reupload byte accounting");
            RenderOptions.TextureFiltering = RenderOptions.TextureMipmaps = true;
            Refresh();
            Check(live.Count(binding => Width(binding, 1) > 0) == 1,
                "mip settings prewarm only the current binding job");
            MethodInfo direct = typeof(Scene).GetMethod("ApplyBoundTextureSampling", Hidden)!;
            MethodInfo retained = typeof(Scene).GetMethod("RetainedTextureBinding", Hidden)!;
            foreach (int binding in live)
            {
                bool residentMips = Width(binding, 1) > 0;
                object recipe = retained.Invoke(scene, new object[] { binding, true })!;
                object sampler = recipe.GetType().GetProperty("Sampling")!.GetValue(recipe)!;
                bool recipeMips = (bool)sampler.GetType().GetProperty("Mipmaps")!.GetValue(sampler)!;
                Check(recipeMips == residentMips, "retained path thawed sampler before its mip job completed");
                GL.BindTexture(TextureTarget.Texture2D, binding);
                direct.Invoke(scene, new object[] { binding, RepeatMode.Repeat, RepeatMode.Repeat });
            }
            Check(live.Count(binding => Width(binding, 1) > 0) == 1,
                "drawing all still-pending textures must not turn one mip job into a same-frame burst");
            for (int i = 0; Pending() > 0 && i < 10; i++) Refresh();
            Check(live.All(binding => Width(binding, 1) > 0), "complete desired filtering eventually gives every live texture valid mips");

            // Reach a real partial promotion, then let the disabled policy
            // retire its outstanding channel work while another source's off
            // job runs. A quick re-enable must fill gaps without replacing the
            // still-resident normal handle or treating it as a complete set.
            RenderOptions.AdvancedMaterials = false;
            RenderOptions.TextureReplacements = true;
            MaterialInventory.Write(manifest, new MaterialPackManifest { Materials = new() { new() {
                Key = MaterialAssetKey.Model(model.Name, 5, 0, 0).Value, Albedo = "pixel.png",
                Normal = "pixel.png", SpecularRoughness = "pixel.png", Emissive = "pixel.png" } } });
            TextureReplacementPack.Reload();
            SourceBankGpuCheck.WaitForIdle(scene);
            var maps = (IDictionary<int, MaterialMapBindings>)typeof(Scene).GetField("_materialMaps", Hidden)!.GetValue(scene)!;
            int target = bindings[5];
            RenderOptions.AdvancedMaterials = true; Refresh();
            while (Pending() > 0) Refresh();
            var partialWatch = Stopwatch.StartNew();
            while ((!maps.TryGetValue(target, out var partial) || partial.Normal == 0)
                && partialWatch.Elapsed < TimeSpan.FromSeconds(10)) { Refresh(); System.Threading.Thread.Sleep(1); }
            Check(maps.TryGetValue(target, out var partialMaps) && partialMaps.Normal != 0
                && partialMaps.Specular == 0 && partialMaps.Emissive == 0, "fixture reached actual normal-only progressive companion residency");
            int residentNormal = partialMaps.Normal;
            RenderOptions.AdvancedMaterials = false; Refresh();
            Check(Pending() > 0 && maps[target].Normal == residentNormal, "off job for another binding leaves target's partial normal resident");
            MethodInfo pump = typeof(Scene).GetMethod("PumpModernTextureStreaming", Hidden)!;
            int StreamingCount(string field) => ((ICollection)typeof(Scene).GetField(field, Hidden)!.GetValue(scene)!).Count;
            partialWatch.Restart();
            while ((StreamingCount("_streamingTextureQueue") + StreamingCount("_streamingTextureDecodes")) > 0
                && partialWatch.Elapsed < TimeSpan.FromSeconds(10)) { pump.Invoke(scene, null); System.Threading.Thread.Sleep(1); }
            Check(StreamingCount("_streamingTextureQueue") + StreamingCount("_streamingTextureDecodes") == 0,
                "disabled advanced policy cancelled all missing companion requests/results");
            RenderOptions.AdvancedMaterials = true;
            SourceBankGpuCheck.WaitForIdle(scene);
            Check(maps.TryGetValue(target, out var completeMaps) && completeMaps.Normal == residentNormal
                && completeMaps.Specular != 0 && completeMaps.Emissive != 0 && GL.IsTexture(residentNormal),
                "rapid advanced off/on fills partial companion gaps and preserves existing ownership");
            RenderOptions.TextureUpscale = TextureUpscaleMode.Scale2x; Refresh();
            Check(Pending() > 0, "teardown fixture has pending desired work");
            scene.UnloadGl();
            Check(Pending() == 0 && live.All(binding => !GL.IsTexture(binding)), "teardown cancels queued settings work and owned GPU names");
            Refresh();
            Check(Pending() == 0 && GL.GetError() == ErrorCode.NoError, "cancelled generation cannot issue stale GL work after teardown");
            Console.WriteLine("TEXTURE SETTINGS GPU PASS: one job per picture, desired/applied lag, rapid supersession, stable handles, stale release, direct/retained mip freeze, bounded prewarm and teardown");
        }
        finally
        {
            scene?.UnloadGl(); GL.BindTexture(TextureTarget.Texture2D, 0);
            RenderOptions.TextureUpscale = upscale; RenderOptions.TextureQuality = quality; RenderOptions.TextureReplacements = replacements;
            RenderOptions.TextureFiltering = filtering; RenderOptions.TextureMipmaps = mips;
            RenderOptions.AdvancedMaterials = advanced;
            File.WriteAllBytes(manifest, manifestBytes); TextureReplacementPack.Reload();
        }
    }
}
