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
        float EmissiveIntensity,
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
                item.Specular, item.Emission, item.EmissiveIntensity, item.Alpha, item.TexgenMode,
                item.XRepeat, item.YRepeat, item.HasTexture, item.TextureBindingId,
                item.TexcoordMatrix, item.Cosmetics, item.CosmeticMaterial,
                item.TexturedPlayerSkin, item.OverrideColor, item.PaletteOverride);
    }

    internal readonly record struct RetainedBatchState(
        CullingMode CullingMode,
        bool Wireframe,
        RenderMode RenderMode,
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
        internal RetainedMaterialDescriptor Material => BatchState.Material;
        internal bool Batchable { get; }
        internal bool ReorderableOpaque { get; }
        internal RetainedBatchState BatchState { get; }
        internal RetainedMaterialIdentity? MaterialIdentity { get; }

        internal RetainedDrawPacket(RenderItem item, int sequence,
            RetainedMeshDescriptor mesh, RetainedMaterialIdentity.Cache? materialCache = null)
        {
            Item = item;
            Sequence = sequence;
            Mesh = mesh;
            RetainedMaterialDescriptor material = RetainedMaterialDescriptor.From(item);
            Batchable = (item.Type == RenderItemType.Mesh
                || ProceduralBatchEnabled && item.Type is RenderItemType.Particle or RenderItemType.TrailSingle
                    or RenderItemType.TrailMulti or RenderItemType.TrailStack)
                && !item.ViewModel
                && item.BillboardMode == BillboardMode.None
                && item.Cosmetics == default
                && item.CosmeticMaterial == default
                && item.OverrideColor == null
                && item.PaletteOverride == null
                && !item.TexturedPlayerSkin
                && item.PlayerOutlineColor == null;
            ReorderableOpaque = item.Type == RenderItemType.Mesh && item.RetainedRoomOwned
                && Batchable
                && item.PolygonId == 0
                && item.PolygonMode == PolygonMode.Modulate
                && item.RenderMode == RenderMode.Normal
                && item.Alpha >= 0.999f
                && !item.Wireframe;
            BatchState = new RetainedBatchState(item.CullingMode, item.Wireframe,
                item.RenderMode, material);
            MaterialIdentity = null;
            if (materialCache != null && item.RetainedRoomOwned)
            {
                var previous = item.CachedMaterialIdentity;
                MaterialIdentity = materialCache.Get(item, BatchState);
                if (!ReferenceEquals(previous, MaterialIdentity)) item.CachedStateKey = BuildStateKey(item);
                StateKey = item.CachedStateKey;
            }
            else if (StaticCacheEnabled && item.RetainedRoomOwned)
            {
                if (!item.HasCachedBatchState || item.CachedBatchState != BatchState)
                {
                    item.CachedStateKey = BuildStateKey(item);
                    item.CachedBatchState = BatchState;
                    item.HasCachedBatchState = true;
                }
                StateKey = item.CachedStateKey;
            }
            else StateKey = BuildStateKey(item);
        }

        private static readonly bool ProceduralBatchEnabled = OpenGlOptimizationPolicy.Enabled("PROCEDURAL_BATCH");
        private static readonly bool StaticCacheEnabled = OpenGlOptimizationPolicy.Enabled("STATIC_CACHE");

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
    /// Proof boundary for one indexed GL draw replacing several opaque room
    /// packets. Shared material is insufficient: every per-draw shader value
    /// (including full bone/matrix palette and dynamic light vectors) must
    /// match exactly. Translucent/stencil/decals/dynamic entities remain
    /// forbidden regardless of apparent batching state.
    /// </summary>
    internal static class RetainedOpaqueBatchPolicy
    {
        // Replay uses the exact same packet/geometry proof. Its pass remains
        // color-masked and stencil-preserving; this changes submission only.
        internal static bool CanBatchPass(WorldRenderPassKind kind, bool depthBatchEnabled)
            => kind == WorldRenderPassKind.Opaque
                || kind == WorldRenderPassKind.RebuildDepth && depthBatchEnabled;

        internal static bool SameMaterial(in RetainedDrawPacket a, in RetainedDrawPacket b)
            => a.MaterialIdentity != null && b.MaterialIdentity != null
                ? ReferenceEquals(a.MaterialIdentity, b.MaterialIdentity) : a.BatchState == b.BatchState;

        internal static bool CanMerge(in RetainedDrawPacket first, in RetainedDrawPacket next)
        {
            if (!first.ReorderableOpaque || !next.ReorderableOpaque
                || !SameMaterial(first, next))
                return false;
            RenderItem a = first.Item, b = next.Item;
            if (a.WeightedSkinning || b.WeightedSkinning
                || a.BillboardMode != BillboardMode.None
                || b.BillboardMode != BillboardMode.None
                || !a.Transform.Equals(b.Transform)
                || a.MatrixStackCount != b.MatrixStackCount
                || a.EmissiveIntensity != b.EmissiveIntensity
                || a.LightInfo.Light1Vector != b.LightInfo.Light1Vector
                || a.LightInfo.Light2Vector != b.LightInfo.Light2Vector
                || a.LightInfo.Light1Color != b.LightInfo.Light1Color
                || a.LightInfo.Light2Color != b.LightInfo.Light2Color)
                return false;
            int values = a.MatrixStackCount * 16;
            if (values > a.MatrixStack.Length || values > b.MatrixStack.Length)
                return false;
            if (MatrixPaletteRevision.Enabled && a.MatrixPaletteVersion > 0
                && a.MatrixPaletteVersion == b.MatrixPaletteVersion
                && ReferenceEquals(a.MatrixPaletteSource, b.MatrixPaletteSource)
                && a.MatrixPaletteScale == b.MatrixPaletteScale)
                return true;
            for (int i = 0; i < values; i++)
                if (a.MatrixStack[i] != b.MatrixStack[i])
                    return false;
            return true;
        }
    }

    internal enum RetainedOpaqueOrder { StateSorted, Submission }

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
        // Subdividing state runs only pays when this backend can submit a
        // retained atlas. GLES keeps its larger shared-state runs intact.
#if !ANDROID && !MPHREAD_SERVER
        private static readonly bool RoomGroupsEnabled = OpenGlOptimizationPolicy.Enabled("ROOM_GROUPS")
            && GraphicsApi.RetainedGeometryBatchingEnabled;
#else
        private const bool RoomGroupsEnabled = false;
#endif
        private static readonly bool RoomCacheEnabled = OpenGlOptimizationPolicy.Enabled("ROOM_CACHE");
        private readonly bool _roomCacheEnabled;
        private static readonly bool RoomIdentityEnabled = OpenGlOptimizationPolicy.Enabled("ROOM_IDENTITY");
        private readonly RetainedMaterialIdentity.Cache _materialIdentities = new();
        private object _meshOwner = new();
        internal RetainedRenderWorld() : this(RoomCacheEnabled) { }
        internal RetainedRenderWorld(bool roomCacheEnabled)
            => _roomCacheEnabled = roomCacheEnabled;
        internal int RoomDescriptorCacheHits { get; private set; }
        internal int MeshDescriptorLookups { get; private set; }
        private readonly List<RetainedDrawPacket> _opaque = new(256);
        private readonly List<RetainedDrawPacket> _decals = new(64);
        private readonly List<RetainedDrawPacket> _translucent = new(128);
        private readonly List<RetainedDrawBatch> _opaqueBatches = new(128);
        private readonly List<RetainedDrawBatch> _decalBatches = new(32);
        private readonly List<RetainedDrawBatch> _translucentBatches = new(64);

        private static readonly bool SortCacheEnabled = OpenGlOptimizationPolicy.Enabled("STATIC_CACHE");
        private ulong[] _sortKeys = Array.Empty<ulong>();
        private bool[] _sortBarriers = Array.Empty<bool>();
        private int[] _sortPermutation = Array.Empty<int>();
        private RetainedDrawPacket[] _sortScratch = Array.Empty<RetainedDrawPacket>();
        private int _sortCount = -1, _cachedSortRuns, _cachedReordered;
        internal bool OpaqueSortOrderReused { get; private set; }

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
        internal int OpaqueSortRunCount { get; private set; }
        internal int OpaqueReorderedPacketCount { get; private set; }
        internal ulong FrameRevision { get; private set; }

        internal void Capture(IReadOnlyList<RenderItem> nonDecal,
            IReadOnlyList<RenderItem> decals, IReadOnlyList<RenderItem> translucent,
            RetainedOpaqueOrder opaqueOrder = RetainedOpaqueOrder.StateSorted)
        {
            RoomDescriptorCacheHits = MeshDescriptorLookups = 0;
            OpaqueSortOrderReused = false;
            OpaqueSortRunCount = 0;
            OpaqueReorderedPacketCount = 0;
            CaptureList(_opaque, _opaqueBatches, nonDecal,
                sortOpaque: opaqueOrder == RetainedOpaqueOrder.StateSorted, opaqueRoomGroups: true);
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
            // Invalidate tokens without visiting possibly invisible room items.
            // Tokens/descriptors never point back to this owner or its scene.
            _meshOwner = new();
            _meshDescriptors.Clear();
            _materialIdentities.Clear();
            RoomDescriptorCacheHits = MeshDescriptorLookups = 0;
            _sortKeys = Array.Empty<ulong>();
            _sortBarriers = Array.Empty<bool>();
            _sortPermutation = Array.Empty<int>();
            _sortScratch = Array.Empty<RetainedDrawPacket>();
            _sortCount = -1;
            OpaqueSortOrderReused = false;
        }

        private void CaptureList(List<RetainedDrawPacket> destination,
            List<RetainedDrawBatch> batches, IReadOnlyList<RenderItem> source,
            bool sortOpaque, bool opaqueRoomGroups = false)
        {
            destination.Clear();
            batches.Clear();
            if (destination.Capacity < source.Count)
                destination.Capacity = source.Count;
            for (int i = 0; i < source.Count; i++)
            {
                RenderItem item = source[i];
                RetainedMeshDescriptor mesh = MeshFor(item);
                var packet = new RetainedDrawPacket(item, i, mesh, RoomIdentityEnabled ? _materialIdentities : null);
                destination.Add(packet);
            }
            if (sortOpaque)
                SortOpaqueWithReuse(destination);
            // Original ordering still allows exact-state adjacent opaque groups.
            // Sorting policy never weakens the combined-draw equality proof.
            BuildAdjacentBatches(destination, batches, opaqueRoomGroups && RoomGroupsEnabled);
        }

        private RetainedMeshDescriptor MeshFor(RenderItem item)
        {
            RetainedMeshDescriptorKey key = RetainedMeshDescriptor.KeyOf(item);
            bool cacheable = _roomCacheEnabled && item.RetainedRoomOwned;
            if (cacheable && ReferenceEquals(item.CachedMeshOwner, _meshOwner)
                && item.CachedMeshDescriptor is { } cached && cached.Key == key)
            {
                RoomDescriptorCacheHits++;
                return cached;
            }
            MeshDescriptorLookups++;
            if (!_meshDescriptors.TryGetValue(key, out RetainedMeshDescriptor? mesh))
            {
                mesh = new RetainedMeshDescriptor(key);
                _meshDescriptors.Add(key, mesh);
            }
            if (cacheable)
            {
                item.CachedMeshOwner = _meshOwner;
                item.CachedMeshDescriptor = mesh;
            }
            else
            {
                item.CachedMeshOwner = null;
                item.CachedMeshDescriptor = null;
            }
            return mesh;
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

        private void SortOpaqueWithReuse(List<RetainedDrawPacket> packets)
        {
            // Tiny runs need less work with the ordinary sort than proof + copy.
            if (!SortCacheEnabled || packets.Count < 8)
            {
                _sortCount = -1;
                SortSafeOpaqueRuns(packets);
                return;
            }
            int count = packets.Count;
            bool matches = count == _sortCount;
            for (int i = 0; matches && i < count; i++)
                matches = packets[i].StateKey == _sortKeys[i]
                    && packets[i].ReorderableOpaque == _sortBarriers[i];
            if (matches)
            {
                for (int i = 0; i < count; i++) _sortScratch[i] = packets[i];
                for (int i = 0; i < count; i++) packets[i] = _sortScratch[_sortPermutation[i]];
                OpaqueSortRunCount = _cachedSortRuns;
                OpaqueReorderedPacketCount = _cachedReordered;
                OpaqueSortOrderReused = true;
                // Retain only proof/permutation; references belong to this frame.
                Array.Clear(_sortScratch, 0, count);
                return;
            }
            if (_sortKeys.Length < count)
            {
                _sortKeys = new ulong[count];
                _sortBarriers = new bool[count];
                _sortPermutation = new int[count];
                _sortScratch = new RetainedDrawPacket[count];
            }
            for (int i = 0; i < count; i++)
            {
                _sortKeys[i] = packets[i].StateKey;
                _sortBarriers[i] = packets[i].ReorderableOpaque;
            }
            SortSafeOpaqueRuns(packets);
            for (int i = 0; i < count; i++) _sortPermutation[i] = packets[i].Sequence;
            _sortCount = count;
            _cachedSortRuns = OpaqueSortRunCount;
            _cachedReordered = OpaqueReorderedPacketCount;
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

        internal static void BuildAdjacentBatches(
            IReadOnlyList<RetainedDrawPacket> packets,
            List<RetainedDrawBatch> batches, bool roomGroups = false)
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
                        if (!next.Batchable || !RetainedOpaqueBatchPolicy.SameMaterial(first, next))
                            break;
                        if (roomGroups && (first.ReorderableOpaque != next.ReorderableOpaque
                            || first.ReorderableOpaque && (count >= RetainedRoomBatchPlan.MaxIds
                                || !RetainedOpaqueBatchPolicy.CanMerge(first, next)))) break;
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
        // Opaque-only rendering has no intervening decal depth writes or
        // translucent surfaces requiring a front/behind depth rebuild.
        // Never reuse the initial depth if either class is nonempty.
        internal static bool CanReuseOpaqueDepth(
            int decalCount, int translucentCount, bool forceLegacy = false)
            => !forceLegacy && decalCount == 0 && translucentCount == 0;

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
        private long _retainedDepthReplaySkips;
        private long _retainedOpaqueReplaySubmissionsSaved;
        internal long RetainedDepthReplaySkips => _retainedDepthReplaySkips;
        internal long RetainedOpaqueReplaySubmissionsSaved => _retainedOpaqueReplaySubmissionsSaved;

        private void CaptureRetainedRenderWorld()
        {
            // Native state hashes include allocation-local GL texture handles.
            // Private replay reconstruction can relocate those handles while
            // retaining exact native state; sorting by their hash changes depth
            // ties on coplanar geometry. Keep the original packet sequence there.
            _retainedRenderWorld.Capture(_nonDecalItems, _decalItems, _translucentItems,
                Services.IsReplica ? Mods.Render.RetainedOpaqueOrder.Submission
                    : Mods.Render.RetainedOpaqueOrder.StateSorted);
            Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.RoomDescriptorHits, _retainedRenderWorld.RoomDescriptorCacheHits);
            Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.RoomDescriptorLookups, _retainedRenderWorld.MeshDescriptorLookups);
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

        private int[] _retainedGlOpaqueBatchIds = Array.Empty<int>();
        private static readonly bool _roomGroupsEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("ROOM_GROUPS");
        private static readonly bool _roomBatchCacheEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("ROOM_CACHE");
        private readonly List<Mods.Render.RetainedRoomBatchPlan> _roomBatchPlans = new();
        private ulong _roomBatchPlanRevision;
        private static readonly bool _meshObjectEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("MESH_OBJECT");
        private static readonly bool _proceduralBatchEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("PROCEDURAL_BATCH");
        private static readonly bool _proceduralStreamEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("PROCEDURAL_STREAM");
#if !MPHREAD_SERVER && !ANDROID
        private Mods.Render.DesktopProceduralStream? _proceduralStream;
#endif
#if !MPHREAD_SERVER
        private Mods.Render.DepthReplaySnapshot? _depthReplaySnapshot;
#endif
        private static readonly bool _depthKeepEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("DEPTH_KEEP");
        private static readonly bool _depthBatchEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("DEPTH_BATCH");

        private bool TryDrawRetainedOpaqueBatch(
            Mods.Render.WorldRenderPassKind kind,
            IReadOnlyList<Mods.Render.RetainedDrawPacket> packets,
            Mods.Render.RetainedDrawBatch batch, int batchIndex, out bool firstPrepared)
        {
            firstPrepared = false;
            if (NcxExcludeBatch(packets, batch)) return false;
#if !MPHREAD_SERVER
            if (!Mods.Render.RetainedOpaqueBatchPolicy.CanBatchPass(kind,
                    _depthBatchEnabled)
                || batch.Count < 2 || !GL.RetainedGeometryBatchingEnabled)
                return false;
            Mods.Render.RetainedDrawPacket first = packets[batch.Start];
            int[] ids;
            ulong? preparedHash = null;
            Mods.Render.RetainedRoomBatchPlan? drawPlan = null;
            if ((_roomBatchCacheEnabled || _roomGroupsEnabled) && batchIndex < Mods.Render.RetainedRoomBatchPlan.MaxPlans
                && batch.Count <= Mods.Render.RetainedRoomBatchPlan.MaxIds)
            {
                while (_roomBatchPlans.Count <= batchIndex)
                    _roomBatchPlans.Add(new Mods.Render.RetainedRoomBatchPlan());
                var plan = _roomBatchPlans[batchIndex];
                if (!plan.Prepare(_roomBatchPlanRevision, packets, batch)) return false;
                drawPlan = plan;
                ids = plan.Ids;
                preparedHash = plan.Hash;
            }
            else
            {
                for (int offset = 1; offset < batch.Count; offset++)
                    if (!Mods.Render.RetainedOpaqueBatchPolicy.CanMerge(first, packets[batch.Start + offset]))
                        return false;
                if (_retainedGlOpaqueBatchIds.Length < batch.Count)
                    Array.Resize(ref _retainedGlOpaqueBatchIds, Math.Max(16, batch.Count));
                for (int offset = 0; offset < batch.Count; offset++)
                    _retainedGlOpaqueBatchIds[offset] = packets[batch.Start + offset].Mesh.ListId;
                ids = _retainedGlOpaqueBatchIds;
            }
            // Prepare the shared uniforms once, but do not draw any geometry
            // until the indexed atlas confirms every list is promotable.
            RenderItem(first.Item, applySharedState: true,
                retainedMesh: first.Mesh, drawGeometry: false);
            firstPrepared = true;
            return _roomGroupsEnabled && drawPlan != null
                ? GL.TryDrawRetainedBatch(drawPlan)
                : GL.TryDrawRetainedBatch(ids, batch.Count, preparedHash);
#else
            return false;
#endif
        }

        private void DrawRenderGraphPackets(Mods.Render.WorldRenderPassKind kind)
        {
            IReadOnlyList<Mods.Render.RetainedDrawPacket> packets = PacketsFor(kind);
            IReadOnlyList<Mods.Render.RetainedDrawBatch> batches = BatchesFor(kind);
            RenderItem? previousMesh = null;
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                Mods.Render.RetainedDrawBatch batch = batches[batchIndex];
                if (TryDrawRetainedOpaqueBatch(kind, packets, batch, batchIndex, out bool firstPrepared))
                { previousMesh = null; continue; }
                // A failed atlas allocation/preflight has already prepared the first
                // item. Retain that exact state for its ordinary draw fallback.
                bool sharedStateValid = firstPrepared;
                for (int offset = 0; offset < batch.Count; offset++)
                {
                    Mods.Render.RetainedDrawPacket packet = packets[batch.Start + offset];
                    RenderItem item = packet.Item;
                    if (NcxExcludeItem(item))
                    { sharedStateValid = false; previousMesh = null; continue; }
                    if (kind == Mods.Render.WorldRenderPassKind.MarkTranslucent)
                        GL.StencilFunc(StencilFunction.Greater, item.PolygonId, 0xFF);
                    else if (kind == Mods.Render.WorldRenderPassKind.TranslucentBehind)
                        GL.StencilFunc(StencilFunction.Notequal, item.PolygonId, 0xFF);
                    else if (kind == Mods.Render.WorldRenderPassKind.TranslucentFront)
                        GL.StencilFunc(StencilFunction.Equal, item.PolygonId, 0xFF);
                    _retainedCompatibilityWorldDraws++;
                    bool reuseObject = _proceduralBatchEnabled && sharedStateValid && offset > 0
                        && Mods.Render.ProceduralStateReuse.CanReuseObjectState(
                            packets[batch.Start + offset - 1].Item, item);
                    bool reuseMesh = _meshObjectEnabled && previousMesh != null
                        && Mods.Render.MeshObjectStateReuse.CanReuse(previousMesh, item);
                    if (reuseMesh) Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.MeshObjectReuses);
                    if (Mods.Render.ProceduralStateReuse.IsProcedural(item.Type))
                    {
                        Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.ProceduralItems);
                        if (sharedStateValid) Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.ProceduralMaterialReuses);
                        if (reuseObject) Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.ProceduralObjectReuses);
                    }
                    RenderItem(item, applySharedState: !sharedStateValid,
                        retainedMesh: packet.Mesh, applyObjectState: !(reuseObject || reuseMesh || offset == 0 && firstPrepared));
                    previousMesh = item.Type == RenderItemType.Mesh ? item : null;
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
        private static readonly bool _depthVertexEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("DEPTH_VERTEX");
        private static readonly bool _depthShaderEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("DEPTH_SHADER");
        private void SetDepthShader(bool colorMasked)
        {
            _depthOnlyWorldPass = colorMasked && _depthShaderEnabled;
            GL.Uniform1(_shaderLocations.DepthOnly, _depthOnlyWorldPass ? 1 : 0);
            GL.Uniform1(_shaderLocations.DepthVertexFast, _depthOnlyWorldPass && _depthVertexEnabled ? 1 : 0);
        }

        private void ExecuteWorldRenderGraph()
        {
            // Scope merge proofs to this exact execution, never a prior frame's
            // visibility, light or pose. Only the bounded ID storage is retained.
            unchecked { _roomBatchPlanRevision++; }
            bool reuseOpaqueDepth = Mods.Render.WorldRenderGraph.CanReuseOpaqueDepth(
                _retainedRenderWorld.Decals.Count, _retainedRenderWorld.Translucent.Count,
                Mods.Render.OpenGlSubmissionProfiler.ForceLegacyDepthReplay);
#if !MPHREAD_SERVER
            bool depthCaptured = false;
            bool snapshotDepth = false;
            bool replayEffectsKnown = false;
            Mods.Render.DisplayListAttributeEffect replayEffects = default;
            if (_depthKeepEnabled && !reuseOpaqueDepth
                && !Mods.Render.OpenGlSubmissionProfiler.ForceLegacyDepthReplay)
            {
                replayEffectsKnown = Mods.Render.DepthReplayAttributes.TryResolve(
                    _retainedRenderWorld.Opaque, _showTextures, out replayEffects);
                if (replayEffectsKnown)
                {
                    reuseOpaqueDepth = Mods.Render.DepthReplayPolicy.CanKeepOpaqueDepth(
                        _retainedRenderWorld.Decals.Count, _retainedRenderWorld.Translucent,
                        _showTextures, _drawingPlayerOutlineMask);
                    snapshotDepth = !reuseOpaqueDepth && _retainedRenderWorld.Opaque.Count >= 8;
                }
            }
#endif
            bool profile = Mods.Render.OpenGlSubmissionProfiler.BeginWorldGraph();
#if !MPHREAD_SERVER
            GL.BeginScopedWorldStateElision();
#endif
            try
            {
#if !MPHREAD_SERVER && !ANDROID
            if (_proceduralStreamEnabled)
                (_proceduralStream ??= new()).Prepare(_retainedRenderWorld.Opaque,
                    _retainedRenderWorld.Decals, _retainedRenderWorld.Translucent);
#endif
            foreach (Mods.Render.WorldRenderGraphPass pass in _worldRenderGraph.Passes)
            {
                using var cpuTiming = Mods.Render.OpenGlPassProfiler.Measure(
                    (Mods.Render.OpenGlCpuPass)((int)Mods.Render.OpenGlCpuPass.Opaque + (int)pass.Kind));
                SetDepthShader(pass.Kind is Mods.Render.WorldRenderPassKind.MarkTranslucent
                    or Mods.Render.WorldRenderPassKind.RebuildDepth);
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
#if !MPHREAD_SERVER
                    if (snapshotDepth)
                        depthCaptured = (_depthReplaySnapshot ??= new()).Capture(
                            _frameBuffer, _targetSize.X, _targetSize.Y);
#endif
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
#if !MPHREAD_SERVER
                    if (depthCaptured && _depthReplaySnapshot!.Restore(_frameBuffer, _targetSize.X, _targetSize.Y))
                        reuseOpaqueDepth = true;
#endif
                    if (!reuseOpaqueDepth)
                        GL.Clear(ClearBufferMask.DepthBufferBit);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep,
                        StencilOp.Keep);
                    GL.StencilFunc(StencilFunction.Always, 0, 0xFF);
                    GL.AlphaFunc(AlphaFunction.Equal, 1.0f);
                    if (reuseOpaqueDepth)
                    {
#if !MPHREAD_SERVER
                        if (replayEffectsKnown && _retainedRenderWorld.Opaque.Count != 0)
                        {
                            RenderItem(_retainedRenderWorld.Opaque[^1].Item, applySharedState: true, drawGeometry: false);
                            FinishViewModelRenderRun();
                            Mods.Render.DepthReplayAttributes.Apply(replayEffects);
                        }
#endif
                        Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.DepthReplayDrawsSaved, _retainedRenderWorld.Opaque.Count);
                        _retainedDepthReplaySkips++;
                        _retainedOpaqueReplaySubmissionsSaved += _retainedRenderWorld.Opaque.Count;
                    }
                    else
                    {
                        DrawRenderGraphPackets(pass.Kind);
                    }
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
            finally
            {
#if !MPHREAD_SERVER && !ANDROID
                _proceduralStream?.End();
#endif
                SetDepthShader(false);
#if !MPHREAD_SERVER
                _depthReplaySnapshot?.End();
                GL.EndScopedWorldStateElision();
#endif
                if (profile)
                    Mods.Render.OpenGlSubmissionProfiler.EndWorldGraph(
                        _retainedRenderWorld.PacketCount, _retainedRenderWorld.BatchCount,
                        reuseOpaqueDepth ? _retainedRenderWorld.Opaque.Count : 0);
            }
        }
    }
}
