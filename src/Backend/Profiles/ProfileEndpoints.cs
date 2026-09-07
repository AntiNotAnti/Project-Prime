using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Identity;

namespace MphRead.Backend.Profiles;

public sealed record ProfilePatch(string? DisplayName, Hunter? FavoriteHunter);
public sealed record LicenseResponse(PlayerId PlayerId, string DisplayName, Hunter FavoriteHunter, DateTimeOffset JoinedAt);

public static class ProfileEndpoints
{
    public static bool ValidDisplayName(string? name) => name is { Length: >= 1 and <= 16 }
        && name == name.Trim() && name.All(c => c is >= ' ' and <= '~');

    public static void MapProfiles(this WebApplication app)
    {
        app.MapGet("/v1/me", async (HttpContext http, UserManager<HunterAccount> users) =>
        {
            var user = await users.GetUserAsync(http.User);
            return user == null ? Results.Unauthorized() : Results.Ok(new
            {
                PlayerId = new PlayerId(user.Id), user.EmailConfirmed,
                // Future official admission must also check server and match policies.
                EmailEligibleForOfficialPlay = user.EmailConfirmed
            });
        }).RequireAuthorization().RequireRateLimiting("api");

        app.MapPatch("/v1/me/profile", async (ProfilePatch patch, HttpContext http,
            UserManager<HunterAccount> users, BackendDbContext db, CancellationToken cancellationToken) =>
        {
            if ((patch.DisplayName == null && patch.FavoriteHunter == null)
                || (patch.DisplayName != null && !ValidDisplayName(patch.DisplayName))
                || (patch.FavoriteHunter.HasValue && patch.FavoriteHunter.Value > Hunter.Weavel))
            {
                return Results.BadRequest(new { Error = "Supply a valid display name or playable favorite hunter." });
            }
            var user = await users.GetUserAsync(http.User);
            if (user == null) { return Results.Unauthorized(); }
            var profile = await db.Profiles.SingleOrDefaultAsync(x => x.PlayerId == user.Id, cancellationToken);
            if (profile == null) { return Results.NotFound(); }
            if (patch.DisplayName != null) { profile.DisplayName = patch.DisplayName; }
            if (patch.FavoriteHunter.HasValue) { profile.FavoriteHunter = patch.FavoriteHunter.Value; }
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("api");

        app.MapGet("/v1/players/{id}/license", async (string id, BackendDbContext db, CancellationToken cancellationToken) =>
        {
            if (!PlayerId.TryParse(id, out PlayerId playerId)) { return Results.BadRequest(); }
            var result = await (from profile in db.Profiles.AsNoTracking()
                join license in db.Licenses.AsNoTracking() on profile.PlayerId equals license.PlayerId
                where profile.PlayerId == playerId.Value
                select new LicenseResponse(playerId, profile.DisplayName, profile.FavoriteHunter, license.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken);
            return result == null ? Results.NotFound() : Results.Ok(result);
        }).RequireRateLimiting("api");
    }
}
