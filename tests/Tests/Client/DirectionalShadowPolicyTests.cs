using System;
using System.IO;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class DirectionalShadowPolicyTests
{
    [Fact]
    public void QualityTiersResolveToBoundedInternalSettings()
    {
        Assert.Equal(new ShadowQualitySettings(false, 0, 0),
            ShadowQualityPolicy.Resolve(ShadowQuality.Off));
        Assert.Equal(new ShadowQualitySettings(true, 1024, 1),
            ShadowQualityPolicy.Resolve(ShadowQuality.Low));
        Assert.Equal(new ShadowQualitySettings(true, 2048, 1),
            ShadowQualityPolicy.Resolve(ShadowQuality.High));
        Assert.Equal(ShadowQuality.High, ShadowQualityPolicy.DefaultQuality);
        Assert.Equal(new ShadowQualitySettings(true, 2048, 1),
            ShadowQualityPolicy.Default);
        Assert.Equal(9, ShadowQualityPolicy.Resolve(ShadowQuality.High).PcfTapCount);
        Assert.Equal(ShadowQualityPolicy.Resolve(ShadowQuality.Off),
            ShadowQualityPolicy.Resolve((ShadowQuality)byte.MaxValue));
        Assert.Throws<ArgumentException>(() => new ShadowQualitySettings(false, 1024, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowQualitySettings(true, 8192, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowQualitySettings(true, 2048, 2));
    }

    [Fact]
    public void PresentationShellCanOptOutOfDirectionalShadowMap()
    {
        var frame = new RenderFrame(2, 2);
        DrawSubmission character = frame.Acquire();
        frame.Add(character);
        DrawSubmission shell = frame.Acquire();
        shell.CastsDirectionalShadow = false;
        frame.Add(shell);

        Assert.True(DirectionalShadowCasterPolicy.ShouldRender(character));
        Assert.False(DirectionalShadowCasterPolicy.ShouldRender(shell));

        frame.Reset();
        Assert.True(frame.Acquire().CastsDirectionalShadow);
    }

    [Fact]
    public void PrimaryLightUsesColorIntensityStableTiesAndValidOverride()
    {
        Assert.True(PrimaryShadowLightPolicy.TrySelect(
            Vector3.UnitX, new Vector3(.2f),
            Vector3.UnitY, new Vector3(.8f),
            environmentOverride: null, out PrimaryShadowLight brighter));
        Assert.Equal(1, brighter.SourceIndex);
        Assert.Equal(Vector3.UnitY, brighter.Direction);

        Assert.True(PrimaryShadowLightPolicy.TrySelect(
            Vector3.UnitX, Vector3.One,
            Vector3.UnitY, Vector3.One,
            environmentOverride: null, out PrimaryShadowLight tie));
        Assert.Equal(0, tie.SourceIndex);

        Assert.True(PrimaryShadowLightPolicy.TrySelect(
            Vector3.UnitX, Vector3.One,
            new Vector3(0, 4, 0), new Vector3(.1f),
            environmentOverride: 1, out PrimaryShadowLight authored));
        Assert.Equal(1, authored.SourceIndex);
        Assert.Equal(Vector3.UnitY, authored.Direction);
    }

    [Fact]
    public void PrimaryLightRejectsInvalidCandidatesAndInvalidOverrideFallsBack()
    {
        Assert.True(PrimaryShadowLightPolicy.TrySelect(
            new Vector3(float.NaN, 0, 0), Vector3.One,
            Vector3.UnitZ, new Vector3(-1, .5f, 0),
            environmentOverride: 0, out PrimaryShadowLight fallback));
        Assert.Equal(1, fallback.SourceIndex);
        Assert.Equal(new Vector3(0, .5f, 0), fallback.Color);

        Assert.False(PrimaryShadowLightPolicy.TrySelect(
            Vector3.Zero, Vector3.One,
            Vector3.UnitY, Vector3.Zero,
            environmentOverride: 7, out _));

        Assert.True(PrimaryShadowLightPolicy.TrySelect(
            new Vector3(float.MaxValue, float.MaxValue, 0), Vector3.One,
            Vector3.Zero, Vector3.Zero,
            environmentOverride: null, out PrimaryShadowLight hugeDirection));
        Assert.True(float.IsFinite(hugeDirection.Direction.X));
        Assert.Equal(1f, hugeDirection.Direction.Length, 5);
    }

    [Fact]
    public void ProjectionSnapsCameraMotionAndBoundsAuthoredRanges()
    {
        ShadowQualitySettings quality = ShadowQualityPolicy.Resolve(ShadowQuality.High);
        Assert.True(StableShadowProjectionPolicy.TryCreate(
            new Vector3(10, 3, -5), new Vector3(.2f, -1, .3f), quality,
            requestedHalfExtent: float.PositiveInfinity,
            requestedDepthRange: float.NaN, out StableShadowProjection first));

        Assert.Equal(StableShadowProjectionPolicy.DefaultHalfExtent, first.HalfExtent);
        Assert.Equal(StableShadowProjectionPolicy.DefaultDepthRange, first.DepthRange);
        Assert.Equal(96f / ShadowQualityPolicy.HighMapSize,
            first.WorldUnitsPerTexel);
        AssertMatrixFinite(first.View);
        AssertMatrixFinite(first.Projection);
        AssertMatrixFinite(first.ViewProjection);
        Vector4 projectedCenter = Vector4.TransformRow(
            new Vector4(first.SnappedCenter, 1), first.ViewProjection);
        Assert.Equal(0, projectedCenter.X, 5);
        Assert.Equal(0, projectedCenter.Y, 5);

        Assert.True(StableShadowProjectionPolicy.TryCreate(
            first.SnappedCenter, new Vector3(.2f, -1, .3f), quality,
            requestedHalfExtent: 0,
            requestedDepthRange: float.MaxValue, out StableShadowProjection bounded));
        Assert.Equal(StableShadowProjectionPolicy.MinimumHalfExtent, bounded.HalfExtent);
        Assert.Equal(StableShadowProjectionPolicy.MaximumDepthRange, bounded.DepthRange);
    }

    [Fact]
    public void SubTexelLateralMotionKeepsTheSameProjection()
    {
        ShadowQualitySettings quality = ShadowQualityPolicy.Resolve(ShadowQuality.High);
        Vector3 direction = new Vector3(.25f, -1, .5f).Normalized();
        Assert.True(StableShadowProjectionPolicy.TryCreate(
            Vector3.Zero, direction, quality, 48, 128,
            out StableShadowProjection first));

        Vector3 upReference = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) < .99f
            ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 right = Vector3.Cross(upReference, direction).Normalized();
        Vector3 subTexelCamera = right * (first.WorldUnitsPerTexel * .49f);
        Assert.True(StableShadowProjectionPolicy.TryCreate(
            subTexelCamera, direction, quality, 48, 128,
            out StableShadowProjection second));

        Assert.Equal(first.SnappedCenter, second.SnappedCenter);
        Assert.Equal(first.View, second.View);
        Assert.Equal(first.ViewProjection, second.ViewProjection);
    }

    [Fact]
    public void ProjectionFailsClosedForDisabledAndDegenerateInputs()
    {
        Assert.False(StableShadowProjectionPolicy.TryCreate(
            Vector3.Zero, Vector3.UnitY,
            ShadowQualityPolicy.Resolve(ShadowQuality.Off), 48, 128, out _));
        Assert.False(StableShadowProjectionPolicy.TryCreate(
            Vector3.Zero, Vector3.Zero,
            ShadowQualityPolicy.Resolve(ShadowQuality.High), 48, 128, out _));
        Assert.False(StableShadowProjectionPolicy.TryCreate(
            new Vector3(float.NaN, 0, 0), Vector3.UnitY,
            ShadowQualityPolicy.Resolve(ShadowQuality.High), 48, 128, out _));

        // A vertical key light selects an alternate up axis instead of
        // producing a zero cross product.
        Assert.True(StableShadowProjectionPolicy.TryCreate(
            Vector3.Zero, Vector3.UnitY,
            ShadowQualityPolicy.Resolve(ShadowQuality.High), 48, 128,
            out StableShadowProjection vertical));
        AssertMatrixFinite(vertical.ViewProjection);
    }

    [Fact]
    public void PcfOffsetsFormOneTexelThreeByThreeKernel()
    {
        const int mapSize = 2048;
        float texel = 1f / mapSize;
        Assert.Equal(new Vector2(-texel, -texel), ShadowPcfPolicy.GetOffset(0, mapSize));
        Assert.Equal(Vector2.Zero, ShadowPcfPolicy.GetOffset(4, mapSize));
        Assert.Equal(new Vector2(texel, texel), ShadowPcfPolicy.GetOffset(8, mapSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowPcfPolicy.GetOffset(-1, mapSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowPcfPolicy.GetOffset(9, mapSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowPcfPolicy.GetOffset(0, 0));
    }

    [Fact]
    public void RenderFrameOwnsAndClearsImmutableShadowSnapshot()
    {
        var frame = new RenderFrame(1, 1);
        var shadow = new RenderDirectionalShadowState(1, new Vector3(0, -2, 0),
            Matrix4.Identity, ShadowQualityPolicy.HighMapSize,
            ShadowQualityPolicy.PcfRadius);

        frame.CaptureDirectionalShadow(shadow);
        frame.Seal();

        Assert.True(frame.DirectionalShadow.Enabled);
        Assert.Equal(1, frame.DirectionalShadow.SourceIndex);
        Assert.Equal(-Vector3.UnitY, frame.DirectionalShadow.Direction);
        Assert.Equal(2048, frame.DirectionalShadow.MapSize);
        Assert.Throws<InvalidOperationException>(() =>
            frame.CaptureDirectionalShadow(RenderDirectionalShadowState.Disabled));

        frame.Reset();
        Assert.False(frame.DirectionalShadow.Enabled);
        Assert.Throws<ArgumentException>(() => new RenderDirectionalShadowState(
            0, Vector3.UnitY, Matrix4.Identity with { M11 = float.NaN }, 2048, 1));
    }

    [Fact]
    public void RendererRequiresEnhancedLightingBeforeCapturingShadows()
    {
        string renderer = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "Client.Presentation", "Rendering", "Renderer.cs"));
        Assert.Contains("frameQuality.GraphicsPreset == Mods.GraphicsPreset.Enhanced\n"
            + "                && LightingOn", renderer,
            StringComparison.Ordinal);
    }

    private static void AssertMatrixFinite(Matrix4 value)
    {
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
        {
            Assert.True(float.IsFinite(value[row, column]));
        }
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
