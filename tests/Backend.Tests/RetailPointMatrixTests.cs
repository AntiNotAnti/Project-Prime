using MphRead.Backend.Rating;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class RetailPointMatrixTests
{
    private static readonly int[,] Gains =
    {
        { 2, 4, 7, 10, 15 },
        { 1, 3, 6, 10, 15 },
        { 1, 2, 4, 8, 12 },
        { 1, 1, 4, 5, 8 },
        { 1, 1, 2, 4, 6 }
    };

    private static readonly int[,] Losses =
    {
        { 1, 1, 1, 0, 0 },
        { 2, 3, 1, 0, 0 },
        { 4, 3, 4, 3, 2 },
        { 8, 8, 6, 5, 4 },
        { 15, 12, 10, 8, 6 }
    };

    public static IEnumerable<object[]> EveryGain()
    {
        for (int self = 1; self <= 5; self++)
            for (int opponent = 1; opponent <= 5; opponent++)
                yield return [self, opponent, Gains[self - 1, opponent - 1]];
    }

    [Theory]
    [MemberData(nameof(EveryGain))]
    public void ExactUsaRev1MatrixContainsEveryGain(int self, int opponent, int gain)
        => Assert.Equal(gain, RetailPointMatrix.Get(self, opponent).Gain);

    public static IEnumerable<object[]> EveryLoss()
    {
        for (int self = 1; self <= 5; self++)
            for (int opponent = 1; opponent <= 5; opponent++)
                yield return [self, opponent, Losses[self - 1, opponent - 1]];
    }

    [Theory]
    [MemberData(nameof(EveryLoss))]
    public void ExactUsaRev1MatrixContainsEveryLoss(int self, int opponent, int loss)
        => Assert.Equal(loss, RetailPointMatrix.Get(self, opponent).Loss);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(39, 1)]
    [InlineData(40, 2)]
    [InlineData(139, 2)]
    [InlineData(140, 3)]
    [InlineData(389, 3)]
    [InlineData(390, 4)]
    [InlineData(749, 4)]
    [InlineData(750, 5)]
    [InlineData(850, 5)]
    public void TierBoundariesIncludeBothSidesOfEveryThreshold(int points, int tier)
        => Assert.Equal(tier, RetailPointMatrix.TierForPoints(points));

    [Theory]
    [InlineData(-1)]
    [InlineData(851)]
    public void PointsOutsideTheLegalRangeAreRejected(int points)
        => Assert.Throws<ArgumentOutOfRangeException>(() => RetailPointMatrix.TierForPoints(points));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(6, 1)]
    [InlineData(1, 6)]
    public void MatrixRejectsInvalidTierIndexes(int self, int opponent)
        => Assert.Throws<ArgumentOutOfRangeException>(() => RetailPointMatrix.Get(self, opponent));
}
