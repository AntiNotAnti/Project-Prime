using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Data;
using MphRead.Backend.Matches;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE") == null)
            Skip = "Set PRIME_TEST_POSTGRES_FILE to an isolated PostgreSQL connection-string file.";
    }
}

public sealed class MatchLedgerTests
{
    private static readonly Guid Server = Guid.Parse("48cb76ad-3a4d-43e1-b8eb-848c861c52df");
    private static readonly Guid RankedServer = Guid.Parse("7d7af24f-cbac-4e55-8492-2bcaaf570b15");
    private static readonly Guid CommunityServer = Guid.Parse("0aeb1b6c-77e6-48a0-b65c-18d84932358e");
    private const string Secret = "test-only-report-secret-at-least-32-characters";
    private static string PostgreSql => File.ReadAllText(Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE")!).Trim();
    private static BackendFactory Factory(string? postgres = null, SaveChangesInterceptor? interceptor = null, Action<IServiceCollection>? configure = null)
        => new(configure: services =>
        {
            services.Configure<GameServerOptions>(o => o.Servers.Add(new()
            { Id = Server, Enabled = true, TrustClass = MatchTrustClass.VerifiedCasual,
                ApiKeySha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Secret))) }));
            services.Configure<GameServerOptions>(o =>
            {
                foreach (var item in new[] { (RankedServer, MatchTrustClass.Ranked), (CommunityServer, MatchTrustClass.Community) })
                    o.Servers.Add(new() { Id = item.Item1, Enabled = true, TrustClass = item.Item2,
                        ApiKeySha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Secret))) });
            });
            if (interceptor != null) services.AddSingleton(interceptor);
            configure?.Invoke(services);
        }, postgresConnection: postgres);

    private static async Task<Guid> Register(HttpClient client, int index)
    {
        var result = await client.PostAsJsonAsync("/v1/auth/register", new
        { Email = $"ledger{index}@example.test", Password = "Strong-Test-Password123!", DisplayName = $"Player{index}" });
        result.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("playerId").GetGuid();
    }

    private static MatchReportV1 Report(params Guid[] players)
    {
        var participants = players.Select((id, slot) => new MatchReportParticipant(Guid.NewGuid(), new PlayerId(id),
            ParticipantKind.RegisteredHuman, "Player", true, ParticipantOutcome.Finished, 600,
            [new(100, 700, ParticipantExitReason.Completed, (byte)slot, Hunter.Samus, slot, 600)],
            new(slot, slot, 0, 3 - slot, slot, 1, 30, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                [3 - slot, 0, 0, 0, 0, 0, 0, 0, 0]))).ToImmutableArray();
        var now = DateTimeOffset.UtcNow;
        return new(1, Guid.NewGuid(), 1, Server, Guid.NewGuid(), "test", 8, null, MatchTrustClass.VerifiedCasual,
            "Custom", null, new(MatchMode.Battle, "test-map"), now.AddSeconds(-10), now, 600, MatchEndReason.TimeLimit, participants);
    }

    private static Task<HttpResponseMessage> Submit(HttpClient client, MatchReportV1 report, string? secret = null)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/server/matches") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Server-Id", report.ServerId.ToString("D"));
        request.Headers.Add("Authorization", "Bearer " + (secret ?? Secret));
        request.Headers.Add("Idempotency-Key", report.MatchId.ToString("D"));
        request.Headers.Add("X-Content-SHA256", Convert.ToHexString(SHA256.HashData(bytes)));
        return client.SendAsync(request);
    }

    [Fact]
    public void EnhancedHunterReportsRequireCustomUnrankedRules()
    {
        MatchReportV1 report = Report(Guid.NewGuid());
        DateTimeOffset now = report.EndedAtUtc.AddMinutes(1);
        MatchRules invalidPreset = new(MatchMode.Battle, "test-map",
            enhancedHunters: true, rulesetPreset: RulesetPreset.Classic,
            rankingEligibility: RankingEligibility.Unranked);
        MatchRules invalidEligibility = new(MatchMode.Battle, "test-map",
            enhancedHunters: true, rulesetPreset: RulesetPreset.Custom,
            rankingEligibility: RankingEligibility.VerifiedServerOnly);
        MatchRules valid = new(MatchMode.Battle, "test-map",
            enhancedHunters: true, rulesetPreset: RulesetPreset.Custom,
            rankingEligibility: RankingEligibility.Unranked);

        Assert.False(ReportValidation.Validate(report with { Rules = invalidPreset },
            Server, MatchTrustClass.VerifiedCasual, now));
        Assert.False(ReportValidation.Validate(report with { Rules = invalidEligibility },
            Server, MatchTrustClass.VerifiedCasual, now));
        Assert.True(ReportValidation.Validate(report with { Rules = valid },
            Server, MatchTrustClass.VerifiedCasual, now));
    }

    [Fact]
    public async Task HttpLedgerAuthenticatesValidatesAndReturnsExactIdempotentReceipt()
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        Guid player = await Register(client, 1);
        var report = Report(player);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Submit(client, report, "wrong")).StatusCode);
        var first = await Submit(client, report); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<MatchReceipt>();
        Assert.NotNull(receipt); Assert.Equal("ineligible", receipt.RatingStatus);
        Assert.Equal("LegacyReport", receipt.Rating.IneligibilityReason);
        Assert.Empty(receipt.Rating.Transactions);
        Assert.Equal(HttpStatusCode.OK, (await Submit(client, report)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(client, report with { BuildVersion = "changed" })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(1, await db.Matches.CountAsync()); Assert.Equal(1, await db.Participations.CountAsync());
        Assert.Equal(3, (await db.Aggregates.SingleAsync(a => a.Dimension == "career" && a.TrustClass == CareerProjection.OfficialScope)).Kills);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(report), (await db.Matches.SingleAsync()).OriginalReport);
    }

    [PostgresFact]
    public async Task PostgreSqlConcurrentOverlappingReportsDuplicatesAndRebuildPreserveAtomicFacts()
    {
        using var factory = Factory(PostgreSql); using var client = factory.CreateDatabaseClient();
        Guid p1 = await Register(client, 1), p2 = await Register(client, 2);
        var first = Report(p1, p2); var second = Report(p2, p1);
        var responses = await Task.WhenAll(Submit(client, first), Submit(client, first), Submit(client, second));
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(2, await db.Matches.CountAsync()); Assert.Equal(4, await db.Participations.CountAsync());
        var before = await Snapshot(db);
        await scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync();
        Assert.Equal(before, await Snapshot(db));
        Assert.Equal(5, (await db.Aggregates.SingleAsync(a => a.PlayerId == p1 && a.Dimension == "career" && a.TrustClass == CareerProjection.OfficialScope)).Kills);
        for (int i = 0; i < 8; i++) (await Submit(client, Report(p1, p2))).EnsureSuccessStatusCode();
        var career = await client.GetFromJsonAsync<CareerView>($"/v1/players/{p1:D}/career");
        Assert.NotNull(career!.BestMap); Assert.NotNull(career.BestHunter);
        foreach (string metric in new[] { "kills", "wins", "kd", "rp", "winPercentage", "headshots", "octolithScores", "nodesCaptured", "killsAsPrime" })
            (await client.GetAsync($"/v1/leaderboards/career?metric={metric}&limit=1")).EnsureSuccessStatusCode();
        (await client.GetAsync("/v1/leaderboards/career?metric=winPercentage&hunter=0")).EnsureSuccessStatusCode();
        var board = await client.GetFromJsonAsync<JsonElement>("/v1/leaderboards/career?limit=1");
        string cursor = board.GetProperty("nextCursor").GetString()!;
        var next = await client.GetFromJsonAsync<JsonElement>("/v1/leaderboards/career?limit=1&cursor=" + Uri.EscapeDataString(cursor));
        Assert.NotEqual(board.GetProperty("entries")[0].GetProperty("playerId").GetGuid(), next.GetProperty("entries")[0].GetProperty("playerId").GetGuid());
        var history = await client.GetFromJsonAsync<JsonElement>($"/v1/players/{p1:D}/matches?limit=1");
        Assert.NotEqual(JsonValueKind.Null, history.GetProperty("nextCursor").ValueKind);
    }

    [PostgresFact]
    public async Task PostgreSqlProjectionFailureRollsBackLedgerAndAllAggregates()
    {
        using var factory = Factory(PostgreSql, new FailProjection()); using var client = factory.CreateDatabaseClient();
        Guid player = await Register(client, 1);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Submit(client, Report(player))).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Empty(await db.Matches.ToListAsync()); Assert.Empty(await db.Participations.ToListAsync()); Assert.Empty(await db.Aggregates.ToListAsync());
    }

    [Fact]
    public async Task RegistryTrustOverridesRawClaimAndRemainsStableDuringRebuild()
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        Guid player = await Register(client, 1);
        var report = Report(player) with { TrustClass = MatchTrustClass.Community };
        (await Submit(client, report)).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        var stored = await db.Matches.SingleAsync();
        Assert.Equal((int)MatchTrustClass.VerifiedCasual, stored.TrustClass);
        Assert.Equal(MatchTrustClass.Community, JsonSerializer.Deserialize<MatchReportV1>(stored.OriginalReport)!.TrustClass);
        string before = await Snapshot(db); await scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync();
        Assert.Equal(before, await Snapshot(db));
    }

    [Fact]
    public async Task OfficialCareerMergesOnlyVerifiedAndRankedAndPreservesUnknownTelemetry()
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        Guid player = await Register(client, 1), opponent = await Register(client, 2);
        var report = Report(player, opponent);
        (await Submit(client, report)).EnsureSuccessStatusCode();
        (await Submit(client, report with { MatchId = Guid.NewGuid(), ServerId = RankedServer, TrustClass = MatchTrustClass.Ranked })).EnsureSuccessStatusCode();
        (await Submit(client, report with { MatchId = Guid.NewGuid(), ServerId = CommunityServer, TrustClass = MatchTrustClass.Community })).EnsureSuccessStatusCode();
        var career = await client.GetFromJsonAsync<CareerView>($"/v1/players/{player:D}/career");
        Assert.NotNull(career); Assert.Equal("official", career.Scope); Assert.Null(career.TrustClass);
        Assert.Equal(2, career.Totals.Matches); Assert.Equal(6, career.Totals.Kills);
        Assert.Equal(2, career.Totals.LongestWinStreak); Assert.Null(career.Totals.BipedKills);
        Assert.Null(career.Totals.KillDeathRatio); Assert.Null(career.BestHunter); Assert.Null(career.BestMap);
        var community = await client.GetFromJsonAsync<CareerView>($"/v1/players/{player:D}/career?trustClass=0");
        Assert.Equal(1, community!.Totals.Matches);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        string before = await Snapshot(db); await scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync();
        Assert.Equal(before, await Snapshot(db));
    }

    [Fact]
    public async Task InvalidRosterFactsNeverReachLedgerAndAbortedReportsStayOutOfCareer()
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        var report = Report(await Register(client, 1), await Register(client, 2));
        var p = report.Participants[0];
        foreach (var changed in new[]
        {
            p with { Kind = ParticipantKind.Bot },
            p with { Metrics = p.Metrics with { HeadshotKills = p.Metrics.Kills + 1 } },
            p with { Metrics = p.Metrics with { BipedKills = p.Metrics.Kills, AltFormKills = 1 } },
            p with { Metrics = p.Metrics with { Kills = int.MaxValue, BipedKills = int.MaxValue, AltFormKills = int.MaxValue } },
            p with { Metrics = p.Metrics with { BeamKills = [int.MaxValue, int.MaxValue, 0, 0, 0, 0, 0, 0, 0] } },
            p with { Spans = [p.Spans[0] with { PlayedTicks = 601 }] },
            p with { Spans = [p.Spans[0] with { Hunter = Hunter.Random }] },
            p with { Spans = [p.Spans[0] with { Slot = 1 }] }
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, report with { Participants = report.Participants.SetItem(0, changed) })).StatusCode);
        (await Submit(client, report with { EndReason = MatchEndReason.Forced })).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Single(await db.Matches.ToListAsync()); Assert.Empty(await db.Aggregates.ToListAsync());
        Assert.All(await db.Participations.ToListAsync(), x => Assert.False(x.Eligible));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(3, null)]
    [InlineData(null, 3)]
    [InlineData(2, 1)]
    public async Task KnownDisjointFormKillsAndLegacyUnknownCountersRemainAdmissible(int? biped, int? alt)
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        var report = Report(await Register(client, 1));
        var participant = report.Participants[0];
        report = report with { Participants = [participant with
        { Metrics = participant.Metrics with { BipedKills = biped, AltFormKills = alt } }] };
        Assert.Equal(HttpStatusCode.Created, (await Submit(client, report)).StatusCode);
    }

    [Theory]
    [InlineData(10584000u)] // 49 hours, crossing uint tick wrap.
    [InlineData((uint)int.MaxValue)]
    public async Task LongSupportedMatchIntervalsRemainAdmissibleAcrossTickWrap(uint ticks)
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        var report = Report(await Register(client, 1)); var participant = report.Participants[0];
        uint start = uint.MaxValue - 999;
        report = report with
        {
            StartedAtUtc = report.EndedAtUtc - TimeSpan.FromSeconds(ticks / 60d), PlayedTicks = ticks,
            Participants = [participant with { PlayedTicks = ticks,
                Spans = [participant.Spans[0] with { JoinedTick = start, LeftTick = unchecked(start + ticks), PlayedTicks = ticks }],
                Metrics = participant.Metrics with { ModeTimeSeconds = ticks / 60f } }]
        };
        Assert.Equal(HttpStatusCode.Created, (await Submit(client, report)).StatusCode);
    }

    [Fact]
    public async Task AmbiguousHalfRangeReportOrParticipationIntervalIsRejected()
    {
        using var factory = Factory(); using var client = factory.CreateDatabaseClient();
        var report = Report(await Register(client, 1)); var participant = report.Participants[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, report with { PlayedTicks = 0x80000000u })).StatusCode);
        report = report with { Participants = [participant with
        { Spans = [participant.Spans[0] with { LeftTick = unchecked(participant.Spans[0].JoinedTick + 0x80000000u) }] }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, report)).StatusCode);
    }

    [PostgresFact]
    public async Task PostgreSqlConcurrentRegistrationRetainsOneAccountProfileAndLicense()
    {
        using var factory = Factory(PostgreSql); using var client = factory.CreateDatabaseClient();
        var request = new { Email = "race@example.test", Password = "Strong-Test-Password123!", DisplayName = "Race" };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/v1/auth/register", request), client.PostAsJsonAsync("/v1/auth/register", request));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(1, await db.Users.CountAsync()); Assert.Equal(1, await db.Profiles.CountAsync()); Assert.Equal(1, await db.Licenses.CountAsync());
    }

    [PostgresFact]
    public async Task ActualServerReporterOutboxReceivesBackendReceiptAfterPostgreSqlCommit()
    {
        using var keys = new TicketTests.TestKeys();
        using var factory = Factory(PostgreSql, configure: services => services.Configure<TicketOptions>(keys.Configure)); using var client = factory.CreateDatabaseClient();
        Guid player = await Register(client, 1);
        var report = Report(player) with { TrustClass = MatchTrustClass.Community };
        string directory = Directory.CreateTempSubdirectory("prime-report-end-to-end-").FullName;
        try
        {
            using var transport = new MphRead.Reporting.HttpMatchReportTransport(Server,
                new Uri("https://localhost/v1/server/matches"), Secret, client);
            await using var outbox = new MphRead.Reporting.MatchReportOutbox(new(directory), transport);
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (!outbox.CanAccept && DateTimeOffset.UtcNow < deadline) await Task.Delay(10);
            Assert.True(outbox.TryEnqueue(report, out var receipt));
            while (receipt!.State is not (MphRead.Reporting.ReportSubmissionState.BackendAccepted or MphRead.Reporting.ReportSubmissionState.Failed
                or MphRead.Reporting.ReportSubmissionState.Quarantined) && DateTimeOffset.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(MphRead.Reporting.ReportSubmissionState.BackendAccepted, receipt.State);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            var match = await db.Matches.SingleAsync(); Assert.Equal(receipt.PayloadHash, match.PayloadHash);
            Assert.Equal((int)MatchTrustClass.VerifiedCasual, match.TrustClass);
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
            var export = await client.GetFromJsonAsync<JsonElement>($"/v1/matches/{report.MatchId:D}/export");
            Assert.Equal(match.PayloadHash, Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(export.GetProperty("originalReport").GetString()!))));
            var jwk = factory.Services.GetRequiredService<GameTicketIssuer>().PublicKeys[0];
            var verified = await new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().ValidateTokenAsync(
                export.GetProperty("signedReceipt").GetString()!, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidIssuer = "https://backend.example.test", ValidAudience = "urn:project-prime:match-result",
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.JsonWebKey
                    {
                        Kty = jwk.Kty, Crv = jwk.Crv, X = jwk.X, Y = jwk.Y, Kid = jwk.Kid
                    },
                    ValidAlgorithms = [Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256],
                    ValidTypes = ["pp-match-result+jwt"], ValidateLifetime = false, RequireExpirationTime = false
                });
            Assert.True(verified.IsValid, verified.Exception?.Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<string> Snapshot(BackendDbContext db) => JsonSerializer.Serialize(await db.Aggregates.AsNoTracking()
        .OrderBy(a => a.PlayerId).ThenBy(a => a.TrustClass).ThenBy(a => a.Dimension).ThenBy(a => a.Key).ToArrayAsync());
    private sealed class FailProjection : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<CareerAggregate>().Any()) throw new InvalidOperationException("Injected projection failure");
            return ValueTask.FromResult(result);
        }
    }
}
