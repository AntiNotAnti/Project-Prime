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
            int failures = DynamicGeometryStreamCheck.Run() + OpenGlModernizationCheck.Run()
                + HudGlyphTextureCacheCheck.Run() + ProceduralFrameGeometryCheck.Run() + DepthReplayPolicyCheck.Run()
                + DisplayListAttributeEffects.Check() + WorldShaderVariantCheck.Run();
            failures += TransitionGcDiagnostics.Check();
            failures += OpenGlDrawAttribution.Check();
            failures += OpenGlUniformAttribution.Check() + OpenGlPassProfiler.CheckPaletteCounters();
            failures += RetainedRoomBatchPlan.CheckMembershipPolicy() + ShadowCasterPreparationCacheCheck.Run()
                + ShadowCasterSpatialGroups.Check()
                + OpenGlFailureInjectionCheck.Run() + RetainedMaterialIdentity.Check() + MeshObjectStateReuse.Check();
            void Check(bool condition, string name)
            {
                if (condition) Console.WriteLine("[rendergraphcheck] PASS: " + name);
                else
                {
                    Console.WriteLine("[rendergraphcheck] FAIL: " + name);
                    failures++;
                }
            }

#if !ANDROID
            failures += DesktopRetainedGeometry.CheckNegativeBatchPolicy() + DesktopRetainedGeometry.CheckDrawTokenPolicy() + DesktopRetainedGeometry.CheckTrailingAttributePolicy();
            Check(DesktopRetainedGeometry.CheckAtlasEvictionPolicy() == 0,
                "atlas eviction preserves current-scope groups and the resident budget");
            Check(DesktopRetainedGeometry.CanUseClientArrays(12, 0, 0, 0)
                && DesktopRetainedGeometry.CanUseClientArrays(12, 12, 12, 12)
                && !DesktopRetainedGeometry.CanUseClientArrays(12, 6, 12, 12)
                && !DesktopRetainedGeometry.CanUseClientArrays(12, 12, 4, 12)
                && !DesktopRetainedGeometry.CanUseClientArrays(12, 12, 12, 10),
                "desktop VBO promotion accepts only homogeneous fixed-function attribute streams");
#endif
            Matrix4 translatedAway = Matrix4.CreateTranslation(600f, 0f, 0f);
            Check(MphRead.Entities.RoomEntity.ShadowCasterOverlapsLightXY(
                    new Vector3(-.5f), new Vector3(.5f), Matrix4.Identity)
                && !MphRead.Entities.RoomEntity.ShadowCasterOverlapsLightXY(
                    new Vector3(100f), new Vector3(110f), Matrix4.Identity)
                && MphRead.Entities.RoomEntity.ShadowCasterOverlapsLightXY(
                    new Vector3(599.5f, -.5f, -.5f),
                    new Vector3(600.5f, .5f, .5f), translatedAway.Inverted()),
                "light-space XY bounds reject distant nodes without using the camera frustum");
            Check(MphRead.Entities.ItemInstanceEntity.FullyOccludedBySamples(
                    true, true, true, true, true)
                && !MphRead.Entities.ItemInstanceEntity.FullyOccludedBySamples(
                    true, true, false, true, true),
                "pickup visual culling requires all five blocked samples");
            Check(WorldRenderGraph.Validate(out string error),
                "six-pass graph validates" + (error.Length == 0 ? "" : ": " + error));
            Check(MphRead.Entities.RoomEntity.RefreshRoomVisibilityOnDraw(
                    processFrame: false, hidden: false, inRoomTransition: false,
                    legacy: false)
                && MphRead.Entities.RoomEntity.RefreshRoomVisibilityOnDraw(
                    processFrame: true, hidden: false, inRoomTransition: false,
                    legacy: false)
                && !MphRead.Entities.RoomEntity.RefreshRoomVisibilityOnDraw(
                    processFrame: false, hidden: false, inRoomTransition: false,
                    legacy: true)
                && !MphRead.Entities.RoomEntity.RefreshRoomVisibilityOnDraw(
                    processFrame: false, hidden: false, inRoomTransition: true,
                    legacy: false)
                && !MphRead.Entities.RoomEntity.RefreshRoomVisibilityOnDraw(
                    processFrame: false, hidden: true, inRoomTransition: false,
                    legacy: false)
                && MphRead.Entities.RoomEntity.RefreshRoomVisibilityOnDraw(
                    processFrame: true, hidden: true, inRoomTransition: false,
                    legacy: false),
                "per-render portal visibility refresh preserves tick-only audio and transition fallback");
            var bindingCache = new OpenGlScopedBindingCache();
            Check(bindingCache.ShouldActivateTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0)
                && bindingCache.ShouldBindTexture(
                    OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 19),
                "binding cache forwards every request outside a world graph");
            bindingCache.Begin();
            Check(bindingCache.ShouldBindTexture(
                    OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 19)
                && bindingCache.ShouldActivateTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0)
                && !bindingCache.ShouldActivateTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0)
                && bindingCache.ShouldBindTexture(
                    OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 19)
                && !bindingCache.ShouldBindTexture(
                    OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 19)
                && bindingCache.ShouldActivateTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture1)
                && bindingCache.ShouldBindTexture(
                    OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 19),
                "per-unit texture cache preserves unknown first binds and independent units");
            Check(bindingCache.ShouldCullFace(OpenTK.Graphics.OpenGL.TriangleFace.Back)
                && !bindingCache.ShouldCullFace(OpenTK.Graphics.OpenGL.TriangleFace.Back)
                && bindingCache.ShouldLineWidth(1)
                && !bindingCache.ShouldLineWidth(1),
                "raster state cache elides only exact redundant values");
            bindingCache.Invalidate();
            Check(bindingCache.ShouldActivateTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture1)
                && bindingCache.ShouldBindTexture(
                    OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 19),
                "unknown display-list/attribute state invalidates texture inference");
            bindingCache.End();
            Check(bindingCache.ShouldCullFace(OpenTK.Graphics.OpenGL.TriangleFace.Back),
                "binding state cache does not escape the world graph");
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
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "compiling GL lists invalidates prior inferred capability state");
            capabilityCache.CallList(12);
            Check(!capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "known geometry-only display list preserves inferred world capability");
            capabilityCache.DeleteLists(12, 1);
            capabilityCache.CallList(12);
            Check(capabilityCache.ShouldSubmit(OpenTK.Graphics.OpenGL.EnableCap.CullFace, false),
                "reclaimed display list IDs cannot inherit stale state purity");
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

