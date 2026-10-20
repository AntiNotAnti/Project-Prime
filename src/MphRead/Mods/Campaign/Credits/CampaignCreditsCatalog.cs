using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;
using TextureKey = MphRead.Mods.Campaign.CampaignCreditsWidgetSampler.TextureKey;

namespace MphRead.Mods.Campaign;

/// <summary>Immutable inventory and aggregate admission. No Model, file, texture decode or UI ownership.</summary>
internal sealed class CampaignCreditsCatalog
{
    internal const int MaxModels = 8, MaxSamplers = 32, MaxWidgets = 64, MaxLayouts = 256,
        MaxSourceBytes = 16 * 1024 * 1024, MaxSamplerVertices = 262144, MaxSamplerTriangles = 262144,
        MaxTextureRgbaBytes = 8 * 1024 * 1024, MaxTextCharacters = 65536, MaxSamplerPixels = 2 * 1024 * 1024;
    internal sealed record Backgrounds(CampaignCreditsBackgroundSource.Snapshot GoodTop,
        CampaignCreditsBackgroundSource.Snapshot GoodBottom, CampaignCreditsBackgroundSource.Snapshot BadTop,
        CampaignCreditsBackgroundSource.Snapshot BadBottom);
    internal sealed record WidgetBinding(CampaignCreditsModelSource.Snapshot Model,
        CampaignCreditsAnimationBinding.Snapshot? Animation);
    internal readonly record struct TexturePair(TextureKey Key, int Width, int Height);
    internal sealed record AdmissionBudget(int SourceBytes, int SamplerCount, int SamplerVertices,
        int SamplerTriangles, int TextureRgbaBytes, int TextLayouts, int TextCharacters)
    {
        internal int SamplerPixels { get; init; }
    }
    internal CampaignCreditsSource.Document Document { get; }
    internal CampaignCreditsFrameTable Frames { get; }
    internal CampaignCreditsFontSource.Snapshot Font { get; }
    internal Backgrounds Images { get; }
    internal IReadOnlyDictionary<int, WidgetBinding> Widgets { get; }
    internal IReadOnlyDictionary<string, CampaignCreditsModelSource.Snapshot> Models { get; }
    internal IReadOnlyList<TexturePair> TexturePairs { get; }
    internal AdmissionBudget Budget { get; }

