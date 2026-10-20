#if (MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !MPHREAD_SERVER && !ANDROID) || (ANDROID && PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Mathematics;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Sampler = MphRead.Mods.Campaign.CampaignCreditsWidgetSampler;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

// Transport only: source state/order comes from FrameOwner, projection/clipping
// and GPU lifetime come from the existing native canvas. No clock or native IO.
internal sealed class CampaignCreditsCanvasAdapter
{
    private readonly record struct Policy(Sampler.TextureKey Texture, RepeatMode S, RepeatMode T);
    private readonly Dictionary<Policy, uint> _slots = new();
    private readonly Dictionary<CampaignCreditsBackgroundSource.Snapshot, uint> _backgrounds = new();
    private readonly IReadOnlyDictionary<int, int> _triangleBounds;
    private readonly int _resultsPage;
    internal RmlUiCanvasTextureTable Table { get; }

    internal CampaignCreditsCanvasAdapter(CampaignCreditsCatalog catalog, CampaignEndingSummary summary,
        CampaignCreditsFrameOwner frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        _triangleBounds = Admit(catalog, summary);
        _resultsPage = summary.Kind == CampaignEndingKind.Complete ? 49 : 50;
        var images = new List<RmlUiCanvasImage>();
        foreach (Policy policy in Policies(catalog))
        {
            if (!frames.Textures.TryGetValue(policy.Texture, out var pixels))
                throw new InvalidDataException("Credits canvas texture was not admitted.");
            _slots.Add(policy, (uint)images.Count);
            images.Add(new(pixels.Width, pixels.Height, pixels.Rgba, 0, (uint)policy.S, (uint)policy.T));
        }
        foreach (var image in Backgrounds(catalog))
        {
            if (_backgrounds.ContainsKey(image)) continue;
            _backgrounds.Add(image, (uint)images.Count);
            images.Add(new(image.Width, image.Height, image.Rgba, 1, 0, 0));
        }
        Table = new(images); // Copies straight RGBA once; native premultiplies once.
    }

    // Admit every possible selected page before starting playback or constructing
    // the owner-thread model samplers. Conservative per-triangle batches retain
    // all admitted geometry; an unsupported profile falls back as a whole.
    internal static IReadOnlyDictionary<int, int> Admit(CampaignCreditsCatalog catalog, CampaignEndingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var pairs = catalog.TexturePairs.ToDictionary(p => p.Key);
        int slots = 4, bytes = 4 * 256 * 192 * 4;
        foreach (Policy policy in Policies(catalog))
        {
            if (!pairs.TryGetValue(policy.Texture, out var pair)) throw new InvalidDataException("Credits canvas pair is unavailable.");
            Require(pair.Width is >= 1 and <= 512 && pair.Height is >= 1 and <= 512, "Credits canvas image dimensions are unsupported.");
            slots++; bytes = checked(bytes + pair.Width * pair.Height * 4);
            Require(slots <= 64 && bytes <= 4 * 1024 * 1024, "Credits canvas texture policies exceed their aggregate budget.");
        }
        var results = new CampaignCreditsResultsBinding(catalog.Document, summary);
        var bounds = new Dictionary<int, int>();
        foreach (var page in catalog.Document.Pages)
        {
            int total = page.Index is 49 or 50 ? 4 : 0;
            foreach (var item in page.Items)
            {
                int largest = 0;
                foreach (var state in item.States)
                {
                    int count = state.Text is { } style ? checked(results.Resolve(catalog.Document, page.Index, item.Index, style).Length * 2)
                        : state.WidgetId is int id ? catalog.Widgets[id].Model.TriangleCount : 0;
                    largest = Math.Max(largest, count);
                }
                total = checked(total + largest);
                Require(total <= 4096 && total * 3 <= 65536 && total * 3 <= 196608,
                    "Credits canvas page exceeds its vertex/index/batch admission.");
            }
            bounds.Add(page.Index, total);
        }
        return new ReadOnlyDictionary<int, int>(bounds);
    }

