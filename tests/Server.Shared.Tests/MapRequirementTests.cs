using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class MapRequirementTests
{
    [Fact]
    public void ExactMapRequirementValidatesAndUnrelatedMapsDoNotAffectCompatibility()
    {
        string requiredContent = new string('a', 64);
        string match = MapRequirement.ComputeMatchContentHash(new string('b', 64),
            "community.parallax", "1.4.0", requiredContent, "test-build", 8);
        var requirement = new MapRequirement("community.parallax", "1.4.0",
            requiredContent, new string('c', 64), 319_488, match);

        requirement.Validate();
        Assert.Equal(match, MapRequirement.ComputeMatchContentHash(new string('b', 64),
            requirement.StableId, requirement.Version, requirement.ContentHash, "test-build", 8));
    }

    [Fact]
    public void InvalidIdentityHashAndSizeFailBeforeAdmission()
    {
        Assert.ThrowsAny<ArgumentException>(() => new MapRequirement("Parallax", "1.0.0",
            new string('a', 64), new string('b', 64), 1, new string('c', 64)).Validate());
        Assert.ThrowsAny<ArgumentException>(() => new MapRequirement("community.parallax", "1.0.0",
            "wrong", new string('b', 64), 1, new string('c', 64)).Validate());
        Assert.ThrowsAny<ArgumentException>(() => new MapRequirement("community.parallax", "1.0.0",
            new string('a', 64), new string('b', 64), 0, new string('c', 64)).Validate());
    }

    [Fact]
    public void AuthoritativeHashExactlyMatchesRuntimeSnapshotFormula()
    {
        var identity = new MapContentIdentity(
            new MapIdentity("community.parallax", new MapVersion(1, 4, 0)),
            new string('a', 64));
        const string baseIdentity =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string gameplayIdentity =
            GameplayContentIdentity.Current("test-build", 8);

        string runtime = MatchContentHash.Compute(baseIdentity, identity,
            gameplayIdentity);
        string authoritative = MapRequirement.ComputeMatchContentHash(
            baseIdentity, identity.Identity.StableId,
            identity.Identity.Version.ToString(), identity.ContentHash,
            "test-build", 8);

        Assert.Equal(authoritative, runtime);
        var requirement = new MapRequirement(identity.Identity.StableId,
            identity.Identity.Version.ToString(), identity.ContentHash,
            new string('d', 64), 1, authoritative);
        Assert.Equal(identity,
            requirement.ToRoomContentRequirement().ContentIdentity);
    }
}
