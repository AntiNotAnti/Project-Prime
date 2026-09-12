using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

public sealed class HudMatchScoreTests
{
    [Fact]
    public void GoalScoreUsesTheCompactPointsFormat()
    {
        Assert.Equal("9 / 7", PlayerPresentation.FormatGoalScore(9, 7));
        Assert.NotEqual(PlayerPresentation.FormatGoalScore(8, 7),
            PlayerPresentation.FormatGoalScore(9, 7));
    }
}
