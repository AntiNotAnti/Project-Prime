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
            favoriteWeapon = (object?)null,
            bestMap = (object?)null,
            bestHunter = (object?)null,
            bestMinimumMatches = 10,
            ratingStatus = "policyPending",
            rating = (object?)null
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
        Assert.Null(career.FavoriteWeapon);
        Assert.Equal("policyPending", career.RatingStatus);
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
            ratingStatus = "policyPending",
            rating = (object?)null
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
                ratingStatus = metric == "rp" ? "policyPending" : null
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
                metric = "kills", scope = "official", trustClass = (int?)null,
                entries = Array.Empty<object>(), nextCursor = "next-page"
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
            kills = 26L - index, deaths = 1L, wins = 1L, matches = 1L, score = (decimal)(26 - index)
        }).ToArray();
        var oversized = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            metric = "kills", scope = "official", trustClass = (int?)null,
            entries, nextCursor = (string?)null
        })));
        using (var session = new AccountSession(new Uri("https://accounts.example.test/"), oversized))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetLeaderboardAsync("kills"));
        }

        var repeated = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new
        {
            metric = "kills", scope = "official", trustClass = (int?)null,
            entries = Array.Empty<object>(), nextCursor = "same"
        })));
        using var repeatedSession = new AccountSession(new Uri("https://accounts.example.test/"), repeated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repeatedSession.GetLeaderboardAsync("kills", "same"));
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

    [Fact]
    public async Task LeaderboardArgumentsRejectInvalidCategoriesHuntersAndCursors()
    {
        using var session = new AccountSession(new Uri("https://accounts.example.test/"),
            new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected.")));

        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("unknown"));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("kills", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("kills", new string('x', 257)));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetLeaderboardAsync("kills", hunter: (Hunter)7));
    }

    private static object HistoryEntry(long order, string room) => new
    {
        matchId = Guid.Parse($"33333333-3333-3333-3333-{order:000000000000}"),
        processingOrder = order,
        endedAt = MatchEnded.AddMinutes(-order),
        roomKey = room,
        mode = (int)MatchMode.Battle,
        trustClass = (int)MatchTrustClass.VerifiedCasual,
        eligible = true,
        won = order == 30,
        tied = false,
        outcome = (int)ParticipantOutcome.Finished,
        playedTicks = 600L,
        kills = order,
        deaths = 2L,
        assists = 1L,
        damage = 1000L,
        ratingStatus = "policyPending"
    };

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
