using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Cosmetics;
using MphRead.Backend.Matches;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using MphRead.Backend.Identity;
using MphRead.Backend.Profiles;
using MphRead.Backend.Tickets;
using MphRead.Backend.Nodes;
using MphRead.Backend.Presence;
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
        builder.WebHost.ConfigureKestrel(options =>
            options.Limits.MaxRequestBodySize = BackendRequestLimits.MaximumKestrelRequestBytes);
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
        builder.Services.AddSingleton<AccountConfirmationCodes>();
        builder.Services.Configure<TicketOptions>(builder.Configuration.GetSection("Tickets"));
        builder.Services.Configure<GameServerOptions>(builder.Configuration.GetSection("GameServers"));
        var ticketOptions = builder.Configuration.GetSection("Tickets").Get<TicketOptions>() ?? new();
        var serverOptions = builder.Configuration.GetSection("GameServers").Get<GameServerOptions>() ?? new();
        builder.Services.AddSingleton<GameTicketIssuer>();
        builder.Services.AddSingleton<GameServerRegistry>();
        builder.Services.AddSingleton<AuthenticatedNodeRateLimiter>();
        builder.Services.AddSingleton<NodeDirectory>();
        builder.Services.AddSingleton<PresenceDirectory>();
        builder.Services.AddScoped<MatchIngestion>();
        builder.Services.AddScoped<CareerRebuild>();
        builder.Services.AddScoped<BackendReadinessChecker>();
        builder.Services.AddScoped<BackendDatabaseChecker>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                BackendDiagnostics.Rejected(
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(BackendDiagnostics.RequestCategory),
                    "rate_limit", "rate_limited");
                await BackendProblem.WriteAsync(context.HttpContext, "rate_limited",
                    "Request rate limit exceeded.", StatusCodes.Status429TooManyRequests, cancellationToken);
            };
            options.AddPolicy(BackendRoutePolicy.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.EndpointPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = BackendRoutePolicy.AuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(BackendRoutePolicy.GuestAuth, http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.IpPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = BackendRoutePolicy.GuestAuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(BackendRoutePolicy.Api, http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.EndpointPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = BackendRoutePolicy.ApiPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(BackendRoutePolicy.Presence, http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.IpPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = BackendRoutePolicy.PresencePermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(BackendRoutePolicy.MachinePreAuth, http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.MachinePartitionKey(http), _ => new FixedWindowRateLimiterOptions
                { PermitLimit = BackendRoutePolicy.MachinePreAuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
        ILogger requestLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(BackendDiagnostics.RequestCategory);
        // Correlation is assigned before HTTPS, body-size, concurrency, and
        // rate-limit rejects so every HTTP response has an ID.
        app.Use(async (http, next) =>
        {
            string supplied = http.Request.Headers["X-Request-Id"].ToString();
            string requestId = Guid.TryParseExact(supplied, "D", out Guid parsed) && parsed != Guid.Empty
                ? parsed.ToString("D") : Guid.NewGuid().ToString("D");
            http.Response.Headers["X-Request-Id"] = requestId;
            await next(http);
        });
        app.UseExceptionHandler();
        // Validate the externally visible scheme before routing and before any
        // request body is touched. This also applies to liveness: the
        // liveness exemption is only for the global lease/body reader, not for
        // the public HTTPS boundary.
        app.Use(async (http, next) =>
        {
            if (!http.Request.IsHttps && !app.Environment.IsEnvironment("Testing")
                && !(app.Environment.IsDevelopment()
                    && BackendSecurity.IsExplicitLoopbackDevelopmentRequest(http, securityOptions)))
            {
                BackendDiagnostics.Rejected(requestLogger, "https", "https_required");
                await BackendProblem.WriteAsync(http, "https_required", "HTTPS is required.",
                    StatusCodes.Status400BadRequest, http.RequestAborted);
                return;
            }
            await next(http);
        });
        app.UseRouting();
        // Authentication and endpoint-specific rate limiting run after routing
        // but before the global lease. Rejected requests therefore never cause
        // an untrusted body to be read or hold a concurrency permit.
        app.UseAuthentication();
        app.UseRateLimiter();
        // Authorization is intentionally before the lease and body reader so
        // an unauthenticated protected request cannot make the service read a
        // slow, attacker-controlled body while waiting for a 401.
        app.UseAuthorization();
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
                BackendDiagnostics.Rejected(requestLogger, "concurrency", "service_busy");
                await BackendProblem.WriteAsync(http, "service_busy", "The service is temporarily unavailable.",
                    StatusCodes.Status503ServiceUnavailable, http.RequestAborted);
                return;
            }
            await next(http);
        });
        app.Use(async (http, next) =>
        {
            // Liveness is deliberately body-independent and remains available
            // under saturation. Never read an arbitrary body on this path.
            if (http.Request.Path.Equals("/health/live", StringComparison.OrdinalIgnoreCase))
            {
                await next(http);
                return;
            }
            http.Response.Headers.CacheControl = "no-store";
            // Reject known oversized bodies before binding; Kestrel also bounds chunked bodies.
            long limit = BackendRequestLimits.ForPath(http.Request.Path);
            var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
            if (http.Request.ContentLength > limit)
            {
                BackendDiagnostics.Rejected(requestLogger, "body", "request_too_large");
                await BackendProblem.WriteAsync(http, "request_too_large", "The request exceeds its route size bound.",
                    StatusCodes.Status413PayloadTooLarge, http.RequestAborted);
                return;
            }

            // A route explicitly marked bodyless never consumes request bytes.
            // Keep this opt-in: a missing body on a body-bound route must still
            // pass through the normal model binder and cancellation path.
            if (BackendRequestLimits.IsBodyless(http)
                || http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()
                    is { CanHaveBody: false })
            {
                await next(http);
                return;
            }

            // Match reports have one owner for payload consumption: the
            // endpoint reads one bounded payload and passes those exact bytes to
            // ingestion for hashing, idempotency, and durable storage. Do not
            // install a second request-body guard around that path.
            if (BackendRequestLimits.IsBoundedPayloadRoute(http.Request.Path)
                || http.Request.ContentLength is not null)
            {
                await next(http);
                return;
            }

            // TestServer and some reverse proxies do not enforce Kestrel's
            // MaxRequestBodySize for a chunked request. Guard those streams in
            // place so model binding receives the original bytes without a
            // second request buffer. The global lease above bounds how many
            // slow readers may exist concurrently.
            var originalBody = http.Request.Body;
            var boundedBody = new BoundedRequestBodyStream(originalBody, limit);
            http.Request.Body = boundedBody;
            try
            {
                await next(http);
            }
            catch (BackendRequestBodyLimitExceededException)
                when (!http.Response.HasStarted && !http.RequestAborted.IsCancellationRequested)
            {
                BackendDiagnostics.Rejected(requestLogger, "body", "request_too_large");
                await BackendProblem.WriteAsync(http, "request_too_large", "The request exceeds its route size bound.",
                    StatusCodes.Status413PayloadTooLarge, http.RequestAborted);
            }
            finally
            {
                http.Request.Body = originalBody;
            }
        });
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).Bodyless().AllowAnonymous();
        app.MapGet("/health/ready", async (BackendReadinessChecker checker, CancellationToken requestAborted) =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            BackendReadinessResult readiness = await checker.CheckAsync(deadline.Token);
            return readiness.Ready
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).Bodyless().AllowAnonymous();
        app.MapAccounts();
        app.MapProfiles();
        app.MapCosmetics();
        app.MapGameTickets();
        app.MapNodes();
        app.MapPresence();
        app.MapMatches();
        app.MapCareerQueries();
        app.MapMatchExports();
        await app.RunAsync();
    }
}
