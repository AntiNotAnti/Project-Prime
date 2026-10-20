using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;

namespace MphRead.Mods.Campaign;

/// <summary>Private CPU widget evaluation on its creating thread. No Scene, clock, IO, cache or GPU ownership.</summary>
internal sealed class CampaignCreditsWidgetSampler
{
    internal readonly record struct Vertex(Vector3 Position, Vector2 Texcoord);
    internal readonly record struct TextureKey(string ModelSha256, int TextureId, int PaletteId);
    internal readonly record struct Triangle(Vertex A, Vertex B, Vertex C, Vector4 Color,
        TextureKey Texture, RepeatMode WrapS, RepeatMode WrapT, float Depth);
    internal sealed class Snapshot
    {
        internal int AnimationFrame { get; }
        internal IReadOnlyList<Triangle> Triangles { get; }
        internal Snapshot(int frame, Triangle[] triangles) { AnimationFrame = frame; Triangles = Array.AsReadOnly(triangles); }
    }
    private readonly record struct SourceVertex(Vector3 Position, Vector2 Texcoord, int Matrix);
    private sealed record Geometry(SourceVertex[] Vertices, int[] Indices);
    private readonly ModelInstance _instance;
    private readonly Geometry[] _geometry;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly int _triangleCount;
    internal string SourceSha256 { get; }
    internal string? AnimationSha256 { get; }
    internal int FrameCount { get; }

    internal CampaignCreditsWidgetSampler(CampaignCreditsModelSource.Snapshot source,
        CampaignCreditsAnimationBinding.Snapshot? animation = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _instance = new ModelInstance(MphRead.Read.ReadCreditsModel(source, animation));
        SourceSha256 = source.SourceSha256; AnimationSha256 = animation?.SourceSha256; FrameCount = animation?.FrameCount ?? 1;
        _triangleCount = source.TriangleCount;
        // Shared primitive expansion preserves the engine's source winding, including degeneracy.
        _geometry = new Geometry[source.MeshCount]; var batch = new LegacyGeometryBatch();
        int totalVertices = 0, totalTriangles = 0;
        for (int meshId = 0; meshId < _geometry.Length; meshId++)
        {
            batch.Clear(); Model model = _instance.Model; Mesh mesh = model.Meshes[meshId];
            Material material = model.Materials[mesh.MaterialId];
            int width = 0, height = 0;
            if (material.TextureId >= 0)
            { var texture = model.Recolors[0].Textures[material.TextureId]; width = texture.Width; height = texture.Height; }
            Vector3 position = Vector3.Zero; Vector2 uv = Vector2.Zero; int matrix = 0;
            foreach (RenderInstruction instruction in model.RenderInstructionLists[mesh.DlistId])
            {
                var a = instruction.Arguments; uint code = ((uint)instruction.Code - 0x400) / 4;
                switch (code)
                {
                    case 0: case 0x21: break; // No lighting in flat menu triangles.
                    case 0x14: matrix = (int)a[0]; break;
                    case 0x22:
                        if (width > 0 && height > 0) uv = new((short)(a[0] & 0xffff) / (16f * width), (short)(a[0] >> 16) / (16f * height));
                        break;
                    case 0x40: batch.Begin(a[0] == 1 ? PrimitiveType.Quads : PrimitiveType.QuadStrip); break;
                    case 0x41: batch.End(); break;
                    case 0x23:
                        position = new(Fixed.ToFloat((short)(a[0] & 0xffff)), Fixed.ToFloat((short)(a[0] >> 16)), Fixed.ToFloat((short)(a[1] & 0xffff)));
                        Add(); break;
                    case 0x24:
                        position = new(Signed10(a[0], 0) / 64f, Signed10(a[0], 10) / 64f, Signed10(a[0], 20) / 64f);
                        Add(); break;
                    case 0x25:
                        position.X = Fixed.ToFloat((short)(a[0] & 0xffff)); position.Y = Fixed.ToFloat((short)(a[0] >> 16)); Add(); break;
                    case 0x26:
                        position.X = Fixed.ToFloat((short)(a[0] & 0xffff)); position.Z = Fixed.ToFloat((short)(a[0] >> 16)); Add(); break;
                    default: throw new InvalidDataException("Admitted credits geometry contradicts its command profile.");
                }
            }
            void Add() => batch.AddVertex(position, Vector4.One, Vector3.UnitZ, new Vector3(uv.X, uv.Y, matrix), hasOwnColor: false);
            Require(batch.LineIndices.Count == 0 && batch.TriIndices.Count % 3 == 0, "Credits geometry expanded to an unsupported primitive.");
            var vertices = new SourceVertex[batch.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                int at = i * LegacyGeometryBatch.FloatsPerVertex; var v = batch.Vertices;
                vertices[i] = new(new(v[at], v[at + 1], v[at + 2]), new(v[at + 10], v[at + 11]), (int)v[at + 12]);
            }
            totalVertices += vertices.Length; totalTriangles += batch.TriIndices.Count / 3;
            Require(totalVertices <= MaxVertices && totalTriangles <= MaxTriangles, "Credits decoded geometry exceeds its admission.");
            _geometry[meshId] = new(vertices, batch.TriIndices.ToArray());
        }
        Require(totalVertices == source.VertexCount && totalTriangles == source.TriangleCount,
            "Credits decoded geometry differs from its admitted counts.");
    }

