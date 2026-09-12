using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MphRead.Backend;
using MphRead.Backend.Data;
using MphRead.Backend.Identity;
using MphRead.Identity;
using Npgsql;

namespace MphRead.Backend.Tests;

internal sealed class BackendFactory(bool requireConfirmation = false, Action<IServiceCollection>? configure = null,
    string? postgresConnection = null, string environment = "Testing") : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string? _database = postgresConnection == null
        ? null
        : "project_prime_test_" + Guid.NewGuid().ToString("N");
    private string? _testConnection;
    public CapturedEmail Email { get; } = new();
    public string PostgreSqlConnection => _testConnection
        ?? throw new InvalidOperationException("This factory is not using PostgreSQL.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseContentRoot(SourceRoot());
        builder.UseSetting("ConnectionStrings:Backend", "Host=unused;Database=unused");
        builder.UseSetting("Accounts:RequireConfirmedEmail", requireConfirmation.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<BackendDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BackendDbContext>>();
            services.RemoveAll<NpgsqlDataSource>();
            if (postgresConnection == null) _connection.Open();
            else
            {
                _testConnection = CreateDisposableDatabase(postgresConnection, _database!);
            }
            services.AddDbContext<BackendDbContext>((provider, options) =>
            {
                if (postgresConnection == null) options.UseSqlite(_connection);
                else options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), BackendDatabase.ConfigureEf);
                options.AddInterceptors(provider.GetServices<SaveChangesInterceptor>());
            });
            if (postgresConnection != null)
            {
                services.AddSingleton<NpgsqlDataSource>(_ =>
                {
                    var builder = new NpgsqlDataSourceBuilder(_testConnection!);
                    builder.Name = BackendDatabase.DataSourceName;
                    BackendDatabase.Configure(builder.ConnectionStringBuilder, new TestHostEnvironment("Testing"));
                    return builder.Build();
                });
            }
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IConfirmationEmail>();
            services.AddSingleton<IConfirmationEmail>(Email);
            configure?.Invoke(services);
        });
    }

    public HttpClient CreateDatabaseClient()
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        if (postgresConnection == null) db.Database.EnsureCreated(); else db.Database.Migrate();
        return client;
    }

    private static string SourceRoot([CallerFilePath] string path = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../src/Backend"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (postgresConnection != null && _database != null)
            {
                using var admin = new NpgsqlConnection(AdminConnection(postgresConnection)); admin.Open();
                using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
                drop.ExecuteNonQuery();
            }
        }
    }

    private static string CreateDisposableDatabase(string adminConnection, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnection);
        string[] hosts = (builder.Host ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length != 1 || hosts[0] is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("PostgreSQL integration tests require a disposable local localhost/127.0.0.1/::1 admin connection.");
        using var admin = new NpgsqlConnection(AdminConnection(adminConnection));
        admin.Open();
        using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
        create.ExecuteNonQuery();
        builder.Database = database;
        builder.SearchPath = null;
        return builder.ConnectionString;
    }

    private static string AdminConnection(string connection)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" };
        return builder.ConnectionString;
    }
}

internal sealed class CapturedEmail : IConfirmationEmail
{
    public bool IsConfigured { get; set; } = true;
    public bool SendSucceeds { get; set; } = true;
    public int Attempts { get; private set; }
    public List<(string Email, PlayerId PlayerId, string Code)> Sent { get; } = [];
    public Task<bool> TrySendAsync(string email, PlayerId playerId, string code, CancellationToken cancellationToken)
    {
        Attempts++;
        if (!SendSucceeds) return Task.FromResult(false);
        Sent.Add((email, playerId, code));
        return Task.FromResult(true);
    }
}
