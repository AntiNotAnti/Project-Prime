using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Data;
using MphRead.Backend.Matches;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class RatingPersistenceTests
{
    private static readonly Guid Server = Guid.Parse("f6a83c04-5b69-46c2-ab0c-80a25ba66fc0");
    private const string Secret = "rating-persistence-test-secret-32-characters";
    private static string PostgreSql => File.ReadAllText(
        Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE")!).Trim();

    private static BackendFactory Factory(string? postgres = null) => new(configure: services =>
        services.Configure<GameServerOptions>(options => options.Servers.Add(new()
        {
            Id = Server,
            Enabled = true,
            TrustClass = MatchTrustClass.VerifiedCasual,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Secret)))
        })), postgresConnection: postgres);

    private static async Task<Guid> Register(HttpClient client, int index)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            Email = $"rating{index}@example.test",
            Password = "Strong-Test-Password123!",
            DisplayName = $"Rated{index}"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
    }

    private static MatchReportParticipant Participant(Guid player, int slot, int standing,
        int beamKills, ParticipantOutcome outcome = ParticipantOutcome.Finished,
        ParticipantOutcomeReason reason = ParticipantOutcomeReason.Completed, bool started = true)
    {
        ParticipantExitReason exit = reason switch
        {
            ParticipantOutcomeReason.ExplicitLeave => ParticipantExitReason.ExplicitLeave,
            ParticipantOutcomeReason.ReconnectGraceExpired => ParticipantExitReason.ReconnectGraceExpired,
            _ => ParticipantExitReason.Completed
        };
        return new(Guid.NewGuid(), new PlayerId(player), ParticipantKind.RegisteredHuman, $"Rated{slot}",
            started, outcome, 600, [new(100, 700, exit, (byte)slot, Hunter.Samus, slot, 600)],
            new(standing, standing, 0, beamKills, 1, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, [beamKills, 0, 0, 0, 0, 0, 0, 0, 0]), reason);
    }

    private static MatchReportV1 Report(params MatchReportParticipant[] participants)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(MatchReportV1.CurrentSchema, Guid.NewGuid(), 1, Server, Guid.NewGuid(), "test", 8,
            null, MatchTrustClass.VerifiedCasual, "Competitive", null,
            new(MatchMode.Battle, "test-map", rankingEligibility: RankingEligibility.VerifiedServerOnly),
            now.AddSeconds(-10), now, 600, MatchEndReason.TimeLimit, participants.ToImmutableArray());
    }

    private static Task<HttpResponseMessage> Submit(HttpClient client, MatchReportV1 report)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/server/matches")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Headers.Add("X-Server-Id", Server.ToString("D"));
        request.Headers.Add("Authorization", "Bearer " + Secret);
        request.Headers.Add("Idempotency-Key", report.MatchId.ToString("D"));
        request.Headers.Add("X-Content-SHA256", Convert.ToHexString(SHA256.HashData(bytes)));
        return client.SendAsync(request);
    }

    [Fact]
    public async Task RatedReportAtomicallyPersistsReceiptBalancesPairsAndQueries()
    {
        using var factory = Factory(); using HttpClient client = factory.CreateDatabaseClient();
        Guid winner = await Register(client, 1), loser = await Register(client, 2);
        MatchReportV1 report = Report(Participant(winner, 0, 0, 2), Participant(loser, 1, 1, 0));

        HttpResponseMessage first = await Submit(client, report);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        string firstBody = await first.Content.ReadAsStringAsync();
        MatchReceipt receipt = JsonSerializer.Deserialize<MatchReceipt>(firstBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("applied", receipt.RatingStatus);
        Assert.Equal("PairwiseNormalizedV1", receipt.Rating.Policy);
        Assert.Null(receipt.Rating.IneligibilityReason);
        Assert.Equal(2, receipt.Rating.Transactions.Count);

        HttpResponseMessage duplicate = await Submit(client, report);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(firstBody, await duplicate.Content.ReadAsStringAsync());

        using IServiceScope scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(2, await db.RatingTransactions.CountAsync());
        Assert.Equal(2, await db.RatingPairContributions.CountAsync());
        Assert.Equal(2, (await db.Licenses.SingleAsync(x => x.PlayerId == winner)).RatingPoints);
        Assert.Equal(0, (await db.Licenses.SingleAsync(x => x.PlayerId == loser)).RatingPoints);
        Assert.Equal(2, await db.Aggregates.CountAsync(x => x.PlayerId == winner
            && x.Dimension == "weapon" && x.Matches == 1 && x.Kills == 2));
        Assert.Equal(0, await db.Aggregates.CountAsync(x => x.PlayerId == loser && x.Dimension == "weapon"));

        JsonElement license = await client.GetFromJsonAsync<JsonElement>($"/v1/players/{winner:D}/license");
        Assert.Equal(2, license.GetProperty("points").GetInt32());
        Assert.Equal(1, license.GetProperty("tier").GetInt32());
        Assert.Equal("Bounty Hunter", license.GetProperty("title").GetString());
        Assert.Equal(40, license.GetProperty("nextThreshold").GetInt32());
        Assert.Equal(2, license.GetProperty("lastOfficialDelta").GetInt32());
        Assert.Equal(report.MatchId, license.GetProperty("lastOfficialMatchId").GetGuid());
        Assert.Equal("PairwiseNormalizedV1", license.GetProperty("policy").GetString());
        JsonElement zeroDeltaLicense = await client.GetFromJsonAsync<JsonElement>(
            $"/v1/players/{loser:D}/license");
        Assert.Equal(0, zeroDeltaLicense.GetProperty("points").GetInt32());
        Assert.Equal(0, zeroDeltaLicense.GetProperty("lastOfficialDelta").GetInt32());
        Assert.Equal(report.MatchId, zeroDeltaLicense.GetProperty("lastOfficialMatchId").GetGuid());
        JsonElement career = await client.GetFromJsonAsync<JsonElement>($"/v1/players/{winner:D}/career");
        Assert.Equal(2, career.GetProperty("rating").GetProperty("points").GetInt32());
        Assert.Equal(report.MatchId,
            career.GetProperty("rating").GetProperty("lastOfficialMatchId").GetGuid());
        Assert.Equal(1, career.GetProperty("favoriteWeapon").GetProperty("matchesUsed").GetInt64());
        JsonElement board = await client.GetFromJsonAsync<JsonElement>("/v1/leaderboards/career?metric=rp");
        Assert.Equal(winner, board.GetProperty("entries")[0].GetProperty("playerId").GetGuid());
    }

    [PostgresFact]
    public async Task PostgreSqlRatedReportPersistsTheRatingTransactionAndItsIdempotentReceipt()
    {
        using var factory = Factory(PostgreSql); using HttpClient client = factory.CreateDatabaseClient();
        Guid winner = await Register(client, 101), loser = await Register(client, 102);
        MatchReportV1 report = Report(Participant(winner, 0, 0, 2), Participant(loser, 1, 1, 0));

        HttpResponseMessage first = await Submit(client, report);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        MatchReceipt receipt = (await first.Content.ReadFromJsonAsync<MatchReceipt>())!;
        Assert.Equal("applied", receipt.RatingStatus);
        Assert.Equal(2, receipt.Rating.Transactions.Count);

        HttpResponseMessage duplicate = await Submit(client, report);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(2, await db.RatingTransactions.CountAsync());
        Assert.Equal(2, await db.RatingPairContributions.CountAsync());
        Assert.Equal(2, (await db.Licenses.SingleAsync(x => x.PlayerId == winner)).RatingPoints);
        Assert.Equal(0, (await db.Licenses.SingleAsync(x => x.PlayerId == loser)).RatingPoints);
    }

    [Fact]
    public async Task RebuildRestoresBalancesAndRejectsAChangedTransactionChain()
    {
        using var factory = Factory(); using HttpClient client = factory.CreateDatabaseClient();
        Guid first = await Register(client, 1), second = await Register(client, 2);
        (await Submit(client, Report(Participant(first, 0, 0, 1), Participant(second, 1, 1, 0))))
            .EnsureSuccessStatusCode();
        using IServiceScope scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        HunterLicense license = await db.Licenses.SingleAsync(x => x.PlayerId == first);
        license.RatingPoints = 500; await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        await scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync();
        Assert.Equal(2, (await db.Licenses.AsNoTracking().SingleAsync(x => x.PlayerId == first)).RatingPoints);
        Assert.False((await db.ProjectionStates.AsNoTracking().SingleAsync()).RebuildRequired);

        RatingLedgerEntry entry = await db.RatingTransactions.SingleAsync(x => x.PlayerId == first);
        entry.PointsAfter++; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync());
    }

    [Fact]
    public async Task CareerPersistsExplicitFinishedForfeitGraceAndNoContestOutcomes()
    {
        using var factory = Factory(); using HttpClient client = factory.CreateDatabaseClient();
        Guid winner = await Register(client, 1), expired = await Register(client, 2), spectator = await Register(client, 3);
        MatchReportV1 report = Report(
            Participant(winner, 0, 0, 1),
            Participant(expired, 1, 1, 0, ParticipantOutcome.Forfeited,
                ParticipantOutcomeReason.ReconnectGraceExpired),
            Participant(spectator, 2, 2, 0, ParticipantOutcome.Departed,
                ParticipantOutcomeReason.DepartedOutsideOfficialRoster, started: false));
        (await Submit(client, report)).EnsureSuccessStatusCode();

        using IServiceScope scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Dictionary<Guid, CareerParticipation> rows = await db.Participations.ToDictionaryAsync(x => x.PlayerId);
        Assert.Equal(CareerOutcome.FinishedWin, (CareerOutcome)rows[winner].Outcome);
        Assert.Equal(CareerOutcome.DepartedGraceExpired, (CareerOutcome)rows[expired].Outcome);
        Assert.Equal(CareerOutcome.NoContest, (CareerOutcome)rows[spectator].Outcome);
        Assert.True(rows[winner].Eligible); Assert.True(rows[expired].Eligible); Assert.False(rows[spectator].Eligible);
        Assert.Equal(1, (await db.Aggregates.SingleAsync(x => x.PlayerId == expired
            && x.Dimension == "career" && x.TrustClass == CareerProjection.OfficialScope)).Losses);
    }
}
