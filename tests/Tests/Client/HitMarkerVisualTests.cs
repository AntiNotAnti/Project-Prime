using System.Collections.Generic;
using System.Linq;
using MphRead.Combat;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class HitMarkerVisualTests
{
    [Fact]
    public void VectorMarkerKeepsSubpixelPositionAndUsesDistinctShapes()
    {
        var geometry = new List<HudGeometryVertex>();
        var state = new CombatFeedbackState { MarkerHealth = 90 };

        HitMarkerVisual.Build(geometry, new Vector2(.501f, .503f),
            HitMarkerKind.Hit, 0, 12, 0, state, 1, reduceMotion: false);
        Assert.Equal(22, geometry.Count);
        Assert.Contains(geometry, vertex => vertex.Position.X % 1 != 0
            || vertex.Position.Y % 1 != 0);

        HitMarkerVisual.Build(geometry, new Vector2(.5f), HitMarkerKind.Headshot,
            5, 16, 1, state, 1, reduceMotion: false);
        Assert.Equal(46, geometry.Count);

        HitMarkerVisual.Build(geometry, new Vector2(.5f), HitMarkerKind.Kill,
            0, 18, 0, state, 1, reduceMotion: false);
        Assert.Equal(28, geometry.Count);
    }

    [Fact]
    public void DamageBurstAndReducedMotionProduceBoundedDeterministicGeometry()
    {
        var quiet = new List<HudGeometryVertex>();
        var strong = new List<HudGeometryVertex>();
        var quietState = new CombatFeedbackState { MarkerHealth = 90 };
        var strongState = new CombatFeedbackState
        {
            MarkerDamage = 100,
            MarkerHealth = 10,
            MarkerFlags = CombatEventFlags.Charged,
            MarkerBurst = 8
        };

        HitMarkerVisual.Build(quiet, new Vector2(.5f), HitMarkerKind.Hit,
            1, 12, 1, quietState, 1, reduceMotion: true);
        HitMarkerVisual.Build(strong, new Vector2(.5f), HitMarkerKind.Hit,
            1, 12, 1, strongState, 1, reduceMotion: true);
        Assert.Equal(quiet.Count, strong.Count);
        float quietWidth = quiet.Max(v => v.Position.X) - quiet.Min(v => v.Position.X);
        float strongWidth = strong.Max(v => v.Position.X) - strong.Min(v => v.Position.X);
        Assert.True(strongWidth > quietWidth);
        Assert.All(strong, vertex =>
        {
            Assert.True(float.IsFinite(vertex.Position.X));
            Assert.True(float.IsFinite(vertex.Position.Y));
            Assert.InRange(vertex.Color.W, 0, 1);
        });
    }

    [Theory]
    [InlineData(HitMarkerPalette.Classic)]
    [InlineData(HitMarkerPalette.HighContrast)]
    [InlineData(HitMarkerPalette.Colorblind)]
    [InlineData(HitMarkerPalette.Monochrome)]
    public void EveryPaletteDistinguishesConfirmedFromKill(HitMarkerPalette palette)
    {
        Vector4 hit = HitMarkerVisual.Color(HitMarkerKind.Hit, palette, .8f);
        Vector4 kill = HitMarkerVisual.Color(HitMarkerKind.Kill, palette, .8f);
        if (palette != HitMarkerPalette.Monochrome)
            Assert.NotEqual(hit.Xyz, kill.Xyz);
        Assert.Equal(.8f, hit.W, 3);
    }

    [Fact]
    public void ConfirmationHapticsAreShortAndEscalateByOutcome()
    {
        HapticPattern hit = GamepadHaptics.Pattern(HapticEvent.HitConfirm);
        HapticPattern headshot = GamepadHaptics.Pattern(HapticEvent.HeadshotConfirm);
        HapticPattern kill = GamepadHaptics.Pattern(HapticEvent.KillConfirm);
        Assert.InRange(hit.DurationMilliseconds, 1u, 50u);
        Assert.True(headshot.Priority > hit.Priority);
        Assert.True(kill.Priority > headshot.Priority);
        Assert.True(kill.HighFrequency > hit.HighFrequency);
    }
}
