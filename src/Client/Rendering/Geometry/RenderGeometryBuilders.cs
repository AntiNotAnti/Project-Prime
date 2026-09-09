using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Small, backend-neutral builders for geometry that used to be emitted
    /// with immediate-mode calls. The vertex order is kept in the same command
    /// order as the legacy renderer; MeshCompiler then supplies the exact
    /// indexed winding for the selected strip/fan topology.
    /// </summary>
    public static class RenderGeometryBuilders
    {
        public static CpuMesh FullscreenQuad()
        {
            return TriangleStrip(
                new[]
                {
                    new Vector3(1, 1, 0), new Vector3(-1, 1, 0),
                    new Vector3(1, -1, 0), new Vector3(-1, -1, 0)
                },
                new[]
                {
                    new Vector2(1, 1), new Vector2(0, 1),
                    new Vector2(1, 0), new Vector2(0, 0)
                });
        }

        public static CpuMesh TexturedQuad(Vector3 topRight, Vector3 topLeft,
            Vector3 bottomRight, Vector3 bottomLeft)
        {
            return TriangleStrip(
                new[] { topRight, topLeft, bottomRight, bottomLeft },
                new[]
                {
                    new Vector2(1, 0), new Vector2(0, 0),
                    new Vector2(1, 1), new Vector2(0, 1)
                });
        }

        public static CpuMesh SolidQuad(Vector3 topRight, Vector3 topLeft,
            Vector3 bottomRight, Vector3 bottomLeft, Vector4 color,
            bool explicitColor = false)
        {
            return TriangleStrip(
                new[] { topRight, topLeft, bottomRight, bottomLeft },
                texcoords: null, color: color, explicitColor: explicitColor);
        }

        public static CpuMesh TriangleStrip(IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector2>? texcoords = null, Vector4? color = null,
            bool explicitColor = false)
            => Compile(positions, MeshPrimitiveTopology.TriangleStrip, texcoords, color, explicitColor);

        public static CpuMesh TriangleFan(IReadOnlyList<Vector3> positions,
            Vector4? color = null, bool explicitColor = false)
            => Compile(positions, MeshPrimitiveTopology.TriangleFan, texcoords: null,
                color: color, explicitColor: explicitColor);

        public static CpuMesh LineLoop(IReadOnlyList<Vector3> positions,
            Vector4? color = null, bool explicitColor = false)
            => Compile(positions, MeshPrimitiveTopology.LineLoop, texcoords: null,
                color: color, explicitColor: explicitColor);

        private static CpuMesh Compile(IReadOnlyList<Vector3> positions,
            MeshPrimitiveTopology topology, IReadOnlyList<Vector2>? texcoords,
            Vector4? color, bool explicitColor)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (texcoords != null && texcoords.Count != positions.Count)
            {
                throw new ArgumentException("Texture coordinates must match the position count.",
                    nameof(texcoords));
            }
            Vector4 vertexColor = color ?? Vector4.One;
            RenderVertexFlags flags = explicitColor ? RenderVertexFlags.ExplicitColor : RenderVertexFlags.None;
            var vertices = new RenderVertex[positions.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new RenderVertex(positions[i], vertexColor, Vector3.UnitZ,
                    texcoords == null ? Vector2.Zero : texcoords[i], flags: flags);
            }
            return MeshCompiler.Compile(vertices, topology);
        }
    }
}
