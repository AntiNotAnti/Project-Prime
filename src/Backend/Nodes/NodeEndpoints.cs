using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Profiles;
using MphRead.Backend.Tickets;
using MphRead.Identity;

namespace MphRead.Backend.Nodes;

public sealed record NodeAdmissionRequest(Guid NodeId);
public sealed record GuestNodeAdmissionRequest(Guid NodeId, string? DisplayName);
public static class NodeEndpoints
{
    public static void MapNodes(this WebApplication app)
    {
        app.MapGet("/v1/nodes", (int protocol, string build, string content, int? page,
            long? revision, NodeDirectory directory, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.DirectoryCategory);
            try
            {
                IResult result = Results.Ok(directory.BrowsePage(protocol, build, content,
                    page ?? 0, revision));
                BackendDiagnostics.Directory(logger, "browse", "success");
                return result;
            }
            catch (NodeDirectoryPageException error)
            {
                BackendDiagnostics.Directory(logger, "browse", error.Code);
                return BackendProblem.Create(error.Code, error.Message, error.StatusCode);
            }
        }).RequireRateLimiting(BackendRoutePolicy.Api);
        app.MapGet("/v1/node-admission-keys", (HttpContext http, GameTicketIssuer issuer,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.AdmissionCategory);
            // Public verification material is useful only over HTTPS. Local
            // loopback HTTP remains an explicit development seam.
            if (!http.Request.IsHttps && !IPAddress.IsLoopback(http.Connection.LocalIpAddress ?? IPAddress.None))
            {
                BackendDiagnostics.Admission(logger, "keys", "https_required");
                return BackendProblem.Create("https_required", "Node admission keys require HTTPS.",
                    StatusCodes.Status400BadRequest);
            }
            BackendDiagnostics.Admission(logger, "keys", "success");
            return Results.Ok(issuer.PublicKeys);
        }).AllowAnonymous().RequireRateLimiting(BackendRoutePolicy.Api);
        app.MapPut("/v1/node/registration", (NodeRegistration value, HttpContext http, NodeDirectory directory,
            AuthenticatedNodeRateLimiter nodeLimiter, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.DirectoryCategory);
            if (!Credentials(http, out var id, out var secret))
            {
                BackendDiagnostics.Directory(logger, "registration", "invalid");
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            }
            if (!directory.Authenticate(id, secret, out _))
            {
                BackendDiagnostics.Directory(logger, "registration", "invalid");
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            }
            if (!nodeLimiter.TryAcquire(id, "registration"))
            {
                BackendDiagnostics.Directory(logger, "registration", "limited");
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            }
            try { return directory.Register(id, secret, value) ? Results.Ok() : BackendProblem.Create("invalid_credential", "Node credentials are invalid.", StatusCodes.Status401Unauthorized); }
            catch (ArgumentException)
            {
                BackendDiagnostics.Directory(logger, "registration", "invalid_request");
                return BackendProblem.Create("invalid_request", "The Node registration is invalid.",
                    StatusCodes.Status400BadRequest);
            }
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);
        app.MapDelete("/v1/node/registration", (HttpContext http, NodeDirectory directory,
            AuthenticatedNodeRateLimiter nodeLimiter, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.DirectoryCategory);
            if (!Credentials(http, out var id, out var secret))
            {
                BackendDiagnostics.Directory(logger, "deregistration", "invalid");
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            }
            if (!directory.Authenticate(id, secret, out _))
            {
                BackendDiagnostics.Directory(logger, "deregistration", "invalid");
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            }
            if (!nodeLimiter.TryAcquire(id, "deregistration"))
            {
                BackendDiagnostics.Directory(logger, "deregistration", "limited");
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            }
            if (!Guid.TryParseExact(http.Request.Query["incarnation"], "D", out Guid incarnation)
                || incarnation == Guid.Empty)
            {
                BackendDiagnostics.Directory(logger, "deregistration", "invalid_request");
                return BackendProblem.Create("invalid_request", "The Node incarnation is invalid.",
                    StatusCodes.Status400BadRequest);
            }
            directory.Deregister(id, secret, incarnation);
            BackendDiagnostics.Directory(logger, "deregistration", "success");
            return Results.NoContent();
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);
        app.MapPost("/v1/node/heartbeat", (NodeHeartbeat value, HttpContext http, NodeDirectory directory,
            AuthenticatedNodeRateLimiter nodeLimiter, ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.DirectoryCategory);
            if (!Credentials(http, out var id, out var secret))
            {
                BackendDiagnostics.Directory(logger, "heartbeat", "invalid");
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            }
            if (!directory.Authenticate(id, secret, out _))
            {
                BackendDiagnostics.Directory(logger, "heartbeat", "invalid");
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            }
            if (!nodeLimiter.TryAcquire(id, "heartbeat"))
            {
                BackendDiagnostics.Directory(logger, "heartbeat", "limited");
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            }
            try { return directory.Heartbeat(id, secret, value) ? Results.Ok() : BackendProblem.Create("invalid_credential", "Node credentials are invalid.", StatusCodes.Status401Unauthorized); }
            catch (ArgumentException)
            {
                BackendDiagnostics.Directory(logger, "heartbeat", "invalid_request");
                return BackendProblem.Create("invalid_request", "The Node heartbeat is invalid.",
                    StatusCodes.Status400BadRequest);
            }
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);
        app.MapPost("/v1/node-admissions", async (NodeAdmissionRequest request, HttpContext http,
            UserManager<HunterAccount> users, SignInManager<HunterAccount> signIn, BackendDbContext db,
            NodeDirectory directory, GameTicketIssuer issuer, CancellationToken cancellationToken) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
                return BackendProblem.Create("invalid_credential", "The session is invalid.",
                    StatusCodes.Status401Unauthorized);
            if (request.NodeId == Guid.Empty)
                return BackendProblem.Create("invalid_request", "The Node identity is invalid.",
                    StatusCodes.Status400BadRequest);
            var user = await signIn.ValidateSecurityStampAsync(http.User);
            if (user == null || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
                return BackendProblem.Create("account_unconfirmed", "The account is not eligible for Node access.",
                    StatusCodes.Status403Forbidden);
            if (!issuer.IsConfigured)
                return BackendProblem.Create("admission_unavailable", "Node admission is unavailable.",
                    StatusCodes.Status503ServiceUnavailable);
            var node = directory.FindOnline(request.NodeId);
            if (node == null)
                return BackendProblem.Create("node_not_found", "The Node is not currently available.",
                    StatusCodes.Status404NotFound);
            var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == user.Id, cancellationToken);
            if (profile == null)
                return BackendProblem.Create("node_admission_unavailable", "The Node admission identity is unavailable.",
                    StatusCodes.Status404NotFound);
            return Results.Ok(issuer.IssueNodeAdmission(new PlayerId(user.Id), profile.DisplayName, node.NodeId, node.PublicControlUri));
        }).RequireRateLimiting(BackendRoutePolicy.Auth);

        app.MapPost("/v1/guest-node-admissions",
            (GuestNodeAdmissionRequest request, NodeDirectory directory, GameTicketIssuer issuer) =>
            {
                if (request.NodeId == Guid.Empty || !TryGuestDisplayName(request.DisplayName, out string name))
                    return BackendProblem.Create("invalid_request", "The guest admission request is invalid.",
                        StatusCodes.Status400BadRequest);
                if (!issuer.IsConfigured)
                    return BackendProblem.Create("admission_unavailable", "Node admission is unavailable.",
                        StatusCodes.Status503ServiceUnavailable);
                var node = directory.FindOnline(request.NodeId);
                if (node == null)
                    return BackendProblem.Create("node_not_found", "The Node is not currently available.",
                        StatusCodes.Status404NotFound);
                Guid guestId = Guid.NewGuid();
                return Results.Ok(issuer.IssueGuestNodeAdmission(guestId, name, node.NodeId,
                    node.PublicControlUri));
            }).RequireRateLimiting(BackendRoutePolicy.GuestAuth);
    }

    private static bool TryGuestDisplayName(string? value, out string name)
    {
        name = value?.Trim() ?? "";
        return !string.IsNullOrWhiteSpace(name) && ProfileEndpoints.ValidDisplayName(name);
    }

    private static bool Credentials(HttpContext http, out Guid id, out string secret)
    {
        secret = "";
        if (!Guid.TryParseExact(http.Request.Headers["X-Server-Id"], "D", out id)) return false;
        string value = http.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        secret = value[7..]; return true;
    }
}
