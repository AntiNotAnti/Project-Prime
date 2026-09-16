using System.Collections.Generic;
using MphRead.Mods.Render;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class ProHudWeaponSettingTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    public void EffectiveValueUsesOnlyTheApplicablePreference(bool proHud, bool ordinary,
        bool proHudPreference, bool expected)
    {
        Assert.Equal(expected, Features.ResolveFixedWeapon(proHud, proHudPreference, ordinary));
    }

    [Fact]
    public void ProHudDefaultRemainsStatic()
    {
        Assert.True(Features.ProHudFixedWeapon);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void WeaponRowVisibilityTracksProHud(bool proHud, bool expected)
    {
        Assert.Equal(expected, Features.ShowProHudWeaponSetting(proHud));
    }

    [Fact]
    public void SettingsMissingOrInvalidValuePreserveCurrentAndRoundTrip()
    {
        bool before = Features.ProHudFixedWeapon;
        try
        {
            Features.ProHudFixedWeapon = false;
            FeaturesSettings.Load(new Dictionary<string, string>());
            Assert.False(Features.ProHudFixedWeapon);
            FeaturesSettings.Load(new Dictionary<string, string>
            {
                [nameof(Features.ProHudFixedWeapon)] = "not-a-bool"
            });
            Assert.False(Features.ProHudFixedWeapon);

            var committed = FeaturesSettings.Commit();
            Features.ProHudFixedWeapon = true;
            FeaturesSettings.Load(committed);
            Assert.False(Features.ProHudFixedWeapon);
        }
        finally
        {
            Features.ProHudFixedWeapon = before;
        }
    }

    [Fact]
    public void AuthoredReticleScaleClampsAndRoundTrips()
    {
        float before = Features.ReticleScale;
        try
        {
            Features.ReticleScale = 1.35f;
            var committed = FeaturesSettings.Commit();
            Features.ReticleScale = 1;
            FeaturesSettings.Load(committed);
            Assert.Equal(1.35f, Features.ReticleScale);

            Features.ReticleScale = 99;
            Assert.Equal(Features.MaximumReticleScale, Features.ReticleScale);
            Features.ReticleScale = -99;
            Assert.Equal(Features.MinimumReticleScale, Features.ReticleScale);
            Features.ReticleScale = float.NaN;
            Assert.Equal(1, Features.ReticleScale);
        }
        finally
        {
            Features.ReticleScale = before;
        }
    }

    [Fact]
    public void CohesiveHudPreferencesRoundTripAndResolveScale()
    {
        ProHudSize beforeSize = Features.ProHudSize;
        bool beforeSafeArea = Features.ProHudSafeArea;
        bool beforeContrast = Features.ProHudHighContrast;
        try
        {
            Features.ProHudSize = ProHudSize.Large;
            Features.ProHudSafeArea = false;
            Features.ProHudHighContrast = true;
            var committed = FeaturesSettings.Commit();

            Features.ProHudSize = ProHudSize.Compact;
            Features.ProHudSafeArea = true;
            Features.ProHudHighContrast = false;
            FeaturesSettings.Load(committed);

            Assert.Equal(ProHudSize.Large, Features.ProHudSize);
            Assert.Equal(1.18f, Features.ProHudScale);
            Assert.False(Features.ProHudSafeArea);
            Assert.True(Features.ProHudHighContrast);
        }
        finally
        {
            Features.ProHudSize = beforeSize;
            Features.ProHudSafeArea = beforeSafeArea;
            Features.ProHudHighContrast = beforeContrast;
        }
    }

    [Fact]
    public void InvalidHudSizePreservesCurrentPreference()
    {
        ProHudSize before = Features.ProHudSize;
        try
        {
            Features.ProHudSize = ProHudSize.Standard;
            FeaturesSettings.Load(new Dictionary<string, string>
            {
                [nameof(Features.ProHudSize)] = "billboard"
            });
            Assert.Equal(ProHudSize.Standard, Features.ProHudSize);
        }
        finally
        {
            Features.ProHudSize = before;
        }
    }

    [Fact]
    public void ExpandedCrosshairStylesRoundTripThroughFeatureSettings()
    {
        CrosshairStyle before = Crosshair.Style;
        CrosshairStyle[] expanded =
        {
            CrosshairStyle.RingDot,
            CrosshairStyle.TDot,
            CrosshairStyle.Box,
            CrosshairStyle.Precision,
            CrosshairStyle.Shotgun
        };
        try
        {
            foreach (CrosshairStyle style in expanded)
            {
                Crosshair.Style = style;
                IReadOnlyDictionary<string, string> committed
                    = FeaturesSettings.Commit();
                Crosshair.Style = CrosshairStyle.Cross;
                FeaturesSettings.Load(committed);
                Assert.Equal(style, Crosshair.Style);
            }
        }
        finally
        {
            Crosshair.Style = before;
        }
    }
}
