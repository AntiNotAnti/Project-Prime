using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Formats;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class CareerQueryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerId Player = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly DateTimeOffset MatchEnded = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task CareerMapsMostPlayedHunterAndNullableFormStatistics()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            scope = "official",
            trustClass = (int?)null,
            totals = new
            {
                matches = 12L, wins = 7L, ties = 1L, playedTicks = 7200L, kills = 36L,
                deaths = 18L, assists = 9L, damage = 12345L, losses = 4L, headshotKills = 5L,
                bipedKills = (long?)null, altFormKills = (long?)null, longestKillStreak = 4L,
                longestWinStreak = 3L, killDeathRatio = 2m, winRatio = 0.583333m
            },
            mostPlayedHunter = new { key = "0", samples = 12L, value = 5200L },
            favoriteMap = new { key = "Arcterra", samples = 4L, value = 2100L },
            favoriteMode = new { key = "0", samples = 8L, value = 3200L },
            favoriteWeapon = new { key = "2", samples = 3L, value = 9L, matchesUsed = 3L },
            bestMap = (object?)null,
            bestHunter = (object?)null,
            bestMinimumMatches = 10,
            ratingStatus = "active",
            rating = new
            {
                points = 390, tier = 4, title = "Master Hunter", nextThreshold = (int?)750,
                lastOfficialDelta = (int?)-2, policy = "PairwiseNormalizedV1"
            }
        })));
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        CareerSummary career = await session.GetCareerAsync(Player);

        Assert.Equal("official", career.Scope);
        Assert.Null(career.TrustClass);
        Assert.Equal(12, career.Totals.Matches);
        Assert.Equal(4, career.Totals.Losses);
        Assert.Equal(2m, career.Totals.KillDeathRatio);
        Assert.Equal(0.583333m, career.Totals.WinRatio);
        Assert.Equal("0", career.MostPlayedHunter!.Key);
        Assert.Equal(12, career.MostPlayedHunter.Samples);
        Assert.Equal(5200, career.MostPlayedHunter.Value);
        Assert.Null(career.Totals.BipedKills);
        Assert.Null(career.Totals.AltFormKills);
        Assert.Equal(3, career.FavoriteWeapon!.MatchesUsed);
        Assert.Equal(9, career.FavoriteWeapon.Value);
        Assert.Equal("active", career.RatingStatus);
        Assert.Equal(390, career.Rating.Points);
        Assert.Equal(4, career.Rating.Tier);
        Assert.Equal("Master Hunter", career.Rating.Title);
        Assert.Equal(750, career.Rating.NextThreshold);
        Assert.Equal(-2, career.Rating.LastOfficialDelta);
        Assert.Equal("PairwiseNormalizedV1", career.Rating.Policy);
        RequestLog request = Assert.Single(handler.Snapshot());
        Assert.Equal($"/v1/players/{Player}/career", request.Uri.AbsolutePath);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task NoActivityCareerPreservesNullChoicesAndTelemetry()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            scope = "official",
            trustClass = (int?)null,
            totals = new
            {
                matches = 0L, wins = 0L, ties = 0L, playedTicks = 0L, kills = 0L,
                deaths = 0L, assists = 0L, damage = 0L, losses = 0L, headshotKills = 0L,
                bipedKills = (long?)null, altFormKills = (long?)null, longestKillStreak = 0L,
                longestWinStreak = 0L, killDeathRatio = (decimal?)null, winRatio = (decimal?)null
            },
            mostPlayedHunter = (object?)null,
            favoriteMap = (object?)null,
            favoriteMode = (object?)null,
            favoriteWeapon = (object?)null,
            bestMap = (object?)null,
            bestHunter = (object?)null,
            bestMinimumMatches = 10,
            ratingStatus = "active",
            rating = new
            {
                points = 0, tier = 1, title = "Bounty Hunter", nextThreshold = (int?)40,
                lastOfficialDelta = (int?)null, policy = "PairwiseNormalizedV1"
            }
        })));
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        CareerSummary career = await session.GetCareerAsync(Player);

        Assert.Equal(0, career.Totals.Matches);
        Assert.Equal(0, career.Totals.Losses);
        Assert.Null(career.MostPlayedHunter);
        Assert.Equal(0, career.Totals.HeadshotKills);
        Assert.Null(career.Totals.BipedKills);
        Assert.Null(career.Totals.AltFormKills);
        Assert.Equal(0, career.Totals.LongestKillStreak);
        Assert.Equal(0, career.Totals.LongestWinStreak);
        Assert.Null(career.BestHunter);
        Assert.Null(Assert.Single(handler.Snapshot()).Authorization);
    }

    [Fact]
    public async Task AllNineLeaderboardCategoriesArePublicAndUseTheFixedPageSize()
    {
        string[] metrics = { "kills", "wins", "kd", "rp", "winPercentage", "headshots",
            "octolithScores", "nodesCaptured", "killsAsPrime" };
        var handler = new RecordingHandler((request, _) =>
        {
            string metric = QueryValue(request.RequestUri!, "metric")!;
            return Task.FromResult(JsonResponse(new
            {
                metric,
                scope = "official",
                trustClass = (int?)null,
                entries = Array.Empty<object>(),
                nextCursor = (string?)null,
                hunter = (int?)null,
                ratingStatus = metric == "rp" ? "active" : null,
                policy = metric == "rp" ? "PairwiseNormalizedV1" : null
            }));
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        foreach (string metric in metrics)
        {
            LeaderboardPage page = await session.GetLeaderboardAsync(metric);
            Assert.Equal(metric, page.Metric);
            Assert.Empty(page.Entries);
        }

        RequestLog[] requests = handler.Snapshot();
        Assert.Equal(metrics.Length, requests.Length);
        for (int index = 0; index < metrics.Length; index++)
        {
            Assert.Equal($"/v1/leaderboards/career", requests[index].Uri.AbsolutePath);
            Assert.Contains($"metric={metrics[index]}&limit=25", requests[index].Uri.Query, StringComparison.Ordinal);
            Assert.Null(requests[index].Authorization);
        }
    }

    [Fact]
    public async Task LeaderboardEscapesCursorAndForwardsHunterFilter()
    {
        const string cursor = "page +/=?&%";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("kills", QueryValue(request.RequestUri!, "metric"));
            Assert.Equal(cursor, QueryValue(request.RequestUri!, "cursor"));
            Assert.Equal("0", QueryValue(request.RequestUri!, "hunter"));
            return Task.FromResult(JsonResponse(new
            {
                metric = "kills", scope = "official", trustClass = (int?)null, hunter = (int?)Hunter.Samus,
                entries = new[]
                {
                    new
                    {
                        playerId = Player, displayName = "Hunter", kills = 2L, deaths = 1L,
                        wins = 1L, matches = 1L, attributedMatches = 1L, score = 2m
                    }
                },
                nextCursor = "next-page"
            }));
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        LeaderboardPage page = await session.GetLeaderboardAsync("kills", cursor, CancellationToken.None, Hunter.Samus);

        Assert.Equal("next-page", page.NextCursor);
        RequestLog request = Assert.Single(handler.Snapshot());
        Assert.Contains("cursor=" + Uri.EscapeDataString(cursor), request.Uri.Query, StringComparison.Ordinal);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task LeaderboardRejectsOversizedPagesAndARepeatedCursor()
    {
        var entries = Enumerable.Range(0, 26).Select(index => new
        {
            playerId = new PlayerId(Guid.Parse($"22222222-2222-2222-2222-{index + 1:000000000000}")),
            displayName = "Player" + index.ToString(CultureInfo.InvariantCulture),
            kills = 26L - index, deaths = 1L, wins = 1L, matches = 1L,
            attributedMatches = 1L, score = (decimal)(26 - index)
        }).ToArray();
        var oversized = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            metric = "kills", scope = "official", trustClass = (int?)null, hunter = (int?)null,
            entries, nextCursor = (string?)null
        })));
        using (var session = new AccountSession(new Uri("https://accounts.example.test/"), oversized))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetLeaderboardAsync("kills"));
        }

        var repeated = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            metric = "kills", scope = "official", trustClass = (int?)null, hunter = (int?)null,
            entries = Array.Empty<object>(), nextCursor = "same"
        })));
        using var repeatedSession = new AccountSession(new Uri("https://accounts.example.test/"), repeated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repeatedSession.GetLeaderboardAsync("kills", "same"));
    }

    [Fact]
    public async Task RpLeaderboardMapsAuthoritativePointsTierTitleAndPolicy()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            metric = "rp",
            scope = "official",
            trustClass = (int?)null,
            ratingStatus = "active",
            policy = "PairwiseNormalizedV1",
            entries = new[]
            {
                new
                {
                    playerId = Player, displayName = "Hunter", kills = 0L, deaths = 0L,
                    wins = 0L, matches = 0L, attributedMatches = 0L, score = 750m,
                    points = 750, tier = 5, title = "Legendary Hunter"
                }
            },
            nextCursor = (string?)null
        })));
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        LeaderboardPage page = await session.GetLeaderboardAsync("rp");

        LeaderboardEntry entry = Assert.Single(page.Entries);
        Assert.Equal(750, entry.Points);
        Assert.Equal(5, entry.Tier);
        Assert.Equal("Legendary Hunter", entry.Title);
        Assert.Equal("PairwiseNormalizedV1", page.Policy);
        Assert.Equal("active", page.RatingStatus);
    }

    [Fact]
    public async Task HistoryKeepsNewestFirstAndRejectsANonDecreasingCursor()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            bool older = QueryValue(request.RequestUri!, "before") != null;
            return Task.FromResult(JsonResponse(new
            {
                entries = older
                    ? new[] { HistoryEntry(20, "older") }
                    : new[] { HistoryEntry(30, "newest"), HistoryEntry(20, "older") },
                nextCursor = older ? 20L : 20L
            }));
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        MatchHistoryPage first = await session.GetHistoryAsync(Player);

        Assert.Equal(new long[] { 30, 20 }, first.Entries.Select(entry => entry.ProcessingOrder));
        Assert.Equal(20, first.NextCursor);
        Assert.Null(handler.Snapshot()[0].Authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetHistoryAsync(Player, first.NextCursor));
        RequestLog[] requests = handler.Snapshot();
        Assert.Equal("limit=25", requests[0].Uri.Query.TrimStart('?'));
        Assert.Equal("limit=25&before=20", requests[1].Uri.Query.TrimStart('?'));
        Assert.Null(requests[1].Authorization);
    }

    [Fact]
    public async Task HistoryRejectsOutOfOrderEntries()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            entries = new[] { HistoryEntry(20, "older"), HistoryEntry(30, "newer") },
            nextCursor = 30L
        })));
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetHistoryAsync(Player));
    }

    [Theory]
    [InlineData(CareerOutcome.FinishedWin, true, true, false)]
    [InlineData(CareerOutcome.FinishedLoss, true, false, false)]
    [InlineData(CareerOutcome.Tie, true, false, true)]
    [InlineData(CareerOutcome.Forfeit, true, false, false)]
    [InlineData(CareerOutcome.DepartedGraceExpired, true, false, false)]
    [InlineData(CareerOutcome.NoContest, false, false, false)]
    public async Task HistoryMapsExplicitCareerOutcome(CareerOutcome outcome, bool eligible, bool won, bool tied)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            entries = new[] { HistoryEntry(10, "room", outcome, eligible, won, tied) },
            nextCursor = (long?)null
        })));
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        MatchHistoryPage page = await session.GetHistoryAsync(Player);

        Assert.Equal(outcome, Assert.Single(page.Entries).Outcome);
    }

    [Fact]
    public async Task CareerRejectsMissingRatingAndUnprovenWeaponMatchesUsed()
    {
        object Totals() => new
        {
            matches = 1L, wins = 1L, ties = 0L, playedTicks = 600L, kills = 1L,
            deaths = 0L, assists = 0L, damage = 10L, losses = 0L, headshotKills = 0L,
            bipedKills = (long?)null, altFormKills = (long?)null, longestKillStreak = 1L,
            longestWinStreak = 1L, killDeathRatio = (decimal?)null, winRatio = (decimal?)1m
        };
        var missingRating = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            scope = "official", trustClass = (int?)null, totals = Totals(),
            mostPlayedHunter = (object?)null, favoriteMap = (object?)null, favoriteMode = (object?)null,
            favoriteWeapon = (object?)null, bestMap = (object?)null, bestHunter = (object?)null,
            bestMinimumMatches = 10, ratingStatus = "active"
        })));
        using (var session = new AccountSession(new Uri("https://accounts.example.test/"), missingRating))
        {
            await Assert.ThrowsAsync<JsonException>(() => session.GetCareerAsync(Player));
        }

        var unprovenWeapon = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            scope = "official", trustClass = (int?)null, totals = Totals(),
            mostPlayedHunter = (object?)null, favoriteMap = (object?)null, favoriteMode = (object?)null,
            favoriteWeapon = new { key = "1", samples = 1L, value = 1L },
            bestMap = (object?)null, bestHunter = (object?)null, bestMinimumMatches = 10,
            ratingStatus = "active", rating = new
            {
                points = 40, tier = 2, title = "Super Hunter", nextThreshold = (int?)140,
                lastOfficialDelta = (int?)1, policy = "PairwiseNormalizedV1"
            }
        })));
        using var invalid = new AccountSession(new Uri("https://accounts.example.test/"), unprovenWeapon);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.GetCareerAsync(Player));
    }

    [Fact]
    public async Task CareerRequestPropagatesCancellation()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, cancel) =>
        {
            requestStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
            throw new InvalidOperationException("Canceled career request continued.");
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);
        using var stop = new CancellationTokenSource();

        Task request = session.GetCareerAsync(Player, stop.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task LeaderboardArgumentsRejectInvalidCategoriesHuntersAndCursors()
    {
        using var session = new AccountSession(new Uri("https://accounts.example.test/"),
            new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected.")));

        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("unknown"));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("kills", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("kills", new string('x', 257)));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync(
            "kills", hunter: Hunter.Random));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("rp", hunter: Hunter.Samus));
    }

    private static object HistoryEntry(long order, string room, CareerOutcome? explicitOutcome = null,
        bool? explicitEligible = null, bool? explicitWon = null, bool? explicitTied = null)
    {
        bool won = explicitWon ?? order == 30;
        bool tied = explicitTied ?? false;
        CareerOutcome outcome = explicitOutcome ?? (won ? CareerOutcome.FinishedWin : CareerOutcome.FinishedLoss);
        return new
        {
            matchId = Guid.Parse($"33333333-3333-3333-3333-{order:000000000000}"),
            processingOrder = order,
            endedAt = MatchEnded.AddMinutes(-order),
            roomKey = room,
            mode = (int)MatchMode.Battle,
            trustClass = (int)MatchTrustClass.VerifiedCasual,
            eligible = explicitEligible ?? true,
            won,
            tied,
            outcome = (int)outcome,
            playedTicks = 600L,
            kills = order,
            deaths = 2L,
            assists = 1L,
            damage = 1000L,
            ratingStatus = outcome == CareerOutcome.NoContest ? "ineligible" : "applied"
        };
    }

    private static string? QueryValue(Uri uri, string key)
    {
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == key) return Uri.UnescapeDataString(pair[1].Replace('+', ' '));
        }
        return null;
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json")
        };

    private sealed record RequestLog(HttpMethod Method, Uri Uri, string? Authorization);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly List<RequestLog> _requests = [];

        public RequestLog[] Snapshot()
        {
            lock (_lock) return _requests.ToArray();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _requests.Add(new RequestLog(request.Method, request.RequestUri!,
                    request.Headers.Authorization?.ToString()));
            }
            return responder(request, cancellationToken);
        }
    }
}
