using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

public sealed class HudMatchScoreTests
{
    [Fact]
    public void BattleScoreKeepsKillsAndAuthoritativeGoalScoreTogether()
    {
        Assert.Equal("K 4 · 9/7", PlayerPresentation.FormatBattleScore(4, 9, 7));
        Assert.NotEqual(PlayerPresentation.FormatBattleScore(3, 9, 7),
            PlayerPresentation.FormatBattleScore(4, 9, 7));
        Assert.NotEqual(PlayerPresentation.FormatBattleScore(4, 8, 7),
            PlayerPresentation.FormatBattleScore(4, 9, 7));
    }
}
