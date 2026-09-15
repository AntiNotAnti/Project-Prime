using System;
using MphRead;
using MphRead.Entities;
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
    public void CameraFovHistoryKeepsZoomAndRapidReversalContinuous()
    {
        var history = new ScalarPoseHistory();
        history.Capture(78, 1, 0);
        history.Capture(74, 2, 0);

        Assert.Equal(78, history.Resolve(0));
        Assert.Equal(76, history.Resolve(.5f));
        Assert.Equal(74, history.Resolve(1));

        // A rapid zoom reversal is another continuous FOV interval, not a
        // camera discontinuity. The presentation must begin at the completed
        // zoom-in sample and interpolate back toward the new target.
        history.Capture(78, 3, 0);
        Assert.Equal(74, history.Resolve(0));
        Assert.Equal(76, history.Resolve(.5f));
        Assert.Equal(78, history.Resolve(1));
    }

    [Fact]
    public void CameraLocalAimHistoryInterpolatesAndResetsAtDiscontinuity()
    {
        var history = new Vector3PoseHistory();
        history.Capture(new Vector3(1, 2, 3), 1, 0);
        history.Capture(new Vector3(3, 4, 5), 2, 0);
        Assert.Equal(new Vector3(2, 3, 4), history.Resolve(.5f));

        history.Capture(new Vector3(20, 30, 40), 3, 0, discontinuity: true);
        Assert.Equal(new Vector3(20, 30, 40), history.Resolve(0));
    }

    [Theory]
    [InlineData(3ul, 0L, false)] // missed completed step
    [InlineData(2ul, 1L, false)] // lifecycle epoch
    [InlineData(2ul, 0L, true)] // explicit teleport/form/pool barrier
    public void CameraLocalAimHistorySeedsCurrentAtTickBoundaries(ulong tick,
        long epoch, bool discontinuity)
    {
        var history = new Vector3PoseHistory();
        history.Capture(Vector3.Zero, 1, 0);
        Vector3 current = new(20, 30, 40);
        history.Capture(current, tick, epoch, discontinuity);

        Assert.Equal(current, history.Resolve(0));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void CameraLocalAimHistoryClampsNonFiniteAlpha(float alpha)
    {
        var history = new Vector3PoseHistory();
        history.Capture(Vector3.Zero, 1, 0);
        history.Capture(Vector3.UnitX, 2, 0);

        Vector3 resolved = history.Resolve(alpha);
        Assert.True(VectorMath.IsFinite(resolved));
        Assert.Equal(float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1,
            resolved.X, 5);
    }

    [Fact]
    public void CameraLocalAimHistoryRejectsNonFiniteSamplesAndHoldsLastValidPoint()
    {
        var history = new Vector3PoseHistory();
        Vector3 valid = new(4, 5, 6);
        history.Capture(valid, 1, 0);
        history.Capture(new Vector3(float.NaN, 0, 0), 2, 0);

        Assert.False(history.HasSamples);
        Assert.Equal(valid, history.Resolve(.5f));
    }

    [Fact]
    public void CameraLocalAimRemainsStationaryThroughCameraRotationAndTranslation()
    {
        Vector3 localAim = new(.8f, -.35f, -12);
        Matrix4 previousCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(-18))
            * Matrix4.CreateTranslation(4, 2, -7);
        Matrix4 currentCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(24))
            * Matrix4.CreateTranslation(9, 3, -2);
        Vector3 previousWorld = Matrix.Vec3MultMtx4(localAim, previousCamera);
        Vector3 currentWorld = Matrix.Vec3MultMtx4(localAim, currentCamera);

        var history = new Vector3PoseHistory();
        history.Capture(ScenePresentation.CameraLocalAimPosition(previousWorld,
            previousCamera.Inverted()), 1, 0);
        history.Capture(ScenePresentation.CameraLocalAimPosition(currentWorld,
            currentCamera.Inverted()), 2, 0);
        Vector3 resolvedLocal = history.Resolve(.5f);
        Matrix4 renderCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(3))
            * Matrix4.CreateTranslation(6.5f, 2.5f, -4.5f);
        Vector3 presented = ScenePresentation.ResolvePresentedAimPosition(
            currentWorld, resolvedLocal, renderCamera, useHistory: true);
        Matrix4 renderView = renderCamera.Inverted();
        Matrix.ProjectPosition(presented, renderView,
            Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(52),
                4 / 3f, .1f, 1000), out Vector2 projected);
        Matrix.ProjectPosition(Matrix.Vec3MultMtx4(localAim, renderCamera),
            renderView,
            Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(52),
                4 / 3f, .1f, 1000), out Vector2 expected);

        Assert.True((localAim - resolvedLocal).Length < .00001f);
        Assert.True((expected - projected).Length < .00001f);
    }

    [Fact]
    public void CameraLocalAimPreservesDynamicOffsetDepthAndEndpoints()
    {
        Matrix4 camera = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(27))
            * Matrix4.CreateTranslation(-3, 1, 8);
        Vector3 first = new(-1.2f, .4f, -5);
        Vector3 second = new(2.7f, -.9f, -19);
        var history = new Vector3PoseHistory();
        history.Capture(first, 1, 0);
        history.Capture(second, 2, 0);

        Vector3 atStart = ScenePresentation.ResolvePresentedAimPosition(
            Vector3.Zero, history.Resolve(0), camera, useHistory: true);
        Vector3 atEnd = ScenePresentation.ResolvePresentedAimPosition(
            Vector3.Zero, history.Resolve(1), camera, useHistory: true);
        Vector3 expectedStart = Matrix.Vec3MultMtx4(first, camera);
        Vector3 expectedEnd = Matrix.Vec3MultMtx4(second, camera);

        Assert.Equal(expectedStart, atStart);
        Assert.Equal(expectedEnd, atEnd);
        Assert.Equal(first.Z, history.Resolve(0).Z);
        Assert.Equal(second.Z, history.Resolve(1).Z);
    }

    [Fact]
    public void CameraLocalAimFallsBackForMissingOrInvalidHistory()
    {
        Vector3 fallback = new(3, 4, -8);
        Matrix4 camera = Matrix4.CreateTranslation(10, 2, -4);

        Assert.Equal(fallback, ScenePresentation.ResolvePresentedAimPosition(
            fallback, Vector3.Zero, camera, useHistory: false));
        Assert.Equal(fallback, ScenePresentation.ResolvePresentedAimPosition(
            fallback, new Vector3(float.NaN, 1, 2), camera, useHistory: true));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void PresentedAimProjectsOneCameraLocalTrajectorySample(float alpha)
    {
        Vector3 firstLocal = new(-1.2f, .4f, -7);
        Vector3 secondLocal = new(1.8f, -.6f, -15);
        Matrix4 firstCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(-14))
            * Matrix4.CreateTranslation(3, 1, -5);
        Matrix4 secondCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(18))
            * Matrix4.CreateTranslation(6, 2, -2);
        Vector3 firstWorld = Matrix.Vec3MultMtx4(firstLocal, firstCamera);
        Vector3 secondWorld = Matrix.Vec3MultMtx4(secondLocal, secondCamera);

        var aimHistory = new Vector3PoseHistory();
        aimHistory.Capture(ScenePresentation.CameraLocalAimPosition(firstWorld,
            firstCamera.Inverted()), 1, 0);
        aimHistory.Capture(ScenePresentation.CameraLocalAimPosition(secondWorld,
            secondCamera.Inverted()), 2, 0);
        var cameraHistory = new SimulationPoseHistory();
        cameraHistory.Capture(firstCamera, 1, 0);
        cameraHistory.Capture(secondCamera, 2, 0);

        Matrix4 renderCamera = cameraHistory.Resolve(alpha);
        Vector3 resolvedLocal = aimHistory.Resolve(alpha);
        Vector3 expectedLocal = Vector3.Lerp(firstLocal, secondLocal, alpha);
        Vector3 presentedAim = ScenePresentation.ResolvePresentedAimPosition(
            secondWorld, resolvedLocal, renderCamera, useHistory: true);
        Matrix4 renderView = renderCamera.Inverted();
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(52), 4 / 3f, .1f, 1000);
        float w = Matrix.ProjectPosition(presentedAim, renderView,
            projection, out Vector2 expected);

        Vector2 reticle = MphRead.Entities.PlayerPresentation
            .ResolveAuthoritativeReticlePosition(Vector2.Zero,
                presentedAim, renderView, projection);

        Assert.Equal(PlayerPresentation.NormalizeReticlePosition(w, expected),
            reticle);
        Assert.True((expectedLocal - resolvedLocal).Length < .00001f);
        Assert.True(VectorMath.IsFinite(presentedAim));
    }

    [Fact]
    public void PresentedAimKeepsLegacyResponseAndUsesCustomCameraHistory()
    {
        Vector3 fallback = new(2, -.5f, -12);
        Vector3 localAim = new(-.75f, .3f, -10);
        Matrix4 renderCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(21))
            * Matrix4.CreateTranslation(4, 1, -3);

        bool legacyResponse = DynamicCrosshairTuning.UsesLegacyCameraResponse(0, 1);
        bool customResponse = DynamicCrosshairTuning.UsesLegacyCameraResponse(15, 1);
        Vector3 legacyPresented = ScenePresentation.ResolvePresentedAimPosition(
            fallback, localAim, renderCamera,
            ScenePresentation.ShouldInterpolateCameraRotation(false,
                legacyResponse));
        Vector3 customPresented = ScenePresentation.ResolvePresentedAimPosition(
            fallback, localAim, renderCamera,
            ScenePresentation.ShouldInterpolateCameraRotation(false,
                customResponse));

        Assert.Equal(fallback, legacyPresented);
        Assert.Equal(Matrix.Vec3MultMtx4(localAim, renderCamera), customPresented);
    }

    [Fact]
    public void LegacyAimAttachesCurrentCompletedSampleWithoutHistory()
    {
        // Model the completed simulation camera and the final render camera
        // separately: the latter includes the presentation-time correction
        // and visual offset. Legacy first-person response must preserve the
        // current aim's local depth without consulting camera history.
        Vector3 currentAim = new(1.25f, -.4f, -14);
        Matrix4 simulationCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(-31))
            * Matrix4.CreateTranslation(-5, 2, 6);
        Matrix4 simulationView = simulationCamera.Inverted();
        Matrix4 renderCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(17))
            * Matrix4.CreateTranslation(8, -1, -3);

        Vector3 expectedLocal = ScenePresentation.CameraLocalAimPosition(
            currentAim, simulationView);
        Vector3 expected = Matrix.Vec3MultMtx4(expectedLocal, renderCamera);
        Vector3 presented = ScenePresentation.ResolveCurrentAimPosition(
            currentAim, simulationView, renderCamera);

        Assert.Equal(expected, presented);
        Vector3 recoveredAim = Matrix.Vec3MultMtx4(expectedLocal,
            simulationCamera);
        Assert.True((currentAim - recoveredAim).Length < .00001f);
        Assert.True(VectorMath.IsFinite(presented));
    }

    [Fact]
    public void OldIndependentWorldChordMovesWhileCameraLocalProjectionStaysStill()
    {
        Vector3 localAim = new(1.1f, -.25f, -10);
        Matrix4 firstCamera = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(-35))
            * Matrix4.CreateTranslation(-4, 0, -5);
        Matrix4 secondCamera = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(35))
            * Matrix4.CreateTranslation(7, 1, 3);
        Vector3 firstWorld = Matrix.Vec3MultMtx4(localAim, firstCamera);
        Vector3 secondWorld = Matrix.Vec3MultMtx4(localAim, secondCamera);
        var cameraHistory = new SimulationPoseHistory();
        cameraHistory.Capture(firstCamera, 1, 0);
        cameraHistory.Capture(secondCamera, 2, 0);
        const float alpha = .25f;
        Matrix4 renderCamera = cameraHistory.Resolve(alpha);
        Matrix4 renderView = renderCamera.Inverted();
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(35), 4 / 3f, .1f, 1000);

        Vector3 localPresented = ScenePresentation.ResolvePresentedAimPosition(
            secondWorld, localAim, renderCamera, useHistory: true);
        Matrix.ProjectPosition(localPresented, renderView, projection,
            out Vector2 localProjection);
        Vector3 worldChord = Vector3.Lerp(firstWorld, secondWorld, alpha);
        Matrix.ProjectPosition(worldChord, renderView, projection,
            out Vector2 worldProjection);

        Assert.True((localProjection - new Vector2(.5f, .5f)).Length < .2f);
        Assert.True((worldProjection - localProjection).Length > .01f);
    }

    [Fact]
    public void CustomAndLegacyCameraResponseGatesRemainUnchanged()
    {
        Assert.True(ScenePresentation.ShouldInterpolateCameraRotation(false, false));
        Assert.False(ScenePresentation.ShouldInterpolateCameraRotation(false, true));
        Assert.True(ScenePresentation.ShouldInterpolateCameraRotation(true, true));
        Assert.True(ScenePresentation.CameraHistoryState(false, false, false,
            false, false, CameraType.First, null)
            == ScenePresentation.CameraHistoryState(false, false, false,
                false, true, CameraType.First, null));
    }

    [Fact]
    public void CameraHistoryStateDoesNotChangeWhenOnlyZoomToggles()
    {
        int stateBeforeZoom = ScenePresentation.CameraHistoryState(
            dead: false, altForm: false, morphing: false, unmorphing: false,
            zoomed: false, cameraType: CameraType.First, currentSequence: null);
        int stateAfterZoom = ScenePresentation.CameraHistoryState(
            dead: false, altForm: false, morphing: false, unmorphing: false,
            zoomed: true, cameraType: CameraType.First, currentSequence: null);

        Assert.Equal(stateBeforeZoom, stateAfterZoom);
    }

    [Theory]
    [InlineData(EntityType.ItemInstance)]
    [InlineData(EntityType.FhItemInstance)]
    public void PickupModelsUseFullPoseInterpolation(EntityType type)
    {
        Assert.True(ScenePresentation.InterpolatesModelPose(type));
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
    [InlineData(60)]
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
    public void CustomDynamicCrosshairResponseDoesNotUseLegacyCameraOnlyPrediction()
    {
        Assert.True(DynamicCrosshairTuning.UsesLegacyCameraResponse(0, 1));
        Assert.False(DynamicCrosshairTuning.UsesLegacyCameraResponse(15, 1));
        Assert.False(DynamicCrosshairTuning.UsesLegacyCameraResponse(0, .5f));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void StableCameraHistoryExcludesMorphTransitions(
        bool alive, bool morphing, bool unmorphing, bool expected)
    {
        Assert.Equal(expected, ScenePresentation.IsStableCameraHistoryEligible(
            alive, morphing, unmorphing));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void AlternateFormAlwaysInterpolatesCameraRotation(
        bool altForm, bool legacyCameraResponse)
    {
        Assert.Equal(altForm || !legacyCameraResponse,
            ScenePresentation.ShouldInterpolateCameraRotation(
                altForm, legacyCameraResponse));
    }

    [Fact]
    public void CustomDynamicCrosshairInterpolatesCameraRotationInsteadOfStepping()
    {
        Matrix4 current = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(30))
            * Matrix4.CreateTranslation(8, 3, -4);
        Matrix4 interpolated = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(10))
            * Matrix4.CreateTranslation(7, 2.5f, -3);

        Matrix4 legacy = ScenePresentation.ResolveCameraPose(current, interpolated,
            interpolateRotation: false);
        Matrix4 custom = ScenePresentation.ResolveCameraPose(current, interpolated,
            interpolateRotation: true);

        Assert.Equal(current.Row0, legacy.Row0);
        Assert.Equal(current.Row1, legacy.Row1);
        Assert.Equal(current.Row2, legacy.Row2);
        Assert.Equal(interpolated.Row3, legacy.Row3);
        Assert.Equal(interpolated, custom);
    }

    [Fact]
    public void ViewmodelCameraLocalPoseFollowsResolvedCameraExactlyOnce()
    {
        Matrix4 simulationCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(20))
            * Matrix4.CreateTranslation(4, 2, -8);
        Matrix4 renderCamera = Matrix4.CreateRotationY(
                MathHelper.DegreesToRadians(24))
            * Matrix4.CreateTranslation(4.5f, 2.25f, -7.5f);
        Matrix4 authoredCameraPose = Matrix4.CreateRotationX(
                MathHelper.DegreesToRadians(-6))
            * Matrix4.CreateTranslation(0.35f, -0.2f, -0.75f);
        Matrix4 worldPose = authoredCameraPose * simulationCamera;

        Matrix4 cameraLocal = ScenePresentation.ViewmodelCameraLocalPose(
            worldPose, simulationCamera.Inverted());
        Matrix4 resolvedWorld = ScenePresentation.ViewmodelWorldPose(
            cameraLocal, renderCamera);
        Matrix4 displayed = resolvedWorld * renderCamera.Inverted();

        AssertMatrixNear(authoredCameraPose, cameraLocal);
        AssertMatrixNear(authoredCameraPose, displayed);
    }

    [Fact]
    public void ViewmodelEffectAnchorUsesResolvedGunPose()
    {
        Matrix4 gunRoot = Matrix4.CreateRotationX(
                MathHelper.DegreesToRadians(-12))
            * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(25))
            * Matrix4.CreateTranslation(8, 3, -4);
        const float muzzleOffset = 1.75f;

        ScenePresentation.ResolveViewmodelEffectAnchor(gunRoot, muzzleOffset,
            out Vector3 right, out Vector3 aim, out Vector3 position);

        AssertVectorNear(gunRoot.Row0.Xyz.Normalized(), right);
        AssertVectorNear(gunRoot.Row2.Xyz.Normalized(), aim);
        AssertVectorNear(Matrix.Vec3MultMtx4(
            new Vector3(0, 0, muzzleOffset), gunRoot), position);
    }

    [Fact]
    public void GuardianFirstPersonUsesGunlessFiniteAuthoritativeEffectAnchor()
    {
        ScenePresentation.ResolveGuardianFirstPersonEffectAnchor(
            renderCameraPosition: new Vector3(11, 2, -4),
            simulationCameraPosition: new Vector3(10, 2, -4),
            shotOrigin: new Vector3(10, 2, -5),
            shotDirection: new Vector3(0, 0, -2),
            out Vector3 right, out Vector3 aim, out Vector3 position);

        Assert.True(VectorMath.IsFinite(right));
        Assert.True(VectorMath.IsFinite(aim));
        Assert.True(VectorMath.IsFinite(position));
        Assert.Equal(1, right.Length, precision: 5);
        Assert.Equal(1, aim.Length, precision: 5);
        Assert.Equal(new Vector3(11, 2, -5), position);
        Assert.False(PlayerPresentation.ShouldSubmitFirstPersonGun(
            Hunter.Guardian, hideViewmodel: false));
        Assert.True(PlayerPresentation.ShouldSubmitFirstPersonGun(
            Hunter.Samus, hideViewmodel: false));
    }

    [Fact]
    public void ClockAlphaIsBoundedAndResetStallDebtHaveBarriers()
    {
        var timing = new FrameTiming();
        timing.Reset();
        Assert.Equal(1, timing.RenderAlpha);
        long generation = timing.Discontinuities;
        Assert.Equal(0, timing.Advance(FrameTiming.StepSeconds / 2));
        Assert.Equal(.5f, timing.RenderAlpha, 5);
        Assert.Equal(1, timing.Advance(1));
        Assert.True(timing.Discontinuities > generation);
        generation = timing.Discontinuities;
        Assert.Equal(5, timing.Advance(.2));
        Assert.True(timing.Discontinuities > generation);
        Assert.InRange(timing.RenderAlpha, 0, 1);
        timing.Reset();
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

    private static void AssertMatrixNear(Matrix4 expected, Matrix4 actual)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Assert.InRange(actual[row, column],
                    expected[row, column] - 0.00001f,
                    expected[row, column] + 0.00001f);
            }
        }
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(actual.X, expected.X - 0.00001f,
            expected.X + 0.00001f);
        Assert.InRange(actual.Y, expected.Y - 0.00001f,
            expected.Y + 0.00001f);
        Assert.InRange(actual.Z, expected.Z - 0.00001f,
            expected.Z + 0.00001f);
    }
}
