using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

public enum LicenseFace { Overview, Customization, Stats, History, Achievements, Emblems, Titles, Comparison, Account }
public enum LicenseAccountAction { SendVerification, FinishPassword, Recover, LinkGoogle, LinkGitHub, LinkDiscord }

public sealed record LicenseProfile(string PlayerId = "", string DisplayName = "Player", int FavoriteHunter = 0,
    DateTimeOffset? CreatedAt = null, int RatingPoints = 0, int? RatingTier = null);
public sealed record LicenseAccount(bool IsAnonymous = true, string Email = "", ImmutableArray<string> Providers = default)
{ public bool IsSecure => !IsAnonymous; }
public sealed record LicenseStats
{
    public long GamesPlayed { get; init; }
    public long Wins { get; init; }
    public long Ties { get; init; }
    public long Losses { get; init; }
    public long Kills { get; init; }
    public long Deaths { get; init; }
    public long Assists { get; init; }
    public long Damage { get; init; }
    public long PlayedTicks { get; init; }
    public long Headshots { get; init; }
    public long LongestKillStreak { get; init; }
    public long CurrentWinStreak { get; init; }
    public long LongestWinStreak { get; init; }
    public long OctolithScores { get; init; }
    public long NodesCaptured { get; init; }
    public long KillsAsPrime { get; init; }
}
public sealed record LicenseMatch(string MatchId, DateTimeOffset? PlayedAt, string RoomKey, int Mode,
    int TrustClass, bool CareerEligible, string RatingStatus, bool Eligible, bool Won, bool Tied,
    long PlayedTicks, long Kills, long Deaths, long Assists, long Damage);
public sealed record LicenseCosmetic(Hunter Hunter, string SkinKey, string ArmorEffectKey, string DeathEffectKey);
public sealed record LicenseData
{
    public bool Connected { get; init; }
    public string Status { get; init; } = "OFFLINE PROFILE";
    public LicenseProfile Profile { get; init; } = new();
    public LicenseAccount Account { get; init; } = new(Providers: ImmutableArray<string>.Empty);
    public LicenseStats Stats { get; init; } = new();
    public ImmutableArray<LicenseMatch> Matches { get; init; } = ImmutableArray<LicenseMatch>.Empty;
    public ImmutableArray<LicenseCosmetic> Cosmetics { get; init; } = ImmutableArray<LicenseCosmetic>.Empty;
}
public readonly record struct LicenseActionResult(bool Success, string Message);
public readonly record struct LicenseMetric(string Label, string Value);
public readonly record struct LicenseAchievement(string Name, string Detail, bool Earned);

/// <summary>No session tokens or credential values are exposed in presentation snapshots.</summary>
public sealed record LicenseViewSnapshot
{
    public Guid Lifetime { get; init; }
    public ulong Version { get; init; }
    public LicenseFace Face { get; init; }
    public LicenseData Data { get; init; } = new();
    public bool Loading { get; init; }
    public bool SecurityBusy { get; init; }
    public string SecurityMessage { get; init; } = "";
    public string CommandError { get; init; } = "";
    public bool HasPasswordInput { get; init; }
    public bool CanRecover { get; init; }
    public ulong CredentialsClearedEpoch { get; init; }
    public int HistoryPageIndex { get; init; }
    public int HistoryPageCount { get; init; } = 1;
    public ImmutableArray<LicenseMatch> History { get; init; } = ImmutableArray<LicenseMatch>.Empty;
    public ImmutableArray<LicenseMetric> CareerMetrics { get; init; } = ImmutableArray<LicenseMetric>.Empty;
    public ImmutableArray<LicenseMetric> Comparison { get; init; } = ImmutableArray<LicenseMetric>.Empty;
    public ImmutableArray<LicenseAchievement> Achievements { get; init; } = ImmutableArray<LicenseAchievement>.Empty;
    public string RecentForm { get; init; } = "";
    public string Rank { get; init; } = "UNRANKED";
    public bool EmblemsAvailable => false;
    public bool TitlesAvailable => false;
    public bool PublicComparisonAvailable => false;
    public string EmblemsUnavailableReason => "Emblem inventory has no catalog in the current identity service.";
    public string TitlesUnavailableReason => "Title inventory has no catalog in the current identity service.";
    public string PublicComparisonUnavailableReason => "Public Hunter ID lookup is unavailable until profile visibility rules are defined. Your per-match benchmark is available.";
}

public interface ILicenseBackend
{
    LicenseData LocalSnapshot();
    Task<LicenseData> LoadAsync(CancellationToken cancellationToken);
    Task<LicenseActionResult> AccountAsync(LicenseAccountAction action, string email, string password, CancellationToken cancellationToken);
}
