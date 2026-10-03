using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    [Flags]
    internal enum WorldRenderResource
    {
        None = 0,
        Color = 1,
        Depth = 2,
        Stencil = 4
    }

    internal enum WorldRenderPassKind
    {
        Opaque,
        Decal,
        MarkTranslucent,
        RebuildDepth,
        TranslucentBehind,
        TranslucentFront
    }

    /// <summary>
    /// Immutable geometry/raster identity retained across frames. It deliberately
    /// excludes animated material values and transforms: those remain frame-local.
    /// </summary>
    internal readonly record struct RetainedMeshDescriptorKey(
        RenderItemType Type,
        int ListId,
        CullingMode CullingMode,
        BillboardMode BillboardMode,
        bool Wireframe,
        bool ViewModel);

    internal sealed class RetainedMeshDescriptor
    {
        internal RetainedMeshDescriptorKey Key { get; }
        internal RenderItemType Type => Key.Type;
        internal int ListId => Key.ListId;
        internal CullingMode CullingMode => Key.CullingMode;
        internal BillboardMode BillboardMode => Key.BillboardMode;
        internal bool Wireframe => Key.Wireframe;
        internal bool ViewModel => Key.ViewModel;

        internal RetainedMeshDescriptor(RetainedMeshDescriptorKey key)
        {
            Key = key;
        }

        internal static RetainedMeshDescriptorKey KeyOf(RenderItem item) =>
            new(item.Type, item.ListId, item.CullingMode, item.BillboardMode,
                item.Wireframe, item.ViewModel);
    }

    /// <summary>
    /// Exact frame-local material state consumed by DoMaterial/DoTexture. This
    /// stays immutable once captured so adjacent packets can prove that sharing
    /// a material-state application is safe without relying on a hash.
    /// </summary>
    internal readonly record struct RetainedMaterialDescriptor(
        MphRead.PolygonMode PolygonMode,
        bool Lighting,
        Vector3 Diffuse,
        Vector3 Ambient,
        Vector3 Specular,
        Vector3 Emission,
        float Alpha,
        TexgenMode TexgenMode,
        RepeatMode XRepeat,
        RepeatMode YRepeat,
        bool HasTexture,
        int TextureBindingId,
        Matrix4 TexcoordMatrix,
        Mods.Cosmetics.CosmeticSurface Cosmetics,
        Mods.Cosmetics.Skins.RenderMaterialOverride CosmeticMaterial,
        bool TexturedPlayerSkin,
        Vector4? OverrideColor,
        Vector4? PaletteOverride)
    {
        internal static RetainedMaterialDescriptor From(RenderItem item) =>
            new(item.PolygonMode, item.Lighting, item.Diffuse, item.Ambient,
                item.Specular, item.Emission, item.Alpha, item.TexgenMode,
                item.XRepeat, item.YRepeat, item.HasTexture, item.TextureBindingId,
                item.TexcoordMatrix, item.Cosmetics, item.CosmeticMaterial,
                item.TexturedPlayerSkin, item.OverrideColor, item.PaletteOverride);
    }

    internal readonly record struct RetainedBatchState(
        CullingMode CullingMode,
        bool Wireframe,
        RetainedMaterialDescriptor Material);

    /// <summary>
    /// Frame-local submission referencing an interned immutable mesh descriptor.
    /// Sequence is never changed; batching in this slice only joins adjacent
    /// compatible packets and therefore cannot perturb cartridge draw ordering.
    /// </summary>
    internal readonly struct RetainedDrawPacket
    {
        internal RenderItem Item { get; }
        internal int Sequence { get; }
        internal ulong StateKey { get; }
        internal RetainedMeshDescriptor Mesh { get; }
        internal RetainedMaterialDescriptor Material { get; }
        internal bool Batchable { get; }
        internal RetainedBatchState BatchState { get; }

        internal RetainedDrawPacket(RenderItem item, int sequence,
            RetainedMeshDescriptor mesh)
        {
            Item = item;
            Sequence = sequence;
            Mesh = mesh;
            Material = RetainedMaterialDescriptor.From(item);
            Batchable = item.Type == RenderItemType.Mesh
                && !item.ViewModel
                && item.BillboardMode == BillboardMode.None
                && item.Cosmetics == default
                && item.CosmeticMaterial == default
                && item.OverrideColor == null
                && item.PaletteOverride == null
                && !item.TexturedPlayerSkin
                && item.PlayerOutlineColor == null;
            BatchState = new RetainedBatchState(item.CullingMode, item.Wireframe,
                Material);
            StateKey = BuildStateKey(item);
        }

        private static ulong BuildStateKey(RenderItem item)
        {
            unchecked
            {
                ulong key = (uint)item.ListId;
                key = key * 1099511628211UL ^ (uint)item.TextureBindingId;
                key = key * 1099511628211UL ^ (uint)item.RenderMode;
                key = key * 1099511628211UL ^ (uint)item.CullingMode;
                key = key * 1099511628211UL ^ (item.HasTexture ? 1UL : 0UL);
                key = key * 1099511628211UL ^ (item.Lighting ? 1UL : 0UL);
                key = key * 1099511628211UL ^ (item.ViewModel ? 1UL : 0UL);
                return key;
            }
        }
    }

    internal readonly record struct RetainedDrawBatch(
        int Start,
        int Count,
        RetainedBatchState State);

    /// <summary>
    /// Reusable retained view of the frame's visible submissions. Mesh
    /// descriptors are interned for the scene lifetime; frame packets and
    /// adjacent batches retain list capacity and add no packet-object
    /// allocations after warmup.
    /// </summary>
    internal sealed class RetainedRenderWorld
    {
        private readonly Dictionary<RetainedMeshDescriptorKey, RetainedMeshDescriptor>
            _meshDescriptors = new();
        private readonly List<RetainedDrawPacket> _opaque = new(256);
        private readonly List<RetainedDrawPacket> _decals = new(64);
        private readonly List<RetainedDrawPacket> _translucent = new(128);
        private readonly List<RetainedDrawBatch> _opaqueBatches = new(128);
        private readonly List<RetainedDrawBatch> _decalBatches = new(32);
        private readonly List<RetainedDrawBatch> _translucentBatches = new(64);

        internal IReadOnlyList<RetainedDrawPacket> Opaque => _opaque;
        internal IReadOnlyList<RetainedDrawPacket> Decals => _decals;
        internal IReadOnlyList<RetainedDrawPacket> Translucent => _translucent;
        internal IReadOnlyList<RetainedDrawBatch> OpaqueBatches => _opaqueBatches;
        internal IReadOnlyList<RetainedDrawBatch> DecalBatches => _decalBatches;
        internal IReadOnlyList<RetainedDrawBatch> TranslucentBatches => _translucentBatches;
        internal int PacketCount => _opaque.Count + _decals.Count + _translucent.Count;
        internal int BatchCount => _opaqueBatches.Count + _decalBatches.Count
            + _translucentBatches.Count;
        internal int StateReuseCount => Reuses(_opaqueBatches)
            + Reuses(_decalBatches) + Reuses(_translucentBatches);
        // Opaque state is consumed by opaque + depth rebuild; translucent
        // state is consumed by mask + behind + front.
        internal int GraphStateApplicationCount => _opaqueBatches.Count * 2
            + _decalBatches.Count + _translucentBatches.Count * 3;
        internal int GraphStateReuseCount => Reuses(_opaqueBatches) * 2
            + Reuses(_decalBatches) + Reuses(_translucentBatches) * 3;
        internal int MeshDescriptorCount => _meshDescriptors.Count;
        internal ulong FrameRevision { get; private set; }

        internal void Capture(IReadOnlyList<RenderItem> nonDecal,
            IReadOnlyList<RenderItem> decals, IReadOnlyList<RenderItem> translucent)
        {
            CaptureList(_opaque, _opaqueBatches, nonDecal);
            CaptureList(_decals, _decalBatches, decals);
            CaptureList(_translucent, _translucentBatches, translucent);
            FrameRevision++;
        }

        internal void Clear()
        {
            _opaque.Clear();
            _decals.Clear();
            _translucent.Clear();
            _opaqueBatches.Clear();
            _decalBatches.Clear();
            _translucentBatches.Clear();
        }

        private void CaptureList(List<RetainedDrawPacket> destination,
            List<RetainedDrawBatch> batches, IReadOnlyList<RenderItem> source)
        {
            destination.Clear();
            batches.Clear();
            if (destination.Capacity < source.Count)
                destination.Capacity = source.Count;
            for (int i = 0; i < source.Count; i++)
            {
                RenderItem item = source[i];
                RetainedMeshDescriptorKey key = RetainedMeshDescriptor.KeyOf(item);
                if (!_meshDescriptors.TryGetValue(key, out RetainedMeshDescriptor mesh))
                {
                    mesh = new RetainedMeshDescriptor(key);
                    _meshDescriptors.Add(key, mesh);
                }
                destination.Add(new RetainedDrawPacket(item, i, mesh));
            }
            BuildAdjacentBatches(destination, batches);
        }

        private static void BuildAdjacentBatches(
            IReadOnlyList<RetainedDrawPacket> packets,
            List<RetainedDrawBatch> batches)
        {
            int start = 0;
            while (start < packets.Count)
            {
                RetainedDrawPacket first = packets[start];
                int count = 1;
                if (first.Batchable)
                {
                    while (start + count < packets.Count)
                    {
                        RetainedDrawPacket next = packets[start + count];
                        if (!next.Batchable || next.BatchState != first.BatchState)
                            break;
                        count++;
                    }
                }
                batches.Add(new RetainedDrawBatch(start, count, first.BatchState));
                start += count;
            }
        }

        private static int Reuses(IReadOnlyList<RetainedDrawBatch> batches)
        {
            int count = 0;
            for (int i = 0; i < batches.Count; i++)
                count += Math.Max(0, batches[i].Count - 1);
            return count;
        }
    }

    internal readonly record struct WorldRenderGraphPass(
        WorldRenderPassKind Kind,
        string Name,
        WorldRenderResource Reads,
        WorldRenderResource Writes);

    /// <summary>
    /// Explicit graph for the ordering-sensitive MPH world passes.
    /// </summary>
    internal sealed class WorldRenderGraph
    {
        private static readonly WorldRenderGraphPass[] _passes =
        {
            new(WorldRenderPassKind.Opaque, "world.opaque",
                WorldRenderResource.Depth,
                WorldRenderResource.Color | WorldRenderResource.Depth | WorldRenderResource.Stencil),
            new(WorldRenderPassKind.Decal, "world.decals",
                WorldRenderResource.Color | WorldRenderResource.Depth,
                WorldRenderResource.Color | WorldRenderResource.Depth),
            new(WorldRenderPassKind.MarkTranslucent, "world.translucent-mask",
                WorldRenderResource.Depth,
                WorldRenderResource.Depth | WorldRenderResource.Stencil),
            new(WorldRenderPassKind.RebuildDepth, "world.rebuild-depth",
                WorldRenderResource.None,
                WorldRenderResource.Depth),
            new(WorldRenderPassKind.TranslucentBehind, "world.translucent-behind",
                WorldRenderResource.Depth | WorldRenderResource.Stencil,
                WorldRenderResource.Color),
            new(WorldRenderPassKind.TranslucentFront, "world.translucent-front",
                WorldRenderResource.Depth | WorldRenderResource.Stencil,
                WorldRenderResource.Color)
        };

        internal IReadOnlyList<WorldRenderGraphPass> Passes => _passes;

        internal static bool Validate(out string error)
        {
            WorldRenderPassKind[] expected =
            {
                WorldRenderPassKind.Opaque,
                WorldRenderPassKind.Decal,
                WorldRenderPassKind.MarkTranslucent,
                WorldRenderPassKind.RebuildDepth,
                WorldRenderPassKind.TranslucentBehind,
                WorldRenderPassKind.TranslucentFront
            };
            if (_passes.Length != expected.Length)
            {
                error = "world render graph pass count changed";
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (_passes[i].Kind != expected[i])
                {
                    error = $"world render graph pass {i} is {_passes[i].Kind}, expected {expected[i]}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(_passes[i].Name))
                {
                    error = $"world render graph pass {i} has no name";
                    return false;
                }
            }
            if ((_passes[0].Writes & (WorldRenderResource.Color | WorldRenderResource.Depth
                | WorldRenderResource.Stencil)) != (WorldRenderResource.Color
                    | WorldRenderResource.Depth | WorldRenderResource.Stencil))
            {
                error = "opaque pass no longer establishes color/depth/stencil";
                return false;
            }
            error = "";
            return true;
        }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private readonly Mods.Render.RetainedRenderWorld _retainedRenderWorld = new();
        private readonly Mods.Render.WorldRenderGraph _worldRenderGraph = new();

        internal int RetainedRenderPacketCount => _retainedRenderWorld.PacketCount;
        internal int RetainedVisiblePacketCount => _retainedRenderWorld.PacketCount;
        internal int RetainedRenderBatchCount => _retainedRenderWorld.BatchCount;
        internal int RetainedRenderStateReuseCount => _retainedRenderWorld.StateReuseCount;
        internal int RetainedGraphStateApplicationCount =>
            _retainedRenderWorld.GraphStateApplicationCount;
        internal int RetainedGraphStateReuseCount =>
            _retainedRenderWorld.GraphStateReuseCount;
        internal int RetainedMeshDescriptorCount => _retainedRenderWorld.MeshDescriptorCount;
        internal long RetainedRoomTemplateBuilds => _room?.RetainedRoomTemplateBuilds ?? 0;
        internal long RetainedRoomTemplateHits => _room?.RetainedRoomTemplateHits ?? 0;
        internal long RetainedRoomPacketSubmissions =>
            _room?.RetainedRoomPacketSubmissions ?? 0;
        internal long RetainedDirectShadowDraws => _retainedDirectShadowDraws;
        internal long RetainedCompatibilityShadowDraws =>
            _retainedCompatibilityShadowDraws;
        private long _retainedDirectWorldDraws;
        private long _retainedDirectAdvancedWorldDraws;
        private long _retainedDirectMatrixStackWorldDraws;
        private long _retainedDirectViewModelWorldDraws;
        private long _retainedDirectBillboardWorldDraws;
        private long _retainedDirectOverrideWorldDraws;
        private long _retainedDirectTexturedSkinWorldDraws;
        private long _retainedDirectOutlinedWorldDraws;
        private long _retainedDirectDecalWorldDraws;
        private long _retainedDirectTranslucentWorldDraws;
        private long _retainedCompatibilityWorldDraws;
        internal long RetainedDirectWorldDraws => _retainedDirectWorldDraws;
        internal long RetainedDirectAdvancedWorldDraws =>
            _retainedDirectAdvancedWorldDraws;
        internal long RetainedDirectMatrixStackWorldDraws =>
            _retainedDirectMatrixStackWorldDraws;
        internal long RetainedDirectViewModelWorldDraws =>
            _retainedDirectViewModelWorldDraws;
        internal long RetainedDirectBillboardWorldDraws =>
            _retainedDirectBillboardWorldDraws;
        internal long RetainedDirectOverrideWorldDraws =>
            _retainedDirectOverrideWorldDraws;
        internal long RetainedDirectTexturedSkinWorldDraws =>
            _retainedDirectTexturedSkinWorldDraws;
        internal long RetainedDirectOutlinedWorldDraws =>
            _retainedDirectOutlinedWorldDraws;
        internal long RetainedDirectDecalWorldDraws =>
            _retainedDirectDecalWorldDraws;
        internal long RetainedDirectTranslucentWorldDraws =>
            _retainedDirectTranslucentWorldDraws;
        internal long RetainedCompatibilityWorldDraws =>
            _retainedCompatibilityWorldDraws;
        internal ulong RetainedRenderFrameRevision => _retainedRenderWorld.FrameRevision;

        private void CaptureRetainedRenderWorld()
        {
            _retainedRenderWorld.Capture(_nonDecalItems, _decalItems, _translucentItems);
        }

        private IReadOnlyList<Mods.Render.RetainedDrawPacket> PacketsFor(
            Mods.Render.WorldRenderPassKind kind) => kind switch
        {
            Mods.Render.WorldRenderPassKind.Decal => _retainedRenderWorld.Decals,
            Mods.Render.WorldRenderPassKind.MarkTranslucent
                or Mods.Render.WorldRenderPassKind.TranslucentBehind
                or Mods.Render.WorldRenderPassKind.TranslucentFront => _retainedRenderWorld.Translucent,
            _ => _retainedRenderWorld.Opaque
        };

        private IReadOnlyList<Mods.Render.RetainedDrawBatch> BatchesFor(
            Mods.Render.WorldRenderPassKind kind) => kind switch
        {
            Mods.Render.WorldRenderPassKind.Decal => _retainedRenderWorld.DecalBatches,
            Mods.Render.WorldRenderPassKind.MarkTranslucent
                or Mods.Render.WorldRenderPassKind.TranslucentBehind
                or Mods.Render.WorldRenderPassKind.TranslucentFront =>
                    _retainedRenderWorld.TranslucentBatches,
            _ => _retainedRenderWorld.OpaqueBatches
        };

        private void DrawRenderGraphPackets(Mods.Render.WorldRenderPassKind kind)
        {
            IReadOnlyList<Mods.Render.RetainedDrawPacket> packets = PacketsFor(kind);
            IReadOnlyList<Mods.Render.RetainedDrawBatch> batches = BatchesFor(kind);
            bool directSceneState = !_editorMaterialPreview
                && _wireframeLevel == 0
                && !Mods.RenderOptions.CelShading;

            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                Mods.Render.RetainedDrawBatch batch = batches[batchIndex];
                bool compatibilitySharedStateValid = false;
                for (int offset = 0; offset < batch.Count; offset++)
                {
                    Mods.Render.RetainedDrawPacket packet =
                        packets[batch.Start + offset];
                    RenderItem item = packet.Item;

#if !MPHREAD_SERVER
                    if (Mods.Render.ModernGraphicsCompat.Active)
                    {
                        if (kind is Mods.Render.WorldRenderPassKind.MarkTranslucent
                            or Mods.Render.WorldRenderPassKind.TranslucentBehind
                            or Mods.Render.WorldRenderPassKind.TranslucentFront)
                        {
                            Mods.Render.ModernGraphicsCompat.SetRetainedWorldStencilReference(
                                kind, item.PolygonId);
                        }

                        if (directSceneState)
                        {
                            SetViewModelRenderState(item.ViewModel);
                            Matrix4? projectionOverride = item.ViewModel
                                ? _viewModelPerspectiveMatrix : null;
                            Matrix4 viewInverse = item.BillboardMode switch
                            {
                                BillboardMode.Sphere => _viewInvRotMatrix,
                                BillboardMode.Cylinder => _viewInvRotYMatrix,
                                _ => Matrix4.Identity
                            };
                            Mods.Render.RetainedWorldTextureSet textures =
                                RetainedWorldTextures(item);
                            if (Mods.Render.ModernGraphicsCompat.TryDrawRetainedWorld(
                                item, packet.Mesh, textures, _showTextures,
                                LightingOn, _faceCulling,
                                projectionOverride, viewInverse, kind))
                            {
                                _retainedDirectWorldDraws++;
                                if (kind == Mods.Render.WorldRenderPassKind.Decal)
                                    _retainedDirectDecalWorldDraws++;
                                else if (kind is
                                    Mods.Render.WorldRenderPassKind.MarkTranslucent
                                    or Mods.Render.WorldRenderPassKind.TranslucentBehind
                                    or Mods.Render.WorldRenderPassKind.TranslucentFront)
                                {
                                    _retainedDirectTranslucentWorldDraws++;
                                }
                                if (textures.Advanced)
                                    _retainedDirectAdvancedWorldDraws++;
                                if (item.MatrixStackCount > 0)
                                    _retainedDirectMatrixStackWorldDraws++;
                                if (item.ViewModel)
                                    _retainedDirectViewModelWorldDraws++;
                                if (item.BillboardMode != BillboardMode.None)
                                    _retainedDirectBillboardWorldDraws++;
                                if (item.OverrideColor.HasValue
                                    || item.PaletteOverride.HasValue)
                                {
                                    _retainedDirectOverrideWorldDraws++;
                                }
                                if (item.TexturedPlayerSkin)
                                    _retainedDirectTexturedSkinWorldDraws++;
                                if (item.PlayerOutlineColor.HasValue)
                                    _retainedDirectOutlinedWorldDraws++;
                                compatibilitySharedStateValid = false;
                                NoteRetainedTextureSampling(
                                    textures, item.XRepeat, item.YRepeat);
                                continue;
                            }
                        }
                    }
                    else
#endif
                    {
                        if (kind == Mods.Render.WorldRenderPassKind.MarkTranslucent)
                            GL.StencilFunc(StencilFunction.Greater, item.PolygonId, 0xFF);
                        else if (kind == Mods.Render.WorldRenderPassKind.TranslucentBehind)
                            GL.StencilFunc(StencilFunction.Notequal, item.PolygonId, 0xFF);
                        else if (kind == Mods.Render.WorldRenderPassKind.TranslucentFront)
                            GL.StencilFunc(StencilFunction.Equal, item.PolygonId, 0xFF);
                    }

                    _retainedCompatibilityWorldDraws++;
                    RenderItem(item,
                        applySharedState: !compatibilitySharedStateValid,
                        retainedMesh: packet.Mesh);
                    compatibilitySharedStateValid = packet.Batchable;
                }
            }
            FinishViewModelRenderRun();
        }

        private Mods.Render.RetainedWorldTextureSet RetainedWorldTextures(
            RenderItem item)
        {
            int albedo = item.HasTexture && _showTextures
                ? item.TextureBindingId : 0;
            Mods.Render.MaterialMapBindings maps = default;
            if (Mods.RenderOptions.AdvancedMaterials && item.HasTexture)
                _materialMaps.TryGetValue(item.TextureBindingId, out maps);

            return new Mods.Render.RetainedWorldTextureSet(
                RetainedTextureBinding(albedo, alwaysSample: true),
                RetainedTextureBinding(maps.Normal, alwaysSample: false),
                RetainedTextureBinding(maps.Specular, alwaysSample: false),
                RetainedTextureBinding(maps.Emissive, alwaysSample: false));
        }

        private Mods.Render.RetainedTextureBinding RetainedTextureBinding(
            int binding, bool alwaysSample)
        {
            if (binding == 0)
                return default;
            if (_modernTextureSampling.TryGetValue(binding, out var modern))
            {
                return new Mods.Render.RetainedTextureBinding(
                    binding,
                    Mods.Render.TextureSamplingPolicy.ResolveModern(
                        modern.AssetClass, modern.Channel),
                    ApplySampling: true);
            }
            return new Mods.Render.RetainedTextureBinding(
                binding,
                Mods.Render.TextureSamplingPolicy.ResolveNativeWorld(),
                ApplySampling: alwaysSample);
        }

        private void NoteRetainedTextureSampling(
            Mods.Render.RetainedWorldTextureSet textures,
            RepeatMode xRepeat, RepeatMode yRepeat)
        {
            for (int unit = 0; unit < 4; unit++)
            {
                Mods.Render.RetainedTextureBinding binding = textures.At(unit);
                if (!binding.IsBound || !binding.ApplySampling)
                    continue;
                _appliedTextureSampling[binding.Id] =
                    new Mods.Render.AppliedTextureSamplingState(
                        binding.Sampling, xRepeat, yRepeat);
                if (binding.Sampling.Mipmaps)
                    _mipmappedTextures.Add(binding.Id);
            }
        }

        /// <summary>
        /// Execute the established six-pass MPH world renderer through an explicit
        /// graph. State transitions intentionally match the prior inline code.
        /// Adjacent compatible mesh packets may share state, but are never sorted.
        /// </summary>
        private void ExecuteWorldRenderGraph()
        {
#if !MPHREAD_SERVER
            bool modernGraph = Mods.Render.ModernGraphicsCompat.Active;
            bool directWorldEnabled = modernGraph
                && !_editorMaterialPreview
                && _wireframeLevel == 0
                && !Mods.RenderOptions.CelShading;
            if (directWorldEnabled)
                Mods.Render.ModernGraphicsCompat.BeginRetainedWorldFrame();
#else
            const bool modernGraph = false;
#endif

            foreach (Mods.Render.WorldRenderGraphPass pass in _worldRenderGraph.Passes)
            {
#if !MPHREAD_SERVER
                if (modernGraph)
                {
                    Mods.Render.ModernGraphicsCompat.ConfigureRetainedWorldPass(
                        pass.Kind);
                    DrawRenderGraphPackets(pass.Kind);
                    continue;
                }
#endif
                switch (pass.Kind)
                {
                case Mods.Render.WorldRenderPassKind.Opaque:
                    GL.ColorMask(true, true, true, true);
                    GL.Enable(EnableCap.AlphaTest);
                    GL.AlphaFunc(AlphaFunction.Equal, 1.0f);
                    GL.DepthFunc(DepthFunction.Less);
                    GL.DepthMask(true);
                    GL.Enable(EnableCap.StencilTest);
                    GL.StencilMask(0xFF);
                    GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);
                    GL.StencilFunc(StencilFunction.Always, 0, 0xFF);
                    DrawRenderGraphPackets(pass.Kind);
                    GL.Disable(EnableCap.AlphaTest);
                    break;

                case Mods.Render.WorldRenderPassKind.Decal:
                    GL.Enable(EnableCap.PolygonOffsetFill);
                    GL.PolygonOffset(-1, -1);
                    GL.DepthFunc(DepthFunction.Lequal);
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha,
                        BlendingFactor.OneMinusSrcAlpha);
                    DrawRenderGraphPackets(pass.Kind);
                    GL.PolygonOffset(0, 0);
                    GL.Disable(EnableCap.PolygonOffsetFill);
                    break;

                case Mods.Render.WorldRenderPassKind.MarkTranslucent:
                    GL.Enable(EnableCap.AlphaTest);
                    GL.AlphaFunc(AlphaFunction.Less, 1.0f);
                    GL.ColorMask(false, false, false, false);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep,
                        StencilOp.Replace);
                    DrawRenderGraphPackets(pass.Kind);
                    break;

                case Mods.Render.WorldRenderPassKind.RebuildDepth:
                    GL.Clear(ClearBufferMask.DepthBufferBit);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep,
                        StencilOp.Keep);
                    GL.StencilFunc(StencilFunction.Always, 0, 0xFF);
                    GL.AlphaFunc(AlphaFunction.Equal, 1.0f);
                    DrawRenderGraphPackets(pass.Kind);
                    break;

                case Mods.Render.WorldRenderPassKind.TranslucentBehind:
                    GL.AlphaFunc(AlphaFunction.Less, 1.0f);
                    GL.ColorMask(true, true, true, true);
                    GL.DepthMask(false);
                    GL.DepthFunc(DepthFunction.Lequal);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep,
                        StencilOp.Keep);
                    DrawRenderGraphPackets(pass.Kind);
                    break;

                case Mods.Render.WorldRenderPassKind.TranslucentFront:
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep,
                        StencilOp.Keep);
                    DrawRenderGraphPackets(pass.Kind);
                    break;
                }
            }

#if !MPHREAD_SERVER
            if (modernGraph)
            {
                Mods.Render.ModernGraphicsCompat.FinishRetainedWorldGraph();
                return;
            }
#endif
            GL.DepthMask(true);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.StencilTest);
            GL.PolygonMode(TriangleFace.FrontAndBack,
                OpenTK.Graphics.OpenGL.PolygonMode.Fill);
        }
    }
}
