using System.Collections.Immutable;

namespace MphRead.Backend.Rating;

public readonly record struct RetailPointCell(int Gain, int Loss);

/// <summary>The exact USA Rev 1 five-tier Ranking Point table.</summary>
public static class RetailPointMatrix
{
    public const int MinimumPoints = 0;
    public const int MaximumPoints = 850;
    public const int TierCount = 5;

    public static ImmutableArray<int> TierMinimums { get; } = [0, 40, 140, 390, 750];

    private static readonly RetailPointCell[,] Cells =
    {
        { new(2, 1), new(4, 1), new(7, 1), new(10, 0), new(15, 0) },
        { new(1, 2), new(3, 3), new(6, 1), new(10, 0), new(15, 0) },
        { new(1, 4), new(2, 3), new(4, 4), new(8, 3), new(12, 2) },
        { new(1, 8), new(1, 8), new(4, 6), new(5, 5), new(8, 4) },
        { new(1, 15), new(1, 12), new(2, 10), new(4, 8), new(6, 6) }
    };

    public static RetailPointCell Get(int selfTier, int opponentTier)
    {
        if (selfTier is < 1 or > TierCount)
            throw new ArgumentOutOfRangeException(nameof(selfTier));
        if (opponentTier is < 1 or > TierCount)
            throw new ArgumentOutOfRangeException(nameof(opponentTier));
        return Cells[selfTier - 1, opponentTier - 1];
    }

    public static int TierForPoints(int points)
    {
        if (points is < MinimumPoints or > MaximumPoints)
            throw new ArgumentOutOfRangeException(nameof(points));
        if (points >= TierMinimums[4]) return 5;
        if (points >= TierMinimums[3]) return 4;
        if (points >= TierMinimums[2]) return 3;
        if (points >= TierMinimums[1]) return 2;
        return 1;
    }
}
