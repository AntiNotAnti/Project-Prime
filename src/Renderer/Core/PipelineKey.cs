using System;
using MphRead.Mods;

namespace MphRead
{
    public enum RenderShaderVariant : byte
    {
        Scene,
        Hud,
        Cel,
        Fullscreen
    }

    public enum RenderBlendMode : byte
    {
        Opaque,
        Alpha
    }

    public enum RenderCullMode : byte
    {
        None,
        Front,
        Back
    }

    public enum RenderStencilMode : byte
    {
        Disabled,
        Clear,
        MarkTransparent,
        Preserve,
        EqualPolygon,
        NotEqualPolygon
    }

    public enum RenderDepthMode : byte
    {
        Disabled,
        Less,
        LessOrEqual
    }

    public enum RenderAlphaTestMode : byte
    {
        Disabled,
        EqualOne,
        LessThanOne
    }

    public enum RenderTopology : byte
    {
        Triangles,
        Lines
    }

    [Flags]
    public enum RenderColorWriteMask : byte
    {
        None = 0,
        Red = 1 << 0,
        Green = 1 << 1,
        Blue = 1 << 2,
        Alpha = 1 << 3,
        All = Red | Green | Blue | Alpha
    }

    /// <summary>
    /// Compact immutable description of pipeline-creation state. Per-draw
    /// constants (colours, transforms, lights) intentionally do not appear.
    /// </summary>
    public readonly struct PipelineKey : IEquatable<PipelineKey>
    {
        public RenderShaderVariant ShaderVariant { get; }
        public RenderBlendMode BlendMode { get; }
        public RenderCullMode CullMode { get; }
        public RenderDepthMode DepthMode { get; }
        public bool DepthWrite { get; }
        public RenderStencilMode StencilMode { get; }
        public RenderTopology Topology { get; }
        public int SampleCount { get; }
        public RenderAlphaTestMode AlphaTestMode { get; }
        public RenderColorWriteMask ColorWriteMask { get; }
        public bool DecalDepthBias { get; }
        public string TargetFormat { get; }
        public bool AlphaTest => AlphaTestMode != RenderAlphaTestMode.Disabled;

        public PipelineKey(RenderShaderVariant shaderVariant, RenderBlendMode blendMode,
            RenderCullMode cullMode, RenderDepthMode depthMode, bool depthWrite,
            RenderStencilMode stencilMode, RenderTopology topology, int sampleCount,
            bool alphaTest)
            : this(shaderVariant, blendMode, cullMode, depthMode, depthWrite, stencilMode,
                topology, sampleCount, alphaTest ? RenderAlphaTestMode.LessThanOne : RenderAlphaTestMode.Disabled,
                RenderColorWriteMask.All, decalDepthBias: false, targetFormat: string.Empty)
        {
        }

        public PipelineKey(RenderShaderVariant shaderVariant, RenderBlendMode blendMode,
            RenderCullMode cullMode, RenderDepthMode depthMode, bool depthWrite,
            RenderStencilMode stencilMode, RenderTopology topology, int sampleCount,
            RenderAlphaTestMode alphaTestMode)
            : this(shaderVariant, blendMode, cullMode, depthMode, depthWrite, stencilMode,
                topology, sampleCount, alphaTestMode, RenderColorWriteMask.All,
                decalDepthBias: false, targetFormat: string.Empty)
        {
        }

        public PipelineKey(RenderShaderVariant shaderVariant, RenderBlendMode blendMode,
            RenderCullMode cullMode, RenderDepthMode depthMode, bool depthWrite,
            RenderStencilMode stencilMode, RenderTopology topology, int sampleCount,
            RenderAlphaTestMode alphaTestMode, RenderColorWriteMask colorWriteMask,
            bool decalDepthBias, string? targetFormat)
        {
            if (sampleCount < 1) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            ShaderVariant = shaderVariant;
            BlendMode = blendMode;
            CullMode = cullMode;
            DepthMode = depthMode;
            DepthWrite = depthWrite;
            StencilMode = stencilMode;
            Topology = topology;
            SampleCount = sampleCount;
            AlphaTestMode = alphaTestMode;
            ColorWriteMask = colorWriteMask;
            DecalDepthBias = decalDepthBias;
            TargetFormat = targetFormat ?? string.Empty;
        }

        public static PipelineKey From(RenderMaterial material, RenderPrimitive primitive,
            RenderPassKind pass, int sampleCount = 1, string? targetFormat = null)
        {
            RenderBlendMode blend = pass is RenderPassKind.Decal or RenderPassKind.TransparentStencil
                or RenderPassKind.TransparentBehind or RenderPassKind.TransparentFront
                ? RenderBlendMode.Alpha : RenderBlendMode.Opaque;
            RenderDepthMode depth = pass is RenderPassKind.Decal or RenderPassKind.TransparentStencil
                or RenderPassKind.DepthRebuild or RenderPassKind.TransparentBehind
                or RenderPassKind.TransparentFront ? RenderDepthMode.LessOrEqual : RenderDepthMode.Less;
            bool depthWrite = pass is RenderPassKind.Opaque or RenderPassKind.Decal
                or RenderPassKind.TransparentStencil or RenderPassKind.DepthRebuild;
            RenderStencilMode stencil = pass switch
            {
                RenderPassKind.Opaque or RenderPassKind.Decal => RenderStencilMode.Clear,
                RenderPassKind.TransparentStencil => RenderStencilMode.MarkTransparent,
                RenderPassKind.DepthRebuild => RenderStencilMode.Preserve,
                RenderPassKind.TransparentBehind => RenderStencilMode.NotEqualPolygon,
                RenderPassKind.TransparentFront => RenderStencilMode.EqualPolygon,
                _ => RenderStencilMode.Disabled
            };
            RenderAlphaTestMode alphaTest = pass is RenderPassKind.Opaque or RenderPassKind.DepthRebuild
                ? RenderAlphaTestMode.EqualOne
                : pass is RenderPassKind.TransparentStencil or RenderPassKind.TransparentBehind
                    or RenderPassKind.TransparentFront
                    ? RenderAlphaTestMode.LessThanOne
                    : RenderAlphaTestMode.Disabled;
            // An N-gon submission owns both its fill triangle fan and its
            // optional edge loop. They are two emitted draws, not one
            // pipeline. The base key is the fill; callers use WithTopology
            // for the line emission after applying VolumeEdges/NoLines.
            RenderTopology topology = RenderTopology.Triangles;
            RenderColorWriteMask colorWriteMask = pass == RenderPassKind.TransparentStencil
                ? RenderColorWriteMask.None : RenderColorWriteMask.All;
            return new PipelineKey(RenderShaderVariant.Scene, blend,
                material.CullingMode switch
                {
                    CullingMode.Front => RenderCullMode.Front,
                    CullingMode.Back => RenderCullMode.Back,
                    _ => RenderCullMode.None
                }, depth, depthWrite, stencil, topology, sampleCount, alphaTest,
                colorWriteMask, decalDepthBias: pass == RenderPassKind.Decal,
                targetFormat ?? string.Empty);
        }

        /// <summary>
        /// Build the same neutral pipeline key using the frame's requested
        /// quality snapshot.  Capability negotiation remains a backend concern;
        /// this only carries the requested MSAA count into cache identity.
        /// </summary>
        public static PipelineKey From(RenderMaterial material, RenderPrimitive primitive,
            RenderPassKind pass, RenderQualitySnapshot quality, string? targetFormat = null)
            => From(material, primitive, pass, quality.MsaaSampleCount, targetFormat);

        public PipelineKey WithTopology(RenderTopology topology)
            => new PipelineKey(ShaderVariant, BlendMode, CullMode, DepthMode, DepthWrite,
                StencilMode, topology, SampleCount, AlphaTestMode, ColorWriteMask,
                DecalDepthBias, TargetFormat);

        public bool Equals(PipelineKey other)
        {
            return ShaderVariant == other.ShaderVariant && BlendMode == other.BlendMode
                && CullMode == other.CullMode && DepthMode == other.DepthMode
                && DepthWrite == other.DepthWrite && StencilMode == other.StencilMode
                && Topology == other.Topology && SampleCount == other.SampleCount
                && AlphaTestMode == other.AlphaTestMode
                && ColorWriteMask == other.ColorWriteMask
                && DecalDepthBias == other.DecalDepthBias
                && string.Equals(TargetFormat, other.TargetFormat, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is PipelineKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)ShaderVariant;
                hash = hash * 31 + (int)BlendMode;
                hash = hash * 31 + (int)CullMode;
                hash = hash * 31 + (int)DepthMode;
                hash = hash * 31 + (DepthWrite ? 1 : 0);
                hash = hash * 31 + (int)StencilMode;
                hash = hash * 31 + (int)Topology;
                hash = hash * 31 + SampleCount;
                hash = hash * 31 + (int)AlphaTestMode;
                hash = hash * 31 + (int)ColorWriteMask;
                hash = hash * 31 + (DecalDepthBias ? 1 : 0);
                return hash * 31 + StringComparer.Ordinal.GetHashCode(TargetFormat);
            }
        }

        public static bool operator ==(PipelineKey left, PipelineKey right) => left.Equals(right);
        public static bool operator !=(PipelineKey left, PipelineKey right) => !left.Equals(right);
    }

    public static class RenderPassClassifier
    {
        public static RenderPassKind Classify(RenderMaterial material)
            => Classify(material.RenderMode, material.Alpha);

        public static RenderPassKind Classify(RenderMode renderMode, float alpha)
        {
            if (renderMode == RenderMode.Decal) return RenderPassKind.Decal;
            if (renderMode == RenderMode.Translucent || alpha < 1f)
            {
                return RenderPassKind.TransparentStencil;
            }
            return RenderPassKind.Opaque;
        }
    }
}
