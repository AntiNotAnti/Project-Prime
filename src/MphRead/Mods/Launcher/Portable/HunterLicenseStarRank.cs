using System;

namespace MphRead.Mods.Launcher;

/// <summary>Presentation of the existing server's MPH rating policy; never awards points.</summary>
public readonly struct HunterLicenseStarRank
{
    private HunterLicenseStarRank(int points, int tier) => (Points, Tier) = (points, tier);
    public int Points { get; }
    public int Tier { get; }
    public string Title => TitleFor(Tier);
    public string Model => Tier switch
    {
        1 => "redstar", 2 => "bronzestar", 3 => "silverstar", 4 => "goldstar", 5 => "blackstar", _ => ""
    };
    public int? NextRankPoints => Tier switch { 1 => 40, 2 => 140, 3 => 390, 4 => 750, _ => null };
    public int? PointsToNextRank => NextRankPoints is int next ? next - Points : null;
    public string NextTitle => Tier is >= 1 and < 5 ? TitleFor(Tier + 1) : "";
    public int ProgressPercent => NextRankPoints is int next
        ? (Points - MinimumPoints(Tier)) * 100 / (next - MinimumPoints(Tier)) : Tier == 5 ? 100 : 0;

    public static bool TryCreate(int points, int? tier, out HunterLicenseStarRank rank)
    {
        rank = default;
        if (points is < 0 or > 850 || tier is not (>= 1 and <= 5)) return false;
        int expected = points >= 750 ? 5 : points >= 390 ? 4 : points >= 140 ? 3 : points >= 40 ? 2 : 1;
        if (tier != expected) return false;
        rank = new(points, tier.Value);
        return true;
    }

    private static int MinimumPoints(int tier) => tier switch { 2 => 40, 3 => 140, 4 => 390, 5 => 750, _ => 0 };
    private static string TitleFor(int tier) => tier switch
    {
        1 => "BOUNTY HUNTER", 2 => "SUPER HUNTER", 3 => "ELITE HUNTER",
        4 => "MASTER HUNTER", 5 => "LEGENDARY HUNTER", _ => ""
    };
}
