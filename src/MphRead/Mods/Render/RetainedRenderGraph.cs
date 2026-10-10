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
    /// Sequence preserves cartridge submission order. Only explicitly safe,
    /// room-owned opaque runs may be reordered; decals, translucency, dynamic
    /// entities and every packet carrying presentation overrides remain barriers.
    /// </summary>
    internal readonly struct RetainedDrawPacket
    {
        internal RenderItem Item { get; }
        internal int Sequence { get; }
        internal ulong StateKey { get; }
        internal RetainedMeshDescriptor Mesh { get; }
        internal RetainedMaterialDescriptor Material { get; }
        internal bool Batchable { get; }
        internal bool ReorderableOpaque { get; }
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
            ReorderableOpaque = item.RetainedRoomOwned
                && Batchable
                && item.PolygonId == 0
                && item.PolygonMode == PolygonMode.Modulate
                && item.RenderMode == RenderMode.Normal
                && item.Alpha >= 0.999f
                && !item.Wireframe;
            BatchState = new RetainedBatchState(item.CullingMode, item.Wireframe,
                Material);
            StateKey = BuildStateKey(item);
        }

        private static ulong BuildStateKey(RenderItem item)
        {
            // This is deliberately a render-state key, not a geometry key.
            // Different room meshes with identical pipeline/material state must
            // compare equal so sorting can make them adjacent and let the batch
            // executor reuse that state. Mesh identity remains outside the key.
            unchecked
            {
                const ulong offsetBasis = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong key = offsetBasis;

                void Mix(uint value)
                {
                    key ^= value;
                    key *= prime;
                }

                void MixFloat(float value) =>
                    Mix(BitConverter.SingleToUInt32Bits(value));

                void MixVector3(Vector3 value)
                {
                    MixFloat(value.X);
                    MixFloat(value.Y);
                    MixFloat(value.Z);
                }

                void MixMatrix(Matrix4 value)
                {
                    MixFloat(value.M11); MixFloat(value.M12);
                    MixFloat(value.M13); MixFloat(value.M14);
                    MixFloat(value.M21); MixFloat(value.M22);
                    MixFloat(value.M23); MixFloat(value.M24);
                    MixFloat(value.M31); MixFloat(value.M32);
                    MixFloat(value.M33); MixFloat(value.M34);
                    MixFloat(value.M41); MixFloat(value.M42);
                    MixFloat(value.M43); MixFloat(value.M44);
                }

                Mix((uint)item.PolygonMode);
                Mix((uint)item.RenderMode);
                Mix((uint)item.CullingMode);
                Mix(item.Wireframe ? 1u : 0u);
                Mix(item.Lighting ? 1u : 0u);
                MixVector3(item.Diffuse);
                MixVector3(item.Ambient);
                MixVector3(item.Specular);
                MixVector3(item.Emission);
                MixFloat(item.Alpha);
                Mix((uint)item.TexgenMode);
                Mix((uint)item.XRepeat);
                Mix((uint)item.YRepeat);
                Mix(item.HasTexture ? 1u : 0u);
                Mix(unchecked((uint)item.TextureBindingId));
                MixMatrix(item.TexcoordMatrix);
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
        private readonly Dictionary<RenderItem, RetainedDrawPacket>
            _packetByItem = new();
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
        internal bool TryGetPacket(RenderItem item, out RetainedDrawPacket packet) =>
            _packetByItem.TryGetValue(item, out packet);
        internal int OpaqueSortRunCount { get; private set; }
        internal int OpaqueReorderedPacketCount { get; private set; }
        internal ulong FrameRevision { get; private set; }

        internal void Capture(IReadOnlyList<RenderItem> nonDecal,
            IReadOnlyList<RenderItem> decals, IReadOnlyList<RenderItem> translucent)
        {
            _packetByItem.Clear();
            OpaqueSortRunCount = 0;
            OpaqueReorderedPacketCount = 0;
            CaptureList(_opaque, _opaqueBatches, nonDecal, sortOpaque: true);
            CaptureList(_decals, _decalBatches, decals, sortOpaque: false);
            CaptureList(_translucent, _translucentBatches, translucent,
                sortOpaque: false);
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
            _packetByItem.Clear();
        }

        private void CaptureList(List<RetainedDrawPacket> destination,
            List<RetainedDrawBatch> batches, IReadOnlyList<RenderItem> source,
            bool sortOpaque)
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
                var packet = new RetainedDrawPacket(item, i, mesh);
                destination.Add(packet);
                _packetByItem.TryAdd(item, packet);
            }
            if (sortOpaque)
                SortSafeOpaqueRuns(destination);
            BuildAdjacentBatches(destination, batches);
        }

        private sealed class OpaquePacketComparer : IComparer<RetainedDrawPacket>
        {
            internal static readonly OpaquePacketComparer Instance = new();

            public int Compare(RetainedDrawPacket x, RetainedDrawPacket y)
            {
                int state = x.StateKey.CompareTo(y.StateKey);
                return state != 0 ? state : x.Sequence.CompareTo(y.Sequence);
            }
        }

        private void SortSafeOpaqueRuns(List<RetainedDrawPacket> packets)
        {
            int start = 0;
            while (start < packets.Count)
            {
                while (start < packets.Count
                    && !packets[start].ReorderableOpaque)
                {
                    start++;
                }
                if (start >= packets.Count)
                    break;

                int end = start + 1;
                while (end < packets.Count && packets[end].ReorderableOpaque)
                    end++;

                int count = end - start;
                if (count > 1)
                {
                    packets.Sort(start, count, OpaquePacketComparer.Instance);
                    OpaqueSortRunCount++;
                    for (int i = start; i < end; i++)
                    {
                        if (packets[i].Sequence != i)
                            OpaqueReorderedPacketCount++;
                    }
                }
                start = end;
            }
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
        internal long RetainedRoomClusterBuilds =>
            _room?.RetainedRoomClusterBuilds ?? 0;
        internal long RetainedRoomClusterTests =>
            _room?.RetainedRoomClusterTests ?? 0;
        internal long RetainedRoomClusterRejects =>
            _room?.RetainedRoomClusterRejects ?? 0;
        internal long RetainedRoomNodeVisibilityTests =>
            _room?.RetainedRoomNodeVisibilityTests ?? 0;
        internal int RetainedOpaqueSortRunCount =>
            _retainedRenderWorld.OpaqueSortRunCount;
        internal int RetainedOpaqueReorderedPacketCount =>
            _retainedRenderWorld.OpaqueReorderedPacketCount;
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
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                Mods.Render.RetainedDrawBatch batch = batches[batchIndex];
                bool sharedStateValid = false;
                for (int offset = 0; offset < batch.Count; offset++)
                {
                    Mods.Render.RetainedDrawPacket packet = packets[batch.Start + offset];
                    RenderItem item = packet.Item;
                    if (kind == Mods.Render.WorldRenderPassKind.MarkTranslucent)
                        GL.StencilFunc(StencilFunction.Greater, item.PolygonId, 0xFF);
                    else if (kind == Mods.Render.WorldRenderPassKind.TranslucentBehind)
                        GL.StencilFunc(StencilFunction.Notequal, item.PolygonId, 0xFF);
                    else if (kind == Mods.Render.WorldRenderPassKind.TranslucentFront)
                        GL.StencilFunc(StencilFunction.Equal, item.PolygonId, 0xFF);
                    _retainedCompatibilityWorldDraws++;
                    RenderItem(item, applySharedState: !sharedStateValid,
                        retainedMesh: packet.Mesh);
                    sharedStateValid = packet.Batchable;
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
            foreach (Mods.Render.WorldRenderGraphPass pass in _worldRenderGraph.Passes)
            {
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

            GL.DepthMask(true);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.StencilTest);
            GL.PolygonMode(TriangleFace.FrontAndBack,
                OpenTK.Graphics.OpenGL.PolygonMode.Fill);
        }
    }
}