    internal RmlUiCanvasFrame Build(CampaignCreditsFrameOwner.Snapshot frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Closed) return new(256, 384, Array.Empty<RmlUiCanvasVertex>(), Array.Empty<int>(), Array.Empty<RmlUiCanvasBatch>());
        Require(frame.Failure == null && _triangleBounds.TryGetValue(frame.Page, out _)
            && (frame.Page is not (49 or 50) || frame.Page == _resultsPage), "Credits canvas frame belongs to an unsupported page.");
        int backgrounds = frame.Backgrounds.Count, triangles = frame.Triangles.Count;
        Require(backgrounds == (frame.Page is 49 or 50 ? 2 : 0) && triangles >= 0
            && triangles <= _triangleBounds[frame.Page] - backgrounds * 2, "Credits canvas frame exceeds its admitted page.");
        int count = checked((triangles + backgrounds * 2) * 3), at = 0;
        var vertices = new RmlUiCanvasVertex[count]; var indices = new int[count];
        var batches = new List<RmlUiCanvasBatch>();
        void Add(uint texture, Sampler.Vertex a, Sampler.Vertex b, Sampler.Vertex c, Vector4 color)
        {
            byte r = RmlUiCanvasVertex.Quantize(color.X), g = RmlUiCanvasVertex.Quantize(color.Y),
                blue = RmlUiCanvasVertex.Quantize(color.Z), alpha = RmlUiCanvasVertex.Quantize(color.W);
            int first = at;
            for (int corner = 0; corner < 3; corner++)
            {
                var vertex = corner == 0 ? a : corner == 1 ? b : c;
                var p = vertex.Position; var uv = vertex.Texcoord;
                vertices[at] = new(p.X, 192 - p.Y, r, g, blue, alpha, uv.X, uv.Y);
                indices[at] = at; at++;
            }
            if (batches.Count > 0 && batches[^1].Texture == texture)
                batches[^1] = batches[^1] with { Count = batches[^1].Count + 3 };
            else batches.Add(new(texture, (uint)first, 3));
        }
        for (int i = 0; i < backgrounds; i++)
        {
            var bg = frame.Backgrounds[i];
            Require(_backgrounds.TryGetValue(bg.Image, out _), "Credits canvas background belongs to another catalog.");
            uint texture = _backgrounds[bg.Image];
            var a = new Sampler.Vertex(new(bg.X, bg.Y, 0), new(0, 0));
            var b = new Sampler.Vertex(new(bg.X + bg.Width, bg.Y, 0), new(1, 0));
            var c = new Sampler.Vertex(new(bg.X + bg.Width, bg.Y - bg.Height, 0), new(1, 1));
            var d = new Sampler.Vertex(new(bg.X, bg.Y - bg.Height, 0), new(0, 1));
            Add(texture, a, b, c, Vector4.One); Add(texture, a, c, d, Vector4.One);
        }
        for (int i = 0; i < triangles; i++)
        {
            var t = frame.Triangles[i]; uint texture = uint.MaxValue;
            Require(t.Texture.TextureId >= 0 || (t.Texture.TextureId == -1 && t.Texture.PaletteId == -1),
                "Credits canvas untextured binding is invalid.");
            if (t.Texture.TextureId >= 0)
            {
                Require(_slots.TryGetValue(new(t.Texture, t.WrapS, t.WrapT), out _), "Credits canvas sampler policy was not admitted.");
                texture = _slots[new(t.Texture, t.WrapS, t.WrapT)];
            }
            Add(texture, t.A, t.B, t.C, t.Color);
        }
        return new(256, 384, vertices, indices, batches.ToArray());
    }

    private static IEnumerable<Policy> Policies(CampaignCreditsCatalog catalog)
    {
        var seen = new HashSet<Policy>();
        foreach (var model in catalog.Widgets.Values.Select(w => w.Model).DistinctBy(m => m.SourceSha256))
        {
            // Material/animation admission already forbids texture/wrap animation.
            // Read bounded admitted metadata once, never files or mutable Models.
            var memory = model.Memory;
            for (int i = 0; i < model.MaterialCount; i++)
            {
                int at = checked((int)U32(memory.Span, 16) + i * 132), texture = I16(memory.Span, at + 70);
                if (texture < 0) continue;
                var policy = new Policy(new(model.SourceSha256, texture, I16(memory.Span, at + 68)),
                    (RepeatMode)memory.Span[at + 72], (RepeatMode)memory.Span[at + 73]);
                Require((policy.S is RepeatMode.Clamp or RepeatMode.Repeat) && (policy.T is RepeatMode.Clamp or RepeatMode.Repeat),
                    "Credits canvas source wrap policy is unsupported.");
                if (seen.Add(policy)) yield return policy;
            }
        }
        var font = new Policy(new(catalog.Font.ModelSha256, 0, 0), RepeatMode.Clamp, RepeatMode.Clamp);
        if (seen.Add(font)) yield return font;
    }
    private static IEnumerable<CampaignCreditsBackgroundSource.Snapshot> Backgrounds(CampaignCreditsCatalog catalog)
    {
        yield return catalog.Images.GoodTop; yield return catalog.Images.GoodBottom;
        yield return catalog.Images.BadTop; yield return catalog.Images.BadBottom;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
#endif
