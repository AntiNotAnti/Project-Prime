#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    internal static class RetainedRenderGraphCheck
    {
        internal static int Run()
        {
            int failures = 0;
            void Check(bool condition, string name)
            {
                if (condition) Console.WriteLine("[rendergraphcheck] PASS: " + name);
                else
                {
                    Console.WriteLine("[rendergraphcheck] FAIL: " + name);
                    failures++;
                }
            }

            Check(WorldRenderGraph.Validate(out string error),
                "six-pass graph validates" + (error.Length == 0 ? "" : ": " + error));
            var capabilityCache = new OpenGlScopedCapabilityCache();
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, true),
                "outside-world GL state requests are always forwarded");
            capabilityCache.Begin();
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, true)
                && !capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, true)
                && capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false)
                && !capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "world scoped capability cache removes only same-value requests");
            capabilityCache.BeginList(12, OpenTK.Graphics.OpenGL.ListMode.Compile);
            capabilityCache.EndList();
            Check(!capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "pure mesh compilation does not mutate a graph's saved capability");
            capabilityCache.CallList(12);
            Check(!capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "known geometry-only display list preserves inferred world capability");
            capabilityCache.BeginList(13, OpenTK.Graphics.OpenGL.ListMode.Compile);
            capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, true);
            capabilityCache.EndList();
            capabilityCache.CallList(13);
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "capability-changing display list invalidates inferred state");
            capabilityCache.CallList(99999);
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "untracked GL display list triggers conservative state invalidation");
            capabilityCache.End();
            capabilityCache.Begin();
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "new world graph starts with unknown driver capability state");
            capabilityCache.End();
            capabilityCache.ResetContext();

            Check(OpenGlSubmissionProfiler.PercentileIndex(100, 50) == 49
                && OpenGlSubmissionProfiler.PercentileIndex(100, 95) == 94
                && OpenGlSubmissionProfiler.PercentileIndex(100, 99) == 98
                && OpenGlSubmissionProfiler.PercentileIndex(1, 99) == 0,
                "bounded GL CPU percentile sampling uses nearest rank");
            Check(WorldRenderGraph.CanReuseOpaqueDepth(0, 0)
                && !WorldRenderGraph.CanReuseOpaqueDepth(1, 0)
                && !WorldRenderGraph.CanReuseOpaqueDepth(0, 1)
                && !WorldRenderGraph.CanReuseOpaqueDepth(1, 1)
                && !WorldRenderGraph.CanReuseOpaqueDepth(0, 0, forceLegacy: true),
                "opaque-depth elision excludes decals/translucency and has A/B fallback");
            Check(FrameRenderGraph.Validate(out string frameError),
                "top-level frame render graph validates"
                    + (frameError.Length == 0 ? "" : ": " + frameError));
            var frameGraph = new FrameRenderGraph();
            Check(frameGraph.Passes.Count == 7
                && frameGraph.Passes[0].Kind == FrameRenderPassKind.Shadow
                && frameGraph.Passes[1].Kind == FrameRenderPassKind.WorldSetup
                && frameGraph.Passes[2].Kind == FrameRenderPassKind.World
                && (frameGraph.Passes[2].Reads & FrameRenderResource.SceneDepth) != 0
                && frameGraph.Passes[3].Kind == FrameRenderPassKind.Outlines
                && !System.Linq.Enumerable.Any(frameGraph.Passes,
                    pass => pass.Name.Contains("pbr", StringComparison.OrdinalIgnoreCase))
                && System.Linq.Enumerable.Single(frameGraph.Passes,
                    pass => pass.Kind == FrameRenderPassKind.PostProcess).Reads
                    == (FrameRenderResource.SceneColor | FrameRenderResource.SceneDepth
                        | FrameRenderResource.ShadowDepth)
                && frameGraph.Passes[^1].Kind == FrameRenderPassKind.Composite
                && (frameGraph.Passes[^1].Writes & FrameRenderResource.Output) != 0,
                "OpenGL frame graph excludes retired GPU visibility and Hi-Z passes");
            var opaqueA = new RenderItem
            {
                Type = RenderItemType.Mesh, ListId = 11,
                HasTexture = true, TextureBindingId = 21,
                Alpha = 1, Diffuse = Vector3.One
            };
            var opaqueB = new RenderItem
            {
                Type = RenderItemType.Mesh, ListId = 12,
                HasTexture = true, TextureBindingId = 21,
                Alpha = 1, Diffuse = Vector3.One
            };
            var overrideMesh = new RenderItem
            {
                Type = RenderItemType.Mesh, ListId = 13,
                HasTexture = true, TextureBindingId = 21,
                Alpha = 1, Diffuse = Vector3.One,
                OverrideColor = new Vector4(1, 0, 0, 1)
            };
            var decal = new RenderItem
            {
                Type = RenderItemType.Mesh, ListId = 14,
                RenderMode = RenderMode.Decal
            };
            var translucent = new RenderItem
            {
                Type = RenderItemType.Mesh, ListId = 15,
                RenderMode = RenderMode.Translucent,
                Alpha = 0.5f
            };

            var world = new RetainedRenderWorld();
            world.Capture(
                new List<RenderItem> { opaqueA, opaqueB, overrideMesh, translucent },
                new List<RenderItem> { decal },
                new List<RenderItem> { translucent });

            Check(world.Opaque.Count == 4 && world.Decals.Count == 1
                && world.Translucent.Count == 1, "packet classes preserve membership");
            Check(!WorldRenderGraph.CanReuseOpaqueDepth(
                    world.Decals.Count, world.Translucent.Count),
                "captured decal/translucent packets force normal depth rebuild");
            Check(ReferenceEquals(world.Opaque[0].Item, opaqueA)
                && ReferenceEquals(world.Opaque[1].Item, opaqueB),
                "capture preserves submission order");
            Check(world.Opaque[0].Sequence == 0 && world.Opaque[1].Sequence == 1,
                "sequence is retained explicitly");
            Check(world.Opaque[0].Mesh.ListId == 11
                && world.Opaque[1].Mesh.ListId == 12,
                "immutable mesh descriptors preserve geometry identity");
            Check(!ReferenceEquals(world.Opaque[0].Mesh, world.Opaque[1].Mesh),
                "different display lists keep distinct mesh descriptors");

            Check(world.OpaqueBatches.Count == 3
                && world.OpaqueBatches[0].Start == 0
                && world.OpaqueBatches[0].Count == 2,
                "adjacent identical material state batches across mesh IDs");
            Check(world.OpaqueBatches[1].Start == 2
                && world.OpaqueBatches[1].Count == 1
                && !world.Opaque[2].Batchable,
                "override packet breaks batching");
            Check(world.StateReuseCount >= 1,
                "batching records at least one shared state reuse");
            Check(world.GraphStateReuseCount >= 2,
                "opaque reuse is counted in both opaque and depth passes");

            RetainedMaterialDescriptor captured = world.Opaque[0].Material;
            opaqueA.Diffuse = new Vector3(.25f, .5f, .75f);
            Check(world.Opaque[0].Material == captured,
                "captured material descriptor is immutable for the frame");

            ulong firstKey = world.Opaque[0].StateKey;
            int descriptorCount = world.MeshDescriptorCount;
            opaqueA.Diffuse = Vector3.One;
            world.Capture(
                new List<RenderItem> { opaqueA, opaqueB },
                Array.Empty<RenderItem>(),
                Array.Empty<RenderItem>());
            Check(world.FrameRevision == 2, "frame revision advances");
            Check(world.Opaque[0].StateKey == firstKey, "state key is deterministic");
            Check(world.PacketCount == 2, "capture reuses and clears packet lists");
            Check(WorldRenderGraph.CanReuseOpaqueDepth(
                    world.Decals.Count, world.Translucent.Count),
                "opaque-only retained frame can reuse its depth attachment");
            Check(world.OpaqueBatches.Count == 1 && world.OpaqueBatches[0].Count == 2,
                "compatible packets remain one adjacent batch next frame");
            Check(world.MeshDescriptorCount == descriptorCount,
                "mesh descriptor table is retained rather than rebuilt per frame");

            var sortableA = new RenderItem
            {
                Type = RenderItemType.Mesh,
                ListId = 31,
                HasTexture = true,
                TextureBindingId = 41,
                Alpha = 1,
                Diffuse = Vector3.One,
                RetainedRoomOwned = true
            };
            var sortableB = new RenderItem
            {
                Type = RenderItemType.Mesh,
                ListId = 32,
                HasTexture = true,
                TextureBindingId = 42,
                Alpha = 1,
                Diffuse = Vector3.One,
                RetainedRoomOwned = true
            };
            world.Capture(
                new List<RenderItem> { sortableA, sortableB },
                Array.Empty<RenderItem>(),
                Array.Empty<RenderItem>());
            RenderItem firstByKey = world.Opaque[0].StateKey <= world.Opaque[1].StateKey
                ? world.Opaque[0].Item : world.Opaque[1].Item;
            RenderItem secondByKey = ReferenceEquals(firstByKey, sortableA)
                ? sortableB : sortableA;
            world.Capture(
                new List<RenderItem> { secondByKey, firstByKey },
                Array.Empty<RenderItem>(),
                Array.Empty<RenderItem>());
            Check(ReferenceEquals(world.Opaque[0].Item, firstByKey)
                && ReferenceEquals(world.Opaque[1].Item, secondByKey)
                && world.OpaqueSortRunCount == 1
                && world.OpaqueReorderedPacketCount == 2,
                "room-owned opaque safe run sorts by retained state");

            var opaqueBarrier = new RenderItem
            {
                Type = RenderItemType.Mesh,
                ListId = 33,
                HasTexture = true,
                TextureBindingId = 43,
                Alpha = 1,
                Diffuse = Vector3.One,
                RetainedRoomOwned = false
            };
            world.Capture(
                new List<RenderItem> { secondByKey, opaqueBarrier, firstByKey },
                Array.Empty<RenderItem>(),
                Array.Empty<RenderItem>());
            Check(ReferenceEquals(world.Opaque[0].Item, secondByKey)
                && ReferenceEquals(world.Opaque[1].Item, opaqueBarrier)
                && ReferenceEquals(world.Opaque[2].Item, firstByKey)
                && world.OpaqueSortRunCount == 0
                && world.OpaqueReorderedPacketCount == 0,
                "dynamic opaque packet is a hard sorting barrier");

            var baseOnly = new RetainedWorldTextureSet(
                new RetainedTextureBinding(10, default, true),
                default, default, default);
            Check(!baseOnly.Advanced && baseOnly.At(0).Id == 10,
                "base-only retained texture set stays non-advanced");
            var advanced = new RetainedWorldTextureSet(
                new RetainedTextureBinding(10, default, true),
                new RetainedTextureBinding(11, default, true),
                new RetainedTextureBinding(12, default, true),
                new RetainedTextureBinding(13, default, true));
            Check(advanced.Advanced
                && advanced.At(1).Id == 11
                && advanced.At(2).Id == 12
                && advanced.At(3).Id == 13,
                "retained texture set preserves companion map units");

            Console.WriteLine(failures == 0
                ? "[rendergraphcheck] PASS"
                : $"[rendergraphcheck] FAIL: {failures} check(s)");
            return failures == 0 ? 0 : 1;
        }
    }
}
#endif
