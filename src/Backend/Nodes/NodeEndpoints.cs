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
            long? revision, NodeDirectory directory) =>
        {
            try
            {
                return Results.Ok(directory.BrowsePage(protocol, build, content,
                    page ?? 0, revision));
            }
            catch (NodeDirectoryPageException error)
            {
                return BackendProblem.Create(error.Code, error.Message, error.StatusCode);
            }
        }).RequireRateLimiting(BackendRoutePolicy.Api);
        app.MapGet("/v1/node-admission-keys", (HttpContext http, GameTicketIssuer issuer) =>
        {
            // Public verification material is useful only over HTTPS. Local
            // loopback HTTP remains an explicit development seam.
            if (!http.Request.IsHttps && !IPAddress.IsLoopback(http.Connection.LocalIpAddress ?? IPAddress.None))
                return BackendProblem.Create("https_required", "Node admission keys require HTTPS.",
                    StatusCodes.Status400BadRequest);
            return Results.Ok(issuer.PublicKeys);
        }).AllowAnonymous().RequireRateLimiting(BackendRoutePolicy.Api);
        app.MapPut("/v1/node/registration", (NodeRegistration value, HttpContext http, NodeDirectory directory,
            AuthenticatedNodeRateLimiter nodeLimiter) =>
        {
            if (!Credentials(http, out var id, out var secret))
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!directory.Authenticate(id, secret, out _))
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!nodeLimiter.TryAcquire(id, "registration"))
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            try { return directory.Register(id, secret, value) ? Results.Ok() : BackendProblem.Create("invalid_credential", "Node credentials are invalid.", StatusCodes.Status401Unauthorized); }
            catch (ArgumentException)
            {
                return BackendProblem.Create("invalid_request", "The Node registration is invalid.",
                    StatusCodes.Status400BadRequest);
            }
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);
        app.MapDelete("/v1/node/registration", (HttpContext http, NodeDirectory directory,
            AuthenticatedNodeRateLimiter nodeLimiter) =>
        {
            if (!Credentials(http, out var id, out var secret))
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!directory.Authenticate(id, secret, out _))
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!nodeLimiter.TryAcquire(id, "deregistration"))
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            if (!Guid.TryParseExact(http.Request.Query["incarnation"], "D", out Guid incarnation)
                || incarnation == Guid.Empty)
                return BackendProblem.Create("invalid_request", "The Node incarnation is invalid.",
                    StatusCodes.Status400BadRequest);
            directory.Deregister(id, secret, incarnation);
            return Results.NoContent();
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);
        app.MapPost("/v1/node/heartbeat", (NodeHeartbeat value, HttpContext http, NodeDirectory directory,
            AuthenticatedNodeRateLimiter nodeLimiter) =>
        {
            if (!Credentials(http, out var id, out var secret))
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!directory.Authenticate(id, secret, out _))
                return BackendProblem.Create("invalid_credential", "Node credentials are invalid.",
                    StatusCodes.Status401Unauthorized);
            if (!nodeLimiter.TryAcquire(id, "heartbeat"))
                return BackendProblem.Create("rate_limited", "Node update rate limit exceeded.",
                    StatusCodes.Status429TooManyRequests);
            try { return directory.Heartbeat(id, secret, value) ? Results.Ok() : BackendProblem.Create("invalid_credential", "Node credentials are invalid.", StatusCodes.Status401Unauthorized); }
            catch (ArgumentException)
            {
                return BackendProblem.Create("invalid_request", "The Node heartbeat is invalid.",
                    StatusCodes.Status400BadRequest);
            }
        }).RequireRateLimiting(BackendRoutePolicy.MachinePreAuth);
        app.MapPost("/v1/node-admissions", async (NodeAdmissionRequest request, HttpContext http,
            UserManager<HunterAccount> users, SignInManager<HunterAccount> signIn, BackendDbContext db,
            NodeDirectory directory, GameTicketIssuer issuer, CancellationToken cancellationToken) =>
        {
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
        }).RequireAuthorization().RequireRateLimiting(BackendRoutePolicy.Auth);

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