#if !ANDROID
            Check(OpenGlFrameProfiler.OnePercentLowFromP99Milliseconds(20) == 50
                && OpenGlFrameProfiler.OnePercentLowFromP99Milliseconds(0) == 0,
                "interval-derived 1-percent low estimate requires a positive p99 frame interval");
#endif
            Check(OpenGlSubmissionProfiler.PercentileIndex(100, 50) == 49
                && OpenGlSubmissionProfiler.PercentileIndex(100, 95) == 94
                && OpenGlSubmissionProfiler.PercentileIndex(100, 99) == 98
                && OpenGlSubmissionProfiler.PercentileIndex(1, 99) == 0,
                "bounded GL CPU percentile sampling uses nearest rank");
            Check(RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.Opaque, false)
                && RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.RebuildDepth, true)
                && !RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.RebuildDepth, false),
                "depth batching has an independent ordered-draw fallback");
            Check(!RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.Decal, true)
                && !RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.MarkTranslucent, true)
                && !RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.TranslucentBehind, true)
                && !RetainedOpaqueBatchPolicy.CanBatchPass(WorldRenderPassKind.TranslucentFront, true),
                "depth batching cannot merge decal or translucent stencil passes");
            Check(OpenGlOptimizationPolicy.DefaultEnabled("DEPTH_BATCH")
                && !OpenGlOptimizationPolicy.Select(true, false, "0", validatedDefault: true)
                && !OpenGlOptimizationPolicy.Select(true, true, "1", validatedDefault: true),
                "depth batching defaults on with explicit disable and forced legacy precedence");

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
            sortableB.TextureBindingId = sortableA.TextureBindingId;
            world.Capture(new List<RenderItem> { sortableA, sortableB },
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(RetainedOpaqueBatchPolicy.CanMerge(world.Opaque[0], world.Opaque[1]),
                "uniform-identical static opaque room packets qualify for one indexed draw");
            sortableB.Transform = Matrix4.CreateTranslation(3f, 0f, 0f);
            world.Capture(new List<RenderItem> { sortableA, sortableB },
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(!RetainedOpaqueBatchPolicy.CanMerge(world.Opaque[0], world.Opaque[1]),
                "different world transforms prevent combined opaque geometry");
            sortableB.Transform = sortableA.Transform;
            sortableB.RenderMode = RenderMode.Translucent;
            world.Capture(new List<RenderItem> { sortableA, sortableB },
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(!RetainedOpaqueBatchPolicy.CanMerge(world.Opaque[0], world.Opaque[1]),
                "translucent/stencil packets are never combined");

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

            if (OpenGlOptimizationPolicy.Enabled("STATIC_CACHE"))
            {
                var cachedWorld = new RetainedRenderWorld();
                var current = new List<RenderItem>();
                for (int i = 0; i < 8; i++) current.Add(new RenderItem
                {
                    Type = RenderItemType.Mesh, ListId = 100 + i, HasTexture = true,
                    TextureBindingId = 20 + (i % 3), RetainedRoomOwned = true,
                    Alpha = 1, Diffuse = Vector3.One
                });
                cachedWorld.Capture(current, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
                int sortRuns = cachedWorld.OpaqueSortRunCount;
                int reordered = cachedWorld.OpaqueReorderedPacketCount;
                // Replace all item identities and transforms while retaining exactly
                // the keys/flags on which ordering depends.
                for (int i = 0; i < 8; i++) current[i] = new RenderItem
                {
                    Type = RenderItemType.Mesh, ListId = 200 + i, HasTexture = true,
                    TextureBindingId = 20 + (i % 3), RetainedRoomOwned = true,
                    Alpha = 1, Diffuse = Vector3.One, Transform = Matrix4.CreateTranslation(i, 2, 3)
                };
                cachedWorld.Capture(current, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
                bool fresh = cachedWorld.OpaqueSortOrderReused;
                foreach (RetainedDrawPacket packet in cachedWorld.Opaque)
                    fresh &= ReferenceEquals(packet.Item, current[packet.Sequence])
                        && packet.Item.Transform == current[packet.Sequence].Transform;
                Check(fresh && cachedWorld.OpaqueSortRunCount == sortRuns
                    && cachedWorld.OpaqueReorderedPacketCount == reordered,
                    "cached opaque permutation uses current-frame items/poses and preserves diagnostics");
                current[0].TextureBindingId++;
                cachedWorld.Capture(current, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
                Check(!cachedWorld.OpaqueSortOrderReused, "changed state key invalidates opaque permutation");
                current[4].RetainedRoomOwned = false;
                cachedWorld.Capture(current, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
                Check(!cachedWorld.OpaqueSortOrderReused && ReferenceEquals(cachedWorld.Opaque[4].Item, current[4]),
                    "new sorting barrier invalidates opaque permutation without crossing barrier");
                current.Add(new RenderItem { Type = RenderItemType.Mesh, Alpha = 1 });
                cachedWorld.Capture(current, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
                Check(!cachedWorld.OpaqueSortOrderReused, "packet count change invalidates opaque permutation");
                cachedWorld.Clear();
                cachedWorld.Capture(current, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
                Check(!cachedWorld.OpaqueSortOrderReused, "scene clear drops sort proof and permutation");
            }

            var descriptorWorld = new RetainedRenderWorld(roomCacheEnabled: true);
            var roomItem = new RenderItem { Type = RenderItemType.Mesh, ListId = 501,
                RetainedRoomOwned = true, Alpha = 1, Diffuse = Vector3.One };
            RenderItem[] currentRoom = { roomItem };
            void CaptureDescriptorWorld() => descriptorWorld.Capture(currentRoom,
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            CaptureDescriptorWorld();
            RetainedMeshDescriptor firstDescriptor = descriptorWorld.Opaque[0].Mesh;
            Check(descriptorWorld.MeshDescriptorLookups == 1 && descriptorWorld.RoomDescriptorCacheHits == 0,
                "room descriptor first capture interns exact geometry key");
            CaptureDescriptorWorld();
            Check(ReferenceEquals(firstDescriptor, descriptorWorld.Opaque[0].Mesh)
                && descriptorWorld.RoomDescriptorCacheHits == 1 && descriptorWorld.MeshDescriptorLookups == 0,
                "unchanged room descriptor avoids the interning dictionary");
            Action[] keyChanges = {
                () => roomItem.ListId++,
                () => roomItem.CullingMode = CullingMode.Front,
                () => roomItem.BillboardMode = BillboardMode.Cylinder,
                () => roomItem.Type = RenderItemType.Quad,
                () => roomItem.ViewModel = true,
                () => roomItem.Wireframe = true
            };
            string[] keyNames = { "list ID", "culling", "billboard", "type", "viewmodel", "wireframe" };
            for (int i = 0; i < keyChanges.Length; i++)
            {
                RetainedMeshDescriptor before = descriptorWorld.Opaque[0].Mesh;
                keyChanges[i](); CaptureDescriptorWorld();
                Check(!ReferenceEquals(before, descriptorWorld.Opaque[0].Mesh)
                    && descriptorWorld.Opaque[0].Mesh.Key == RetainedMeshDescriptor.KeyOf(roomItem)
                    && descriptorWorld.MeshDescriptorLookups == 1 && descriptorWorld.RoomDescriptorCacheHits == 0,
                    "room descriptor key change invalidates " + keyNames[i]);
            }
            RetainedMeshDescriptor stableMesh = descriptorWorld.Opaque[0].Mesh;
            descriptorWorld.Capture(Array.Empty<RenderItem>(), Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(descriptorWorld.PacketCount == 0, "descriptor cache never resurrects invisible room packets");
            roomItem.Transform = Matrix4.CreateTranslation(3, 4, 5);
            roomItem.TexcoordMatrix = Matrix4.CreateScale(2, 3, 1);
            roomItem.Alpha = .35f; roomItem.Diffuse = new Vector3(.2f, .3f, .4f);
            roomItem.MatrixStackCount = 1; roomItem.MatrixStack[0] = .75f;
            CaptureDescriptorWorld();
            Check(ReferenceEquals(stableMesh, descriptorWorld.Opaque[0].Mesh)
                && ReferenceEquals(roomItem, descriptorWorld.Opaque[0].Item)
                && descriptorWorld.Opaque[0].Material.Alpha == .35f
                && descriptorWorld.Opaque[0].Material.Diffuse == roomItem.Diffuse
                && descriptorWorld.Opaque[0].Material.TexcoordMatrix == roomItem.TexcoordMatrix
                && descriptorWorld.Opaque[0].Item.Transform == roomItem.Transform
                && descriptorWorld.Opaque[0].Item.MatrixStack[0] == .75f,
                "descriptor reuse retains fresh visibility, material, UV and pose data");
            object? firstOwner = roomItem.CachedMeshOwner;
            var otherWorld = new RetainedRenderWorld(roomCacheEnabled: true);
            otherWorld.Capture(currentRoom, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(!ReferenceEquals(stableMesh, otherWorld.Opaque[0].Mesh)
                && !ReferenceEquals(firstOwner, roomItem.CachedMeshOwner)
                && roomItem.CachedMeshOwner?.GetType() == typeof(object),
                "owner-isolated descriptor token cannot retain another scene/world");
            CaptureDescriptorWorld();
            Check(ReferenceEquals(stableMesh, descriptorWorld.Opaque[0].Mesh)
                && descriptorWorld.MeshDescriptorLookups == 1,
                "returning to original owner revalidates through its own interning table");
            descriptorWorld.Clear();
            Check(descriptorWorld.PacketCount == 0 && descriptorWorld.MeshDescriptorCount == 0,
                "world clear releases descriptor dictionary and packet references");
            CaptureDescriptorWorld();
            Check(!ReferenceEquals(stableMesh, descriptorWorld.Opaque[0].Mesh)
                && descriptorWorld.MeshDescriptorLookups == 1 && descriptorWorld.RoomDescriptorCacheHits == 0,
                "clear invalidates invisible item cache tokens before reuse");
            var fallbackWorld = new RetainedRenderWorld(roomCacheEnabled: false);
            fallbackWorld.Capture(currentRoom, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            fallbackWorld.Capture(currentRoom, Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(fallbackWorld.RoomDescriptorCacheHits == 0 && fallbackWorld.MeshDescriptorLookups == 1
                && roomItem.CachedMeshOwner == null && roomItem.CachedMeshDescriptor == null,
                "disabled room cache uses ordinary interning without retaining owner stamps");
            roomItem.RetainedRoomOwned = false;
            CaptureDescriptorWorld(); CaptureDescriptorWorld();
            Check(descriptorWorld.RoomDescriptorCacheHits == 0 && descriptorWorld.MeshDescriptorLookups == 1,
                "dynamic/procedural ownership never uses retained room descriptor cache");

            var planItemA = new RenderItem { Type = RenderItemType.Mesh, ListId = 701,
                RetainedRoomOwned = true, Alpha = 1, Diffuse = Vector3.One, Transform = Matrix4.Identity,
                MatrixStackCount = 1 };
            var planItemB = new RenderItem { Type = RenderItemType.Mesh, ListId = 702,
                RetainedRoomOwned = true, Alpha = 1, Diffuse = Vector3.One, Transform = Matrix4.Identity,
                MatrixStackCount = 1 };
            planItemA.MatrixStack[0] = planItemB.MatrixStack[0] = 1;
            var planPackets = new RetainedDrawPacket[2];
            void RefreshPlanPackets()
            {
                planPackets[0] = new RetainedDrawPacket(planItemA, 0,
                    new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(planItemA)));
                planPackets[1] = new RetainedDrawPacket(planItemB, 1,
                    new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(planItemB)));
            }
            RetainedDrawBatch PlanBatch(int count = 2) => new(0, count, planPackets[0].BatchState);
            RefreshPlanPackets();
            var roomPlan = new RetainedRoomBatchPlan();
            ulong planRevision = 1;
            Check(roomPlan.Prepare(planRevision, planPackets, PlanBatch())
                && roomPlan.Count == 2 && roomPlan.Ids[0] == 701 && roomPlan.Ids[1] == 702,
                "room batch plan captures exact current opaque membership");
            int[] firstIds = roomPlan.Ids; ulong firstHash = roomPlan.Hash;
            Check(roomPlan.Prepare(planRevision, planPackets, PlanBatch())
                && ReferenceEquals(firstIds, roomPlan.Ids) && roomPlan.Hash == firstHash,
                "opaque and depth replay share one execution proof and ID array");
            planItemB.Transform = Matrix4.CreateTranslation(1, 0, 0);
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch())
                && roomPlan.Count == 0 && !roomPlan.Mergeable,
                "new execution rejects changed current transform");
            Check(!roomPlan.Prepare(planRevision, planPackets, PlanBatch()),
                "failed merge proof is reused safely for depth replay");
            planItemB.Transform = Matrix4.Identity;
            RefreshPlanPackets();
            Check(roomPlan.Prepare(++planRevision, planPackets, PlanBatch())
                && ReferenceEquals(firstIds, roomPlan.Ids), "restored transform renews proof without reallocating IDs");
            planItemB.LightInfo = new LightInfo(Vector3.UnitX, Vector3.One, Vector3.UnitY, Vector3.One);
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch()),
                "current light vectors and colors participate in renewed proof");
            planItemB.LightInfo = planItemA.LightInfo;
            planItemB.MatrixStack[0] = 2;
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch()),
                "unknown-version palette contents are compared exactly");
            planItemB.MatrixStack[0] = planItemA.MatrixStack[0];
            planItemB.MatrixStackCount = 2;
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch()),
                "current palette length invalidates plan even with matching first matrix");
            planItemB.MatrixStackCount = 1;
            planItemB.ListId = 703;
            RefreshPlanPackets();
            Check(roomPlan.Prepare(++planRevision, planPackets, PlanBatch())
                && roomPlan.Ids[1] == 703 && roomPlan.Hash != firstHash,
                "changed membership updates ID sequence and hash on next execution");
            ulong changedHash = roomPlan.Hash;
            (planPackets[0], planPackets[1]) = (planPackets[1], planPackets[0]);
            Check(roomPlan.Prepare(++planRevision, planPackets, PlanBatch())
                && roomPlan.Ids[0] == 703 && roomPlan.Ids[1] == 701 && roomPlan.Hash != changedHash,
                "ordered ID hash never treats reversed draw membership as same sequence");
            RefreshPlanPackets();
            planItemB.OverrideColor = Vector4.One;
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch()),
                "presentation override is a hard indexed batch proof barrier");
            planItemB.OverrideColor = null; planItemB.RenderMode = RenderMode.Translucent;
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch()),
                "translucent material cannot enter opaque cached batch plan");
            planItemB.RenderMode = RenderMode.Normal;
            RefreshPlanPackets();
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch(1)),
                "one-item run uses ordinary draw path");
            Check(!roomPlan.Prepare(++planRevision, planPackets, PlanBatch(RetainedRoomBatchPlan.MaxIds + 1)),
                "oversize plan falls back before copying or retaining unbounded IDs");
            var maxPackets = new RetainedDrawPacket[RetainedRoomBatchPlan.MaxIds];
            for (int i = 0; i < maxPackets.Length; i++) maxPackets[i] = planPackets[i % 2];
            var boundedPlan = new RetainedRoomBatchPlan();
            Check(boundedPlan.Prepare(1, maxPackets, PlanBatch(maxPackets.Length))
                && boundedPlan.Ids.Length == RetainedRoomBatchPlan.MaxIds,
                "maximum bounded plan preserves every packet ID");
            Check(roomPlan.Prepare(++planRevision, planPackets, PlanBatch()),
                "plan remains reusable after failure and size fallback");
            long planAllocated = GC.GetAllocatedBytesForCurrentThread();
            bool allPlanProofs = true;
            RetainedDrawBatch warmedPlanBatch = PlanBatch();
            for (int i = 0; i < 100; i++)
            {
                allPlanProofs &= roomPlan.Prepare(++planRevision, planPackets, warmedPlanBatch);
                allPlanProofs &= roomPlan.Prepare(planRevision, planPackets, warmedPlanBatch);
            }
            Check(allPlanProofs && GC.GetAllocatedBytesForCurrentThread() == planAllocated,
                "warmed proof renewal and replay reuse allocate no managed storage");

            RetainedDrawPacket GroupPacket(int id, bool room = true, float x = 0)
            {
                var item = new RenderItem { Type = RenderItemType.Mesh, ListId = id, RetainedRoomOwned = room,
                    Alpha = 1f, PolygonMode = PolygonMode.Modulate, RenderMode = RenderMode.Normal,
                    Transform = Matrix4.CreateTranslation(x, 0, 0) };
                return new(item, id, new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(item)));
            }
            var subsetPackets = new[] { GroupPacket(1), GroupPacket(2), GroupPacket(3, x: 1),
                GroupPacket(4, room: false), GroupPacket(5), GroupPacket(6) };
            var subsetBatches = new List<RetainedDrawBatch>();
            RetainedRenderWorld.BuildAdjacentBatches(subsetPackets, subsetBatches, roomGroups: true);
            Check(subsetBatches.Count == 4 && subsetBatches[0].Count == 2 && subsetBatches[1].Count == 1
                && subsetBatches[2].Count == 1 && subsetBatches[3].Count == 2,
                "room groups retain compatible subsets across pose and dynamic barriers");
            int cursor = 0; bool ordered = true;
            foreach (var group in subsetBatches) { ordered &= group.Start == cursor; cursor += group.Count; }
            Check(ordered && cursor == subsetPackets.Length, "room partition emits each original packet once in order including singletons");
            subsetBatches.Clear();
            RetainedRenderWorld.BuildAdjacentBatches(subsetPackets, subsetBatches, roomGroups: false);
            Check(subsetBatches.Count == 1 && subsetBatches[0].Count == subsetPackets.Length,
                "disabled or nonopaque partition retains ordinary adjacent material grouping");
            var groupBoundPackets = new RetainedDrawPacket[RetainedRoomBatchPlan.MaxIds + 2];
            for (int i = 0; i < groupBoundPackets.Length; i++) groupBoundPackets[i] = GroupPacket(i + 1);
            subsetBatches.Clear(); RetainedRenderWorld.BuildAdjacentBatches(groupBoundPackets, subsetBatches, roomGroups: true);
            Check(subsetBatches.Count == 2 && subsetBatches[0].Count == RetainedRoomBatchPlan.MaxIds
                && subsetBatches[1].Count == 2 && subsetBatches[1].Start == RetainedRoomBatchPlan.MaxIds,
                "room group size cap preserves the ordered remainder");
            var translucentBarrier = GroupPacket(2); translucentBarrier.Item.Alpha = .5f;
            translucentBarrier = new(translucentBarrier.Item, 2, translucentBarrier.Mesh);
            subsetBatches.Clear(); RetainedRenderWorld.BuildAdjacentBatches(new[] { GroupPacket(1), translucentBarrier, GroupPacket(3) }, subsetBatches, roomGroups: true);
            Check(subsetBatches.Count == 3, "translucent material cannot join opaque room groups");

            Console.WriteLine(failures == 0
                ? "[rendergraphcheck] PASS"
                : $"[rendergraphcheck] FAIL: {failures} check(s)");
            return failures == 0 ? 0 : 1;
        }
    }
}
#endif
