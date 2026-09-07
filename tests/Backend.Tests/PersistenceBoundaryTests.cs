using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Data;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void PostgreSqlMigrationMatchesModelAndGeneratesExpectedConstraintsOffline()
    {
        using var db = new BackendDbContext(new DbContextOptionsBuilder<BackendDbContext>()
            .UseNpgsql("Host=unused;Database=offline_model_only").Options);
        Assert.Equal(3, db.Database.GetMigrations().Count());
        Assert.False(db.Database.HasPendingModelChanges());
        string sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE players", sql);
        Assert.Contains("CREATE TABLE player_profiles", sql);
        Assert.Contains("CREATE TABLE hunter_licenses", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"EmailIndex\"", sql);
        Assert.Contains("FOREIGN KEY (\"PlayerId\") REFERENCES players (\"Id\")", sql);
        Assert.Contains("uuid NOT NULL", sql);
        Assert.Contains("CREATE TABLE accepted_matches", sql);
    }

    [Fact]
    public async Task ProfileWriteFailureRollsBackThePreviouslyInsertedAccount()
    {
        using var factory = new BackendFactory(configure: services =>
            services.AddSingleton<SaveChangesInterceptor, FailProfileWrite>());
        using var client = factory.CreateDatabaseClient();
        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        { Email = "rollback@example.test", Password = "Test-Strong-Password123!", DisplayName = "Rollback" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Profiles.CountAsync());
        Assert.Equal(0, await db.Licenses.CountAsync());
        Assert.Empty(factory.Email.Sent);
    }

    private sealed class FailProfileWrite : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PlayerProfile>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Injected profile storage failure.");
            return ValueTask.FromResult(result);
        }
    }
}
