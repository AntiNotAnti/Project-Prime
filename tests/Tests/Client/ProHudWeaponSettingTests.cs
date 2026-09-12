using System.Collections.Generic;
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
}
