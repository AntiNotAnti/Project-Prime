using System;
using MphRead;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public class RenderInterpolationTests
{
    [Fact]
    public void CompletedPosesBlendWithoutChangingSources()
    {
        var history = new SimulationPoseHistory();
        Matrix4 first = Matrix4.CreateTranslation(1, 2, 3);
        Matrix4 second = Matrix4.CreateTranslation(3, 2, 3);
        history.Capture(first, 1, 0);
        history.Capture(second, 2, 0);
        Assert.Equal(new Vector3(2, 2, 3), history.Resolve(.5f).Row3.Xyz);
        Assert.Equal(new Vector3(2, 2, 3), (second * history.Delta(.5f)).Row3.Xyz);
        Assert.Equal(new Vector3(1, 2, 3), first.Row3.Xyz);
        Assert.Equal(new Vector3(3, 2, 3), second.Row3.Xyz);
    }

    [Theory]
    [InlineData(3ul, 0L, false, 3f)] // missed completed step
    [InlineData(2ul, 1L, false, 3f)] // lifecycle epoch
    [InlineData(2ul, 0L, true, 3f)] // explicit teleport/form/pool barrier
    [InlineData(2ul, 0L, false, 20f)] // hard discontinuity
    public void DiscontinuitySeedsCurrentPose(ulong tick, long epoch, bool barrier, float x)
    {
        var history = new SimulationPoseHistory();
        history.Capture(Matrix4.Identity, 1, 0);
        history.Capture(Matrix4.CreateTranslation(x, 0, 0), tick, epoch, barrier);
        Assert.Equal(x, history.Resolve(0).Row3.X);
        Assert.Equal(Matrix4.Identity, history.Delta(.5f));
    }

    [Fact]
    public void InvalidOrSingularPoseCannotReachGpu()
    {
        var history = new SimulationPoseHistory();
        history.Capture(Matrix4.Identity, 1, 0);
        history.Capture(Matrix4.CreateScale(0), 2, 0);
        Assert.False(history.HasSamples);
        Matrix4 invalid = Matrix4.Identity;
        invalid.M41 = float.NaN;
        history.Capture(invalid, 3, 0);
        Assert.False(history.HasSamples);
        Assert.Equal(Matrix4.Identity, history.Delta(.5f));
    }

    [Fact]
    public void CameraScalarHistoryInterpolatesFovAndResetsAtDiscontinuity()
    {
        var history = new ScalarPoseHistory();
        history.Capture(78, 1, 0);
        history.Capture(60, 2, 0);
        Assert.Equal(69, history.Resolve(.5f));

        history.Capture(100, 3, 0, discontinuity: true);
        Assert.Equal(100, history.Resolve(0));
    }

    [Fact]
    public void SubmissionChangesOnlyCopiedStackEvenWhenDrawingFails()
    {
        float[] source = {1,0,0,0, 0,1,0,0, 0,0,1,0, 4,5,6,1};
        float[] snapshot = (float[])source.Clone();
        float[] submitted = (float[])source.Clone();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            ScenePresentation.TransformCopiedStack(submitted, 1, Matrix4.CreateTranslation(-1, 2, 0));
            throw new InvalidOperationException("simulated submission failure");
        }));
        Assert.Equal(snapshot, source);
        Assert.Equal(3, submitted[12]);
        Assert.Equal(7, submitted[13]);
        Assert.Equal(6, submitted[14]);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(240)]
    public void ExtraRenderReadsDoNotChangeFixedStepMouseAim(int renderHz)
    {
        var baseline = new RenderLookAccumulator();
        var rendered = new RenderLookAccumulator();
        Vector2 baseAim = Vector2.Zero, renderAim = Vector2.Zero;
        // 720 timestamp slices align all tested display rates and 60 Hz steps.
        for (int slice = 1; slice <= 720; slice++)
        {
            double seconds = slice / 720.0;
            float dx = slice % 7 - 3, dy = slice % 5 - 2;
            baseline.Add(dx, dy, seconds);
            rendered.Add(dx, dy, seconds);
            if (slice % (720 / renderHz) == 0)
            {
                Vector2 before = rendered.Peek(seconds);
                Assert.Equal(before, rendered.Peek(seconds));
                _ = RenderLookAccumulator.AimDegrees(before, 2, true, false, .5f);
            }
            if (slice % 12 == 0)
            {
                Vector2 expected = baseline.Consume(seconds), actual = rendered.Consume(seconds);
                Assert.Equal(expected, actual);
                baseAim += RenderLookAccumulator.AimDegrees(expected, 2, true, false, .5f);
                renderAim += RenderLookAccumulator.AimDegrees(actual, 2, true, false, .5f);
                Assert.Equal(baseAim, renderAim);
                Assert.Equal(Vector2.Zero, rendered.Consume(seconds));
            }
        }
    }

    [Fact]
    public void LateLookMatchesBipedYawPitchSignsAndPreservesCameraPosition()
    {
        Matrix4 camera = Matrix4.CreateTranslation(4, 5, 6);
        Matrix4 result = RenderLookAccumulator.ApplyCameraLook(camera, new Vector2(30, 20), 0);
        Vector3 direction = -result.Row2.Xyz;
        float yaw = MathHelper.DegreesToRadians(30), pitch = MathHelper.DegreesToRadians(20);
        // PlayerInput.UpdateAimY raises Y; UpdateAimX maps X=x*cos+z*sin, Z=-x*sin+z*cos.
        Vector3 expected = new(-MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), -MathF.Cos(pitch) * MathF.Cos(yaw));
        Assert.True((expected - direction).Length < .00001f);
        Assert.Equal(camera.Row3, result.Row3);
        Assert.Equal(Matrix4.CreateTranslation(4, 5, 6), camera);
        Matrix4 limited = RenderLookAccumulator.ApplyCameraLook(Matrix4.Identity, new Vector2(0, 50), 80);
        Assert.True(MathF.Abs((-limited.Row2.Xyz).Y - MathF.Sin(MathHelper.DegreesToRadians(5))) < .00001f);
    }

    [Fact]
    public void ClockAlphaIsBoundedAndResetStallDebtHaveBarriers()
    {
        FrameTiming.Reset();
        Assert.Equal(1, FrameTiming.RenderAlpha);
        long generation = FrameTiming.Discontinuities;
        Assert.Equal(0, FrameTiming.Advance(FrameTiming.StepSeconds / 2));
        Assert.Equal(.5f, FrameTiming.RenderAlpha, 5);
        Assert.Equal(1, FrameTiming.Advance(1));
        Assert.True(FrameTiming.Discontinuities > generation);
        generation = FrameTiming.Discontinuities;
        Assert.Equal(5, FrameTiming.Advance(.2));
        Assert.True(FrameTiming.Discontinuities > generation);
        Assert.InRange(FrameTiming.RenderAlpha, 0, 1);
        FrameTiming.Reset();
    }

    [Fact]
    public void FocusResetStaleAndNonfiniteDeltasDoNotReplay()
    {
        var input = new RenderLookAccumulator();
        input.Add(4, 8, 1);
        input.Add(float.NaN, 1, 1);
        Assert.Equal(new Vector2(4, 8), input.Peek(1));
        input.Reset();
        Assert.Equal(Vector2.Zero, input.Consume(1));
        input.Add(4, 8, 2);
        Assert.Equal(Vector2.Zero, input.Peek(2.3));
        input.Add(3, 5, 3);
        Assert.Equal(new Vector2(3, 5), input.Consume(3));
        input.Add(7, 9, 3.01);
        Assert.Equal(new Vector2(7, 9), input.Consume(3.01));
    }
}
