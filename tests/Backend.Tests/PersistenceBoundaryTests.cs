using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Data;
using Npgsql;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void PostgreSqlMigrationMatchesModelAndGeneratesExpectedConstraintsOffline()
    {
        using var db = new BackendDbContext(new DbContextOptionsBuilder<BackendDbContext>()
            .UseNpgsql("Host=unused;Database=offline_model_only", BackendDatabase.ConfigureEf).Options);
        string[] migrations = db.Database.GetMigrations().ToArray();
        Assert.Equal(2, migrations.Length);
        Assert.EndsWith("_PrimeInitialPostgres", migrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_AddPlayerCosmeticLoadouts", migrations[1], StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());
        string sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE SCHEMA prime", sql);
        Assert.Contains("CREATE TABLE prime.players", sql);
        Assert.Contains("CREATE TABLE prime.player_profiles", sql);
        Assert.Contains("CREATE TABLE prime.hunter_licenses", sql);
        Assert.Contains("CREATE TABLE prime.player_cosmetic_loadouts", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"EmailIndex\"", sql);
        Assert.Contains("FOREIGN KEY (\"PlayerId\") REFERENCES prime.players (\"Id\")", sql);
        Assert.Contains("FOREIGN KEY (player_id) REFERENCES prime.players (\"Id\") ON DELETE CASCADE", sql);
        Assert.Contains("uuid NOT NULL", sql);
        Assert.Contains("CREATE TABLE prime.accepted_matches", sql);
        Assert.Contains("CREATE TABLE prime.rating_transactions", sql);
        Assert.Contains("CREATE TABLE prime.rating_pair_contributions", sql);
        const string historyCreate = "CREATE TABLE IF NOT EXISTS prime.\"__EFMigrationsHistory\"";
        Assert.Contains(historyCreate, sql);
        Assert.True(sql.IndexOf("CREATE SCHEMA prime", StringComparison.Ordinal)
            < sql.IndexOf(historyCreate, StringComparison.Ordinal));
        Assert.DoesNotContain("CREATE TABLE public.", sql);
    }

    [Fact]
    public void ProductionDatabaseConfigurationEnforcesBoundedVerifiedTlsSessionConnections()
    {
        var environment = new TestHostEnvironment("Production");
        string configured = BackendDatabase.ConfigureConnectionString(
            "Host=db.example.test;Port=5432;Database=postgres;Username=prime_app;Password=secret;" +
            "SSL Mode=VerifyFull;Minimum Pool Size=5;Maximum Pool Size=99;Timeout=30;Command Timeout=90",
            environment);
        var parsed = new NpgsqlConnectionStringBuilder(configured);
        Assert.Equal(0, parsed.MinPoolSize);
        Assert.Equal(10, parsed.MaxPoolSize);
        Assert.Equal(10, parsed.Timeout);
        Assert.Equal(15, parsed.CommandTimeout);
        Assert.Equal("ProjectPrime.Backend", parsed.ApplicationName);
        Assert.Equal(SslMode.VerifyFull, parsed.SslMode);

        Assert.Throws<InvalidOperationException>(() => BackendDatabase.ConfigureConnectionString(
            "Host=db.example.test;Port=6543;Database=postgres;SSL Mode=VerifyFull", environment));
        Assert.Throws<InvalidOperationException>(() => BackendDatabase.ConfigureConnectionString(
            "Host=db.example.test;Port=5432;Database=postgres;SSL Mode=Require", environment));
    }

    [PostgresFact]
    public async Task PostgreSqlBaselineUsesOnlyPrimeAndLegacyGuardFailsClosed()
    {
        using var factory = new BackendFactory(postgresConnection: PostgreSqlConnection());
        using HttpClient client = factory.CreateDatabaseClient();
        await using var connection = new NpgsqlConnection(factory.PostgreSqlConnection);
        await connection.OpenAsync();

        await using (var schema = new NpgsqlCommand("""
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_name = ANY (@names)
            ORDER BY table_schema, table_name
            """, connection))
        {
            schema.Parameters.AddWithValue("names", BackendDatabase.ExpectedTables.ToArray());
            var rows = new List<(string Schema, string Table)>();
            await using var reader = await schema.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1)));
            Assert.Equal(BackendDatabase.ExpectedTables.Count, rows.Count);
            Assert.All(rows, row => Assert.Equal(BackendDatabase.Schema, row.Schema));
        }

        await using (var history = new NpgsqlCommand(
            "SELECT COUNT(*) FROM prime.\"__EFMigrationsHistory\"", connection))
            Assert.Equal(2L, (long)(await history.ExecuteScalarAsync())!);

        await using (var legacy = new NpgsqlCommand("CREATE TABLE public.players (id integer)", connection))
            await legacy.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LegacyDatabaseGuard.ThrowIfLegacyObjectsAsync(factory.Services.GetRequiredService<NpgsqlDataSource>()));
    }

    [PostgresFact]
    public async Task RuntimeRoleCanMutateApplicationRowsButCannotOwnSchemaOrMutateHistory()
    {
        using var factory = new BackendFactory(postgresConnection: PostgreSqlConnection());
        using HttpClient client = factory.CreateDatabaseClient();
        string role = "prime_runtime_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(factory.PostgreSqlConnection);
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(connection, $"CREATE ROLE \"{role}\" NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE");
            await ExecuteAsync(connection, $"GRANT USAGE ON SCHEMA prime TO \"{role}\"");
            await ExecuteAsync(connection, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA prime TO \"{role}\"");
            await ExecuteAsync(connection, $"REVOKE INSERT, UPDATE, DELETE ON prime.\"__EFMigrationsHistory\" FROM \"{role}\"");
            await ExecuteAsync(connection, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA prime TO \"{role}\"");
            await ExecuteAsync(connection, $"ALTER ROLE \"{role}\" SET search_path = prime");
            await ExecuteAsync(connection, $"SET ROLE \"{role}\"");

            Guid roleId = Guid.NewGuid();
            await ExecuteAsync(connection,
                $"INSERT INTO prime.\"AspNetRoles\" (\"Id\", \"Name\") VALUES ('{roleId:D}', 'runtime-test')");
            await ExecuteAsync(connection,
                $"UPDATE prime.\"AspNetRoles\" SET \"Name\" = 'runtime-updated' WHERE \"Id\" = '{roleId:D}'");
            await ExecuteAsync(connection,
                $"DELETE FROM prime.\"AspNetRoles\" WHERE \"Id\" = '{roleId:D}'");
            Assert.Equal(2L, (long)(await new NpgsqlCommand(
                "SELECT COUNT(*) FROM prime.\"__EFMigrationsHistory\"", connection).ExecuteScalarAsync())!);
            Assert.IsType<long>(await new NpgsqlCommand(
                "SELECT nextval(pg_get_serial_sequence('prime.accepted_matches', 'ProcessingOrder'))",
                connection).ExecuteScalarAsync());

            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                "CREATE TABLE prime.runtime_must_not_create (id integer)"));
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                "CREATE TABLE runtime_must_not_create (id integer)"));
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                "ALTER TABLE prime.players ADD COLUMN runtime_must_not_alter integer"));
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                "DROP TABLE prime.players"));
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                "DELETE FROM prime.\"__EFMigrationsHistory\""));
        }
        finally
        {
            await ExecuteAsync(connection, "RESET ROLE");
            await ExecuteAsync(connection, $"DROP OWNED BY \"{role}\"");
            await ExecuteAsync(connection, $"DROP ROLE IF EXISTS \"{role}\"");
        }
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

    private static string PostgreSqlConnection()
        => File.ReadAllText(Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE")!).Trim();

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
