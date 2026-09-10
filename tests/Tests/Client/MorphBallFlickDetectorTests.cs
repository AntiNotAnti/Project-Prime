using System;
using System.Linq;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Settings;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class MorphBallFlickDetectorTests
{
    [Fact]
    public void StickRequiresNeutralActivationWindowAndReleaseRearm()
    {
        var detector = new MorphBallStickFlickDetector();

        Assert.False(detector.ObserveFixedTick(new Vector2(0.8f, 0), 0));
        Assert.False(detector.ObserveFixedTick(Vector2.Zero, .01));
        Assert.True(detector.ObserveFixedTick(new Vector2(.8f, .2f), .14));
        Assert.True(detector.TakeDirection(out Vector2 direction));
        Assert.True(direction.X > 0);
        Assert.True(direction.Y < 0);
        Assert.False(detector.ObserveFixedTick(new Vector2(.9f, 0), .15));

        Assert.False(detector.ObserveFixedTick(new Vector2(.34f, 0), .2));
        Assert.False(detector.ObserveFixedTick(Vector2.Zero, .21));
        Assert.True(detector.ObserveFixedTick(new Vector2(-.8f, 0), .30));
        Assert.True(detector.TakeDirection(out direction));
        Assert.True(direction.X < 0);
    }

    [Fact]
    public void StickRejectsLateAndNonMonotonicSamplesAndInvertsUpPositiveY()
    {
        var detector = new MorphBallStickFlickDetector();
        detector.ObserveFixedTick(Vector2.Zero, 1);
        Assert.False(detector.ObserveFixedTick(new Vector2(0, .8f), 1.141));
        detector.ObserveFixedTick(Vector2.Zero, 2);
        Assert.False(detector.ObserveFixedTick(new Vector2(0, .8f), 1.9));
        Assert.False(detector.ObserveFixedTick(new Vector2(0, .8f), 2.141));

        detector.Reset();
        detector.ObserveFixedTick(Vector2.Zero, 3);
        Assert.True(detector.ObserveFixedTick(new Vector2(0, .8f), 3.10));
        Assert.True(detector.TakeDirection(out Vector2 direction));
        Assert.Equal(0, direction.X, 3);
        Assert.Equal(-1, direction.Y, 3);
    }

    [Fact]
    public void MouseUsesRawRollingWindowAndQuietRearmAtHighRate()
    {
        var detector = new MorphBallMouseFlickDetector(distance: 50);
        bool fired = false;
        for (int i = 0; i < 30; i++)
        {
            fired |= detector.Observe(new Vector2(2, 0), i / 240.0);
        }

        Assert.True(fired);
        Assert.True(detector.TakeDirection(out Vector2 direction));
        Assert.Equal(Vector2.UnitX, direction);
        Assert.False(detector.Observe(new Vector2(60, 0), .130));

        // A quiet interval rearms the detector without depending on sample
        // count, then the next raw gesture can fire once again.
        detector.Observe(Vector2.Zero, .300);
        Assert.True(detector.Observe(new Vector2(50, 0), .301));
        Assert.True(detector.TakeDirection(out _));
    }

    [Fact]
    public void PointerFlickHandlesHighRateHistoryAndOutOfOrderEvents()
    {
        var detector = new MorphBallPointerFlickDetector();
        detector.Begin(0, 0, 0);
        bool fired = false;
        for (int i = 1; i <= 60; i++)
        {
            fired |= detector.Move(i, 0, i * 2, out _);
        }

        Assert.True(fired);
        Assert.True(detector.TakeDirection(out Vector2 direction));
        Assert.Equal(Vector2.UnitX, direction);
        Assert.False(detector.Move(100, 0, 200, out _));

        detector.End();
        detector.Begin(0, 0, 100);
        Assert.False(detector.Move(40, 0, 110, out _));
        Assert.False(detector.Move(51, 0, 105, out _));
    }

    [Fact]
    public void RawMouseMetadataIsPreservedAndConsumedOnce()
    {
        var coordinator = new LookInputCoordinator(() => 1);
        Assert.True(coordinator.Submit(new LocalLookFrame(LookDeviceKind.Mouse,
            new Vector2(2, -1), new Vector2(17, -9), 19.2f), 1));

        LocalLookFrame preview = coordinator.PeekForRender(1.01);
        Assert.Equal(new Vector2(17, -9), preview.RawMouseDelta);
        LocalLookFrame consumed = coordinator.ConsumeForSimulation(1.02);
        Assert.Equal(new Vector2(17, -9), consumed.RawMouseDelta);
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1.03).RawMouseDelta);
    }

    [Fact]
    public void MorphBallTogglesPersistAndResetToEnabled()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.LoadLines(new[]
            {
                "morph_ball_mouse_flick_boost=false",
                "morph_ball_stick_flick_boost=false",
                "morph_ball_swipe_boost=false"
            });

            Assert.False(InputSettings.MorphBallMouseFlickBoost);
            Assert.False(InputSettings.MorphBallStickFlickBoost);
            Assert.False(InputSettings.MorphBallSwipeBoost);
            var saved = InputSettings.GetSaveLines();
            Assert.Contains("morph_ball_mouse_flick_boost=false", saved);
            Assert.Contains("morph_ball_stick_flick_boost=false", saved);
            Assert.Contains("morph_ball_swipe_boost=false", saved);
        }
        finally
        {
            InputSettings.Reset();
        }

        Assert.True(InputSettings.MorphBallMouseFlickBoost);
        Assert.True(InputSettings.MorphBallStickFlickBoost);
        Assert.True(InputSettings.MorphBallSwipeBoost);
    }

    [Fact]
    public void MorphBallSettingsHaveStableRegistryOwnership()
    {
        string[] ids =
        {
            SettingRowIds.MorphBallMouseFlickBoost,
            SettingRowIds.MorphBallStickFlickBoost,
            SettingRowIds.MorphBallSwipeBoost
        };

        Assert.All(ids, id =>
        {
            Assert.Contains(id, SettingRowIds.Fixed);
            Assert.Equal(SettingControlKind.Toggle, SettingRegistry.Get(id).Kind);
        });
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
