using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;

namespace MphRead.Mods.Accounts;

public enum CareerOutcome
{
    FinishedWin,
    FinishedLoss,
    Tie,
    Forfeit,
    DepartedGraceExpired,
    NoContest
}

public sealed record CareerTotals(
    [property: JsonRequired] long Matches,
    [property: JsonRequired] long Wins,
    [property: JsonRequired] long Ties,
    [property: JsonRequired] long PlayedTicks,
    [property: JsonRequired] long Kills,
    [property: JsonRequired] long Deaths,
    [property: JsonRequired] long Assists,
    [property: JsonRequired] long Damage,
    [property: JsonRequired] long? Losses,
    [property: JsonRequired] long HeadshotKills,
    [property: JsonRequired] long? BipedKills,
    [property: JsonRequired] long? AltFormKills,
    [property: JsonRequired] long LongestKillStreak,
    [property: JsonRequired] long LongestWinStreak,
    [property: JsonRequired] decimal? KillDeathRatio,
    [property: JsonRequired] decimal? WinRatio)
{
    public long LossCount => Losses
        ?? throw new InvalidOperationException("The backend omitted the career loss count.");
}

public sealed record CareerChoice(
    [property: JsonRequired] string Key,
    [property: JsonRequired] long Samples,
    [property: JsonRequired] long Value,
    long? MatchesUsed = null);

public sealed record CareerRatingSummary(
    [property: JsonRequired] int Points,
    [property: JsonRequired] int Tier,
    [property: JsonRequired] string Title,
    [property: JsonRequired] int? NextThreshold,
    [property: JsonRequired] int? LastOfficialDelta,
    [property: JsonRequired] string Policy);

public sealed record CareerSummary(
    [property: JsonRequired] string Scope,
    [property: JsonRequired] MatchTrustClass? TrustClass,
    [property: JsonRequired] CareerTotals Totals,
    CareerChoice? MostPlayedHunter,
    CareerChoice? FavoriteMap,
    CareerChoice? FavoriteMode,
    CareerChoice? FavoriteWeapon,
    CareerChoice? BestMap,
    CareerChoice? BestHunter,
    [property: JsonRequired] int BestMinimumMatches,
    [property: JsonRequired] string RatingStatus,
    [property: JsonRequired] CareerRatingSummary Rating);

public sealed record MatchHistoryEntry(
    [property: JsonRequired] Guid MatchId,
    [property: JsonRequired] long ProcessingOrder,
    [property: JsonRequired] DateTimeOffset EndedAt,
    [property: JsonRequired] string RoomKey,
    [property: JsonRequired] MatchMode Mode,
    [property: JsonRequired] MatchTrustClass TrustClass,
    [property: JsonRequired] bool Eligible,
    [property: JsonRequired] bool Won,
    [property: JsonRequired] bool Tied,
    [property: JsonRequired] CareerOutcome Outcome,
    [property: JsonRequired] long PlayedTicks,
    [property: JsonRequired] long Kills,
    [property: JsonRequired] long Deaths,
    [property: JsonRequired] long Assists,
    [property: JsonRequired] long Damage,
    [property: JsonRequired] string RatingStatus);

public sealed record MatchHistoryPage(
    [property: JsonRequired] ImmutableArray<MatchHistoryEntry> Entries,
    [property: JsonRequired] long? NextCursor);

public sealed record LeaderboardEntry(
    [property: JsonRequired] PlayerId PlayerId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] long Kills,
    [property: JsonRequired] long Deaths,
    [property: JsonRequired] long Wins,
    [property: JsonRequired] long Matches,
    [property: JsonRequired] long AttributedMatches,
    [property: JsonRequired] decimal Score,
    int? Points = null,
    int? Tier = null,
    string? Title = null);

public sealed record LeaderboardPage(
    [property: JsonRequired] string Metric,
    [property: JsonRequired] string Scope,
    [property: JsonRequired] MatchTrustClass? TrustClass,
    [property: JsonRequired] ImmutableArray<LeaderboardEntry> Entries,
    [property: JsonRequired] string? NextCursor,
    Hunter? Hunter = null,
    string? RatingStatus = null,
    string? Policy = null);

public sealed partial class AccountSession
{
    private const string RatingPolicy = "PairwiseNormalizedV1";

    public async Task<CareerSummary> GetCareerAsync(PlayerId player, CancellationToken cancel = default)
    {
        if (player.IsEmpty) throw new ArgumentException("A player identity is required.", nameof(player));
        CareerSummary career = await SendAsync<CareerSummary>(HttpMethod.Get,
            $"v1/players/{player}/career", null, null, cancel).ConfigureAwait(false);
        ValidateCareer(career);
        return career;
    }

