using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Matches;
using Microsoft.AspNetCore.Http.Features;
using MphRead.Backend.Identity;
using MphRead.Backend.Profiles;
using MphRead.Backend.Tickets;

namespace MphRead.Backend;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection("Accounts"));
        builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
        var accountOptions = builder.Configuration.GetSection("Accounts").Get<AccountOptions>() ?? new();
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")
            && string.IsNullOrWhiteSpace(accountOptions.DataProtectionKeyPath))
            throw new InvalidOperationException("Accounts__DataProtectionKeyPath must be configured outside Development/Testing.");
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
        builder.Services.AddSingleton<GameTicketIssuer>();
        builder.Services.AddSingleton<GameServerRegistry>();
        builder.Services.AddScoped<MatchIngestion>();
        builder.Services.AddScoped<CareerRebuild>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("api", http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        // Validate configured operator identities/keys before accepting requests.
        _ = app.Services.GetRequiredService<GameServerRegistry>();
        _ = app.Services.GetRequiredService<GameTicketIssuer>();
        app.UseExceptionHandler();
        app.Use(async (http, next) =>
        {
            if (!http.Request.IsHttps && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
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
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAccounts();
        app.MapProfiles();
        app.MapGameTickets();
        app.MapMatches();
        app.MapCareerQueries();
        app.MapMatchExports();
        app.Run();
    }
}
