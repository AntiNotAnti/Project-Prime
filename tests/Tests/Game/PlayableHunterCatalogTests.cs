using System;
using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

public sealed class PlayableHunterCatalogTests
{
    [Fact]
    public void OfficialCatalogIncludesGuardianButNotRandomSentinel()
    {
        Assert.Equal(8, PlayableHunterCatalog.Count);
        Assert.Equal(Hunter.Guardian, PlayableHunterCatalog.Last);
        Assert.Equal(Hunter.Guardian, PlayableHunterCatalog.FromIndex(7));
        Assert.True(PlayableHunterCatalog.IsPlayable(Hunter.Guardian));
        Assert.False(PlayableHunterCatalog.IsPlayable(Hunter.Random));
        Assert.True(PlayableHunterCatalog.IsSelectorValue(Hunter.Random));
        Assert.Equal(Hunter.Guardian,
            PlayableHunterCatalog.ResolveRandom(Hunter.Random, 7));
    }

    [Fact]
    public void GuardianUsesPowerBeamAndReplicatedAltAttack()
    {
        Assert.Equal(BeamType.PowerBeam,
            PlayableHunterCatalog.GetAffinityWeapon(Hunter.Guardian));
        Assert.True(PlayableHunterCatalog.Get(Hunter.Guardian)
            .SupportsReplicatedAltAttack);
        Assert.False(PlayerEntity.SupportsReplicatedAltAttack(Hunter.Noxus));
    }

    [Fact]
    public void CanonicalAndPsychoBitRecolorsHaveSeparatePolicies()
    {
        Assert.Equal(5, PlayableHunterCatalog.ValidateRecolor(
            Hunter.Guardian, 5));
        Assert.Equal(3, PlayableHunterCatalog.ResolveAltRecolor(
            Hunter.Guardian, 4));
        Assert.Equal(4, PlayableHunterCatalog.ResolveAltRecolor(
            Hunter.Guardian, 5));
        Assert.Equal(2, PlayableHunterCatalog.ResolveAltRecolor(
            Hunter.Guardian, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayableHunterCatalog.ResolveAltRecolor(Hunter.Guardian, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayableHunterCatalog.ValidateRecolor(Hunter.Random, 0));
    }
}
