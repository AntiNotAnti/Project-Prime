using System;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class NormalMappingTests
{
    [Fact]
    public void IndexedQuadGeneratesOrthogonalPositiveHandedTangents()
    {
        RenderVertex[] vertices =
        {
            Vertex(0, 0, 0, 0), Vertex(1, 0, 1, 0),
            Vertex(1, 1, 1, 1), Vertex(0, 1, 0, 1)
        };

        RenderVertex[] generated = MeshTangentGenerator.Generate(vertices,
            new[] { 0, 1, 2, 0, 2, 3 });

        Assert.All(generated, vertex =>
        {
            AssertVectorNear(Vector3.UnitX, vertex.Tangent.Xyz);
            Assert.Equal(1, vertex.Tangent.W);
            Assert.InRange(MathF.Abs(Vector3.Dot(vertex.Normal,
                vertex.Tangent.Xyz)), 0, 0.00001f);
        });
    }

    [Fact]
    public void MirroredUvTriangleRecordsNegativeHandedness()
    {
        RenderVertex[] generated = MeshTangentGenerator.Generate(new[]
        {
            Vertex(0, 0, 0, 0), Vertex(1, 0, -1, 0),
            Vertex(0, 1, 0, 1)
        }, new[] { 0, 1, 2 });

        Assert.All(generated, vertex =>
        {
            AssertVectorNear(-Vector3.UnitX, vertex.Tangent.Xyz);
            Assert.Equal(-1, vertex.Tangent.W);
        });
    }

    [Fact]
    public void DegenerateAndNonFiniteInputsProduceFiniteFallbackTangents()
    {
        RenderVertex[] generated = MeshTangentGenerator.Generate(new[]
        {
            Vertex(0, 0, 0, 0), Vertex(1, 0, 0, 0),
            new RenderVertex(new Vector3(float.NaN, 1, 0), Vector4.One,
                Vector3.UnitZ, Vector2.Zero)
        }, new[] { 0, 1, 2 });

        Assert.All(generated, vertex =>
        {
            Assert.True(float.IsFinite(vertex.Tangent.X));
            Assert.True(float.IsFinite(vertex.Tangent.Y));
            Assert.True(float.IsFinite(vertex.Tangent.Z));
            Assert.Equal(1, vertex.Tangent.W);
            Assert.InRange(vertex.Tangent.Xyz.Length, .9999f, 1.0001f);
        });
    }

    [Fact]
    public void SeparateHardEdgeVerticesAreNeverWelded()
    {
        RenderVertex[] source =
        {
            Vertex(0, 0, 0, 0), Vertex(1, 0, 1, 0), Vertex(0, 1, 0, 1),
            Vertex(0, 0, 0, 0), Vertex(1, 0, 0, 1), Vertex(0, 1, 1, 0)
        };

        RenderVertex[] generated = MeshTangentGenerator.Generate(source,
            new[] { 0, 1, 2, 3, 4, 5 });

        Assert.Equal(source.Length, generated.Length);
        AssertVectorNear(Vector3.UnitX, generated[0].Tangent.Xyz);
        AssertVectorNear(Vector3.UnitY, generated[3].Tangent.Xyz);
    }

    [Fact]
    public void InvalidIndexStreamsAreRejected()
    {
        RenderVertex[] vertices = { Vertex(0, 0, 0, 0) };
        Assert.Throws<ArgumentException>(() =>
            MeshTangentGenerator.Generate(vertices, new[] { 0, 0 }));
        Assert.Throws<ArgumentException>(() =>
            MeshTangentGenerator.Generate(vertices, new[] { 0, 0, 1 }));
    }

    [Fact]
    public void NormalMapsAreEnhancedTriangleLightingOnly()
    {
        TextureIdentity normal = new(new object());
        RenderMaterial material = new()
        {
            Lighting = true,
            Textured = true,
            TexgenMode = TexgenMode.Texcoord,
            Enhanced = new EnhancedMaterial(null, normal, null, 0,
                EnhancedMaterial.DefaultSmoothness, 0, Vector3.One, 0)
        };

        Assert.True(SdlGpuNormalMappingPolicy.IsEnabled(GraphicsPreset.Enhanced,
            frameLighting: true, showTextures: true, material,
            RenderTopology.Triangles));
        Assert.False(SdlGpuNormalMappingPolicy.IsEnabled(GraphicsPreset.Original,
            frameLighting: true, showTextures: true, material,
            RenderTopology.Triangles));
        Assert.False(SdlGpuNormalMappingPolicy.IsEnabled(GraphicsPreset.Performance,
            frameLighting: true, showTextures: true, material,
            RenderTopology.Triangles));
        Assert.False(SdlGpuNormalMappingPolicy.IsEnabled(GraphicsPreset.Enhanced,
            frameLighting: true, showTextures: true, material,
            RenderTopology.Lines));
        material.TexgenMode = TexgenMode.Normal;
        Assert.False(SdlGpuNormalMappingPolicy.IsEnabled(GraphicsPreset.Enhanced,
            frameLighting: true, showTextures: true, material,
            RenderTopology.Triangles));
        Assert.Equal(72, SdlGpuSceneVertexAbi.ByteSize);
        Assert.Equal(56, SdlGpuSceneVertexAbi.TangentOffset);
        Assert.Equal(7, SdlGpuSceneVertexAbi.AttributeCount);
    }

    [Theory]
    [InlineData(-1, 0, 0, 1, -1, 0, 0, 1)]
    [InlineData(1, 0, 0, -1, 1, 0, 0, -1)]
    [InlineData(0, -1, -1, 0, 0, -1, -1, 0)]
    public void SignedUvBasisTransformPreservesReflections(float a, float b,
        float c, float d, float tx, float ty, float bx, float by)
    {
        (Vector3 tangent, Vector3 bitangent)
            = SdlGpuNormalMappingPolicy.TransformUvBasis(
                Vector3.UnitX, Vector3.UnitY, a, b, c, d);

        AssertVectorNear(new Vector3(tx, ty, 0), tangent);
        AssertVectorNear(new Vector3(bx, by, 0), bitangent);
    }

    private static RenderVertex Vertex(float x, float y, float u, float v)
        => new(new Vector3(x, y, 0), Vector4.One, Vector3.UnitZ,
            new Vector2(u, v));

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(actual.X, expected.X - .00001f, expected.X + .00001f);
        Assert.InRange(actual.Y, expected.Y - .00001f, expected.Y + .00001f);
        Assert.InRange(actual.Z, expected.Z - .00001f, expected.Z + .00001f);
    }
}
