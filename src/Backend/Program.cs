using System.Text.Json.Serialization;
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

namespace MphRead.Backend;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
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
        BackendSecurity.ValidateCommon(securityOptions);
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        {
            BackendSecurity.ValidateProductionListeners(builder.Configuration, securityOptions);
            if (string.IsNullOrWhiteSpace(accountOptions.DataProtectionKeyPath))
                throw new InvalidOperationException("Accounts__DataProtectionKeyPath must be configured outside Development/Testing.");
        }
        var protection = builder.Services.AddDataProtection().SetApplicationName("PrimeHunters.Backend");
        if (!string.IsNullOrWhiteSpace(accountOptions.DataProtectionKeyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(accountOptions.DataProtectionKeyPath));
        }
        string connection = builder.Configuration.GetConnectionString("Backend")
            ?? throw new InvalidOperationException("ConnectionStrings__Backend must be configured.");
        builder.Services.AddDbContext<BackendDbContext>(options => options.UseNpgsql(connection));
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
        builder.Services.AddScoped<MatchIngestion>();
        builder.Services.AddScoped<CareerRebuild>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
                BackendSecurity.EndpointPartitionKey(http), _ => new FixedWindowRateLimiterOptions
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
        // Validate configured operator identities/keys before accepting requests.
        _ = app.Services.GetRequiredService<GameServerRegistry>();
        var ticketIssuer = app.Services.GetRequiredService<GameTicketIssuer>();
        var confirmationEmail = app.Services.GetRequiredService<IConfirmationEmail>();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            BackendSecurity.ValidateProduction(securityOptions, accountOptions, ticketOptions,
                serverOptions, confirmationEmail.IsConfigured, ticketIssuer.IsConfigured);
        }
        if (args.Contains("--rebuild-career", StringComparer.Ordinal))
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            BackendDbContext database = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            await database.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<CareerRebuild>().RebuildAsync();
            return;
        }
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            bool rebuildRequired = await scope.ServiceProvider.GetRequiredService<BackendDbContext>()
                .ProjectionStates.AsNoTracking().AnyAsync(x => x.Id == 1 && x.RebuildRequired);
            if (rebuildRequired)
                throw new InvalidOperationException("Run the Backend once with --rebuild-career before serving requests.");
        }
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.Use(async (http, next) =>
        {
            if (!http.Request.IsHttps && !app.Environment.IsEnvironment("Testing")
                && !(app.Environment.IsDevelopment()
                    && BackendSecurity.IsExplicitLoopbackDevelopmentRequest(http, securityOptions)))
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
        app.MapAccounts();
        app.MapProfiles();
        app.MapGameTickets();
        app.MapMatches();
        app.MapCareerQueries();
        app.MapMatchExports();
        await app.RunAsync();
    }
}
