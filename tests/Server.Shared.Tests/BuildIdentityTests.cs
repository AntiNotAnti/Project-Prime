using FruityPrime.Server.Shared;
using Xunit;

namespace FruityPrime.Server.Shared.Tests;

public sealed class BuildIdentityTests
{
    [Theory]
    [InlineData(null, "a local build")]
    [InlineData("", "a local build")]
    [InlineData("1.0.0", "a local build")]
    [InlineData("1.0.0+7a59474d0bc331b7309a8543bee193cf11b2ac0f", "a local build")]
    [InlineData("v2.4.6", "v2.4.6")]
    [InlineData("2.4.6+commit", "v2.4.6")]
    [InlineData("v2.4.6+commit", "v2.4.6")]
    [InlineData("2.4.6-rc1", "a local build")]
    public void DisplayForUsesOneStableCompatibilityFormat(string? informationalVersion, string expected)
        => Assert.Equal(expected, BuildIdentity.DisplayFor(informationalVersion));

    [Fact]
    public void ParseNormalizesMissingBuildComponent()
        => Assert.Equal(new Version(2, 4, 0), BuildIdentity.Parse("2.4"));
}
