using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Rating;
using MphRead.Identity;

namespace MphRead.Backend.Matches;

public sealed record CareerTotals(long Matches, long Wins, long Ties, long PlayedTicks, long Kills,
    long Deaths, long Assists, long Damage, long Losses, long HeadshotKills, long? BipedKills, long? AltFormKills,
    long LongestKillStreak, long LongestWinStreak, decimal? KillDeathRatio, decimal? WinRatio);
public sealed record CareerChoice(string Key, long Samples, long Value, long? MatchesUsed = null);
public sealed record CareerView(string Scope, MatchTrustClass? TrustClass, CareerTotals Totals,
    CareerChoice? MostPlayedHunter, CareerChoice? FavoriteMap, CareerChoice? FavoriteMode,
    CareerChoice? FavoriteWeapon, CareerChoice? BestMap, CareerChoice? BestHunter, int BestMinimumMatches,
    string RatingStatus, RatingSummary Rating);

public static class CareerQueries
{
    public const int BestMinimumMatches = 10;

    public static async Task<CareerView> ReadAsync(BackendDbContext db, Guid player,
        MatchTrustClass? trust, CancellationToken ct)
    {
        var rows = await db.Aggregates.AsNoTracking().Where(x => x.PlayerId == player && (trust.HasValue ? x.TrustClass == (int)trust.Value : x.TrustClass == CareerProjection.OfficialScope)).ToListAsync(ct);
        HunterLicense license = await db.Licenses.AsNoTracking().SingleAsync(x => x.PlayerId == player, ct);
        RatingSummary rating = await RatingProjection.ReadSummaryAsync(db, license, ct);
        var career = rows.SingleOrDefault(x => x.Dimension == "career");
        CareerChoice? Favorite(string dimension, Func<CareerAggregate, long> score)
        {
            var row = rows.Where(x => x.Dimension == dimension && score(x) > 0)
                .OrderByDescending(score).ThenBy(x => x.Key, StringComparer.Ordinal).FirstOrDefault();
            return row == null ? null : new(row.Key, row.Matches, score(row),
                dimension == "weapon" ? row.Matches : null);
        }
        var best = rows.Where(x => x.Dimension == "map" && x.Matches >= BestMinimumMatches)
            .OrderByDescending(x => (decimal)x.Wins / x.Matches).ThenByDescending(x => x.Matches)
            .ThenBy(x => x.Key, StringComparer.Ordinal).FirstOrDefault();
        var bestHunter = rows.Where(x => x.Dimension == "hunter" && x.OutcomeSamples >= BestMinimumMatches)
            .OrderByDescending(x => (decimal)x.Wins / x.OutcomeSamples).ThenByDescending(x => x.OutcomeSamples)
            .ThenBy(x => x.Key, StringComparer.Ordinal).FirstOrDefault();
        return new(trust.HasValue ? "trustClass" : "official", trust, career == null ? new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null)
            : new(career.Matches, career.Wins, career.Ties, career.PlayedTicks, career.Kills, career.Deaths, career.Assists, career.Damage, career.Losses,
                career.HeadshotKills, career.BipedKills, career.AltFormKills, career.LongestKillStreak,
                career.LongestWinStreak, career.Deaths > 0 ? (decimal)career.Kills / career.Deaths : null,
                career.Matches > 0 ? (decimal)career.Wins / career.Matches : null),
            Favorite("hunter", x => x.PlayedTicks), Favorite("map", x => x.PlayedTicks),
            Favorite("mode", x => x.PlayedTicks), Favorite("weapon", x => x.Kills),
            best == null ? null : new(best.Key, best.Matches, best.Wins), bestHunter == null ? null : new(bestHunter.Key, bestHunter.OutcomeSamples, bestHunter.Wins), BestMinimumMatches,
            "active", rating);
    }

    public static void MapCareerQueries(this WebApplication app)
    {
        app.MapGet("/v1/players/{id}/career", async (string id, MatchTrustClass? trustClass,
            BackendDbContext db, CancellationToken ct) =>
        {
            if (!PlayerId.TryParse(id, out var player) || !Enum.IsDefined(trustClass ?? MatchTrustClass.VerifiedCasual))
                return BackendProblem.Create("invalid_request", "The career query is invalid.", StatusCodes.Status400BadRequest);
            if (!await db.Licenses.AnyAsync(x => x.PlayerId == player.Value, ct))
                return BackendProblem.Create("license_not_found", "The player license was not found.", StatusCodes.Status404NotFound);
            return Results.Ok(await ReadAsync(db, player.Value, trustClass, ct));
        }).RequireRateLimiting(BackendRoutePolicy.Api);
        app.MapGet("/v1/players/{id}/matches", async (string id, long? before, int? limit,
            BackendDbContext db, CancellationToken ct) =>
        {
            if (!PlayerId.TryParse(id, out var player) || before is <= 0 || limit is < 1 or > 100)
                return BackendProblem.Create("invalid_request", "The match history query is invalid.", StatusCodes.Status400BadRequest);
            if (!await db.Licenses.AnyAsync(x => x.PlayerId == player.Value, ct))
                return BackendProblem.Create("license_not_found", "The player license was not found.", StatusCodes.Status404NotFound);
            int count = limit ?? 25;
            var rows = await (from participation in db.Participations.AsNoTracking()
                join match in db.Matches.AsNoTracking() on participation.MatchId equals match.MatchId
                where participation.PlayerId == player.Value && (!before.HasValue || participation.ProcessingOrder < before.Value)
                orderby participation.ProcessingOrder descending
                select new { match.MatchId, match.ProcessingOrder, match.EndedAt, match.RoomKey, match.Mode,
                    match.TrustClass, participation.Eligible, participation.Won, participation.Tied,
                    participation.Outcome, participation.PlayedTicks, participation.Kills, participation.Deaths,
                    participation.Assists, participation.Damage, match.RatingStatus }).Take(count + 1).ToListAsync(ct);
            bool more = rows.Count > count; if (more) rows.RemoveAt(count);
            return Results.Ok(new { Entries = rows, NextCursor = more ? (long?)rows[^1].ProcessingOrder : null });
        }).RequireRateLimiting(BackendRoutePolicy.Api);
        app.MapGet("/v1/leaderboards/career", LeaderboardAsync).RequireRateLimiting(BackendRoutePolicy.Api);
    }

    private sealed record BoardCursor(decimal Score, Guid PlayerId, string Metric, MatchTrustClass? TrustClass, Hunter? Hunter);

    private static async Task<IResult> LeaderboardAsync(MatchTrustClass? trustClass, string? metric, int? limit,
        string? cursor, Hunter? hunter, BackendDbContext db, CancellationToken ct)
    {
        metric ??= "kills";
        if ((trustClass.HasValue && !Enum.IsDefined(trustClass.Value)) || limit is < 1 or > 100
            || hunter is < Hunter.Samus or > Hunter.Weavel
            || metric is not ("kills" or "wins" or "kd" or "rp" or "winPercentage" or "headshots" or "octolithScores" or "nodesCaptured" or "killsAsPrime"))
            return BackendProblem.Create("invalid_query", "The leaderboard query is invalid.", StatusCodes.Status400BadRequest);
        BoardCursor? after = null;
        if (cursor != null)
        {
            if (cursor.Length > 256) return BackendProblem.Create("invalid_query", "The leaderboard cursor is invalid.", StatusCodes.Status400BadRequest);
            try { after = System.Text.Json.JsonSerializer.Deserialize<BoardCursor>(Convert.FromBase64String(cursor)); }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
            { return BackendProblem.Create("invalid_query", "The leaderboard cursor is invalid.", StatusCodes.Status400BadRequest); }
            if (after == null || after.Score < 0 || after.PlayerId == Guid.Empty
                || after.Metric != metric || after.TrustClass != trustClass || after.Hunter != hunter)
                return BackendProblem.Create("invalid_query", "The leaderboard cursor is invalid.", StatusCodes.Status400BadRequest);
        }
        string scope = trustClass.HasValue ? "trustClass" : "official";
        if (metric == "rp")
        {
            if (trustClass.HasValue || hunter.HasValue)
                return BackendProblem.Create("invalid_query", "The rating leaderboard filters are invalid.", StatusCodes.Status400BadRequest);
            var ratings = from license in db.Licenses.AsNoTracking()
                join profile in db.Profiles.AsNoTracking() on license.PlayerId equals profile.PlayerId
                select new
                {
                    license.PlayerId, profile.DisplayName, Kills = 0L, Deaths = 0L, Wins = 0L,
                    Matches = 0L, AttributedMatches = 0L, Score = (decimal)license.RatingPoints,
                    Points = license.RatingPoints
                };
            if (after != null) ratings = ratings.Where(x => x.Score < after.Score
                || x.Score == after.Score && x.PlayerId.CompareTo(after.PlayerId) > 0);
            int ratingCount = limit ?? 25;
            var ratingRows = await ratings.OrderByDescending(x => x.Score).ThenBy(x => x.PlayerId)
                .Take(ratingCount + 1).ToListAsync(ct);
            bool ratingMore = ratingRows.Count > ratingCount;
            if (ratingMore) ratingRows.RemoveAt(ratingCount);
            var entries = ratingRows.Select(x => new
            {
                x.PlayerId, x.DisplayName, x.Kills, x.Deaths, x.Wins, x.Matches, x.AttributedMatches,
                x.Score, x.Points, Tier = RetailPointMatrix.TierForPoints(x.Points),
                Title = RatingProjection.Title(RetailPointMatrix.TierForPoints(x.Points))
            }).ToArray();
            string? ratingNext = ratingMore ? Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new BoardCursor(ratingRows[^1].Score, ratingRows[^1].PlayerId, metric, trustClass, hunter))) : null;
            return Results.Ok(new { Metric = metric, Scope = scope, TrustClass = trustClass,
                RatingStatus = "active", Policy = RatingPolicyVersion.PairwiseNormalizedV1.ToString(),
                Entries = entries, NextCursor = ratingNext });
        }
        var totals = db.Aggregates.AsNoTracking().Where(a => (hunter.HasValue ? a.Dimension == "hunter" && a.Key == ((int)hunter.Value).ToString() : a.Dimension == "career")
            && (trustClass.HasValue ? a.TrustClass == (int)trustClass.Value : a.TrustClass == CareerProjection.OfficialScope))
            .Select(a => new { a.PlayerId, a.Kills, a.Deaths, a.Wins, a.Matches, a.OutcomeSamples,
                Headshots = a.HeadshotKills, a.OctolithScores, a.NodesCaptured, a.KillsAsPrime });
        if (metric == "kd") totals = totals.Where(a => a.Deaths > 0 && a.OutcomeSamples >= BestMinimumMatches);
        if (metric == "winPercentage") totals = totals.Where(a => a.OutcomeSamples >= BestMinimumMatches);
        var query = from a in totals join p in db.Profiles.AsNoTracking() on a.PlayerId equals p.PlayerId
            select new { a.PlayerId, p.DisplayName, a.Kills, a.Deaths, a.Wins, a.Matches, AttributedMatches = a.OutcomeSamples,
                Score = metric == "kills" ? (decimal)a.Kills : metric == "wins" ? a.Wins
                    : metric == "headshots" ? a.Headshots : metric == "octolithScores" ? a.OctolithScores
                    : metric == "nodesCaptured" ? a.NodesCaptured : metric == "killsAsPrime" ? a.KillsAsPrime
                    : metric == "winPercentage" ? Math.Round((decimal)a.Wins / a.OutcomeSamples, 6) : Math.Round((decimal)a.Kills / a.Deaths, 6) };
        if (after != null) query = query.Where(a => a.Score < after.Score
            || a.Score == after.Score && a.PlayerId.CompareTo(after.PlayerId) > 0);
        int count = limit ?? 25;
        var rows = await query.OrderByDescending(a => a.Score).ThenBy(a => a.PlayerId).Take(count + 1).ToListAsync(ct);
        bool more = rows.Count > count; if (more) rows.RemoveAt(count);
        string? next = more ? Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new BoardCursor(rows[^1].Score, rows[^1].PlayerId, metric, trustClass, hunter))) : null;
        return Results.Ok(new { Metric = metric, Scope = scope, TrustClass = trustClass, Hunter = hunter, Entries = rows, NextCursor = next });
    }
}
