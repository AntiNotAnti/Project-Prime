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
            Check(FrameRenderGraph.Validate(out string frameError),
                "top-level frame render graph validates"
                    + (frameError.Length == 0 ? "" : ": " + frameError));
            var frameGraph = new FrameRenderGraph();
            Check(frameGraph.Passes.Count > 2
                && frameGraph.Passes[1].Kind == FrameRenderPassKind.GpuVisibility
                && (frameGraph.Passes[1].Reads & FrameRenderResource.SceneDepth) != 0
                && (frameGraph.Passes[1].Writes
                    & (FrameRenderResource.HiZ | FrameRenderResource.Visibility))
                    == (FrameRenderResource.HiZ | FrameRenderResource.Visibility),
                "GPU visibility runs before world clear and owns Hi-Z/visibility resources");
            Check(ModernGraphicsCompat.ValidateRetainedWorldUniformLayout(
                    out string layoutError),
                "retained World generated uniform layout validates"
                    + (layoutError.Length == 0 ? "" : ": " + layoutError));
            Check(ModernGraphicsCompat.ValidateRetainedDeferredPbrLayout(
                    out string pbrLayoutError),
                "retained DeferredPbrMrt generated uniform layout validates"
                    + (pbrLayoutError.Length == 0 ? "" : ": " + pbrLayoutError));

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
            direct.MatrixStackCount = 1;
            Check(ModernGraphicsCompat.RetainedWorldPacketEligible(direct),
                "valid matrix-stack geometry is eligible for direct submission");
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
