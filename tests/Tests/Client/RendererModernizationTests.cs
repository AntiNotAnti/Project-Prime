using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using SDL;
using Xunit;

[Collection("Match baseline globals")]
public sealed class RendererModernizationTests
{
    [Fact]
    public void HudTextureCommandFreezesCropRotationAlphaStageAndIdentity()
    {
        var identity = new TextureIdentity(new object(), variant: new object());
        var uv = new OpenTK.Mathematics.Vector4(.2f, .3f, .6f, .9f);
        RenderOverlayCommand command = ScenePresentation.CreateHudTextureCommand(
            new OpenTK.Mathematics.Vector2i(1920, 1080), identity,
            10, 20, 50, 60, new OpenTK.Mathematics.Vector4(.5f, .6f, .7f, .8f),
            uv, MathF.PI / 2, .4f, RenderPresentationStage.ReplayOverlay);

        Assert.Equal(RenderOverlayKind.HudTexture, command.Kind);
        Assert.Equal(identity, command.Texture);
        Assert.True(command.UseTexture);
        Assert.Equal(.4f, command.Alpha);
        Assert.Equal(RenderPresentationStage.ReplayOverlay, command.Stage);
        Assert.Equal(4, command.Vertices.Count);
        Assert.InRange(command.Vertices[0].TexCoord.X, .6999f, .7001f);
        Assert.InRange(command.Vertices[0].TexCoord.Y, .7999f, .8001f);
    }

