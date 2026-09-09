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
        app.MapGet("/v1/nodes", (int protocol, string build, string content, NodeDirectory directory) =>
            Results.Ok(directory.Browse(protocol, build, content))).RequireRateLimiting("api");
        app.MapPut("/v1/node/registration", (NodeRegistration value, HttpContext http, NodeDirectory directory) =>
        {
            if (!Credentials(http, out var id, out var secret)) return Results.Unauthorized();
            try { return directory.Register(id, secret, value) ? Results.Ok() : Results.Unauthorized(); }
            catch (ArgumentException) { return Results.BadRequest(); }
        }).RequireRateLimiting("auth");
        app.MapPost("/v1/node/heartbeat", (NodeHeartbeat value, HttpContext http, NodeDirectory directory) =>
        {
            if (!Credentials(http, out var id, out var secret)) return Results.Unauthorized();
            try { return directory.Heartbeat(id, secret, value) ? Results.Ok() : Results.Unauthorized(); }
            catch (ArgumentException) { return Results.BadRequest(); }
        }).RequireRateLimiting("api");
        app.MapPost("/v1/node-admissions", async (NodeAdmissionRequest request, HttpContext http,
            UserManager<HunterAccount> users, SignInManager<HunterAccount> signIn, BackendDbContext db,
            NodeDirectory directory, GameTicketIssuer issuer, CancellationToken cancellationToken) =>
        {
            if (request.NodeId == Guid.Empty) return Results.BadRequest();
            var user = await signIn.ValidateSecurityStampAsync(http.User);
            if (user == null || !user.EmailConfirmed || await users.IsLockedOutAsync(user)) return Results.Forbid();
            if (!issuer.IsConfigured) return Results.StatusCode(503);
            var node = directory.FindOnline(request.NodeId);
            if (node == null) return Results.NotFound();
            var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == user.Id, cancellationToken);
            if (profile == null) return Results.NotFound();
            return Results.Ok(issuer.IssueNodeAdmission(new PlayerId(user.Id), profile.DisplayName, node.NodeId, node.PublicControlUri));
        }).RequireAuthorization().RequireRateLimiting("auth");

        app.MapPost("/v1/guest-node-admissions",
            (GuestNodeAdmissionRequest request, NodeDirectory directory, GameTicketIssuer issuer) =>
            {
                if (request.NodeId == Guid.Empty || !TryGuestDisplayName(request.DisplayName, out string name))
                    return Results.BadRequest();
                if (!issuer.IsConfigured) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                var node = directory.FindOnline(request.NodeId);
                if (node == null) return Results.NotFound();
                Guid guestId = Guid.NewGuid();
                return Results.Ok(issuer.IssueGuestNodeAdmission(guestId, name, node.NodeId,
                    node.PublicControlUri));
            }).RequireRateLimiting("guest-auth");
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
