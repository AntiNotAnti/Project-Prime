using FruityPrime.Server.Node.Identity;
using FruityPrime.Server.Node.Admin;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Lobbies.Queue;
using FruityPrime.Server.Node.Sessions;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FruityPrime.Server.Node.Discovery;
using System.Threading.RateLimiting;

namespace FruityPrime.Server.Node;

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
            builder.Configuration.GetValue("Node:PostMatchVoteSeconds", 15)));
        builder.Services.AddNodeWorkerPool(builder.Configuration, auth.NodeId);
        builder.Services.AddSingleton(NodeContentCatalog.FromConfiguration(
            builder.Configuration.GetSection("Node:Maps").Get<NodeMapConfiguration[]>() ?? []));
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
            options.AddPolicy("host-admin", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        // Fail at startup, rather than expose an accidentally unauthenticated service.
        _ = app.Services.GetRequiredService<NodeAdmissionValidator>();
        _ = app.Services.GetRequiredService<LobbyManager>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(20) });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/v1/status", (NodeSessionManager sessions, LobbyManager lobbies, NodeContentCatalog content) =>
            Results.Ok(new { nodeId = auth.NodeId, protocolVersion = 1, onlineUsers = sessions.Count, lobbyCount = lobbies.Count,
                protocolClosures = sessions.ProtocolClosures, maps = content.Maps }));
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
