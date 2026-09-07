using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;

namespace MphRead.Mods.Accounts;

public sealed record CareerTotals(long Matches, long Wins, long Ties, long PlayedTicks, long Kills,
    long Deaths, long Assists, long Damage, long? HeadshotKills = null, long? BipedKills = null,
    long? AltFormKills = null, long? LongestKillStreak = null, long? LongestWinStreak = null,
    long? Losses = null, decimal? KillDeathRatio = null, decimal? WinRatio = null);
public sealed record CareerChoice(string Key, long Samples, long Value);
public sealed record CareerSummary(string Scope, MatchTrustClass? TrustClass, CareerTotals Totals,
    CareerChoice? MostPlayedHunter, CareerChoice? FavoriteMap, CareerChoice? FavoriteMode,
    CareerChoice? FavoriteWeapon, CareerChoice? BestMap, int BestMinimumMatches,
    string RatingStatus, CareerChoice? BestHunter = null);
public sealed record MatchHistoryEntry(Guid MatchId, long ProcessingOrder, DateTimeOffset EndedAt,
    string RoomKey, MatchMode Mode, MatchTrustClass TrustClass, bool Eligible, bool Won, bool Tied,
    ParticipantOutcome Outcome, long PlayedTicks, long Kills, long Deaths, long Assists, long Damage,
    string RatingStatus);
public sealed record MatchHistoryPage(ImmutableArray<MatchHistoryEntry> Entries, long? NextCursor);
public sealed record LeaderboardEntry(PlayerId PlayerId, string DisplayName, long Kills, long Deaths,
    long Wins, long Matches, decimal Score);
public sealed record LeaderboardPage(string Metric, string Scope, MatchTrustClass? TrustClass,
    ImmutableArray<LeaderboardEntry> Entries, string? NextCursor, string? RatingStatus = null);

public sealed partial class AccountSession
{
    public Task<CareerSummary> GetCareerAsync(PlayerId player, CancellationToken cancel = default)
    {
        if (player.IsEmpty) throw new ArgumentException("A player identity is required.", nameof(player));
        return SendAsync<CareerSummary>(HttpMethod.Get, $"v1/players/{player}/career", null, null, cancel);
    }

    public async Task<MatchHistoryPage> GetHistoryAsync(PlayerId player, long? before = null, CancellationToken cancel = default)
    {
        if (player.IsEmpty || before is <= 0) throw new ArgumentException("Invalid history cursor or player.");
        string query = before.HasValue ? "&before=" + before.Value.ToString(CultureInfo.InvariantCulture) : "";
        MatchHistoryPage page = await SendAsync<MatchHistoryPage>(HttpMethod.Get,
            $"v1/players/{player}/matches?limit=25{query}", null, null, cancel).ConfigureAwait(false);
        if (page.Entries.IsDefault || page.Entries.Length > 25 || page.NextCursor is <= 0
            || before.HasValue && page.NextCursor >= before || page.NextCursor.HasValue && page.Entries.IsEmpty)
            throw new InvalidOperationException("The backend returned an invalid history page.");
        long? previous = before;
        foreach (MatchHistoryEntry entry in page.Entries)
        {
            if (entry.ProcessingOrder <= 0 || previous.HasValue && entry.ProcessingOrder >= previous.Value)
                throw new InvalidOperationException("The backend returned an invalid history page.");
            previous = entry.ProcessingOrder;
        }
        if (page.NextCursor.HasValue && page.NextCursor.Value != page.Entries[^1].ProcessingOrder)
            throw new InvalidOperationException("The backend returned an invalid history page.");
        return page;
    }

    public async Task<LeaderboardPage> GetLeaderboardAsync(string metric, string? cursor = null, CancellationToken cancel = default, Hunter? hunter = null)
    {
        if (metric is not ("kills" or "wins" or "kd" or "rp" or "winPercentage" or "headshots" or "octolithScores" or "nodesCaptured" or "killsAsPrime")
            || cursor != null && (cursor.Length == 0 || cursor.Length > 256)
            || hunter.HasValue && (int)hunter.Value is < 0 or > 6)
            throw new ArgumentException("Invalid leaderboard category or cursor.");
        string query = cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor);
        if (hunter.HasValue) query += "&hunter=" + ((int)hunter.Value).ToString(CultureInfo.InvariantCulture);
        LeaderboardPage page = await SendAsync<LeaderboardPage>(HttpMethod.Get,
            $"v1/leaderboards/career?metric={metric}&limit=25{query}", null, null, cancel).ConfigureAwait(false);
        if (page.Metric != metric || page.Entries.IsDefault || page.Entries.Length > 25
            || page.NextCursor is "" || page.NextCursor?.Length > 256 || cursor != null && page.NextCursor == cursor)
            throw new InvalidOperationException("The backend returned an invalid leaderboard page.");
        return page;
    }
}
