using ProjectPrime.Server.Node.Identity;
using ProjectPrime.Server.Node.Admin;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Lobbies.Queue;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var hostAdmin = NodeHostAdminAuthorization.Load(builder.Configuration);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(auth);
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
        builder.Services.AddSingleton<NodeMatchCoordinator>();
        builder.Services.AddSingleton(sp => new NodeSessionManager(sp.GetRequiredService<LobbyManager>(), auth.NodeId,
            builder.Configuration.GetValue("Node:MaximumSessions", 1024), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<NodeMatchCoordinator>()));
        builder.Services.AddHostedService<NodeSessionReaper>();
        builder.Services.AddNodeDirectoryReporter(builder.Configuration);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("control-upgrade", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("latency-probe", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("host-admin", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("map-download", context => RateLimitPartition.GetConcurrencyLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new ConcurrencyLimiterOptions { PermitLimit = 2, QueueLimit = 0 }));
        });
        var app = builder.Build();
        // Fail at startup, rather than expose an accidentally unauthenticated service.
        _ = app.Services.GetRequiredService<NodeAdmissionValidator>();
        _ = app.Services.GetRequiredService<LobbyManager>();
        _ = app.Services.GetRequiredService<NodeMapPackageStore>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(20) });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .RequireRateLimiting("latency-probe");
        app.MapGet("/v1/status", (NodeSessionManager sessions, LobbyManager lobbies,
            NodeContentCatalog content, NodeMapPackageStore packages) =>
            Results.Ok(new { nodeId = auth.NodeId, protocolVersion = 1, onlineUsers = sessions.Count, lobbyCount = lobbies.Count,
                protocolClosures = sessions.ProtocolClosures, maps = content.Maps,
                mapStates = packages.States }));
        app.MapGet("/v1/maps/{stableId}/{version}/{artifactHash}",
            (HttpContext context, NodeMapPackageStore packages, string stableId,
                string version, string artifactHash) =>
            {
                if (!context.Request.IsHttps) return Results.StatusCode(StatusCodes.Status403Forbidden);
                try
                {
                    if (!packages.TryOpen(stableId, version, artifactHash,
                        out FileStream? stream, out MapRequirement? requirement)) return Results.NotFound();
                    context.Response.Headers.ETag = $"\"sha256-{requirement!.ArtifactHash}\"";
                    context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
                    return Results.Stream(stream!, "application/vnd.project-prime.fpmap",
                        $"{requirement.StableId}-{requirement.Version}.fpmap",
                        enableRangeProcessing: false);
                }
                catch (InvalidDataException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
            }).RequireRateLimiting("map-download");
        if (hostAdmin.Enabled)
        {
            app.MapPost("/v1/host/matches/{matchId:guid}/lagcomp-debug",
                NodeHostAdminEndpoints.ConfigureHistoricalDebugAsync)
                .RequireRateLimiting("host-admin");
        }
        app.Map("/v1/control", async (HttpContext context, NodeAdmissionValidator admission, NodeSessionManager sessions) =>
        {
            if (!context.Request.IsHttps) { context.Response.StatusCode = 403; return; }
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            string authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Resume ", StringComparison.Ordinal))
            {
                string resume = authorization[7..];
                if (!sessions.CanResume(resume)) { context.Response.StatusCode = 401; return; }
                using var resumed = await context.WebSockets.AcceptWebSocketAsync();
                await sessions.RunAsync(resumed, null, context.RequestAborted, resume);
                return;
            }
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 4103)
            { context.Response.StatusCode = 401; return; }
            var identity = await admission.ValidateAsync(authorization[7..], context.RequestAborted);
            if (identity == null) { context.Response.StatusCode = 401; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await sessions.RunAsync(socket, identity, context.RequestAborted);
        }).RequireRateLimiting("control-upgrade");
        return app;
    }
}
