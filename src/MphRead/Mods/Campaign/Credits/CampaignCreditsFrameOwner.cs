using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenTK.Mathematics;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Sampler = MphRead.Mods.Campaign.CampaignCreditsWidgetSampler;

namespace MphRead.Mods.Campaign;

/// <summary>One-thread CPU presentation owner. The supplied timeline alone advances states and owns return intent.</summary>
internal sealed class CampaignCreditsFrameOwner : IDisposable
{
    internal const int MaxCachedGlyphs = 65536, MaxFrameGlyphs = 16384, MaxFrameTriangles = 65536,
        MaxAggregateMeasurementBytes = 64 * 1024 * 1024;
    // Y is the top edge in source Y-up space. These are stacked DS regions, not the reference widescreen recomposition.
    internal sealed record Background(CampaignCreditsBackgroundSource.Snapshot Image, float X, float Y, float Width, float Height);
    internal sealed record Snapshot(int Page, long PageFrame, bool Closed, string? Failure,
        IReadOnlyList<Sampler.Triangle> Triangles, IReadOnlyList<Background> Backgrounds);
    internal sealed class TexturePixels
    {
        private readonly byte[] _rgba;
        internal int Width { get; }
        internal int Height { get; }
        internal ReadOnlyMemory<byte> Rgba => _rgba;
        internal TexturePixels(int width, int height, byte[] rgba) { Width = width; Height = height; _rgba = rgba; }
    }
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private CampaignCreditsCatalog? _catalog;
    private CampaignCreditsTimeline? _timeline;
    private readonly Dictionary<int, CampaignCreditsSource.Page> _pages = new();
    private readonly Dictionary<(string Model, string? Animation), Sampler> _samplers = new();
    private readonly Dictionary<(int Page, int Item, int Code), CampaignCreditsTextLayout.Snapshot> _layouts = new();
    private IReadOnlyDictionary<Sampler.TextureKey, TexturePixels> _textures = null!;
    internal IReadOnlyDictionary<Sampler.TextureKey, TexturePixels> Textures { get { VerifyOwner(); return _textures; } }

    internal CampaignCreditsFrameOwner(CampaignCreditsCatalog catalog, CampaignCreditsTimeline timeline, CampaignEndingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(timeline);
        Require(timeline.UsesSource(catalog.Document, catalog.Frames, summary.Kind == CampaignEndingKind.Complete),
            "Credits frame owner requires the catalog's exact timeline and captured outcome.");
        var results = new CampaignCreditsResultsBinding(catalog.Document, summary);
        var textPlans = new List<(int Page, CampaignCreditsSource.Item Item, CampaignCreditsSource.State State, string Text)>();
        int characters = 0; long measurement = 0;
        // Admit every planned layout and each page's maximum geometry BEFORE model/pixel construction or layout allocation.
        foreach (var page in catalog.Document.Pages)
        {
            _pages.Add(page.Index, page); int pageGlyphs = 0, pageTriangles = 0;
            foreach (var item in page.Items)
            {
                int itemGlyphs = 0, itemTriangles = 0;
                foreach (var state in item.States)
                {
                    if (state.Text is { } style)
                    {
                        string text = results.Resolve(catalog.Document, page.Index, item.Index, style);
                        characters = checked(characters + text.Length);
                        // Each Measure consumes a contiguous source slice, with at most length+1 candidates plus raw line scans.
                        measurement += Math.Min(CampaignCreditsTextLayout.MaxMeasurementBytes, (text.Length + 1L) * (text.Length + 2L));
                        Require(textPlans.Count < CampaignCreditsCatalog.MaxLayouts && characters <= MaxCachedGlyphs
                            && measurement <= MaxAggregateMeasurementBytes, "Credits cached text exceeds its aggregate budget.");
                        textPlans.Add((page.Index, item, state, text));
                        itemGlyphs = Math.Max(itemGlyphs, text.Length); itemTriangles = Math.Max(itemTriangles, checked(text.Length * 2));
                    }
                    else if (state.WidgetId is int id)
                        itemTriangles = Math.Max(itemTriangles, catalog.Widgets[id].Model.TriangleCount);
                }
                pageGlyphs = checked(pageGlyphs + itemGlyphs); pageTriangles = checked(pageTriangles + itemTriangles);
            }
            Require(pageGlyphs <= MaxFrameGlyphs && pageTriangles <= MaxFrameTriangles,
                "Credits page geometry exceeds its aggregate frame budget.");
        }
        int glyphs = 0;
        foreach (var plan in textPlans)
        {
            var item = plan.Item;
            var layout = CampaignCreditsTextLayout.Build(catalog.Font, plan.Text, plan.State.Text!, item.XFx, item.YFx, item.DepthFx);
            glyphs = checked(glyphs + layout.Glyphs.Count);
            Require(glyphs <= MaxCachedGlyphs, "Credits actual cached glyphs exceed their admission.");
            _layouts.Add((plan.Page, item.Index, plan.State.Code), layout);
        }
        foreach (var binding in catalog.Widgets.Values)
        {
            var key = (binding.Model.SourceSha256, binding.Animation?.SourceSha256);
            if (!_samplers.ContainsKey(key)) _samplers.Add(key, new Sampler(binding.Model, binding.Animation));
        }
        var textures = new Dictionary<Sampler.TextureKey, TexturePixels>();
        foreach (var group in catalog.TexturePairs.GroupBy(p => p.Key.ModelSha256, StringComparer.Ordinal))
        {
            // One private still decode per model, never the global cache. Its mutable Model is not published or retained.
            Model model = MphRead.Read.ReadCreditsModel(catalog.Models[group.Key]);
            foreach (var pair in group)
            {
                var pixels = model.Recolors[0].GetPixels(pair.Key.TextureId, pair.Key.PaletteId);
                Require(pixels.Count == pair.Width * pair.Height, "Credits decoded texture extent differs from its pair admission.");
                var rgba = new byte[checked(pixels.Count * 4)];
                for (int i = 0; i < pixels.Count; i++)
                {
                    var pixel = pixels[i]; int at = i * 4;
                    rgba[at] = pixel.Red; rgba[at + 1] = pixel.Green; rgba[at + 2] = pixel.Blue; rgba[at + 3] = pixel.Alpha;
                }
                textures.Add(pair.Key, new(pair.Width, pair.Height, rgba));
            }
        }
        _textures = new ReadOnlyDictionary<Sampler.TextureKey, TexturePixels>(textures);
        _catalog = catalog; _timeline = timeline;
    }