    public async Task<MatchHistoryPage> GetHistoryAsync(PlayerId player, long? before = null,
        CancellationToken cancel = default)
    {
        if (player.IsEmpty || before is <= 0) throw new ArgumentException("Invalid history cursor or player.");
        string query = before.HasValue ? "&before=" + before.Value.ToString(CultureInfo.InvariantCulture) : "";
        MatchHistoryPage page = await SendAsync<MatchHistoryPage>(HttpMethod.Get,
            $"v1/players/{player}/matches?limit=25{query}", null, null, cancel).ConfigureAwait(false);
        if (page.Entries.IsDefault || page.Entries.Length > 25 || page.NextCursor is <= 0
            || before.HasValue && page.NextCursor >= before || page.NextCursor.HasValue && page.Entries.IsEmpty)
            throw InvalidHistory();
        long? previous = before;
        foreach (MatchHistoryEntry entry in page.Entries)
        {
            if (entry.ProcessingOrder <= 0 || previous.HasValue && entry.ProcessingOrder >= previous.Value
                || !ValidHistoryEntry(entry)) throw InvalidHistory();
            previous = entry.ProcessingOrder;
        }
        if (page.NextCursor.HasValue && page.NextCursor.Value != page.Entries[^1].ProcessingOrder)
            throw InvalidHistory();
        return page;
    }

    public async Task<LeaderboardPage> GetLeaderboardAsync(string metric, string? cursor = null,
        CancellationToken cancel = default, Hunter? hunter = null)
    {
        if (!ValidMetric(metric) || cursor != null && (cursor.Length == 0 || cursor.Length > 256)
            || hunter.HasValue && !PlayableHunterCatalog.IsPlayable(hunter.Value)
                || metric == "rp" && hunter.HasValue)
            throw new ArgumentException("Invalid leaderboard category or cursor.");
        string query = cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor);
        if (hunter.HasValue) query += "&hunter=" + ((int)hunter.Value).ToString(CultureInfo.InvariantCulture);
        LeaderboardPage page = await SendAsync<LeaderboardPage>(HttpMethod.Get,
            $"v1/leaderboards/career?metric={metric}&limit=25{query}", null, null, cancel).ConfigureAwait(false);
        if (page.Metric != metric || page.Scope != "official" || page.TrustClass != null
            || page.Hunter != hunter || page.Entries.IsDefault || page.Entries.Length > 25
            || page.NextCursor is "" || page.NextCursor?.Length > 256 || cursor != null && page.NextCursor == cursor
            || page.NextCursor != null && page.Entries.IsEmpty
            || metric == "rp" && (page.RatingStatus != "active" || page.Policy != RatingPolicy)
            || metric != "rp" && (page.RatingStatus != null || page.Policy != null))
            throw InvalidLeaderboard();

