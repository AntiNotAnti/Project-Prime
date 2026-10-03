using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

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
    /// Immutable frame-local submission record. The RenderItem remains pooled by Scene;
    /// packets only retain a reference for the lifetime of the current picture.
    /// Sequence is deliberately preserved so the first retained-renderer slice cannot
    /// perturb the cartridge renderer's ordering-sensitive decal/translucency behavior.
    /// </summary>
    internal readonly struct RetainedDrawPacket
    {
        internal RenderItem Item { get; }
        internal int Sequence { get; }
        internal ulong StateKey { get; }

        internal RetainedDrawPacket(RenderItem item, int sequence)
        {
            Item = item;
            Sequence = sequence;
            StateKey = BuildStateKey(item);
        }

        private static ulong BuildStateKey(RenderItem item)
        {
            unchecked
            {
                // Stable enough for diagnostics and future state sorting, but unused
                // for ordering until parity gates explicitly permit reordering.
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

    /// <summary>
    /// Reusable retained view of the frame's draw submissions. Lists keep their
    /// capacity between pictures, so once a scene reaches steady state this layer
    /// adds no per-frame packet-object allocation.
    ///
    /// This is intentionally a bridge: entities still build RenderItems today.
    /// Later slices can retain static room packets across frames without changing
    /// the render graph or pass executor introduced here.
    /// </summary>
    internal sealed class RetainedRenderWorld
    {
        private readonly List<RetainedDrawPacket> _opaque = new(256);
        private readonly List<RetainedDrawPacket> _decals = new(64);
        private readonly List<RetainedDrawPacket> _translucent = new(128);

        internal IReadOnlyList<RetainedDrawPacket> Opaque => _opaque;
        internal IReadOnlyList<RetainedDrawPacket> Decals => _decals;
        internal IReadOnlyList<RetainedDrawPacket> Translucent => _translucent;
        internal int PacketCount => _opaque.Count + _decals.Count + _translucent.Count;
        internal ulong FrameRevision { get; private set; }

        internal void Capture(IReadOnlyList<RenderItem> nonDecal,
            IReadOnlyList<RenderItem> decals, IReadOnlyList<RenderItem> translucent)
        {
            CaptureList(_opaque, nonDecal);
            CaptureList(_decals, decals);
            CaptureList(_translucent, translucent);
            FrameRevision++;
        }

        internal void Clear()
        {
            _opaque.Clear();
            _decals.Clear();
            _translucent.Clear();
        }

        private static void CaptureList(List<RetainedDrawPacket> destination,
            IReadOnlyList<RenderItem> source)
        {
            destination.Clear();
            if (destination.Capacity < source.Count)
                destination.Capacity = source.Count;
            for (int i = 0; i < source.Count; i++)
                destination.Add(new RetainedDrawPacket(source[i], i));
        }
    }

    internal readonly record struct WorldRenderGraphPass(
        WorldRenderPassKind Kind,
        string Name,
        WorldRenderResource Reads,
        WorldRenderResource Writes);

    /// <summary>
    /// Explicit graph for the ordering-sensitive MPH world passes. Resource
    /// declarations are descriptive in this first slice; subsequent native
    /// render-graph work can use the same graph to allocate/alias attachments
    /// and schedule backend-native passes.
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

        private void DrawRenderGraphPackets(Mods.Render.WorldRenderPassKind kind)
        {
            IReadOnlyList<Mods.Render.RetainedDrawPacket> packets = PacketsFor(kind);
            for (int i = 0; i < packets.Count; i++)
            {
                RenderItem item = packets[i].Item;
                if (kind == Mods.Render.WorldRenderPassKind.MarkTranslucent)
                    GL.StencilFunc(StencilFunction.Greater, item.PolygonId, 0xFF);
                else if (kind == Mods.Render.WorldRenderPassKind.TranslucentBehind)
                    GL.StencilFunc(StencilFunction.Notequal, item.PolygonId, 0xFF);
                else if (kind == Mods.Render.WorldRenderPassKind.TranslucentFront)
                    GL.StencilFunc(StencilFunction.Equal, item.PolygonId, 0xFF);
                RenderItem(item);
            }
        }

        /// <summary>
        /// Execute the established six-pass MPH world renderer through an explicit
        /// graph. State transitions intentionally match the prior inline code.
        /// No sorting or pass merging is allowed in this migration slice.
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
                    // Preserve cartridge ordering until a later parity-gated
                    // material sort proves decals can be reordered safely.
                    GL.DepthFunc(DepthFunction.Lequal);
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    DrawRenderGraphPackets(pass.Kind);
                    GL.PolygonOffset(0, 0);
                    GL.Disable(EnableCap.PolygonOffsetFill);
                    break;

                case Mods.Render.WorldRenderPassKind.MarkTranslucent:
                    GL.Enable(EnableCap.AlphaTest);
                    GL.AlphaFunc(AlphaFunction.Less, 1.0f);
                    GL.ColorMask(false, false, false, false);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                    DrawRenderGraphPackets(pass.Kind);
                    break;

                case Mods.Render.WorldRenderPassKind.RebuildDepth:
                    GL.Clear(ClearBufferMask.DepthBufferBit);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
                    GL.StencilFunc(StencilFunction.Always, 0, 0xFF);
                    GL.AlphaFunc(AlphaFunction.Equal, 1.0f);
                    DrawRenderGraphPackets(pass.Kind);
                    break;

                case Mods.Render.WorldRenderPassKind.TranslucentBehind:
                    GL.AlphaFunc(AlphaFunction.Less, 1.0f);
                    GL.ColorMask(true, true, true, true);
                    GL.DepthMask(false);
                    GL.DepthFunc(DepthFunction.Lequal);
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
                    DrawRenderGraphPackets(pass.Kind);
                    break;

                case Mods.Render.WorldRenderPassKind.TranslucentFront:
                    GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
                    DrawRenderGraphPackets(pass.Kind);
                    break;
                }
            }

            GL.DepthMask(true);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.StencilTest);
            GL.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);
        }
    }
}
