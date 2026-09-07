using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Identity;

namespace MphRead.Backend.Tickets;

public sealed record GameTicketRequest(Guid ServerId, string Nonce);
public sealed record ServerSessionRequest(Guid ServerIncarnation);

public static class TicketEndpoints
{
    public static void MapGameTickets(this WebApplication app)
    {
        app.MapGet("/v1/game-ticket-keys", (GameTicketIssuer issuer) =>
            issuer.IsConfigured ? Results.Ok(new { Keys = issuer.PublicKeys })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).RequireRateLimiting("api");
        app.MapPut("/v1/server/session", (ServerSessionRequest request, HttpContext http, GameServerRegistry registry) =>
        {
            string authorization = http.Request.Headers.Authorization.ToString();
            if (!Guid.TryParseExact(http.Request.Headers["X-Server-Id"], "D", out var serverId)
                || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                || !registry.TryRegister(serverId, authorization[7..], request.ServerIncarnation))
                return Results.Unauthorized();
            return Results.Ok(new { ServerId = serverId, request.ServerIncarnation });
        }).RequireRateLimiting("auth");
        app.MapPost("/v1/game-tickets", async (GameTicketRequest request, HttpContext http,
            UserManager<HunterAccount> users, SignInManager<HunterAccount> signIn, BackendDbContext db, GameServerRegistry registry,
            GameTicketIssuer issuer, CancellationToken cancellationToken) =>
        {
            if (request.ServerId == Guid.Empty || !ulong.TryParse(request.Nonce, NumberStyles.None,
                CultureInfo.InvariantCulture, out ulong nonce) || nonce == 0
                || request.Nonce != nonce.ToString(CultureInfo.InvariantCulture)) return Results.BadRequest();
            var user = await signIn.ValidateSecurityStampAsync(http.User);
            // The private-test login bypass never permits unconfirmed game tickets.
            if (user == null || !user.EmailConfirmed || await users.IsLockedOutAsync(user)) return Results.Forbid();
            if (!issuer.IsConfigured) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!registry.TryGetTicketDestination(request.ServerId, out var incarnation, out var address, out int port)) return Results.NotFound();
            var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == user.Id, cancellationToken);
            if (profile == null) return Results.NotFound();
            return Results.Ok(issuer.Issue(new PlayerId(user.Id), profile.DisplayName, request.ServerId, incarnation, nonce, address, port));
        }).RequireAuthorization().RequireRateLimiting("auth");
    }
}
