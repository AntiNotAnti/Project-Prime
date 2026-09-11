using System.Net;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Data;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class BackendHealthTests
{
    [Fact]
    public async Task LivenessDoesNotRequireDatabaseAndReadinessFailsBeforeInitialization()
    {
        using var factory = new BackendFactory();
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task LivenessBypassesRequestConcurrencySaturation()
    {
        using var factory = new BackendFactory();
        using HttpClient client = factory.CreateClient();
        var limiter = factory.Services.GetRequiredService<ConcurrencyLimiter>();
        var held = new List<RateLimitLease>();
        try
        {
            for (int i = 0; i < 128; i++) held.Add(await limiter.AcquireAsync(1));
            Assert.All(held, lease => Assert.True(lease.IsAcquired));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        finally
        {
            foreach (RateLimitLease lease in held) lease.Dispose();
        }
    }

    [Fact]
    public async Task ReadinessRequiresProjectionRebuildAndTurnsReadyAfterInitialization()
    {
        using var factory = new BackendFactory();
        using HttpClient client = factory.CreateDatabaseClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            CareerProjectionState state = await db.ProjectionStates.SingleAsync(x => x.Id == 1);
            state.RebuildRequired = false;
            await db.SaveChangesAsync();
        }
        HttpResponseMessage response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ready\"}", await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task PostgreSqlReadinessRejectsUnknownMigrationHistory()
    {
        string admin = File.ReadAllText(Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE")!).Trim();
        using var factory = new BackendFactory(postgresConnection: admin);
        using HttpClient client = factory.CreateDatabaseClient();
        using var scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        CareerProjectionState state = await db.ProjectionStates.SingleAsync(x => x.Id == 1);
        state.RebuildRequired = false;
        await db.SaveChangesAsync();
        Assert.True((await scope.ServiceProvider.GetRequiredService<BackendReadinessChecker>().CheckAsync()).Ready);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO prime."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('99999999999999_UnknownFutureMigration', '99.0.0')
            """);
        BackendReadinessResult result = await scope.ServiceProvider
            .GetRequiredService<BackendReadinessChecker>().CheckAsync();
        Assert.False(result.Ready);
        Assert.Equal("migration_sequence", result.Failure);
    }
}
