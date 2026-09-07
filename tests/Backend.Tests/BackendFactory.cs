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

namespace MphRead.Backend.Tests;

internal sealed class BackendFactory(bool requireConfirmation = false, Action<IServiceCollection>? configure = null, string? postgresConnection = null) : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _schema = "test_" + Guid.NewGuid().ToString("N");
    public CapturedEmail Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(SourceRoot());
        builder.UseSetting("ConnectionStrings:Backend", "Host=unused;Database=unused");
        builder.UseSetting("Accounts:RequireConfirmedEmail", requireConfirmation.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<BackendDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BackendDbContext>>();
            if (postgresConnection == null) _connection.Open();
            else
            {
                using var admin = new Npgsql.NpgsqlConnection(postgresConnection); admin.Open();
                using var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA {_schema}", admin); create.ExecuteNonQuery();
            }
            services.AddDbContext<BackendDbContext>((provider, options) =>
            {
                if (postgresConnection == null) options.UseSqlite(_connection);
                else options.UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgresConnection) { SearchPath = _schema }.ConnectionString);
                options.AddInterceptors(provider.GetServices<SaveChangesInterceptor>());
            });
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
            if (postgresConnection != null)
            {
                using var admin = new Npgsql.NpgsqlConnection(postgresConnection); admin.Open();
                using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE", admin); drop.ExecuteNonQuery();
            }
        }
    }
}

internal sealed class CapturedEmail : IConfirmationEmail
{
    public bool IsConfigured { get; set; } = true;
    public List<(string Email, PlayerId PlayerId, string Code)> Sent { get; } = [];
    public Task SendAsync(string email, PlayerId playerId, string code, CancellationToken cancellationToken)
    {
        Sent.Add((email, playerId, code));
        return Task.CompletedTask;
    }
}