    internal Snapshot Capture()
    {
        VerifyOwner(); var catalog = _catalog!; var state = _timeline!.Snapshot();
        if (state.Closed) return new(state.Page, state.PageFrame, true, state.Failure,
            Array.Empty<Sampler.Triangle>(), Array.Empty<Background>());
        var page = _pages[state.Page];
        var output = new List<(Sampler.Triangle Triangle, int Order)>(); int glyphs = 0;
        foreach (var active in state.Items)
        {
            if (!active.Started || active.Visual is not { } visual) continue;
            var item = page.Items[active.Index];
            if (visual.Text != null)
            {
                var layout = _layouts[(state.Page, active.Index, visual.Code)]; var color = layout.ColorAt(visual.Code, active.Frame);
                if (color.W <= 0) continue;
                Require(layout.Glyphs.Count <= MaxFrameGlyphs - glyphs && layout.Glyphs.Count * 2 <= MaxFrameTriangles - output.Count,
                    "Credits sampled glyph count exceeds its admitted frame budget.");
                glyphs += layout.Glyphs.Count;
                var texture = new Sampler.TextureKey(catalog.Font.ModelSha256, 0, 0);
                foreach (var g in layout.Glyphs)
                {
                    var a = new Sampler.Vertex(new(g.X, g.Y, g.Depth), new(g.U0, g.V0));
                    var b = new Sampler.Vertex(new(g.X + 8, g.Y, g.Depth), new(g.U1, g.V0));
                    var c = new Sampler.Vertex(new(g.X + 8, g.Y - 8, g.Depth), new(g.U1, g.V1));
                    var d = new Sampler.Vertex(new(g.X, g.Y - 8, g.Depth), new(g.U0, g.V1));
                    Add(new(a, b, c, color, texture, RepeatMode.Clamp, RepeatMode.Clamp, g.Depth));
                    Add(new(a, c, d, color, texture, RepeatMode.Clamp, RepeatMode.Clamp, g.Depth));
                }
            }
            else if (visual.WidgetId is int id)
            {
                var binding = catalog.Widgets[id];
                var sampler = _samplers[(binding.Model.SourceSha256, binding.Animation?.SourceSha256)];
                Require(binding.Model.TriangleCount <= MaxFrameTriangles - output.Count,
                    "Credits sampled widget count exceeds its admitted frame budget.");
                var sample = sampler.Sample(active.Frame, active.Code < 8);
                // Native widgets translate in XY; the item's depth changes painter order, not source vertex Z.
                var translation = new Vector3(item.XFx / 4096f, item.YFx / 4096f, 0);
                foreach (var triangle in sample.Triangles)
                {
                    Sampler.Vertex Move(Sampler.Vertex vertex) => vertex with { Position = vertex.Position + translation };
                    Add(triangle with { A = Move(triangle.A), B = Move(triangle.B), C = Move(triangle.C), Depth = triangle.Depth + item.DepthFx / 4096f });
                }
            }
        }
        // Explicit tie-break preserves source order; List.Sort itself is not stable.
        output.Sort((a, b) => { int z = a.Triangle.Depth.CompareTo(b.Triangle.Depth); return z != 0 ? z : a.Order.CompareTo(b.Order); });
        var triangles = new Sampler.Triangle[output.Count];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = output[i].Triangle;
        Background[] backgrounds = state.Page switch
        {
            49 => new[] { new Background(catalog.Images.GoodTop, 0, 192, 256, 192), new Background(catalog.Images.GoodBottom, 0, 0, 256, 192) },
            50 => new[] { new Background(catalog.Images.BadTop, 0, 192, 256, 192), new Background(catalog.Images.BadBottom, 0, 0, 256, 192) },
            _ => Array.Empty<Background>()
        };
        return new(state.Page, state.PageFrame, false, state.Failure, Array.AsReadOnly(triangles), Array.AsReadOnly(backgrounds));

        void Add(Sampler.Triangle triangle)
        {
            Require(output.Count < MaxFrameTriangles && float.IsFinite(triangle.Depth), "Credits output exceeds its finite frame admission.");
            if (triangle.Texture.TextureId >= 0) Require(_textures.ContainsKey(triangle.Texture), "Credits output texture was not admitted.");
            output.Add((triangle, output.Count));
        }
    }
    public void Dispose()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Credits frame owner belongs to its creating thread.");
        _catalog = null; _timeline = null; _samplers.Clear(); _layouts.Clear(); _pages.Clear();
        _textures = new ReadOnlyDictionary<Sampler.TextureKey, TexturePixels>(new Dictionary<Sampler.TextureKey, TexturePixels>());
    }
    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Credits frame owner belongs to its creating thread.");
        ObjectDisposedException.ThrowIf(_catalog == null, this);
    }
}
