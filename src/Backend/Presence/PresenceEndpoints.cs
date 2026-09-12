using MphRead.Backend.Nodes;
using ProjectPrime.Server.Shared;
using System.Text.Json;

namespace MphRead.Backend.Presence;

public static class PresenceEndpoints
{
    public static void MapPresence(this WebApplication app)
    {
        app.MapPut("/v1/node/presence", async (HttpContext http,
            NodeDirectory nodes, PresenceDirectory presence,
            AuthenticatedNodeRateLimiter nodeLimiter, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.DirectoryCategory);
            if (!Credentials(http, out Guid nodeId, out string secret))
            {
                BackendDiagnostics.Directory(logger, "presence-report", "invalid");
                return BackendProblem.Create("invalid_credential",
                    "Node credentials are invalid.", StatusCodes.Status401Unauthorized);
            }
            if (!nodes.Authenticate(nodeId, secret, out _))
            {
                BackendDiagnostics.Directory(logger, "presence-report", "invalid");
                return BackendProblem.Create("invalid_credential",
                    "Node credentials are invalid.", StatusCodes.Status401Unauthorized);
            }
            if (!nodeLimiter.TryAcquire(nodeId, "presence"))
            {
                BackendDiagnostics.Directory(logger, "presence-report", "limited");
                return BackendProblem.Create("rate_limited",
                    "Node update rate limit exceeded.", StatusCodes.Status429TooManyRequests);
            }

            NodePresenceReport? value;
            try
            {
                // Machine credentials are deliberately validated before any
                // attacker-controlled body work. The route-level middleware
                // supplies the streaming size guard for chunked requests and
                // Kestrel enforces the same bound for known lengths.
                value = await http.Request.ReadFromJsonAsync<NodePresenceReport>(
                    cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                return BackendProblem.Create("invalid_presence_report",
                    "The presence report is invalid.", StatusCodes.Status400BadRequest);
            }
            if (value == null)
                return BackendProblem.Create("invalid_presence_report",
                    "The presence report is required.", StatusCodes.Status400BadRequest);

            try
            {
                presence.Report(nodeId, value);
                return Results.Ok();
            }
            catch (PresenceDirectoryException error)
            {
                BackendDiagnostics.Directory(logger, "presence-report", error.Code);
                return BackendProblem.Create(error.Code, error.Message, error.StatusCode);
            }
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);

        app.MapGet("/v1/presence", (int? page, long? revision,
            PresenceDirectory presence, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.DirectoryCategory);
            try
            {
                IResult result = Results.Ok(presence.BrowsePage(page ?? 0, revision));
                BackendDiagnostics.Directory(logger, "presence-browse", "success");
                return result;
            }
            catch (PresenceDirectoryException error)
            {
                BackendDiagnostics.Directory(logger, "presence-browse", error.Code);
                return BackendProblem.Create(error.Code, error.Message, error.StatusCode);
            }
        }).Bodyless().AllowAnonymous().RequireRateLimiting(BackendRoutePolicy.Presence);
    }

    private static bool Credentials(HttpContext http, out Guid id, out string secret)
    {
        secret = "";
        if (!Guid.TryParseExact(http.Request.Headers["X-Server-Id"], "D", out id)) return false;
        string value = http.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        secret = value[7..];
        return true;
    }
}