    [Fact]
    public void HudGeometryCommandIsBoundedCopiedAndUsesCurrentStage()
    {
        var source = new List<HudGeometryVertex>
        {
            new(new OpenTK.Mathematics.Vector2(0, 0), OpenTK.Mathematics.Vector4.UnitX),
            new(new OpenTK.Mathematics.Vector2(10, 0), OpenTK.Mathematics.Vector4.UnitY),
            new(new OpenTK.Mathematics.Vector2(0, 10), OpenTK.Mathematics.Vector4.UnitZ)
        };
        RenderOverlayCommand command = ScenePresentation.CreateHudGeometryCommand(
            new OpenTK.Mathematics.Vector2i(256, 192), source,
            RenderPresentationStage.HudOverlay);
        source[0] = new HudGeometryVertex(new OpenTK.Mathematics.Vector2(99), OpenTK.Mathematics.Vector4.One);

        Assert.Equal(RenderOverlayKind.HudGeometry, command.Kind);
        Assert.Equal(RenderPresentationStage.HudOverlay, command.Stage);
        Assert.Equal(new OpenTK.Mathematics.Vector3(-1, 1, 0), command.Vertices[0].Position);
        Assert.Equal(OpenTK.Mathematics.Vector4.UnitX, command.Vertices[0].Color);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenePresentation.CreateHudGeometryCommand(
            new OpenTK.Mathematics.Vector2i(256, 192), Array.Empty<HudGeometryVertex>(),
            RenderPresentationStage.HudOverlay));
    }

    private static RenderVertex[] Vertices(int count)
        => Enumerable.Range(0, count)
            .Select(i => new RenderVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                new OpenTK.Mathematics.Vector2(i, i), (uint)i,
                i == 0 ? RenderVertexFlags.ExplicitColor : RenderVertexFlags.None))
            .ToArray();

    [Fact]
    public void SdlGpuMappedCopyHonorsArraySegmentOffsetCountAndDestinationGuards()
    {
        byte[] source = { 0x10, 0x20, 0x30, 0x40, 0x50 };
        IntPtr destination = Marshal.AllocHGlobal(7);
        try
        {
            for (int i = 0; i < 7; i++)
            {
                Marshal.WriteByte(destination, i, 0xCC);
            }

            SdlGpuMappedMemoryCopy.Copy(new ReadOnlyMemory<byte>(source, 1, 3),
                destination + 2);

            Assert.Equal(new byte[] { 0xCC, 0xCC, 0x20, 0x30, 0x40, 0xCC, 0xCC },
                Enumerable.Range(0, 7).Select(i => Marshal.ReadByte(destination, i)));
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
        }
    }

    [Fact]
    public void SdlGpuMappedCopySupportsNonArrayBackedMemory()
    {
        using var manager = new NonArrayMemoryManager(new byte[] { 0x10, 0x20, 0x30, 0x40 });
        ReadOnlyMemory<byte> source = manager.Memory.Slice(1, 2);
        Assert.False(MemoryMarshal.TryGetArray(source, out _));
        IntPtr destination = Marshal.AllocHGlobal(2);
        try
        {
            SdlGpuMappedMemoryCopy.Copy(source, destination);

            Assert.Equal(0x20, Marshal.ReadByte(destination, 0));
            Assert.Equal(0x30, Marshal.ReadByte(destination, 1));
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
        }
    }

    [Fact]
    public void SdlGpuMappedCopyAllowsEmptyInputWithNullDestination()
        => SdlGpuMappedMemoryCopy.Copy(ReadOnlyMemory<byte>.Empty, IntPtr.Zero);

    [Fact]
    public void SdlGpuMappedCopyRejectsNullDestinationForNonEmptyInput()
        => Assert.Throws<ArgumentNullException>(()
            => SdlGpuMappedMemoryCopy.Copy(new byte[] { 1 }, IntPtr.Zero));

    [Fact]
    public void SdlPresentPolicySelectsDisplayImmediateAndVsyncFallback()
    {
        SdlGpuPresentPolicy display = SdlGpuPresentPolicy.Resolve(FrameTiming.DisplayRate,
            immediateSupported: true);
        Assert.Equal(SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC, display.NativeMode);
        Assert.Equal("vsync", display.Label);
        Assert.False(display.UsesSoftwarePacing);
        Assert.False(display.ImmediateFallback);

        SdlGpuPresentPolicy immediate = SdlGpuPresentPolicy.Resolve(120,
            immediateSupported: true);
        Assert.Equal(SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE, immediate.NativeMode);
        Assert.Equal("immediate", immediate.Label);
        Assert.True(immediate.UsesSoftwarePacing);
        Assert.False(immediate.ImmediateFallback);

        SdlGpuPresentPolicy fallback = SdlGpuPresentPolicy.Resolve(120,
            immediateSupported: false);
        Assert.Equal(SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC, fallback.NativeMode);
        Assert.Equal("vsync-fallback", fallback.Label);
        Assert.True(fallback.UsesSoftwarePacing);
        Assert.True(fallback.ImmediateFallback);
    }

    [Fact]
    public void SdlFramePacerFirstCallDoesNotDelayAndThenUsesAbsoluteCadence()
    {
        var pacer = new SdlFramePacer();
        SdlSoftwarePacingPolicy policy = SdlSoftwarePacingPolicy.Resolve(100,
            minimized: false);

        Assert.False(pacer.Plan(10, policy, discontinuities: 0).ShouldWait);
        SdlFramePaceDecision second = pacer.Plan(10.002, policy, discontinuities: 0);
        SdlFramePaceDecision third = pacer.Plan(10.01, policy, discontinuities: 0);

        Assert.True(second.ShouldWait);
        Assert.Equal(10.01, second.DeadlineSeconds, 9);
        Assert.True(third.ShouldWait);
        Assert.Equal(10.02, third.DeadlineSeconds, 9);
    }

    [Fact]
    public void SdlFramePacerResetsOnCapModeAndTimingDiscontinuity()
    {
        var pacer = new SdlFramePacer();
        SdlSoftwarePacingPolicy capped = SdlSoftwarePacingPolicy.Resolve(120,
            minimized: false);
        Assert.False(pacer.Plan(0, capped, discontinuities: 0).ShouldWait);
        Assert.True(pacer.Plan(0, capped, discontinuities: 0).ShouldWait);

        SdlSoftwarePacingPolicy changedCap = SdlSoftwarePacingPolicy.Resolve(144,
            minimized: false);
        Assert.False(pacer.Plan(0, changedCap, discontinuities: 0).ShouldWait);

        SdlSoftwarePacingPolicy minimized = SdlSoftwarePacingPolicy.Resolve(144,
            minimized: true);
        Assert.False(pacer.Plan(0, minimized, discontinuities: 0).ShouldWait);
        Assert.False(pacer.Plan(0, changedCap, discontinuities: 0).ShouldWait);
        Assert.False(pacer.Plan(0, changedCap, discontinuities: 1).ShouldWait);
    }

    [Fact]
    public void SdlFramePacerResetsAfterLargeLatenessWithoutCatchUpBurst()
    {
        var pacer = new SdlFramePacer();
        SdlSoftwarePacingPolicy policy = SdlSoftwarePacingPolicy.Resolve(100,
            minimized: false);
        pacer.Plan(0, policy, discontinuities: 0);

        Assert.False(pacer.Plan(1, policy, discontinuities: 0).ShouldWait);
        SdlFramePaceDecision afterReset = pacer.Plan(1, policy, discontinuities: 0);
        Assert.True(afterReset.ShouldWait);
        Assert.Equal(1.01, afterReset.DeadlineSeconds, 9);
    }

    [Fact]
    public void SdlFramePacerRendersSmallMissThenWaitsForRetainedFutureDeadline()
    {
        var pacer = new SdlFramePacer();
        SdlSoftwarePacingPolicy policy = SdlSoftwarePacingPolicy.Resolve(60,
            minimized: false);
        pacer.Plan(0, policy, discontinuities: 0);

        SdlFramePaceDecision afterMiss = pacer.Plan(0.020, policy, discontinuities: 0);
        SdlFramePaceDecision afterLateFrame = pacer.Plan(0.021, policy, discontinuities: 0);

        Assert.False(afterMiss.ShouldWait);
        Assert.True(afterLateFrame.ShouldWait);
        Assert.Equal(2d / 60, afterLateFrame.DeadlineSeconds, 9);
    }

    [Fact]
    public void SdlFramePacingUsesSixtyHertzOnlyWhileMinimized()
    {
        SdlSoftwarePacingPolicy visibleDisplay = SdlSoftwarePacingPolicy.Resolve(
            FrameTiming.DisplayRate, minimized: false);
        SdlSoftwarePacingPolicy minimizedDisplay = SdlSoftwarePacingPolicy.Resolve(
            FrameTiming.DisplayRate, minimized: true);

        Assert.Equal(SdlSoftwarePacingMode.Disabled, visibleDisplay.Mode);
        Assert.Equal(0, visibleDisplay.EffectiveRate);
        Assert.Equal(SdlSoftwarePacingMode.Minimized, minimizedDisplay.Mode);
        Assert.Equal(FrameTiming.SimulationHz, minimizedDisplay.EffectiveRate);

        var pacer = new SdlFramePacer();
        Assert.False(pacer.Plan(2, minimizedDisplay, discontinuities: 0).ShouldWait);
        SdlFramePaceDecision next = pacer.Plan(2, minimizedDisplay, discontinuities: 0);
        Assert.Equal(2 + 1d / 60, next.DeadlineSeconds, 9);
    }

    [Theory]
    [InlineData(MeshPrimitiveTopology.Triangles, new[] { 0, 1, 2 })]
    [InlineData(MeshPrimitiveTopology.Quads, new[] { 0, 1, 2, 0, 2, 3 })]
    [InlineData(MeshPrimitiveTopology.TriangleStrip, new[] { 0, 1, 2, 2, 1, 3 })]
    [InlineData(MeshPrimitiveTopology.QuadStrip, new[] { 0, 1, 3, 0, 3, 2 })]
    [InlineData(MeshPrimitiveTopology.TriangleFan, new[] { 0, 1, 2, 0, 2, 3 })]
    public void MeshCompilerPreservesAndroidTriangleWinding(MeshPrimitiveTopology topology, int[] expected)
    {
        CpuMesh mesh = MeshCompiler.Compile(Vertices(4), topology);
        Assert.Equal(expected, mesh.TriangleIndices);
        Assert.Equal((uint)0, mesh.Vertices[0].MatrixIndex);
        Assert.True(mesh.Vertices[0].HasExplicitColor);
    }

    [Fact]
    public void MeshCompilerBuildsLineLoopAndIgnoresIncompleteTriangles()
    {
        CpuMesh lines = MeshCompiler.Compile(Vertices(3), MeshPrimitiveTopology.LineLoop);
        Assert.Equal(new[] { 0, 1, 1, 2, 2, 0 }, lines.LineIndices);
        Assert.Empty(lines.TriangleIndices);

        CpuMesh incomplete = MeshCompiler.Compile(Vertices(4), MeshPrimitiveTopology.Triangles, count: 2);
        Assert.Empty(incomplete.TriangleIndices);
    }

    [Fact]
    public void MeshCompilerRejectsOutOfBoundsSlicesAndCpuMeshIndices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MeshCompiler.Compile(Vertices(2), MeshPrimitiveTopology.Triangles, start: 2, count: 1));
        Assert.Throws<ArgumentException>(() =>
            new CpuMesh(Vertices(1), new[] { 0, 1, 0 }));
    }

    [Fact]
    public void RenderGeometryBuildersPreserveFullscreenAndHudUvConventions()
    {
        CpuMesh fullscreen = RenderGeometryBuilders.FullscreenQuad();
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, fullscreen.TriangleIndices);
        Assert.Equal(new OpenTK.Mathematics.Vector2(1, 1), fullscreen.Vertices[0].TexCoord);
        Assert.Equal(new OpenTK.Mathematics.Vector2(0, 1), fullscreen.Vertices[1].TexCoord);
        Assert.Equal(new OpenTK.Mathematics.Vector2(1, 0), fullscreen.Vertices[2].TexCoord);
        Assert.Equal(new OpenTK.Mathematics.Vector2(0, 0), fullscreen.Vertices[3].TexCoord);

        CpuMesh hud = RenderGeometryBuilders.TexturedQuad(
            new OpenTK.Mathematics.Vector3(1, 1, 0),
            new OpenTK.Mathematics.Vector3(-1, 1, 0),
            new OpenTK.Mathematics.Vector3(1, -1, 0),
            new OpenTK.Mathematics.Vector3(-1, -1, 0));
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, hud.TriangleIndices);
        Assert.Equal(new OpenTK.Mathematics.Vector2(1, 0), hud.Vertices[0].TexCoord);
        Assert.Equal(new OpenTK.Mathematics.Vector2(0, 0), hud.Vertices[1].TexCoord);
        Assert.Equal(new OpenTK.Mathematics.Vector2(1, 1), hud.Vertices[2].TexCoord);
        Assert.Equal(new OpenTK.Mathematics.Vector2(0, 1), hud.Vertices[3].TexCoord);
    }

    [Fact]
    public void RenderGeometryBuildersPreserveNgonFillEdgeAndSeparateStripBoundaries()
    {
        var positions = new[]
        {
            new OpenTK.Mathematics.Vector3(0, 0, 0),
            new OpenTK.Mathematics.Vector3(1, 0, 0),
            new OpenTK.Mathematics.Vector3(1, 1, 0),
            new OpenTK.Mathematics.Vector3(0, 1, 0)
        };
        CpuMesh fill = RenderGeometryBuilders.TriangleFan(positions);
        CpuMesh edge = RenderGeometryBuilders.LineLoop(positions);

        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, fill.TriangleIndices);
        Assert.Empty(fill.LineIndices);
        Assert.Empty(edge.TriangleIndices);
        Assert.Equal(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, edge.LineIndices);

        CpuMesh firstStrip = RenderGeometryBuilders.SolidQuad(
            positions[0], positions[1], positions[2], positions[3],
            new OpenTK.Mathematics.Vector4(1, 0, 0, 1));
        CpuMesh secondStrip = RenderGeometryBuilders.SolidQuad(
            positions[1], positions[2], positions[3], positions[0],
            new OpenTK.Mathematics.Vector4(0, 1, 0, 1));
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, firstStrip.TriangleIndices);
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, secondStrip.TriangleIndices);
    }

    [Fact]
    public void RenderGeometryBuildersPreserveInheritedAndExplicitColorFlags()
    {
        OpenTK.Mathematics.Vector4 color = new(.25f, .5f, .75f, 1);
        CpuMesh inherited = RenderGeometryBuilders.SolidQuad(
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitX,
            OpenTK.Mathematics.Vector3.One, OpenTK.Mathematics.Vector3.UnitY, color);
        CpuMesh explicitColor = RenderGeometryBuilders.SolidQuad(
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitX,
            OpenTK.Mathematics.Vector3.One, OpenTK.Mathematics.Vector3.UnitY, color,
            explicitColor: true);

        Assert.All(inherited.Vertices, vertex => Assert.False(vertex.HasExplicitColor));
        Assert.All(explicitColor.Vertices, vertex => Assert.True(vertex.HasExplicitColor));
        Assert.Equal(color, explicitColor.Vertices[0].Color);
    }

    [Fact]
    public void RenderMeshPackingPreservesTheEighteenFloatGlesAbiAndIndexStreams()
    {
        RenderVertex explicitVertex = new(
            new OpenTK.Mathematics.Vector3(1, 2, 3),
            new OpenTK.Mathematics.Vector4(.1f, .2f, .3f, .4f),
            new OpenTK.Mathematics.Vector3(4, 5, 6),
            new OpenTK.Mathematics.Vector2(.7f, .8f),
            new OpenTK.Mathematics.Vector4(9, 10, 11, -1), matrixIndex: 7,
            flags: RenderVertexFlags.ExplicitColor);
        RenderVertex inheritedVertex = new(
            new OpenTK.Mathematics.Vector3(-1, -2, -3), OpenTK.Mathematics.Vector4.One,
            OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector2.Zero,
            matrixIndex: 2);
        CpuMesh mesh = new(new[] { explicitVertex, inheritedVertex },
            new[] { 0, 1, 0 }, new[] { 1, 0 }, revision: 17);
        float[] vertices = new float[RenderMeshPacking.RequiredVertexFloats(mesh)];
        int[] indices = new int[RenderMeshPacking.RequiredIndexCount(mesh)];

        Assert.Equal(36, RenderMeshPacking.PackVertices(mesh, vertices));
        Assert.Equal(new[]
        {
            1f, 2f, 3f, .1f, .2f, .3f, .4f, 4f, 5f, 6f, .7f, .8f, 7f, 1f,
            9f, 10f, 11f, -1f,
            -1f, -2f, -3f, 1f, 1f, 1f, 1f, 0f, 0f, 1f, 0f, 0f, 2f, 0f,
            1f, 0f, 0f, 1f
        }, vertices);
        Assert.Equal(5, RenderMeshPacking.PackIndices(mesh, indices));
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, indices);
        Assert.Equal(12, RenderMeshPacking.LineIndexOffsetBytes(mesh));
    }

    [Fact]
    public void RenderMeshPackingBoundsDynamicUploadsAndDetectsStaticRevisionChanges()
    {
        CpuMesh mesh = new(Vertices(2), new[] { 0, 1, 0 }, revision: 41);
        Assert.Throws<InvalidOperationException>(() =>
            RenderMeshPacking.ValidateDynamicCapacity(mesh, maximumVertices: 1));
        Assert.Throws<InvalidOperationException>(() =>
            RenderMeshPacking.ValidateDynamicCapacity(mesh, maximumIndices: 2));

        object identity = new();
        Assert.False(RenderMeshPacking.NeedsStaticUpload(identity, mesh, mesh.Revision, identity, mesh));
        // Keep the CpuMesh reference the same: the cache's explicit snapshot
        // must still invalidate an object if the source revision advances.
        Assert.True(RenderMeshPacking.NeedsStaticUpload(identity, mesh, mesh.Revision - 1, identity, mesh));
        CpuMesh replacement = new(Vertices(2), new[] { 0, 1, 0 }, revision: 42);
        Assert.True(RenderMeshPacking.NeedsStaticUpload(identity, mesh, mesh.Revision,
            identity, replacement));
        Assert.True(RenderMeshPacking.NeedsStaticUpload(new object(), mesh, mesh.Revision,
            identity, mesh));
    }

    [Fact]
    public void RenderMeshScratchReusesBoundedStorageAfterWarmup()
    {
        var scratch = new RenderMeshScratch(initialVertexCapacity: 4);
        scratch.SetTexturedQuad(
            new OpenTK.Mathematics.Vector3(1, 1, 0),
            new OpenTK.Mathematics.Vector3(-1, 1, 0),
            new OpenTK.Mathematics.Vector3(1, -1, 0),
            new OpenTK.Mathematics.Vector3(-1, -1, 0));

        // A larger diagnostic mesh is the bounded warm-up.  Subsequent HUD
        // primitives fit in the same arrays and do not allocate new storage.
        scratch.Prepare(MeshPrimitiveTopology.LineLoop, 8);
        for (int i = 0; i < scratch.VertexCount; i++)
        {
            scratch.SetVertex(i, new OpenTK.Mathematics.Vector3(i, i, 0));
        }
        RenderVertex[] vertices = scratch.VertexStorage;
        int[] triangles = scratch.TriangleIndexStorage;
        int[] lines = scratch.LineIndexStorage;
        int vertexCapacity = scratch.VertexCapacity;
        int triangleCapacity = scratch.TriangleIndexCapacity;
        int lineCapacity = scratch.LineIndexCapacity;

        scratch.SetSolidQuad(
            OpenTK.Mathematics.Vector3.One,
            OpenTK.Mathematics.Vector3.UnitX,
            OpenTK.Mathematics.Vector3.UnitY,
            OpenTK.Mathematics.Vector3.Zero,
            OpenTK.Mathematics.Vector4.One);

        Assert.Same(vertices, scratch.VertexStorage);
        Assert.Same(triangles, scratch.TriangleIndexStorage);
        Assert.Same(lines, scratch.LineIndexStorage);
        Assert.Equal(vertexCapacity, scratch.VertexCapacity);
        Assert.Equal(triangleCapacity, scratch.TriangleIndexCapacity);
        Assert.Equal(lineCapacity, scratch.LineIndexCapacity);
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, scratch.TriangleIndices.ToArray());
        Assert.Empty(scratch.LineIndices.ToArray());
        Assert.InRange(scratch.VertexCapacity, 4, RenderMeshPacking.DefaultMaximumVertices);
        Assert.InRange(scratch.TriangleIndexCapacity, 1, RenderMeshPacking.DefaultMaximumIndices);
        Assert.InRange(scratch.LineIndexCapacity, 1, RenderMeshPacking.DefaultMaximumIndices);

        float[] packedVertices = new float[RenderMeshPacking.RequiredVertexFloats(scratch)];
        int[] packedIndices = new int[RenderMeshPacking.RequiredIndexCount(scratch)];
        Assert.Equal(packedVertices.Length, RenderMeshPacking.PackVertices(scratch, packedVertices));
        Assert.Equal(packedIndices.Length, RenderMeshPacking.PackIndices(scratch, packedIndices));
    }

    [Fact]
    public void CrosshairFillBarsMatchesListGeometryWithoutRendererStorage()
    {
        Span<CrosshairBar> actual = stackalloc CrosshairBar[8];
        foreach (CrosshairStyle style in Enum.GetValues<CrosshairStyle>())
        {
            IReadOnlyList<CrosshairBar> expected = Crosshair.BarsOf(style, 1f);
            int count = Crosshair.FillBars(style, 1f, actual);

            Assert.Equal(expected.Count, count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(expected[i].X, actual[i].X);
                Assert.Equal(expected[i].Y, actual[i].Y);
                Assert.Equal(expected[i].Width, actual[i].Width);
                Assert.Equal(expected[i].Height, actual[i].Height);
            }
        }
    }

    [Fact]
    public void DynamicPrimitiveCompilerPreservesQuadAndTrailWinding()
    {
        var quad = new DrawSubmission { Primitive = RenderPrimitive.Quad,
            Points = Enumerable.Range(0, 4).Select(i => new OpenTK.Mathematics.Vector3(i, 0, 0)).ToArray() };
        Assert.Equal(new[] { 0, 3, 1, 1, 3, 2 },
            DynamicPrimitiveCompiler.Compile(quad).TriangleIndices);

        var particle = new DrawSubmission { Primitive = RenderPrimitive.Particle,
            ScaleS = 2, ScaleT = 3,
            Points = new[]
            {
                new OpenTK.Mathematics.Vector3(0, 0, 0), new OpenTK.Mathematics.Vector3(10, 0, 0),
                new OpenTK.Mathematics.Vector3(1, 0, 0), new OpenTK.Mathematics.Vector3(11, 0, 0),
                new OpenTK.Mathematics.Vector3(1, 1, 0), new OpenTK.Mathematics.Vector3(11, 1, 0),
                new OpenTK.Mathematics.Vector3(0, 1, 0), new OpenTK.Mathematics.Vector3(10, 1, 0)
            } };
        CpuMesh particleMesh = DynamicPrimitiveCompiler.Compile(particle);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, particleMesh.TriangleIndices);
        Assert.Equal(new OpenTK.Mathematics.Vector2(2, 3), particleMesh.Vertices[2].TexCoord);

        var trail = new DrawSubmission { Primitive = RenderPrimitive.TrailSingle,
            Points = particle.Points };
        Assert.Equal(new[] { 0, 1, 3, 0, 3, 2 },
            DynamicPrimitiveCompiler.Compile(trail).TriangleIndices);

        var stack = new DrawSubmission { Primitive = RenderPrimitive.TrailStack, ItemCount = 1,
            Points = particle.Points };
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 },
            DynamicPrimitiveCompiler.Compile(stack).TriangleIndices);
    }

    [Fact]
    public void DynamicPrimitiveCompilerPreservesBoxCylinderAndSphereCounts()
    {
        var box = new DrawSubmission { Primitive = RenderPrimitive.Box,
            Points = Enumerable.Range(0, 8).Select(i => new OpenTK.Mathematics.Vector3(i, 0, 0)).ToArray() };
        Assert.Equal(new[] { 2, 6, 0, 0, 6, 4, 0, 4, 1, 1, 4, 5,
            1, 5, 3, 3, 5, 7, 3, 7, 2, 2, 7, 6, 5, 4, 7, 7, 4, 6,
            3, 2, 1, 1, 2, 0 },
            DynamicPrimitiveCompiler.Compile(box).TriangleIndices);

        var cylinder = new DrawSubmission { Primitive = RenderPrimitive.Cylinder,
            Points = Enumerable.Range(0, 34).Select(i => new OpenTK.Mathematics.Vector3(i, 0, 0)).ToArray() };
        CpuMesh cylinderMesh = DynamicPrimitiveCompiler.Compile(cylinder);
        Assert.Equal(34, cylinderMesh.Vertices.Length);
        Assert.Equal(64 * 3, cylinderMesh.TriangleIndices.Length);
        Assert.Equal(new[] { 32, 0, 1, 32, 1, 2 }, cylinderMesh.TriangleIndices.Take(6));
        Assert.Equal(new[] { 33, 31, 30, 33, 30, 29 }, cylinderMesh.TriangleIndices.Skip(48).Take(6));

        var sphere = new DrawSubmission { Primitive = RenderPrimitive.Sphere,
            Points = Enumerable.Range(0, 17 * 25).Select(i => new OpenTK.Mathematics.Vector3(i, 0, 0)).ToArray() };
        Assert.Equal(17 * 25, DynamicPrimitiveCompiler.Compile(sphere).Vertices.Length);
        Assert.Equal(720 * 3, DynamicPrimitiveCompiler.Compile(sphere).TriangleIndices.Length);
    }

    [Fact]
    public void DisplayListCompilerPreservesPackedStateAndVertexWinding()
    {
        uint color = 31u | (15u << 5) | (7u << 10);
        uint normal = Pack10(-1, 2, -3);
        var instructions = new[]
        {
            new RenderInstruction(InstructionCode.BEGIN_VTXS, 0),
            new RenderInstruction(InstructionCode.COLOR, color),
            new RenderInstruction(InstructionCode.NORMAL, normal),
            new RenderInstruction(InstructionCode.TEXCOORD, Pack16(16, 32)),
            new RenderInstruction(InstructionCode.MTX_RESTORE, 4),
            new RenderInstruction(InstructionCode.VTX_16, Pack16(4096, 8192), Pack16(12288, 0)),
            new RenderInstruction(InstructionCode.VTX_XY, Pack16(0, 0)),
            new RenderInstruction(InstructionCode.VTX_DIFF, Pack10(64, 0, 0)),
            new RenderInstruction(InstructionCode.END_VTXS)
        };

        CpuMesh mesh = MeshCompiler.CompileDisplayList(instructions, textureWidth: 64, textureHeight: 64,
            texgen: false, isRoom: false);

        Assert.Equal(new[] { 0, 1, 2 }, mesh.TriangleIndices);
        Assert.Equal(new OpenTK.Mathematics.Vector3(1, 2, 3), mesh.Vertices[0].Position);
        Assert.Equal(new OpenTK.Mathematics.Vector3(0, 0, 3), mesh.Vertices[1].Position);
        Assert.Equal(new OpenTK.Mathematics.Vector3(0.015625f, 0, 3), mesh.Vertices[2].Position);
        Assert.Equal(new OpenTK.Mathematics.Vector2(1f / 64, 2f / 64), mesh.Vertices[0].TexCoord);
        Assert.Equal((uint)4, mesh.Vertices[0].MatrixIndex);
        Assert.True(mesh.Vertices[0].HasExplicitColor);
        Assert.Equal(new OpenTK.Mathematics.Vector4(1, 15f / 31, 7f / 31, 1), mesh.Vertices[0].Color);
        Assert.Equal(new OpenTK.Mathematics.Vector3(-1f / 512, 2f / 512, -3f / 512), mesh.Vertices[0].Normal);
    }

    [Fact]
    public void DisplayListCompilerRejectsMalformedStateAndTextureCoordinates()
    {
        Assert.Throws<ProgramException>(() => MeshCompiler.CompileDisplayList(
            new[] { new RenderInstruction(InstructionCode.BEGIN_VTXS, 0) }, 1, 1, false, false));
        Assert.Throws<ProgramException>(() => MeshCompiler.CompileDisplayList(
            new[]
            {
                new RenderInstruction(InstructionCode.BEGIN_VTXS, 0),
                new RenderInstruction(InstructionCode.TEXCOORD, 0)
            }, 0, 1, false, false));
    }

    private static uint Pack16(int first, int second)
        => (uint)(first & 0xFFFF) | ((uint)(second & 0xFFFF) << 16);

    private static uint Pack10(int x, int y, int z)
        => (uint)(x & 0x3FF) | ((uint)(y & 0x3FF) << 10) | ((uint)(z & 0x3FF) << 20);

    [Fact]
    public void RenderFrameReusesSubmissionAndMatrixStorage()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        DrawSubmission first = frame.Acquire();
        first.PolygonId = 19;
        frame.Reset();
        DrawSubmission second = frame.Acquire();
        Assert.Same(first, second);
        Assert.Same(first.MatrixStack, second.MatrixStack);
        Assert.Equal(0, second.PolygonId);
        Assert.Throws<InvalidOperationException>(() => frame.Acquire());
    }

    [Fact]
    public void RenderCaptureRequestIsImmutableAndFrameBounded()
    {
        Guid requestId = Guid.NewGuid();
        var request = new RenderCaptureRequest(requestId, originatingFrame: 42,
            CaptureTargetKind.FinalPresentedFrame, width: 1280, height: 720,
            CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp,
            CaptureDeliveryKind.Recording, outputName: "frame0000");
        var second = new RenderCaptureRequest(Guid.NewGuid(), originatingFrame: 43,
            CaptureTargetKind.SceneTarget, width: 1280, height: 720,
            CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp,
            CaptureDeliveryKind.Screenshot, outputName: "shot");
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1,
            maximumCaptureRequests: 1);

        frame.AddCaptureRequest(request);
        Assert.Same(request, frame.CaptureRequests[0]);
        Assert.Equal(requestId, frame.CaptureRequests[0].RequestId);
        Assert.Equal("frame0000", frame.CaptureRequests[0].OutputName);
        Assert.Throws<InvalidOperationException>(() => frame.AddCaptureRequest(second));

        frame.Seal();
        Assert.Throws<InvalidOperationException>(() => frame.AddCaptureRequest(second));

        // A failed submission resets the reusable frame, then retries the
        // same immutable request. The name and identity must not advance just
        // because a backend had no drawable frame.
        frame.Reset();
        frame.AddCaptureRequest(request);
        Assert.Same(request, frame.CaptureRequests[0]);
        Assert.Equal(requestId, frame.CaptureRequests[0].RequestId);
        Assert.Equal("frame0000", frame.CaptureRequests[0].OutputName);
    }

    [Fact]
    public void RenderCaptureRequestRejectsInvalidGeometry()
    {
        Assert.Throws<ArgumentException>(() => new RenderCaptureRequest(Guid.Empty, 0,
            CaptureTargetKind.SceneTarget, 1, 1, CapturePixelFormat.Rgb8,
            CaptureRowOrientation.TopDown, CaptureDeliveryKind.Screenshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderCaptureRequest(Guid.NewGuid(), -1,
            CaptureTargetKind.SceneTarget, 1, 1, CapturePixelFormat.Rgb8,
            CaptureRowOrientation.TopDown, CaptureDeliveryKind.Screenshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderCaptureRequest(Guid.NewGuid(), 0,
            CaptureTargetKind.SceneTarget, 0, 1, CapturePixelFormat.Rgb8,
            CaptureRowOrientation.TopDown, CaptureDeliveryKind.Screenshot));
    }

    [Fact]
    public void SubmissionCarriesSourceIdentitiesAndNoLegacyHandles()
    {
        var geometry = new object();
        var textureSource = new object();
        var submission = new DrawSubmission();
        submission.GeometryIdentity = geometry;
        submission.TextureIdentity = new TextureIdentity(textureSource, textureId: 2, paletteId: 3, recolorId: 1);

        Assert.Same(geometry, submission.GeometryIdentity);
        Assert.Same(textureSource, submission.TextureIdentity!.Value.Source);
        Assert.Equal(2, submission.TextureIdentity.Value.TextureId);
        Assert.Equal(3, submission.TextureIdentity.Value.PaletteId);
        Assert.Equal(1, submission.TextureIdentity.Value.RecolorId);
        Assert.Null(typeof(DrawSubmission).GetProperty("LegacyListId"));
        Assert.Null(typeof(DrawSubmission).GetProperty("TextureBindingId"));
    }

    [Fact]
    public void TextureIdentityDistinguishesResolvedPaletteRecolorAndEffectVariants()
    {
        var source = new object();
        var effectA = new object();
        var effectB = new object();
        TextureIdentity baseIdentity = new TextureIdentity(source, textureId: 2, paletteId: 3, recolorId: 0);

        Assert.NotEqual(baseIdentity, new TextureIdentity(source, textureId: 2, paletteId: 4, recolorId: 0));
        Assert.NotEqual(baseIdentity, new TextureIdentity(source, textureId: 2, paletteId: 3, recolorId: 1));
        Assert.NotEqual(
            new TextureIdentity(source, textureId: 2, paletteId: 3, recolorId: 0, variant: effectA),
            new TextureIdentity(source, textureId: 2, paletteId: 3, recolorId: 0, variant: effectB));
        Assert.NotEqual(baseIdentity, baseIdentity.WithPaletteOverride(new OpenTK.Mathematics.Vector4(1, 0, 0, 1)));
    }

    [Fact]
    public void PassAndPipelineKeysKeepTransparentStencilStagesDistinct()
    {
        RenderMaterial opaque = new RenderMaterial
        {
            RenderMode = RenderMode.Normal,
            Alpha = 1,
            CullingMode = CullingMode.Back,
            WrapX = RepeatMode.Clamp,
            WrapY = RepeatMode.Clamp
        };
        RenderMaterial translucent = opaque;
        translucent.RenderMode = RenderMode.Translucent;
        translucent.Alpha = .5f;

        Assert.Equal(RenderPassKind.Opaque, RenderPassClassifier.Classify(opaque));
        Assert.Equal(RenderPassKind.TransparentStencil, RenderPassClassifier.Classify(translucent));
        PipelineKey opaqueKey = PipelineKey.From(opaque, RenderPrimitive.Mesh, RenderPassKind.Opaque);
        PipelineKey behindKey = PipelineKey.From(translucent, RenderPrimitive.Mesh, RenderPassKind.TransparentBehind);
        PipelineKey frontKey = PipelineKey.From(translucent, RenderPrimitive.Mesh, RenderPassKind.TransparentFront);
        Assert.NotEqual(opaqueKey, behindKey);
        Assert.NotEqual(behindKey, frontKey);
        Assert.Equal(RenderAlphaTestMode.EqualOne, opaqueKey.AlphaTestMode);
        Assert.Equal(RenderAlphaTestMode.LessThanOne, behindKey.AlphaTestMode);
        Assert.Equal(RenderBlendMode.Alpha, behindKey.BlendMode);
        Assert.Equal(RenderDepthMode.LessOrEqual, behindKey.DepthMode);
        Assert.Equal(RenderStencilMode.NotEqualPolygon, behindKey.StencilMode);
        Assert.Equal(RenderStencilMode.EqualPolygon, frontKey.StencilMode);
    }

    [Fact]
    public void PipelineKeyIncludesTargetFormatColorMaskAndDecalBias()
    {
        RenderMaterial material = new RenderMaterial
        {
            RenderMode = RenderMode.Normal,
            Alpha = 1,
            CullingMode = CullingMode.Back
        };
        PipelineKey mark = PipelineKey.From(material, RenderPrimitive.Mesh,
            RenderPassKind.TransparentStencil, targetFormat: "R16G16B16A16_FLOAT");
        PipelineKey markOtherFormat = PipelineKey.From(material, RenderPrimitive.Mesh,
            RenderPassKind.TransparentStencil, targetFormat: "R8G8B8A8_UNORM");
        PipelineKey decal = PipelineKey.From(material, RenderPrimitive.Mesh,
            RenderPassKind.Decal, targetFormat: "R16G16B16A16_FLOAT");

        Assert.Equal(RenderColorWriteMask.None, mark.ColorWriteMask);
        Assert.Equal("R16G16B16A16_FLOAT", mark.TargetFormat);
        Assert.False(mark.DecalDepthBias);
        Assert.NotEqual(mark, markOtherFormat);
        Assert.True(decal.DecalDepthBias);
        Assert.Equal(RenderColorWriteMask.All, decal.ColorWriteMask);
    }

    [Fact]
    public void RenderQualityPolicyParsesPresetsAndLegacyFilteringSettings()
    {
        Assert.Equal(GraphicsPreset.Original, RenderOptions.ParseGraphicsPreset(null));
        Assert.Equal(GraphicsPreset.Enhanced, RenderOptions.ParseGraphicsPreset(" ENHANCED "));
        Assert.Equal(GraphicsPreset.Performance, RenderOptions.ParseGraphicsPreset("perf"));
        Assert.Equal("performance", RenderOptions.FormatGraphicsPreset(GraphicsPreset.Performance));

        Assert.Equal(TextureFilteringPreset.Original,
            RenderOptions.ResolveTextureFilteringPreset(null, "off"));
        Assert.Equal(TextureFilteringPreset.Smooth,
            RenderOptions.ResolveTextureFilteringPreset(null, "on"));
        Assert.Equal(TextureFilteringPreset.Enhanced,
            RenderOptions.ResolveTextureFilteringPreset("enhanced", "off"));

        Assert.Equal(AnisotropyLevel.X2, RenderOptions.ParseAnisotropy("1x"));
        Assert.Equal(AnisotropyLevel.X16, RenderOptions.ParseAnisotropy("99x"));
        Assert.Equal(AnisotropyLevel.Off, RenderOptions.ParseAnisotropy("off"));
        Assert.Equal(MsaaLevel.X4, RenderOptions.ParseMsaa("8x"));
        Assert.Equal(MsaaLevel.Off, RenderOptions.ParseMsaa("off"));
        Assert.Equal("16x", RenderOptions.FormatAnisotropy(AnisotropyLevel.X16));
        Assert.Equal("4x", RenderOptions.FormatMsaa(MsaaLevel.X4));

        MenuSettings legacyOff = new() { TextureFiltering = "off" };
        RenderQualitySnapshot offQuality = RenderOptions.ResolveQuality(legacyOff);
        Assert.Equal(GraphicsPreset.Original, offQuality.GraphicsPreset);
        Assert.Equal(TextureFilteringPreset.Original, offQuality.TextureFilteringPreset);

        MenuSettings legacyOn = new() { TextureFiltering = "on" };
        RenderQualitySnapshot onQuality = RenderOptions.ResolveQuality(legacyOn);
        Assert.Equal(TextureFilteringPreset.Smooth, onQuality.TextureFilteringPreset);

        MenuSettings enhanced = new() { GraphicsPreset = "enhanced" };
        RenderQualitySnapshot enhancedDefaults = RenderOptions.ResolveQuality(enhanced);
        Assert.Equal(TextureFilteringPreset.Enhanced, enhancedDefaults.TextureFilteringPreset);
        Assert.Equal(AnisotropyLevel.X4, enhancedDefaults.Anisotropy);
        Assert.Equal(MsaaLevel.X4, enhancedDefaults.Msaa);
        Assert.True(enhancedDefaults.Bloom);
        Assert.True(enhancedDefaults.DynamicVisualLights);
    }

    [Fact]
    public void RenderQualitySnapshotIsNeutralAndSamplerKeyKeepsFilteringDimensions()
    {
        RenderQualitySnapshot original = new(
            GraphicsPreset.Original, TextureFilteringPreset.Original,
            AnisotropyLevel.Off, MsaaLevel.Off, Bloom: false,
            DynamicVisualLights: false);
        RenderQualitySnapshot enhanced = new(
            GraphicsPreset.Enhanced, TextureFilteringPreset.Enhanced,
            AnisotropyLevel.X8, MsaaLevel.X4, Bloom: true,
            DynamicVisualLights: true);

        Assert.Equal(1, original.MsaaSampleCount);
        Assert.False(original.UsesMipmaps);
        Assert.True(enhanced.UsesMipmaps);
        Assert.True(enhanced.UsesAnisotropicFiltering);
        Assert.Equal(4, enhanced.MsaaSampleCount);

        RenderMaterial material = new() { WrapX = RepeatMode.Clamp, WrapY = RepeatMode.Repeat };
        SamplerKey originalKey = SamplerKey.From(material, original);
        SamplerKey enhancedKey = SamplerKey.From(material, enhanced);
        Assert.Equal(RenderFilterMode.Nearest, originalKey.Filter);
        Assert.Equal(RenderFilterMode.Anisotropic, enhancedKey.Filter);
        Assert.False(originalKey.Mipmapped);
        Assert.True(enhancedKey.Mipmapped);
        Assert.Equal(AnisotropyLevel.X8, enhancedKey.Anisotropy);
        Assert.NotEqual(originalKey, enhancedKey);
    }

    [Fact]
    public void PipelineKeyCarriesRequestedMsaaWithoutBackendTypes()
    {
        RenderMaterial material = new() { CullingMode = CullingMode.Back };
        RenderQualitySnapshot off = new(
            GraphicsPreset.Original, TextureFilteringPreset.Original,
            AnisotropyLevel.Off, MsaaLevel.Off, false, false);
        RenderQualitySnapshot fourX = off with { Msaa = MsaaLevel.X4 };

        PipelineKey offKey = PipelineKey.From(material, RenderPrimitive.Mesh,
            RenderPassKind.Opaque, off);
        PipelineKey fourXKey = PipelineKey.From(material, RenderPrimitive.Mesh,
            RenderPassKind.Opaque, fourX);

        Assert.Equal(1, offKey.SampleCount);
        Assert.Equal(4, fourXKey.SampleCount);
        Assert.NotEqual(offKey, fourXKey);
    }

    [Fact]
    public void SdlMipPolicyBuildsFullBoundedChains()
    {
        Assert.Equal(1u, SdlGpuTextureQuality.MipLevelCount(1, 1, mipmapped: true));
        Assert.Equal(1u, SdlGpuTextureQuality.MipLevelCount(3840, 2160, mipmapped: false));
        Assert.Equal(12u, SdlGpuTextureQuality.MipLevelCount(3840, 2160, mipmapped: true));
        Assert.Equal(13u, SdlGpuTextureQuality.MipLevelCount(4096, 2160, mipmapped: true));
        Assert.Equal(SdlGpuTextureQuality.MaximumMipLevels,
            SdlGpuTextureQuality.MipLevelCount(int.MaxValue, 1, mipmapped: true));
        Assert.Throws<ArgumentOutOfRangeException>(()
            => SdlGpuTextureQuality.MipLevelCount(0, 1, mipmapped: true));
        Assert.Throws<ArgumentOutOfRangeException>(()
            => SdlGpuTextureQuality.MipLevelCount(1, 0, mipmapped: true));

        SDL_GPUTextureUsageFlags original = SdlGpuTextureQuality.SceneTextureUsage(
            mipmapped: false);
        SDL_GPUTextureUsageFlags mipmapped = SdlGpuTextureQuality.SceneTextureUsage(
            mipmapped: true);
        Assert.Equal(SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER, original);
        Assert.True((mipmapped & SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER) != 0);
        Assert.True((mipmapped & SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET) != 0);
    }

    [Fact]
    public void SdlSamplerPolicyMapsPresetsAndDeterministicDowngradeSequence()
    {
        RenderMaterial material = new() { WrapX = RepeatMode.Clamp, WrapY = RepeatMode.Repeat };
        RenderQualitySnapshot original = new(GraphicsPreset.Original,
            TextureFilteringPreset.Original, AnisotropyLevel.X16, MsaaLevel.Off,
            Bloom: false, DynamicVisualLights: false);
        RenderQualitySnapshot smooth = original with
        {
            TextureFilteringPreset = TextureFilteringPreset.Smooth
        };
        RenderQualitySnapshot enhanced = original with
        {
            TextureFilteringPreset = TextureFilteringPreset.Enhanced
        };

        SamplerKey originalKey = SamplerKey.From(material, original);
        SamplerKey smoothKey = SamplerKey.From(material, smooth);
        SamplerKey enhancedKey = SamplerKey.From(material, enhanced);
        SdlGpuSamplerDescription originalDescription = SdlGpuSamplerPolicy.Describe(originalKey);
        SdlGpuSamplerDescription smoothDescription = SdlGpuSamplerPolicy.Describe(smoothKey);
        SdlGpuSamplerDescription enhancedDescription = SdlGpuSamplerPolicy.Describe(enhancedKey);

        Assert.Equal(RenderFilterMode.Nearest, originalKey.Filter);
        Assert.False(originalKey.Mipmapped);
        Assert.Equal(0, originalDescription.MaxLod);
        Assert.Equal(SdlGpuSamplerFilter.Nearest, originalDescription.MinFilter);
        Assert.Equal(AnisotropyLevel.Off, originalDescription.Anisotropy);

        Assert.Equal(RenderFilterMode.Linear, smoothKey.Filter);
        Assert.True(smoothKey.Mipmapped);
        Assert.Equal(SdlGpuSamplerFilter.Linear, smoothDescription.MinFilter);
        Assert.Equal(SdlGpuSamplerFilter.Linear, smoothDescription.MagFilter);
        Assert.Equal(SdlGpuSamplerMipFilter.Linear, smoothDescription.MipFilter);
        Assert.Equal(SdlGpuSamplerPolicy.MipmappedMaxLod, smoothDescription.MaxLod);
        Assert.Equal(AnisotropyLevel.Off, smoothDescription.Anisotropy);

        Assert.Equal(RenderFilterMode.Anisotropic, enhancedKey.Filter);
        Assert.Equal(AnisotropyLevel.X16, enhancedDescription.Anisotropy);
        Assert.True(enhancedDescription.EnableAnisotropy);
        Assert.Equal(16f, enhancedDescription.MaxAnisotropy);
        var attempts = new List<AnisotropyLevel>();
        for (int index = 0; SdlGpuSamplerPolicy.TryGetAnisotropyAttempt(
            enhancedKey.Anisotropy, index, out AnisotropyLevel attempt); index++)
        {
            attempts.Add(attempt);
        }
        Assert.Equal(new[] { AnisotropyLevel.X16, AnisotropyLevel.X8,
            AnisotropyLevel.X4, AnisotropyLevel.X2, AnisotropyLevel.Off }, attempts);
        Assert.Equal(RenderFilterMode.LinearMipmapped,
            enhancedKey.WithAnisotropy(AnisotropyLevel.Off).Filter);
    }

    [Fact]
    public void SdlMsaaPolicyRequiresJointColorDepthSupportAndCelFallsBack()
    {
        SdlGpuSampleNegotiation full = SdlGpuMsaaPolicy.Resolve(4,
            celDepthSampling: false, true, true, true, true);
        SdlGpuSampleNegotiation downgrade = SdlGpuMsaaPolicy.Resolve(4,
            celDepthSampling: false, true, false, true, false);
        SdlGpuSampleNegotiation unsupported = SdlGpuMsaaPolicy.Resolve(2,
            celDepthSampling: false, true, false, false, false);
        SdlGpuSampleNegotiation cel = SdlGpuMsaaPolicy.Resolve(4,
            celDepthSampling: true, true, true, true, true);

        Assert.Equal(new SdlGpuSampleNegotiation(4, 4,
            SdlGpuMsaaFallbackReason.None), full);
        Assert.Equal(new SdlGpuSampleNegotiation(4, 2,
            SdlGpuMsaaFallbackReason.UnsupportedColorOrDepthFormat), downgrade);
        Assert.Equal(new SdlGpuSampleNegotiation(2, 1,
            SdlGpuMsaaFallbackReason.UnsupportedColorOrDepthFormat), unsupported);
        Assert.Equal(new SdlGpuSampleNegotiation(4, 1,
            SdlGpuMsaaFallbackReason.CelDepthSampling), cel);
    }

    [Fact]
    public void SdlTargetPlanKeepsWorldHudAndPipelineSamplesConsistent()
    {
        SdlGpuSceneTargetPlan plan = SdlGpuSceneTargetPlan.From(
            new SdlGpuSampleNegotiation(4, 4, SdlGpuMsaaFallbackReason.None));
        RenderMaterial material = new() { CullingMode = CullingMode.Back };
        PipelineKey world = PipelineKey.From(material, RenderPrimitive.Mesh,
            RenderPassKind.Opaque, sampleCount: 4, targetFormat: "test-format");
        PipelineKey hud = SdlGpuSceneResources.CreateHudPipelineKey(material,
            "test-format", sampleCount: 4);

        Assert.True(plan.UsesResolve);
        Assert.Equal(4, plan.RenderColorSamples);
        Assert.Equal(4, plan.DepthSamples);
        Assert.Equal(1, plan.ResolveColorSamples);
        Assert.True(plan.MatchesPipeline(world));
        Assert.True(plan.MatchesPipeline(hud));
        Assert.False(plan.MatchesPipeline(PipelineKey.From(material,
            RenderPrimitive.Mesh, RenderPassKind.Opaque, sampleCount: 2)));
    }

    [Fact]
    public void SdlMatrixAbiPreservesLegacyNonIdentityTransform()
    {
        OpenTK.Mathematics.Matrix4 source
            = OpenTK.Mathematics.Matrix4.CreateRotationY(.37f)
            * OpenTK.Mathematics.Matrix4.CreateTranslation(3, -2, 5);
        OpenTK.Mathematics.Vector4 point = new(.25f, 1.5f, -4, 1);
        OpenTK.Mathematics.Vector4 legacy = OpenTK.Mathematics.Vector4.TransformRow(point, source);
        OpenTK.Mathematics.Vector4 shader = OpenTK.Mathematics.Vector4.TransformColumn(
            SdlGpuMatrixAbi.Upload(source), point);

        Assert.Equal(legacy.X, shader.X, 5);
        Assert.Equal(legacy.Y, shader.Y, 5);
        Assert.Equal(legacy.Z, shader.Z, 5);
        Assert.Equal(legacy.W, shader.W, 5);

        float[] sourceStack =
        {
            source.Row0.X, source.Row0.Y, source.Row0.Z, source.Row0.W,
            source.Row1.X, source.Row1.Y, source.Row1.Z, source.Row1.W,
            source.Row2.X, source.Row2.Y, source.Row2.Z, source.Row2.W,
            source.Row3.X, source.Row3.Y, source.Row3.Z, source.Row3.W
        };
        float[] uploaded = new float[16];
        SdlGpuMatrixAbi.CopyStack(sourceStack, 1, uploaded);
        Assert.Equal(source.Row3.X, uploaded[3]);
        Assert.Equal(source.Row0.W, uploaded[12]);
    }

    [Theory]
    [InlineData(0f, false, true)]
    [InlineData(.999f, false, true)]
    [InlineData(.9995f, false, true)]
    [InlineData(1f, true, false)]
    public void SdlAlphaPassPredicatesAreExactAndDisjoint(float alpha, bool opaque, bool transparent)
    {
        Assert.Equal(opaque, SdlGpuAlphaPredicate.Opaque(alpha));
        Assert.Equal(transparent, SdlGpuAlphaPredicate.Transparent(alpha));
        Assert.False(SdlGpuAlphaPredicate.Opaque(alpha) && SdlGpuAlphaPredicate.Transparent(alpha));
    }

    [Fact]
    public void NgonFillAndOptionalEdgeLoopUseDistinctPipelineTopologies()
    {
        RenderMaterial material = new RenderMaterial
        {
            RenderMode = RenderMode.Translucent,
            Alpha = 1,
            CullingMode = CullingMode.Neither
        };
        PipelineKey fill = PipelineKey.From(material, RenderPrimitive.Ngon,
            RenderPassKind.TransparentStencil);
        PipelineKey edgeLoop = fill.WithTopology(RenderTopology.Lines);

        Assert.Equal(RenderTopology.Triangles, fill.Topology);
        Assert.Equal(RenderTopology.Lines, edgeLoop.Topology);
        Assert.NotEqual(fill, edgeLoop);
    }

    [Theory]
    [InlineData(false, CollisionColor.None, 1f, 1f, 0f, 0f, 1f)]
    [InlineData(true, CollisionColor.Entity, 1f, 1f, 0f, 0f, 1f)]
    [InlineData(true, CollisionColor.None, .5f, 1f, 0f, 0f, 1f)]
    [InlineData(true, CollisionColor.None, 1f, 0f, 0f, 1f, 1f)]
    public void NgonDiagnosticEdgeColorMatchesLegacyVolumeCondition(bool showCollision,
        CollisionColor displayColor, float displayAlpha, float red, float green, float blue, float alpha)
    {
        Assert.Equal(new OpenTK.Mathematics.Vector4(red, green, blue, alpha),
            ScenePresentation.GetNgonEdgeColor(showCollision, displayColor, displayAlpha));
    }

    [Fact]
    public void HistoricalDebugPointBuffersHonorScenePresentationPoolOwnership()
    {
        var first = new OpenTK.Mathematics.Vector3(1, 2, 3);
        var second = new OpenTK.Mathematics.Vector3(4, 5, 6);
        var third = new OpenTK.Mathematics.Vector3(7, 8, 9);
        var fourth = new OpenTK.Mathematics.Vector3(10, 11, 12);
        OpenTK.Mathematics.Vector3[] quad = HistoricalCollisionDebugPresentation.RentQuad(
            first, second, third, fourth);
        OpenTK.Mathematics.Vector3[] box = HistoricalCollisionDebugPresentation.Box(first, fourth);
        try
        {
            Assert.True(quad.Length >= 4);
            Assert.Equal(new[] { first, second, third, fourth }, quad[..4]);
            Assert.True(box.Length >= 8);
            Assert.Equal(first, box[0]);
            Assert.Equal(fourth, box[7]);
        }
        finally
        {
            // ScenePresentation returns every submitted non-mesh point array
            // through this exact pool when the following frame begins.
            ArrayPool<OpenTK.Mathematics.Vector3>.Shared.Return(quad);
            ArrayPool<OpenTK.Mathematics.Vector3>.Shared.Return(box);
        }
    }

    [Fact]
    public void RenderFrameFreezesNgonDiagnosticEdgeColor()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        DrawSubmission ngon = frame.Acquire();
        ngon.Primitive = RenderPrimitive.Ngon;
        ngon.EdgeColor = new OpenTK.Mathematics.Vector4(0, 0, 1, 1);
        frame.Add(ngon);
        frame.Seal();

        ngon.EdgeColor = new OpenTK.Mathematics.Vector4(1, 0, 0, 1);
        Assert.Equal(new OpenTK.Mathematics.Vector4(0, 0, 1, 1), ngon.FrozenEdgeColor);

        frame.Reset();
        Assert.Equal(new OpenTK.Mathematics.Vector4(1, 0, 0, 1), ngon.EdgeColor);
        Assert.Equal(ngon.EdgeColor, ngon.FrozenEdgeColor);
    }

    [Fact]
    public void RenderFrameFreezesMaterialAndKeepsPassOrderAuthoritative()
    {
        var frame = new RenderFrame(capacity: 2, maximumCapacity: 2);
        DrawSubmission opaque = frame.Acquire();
        opaque.Alpha = 1;
        opaque.RenderMode = RenderMode.Normal;
        opaque.Diffuse = new OpenTK.Mathematics.Vector3(.2f, .4f, .6f);
        frame.Add(opaque);

        DrawSubmission transparent = frame.Acquire();
        transparent.Alpha = .5f;
        transparent.RenderMode = RenderMode.Translucent;
        frame.Add(transparent);

        // Compatibility code may continue touching its legacy-facing fields
        // after submission construction, but backend consumption is frozen at
        // Add() and must not diverge from the material snapshot.
        opaque.Diffuse = OpenTK.Mathematics.Vector3.One;
        transparent.Alpha = 1;
        frame.Seal();

        Assert.Single(frame.OpaqueItems);
        Assert.Single(frame.TransparentItems);
        Assert.Equal(new OpenTK.Mathematics.Vector3(.2f, .4f, .6f),
            frame.OpaqueItems[0].Material.Diffuse);
        Assert.Equal(.5f, frame.TransparentItems[0].Material.Alpha);
        Assert.Equal(RenderPassKind.Opaque, frame.OpaqueItems[0].Pass);
        Assert.Equal(RenderPassKind.TransparentStencil, frame.TransparentItems[0].Pass);
    }

    [Fact]
    public void RenderFrameCapturesImmutablePresentationStages()
    {
        var sourceStack = Enumerable.Range(0, 16).Select(i => (float)i).ToArray();
        var sourceVertices = new[]
        {
            new RenderOverlayVertex(new OpenTK.Mathematics.Vector3(1, 2, 0),
                new OpenTK.Mathematics.Vector2(1, 0), OpenTK.Mathematics.Vector4.One)
        };
        var material = new RenderMaterial
        {
            Alpha = 1,
            Textured = false,
            Lighting = false,
            CullingMode = CullingMode.Neither,
            TextureMatrix = OpenTK.Mathematics.Matrix4.Identity
        };
        var hudScene = new RenderHudSceneSubmission(material, RenderPrimitive.Mesh,
            polygonId: 7, alpha: 1, OpenTK.Mathematics.Matrix4.Identity,
            matrixStackCount: 1, sourceStack, geometryIdentity: new object(),
            textureIdentity: null, LightInfo.Zero, OpenTK.Mathematics.Matrix4.Identity,
            OpenTK.Mathematics.Matrix4.Identity);
        var overlay = new RenderOverlayCommand(RenderOverlayKind.HudObject,
            sourceVertices, alpha: .75f, useTexture: false, mode: 1, scale: .5f);
        var shiftTable = Enumerable.Range(0, 64).Select(i => (float)i).ToArray();
        var whiteoutTable = Enumerable.Range(0, 192).Select(i => (float)i).ToArray();
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        frame.AddHudSceneSubmission(hudScene);
        frame.AddOverlayCommand(overlay);
        frame.CaptureCelState(new RenderCelState(true, .2f, 4,
            new OpenTK.Mathematics.Vector2(.5f, .25f), .0625f, 1000, .0001f));
        frame.CaptureDisruption(new RenderDisruptionState(true, .5f, 12, .25f, .75f,
            shiftTable, whiteoutTable));
        frame.CaptureComposite(new RenderCompositeState(
            new OpenTK.Mathematics.Vector2i(1280, 720),
            new OpenTK.Mathematics.Vector2i(2560, 1440), RenderCompositeFilter.Linear, true));
        frame.CaptureFade(new RenderFadeState(true, FadeType.FadeOutBlack, 0, false, .5f, .5f));

        sourceStack[0] = 99;
        sourceVertices[0] = new RenderOverlayVertex(OpenTK.Mathematics.Vector3.Zero,
            OpenTK.Mathematics.Vector2.Zero, OpenTK.Mathematics.Vector4.Zero);
        shiftTable[0] = 99;
        whiteoutTable[0] = 99;
        frame.Seal();

        Assert.Equal(0, frame.HudSceneItems[0].MatrixStack[0]);
        Assert.Equal(new OpenTK.Mathematics.Vector3(1, 2, 0), frame.OverlayCommands[0].Vertices[0].Position);
        Assert.Equal(0, frame.Disruption.ShiftTable[0]);
        Assert.Equal(0, frame.Disruption.WhiteoutTable[0]);
        Assert.Equal(RenderCompositeFilter.Linear, frame.Composite.Filter);
        Assert.Equal(.5f, frame.Fade.Coverage);
        Assert.Throws<InvalidOperationException>(() => frame.AddOverlayCommand(overlay));
    }

    [Fact]
    public void HudSceneCurrentColorRemainsSeparateFromMaterialOverride()
    {
        RenderMaterial material = new RenderMaterial
        {
            Alpha = 1,
            Textured = true,
            Lighting = false,
            CullingMode = CullingMode.Neither,
            TextureMatrix = OpenTK.Mathematics.Matrix4.Identity
        };
        OpenTK.Mathematics.Vector4 tint = new(.25f, .5f, .75f, 1);
        var submission = new RenderHudSceneSubmission(material, RenderPrimitive.Mesh,
            polygonId: 0, alpha: 1, OpenTK.Mathematics.Matrix4.Identity,
            matrixStackCount: 0, matrixStack: null, geometryIdentity: new object(),
            textureIdentity: null, LightInfo.Zero, OpenTK.Mathematics.Matrix4.Identity,
            OpenTK.Mathematics.Matrix4.Identity, currentColor: tint);

        Assert.Equal(tint, submission.CurrentColor);
        Assert.Null(submission.Material.ColorOverride);
    }

    [Fact]
    public void PresentationReadOnlyViewsAreStableAndDisabledDisruptionIsShared()
    {
        var overlay = new RenderOverlayCommand(RenderOverlayKind.HudObject,
            new[] { new RenderOverlayVertex(OpenTK.Mathematics.Vector3.Zero,
                OpenTK.Mathematics.Vector2.Zero, OpenTK.Mathematics.Vector4.One) });
        Assert.Same(overlay.Vertices, overlay.Vertices);

        RenderDisruptionState disabled = RenderDisruptionState.Disabled;
        Assert.Same(disabled.ShiftTable, disabled.ShiftTable);
        Assert.Same(disabled.WhiteoutTable, disabled.WhiteoutTable);
    }

    [Fact]
    public void RenderOverlayStageMarkersPreservePostProcessOrder()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.HudScene));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.Cel));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.SceneComposite));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.HudOverlay));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.SpectatorOverlay));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.ReplayOverlay));
        frame.AddOverlayCommand(RenderOverlayCommand.StageMarker(RenderPresentationStage.Fade));
        frame.Seal();

        Assert.Equal(new[]
        {
            RenderPresentationStage.HudScene,
            RenderPresentationStage.Cel,
            RenderPresentationStage.SceneComposite,
            RenderPresentationStage.HudOverlay,
            RenderPresentationStage.SpectatorOverlay,
            RenderPresentationStage.ReplayOverlay,
            RenderPresentationStage.Fade
        }, frame.OverlayCommands.Select(command => command.Stage));
    }

    [Fact]
    public void SdlPresentationOrderAcceptsOnlyTheFixedStageSequence()
    {
        var commands = new List<RenderOverlayCommand>
        {
            RenderOverlayCommand.StageMarker(RenderPresentationStage.HudScene),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.Cel),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.SceneComposite),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.HudOverlay),
            Overlay(RenderPresentationStage.HudOverlay),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.SpectatorOverlay),
            Overlay(RenderPresentationStage.SpectatorOverlay),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.ReplayOverlay),
            Overlay(RenderPresentationStage.ReplayOverlay),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.Fade),
            new RenderOverlayCommand(RenderOverlayKind.Fade, Vertices(3)
                .Select(vertex => new RenderOverlayVertex(vertex.Position, vertex.TexCoord, vertex.Color))
                .ToArray(), stage: RenderPresentationStage.Fade)
        };

        SdlGpuPresentationOrder.Validate(commands);
        Assert.Throws<InvalidOperationException>(() => SdlGpuPresentationOrder.Validate(
            new[] { Overlay(RenderPresentationStage.HudScene) }));
        commands[1] = RenderOverlayCommand.StageMarker(RenderPresentationStage.SceneComposite);
        Assert.Throws<InvalidOperationException>(() => SdlGpuPresentationOrder.Validate(commands));

        static RenderOverlayCommand Overlay(RenderPresentationStage stage)
            => new(RenderOverlayKind.FlatBox, new[]
            {
                new RenderOverlayVertex(OpenTK.Mathematics.Vector3.Zero,
                    OpenTK.Mathematics.Vector2.Zero, OpenTK.Mathematics.Vector4.One),
                new RenderOverlayVertex(OpenTK.Mathematics.Vector3.UnitX,
                    OpenTK.Mathematics.Vector2.Zero, OpenTK.Mathematics.Vector4.One),
                new RenderOverlayVertex(OpenTK.Mathematics.Vector3.UnitY,
                    OpenTK.Mathematics.Vector2.Zero, OpenTK.Mathematics.Vector4.One)
            }, stage: stage);
    }

    [Fact]
    public void SpectatorAndReplayFlatBoxHelpersKeepTheirActiveStages()
    {
        var commands = new List<RenderOverlayCommand>
        {
            RenderOverlayCommand.StageMarker(RenderPresentationStage.HudScene),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.Cel),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.SceneComposite),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.HudOverlay),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.SpectatorOverlay),
            ScenePresentation.CreateHudFlatBoxCommand(new OpenTK.Mathematics.Vector2i(1280, 720),
                5, 4, 251, 29, new OpenTK.Mathematics.Vector4(0, 0, 0, .7f),
                RenderPresentationStage.SpectatorOverlay),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.ReplayOverlay),
            ScenePresentation.CreateHudFlatBoxCommand(new OpenTK.Mathematics.Vector2i(1280, 720),
                8, 161, 248, 191, new OpenTK.Mathematics.Vector4(0, 0, 0, .8f),
                RenderPresentationStage.ReplayOverlay),
            RenderOverlayCommand.StageMarker(RenderPresentationStage.Fade)
        };

        SdlGpuPresentationOrder.Validate(commands);
        Assert.Equal(RenderPresentationStage.SpectatorOverlay, commands[5].Stage);
        Assert.Equal(RenderPresentationStage.ReplayOverlay, commands[7].Stage);
    }

    [Fact]
    public void SdlMaskCoordinatesConvertTopOriginBeforeLegacyCentering()
    {
        OpenTK.Mathematics.Vector2 viewport = new(320, 180);
        OpenTK.Mathematics.Vector2 nearTop = SdlGpuMaskCoordinates.FromTopOrigin(new(80, 20), viewport);
        OpenTK.Mathematics.Vector2 nearBottom = SdlGpuMaskCoordinates.FromTopOrigin(new(80, 160), viewport);

        Assert.Equal(.25f, nearTop.X);
        Assert.Equal(.28125f, nearTop.Y);
        Assert.Equal(.71875f, nearBottom.Y);
        Assert.True(nearTop.Y < nearBottom.Y);
    }

    [Fact]
    public void SdlHudScenePipelineDisablesDepthLikeLegacyHudUniforms()
    {
        RenderMaterial material = new() { CullingMode = CullingMode.Back };
        PipelineKey pipeline = SdlGpuSceneResources.CreateHudPipelineKey(material, "test-format");

        Assert.Equal(RenderShaderVariant.Scene, pipeline.ShaderVariant);
        Assert.Equal(RenderBlendMode.Alpha, pipeline.BlendMode);
        Assert.Equal(RenderDepthMode.Disabled, pipeline.DepthMode);
        Assert.False(pipeline.DepthWrite);
        Assert.Equal(RenderStencilMode.Disabled, pipeline.StencilMode);
        Assert.Equal(RenderCullMode.Back, pipeline.CullMode);
    }

    [Fact]
    public void SdlCelSurfaceConstantsUseCapturedBandsAndTextureAverage()
    {
        RenderFrameOptions options = new(ShowTextures: true, ShowColors: true,
            Wireframe: false, FaceCulling: true, Filtering: false, Lighting: true,
            Fog: true, CelShading: true, CelBands: 8, CelEdge: .5f,
            VolumeEdges: 0, ShowInvisible: false, NoLines: false);
        TextureIdentity identity = new(new object());
        OpenTK.Mathematics.Vector3 average = new(.2f, .4f, .6f);
        var textures = new Dictionary<TextureIdentity, RenderTexturePixels>
        {
            [identity] = new RenderTexturePixels(identity, 1, 1,
                new byte[] { 255, 255, 255, 255 }, alphaWeightedFlatColor: average)
        };
        RenderMaterial material = new() { Textured = true, Texture = identity };

        Assert.Equal(8, SdlGpuCelSurface.BandCount(options));
        Assert.True(SdlGpuCelSurface.TryGetFlatColor(options, textures, material, out var actual));
        Assert.Equal(average, actual);
        Assert.Equal(0, SdlGpuCelSurface.BandCount(options with { CelShading = false }));
        Assert.False(SdlGpuCelSurface.TryGetFlatColor(options with { ShowTextures = false },
            textures, material, out _));
    }

    [Fact]
    public void BackendSelectorDefaultsSdlAndRejectsLegacyOrInvalidValues()
    {
        RenderBackendSelection.ResetForTests();
        Assert.Equal(RenderBackendKind.Sdl, RenderBackendSelection.Current);
        Assert.Equal("sdl", RenderBackendSelection.CurrentCliValue);
        Assert.False(RenderBackendSelection.ApplyArguments(new[] { "--renderer=legacy" }, out string? error));
        Assert.Contains("Expected sdl", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RenderBackendKind.Sdl, RenderBackendSelection.Current);
        Assert.False(RenderBackendSelection.ApplyArguments(new[] { "--renderer=other" }, out error));
        Assert.Contains("Expected sdl", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RenderBackendKind.Sdl, RenderBackendSelection.Current);
        Assert.True(RenderBackendSelection.ApplyArguments(new[] { "--renderer=sdl" }, out error));
        Assert.Null(error);
        Assert.Equal(RenderBackendKind.Sdl, RenderBackendSelection.Current);
        RenderBackendSelection.ResetForTests();
    }

    [Fact]
    public void BackendDefaultsAndMeshPreparationPredicatesArePlatformPure()
    {
        RenderBackendKind before = RenderBackendSelection.Current;

        Assert.Equal(RenderBackendKind.Gles, RenderBackendSelection.DefaultForPlatform(isAndroid: true));
        Assert.Equal(RenderBackendKind.Sdl, RenderBackendSelection.DefaultForPlatform(isAndroid: false));
        Assert.True(RenderBackendSelection.UsesPortableMeshPreparation(RenderBackendKind.Gles));
        Assert.True(RenderBackendSelection.UsesPortableMeshPreparation(RenderBackendKind.Sdl));
        Assert.True(RenderBackendSelection.IsGles(RenderBackendKind.Gles));
        Assert.False(RenderBackendSelection.IsGles(RenderBackendKind.Sdl));
        Assert.Equal("gles", RenderBackendSelection.ToCliValue(RenderBackendKind.Gles));

        Assert.Equal(before, RenderBackendSelection.Current);
    }

    [Fact]
    public void GlesIsDiagnosticOnlyAndNotAnAcceptedDesktopCliValue()
    {
        Assert.False(RenderBackendSelection.TryParse("gles", out _));
        Assert.False(RenderBackendSelection.ApplyArguments(new[] { "--renderer=gles" },
            out string? error));
        Assert.Contains("Expected sdl", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RenderBackendKind.Sdl, "--renderer=sdl")]
    public void ThumbnailWorkersPropagateExactSelectedBackend(RenderBackendKind backend,
        string expectedArgument)
    {
        IReadOnlyList<string> arguments = ThumbnailBatch.BuildWorkerArguments(
            new[] { "ROOM A", "ROOM B" }, 320, 180, backend);

        Assert.Equal(new[]
        {
            "-thumbnail", "ROOM A", "-thumbnail", "ROOM B",
            expectedArgument, "-size", "320x180"
        }, arguments);
    }

    [Fact]
    public void FrameLoopAcknowledgesOnlySubmittedFramesAndStillPumpsPauseMenuWhenSkipped()
    {
        var client = new RecordingFrameClient();
        var backend = new RecordingBackend { BeginResult = false };
        new GameWindowFrameLoop().Tick(1 / 60d, default, client, backend);

        Assert.Equal(0, client.Presented);
        Assert.Equal(0, client.AfterRender);
        Assert.Equal(1, client.PausePumps);

        backend.BeginResult = true;
        backend.SubmitResult = true;
        new GameWindowFrameLoop().Tick(1 / 60d, default, client, backend);
        Assert.Equal(1, client.Presented);
        Assert.Equal(1, client.AfterRender);
        Assert.Equal(2, client.PausePumps);
    }

    [Fact]
    public void FrameLoopDoesNotAcknowledgeAFrameWhoseSubmitFails()
    {
        var client = new RecordingFrameClient();
        var backend = new RecordingBackend { BeginResult = true, SubmitResult = false };
        new GameWindowFrameLoop().Tick(1 / 60d, default, client, backend);

        Assert.Equal(0, client.Presented);
        Assert.Equal(0, client.AfterRender);
        Assert.Equal(1, client.PausePumps);
        Assert.True(backend.RenderCalls > 0);
    }

    [Fact]
    public void FrameLoopManualAdvanceResetsClockAndRunsExactlyOneStep()
    {
        FrameTiming.Reset();
        // Establish real wall-clock debt first. Entering manual mode must
        // discard it; otherwise the next ordinary frame can pay the old debt
        // in addition to the one explicitly requested presentation tick.
        Assert.InRange(FrameTiming.Advance(0.20), 1, FrameTiming.MaxCatchUpSteps);
        var client = new RecordingFrameClient();
        var backend = new RecordingBackend { BeginResult = true, SubmitResult = true };
        new GameWindowFrameLoop().Tick(0.25, new WindowInputSnapshot(
            null, null, default, default, default, string.Empty, true,
            frameAdvanceMode: true), client, backend);

        Assert.Equal(1, client.SimulationSteps);
        Assert.Equal(1, FrameTiming.StepsThisFrame);
        Assert.Equal(0, FrameTiming.Advance(0));
    }

    [Fact]
    public void FrameLoopPumpsPauseMenuBetweenPresentationAcknowledgementAndRetirement()
    {
        var client = new RecordingFrameClient();
        var backend = new RecordingBackend { BeginResult = true, SubmitResult = true };
        new GameWindowFrameLoop().Tick(0, default, client, backend);
        Assert.Equal(new[] { "presented", "pause", "after" }, client.Order);
    }

    [Fact]
    public void FrameLoopSkipsAcquireAfterPresentationMarksExitButStillPumpsPauseMenu()
    {
        var client = new RecordingFrameClient { CanRender = false };
        var backend = new RecordingBackend { BeginResult = true, SubmitResult = true };
        new GameWindowFrameLoop().Tick(0, default, client, backend);

        Assert.Equal(0, backend.BeginCalls);
        Assert.Equal(0, backend.RenderCalls);
        Assert.Equal(0, client.Presented);
        Assert.Equal(0, client.AfterRender);
        Assert.Equal(1, client.PausePumps);
    }

    [Fact]
    public void RenderToolFramePolicyPreservesSubmitCallbackOrder()
    {
        var order = new List<string>();
        Assert.False(RenderToolFramePolicy.Complete(submitted: false,
            acknowledgePresentation: true, onFramePresented: () => order.Add("presented"),
            afterRenderFrame: () => order.Add("after")));
        Assert.Empty(order);

        Assert.True(RenderToolFramePolicy.Complete(submitted: true,
            acknowledgePresentation: true, onFramePresented: () => order.Add("presented"),
            afterRenderFrame: () => order.Add("after")));
        Assert.Equal(new[] { "presented", "after" }, order);

        order.Clear();
        Assert.True(RenderToolFramePolicy.Complete(submitted: true,
            acknowledgePresentation: false, onFramePresented: () => order.Add("presented"),
            afterRenderFrame: () => order.Add("after")));
        Assert.Equal(new[] { "after" }, order);
    }

    [Fact]
    public void MapAuditCaptureCadenceUsesCompletedStepNumbers()
    {
        Assert.False(MapAudit.ShouldCaptureSample(0));
        Assert.True(MapAudit.ShouldCaptureSample(1));
        Assert.False(MapAudit.ShouldCaptureSample(60));
        Assert.True(MapAudit.ShouldCaptureSample(61));
    }

    [Fact]
    public void RenderToolCaptureAttributionKeepsRenderedFrameIdentity()
    {
        Guid id = Guid.NewGuid();
        var request = new RenderCaptureRequest(id, originatingFrame: 61,
            CaptureTargetKind.SceneTarget, width: 1, height: 1,
            CapturePixelFormat.Rgb8, CaptureRowOrientation.BottomUp,
            CaptureDeliveryKind.Screenshot);
        var result = new RenderCaptureResult(request.RequestId, request.OriginatingFrame,
            request.Target, request.Width, request.Height, request.PixelFormat,
            request.RowOrientation, new byte[] { 1, 2, 3 });

        Assert.Equal(request.RequestId, result.RequestId);
        Assert.Equal(request.OriginatingFrame, result.OriginatingFrame);
        Assert.Equal(request.Target, result.Target);
    }

    [Fact]
    public void SurfaceKeepsLogicalAndFramebufferSizesSeparate()
    {
        var surface = new RenderSurfaceInfo(new OpenTK.Mathematics.Vector2i(800, 600),
            new OpenTK.Mathematics.Vector2i(1600, 1200), false, true, "R8G8B8A8_UNORM", "vsync");
        Assert.Equal(new OpenTK.Mathematics.Vector2i(800, 600), surface.LogicalSize);
        Assert.Equal(new OpenTK.Mathematics.Vector2i(1600, 1200), surface.FramebufferSize);
        Assert.True(surface.HasDrawablePixels);
    }

    [Fact]
    public void DeviceScopedResourcesAreDisposedWhenCachesInvalidate()
    {
        using var caches = new DeviceRenderCaches();
        var resource = new DisposableProbe();
        Assert.Same(resource, caches.GetOrAddDeviceResource("proof", () => resource));
        Assert.Equal(1, caches.DeviceResourceCount);
        caches.Invalidate();
        Assert.True(resource.Disposed);
        Assert.Equal(0, caches.DeviceResourceCount);
    }

    [Fact]
    public void CheckedInShaderManifestMatchesPublishedArtifacts()
    {
        string outputRoot = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders");
        string source = Path.Combine(outputRoot, "scene_triangle.hlsl");
        string manifest = Path.Combine(outputRoot, "Generated", "manifest.json");
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(manifest));
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();
        string text = File.ReadAllText(manifest);
        string shaderSource = File.ReadAllText(source);
        Assert.Contains("TEXCOORD0", shaderSource, StringComparison.Ordinal);
        Assert.Contains("TEXCOORD1", shaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("POSITION", shaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("COLOR0", shaderSource, StringComparison.Ordinal);
        Assert.Contains($"\"source_sha256\": \"{sourceHash}\"", text, StringComparison.Ordinal);
        Assert.Contains("SDL3-CS.MacOS.Shadercross", text, StringComparison.Ordinal);
        Assert.Contains("3.0.0.11", text, StringComparison.Ordinal);
        Assert.Contains("1ff05bec573988a98ef9e0260b4da44f512b8367", text, StringComparison.Ordinal);
        Assert.Contains("\"cli_sha256\":", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dxil\":", text, StringComparison.Ordinal);
        foreach (string artifact in new[]
        {
            "scene_triangle.vert.spv", "scene_triangle.frag.spv",
            "scene_triangle.vert.msl", "scene_triangle.frag.msl",
            "scene_triangle.vert.dxil", "scene_triangle.frag.dxil"
        })
        {
            Assert.True(File.Exists(Path.Combine(outputRoot, "Generated", artifact)), artifact);
        }

        string sceneSource = Path.Combine(outputRoot, "scene.hlsl");
        string sceneManifest = Path.Combine(outputRoot, "Generated", "scene_manifest.json");
        Assert.True(File.Exists(sceneSource));
        Assert.True(File.Exists(sceneManifest));
        string sceneHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sceneSource))).ToLowerInvariant();
        string sceneSourceText = File.ReadAllText(sceneSource);
        Assert.Contains($"\"source_sha256\": \"{sceneHash}\"", File.ReadAllText(sceneManifest), StringComparison.Ordinal);
        Assert.Contains("float3 cel_shade", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("smoothstep(0.46f, 0.54f", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("textureColor.rgb = flatColor.rgb", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("result.rgb = cel_shade(result.rgb)", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("visualLightPositionRadius[8]", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("visualLightColorIntensity[8]", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("min((uint)visualLightOptions.x, 8u)", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("attenuation *= attenuation", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("float3 worldPosition : TEXCOORD0", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("float3 worldNormal : TEXCOORD1", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("float4 cameraWorldPosition", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("EvaluateRoomLighting", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("EvaluatePointLight", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("EvaluateSpecular", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("float4 tangent : TEXCOORD6", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("Texture2D normalTexture", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("ResolveNormalMap", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("Texture2D emissiveTexture", sceneSourceText,
            StringComparison.Ordinal);
        Assert.Contains("materialEmission = hasMappedEmission", sceneSourceText,
            StringComparison.Ordinal);
        Assert.Contains("result.rgb += materialEmission", sceneSourceText,
            StringComparison.Ordinal);
        Assert.Contains("TextureCube reflectionTexture : register(t3, space2)",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("float3 reflectionVector = reflect(-viewDirection, normal)",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("SampleLevel(\n        reflectionSampler, reflectionVector",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("result.rgb += EvaluateReflection(input.worldPosition, normal)",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("float reciprocalDeterminant = 1.0f / determinant",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("* reciprocalDeterminant, tangent",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("* reciprocalDeterminant, bitangent",
            sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("renderOptions.z > 0.0f", sceneSourceText, StringComparison.Ordinal);
        Assert.Contains("(1.0f - fogDensity)", sceneSourceText, StringComparison.Ordinal);
        foreach (string artifact in new[]
        {
            "scene.vert.spv", "scene.frag.spv", "scene.vert.msl", "scene.frag.msl",
            "scene.vert.dxil", "scene.frag.dxil"
        })
        {
            Assert.True(File.Exists(Path.Combine(outputRoot, "Generated", artifact)), artifact);
        }

        foreach ((string stem, string[] tokens) in new[]
        {
            ("fullscreen", new[] { "LegacyMaskTexcoord", "fadeColor", "operation" }),
            ("hud", new[] { "LegacyMaskTexcoord", "hudOptions", "hudTexture" }),
            ("disruption", new[] { "ShiftValue", "WhiteoutValue", "disruptionOptions" }),
            ("cel", new[] { "KinkAbs", "EdgeAt", "depthTexture" }),
            ("bloom", new[] { "BlurSample", "bloomOptions", "sourceTexture" }),
            ("tone_map", new[] { "ToneMapAces", "LinearToSRGB", "toneMapOptions" }),
            ("color_grade", new[] { "LutUv", "LutTextureSize", "colorGradeOptions", "lutTexture" }),
            ("visor", new[] { "VisorConstants", "EdgeMask", "visorDamage", "sourceTexture" })
        })
        {
            string familySource = Path.Combine(outputRoot, $"{stem}.hlsl");
            string familyManifest = Path.Combine(outputRoot, "Generated", $"{stem}_manifest.json");
            Assert.True(File.Exists(familySource), familySource);
            Assert.True(File.Exists(familyManifest), familyManifest);
            string familySourceText = File.ReadAllText(familySource);
            string familyManifestText = File.ReadAllText(familyManifest);
            string familyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(familySource))).ToLowerInvariant();
            Assert.Contains($"\"source_sha256\": \"{familyHash}\"", familyManifestText, StringComparison.Ordinal);
            foreach (string token in tokens)
            {
                Assert.Contains(token, familySourceText, StringComparison.Ordinal);
            }
            foreach (string artifact in new[]
            {
                $"{stem}.vert.spv", $"{stem}.frag.spv", $"{stem}.vert.msl",
                $"{stem}.frag.msl", $"{stem}.vert.dxil", $"{stem}.frag.dxil"
            })
            {
                Assert.True(File.Exists(Path.Combine(outputRoot, "Generated", artifact)), artifact);
            }
        }
    }

    [Fact]
    public void WorldPlanPreservesTheSixPassStateContract()
    {
        RenderWorldPass[] passes = RenderWorldPlan.Passes.ToArray();

        Assert.Equal(new[]
        {
            RenderPassKind.Opaque,
            RenderPassKind.Decal,
            RenderPassKind.TransparentStencil,
            RenderPassKind.DepthRebuild,
            RenderPassKind.TransparentBehind,
            RenderPassKind.TransparentFront
        }, passes.Select(pass => pass.Kind));
        Assert.False(passes[0].ClearDepthBefore);
        Assert.True(passes[3].ClearDepthBefore);
        Assert.Equal(RenderAlphaTestMode.EqualOne, passes[0].AlphaTestMode);
        Assert.Equal(RenderAlphaTestMode.LessThanOne, passes[2].AlphaTestMode);
        Assert.Equal(RenderColorWriteMask.None, passes[2].ColorWriteMask);
        Assert.Equal(RenderStencilMode.MarkTransparent, passes[2].StencilMode);
        Assert.Equal(RenderStencilMode.NotEqualPolygon, passes[4].StencilMode);
        Assert.Equal(RenderStencilMode.EqualPolygon, passes[5].StencilMode);
        Assert.True(passes[1].DecalDepthBias);
        Assert.False(passes[4].DepthWrite);
        Assert.Equal(RenderDepthMode.LessOrEqual, passes[5].DepthMode);
    }

    [Fact]
    public void WorldPlanReusesSealedListsAndCapturedMeshResources()
    {
        var frame = new RenderFrame(3, 3);
        DrawSubmission opaque = frame.Acquire();
        opaque.GeometryIdentity = new object();
        frame.Add(opaque);
        DrawSubmission decal = frame.Acquire();
        decal.RenderMode = RenderMode.Decal;
        decal.GeometryIdentity = new object();
        frame.Add(decal);
        DrawSubmission transparent = frame.Acquire();
        transparent.Primitive = RenderPrimitive.Quad;
        transparent.Alpha = .5f;
        frame.Add(transparent);
        CpuMesh staticMesh = MeshCompiler.Compile(Vertices(3), MeshPrimitiveTopology.Triangles);
        CpuMesh dynamicMesh = MeshCompiler.Compile(Vertices(3), MeshPrimitiveTopology.Triangles);
        frame.CaptureMesh(opaque.GeometryIdentity, staticMesh);
        frame.CaptureMesh(transparent, dynamicMesh);
        frame.Seal();

        Assert.Same(frame.OpaqueItems,
            RenderWorldPlan.GetSubmissions(frame, RenderPassKind.DepthRebuild));
        Assert.Same(frame.TransparentItems,
            RenderWorldPlan.GetSubmissions(frame, RenderPassKind.TransparentFront));
        Assert.Same(staticMesh, RenderWorldPlan.ResolveMesh(frame, opaque));
        Assert.Same(dynamicMesh, RenderWorldPlan.ResolveMesh(frame, transparent));
        Assert.Throws<InvalidOperationException>(() => RenderWorldPlan.ResolveMesh(frame, decal));
    }

    [Fact]
    public void WorldPlanKeepsNgonFillAndEdgeSelectionIndependent()
    {
        var frame = new RenderFrame(2, 2);
        DrawSubmission ngon = frame.Acquire();
        ngon.Primitive = RenderPrimitive.Ngon;
        frame.Add(ngon);
        DrawSubmission noLines = frame.Acquire();
        noLines.Primitive = RenderPrimitive.Ngon;
        noLines.NoLines = true;
        frame.Add(noLines);

        Assert.Equal(RenderMeshStreams.Triangles | RenderMeshStreams.Lines,
            RenderWorldPlan.GetMeshStreams(ngon, default));
        Assert.Equal(RenderMeshStreams.Lines,
            RenderWorldPlan.GetMeshStreams(ngon, default(RenderFrameOptions) with { VolumeEdges = 1 }));
        Assert.Equal(RenderMeshStreams.Triangles,
            RenderWorldPlan.GetMeshStreams(ngon, default(RenderFrameOptions) with { VolumeEdges = 2 }));
        Assert.Equal(RenderMeshStreams.Triangles,
            RenderWorldPlan.GetMeshStreams(noLines, default));
    }

    [Fact]
    public void WorldPlanUsesCapturedBillboardMatrices()
    {
        var frame = new RenderFrame(1, 1);
        OpenTK.Mathematics.Matrix4 sphere
            = OpenTK.Mathematics.Matrix4.CreateRotationX(.25f);
        OpenTK.Mathematics.Matrix4 cylinder
            = OpenTK.Mathematics.Matrix4.CreateRotationY(.5f);
        frame.CaptureState(OpenTK.Mathematics.Matrix4.Identity, sphere, cylinder,
            OpenTK.Mathematics.Matrix4.Identity, default, default, default, default,
            default, default, default, default, false, default, 0, 0, default);
        frame.Seal();

        Assert.Equal(sphere, RenderWorldPlan.GetBillboardMatrix(frame,
            new RenderMaterial { BillboardMode = BillboardMode.Sphere }));
        Assert.Equal(cylinder, RenderWorldPlan.GetBillboardMatrix(frame,
            new RenderMaterial { BillboardMode = BillboardMode.Cylinder }));
        Assert.Equal(OpenTK.Mathematics.Matrix4.Identity,
            RenderWorldPlan.GetBillboardMatrix(frame, default));
    }

    private sealed class RecordingFrameClient : IGameWindowFrameClient
    {
        public int Presented;
        public int AfterRender;
        public int PausePumps;
        public int SimulationSteps;
        public bool CanRender = true;
        public List<string> Order { get; } = new();
        bool IGameWindowFrameClient.CanRenderFrame => CanRender;
        public void OnInput(WindowInputSnapshot input) { }
        public void AdvanceSimulation(int steps) => SimulationSteps += steps;
        public void OnDrawFrame() { }
        private readonly RenderFrame _snapshot = new RenderFrame(1, 1);
        public void Render(RenderBackendFrame frame, IRenderBackend backend) => backend.Render(frame, _snapshot);
        public void OnFramePresented() { Presented++; Order.Add("presented"); }
        public void AfterRenderFrame() { AfterRender++; Order.Add("after"); }
        public void PumpPauseMenu() { PausePumps++; Order.Add("pause"); }
    }

    private sealed class RecordingBackend : IRenderBackend
    {
        public bool BeginResult;
        public bool SubmitResult;
        public int RenderCalls;
        public int BeginCalls;
        private RenderBackendFrame? _frame;
        public RenderBackendInfo Info => new("test", "test", "test", "test", "test", true, true, true);
        public RenderSurfaceInfo Surface => new(default, default, !BeginResult, BeginResult, "test", "test");
        public bool TryBeginFrame(out RenderBackendFrame frame)
        {
            BeginCalls++;
            if (!BeginResult)
            {
                frame = null!;
                return false;
            }
            _frame = frame = new RenderBackendFrame(true, false, new OpenTK.Mathematics.Vector2i(1, 1));
            return true;
        }
        public void Render(RenderBackendFrame frame, RenderFrame snapshot)
        {
            Assert.Same(_frame, frame);
            RenderCalls++;
            frame.Encoded = true;
        }
        public bool TrySubmitFrame(RenderBackendFrame frame)
        {
            if (!SubmitResult || !ReferenceEquals(_frame, frame)) return false;
            frame.Submitted = true;
            return true;
        }
        public void Resize(OpenTK.Mathematics.Vector2i logicalSize, OpenTK.Mathematics.Vector2i framebufferSize) { }
        public void InvalidateCaches() { }
        public void Dispose() { }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private sealed class NonArrayMemoryManager : MemoryManager<byte>
    {
        private readonly byte[] _bytes;

        public NonArrayMemoryManager(byte[] bytes) => _bytes = bytes;

        public override Span<byte> GetSpan() => _bytes;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
