using Npgsql;
using NpgsqlTypes;

namespace MphRead.Backend.Data;

/// <summary>Prevents the first prime-schema migration from silently touching a legacy public schema.</summary>
public static class LegacyDatabaseGuard
{
    public static async Task ThrowIfLegacyObjectsAsync(NpgsqlDataSource dataSource,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("""
            WITH candidates AS (
                SELECT 'public.__EFMigrationsHistory' AS object_name
                WHERE to_regclass('public."__EFMigrationsHistory"') IS NOT NULL
                UNION ALL
                SELECT 'public.' || table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = ANY (@known_tables)
            )
            SELECT COALESCE(string_agg(object_name, ', ' ORDER BY object_name), '')
            FROM candidates;
            """);
        var parameter = new NpgsqlParameter<string[]>("known_tables", BackendDatabase.ExpectedTables.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text
        };
        command.Parameters.Add(parameter);
        string found = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "";
        if (found.Length != 0)
            throw new InvalidOperationException(
                $"Refusing --migrate because legacy public Project Prime objects exist: {found}. " +
                "Create and use an explicit MoveToPrimeSchema migration; this command never moves or drops legacy objects automatically.");
    }
}