    internal Snapshot Sample(long age, bool loop, float alpha = 1)
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Credits sampler belongs to its creating thread.");
        if (age < 0) throw new ArgumentOutOfRangeException(nameof(age));
        if (!float.IsFinite(alpha) || alpha < 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        Model model = _instance.Model; var info = _instance.AnimInfo; var groups = model.AnimationGroups;
        info.Node.Slot = info.Material.Slot = info.Texture.Slot = info.Texcoord.Slot = 0;
        info.Node.Group = groups.Node.Count > 0 ? groups.Node[0] : null;
        info.Material.Group = groups.Material.Count > 0 ? groups.Material[0] : null;
        info.Texture.Group = groups.Texture.Count > 0 ? groups.Texture[0] : null;
        info.Texcoord.Group = groups.Texcoord.Count > 0 ? groups.Texcoord[0] : null;
        info.Frame[0] = Phase(info.Material.Group?.FrameCount ?? 0); model.AnimateMaterials(info);
        info.Frame[0] = Phase(info.Texture.Group?.FrameCount ?? 0); model.AnimateTextures(info);
        model.ComputeNodeMatrices(0); info.Frame[0] = Phase(info.Node.Group?.FrameCount ?? 0);
        model.AnimateNodes(0, true, Matrix4.Identity, model.Scale, info); model.UpdateMatrixStack();
        var triangles = new List<Triangle>(_triangleCount);
        EmitNode(0);
        return new(Phase(FrameCount), triangles.ToArray());

        int Phase(int frames) => frames <= 1 ? 0 : loop ? (int)(age % frames) : (int)Math.Min(age, frames - 1L);
        void EmitNode(int index)
        {
            for (int at = index; at != -1; at = model.Nodes[at].NextIndex)
            {
                Node node = model.Nodes[at];
                if (!node.Enabled) continue;
                for (int m = node.MeshId / 2; m < node.MeshId / 2 + node.MeshCount; m++) EmitMesh(m);
                if (node.ChildIndex != -1) EmitNode(node.ChildIndex);
            }
        }
        void EmitMesh(int meshId)
        {
            Mesh mesh = model.Meshes[meshId]; if (!mesh.Visible) return;
            Material material = model.Materials[mesh.MaterialId]; float opacity = material.CurrentAlpha * alpha;
            Vector4 color = new(material.CurrentDiffuse, opacity);
            Require(Unit(color), "Credits evaluated material color is nonfinite or outside its domain.");
            if (opacity <= 0) return;
            int texture = material.CurrentTextureId, palette = material.CurrentPaletteId;
            Require((texture == -1 && palette == -1) || (texture >= 0 && texture < model.Recolors[0].Textures.Count
                && palette >= 0 && palette < model.Recolors[0].Palettes.Count), "Credits evaluated texture binding is invalid.");
            Geometry geometry = _geometry[meshId];
            for (int i = 0; i < geometry.Indices.Length; i += 3)
            {
                Vertex a = Transform(geometry.Vertices[geometry.Indices[i]]), b = Transform(geometry.Vertices[geometry.Indices[i + 1]]),
                    c = Transform(geometry.Vertices[geometry.Indices[i + 2]]);
                float z = (a.Position.Z + b.Position.Z + c.Position.Z) / 3;
                Require(float.IsFinite(z) && triangles.Count < _triangleCount, "Credits evaluated triangles exceed their finite admission.");
                triangles.Add(new(a, b, c, color, new(SourceSha256, texture, palette), material.XRepeat, material.YRepeat, z));
            }
        }
        Vertex Transform(SourceVertex vertex)
        {
            int at = vertex.Matrix * 16; var s = model.MatrixStackValues;
            Require(at >= 0 && at + 16 <= s.Count, "Credits vertex matrix is outside its admitted stack.");
            var matrix = new Matrix4(s[at], s[at + 1], s[at + 2], s[at + 3], s[at + 4], s[at + 5], s[at + 6], s[at + 7],
                s[at + 8], s[at + 9], s[at + 10], s[at + 11], s[at + 12], s[at + 13], s[at + 14], s[at + 15]);
            Vector4 transformed = new Vector4(vertex.Position, 1) * matrix;
            Vector3 position = transformed.Xyz * model.Scale.X;
            Require(Finite(position) && float.IsFinite(vertex.Texcoord.X) && float.IsFinite(vertex.Texcoord.Y),
                "Credits evaluated vertex is nonfinite.");
            return new(position, vertex.Texcoord);
        }
    }
    private static int Signed10(uint value, int shift) { int v = (int)(value >> shift) & 1023; return v >= 512 ? v - 1024 : v; }
    private static bool Unit(Vector4 value) => float.IsFinite(value.X) && value.X is >= 0 and <= 1
        && float.IsFinite(value.Y) && value.Y is >= 0 and <= 1 && float.IsFinite(value.Z) && value.Z is >= 0 and <= 1
        && float.IsFinite(value.W) && value.W is >= 0 and <= 1;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
