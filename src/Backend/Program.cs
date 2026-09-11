using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Matches;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using MphRead.Backend.Identity;
using MphRead.Backend.Profiles;
using MphRead.Backend.Tickets;
using MphRead.Backend.Nodes;
using Npgsql;

namespace MphRead.Backend;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        string[] operatorFlags = new[] { "--migrate", "--rebuild-career", "--check-database" }
            .Where(flag => args.Contains(flag, StringComparer.Ordinal)).ToArray();
        if (operatorFlags.Length > 1)
            throw new InvalidOperationException("Specify exactly one operator flag: --migrate, --rebuild-career, or --check-database.");
        string? operatorMode = operatorFlags.SingleOrDefault();

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection("Accounts"));
        builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
        builder.Services.Configure<BackendSecurityOptions>(builder.Configuration.GetSection("Backend"));
        var accountOptions = builder.Configuration.GetSection("Accounts").Get<AccountOptions>() ?? new();
        var securityOptions = builder.Configuration.GetSection("Backend").Get<BackendSecurityOptions>() ?? new();
        var protection = builder.Services.AddDataProtection().SetApplicationName("ProjectPrime.Backend");
        if (!string.IsNullOrWhiteSpace(accountOptions.DataProtectionKeyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(accountOptions.DataProtectionKeyPath));
        }
        builder.Services.AddSingleton<NpgsqlDataSource>(services =>
            BackendDatabase.CreateDataSource(builder.Configuration, builder.Environment,
                services.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddDbContext<BackendDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>(), BackendDatabase.ConfigureEf));
        builder.Services.AddIdentityCore<HunterAccount>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.SignIn.RequireConfirmedEmail = accountOptions.RequireConfirmedEmail;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<BackendDbContext>().AddSignInManager().AddDefaultTokenProviders();
        builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromMinutes(10);
                options.RefreshTokenExpiration = TimeSpan.FromDays(7);
            });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IConfirmationEmail, ConfirmationEmail>();
        builder.Services.Configure<TicketOptions>(builder.Configuration.GetSection("Tickets"));
        builder.Services.Configure<GameServerOptions>(builder.Configuration.GetSection("GameServers"));
        var ticketOptions = builder.Configuration.GetSection("Tickets").Get<TicketOptions>() ?? new();
        var serverOptions = builder.Configuration.GetSection("GameServers").Get<GameServerOptions>() ?? new();
        builder.Services.AddSingleton<GameTicketIssuer>();
        builder.Services.AddSingleton<GameServerRegistry>();
        builder.Services.AddSingleton<NodeDirectory>();
        builder.Services.AddScoped<MatchIngestion>();
        builder.Services.AddScoped<CareerRebuild>();
        builder.Services.AddScoped<BackendReadinessChecker>();
        builder.Services.AddScoped<BackendDatabaseChecker>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.EndpointPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("guest-auth", http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.IpPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("api", http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.EndpointPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.Services.AddSingleton(new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = securityOptions.MaxConcurrentRequests,
            QueueLimit = 0
        }));
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            BackendSecurity.ConfigureForwarding(options, securityOptions));
        var app = builder.Build();

        // Operator commands intentionally run before listener, SMTP, ticket, and
        // server-registration validation. They are one-shot and never open a web listener.
        if (operatorMode != null)
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            switch (operatorMode)
            {
                case "--migrate":
                    await LegacyDatabaseGuard.ThrowIfLegacyObjectsAsync(
                        scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>());
                    await scope.ServiceProvider.GetRequiredService<BackendDbContext>().Database.MigrateAsync();
                    return;
                case "--rebuild-career":
                {
                    using var rebuildDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    BackendReadinessResult current = await scope.ServiceProvider
                        .GetRequiredService<BackendReadinessChecker>().CheckForRebuildAsync(rebuildDeadline.Token);
                    if (!current.Ready)
                        throw new InvalidOperationException(
                            $"Cannot rebuild career until the exact prime migration sequence and projection state exist (failure: {current.Failure}). Run --migrate first; --rebuild-career never migrates.");
                    await scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync(rebuildDeadline.Token);
                    return;
                }
                case "--check-database":
                {
                    using var checkDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    BackendDatabaseCheckReport report = await scope.ServiceProvider
                        .GetRequiredService<BackendDatabaseChecker>().CheckAsync(checkDeadline.Token);
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        success = report.Success,
                        selectOne = report.SelectOne,
                        serverVersion = report.ServerVersion,
                        currentUser = report.CurrentUser,
                        primeSchema = report.PrimeSchema,
                        exactMigrations = report.ExactMigrations,
                        migration = report.Migration,
                        expectedTables = report.ExpectedTables,
                        failure = report.Failure.Length == 0 ? null : report.Failure
                    }));
                    if (!report.Success) Environment.ExitCode = 1;
                    return;
                }
            }
        }

        BackendSecurity.ValidateCommon(securityOptions);
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            BackendSecurity.ValidateProductionListeners(builder.Configuration, securityOptions);
            if (string.IsNullOrWhiteSpace(accountOptions.DataProtectionKeyPath))
                throw new InvalidOperationException("Accounts__DataProtectionKeyPath must be configured outside Development/Testing.");
        }
        // Validate configured operator identities/keys before accepting requests.
        _ = app.Services.GetRequiredService<GameServerRegistry>();
        var ticketIssuer = app.Services.GetRequiredService<GameTicketIssuer>();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            await using var validationScope = app.Services.CreateAsyncScope();
            var confirmationEmail = validationScope.ServiceProvider.GetRequiredService<IConfirmationEmail>();
            BackendSecurity.ValidateProduction(securityOptions, accountOptions, ticketOptions,
                serverOptions, confirmationEmail.IsConfigured, ticketIssuer.IsConfigured);
        }
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            using var readinessDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            BackendReadinessResult readiness = await scope.ServiceProvider
                .GetRequiredService<BackendReadinessChecker>().CheckAsync(readinessDeadline.Token);
            if (!readiness.Ready)
                throw new InvalidOperationException($"Backend database readiness failed: {readiness.Failure}.");
        }
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.Use(async (http, next) =>
        {
            if (!http.Request.IsHttps && !app.Environment.IsEnvironment("Testing")
                && !(app.Environment.IsDevelopment()
                    && (BackendSecurity.IsExplicitLoopbackDevelopmentRequest(http, securityOptions)
                        || BackendSecurity.IsExplicitRemoteHttpDevelopmentRequest(http, securityOptions))))
            { http.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            // Reject known oversized bodies before binding; Kestrel also bounds chunked bodies.
            long limit = http.Request.Path == "/v1/server/matches" ? ReportValidation.MaximumBytes : 16 * 1024;
            var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
            if (http.Request.ContentLength > limit)
            { http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
            http.Response.Headers.CacheControl = "no-store";
            await next(http);
        });
        app.UseRouting();
        app.Use(async (http, next) =>
        {
            if (http.Request.Path.Equals("/health/live", StringComparison.OrdinalIgnoreCase))
            {
                await next(http);
                return;
            }
            var limiter = http.RequestServices.GetRequiredService<ConcurrencyLimiter>();
            using RateLimitLease lease = await limiter.AcquireAsync(1, http.RequestAborted);
            if (!lease.IsAcquired)
            {
                http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            await next(http);
        });
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/health/ready", async (BackendReadinessChecker checker, CancellationToken requestAborted) =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            BackendReadinessResult readiness = await checker.CheckAsync(deadline.Token);
            return readiness.Ready
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();
        app.MapAccounts();
        app.MapProfiles();
        app.MapGameTickets();
        app.MapNodes();
        app.MapMatches();
        app.MapCareerQueries();
        app.MapMatchExports();
        await app.RunAsync();
    }
}
