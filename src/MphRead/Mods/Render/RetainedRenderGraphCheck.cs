#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
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
                "world graph validates" + (error.Length == 0 ? "" : ": " + error));
            Check(FrameRenderGraph.Validate(out string frameError),
                "top-level frame render graph validates"
                    + (frameError.Length == 0 ? "" : ": " + frameError));
            var frameGraph = new FrameRenderGraph();
            Check(frameGraph.Passes.Count > 5
                && frameGraph.Passes[1].Kind == FrameRenderPassKind.WorldSetup
                && frameGraph.Passes[2].Kind == FrameRenderPassKind.StaticOcclusionDepth
                && frameGraph.Passes[3].Kind == FrameRenderPassKind.GpuHiZBuild
                && (frameGraph.Passes[3].Reads & FrameRenderResource.SceneDepth) != 0
                && (frameGraph.Passes[3].Writes & FrameRenderResource.HiZ) != 0
                && frameGraph.Passes[4].Kind == FrameRenderPassKind.GpuVisibility
                && (frameGraph.Passes[4].Reads & FrameRenderResource.HiZ) != 0
                && (frameGraph.Passes[4].Writes & FrameRenderResource.Visibility) != 0
                && frameGraph.Passes[5].Kind == FrameRenderPassKind.World,
                "GPU visibility consumes current static depth before final World");
            FrameRenderResource depthStencil = FrameRenderResource.SceneDepth
                | FrameRenderResource.SceneStencil;
            Check((frameGraph.Passes[2].Writes & depthStencil) == depthStencil
                && (frameGraph.Passes[2].Writes & FrameRenderResource.SceneColor) == 0,
                "static occluder replay declares depth/stencil writes while preserving scene color");
            Check((frameGraph.Passes[4].Writes & depthStencil) == depthStencil
                && (frameGraph.Passes[4].Writes & FrameRenderResource.Visibility) != 0,
                "visibility stage declares depth/stencil reset before final World");
            var worldGraph = new WorldRenderGraph();
            Check(worldGraph.Passes[0].Kind == WorldRenderPassKind.Opaque
                && worldGraph.Passes[1].Kind == WorldRenderPassKind.DeferredPbr
                && worldGraph.Passes[2].Kind == WorldRenderPassKind.ForwardOpaque
                && worldGraph.Passes[3].Kind == WorldRenderPassKind.Decal
                && worldGraph.Passes[6].Kind == WorldRenderPassKind.TranslucentBehind
                && worldGraph.Passes[^1].Kind == WorldRenderPassKind.TranslucentSingle
                && (worldGraph.Passes[^1].Reads & WorldRenderResource.Stencil) == 0,
                "native transparency remains ordered and fast transparency needs only depth");
            Check((frameGraph.Passes[^2].Reads & (FrameRenderResource.PbrAlbedo
                    | FrameRenderResource.PbrNormal | FrameRenderResource.PbrMaterial)) == 0,
                "final post-process cannot apply background PBR over forward layers");
            Check(Scene.SelectDeferredPbrDepthTexture(true, 17, 23) == 17
                && Scene.SelectDeferredPbrDepthTexture(false, 17, 23) == 23
                && Scene.SelectDeferredPbrDepthTexture(true, 0, 23) == 0,
                "reduced G-buffer samples its owned depth and never substitutes forward depth");
            Check(!Scene.UseFinalSceneDepth(true, true, true)
                && Scene.UseFinalSceneDepth(true, true, false)
                && !Scene.UseFinalSceneDepth(false, true, false)
                && !Scene.UseFinalSceneDepth(true, false, false),
                "resolved world depth effects cannot run again over foreground in final presentation");
            Check(Scene.SelectSceneColorFormat(false, true, true, false) == PixelInternalFormat.Rgba16f
                && Scene.SelectSceneColorFormat(true, true, false, false) == PixelInternalFormat.Rgba16f
                && Scene.SelectSceneColorFormat(false, true, false, false) == PixelInternalFormat.Rgb
                && Scene.SelectSceneColorFormat(false, true, true, true) == PixelInternalFormat.Rgb
                && Scene.SelectSceneColorFormat(true, false, true, false) == PixelInternalFormat.Rgb,
                "HDR scene storage preserves float values on validated GL and modern backends");
            Check(Scene.IsDeferredPbrOpaqueSurface(new RenderItem
                { Type = RenderItemType.Mesh, Alpha = 1 })
                && !Scene.IsDeferredPbrOpaqueSurface(new RenderItem
                    { Type = RenderItemType.Mesh, Alpha = 1, ViewModel = true })
                && !Scene.IsDeferredPbrOpaqueSurface(new RenderItem
                    { Type = RenderItemType.Mesh, Alpha = 0.5f })
                && !Scene.IsDeferredPbrOpaqueSurface(new RenderItem
                    { Type = RenderItemType.Mesh, Alpha = 0.999f })
                && !Scene.IsDeferredPbrOpaqueSurface(new RenderItem
                    { Type = RenderItemType.Mesh, Alpha = 1, RenderMode = RenderMode.Translucent })
                && !Scene.IsDeferredPbrOpaqueSurface(new RenderItem
                    { Type = RenderItemType.TrailSingle, Alpha = 1 }),
                "G-buffer owns opaque world meshes, excluding foreground and effect layers");
            Check(ModernGraphicsCompat.ValidateRetainedWorldUniformLayout(
                    out string layoutError),
                "retained World generated uniform layout validates"
                    + (layoutError.Length == 0 ? "" : ": " + layoutError));
            Check(ModernGraphicsCompat.ValidateRetainedDeferredPbrLayout(
                    out string pbrLayoutError),
                "retained DeferredPbrMrt generated uniform layout validates"
                    + (pbrLayoutError.Length == 0 ? "" : ": " + pbrLayoutError));
            Check(ModernGraphicsCompat.ValidateGpuVisibilityResourceLayoutsForCheck(),
                "Hi-Z compute layouts use unfilterable R32Float with matching depth/storage declarations");

            // Every original texel must survive into the final max pyramid.
            // In particular, a clear-depth final texel may never disappear:
            // doing so can falsely classify a visible object as occluded.
            bool hiZCoverage = true;
            foreach (int size in new[] { 1, 2, 3, 5, 7, 15, 31, 63, 127, 255,
                1080, 1440, 1920, 2560, 3840 })
            {
                int currentSize = size;
                float[] current = new float[currentSize];
                Array.Fill(current, 0.25f);
                current[^1] = 1f;
                do
                {
                    int nextSize = Math.Max(1, currentSize / 2);
                    float[] next = new float[nextSize];
                    int[] covered = new int[currentSize];
                    for (int dst = 0; dst < nextSize; dst++)
                    {
                        (int start, int end) =
                            ModernGraphicsCompat.GpuHiZReductionFootprint(
                                currentSize, dst);
                        // Production uses normalized footprints. An odd source
                        // texel can straddle two destination cells, so both must
                        // retain it for conservative depth rejection.
                        hiZCoverage &= start==(int)Math.Floor(dst*(double)currentSize/nextSize)
                            && end==(int)Math.Ceiling((dst+1)*(double)currentSize/nextSize);
                        for (int src = start; src < end; src++)
                        {
                            covered[src]++;
                            next[dst] = Math.Max(next[dst], current[src]);
                        }
                    }
                    foreach (int count in covered)
                        hiZCoverage &= count is >=1 and <=2;
                    for(int src=0;src<currentSize;src++)
                        for(int quarter=0;quarter<4;quarter++)
                        {
                            // Shader hiz_at maps pixel/viewport to mip size,
                            // then clamps the resulting destination texel.
                            int lookup=Math.Clamp((int)(((4L*src+quarter)*nextSize)/(4L*currentSize)),0,nextSize-1);
                            var footprint=ModernGraphicsCompat.GpuHiZReductionFootprint(currentSize,lookup);
                            hiZCoverage &= footprint.Start<=src && src<footprint.End;
                        }
                    var first=ModernGraphicsCompat.GpuHiZReductionFootprint(currentSize,0);
                    var last=ModernGraphicsCompat.GpuHiZReductionFootprint(currentSize,nextSize-1);
                    hiZCoverage &= first.Start==0 && last.End==currentSize;
                    hiZCoverage &= next[^1] == 1f;
                    current = next;
                    currentSize = nextSize;
                } while (currentSize > 1);
                hiZCoverage &= current[0] == 1f;
            }
            Check(hiZCoverage,
                "Hi-Z max pyramid preserves odd edges and clamped lookup coverage");

            Check(!ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.DepthBufferBit, false, false, true, 0),
                "depth rebuild uses attachment clear despite unrelated color/stencil masks");
            Check(!ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.ColorBufferBit, false, true, false, 0),
                "full color clear ignores unrelated depth/stencil write masks");
            Check(!ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.StencilBufferBit, false, false, false, 0xFF)
                && !ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.StencilBufferBit, false, false, false, -1),
                "both 0xff and -1 fully clear an eight-bit stencil attachment");
            Check(ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.ColorBufferBit, false, false, true, -1)
                && ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.DepthBufferBit, false, true, false, -1)
                && ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.StencilBufferBit, false, true, true, 0x0F),
                "partial color, disabled depth and partial stencil clear preserve write masks");
            Check(ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit,
                    true, true, true, -1)
                && ModernGraphicsCompat.NeedsPartialClear(
                    ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit
                        | ClearBufferMask.StencilBufferBit,
                    false, true, true, 0x0F),
                "scissor and a masked requested attachment retain draw-based clears");

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
            RenderOptions.Translucency = TranslucencyMode.Native;
            Check(world.GraphStateReuseCount >= 2,
                "native transparency counts opaque reuse in opaque and depth passes");
            int nativeApplications = world.GraphStateApplicationCount;
            RenderOptions.Translucency = TranslucencyMode.Fast;
            Check(world.GraphStateApplicationCount < nativeApplications,
                "fast transparency removes mask, depth rebuild and second translucent draw");
            RenderOptions.Translucency = TranslucencyMode.Native;

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
            Check(world.OpaqueBatches.Count == 1 && world.OpaqueBatches[0].Count == 2,
                "compatible packets remain one adjacent batch next frame");
            Check(world.MeshDescriptorCount == descriptorCount,
                "mesh descriptor table is retained rather than rebuilt per frame");

            // DoMaterial uploads emissive intensity as shared material state.
            // Distinct animated values must break a batch on every backend,
            // including compatibility draws that skip the second DoMaterial.
            opaqueA.EmissiveIntensity = 0.25f;
            opaqueB.EmissiveIntensity = 2f;
            world.Capture(new[] { opaqueA, opaqueB },
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(world.OpaqueBatches.Count == 2
                && world.Opaque[0].Material != world.Opaque[1].Material
                && world.Opaque[0].StateKey != world.Opaque[1].StateKey,
                "different emissive animation intensities split material batches");
            RetainedMaterialDescriptor firstEmissive = world.Opaque[0].Material;
            ulong firstEmissiveKey = world.Opaque[0].StateKey;
            opaqueB.EmissiveIntensity = opaqueA.EmissiveIntensity;
            world.Capture(new[] { opaqueA, opaqueB },
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(world.OpaqueBatches.Count == 1
                && world.OpaqueBatches[0].Count == 2,
                "matching emissive animation intensities share material state");
            opaqueA.EmissiveIntensity = 1f;
            world.Capture(new[] { opaqueA, opaqueB },
                Array.Empty<RenderItem>(), Array.Empty<RenderItem>());
            Check(world.OpaqueBatches.Count == 2
                && world.Opaque[0].Material != firstEmissive
                && world.Opaque[0].StateKey != firstEmissiveKey,
                "emissive animation changes refresh the next frame's batch identity");

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

            var denseStateA = new RenderItem
            {
                MatrixStackCount = 0,
                Transform = Matrix4.Identity,
                LightInfo = new LightInfo(
                    Vector3.UnitX, Vector3.One,
                    Vector3.UnitY, new Vector3(.5f))
            };
            var denseStateB = new RenderItem
            {
                MatrixStackCount = 0,
                Transform = Matrix4.Identity,
                LightInfo = denseStateA.LightInfo
            };
            Check(ModernGraphicsCompat.RetainedMultiDrawDynamicStateEquivalent(
                    denseStateA, denseStateB),
                "dense multi-draw accepts bit-identical light/transform state");
            denseStateB.Transform =
                Matrix4.CreateTranslation(1, 0, 0);
            Check(!ModernGraphicsCompat.RetainedMultiDrawDynamicStateEquivalent(
                    denseStateA, denseStateB),
                "dense multi-draw splits different room transforms");
            denseStateB.Transform = Matrix4.Identity;
            denseStateB.LightInfo = new LightInfo(
                Vector3.UnitX, Vector3.One,
                Vector3.UnitZ, new Vector3(.5f));
            Check(!ModernGraphicsCompat.RetainedMultiDrawDynamicStateEquivalent(
                    denseStateA, denseStateB),
                "dense multi-draw splits different room lighting");

            var direct = new RenderItem
            {
                Type = RenderItemType.Mesh,
                RenderMode = RenderMode.Normal,
                BillboardMode = BillboardMode.None,
                Alpha = 1,
                MatrixStackCount = 0,
                Diffuse = Vector3.One
            };
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "plain opaque mesh is eligible for direct modern submission");
            direct.WeightedSkinning = true;
            Check(!ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "Weighted4 mesh stays on compatibility submission until direct parity is implemented");
            direct.WeightedSkinning = false;

            direct.ViewModel = true;
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "plain viewmodel mesh is eligible for direct submission");
            direct.ViewModel = false;
            direct.BillboardMode = BillboardMode.Sphere;
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "spherical billboard is eligible for direct submission");
            direct.BillboardMode = BillboardMode.Cylinder;
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "cylindrical billboard is eligible for direct submission");
            direct.BillboardMode = (BillboardMode)255;
            Check(!ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "unknown billboard mode stays on compatibility executor");
            direct.BillboardMode = BillboardMode.None;
            direct.OverrideColor = new Vector4(1, 0, 0, 1);
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "explicit color override is eligible for direct submission");
            direct.OverrideColor = null;
            direct.PaletteOverride = new Vector4(0, 1, 0, 1);
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "palette override is eligible for direct submission");
            direct.PaletteOverride = null;
            direct.TexturedPlayerSkin = true;
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "textured-player skin is eligible for direct submission");
            direct.TexturedPlayerSkin = false;
            direct.PlayerOutlineColor = new Vector4(0, 1, 1, 1);
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "outlined player's normal world draw is direct eligible");
            direct.PlayerOutlineColor = null;
            direct.RenderMode = RenderMode.Translucent;
            Check(!ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "translucent mesh stays off opaque direct eligibility");
            Check(ModernGraphicsCompat.RetainedWorldPacketEligibleForPass(
                    direct, WorldRenderPassKind.MarkTranslucent)
                && ModernGraphicsCompat.RetainedWorldPacketEligibleForPass(
                    direct, WorldRenderPassKind.TranslucentBehind)
                && ModernGraphicsCompat.RetainedWorldPacketEligibleForPass(
                    direct, WorldRenderPassKind.TranslucentFront),
                "translucent mesh is direct eligible in all retained transparency passes");
            direct.RenderMode = RenderMode.Decal;
            Check(ModernGraphicsCompat.RetainedWorldPacketEligibleForPass(
                    direct, WorldRenderPassKind.Decal),
                "decal mesh is direct eligible in retained decal pass");
            direct.RenderMode = RenderMode.Normal;
            direct.MatrixStackCount = direct.MatrixStack.Length / 16;
            Check(direct.MatrixStackCount == 32
                && ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "full 32-matrix shader palette is eligible for ordinary direct submission");
            direct.MatrixStackCount = direct.MatrixStack.Length / 16 + 1;
            Check(!ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "out-of-range matrix stack stays on compatibility executor");
            direct.MatrixStackCount = 0;
            direct.Alpha = 0.5f;
            Check(!ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "alpha-blended normal mesh skips the opaque direct path");

            var pbrDirect = new RenderItem
            {
                Type = RenderItemType.Mesh,
                RenderMode = RenderMode.Normal,
                Alpha = 1,
                BillboardMode = BillboardMode.None
            };
            Check(ModernGraphicsCompat.RetainedDeferredPbrPacketEligible(pbrDirect),
                "plain opaque mesh is eligible for direct retained PBR MRT");
            pbrDirect.WeightedSkinning = true;
            Check(!ModernGraphicsCompat.RetainedDeferredPbrPacketEligible(pbrDirect),
                "Weighted4 mesh stays on compatibility PBR replay");
            pbrDirect.WeightedSkinning = false;
            pbrDirect.ViewModel = true;
            Check(!ModernGraphicsCompat.RetainedDeferredPbrPacketEligible(pbrDirect),
                "viewmodel stays off direct PBR MRT replay");
            pbrDirect.ViewModel = false;
            pbrDirect.Cosmetics = new Mods.Cosmetics.CosmeticSurface(
                1, 0, 0, Vector3.One, Vector3.One, 1, 0, 0);
            Check(!ModernGraphicsCompat.RetainedDeferredPbrPacketEligible(pbrDirect),
                "cosmetic surface stays on compatibility PBR replay");
            pbrDirect.Cosmetics = default;
            pbrDirect.Alpha = 0.5f;
            Check(!ModernGraphicsCompat.RetainedDeferredPbrPacketEligible(pbrDirect),
                "alpha-blended mesh stays off direct PBR MRT replay");
            pbrDirect.Alpha = 0.999f;
            Check(!ModernGraphicsCompat.RetainedDeferredPbrPacketEligible(pbrDirect),
                "near-opaque mesh cannot bypass the PBR surface filter in a multidraw batch");

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
