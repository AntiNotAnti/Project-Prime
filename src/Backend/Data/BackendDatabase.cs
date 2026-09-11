using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MphRead.Backend.Data;

/// <summary>Central PostgreSQL provider configuration owned by the Backend DI container.</summary>
public static class BackendDatabase
{
    public const string Schema = "prime";
    public const string HistoryTable = "__EFMigrationsHistory";
    public const string DataSourceName = "ProjectPrime.Backend";

    public static readonly IReadOnlyList<string> ExpectedTables =
    [
        "AspNetRoles", "AspNetRoleClaims", "AspNetUserClaims", "AspNetUserLogins",
        "AspNetUserRoles", "AspNetUserTokens", "players", "player_profiles",
        "hunter_licenses", "career_projection_state", "accepted_matches",
        "career_participations", "career_aggregates", "rating_transactions",
        "rating_pair_contributions"
    ];

    public static NpgsqlDataSource CreateDataSource(IConfiguration configuration,
        IHostEnvironment environment, ILoggerFactory loggerFactory)
    {
        string connection = configuration.GetConnectionString("Backend")
            ?? throw new InvalidOperationException("ConnectionStrings__Backend must be configured.");
        var builder = new NpgsqlDataSourceBuilder(connection);
        Configure(builder.ConnectionStringBuilder, environment);
        builder.Name = DataSourceName;
        builder.UseLoggerFactory(loggerFactory);
        // Npgsql's native OpenTelemetry meter is keyed by the stable data-source name.
        // Do not add an EF interceptor or a second pool gauge here.
        return builder.Build();
    }

    public static string ConfigureConnectionString(string connection, IHostEnvironment environment)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection);
        Configure(builder, environment);
        return builder.ConnectionString;
    }

    public static void Configure(NpgsqlConnectionStringBuilder builder, IHostEnvironment environment)
    {
        builder.MinPoolSize = 0;
        builder.MaxPoolSize = 10;
        builder.Timeout = 10;
        builder.CommandTimeout = 15;
        builder.ApplicationName = DataSourceName;
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            if (builder.Port != 5432)
                throw new InvalidOperationException("Backend PostgreSQL must use port 5432 outside Development/Testing.");
            if (builder.SslMode != SslMode.VerifyFull)
                throw new InvalidOperationException("Backend PostgreSQL must use SslMode=VerifyFull outside Development/Testing.");
        }
    }

    public static void ConfigureEf(NpgsqlDbContextOptionsBuilder options)
        => options.MigrationsHistoryTable(HistoryTable, Schema);
}