        LeaderboardEntry? previous = null;
        foreach (LeaderboardEntry entry in page.Entries)
        {
            if (!ValidLeaderboardEntry(entry, metric)
                || previous != null && (entry.Score > previous.Score
                    || entry.Score == previous.Score && entry.PlayerId.Value.CompareTo(previous.PlayerId.Value) <= 0))
                throw InvalidLeaderboard();
            previous = entry;
        }
        return page;
    }

    private static void ValidateCareer(CareerSummary career)
    {
        CareerTotals totals = career.Totals;
        if (career.Scope != "official" || career.TrustClass != null || career.RatingStatus != "active"
            || career.BestMinimumMatches is < 1 or > 1000 || !ValidTotals(totals)
            || !ValidChoice(career.MostPlayedHunter, "hunter", requireOutcomeSample: false)
            || !ValidChoice(career.FavoriteMap, "map", requireOutcomeSample: false)
            || !ValidChoice(career.FavoriteMode, "mode", requireOutcomeSample: false)
            || !ValidChoice(career.FavoriteWeapon, "weapon", requireOutcomeSample: false)
            || !ValidChoice(career.BestMap, "map", requireOutcomeSample: true, career.BestMinimumMatches)
            || !ValidChoice(career.BestHunter, "hunter", requireOutcomeSample: true, career.BestMinimumMatches)
            || !ValidRating(career.Rating))
            throw new InvalidOperationException("The backend returned an invalid career summary.");
    }

    private static bool ValidTotals(CareerTotals totals)
    {
        if (totals == null || totals.Losses is not { } losses
            || totals.Matches < 0 || totals.Wins < 0 || totals.Ties < 0 || losses < 0
            || totals.Wins > totals.Matches || totals.Ties > totals.Matches - totals.Wins
            || losses != totals.Matches - totals.Wins - totals.Ties
            || totals.PlayedTicks < 0 || totals.Kills < 0 || totals.Deaths < 0 || totals.Assists < 0
            || totals.Damage < 0 || totals.HeadshotKills < 0 || totals.HeadshotKills > totals.Kills
            || totals.BipedKills < 0 || totals.AltFormKills < 0
            || totals.BipedKills is { } biped && totals.AltFormKills is { } alt
                && (biped > totals.Kills || alt > totals.Kills - biped)
            || totals.LongestKillStreak < 0 || totals.LongestWinStreak < 0
            || totals.LongestWinStreak > totals.Wins || totals.KillDeathRatio < 0
            || totals.WinRatio is < 0 or > 1) return false;
        bool validKillDeath = totals.Deaths == 0 ? totals.KillDeathRatio == null : totals.KillDeathRatio.HasValue;
        bool validWinRatio = totals.Matches == 0 ? totals.WinRatio == null : totals.WinRatio.HasValue;
        return validKillDeath && validWinRatio;
    }

    private static bool ValidChoice(CareerChoice? choice, string dimension, bool requireOutcomeSample,
        int minimumSamples = 0)
    {
        if (choice == null) return true;
        if (!ValidKey(choice.Key, 128) || choice.Samples <= 0 || choice.Value < 0
            || !requireOutcomeSample && choice.Value == 0
            || requireOutcomeSample && (choice.Samples < minimumSamples || choice.Value > choice.Samples)) return false;
        if (dimension == "hunter" && (!int.TryParse(choice.Key, NumberStyles.None,
                CultureInfo.InvariantCulture, out int hunter)
                || !PlayableHunterCatalog.IsPlayable((Hunter)hunter))) return false;
        if (dimension == "mode" && (!int.TryParse(choice.Key, NumberStyles.None,
                CultureInfo.InvariantCulture, out int mode) || mode is < byte.MinValue or > byte.MaxValue
                || !Enum.IsDefined((MatchMode)(byte)mode))) return false;
        if (dimension == "weapon")
            return int.TryParse(choice.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int weapon)
                && weapon is >= 0 and <= 8 && choice.Value > 0 && choice.MatchesUsed == choice.Samples;
        return choice.MatchesUsed == null;
    }

    private static bool ValidRating(CareerRatingSummary rating)
    {
        if (rating == null || rating.Points is < 0 or > 850 || rating.Tier is < 1 or > 5
            || rating.Policy != RatingPolicy || rating.LastOfficialDelta is < -850 or > 850) return false;
        (int minimum, int? next, string title) = rating.Tier switch
        {
            1 => (0, 40, "Bounty Hunter"),
            2 => (40, 140, "Super Hunter"),
            3 => (140, 390, "Elite Hunter"),
            4 => (390, 750, "Master Hunter"),
            5 => (750, (int?)null, "Legendary Hunter"),
            _ => throw new InvalidOperationException()
        };
        return rating.Points >= minimum && (!next.HasValue || rating.Points < next.Value)
            && rating.NextThreshold == next && rating.Title == title;
    }

    private static bool ValidHistoryEntry(MatchHistoryEntry entry)
    {
        if (entry.MatchId == Guid.Empty || entry.EndedAt.Offset != TimeSpan.Zero || !ValidKey(entry.RoomKey, 128)
            || !Enum.IsDefined(entry.Mode) || !Enum.IsDefined(entry.TrustClass) || !Enum.IsDefined(entry.Outcome)
            || entry.PlayedTicks < 0 || entry.Kills < 0 || entry.Deaths < 0 || entry.Assists < 0
            || entry.Damage < 0 || entry.RatingStatus is not ("applied" or "ineligible")) return false;
        return entry.Outcome switch
        {
            CareerOutcome.FinishedWin => entry.Eligible && entry.Won && !entry.Tied,
            CareerOutcome.FinishedLoss or CareerOutcome.Forfeit or CareerOutcome.DepartedGraceExpired
                => entry.Eligible && !entry.Won && !entry.Tied,
            CareerOutcome.Tie => entry.Eligible && !entry.Won && entry.Tied,
            CareerOutcome.NoContest => !entry.Eligible && !entry.Won && !entry.Tied,
            _ => false
        };
    }

    private static bool ValidLeaderboardEntry(LeaderboardEntry entry, string metric)
    {
        if (entry.PlayerId.IsEmpty || !ValidText(entry.DisplayName, 16) || entry.Kills < 0
            || entry.Deaths < 0 || entry.Wins < 0 || entry.Matches < 0
            || entry.AttributedMatches < 0 || entry.AttributedMatches > entry.Matches
            || entry.Wins > entry.AttributedMatches || entry.Score < 0) return false;
        if (metric == "rp")
            return entry.Points is { } points && entry.Tier is { } tier && entry.Title != null
                && entry.Score == points && ValidRating(new(points, tier, entry.Title,
                    tier == 5 ? null : new[] { 0, 40, 140, 390, 750 }[tier], null, RatingPolicy));
        if (entry.Points != null || entry.Tier != null || entry.Title != null) return false;
        return metric != "winPercentage" || entry.Score <= 1;
    }

    private static bool ValidMetric(string metric)
        => metric is "kills" or "wins" or "kd" or "rp" or "winPercentage" or "headshots"
            or "octolithScores" or "nodesCaptured" or "killsAsPrime";

    private static bool ValidText(string? value, int maximum)
        => value is { Length: > 0 } && value.Length <= maximum
            && value.All(character => character is >= ' ' and <= '~');

    private static bool ValidKey(string? value, int maximum)
        => value is { Length: > 0 } && value.Length <= maximum && !string.IsNullOrWhiteSpace(value);

    private static InvalidOperationException InvalidHistory()
        => new("The backend returned an invalid history page.");

    private static InvalidOperationException InvalidLeaderboard()
        => new("The backend returned an invalid leaderboard page.");
}
