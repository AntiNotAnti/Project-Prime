using ProjectPrime.Server.Node.Identity;
using ProjectPrime.Server.Node.Admin;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Lobbies.Queue;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using ProjectPrime.Server.Node.Discovery;
using System.Threading.RateLimiting;
using ProjectPrime.Server.Node.Maps;

namespace ProjectPrime.Server.Node;

public static class NodeApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var auth = builder.Configuration.GetSection("Node:Authentication").Get<NodeAuthOptions>() ?? new();
        var network = builder.Configuration.GetSection("Node:Network").Get<NodeNetworkOptions>() ?? new();
        NodeNetworkOptions.Validate(network);
        var hostAdmin = NodeHostAdminAuthorization.Load(builder.Configuration);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(auth);
        builder.Services.AddSingleton(network);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            NodeNetworkOptions.ConfigureForwarding(options, network));
        builder.Services.AddSingleton(hostAdmin);
        builder.Services.AddSingleton<NodeAdmissionValidator>();
        builder.Services.AddSingleton(sp => new LobbyManager(
            builder.Configuration.GetValue("Node:MaximumLobbies", 256),
            sp.GetRequiredService<TimeProvider>(),
            builder.Configuration.GetValue("Node:MaximumWaitlistPerLobby", LobbyWaitlist.DefaultMaximumEntries),
            TimeSpan.FromSeconds(builder.Configuration.GetValue("Node:WaitlistOfferSeconds", 15)),
            builder.Configuration.GetValue("Node:PostMatchVoteSeconds", 15),
            builder.Configuration.GetValue("Node:QuickPlayV2Enabled", true),
            sp.GetRequiredService<ILogger<LobbyManager>>()));
        builder.Services.AddNodeWorkerPool(builder.Configuration, auth.NodeId);
        NodeMapConfiguration[] mapConfigurations =
            builder.Configuration.GetSection("Node:Maps").Get<NodeMapConfiguration[]>() ?? [];
        builder.Services.AddSingleton(mapConfigurations);
        builder.Services.AddSingleton(sp => new NodeMapPackageStore(mapConfigurations,
            builder.Environment.ContentRootPath));
        builder.Services.AddSingleton(sp => NodeContentCatalog.FromConfiguration(
            sp.GetRequiredService<NodeMapPackageStore>().ReadyConfigurations));
        builder.Services.AddSingleton<NodeReadinessEvaluator>();
        builder.Services.AddSingleton<NodeMatchCoordinator>();
        builder.Services.AddSingleton(sp => new NodeSessionManager(sp.GetRequiredService<LobbyManager>(), auth.NodeId,
            builder.Configuration.GetValue("Node:MaximumSessions", 1024), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<NodeMatchCoordinator>(), sp.GetRequiredService<NodeContentCatalog>(),
            sp.GetRequiredService<ILogger<NodeSessionManager>>()));
        builder.Services.AddHostedService<NodeSessionReaper>();
        builder.Services.AddNodeDirectoryReporter(builder.Configuration);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static (context, _) =>
            {
                TimeSpan retryAfter = context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter, out TimeSpan advertised)
                    ? advertised : TimeSpan.FromSeconds(60);
                long seconds = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            options.AddPolicy("control-upgrade", context => RateLimitPartition.GetFixedWindowLimiter(
                NodeNetworkOptions.EffectiveRemoteAddress(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("latency-probe", context => RateLimitPartition.GetFixedWindowLimiter(
                NodeNetworkOptions.EffectiveRemoteAddress(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("host-admin", context => RateLimitPartition.GetFixedWindowLimiter(
                NodeNetworkOptions.EffectiveRemoteAddress(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("map-download", context => RateLimitPartition.GetConcurrencyLimiter(
                NodeNetworkOptions.EffectiveRemoteAddress(context),
                _ => new ConcurrencyLimiterOptions { PermitLimit = 2, QueueLimit = 0 }));
            options.AddPolicy("status", context => RateLimitPartition.GetFixedWindowLimiter(
                NodeNetworkOptions.EffectiveRemoteAddress(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        // Fail at startup, rather than expose an accidentally unauthenticated service.
        _ = app.Services.GetRequiredService<NodeAdmissionValidator>();
        _ = app.Services.GetRequiredService<LobbyManager>();
        _ = app.Services.GetRequiredService<NodeMapPackageStore>();
        app.UseForwardedHeaders();
        NodeRequestId.Use(app);
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(20) });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .RequireRateLimiting("latency-probe");
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .RequireRateLimiting("latency-probe");
        app.MapGet("/health/ready", (NodeContentCatalog content, NodeReadinessEvaluator readiness,
            ILoggerFactory loggerFactory) =>
        {
            NodeReadinessResult result = readiness.Evaluate();
            ILogger logger = loggerFactory.CreateLogger(NodeDiagnostics.ReadinessCategory);
            NodeDiagnostics.Readiness(logger, result.IsReady ? "ready" : result.Code);
            return result.IsReady
                ? Results.Ok(new { status = "ready", catalogRevision = content.MapCatalogRevision })
                : Results.Json(new { status = "not_ready", code = result.Code },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .RequireRateLimiting("latency-probe");
        app.MapGet("/v1/status", (NodeSessionManager sessions, LobbyManager lobbies,
            NodeContentCatalog content) =>
            Results.Ok(new { nodeId = auth.NodeId, protocolVersion = 1, onlineUsers = sessions.Count, lobbyCount = lobbies.Count,
                protocolClosures = sessions.ProtocolClosures, mapCount = content.MapCount,
                catalogRevision = content.MapCatalogRevision, catalogHash = content.MapCatalogHash }))
            .RequireRateLimiting("status");
        app.MapGet("/v1/maps/{stableId}/{version}/{artifactHash}",
            (HttpContext context, NodeMapPackageStore packages, string stableId,
                string version, string artifactHash, ILoggerFactory loggerFactory) =>
            {
                ILogger logger = loggerFactory.CreateLogger(NodeDiagnostics.MapCategory);
                if (!context.Request.IsHttps)
                {
                    NodeDiagnostics.Map(logger, "download", "https_required");
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                try
                {
                    if (!packages.TryOpen(stableId, version, artifactHash,
                        out FileStream? stream, out MapRequirement? requirement))
                    {
                        NodeDiagnostics.Map(logger, "download", "not_found");
                        return Results.NotFound();
                    }
                    context.Response.Headers.ETag = $"\"sha256-{requirement!.ArtifactHash}\"";
                    context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
                    NodeDiagnostics.Map(logger, "download", "success");
                    return Results.Stream(stream!, "application/vnd.project-prime.fpmap",
                        $"{requirement.StableId}-{requirement.Version}.fpmap",
                        enableRangeProcessing: true);
                }
                catch (InvalidDataException)
                {
                    NodeDiagnostics.Map(logger, "download", "package_changed");
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
            }).RequireRateLimiting("map-download");
        if (hostAdmin.Enabled)
        {
            app.MapGet("/v1/host/lifecycle",
                NodeHostAdminEndpoints.GetLifecycleDiagnostics)
                .RequireRateLimiting("host-admin");
            app.MapPost("/v1/host/matches/{matchId:guid}/lagcomp-debug",
                NodeHostAdminEndpoints.ConfigureHistoricalDebugAsync)
                .RequireRateLimiting("host-admin");
        }
        app.MapMethods("/v1/control", [HttpMethods.Get], async (HttpContext context,
            NodeAdmissionValidator admission, NodeSessionManager sessions, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(NodeDiagnostics.WebSocketCategory);
            if (!context.Request.IsHttps)
            {
                NodeDiagnostics.Rejected(logger, "control", "https_required");
                context.Response.StatusCode = 403; return;
            }
            if (!context.WebSockets.IsWebSocketRequest)
            {
                NodeDiagnostics.Rejected(logger, "control", "not_websocket");
                context.Response.StatusCode = 400; return;
            }
            string authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Resume ", StringComparison.Ordinal))
            {
                string resume = authorization[7..];
                if (!sessions.CanResume(resume))
                {
                    NodeDiagnostics.Rejected(logger, "control", "resume_invalid");
                    context.Response.StatusCode = 401; return;
                }
                using var resumed = await context.WebSockets.AcceptWebSocketAsync();
                await sessions.RunAsync(resumed, null, context.RequestAborted, resume);
                return;
            }
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 4103)
            {
                NodeDiagnostics.Rejected(logger, "control", "credential_missing");
                context.Response.StatusCode = 401; return;
            }
            var identity = await admission.ValidateAsync(authorization[7..], context.RequestAborted);
            if (identity == null)
            {
                NodeDiagnostics.Rejected(logger, "control", "credential_invalid");
                context.Response.StatusCode = 401; return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await sessions.RunAsync(socket, identity, context.RequestAborted);
        }).RequireRateLimiting("control-upgrade");
        return app;
    }
}
