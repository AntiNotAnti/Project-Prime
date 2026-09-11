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
}
