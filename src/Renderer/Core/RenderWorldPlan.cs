using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    [Flags]
    public enum RenderMeshStreams : byte
    {
        None = 0,
        Triangles = 1 << 0,
        Lines = 1 << 1
    }

    /// <summary>
    /// Immutable description of one visit through the legacy six-pass world
    /// algorithm. Backends consume this plan instead of reconstructing pass
    /// order or choosing a different submission list.
    /// </summary>
    public readonly record struct RenderWorldPass(
        RenderPassKind Kind,
        bool ClearDepthBefore,
        RenderBlendMode BlendMode,
        RenderDepthMode DepthMode,
        bool DepthWrite,
        RenderStencilMode StencilMode,
        RenderAlphaTestMode AlphaTestMode,
        RenderColorWriteMask ColorWriteMask,
        bool DecalDepthBias);

    public static class RenderWorldPlan
    {
        private static readonly RenderWorldPass[] _passes =
        {
            Pass(RenderPassKind.Opaque),
            Pass(RenderPassKind.Decal),
            Pass(RenderPassKind.TransparentStencil),
            Pass(RenderPassKind.DepthRebuild, clearDepthBefore: true),
            Pass(RenderPassKind.TransparentBehind),
            Pass(RenderPassKind.TransparentFront)
        };

        public static ReadOnlySpan<RenderWorldPass> Passes => _passes;

        public static IReadOnlyList<DrawSubmission> GetSubmissions(
            RenderFrame frame, RenderPassKind pass)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            return pass switch
            {
                RenderPassKind.Opaque or RenderPassKind.DepthRebuild => frame.OpaqueItems,
                RenderPassKind.Decal => frame.DecalItems,
                RenderPassKind.TransparentStencil or RenderPassKind.TransparentBehind
                    or RenderPassKind.TransparentFront => frame.TransparentItems,
                _ => throw new ArgumentOutOfRangeException(nameof(pass), pass,
                    "Pass is not part of the six-pass world plan.")
            };
        }

        public static object GetMeshIdentity(DrawSubmission submission)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (submission.Primitive != RenderPrimitive.Mesh) return submission;
            return submission.GeometryIdentity
                ?? throw new InvalidOperationException(
                    $"Mesh submission polygon {submission.PolygonId} has no geometry identity.");
        }

        public static CpuMesh ResolveMesh(RenderFrame frame, DrawSubmission submission)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            object identity = GetMeshIdentity(submission);
            if (!frame.MeshResources.TryGetValue(identity, out CpuMesh? mesh))
            {
                throw new InvalidOperationException(
                    $"Sealed scene frame is missing {submission.Primitive} geometry "
                    + $"for submission polygon {submission.PolygonId}.");
            }
            return mesh;
        }

        public static Matrix4 GetBillboardMatrix(RenderFrame frame, RenderMaterial material)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            return material.BillboardMode switch
            {
                BillboardMode.Sphere => frame.ViewInverseRotation,
                BillboardMode.Cylinder => frame.ViewInverseRotationY,
                _ => Matrix4.Identity
            };
        }

        public static RenderMeshStreams GetMeshStreams(
            DrawSubmission submission, RenderFrameOptions options)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (submission.Primitive != RenderPrimitive.Ngon)
            {
                return RenderMeshStreams.Triangles | RenderMeshStreams.Lines;
            }

            RenderMeshStreams streams = RenderMeshStreams.None;
            if (options.VolumeEdges != 1) streams |= RenderMeshStreams.Triangles;
            if (options.VolumeEdges != 2 && !submission.Material.NoLines)
                streams |= RenderMeshStreams.Lines;
            return streams;
        }

        private static RenderWorldPass Pass(RenderPassKind kind, bool clearDepthBefore = false)
        {
            PipelineKey pipeline = PipelineKey.From(default, RenderPrimitive.Mesh, kind);
            return new RenderWorldPass(kind, clearDepthBefore, pipeline.BlendMode,
                pipeline.DepthMode, pipeline.DepthWrite, pipeline.StencilMode,
                pipeline.AlphaTestMode, pipeline.ColorWriteMask,
                pipeline.DecalDepthBias);
        }
    }
}