    internal CampaignCreditsCatalog(CampaignCreditsSource.Document document,
        IReadOnlyDictionary<string, CampaignCreditsModelSource.Snapshot> models,
        IReadOnlyDictionary<string, CampaignCreditsAnimationBinding.Snapshot> animations,
        CampaignCreditsFontSource.Snapshot font, Backgrounds backgrounds)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(animations); ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(backgrounds);
        Require(backgrounds.GoodTop != null && backgrounds.GoodBottom != null
            && backgrounds.BadTop != null && backgrounds.BadBottom != null, "Credits background inventory is incomplete.");
        Require(document.Pages.Select(p => p.Index).SequenceEqual(new[] { 6, 7, 8, 9, 10, 11, 12, 49, 50 })
            && document.Widgets.Count <= MaxWidgets, "Credits catalog page/widget profile is unsupported.");
        // Copy with our own comparer and bounded enumeration: caller Count and lookup policy are not admission.
        var suppliedModels = Copy(models, MaxWidgets);
        var suppliedAnimations = Copy(animations, MaxWidgets);
        var usedModels = new HashSet<string>(StringComparer.Ordinal);
        var usedAnimations = new HashSet<string>(StringComparer.Ordinal);
        var uniqueModels = new Dictionary<string, CampaignCreditsModelSource.Snapshot>(StringComparer.Ordinal);
        var uniqueAnimations = new Dictionary<(string Model, string Animation), CampaignCreditsAnimationBinding.Snapshot>();
        var samplers = new Dictionary<(string, string?), WidgetBinding>();
        var widgets = new Dictionary<int, WidgetBinding>();
        var durations = new Dictionary<string, CampaignCreditsAnimationDuration.Snapshot>(StringComparer.Ordinal);
        int vertices = 0, triangles = 0, samplerPixels = 0;
        AddModel(font.Model); // The font retains this instance; all equal widget hashes must reuse it too.
        foreach (var pair in document.Widgets)
        {
            Require(widgets.Count < MaxWidgets && pair.Key == pair.Value.Id && pair.Value.Files.Count is 1 or 2,
                "Credits widget inventory is inconsistent.");
            string? modelPath = pair.Value.Files[0].Path;
            Require(modelPath != null && suppliedModels.ContainsKey(modelPath), "Credits required model is unavailable.");
            var model = AddModel(suppliedModels[modelPath!]); usedModels.Add(modelPath!);
            string? animationPath = pair.Value.Files.Count == 2 ? pair.Value.Files[1].Path : null;
            CampaignCreditsAnimationBinding.Snapshot? animation = null;
            if (animationPath != null)
            {
                Require(suppliedAnimations.TryGetValue(animationPath, out animation)
                    && animation.ModelSha256 == model.SourceSha256, "Credits animation is unavailable or belongs to another model.");
                usedAnimations.Add(animationPath);
                var animationKey = (model.SourceSha256, animation!.SourceSha256);
                uniqueAnimations.TryAdd(animationKey, animation);
                animation = uniqueAnimations[animationKey];
                durations.TryAdd(animationPath, CampaignCreditsAnimationDuration.Read(animation.Memory.Span));
            }
            var binding = new WidgetBinding(model, animation);
            widgets.Add(pair.Key, binding);
            if (samplers.TryAdd((model.SourceSha256, animation?.SourceSha256), binding))
            {
                vertices = checked(vertices + model.VertexCount); triangles = checked(triangles + model.TriangleCount);
                samplerPixels = checked(samplerPixels + model.PixelCount);
                Require(samplers.Count <= MaxSamplers && vertices <= MaxSamplerVertices && triangles <= MaxSamplerTriangles
                    && samplerPixels <= MaxSamplerPixels,
                    "Credits private sampler geometry exceeds its aggregate budget.");
            }
        }
        Require(usedModels.Count == suppliedModels.Count && usedAnimations.Count == suppliedAnimations.Count,
            "Credits inventory contains unreferenced model/animation paths.");
        // Charge canonical retained model/binding instances, metrics, all four encoded bitmap extents,
        // and later per-style text. Document admission separately bounds MARM records/retained strings;
        // this is not a claim to retain or measure the original encoded MARM/localization files.
        int sourceBytes = checked(uniqueModels.Values.Sum(m => m.ByteLength)
            + uniqueAnimations.Values.Sum(a => a.Memory.Length) + CampaignCreditsFontSource.GlyphCount * 2
            + 4 * CampaignCreditsBackgroundSource.EncodedBytes);
        int layouts = 0, characters = 0;
        foreach (var page in document.Pages)
        {
            Require(page.Items.Count <= CampaignCreditsSource.MaxItems, "Credits catalog item bound exceeded.");
            foreach (var item in page.Items)
            {
                Require(item.States.Count <= CampaignCreditsSource.MaxStates, "Credits catalog state bound exceeded.");
                foreach (var state in item.States)
                {
                    if (state.WidgetId is int id) Require(widgets.ContainsKey(id), "Credits visual has no admitted widget.");
                    if (state.Text is not { } style) continue;
                    Require(document.Strings.TryGetValue(style.StringId, out var text), "Credits visual has no admitted text.");
                    characters = checked(characters + text!.ContentLatin1.Length); layouts++;
                    Require(layouts <= MaxLayouts && characters <= MaxTextCharacters, "Credits aggregate text budget exceeded.");
                }
            }
        }
        sourceBytes = checked(sourceBytes + characters);
        Require(sourceBytes <= MaxSourceBytes, "Credits retained source bytes exceed their aggregate budget.");
        // Texture/palette identity, not declared texture count, bounds decoded RGBA.
        // Material animation admission has no texture channel, so these are all reachable pairs.
        var pairs = new List<TexturePair>(); var seenPairs = new HashSet<TextureKey>(); int rgba = 0;
        foreach (var model in uniqueModels.Values)
        {
            var b = model.Memory.Span; int materials = (int)U32(b, 16), textures = (int)U32(b, 44);
            for (int i = 0; i < model.MaterialCount; i++)
            {
                int at = materials + i * 132, texture = I16(b, at + 70), palette = I16(b, at + 68);
                if (texture < 0) continue;
                var key = new TextureKey(model.SourceSha256, texture, palette);
                if (!seenPairs.Add(key)) continue;
                int width = U16(b, textures + texture * 40 + 2), height = U16(b, textures + texture * 40 + 4);
                rgba = checked(rgba + width * height * 4);
                Require(rgba <= MaxTextureRgbaBytes, "Credits texture/palette RGBA exceeds its aggregate budget.");
                pairs.Add(new(key, width, height));
            }
        }
        Document = document; Font = font; Images = backgrounds;
        Widgets = new ReadOnlyDictionary<int, WidgetBinding>(widgets);
        Models = new ReadOnlyDictionary<string, CampaignCreditsModelSource.Snapshot>(uniqueModels);
        TexturePairs = pairs.AsReadOnly(); Frames = CampaignCreditsFrameTable.Bind(document, durations);
        Budget = new(sourceBytes, samplers.Count, vertices, triangles, rgba, layouts, characters) { SamplerPixels = samplerPixels };

        CampaignCreditsModelSource.Snapshot AddModel(CampaignCreditsModelSource.Snapshot model)
        {
            uniqueModels.TryAdd(model.SourceSha256, model);
            Require(uniqueModels.Count <= MaxModels, "Credits unique-model budget exceeded.");
            return uniqueModels[model.SourceSha256];
        }
    }
    private static Dictionary<string, T> Copy<T>(IReadOnlyDictionary<string, T> source, int max) where T : class
    {
        var copy = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            Require(copy.Count < max && pair.Key != null && pair.Value != null && copy.TryAdd(pair.Key, pair.Value),
                "Credits supplied inventory is invalid or exceeds its bound.");
        }
        return copy;
    }
}
