using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Backend.Matches;
using MphRead.Identity;

namespace MphRead.Backend.Profiles;

public sealed record ProfilePatch(string? DisplayName, Hunter? FavoriteHunter);
public sealed record LicenseResponse(PlayerId PlayerId, string DisplayName, Hunter FavoriteHunter,
    DateTimeOffset JoinedAt, int Points, int Tier, string Title, int? NextThreshold,
    int? LastOfficialDelta, string Policy, Guid? LastOfficialMatchId);

public static class ProfileEndpoints
{
    public static bool ValidDisplayName(string? name) => name is { Length: >= 1 and <= 16 }
        && name == name.Trim() && name.All(c => c is >= ' ' and <= '~');

    public static void MapProfiles(this WebApplication app)
    {
        app.MapGet("/v1/me", async (HttpContext http, UserManager<HunterAccount> users) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
                return BackendProblem.Create("invalid_credential", "The session is invalid.",
                    StatusCodes.Status401Unauthorized);
            var user = await users.GetUserAsync(http.User);
            return user == null ? BackendProblem.Create("invalid_credential", "The session is invalid.",
                StatusCodes.Status401Unauthorized) : Results.Ok(new
            {
                PlayerId = new PlayerId(user.Id), user.EmailConfirmed,
                // Future official admission must also check server and match policies.
                EmailEligibleForOfficialPlay = user.EmailConfirmed
            });
        }).RequireRateLimiting(BackendRoutePolicy.Api);

        app.MapPatch("/v1/me/profile", async (ProfilePatch patch, HttpContext http,
            UserManager<HunterAccount> users, BackendDbContext db, CancellationToken cancellationToken) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
                return BackendProblem.Create("invalid_credential", "The session is invalid.",
                    StatusCodes.Status401Unauthorized);
            if ((patch.DisplayName == null && patch.FavoriteHunter == null)
                || (patch.DisplayName != null && !ValidDisplayName(patch.DisplayName))
                || (patch.FavoriteHunter.HasValue && patch.FavoriteHunter.Value > Hunter.Weavel))
            {
                return BackendProblem.Create("invalid_display_name",
                    "Supply a valid display name or playable favorite hunter.",
                    StatusCodes.Status400BadRequest);
            }
            var user = await users.GetUserAsync(http.User);
            if (user == null)
                return BackendProblem.Create("invalid_credential", "The session is invalid.",
                    StatusCodes.Status401Unauthorized);
            var profile = await db.Profiles.SingleOrDefaultAsync(x => x.PlayerId == user.Id, cancellationToken);
            if (profile == null)
                return BackendProblem.Create("invalid_credential", "The account profile is unavailable.",
                    StatusCodes.Status404NotFound);
            if (patch.DisplayName != null) { profile.DisplayName = patch.DisplayName; }
            if (patch.FavoriteHunter.HasValue) { profile.FavoriteHunter = patch.FavoriteHunter.Value; }
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting(BackendRoutePolicy.Api);

        app.MapGet("/v1/players/{id}/license", async (string id, BackendDbContext db, CancellationToken cancellationToken) =>
        {
            if (!PlayerId.TryParse(id, out PlayerId playerId))
                return BackendProblem.Create("invalid_request", "The player identity is invalid.",
                    StatusCodes.Status400BadRequest);
            var result = await (from profile in db.Profiles.AsNoTracking()
                join license in db.Licenses.AsNoTracking() on profile.PlayerId equals license.PlayerId
                where profile.PlayerId == playerId.Value
                select new { Profile = profile, License = license })
                .SingleOrDefaultAsync(cancellationToken);
            if (result == null)
                return BackendProblem.Create("license_not_found", "The player license was not found.",
                    StatusCodes.Status404NotFound);
            RatingSummary rating = await RatingProjection.ReadSummaryAsync(db, result.License, cancellationToken);
            return Results.Ok(new LicenseResponse(playerId, result.Profile.DisplayName,
                result.Profile.FavoriteHunter, result.License.CreatedAt, rating.Points, rating.Tier,
                rating.Title, rating.NextThreshold, rating.LastOfficialDelta, rating.Policy,
                rating.LastOfficialMatchId));
        }).RequireRateLimiting(BackendRoutePolicy.Api);
    }
}
