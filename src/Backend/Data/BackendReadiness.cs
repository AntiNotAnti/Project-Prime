using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MphRead.Backend.Data;

public sealed record BackendReadinessResult(bool Ready, string Failure = "ready")
{
    public static BackendReadinessResult Success { get; } = new(true);
}

/// <summary>Bounded, secret-free database checks used by operator and health flows.</summary>
public sealed class BackendReadinessChecker(BackendDbContext db, ILogger<BackendReadinessChecker> logger)
{
    public async Task<BackendReadinessResult> CheckAsync(CancellationToken cancellationToken = default)
        => await CheckCoreAsync(requireProjectionReady: true, cancellationToken);

    public async Task<BackendReadinessResult> CheckForRebuildAsync(
        CancellationToken cancellationToken = default)
        => await CheckCoreAsync(requireProjectionReady: false, cancellationToken);

    private async Task<BackendReadinessResult> CheckCoreAsync(bool requireProjectionReady,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            if (db.Database.IsNpgsql())
            {
                string[] expected = db.Database.GetMigrations().ToArray();
                string[] applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
                if (!expected.SequenceEqual(applied, StringComparer.Ordinal))
                    return new(false, "migration_sequence");
                if (!await HasExpectedTablesAsync(cancellationToken))
                    return new(false, "schema_objects");
            }
            else if (!await HasExpectedSqliteTablesAsync(cancellationToken))
            {
                // SQLite is intentionally limited to focused HTTP tests. It has no
                // PostgreSQL schema/history semantics, but still gets a useful shape gate.
                return new(false, "schema_objects");
            }

            CareerProjectionState? state = await db.ProjectionStates.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (state == null) return new(false, "projection_state");
            if (requireProjectionReady && state.RebuildRequired)
                return new(false, "rebuild_required");
            return BackendReadinessResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, "timeout");
        }
        catch (Exception exception)
        {
            logger.LogWarning("Backend database readiness failed: {FailureClass}", exception.GetType().Name);
            return new(false, "database_unavailable");
        }
    }

    private async Task<bool> HasExpectedTablesAsync(CancellationToken cancellationToken)
    {
        await using var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = ANY (@tables)
            """;
        command.Parameters.Add(new NpgsqlParameter<string>("schema", BackendDatabase.Schema));
        command.Parameters.Add(new NpgsqlParameter<string[]>("tables", BackendDatabase.ExpectedTables.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text
        });
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) found.Add(reader.GetString(0));
            return BackendDatabase.ExpectedTables.All(found.Contains);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> HasExpectedSqliteTablesAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            string names = string.Join(", ", BackendDatabase.ExpectedTables.Select(x => $"'{x.Replace("'", "''")}'"));
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name IN ({names})";
            var found = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) found.Add(reader.GetString(0));
            return BackendDatabase.ExpectedTables.All(found.Contains);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

public sealed record BackendDatabaseCheckReport(
    bool Success,
    bool SelectOne,
    string ServerVersion,
    string CurrentUser,
    bool PrimeSchema,
    bool ExactMigrations,
    bool ExpectedTables,
    string Migration,
    string Failure = "");

public sealed class BackendDatabaseChecker(BackendDbContext db, ILogger<BackendDatabaseChecker> logger)
{
    public async Task<BackendDatabaseCheckReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            if (!db.Database.IsNpgsql())
            {
                bool tables = await HasSqliteTablesAsync(cancellationToken);
                return new(tables, true, "sqlite", "test", tables, true, tables, "none");
            }

            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT current_setting('server_version'), current_user, to_regnamespace(@schema) IS NOT NULL";
                command.Parameters.Add(new NpgsqlParameter<string>("schema", BackendDatabase.Schema));
                string serverVersion;
                string currentUser;
                bool schema;
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                        return new(false, true, "unknown", "unknown", false, false, false, "none", "server_metadata");
                    serverVersion = reader.GetString(0);
                    currentUser = reader.GetString(1);
                    schema = reader.GetBoolean(2);
                }
                string[] expected = db.Database.GetMigrations().ToArray();
                string[] applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
                bool migrations = expected.SequenceEqual(applied, StringComparer.Ordinal);
                bool tables = schema && await HasExpectedTablesAsync(cancellationToken);
                return new(schema && migrations && tables, true, serverVersion, currentUser,
                    schema, migrations, tables, applied.LastOrDefault() ?? "none");
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, false, "unavailable", "unavailable", false, false, false, "none", "timeout");
        }
        catch (Exception exception)
        {
            logger.LogWarning("Backend database check failed: {FailureClass}", exception.GetType().Name);
            return new(false, false, "unavailable", "unavailable", false, false, false, "none", "database_unavailable");
        }
    }

    private async Task<bool> HasExpectedTablesAsync(CancellationToken cancellationToken)
    {
        await using var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_name = ANY (@tables)";
        command.Parameters.Add(new NpgsqlParameter<string>("schema", BackendDatabase.Schema));
        command.Parameters.Add(new NpgsqlParameter<string[]>("tables", BackendDatabase.ExpectedTables.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text
        });
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) found.Add(reader.GetString(0));
        return BackendDatabase.ExpectedTables.All(found.Contains);
    }

    private async Task<bool> HasSqliteTablesAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            string names = string.Join(", ", BackendDatabase.ExpectedTables.Select(x => $"'{x.Replace("'", "''")}'"));
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name IN ({names})";
            var found = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) found.Add(reader.GetString(0));
            return BackendDatabase.ExpectedTables.All(found.Contains);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
