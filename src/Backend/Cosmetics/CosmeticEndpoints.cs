using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Cosmetics;

namespace MphRead.Backend.Cosmetics;

public sealed record CosmeticLoadoutRequest(
    string? SkinKey,
    string? ArmorEffectKey,
    string? DeathEffectKey);

public sealed record CosmeticLoadoutResponse(
    string Hunter,
    string SkinKey,
    string ArmorEffectKey,
    string DeathEffectKey);

public static class CosmeticEndpoints
{
    private const int MaximumKeyLength = 96;
    public static void MapCosmetics(this WebApplication app)
    {
        RouteGroupBuilder cosmetics = app.MapGroup("/v1/me/cosmetics")
            .RequireAuthorization()
            .RequireRateLimiting(BackendRoutePolicy.Api);

        cosmetics.MapGet("/", GetAll).Bodyless();
        cosmetics.MapGet("/{hunter}", GetOne).Bodyless();
        cosmetics.MapPut("/{hunter}", Put);
    }

    private static async Task<IResult> GetAll(HttpContext http, UserManager<HunterAccount> users,
        BackendDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.CosmeticsCategory);
        HunterAccount? user = await users.GetUserAsync(http.User);
        if (user == null) return InvalidCredential(logger);

        var stored = await db.CosmeticLoadouts.AsNoTracking()
            .Where(x => x.PlayerId == user.Id)
            .ToDictionaryAsync(x => x.Hunter, cancellationToken);
        CosmeticLoadoutResponse[] result = PlayableHunters()
            .Select(hunter => Response(hunter, stored.TryGetValue(hunter, out PlayerCosmeticLoadout? row)
                ? PersistedOrDefault(row, hunter)
                : DefaultLoadout(hunter)))
            .ToArray();
        return Results.Ok(result);
    }

    private static async Task<IResult> GetOne(string hunter, HttpContext http,
        UserManager<HunterAccount> users, BackendDbContext db, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.CosmeticsCategory);
        if (!TryParsePlayableHunter(hunter, out Hunter parsed)) return InvalidHunter(logger);
        HunterAccount? user = await users.GetUserAsync(http.User);
        if (user == null) return InvalidCredential(logger);

        PlayerCosmeticLoadout? stored = await db.CosmeticLoadouts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlayerId == user.Id && x.Hunter == parsed, cancellationToken);
        return Results.Ok(Response(parsed, stored == null
            ? DefaultLoadout(parsed)
            : PersistedOrDefault(stored, parsed)));
    }

    private static async Task<IResult> Put(string hunter, CosmeticLoadoutRequest request,
        HttpContext http, UserManager<HunterAccount> users, BackendDbContext db,
        TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger(BackendDiagnostics.CosmeticsCategory);
        if (!TryParsePlayableHunter(hunter, out Hunter parsed)) return InvalidHunter(logger);
        if (!Bounded(request.SkinKey) || !Bounded(request.ArmorEffectKey)
            || !Bounded(request.DeathEffectKey))
        {
            return InvalidLoadout(logger);
        }

        var requested = new CosmeticLoadout(request.SkinKey!, request.ArmorEffectKey!, request.DeathEffectKey!);
        if (!CosmeticCatalog.BuiltIn.TryResolve(requested, parsed, out _, out _))
            return InvalidLoadout(logger);

        HunterAccount? user = await users.GetUserAsync(http.User);
        if (user == null) return InvalidCredential(logger);

        await UpsertAsync(db, user.Id, parsed, requested, clock.GetUtcNow(), cancellationToken);
        return Results.Ok(Response(parsed, requested));
    }

    private static async Task UpsertAsync(BackendDbContext db, Guid playerId, Hunter hunter,
        CosmeticLoadout loadout, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO prime.player_cosmetic_loadouts
                    (player_id, hunter, skin_key, armor_effect_key, death_effect_key, updated_at)
                VALUES
                    ({playerId}, {(short)hunter}, {loadout.SkinKey}, {loadout.ArmorEffectKey}, {loadout.DeathEffectKey}, {updatedAt})
                ON CONFLICT (player_id, hunter) DO UPDATE SET
                    skin_key = EXCLUDED.skin_key,
                    armor_effect_key = EXCLUDED.armor_effect_key,
                    death_effect_key = EXCLUDED.death_effect_key,
                    updated_at = EXCLUDED.updated_at
                """, cancellationToken);
            return;
        }

        // Focused HTTP tests use SQLite; it supports the same one-statement
        // conflict update while keeping production's PostgreSQL path explicit.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO player_cosmetic_loadouts
                (player_id, hunter, skin_key, armor_effect_key, death_effect_key, updated_at)
            VALUES
                ({playerId}, {(short)hunter}, {loadout.SkinKey}, {loadout.ArmorEffectKey}, {loadout.DeathEffectKey}, {updatedAt})
            ON CONFLICT (player_id, hunter) DO UPDATE SET
                skin_key = excluded.skin_key,
                armor_effect_key = excluded.armor_effect_key,
                death_effect_key = excluded.death_effect_key,
                updated_at = excluded.updated_at
            """, cancellationToken);
    }

    private static bool TryParsePlayableHunter(string value, out Hunter hunter)
        => Enum.TryParse(value, ignoreCase: true, out hunter)
            && PlayableHunterCatalog.IsPlayable(hunter);

    private static bool Bounded(string? value) => value is { Length: >= 1 and <= MaximumKeyLength };

    private static IEnumerable<Hunter> PlayableHunters()
    {
        foreach (Hunter hunter in PlayableHunterCatalog.All) yield return hunter;
    }

    private static CosmeticLoadout DefaultLoadout(Hunter hunter)
        => CosmeticLoadout.DefaultFor(hunter);

    private static CosmeticLoadout PersistedOrDefault(PlayerCosmeticLoadout stored, Hunter hunter)
    {
        var loadout = new CosmeticLoadout(stored.SkinKey, stored.ArmorEffectKey, stored.DeathEffectKey);
        return CosmeticCatalog.BuiltIn.TryResolve(loadout, hunter, out _, out _)
            ? loadout : DefaultLoadout(hunter);
    }

    private static CosmeticLoadoutResponse Response(Hunter hunter, CosmeticLoadout loadout)
        => new(hunter.ToString(), loadout.SkinKey, loadout.ArmorEffectKey, loadout.DeathEffectKey);

    private static IResult InvalidCredential(ILogger logger)
    {
        BackendDiagnostics.Rejected(logger, "cosmetics", "invalid_credential");
        return BackendProblem.Create("invalid_credential", "The session is invalid.",
            StatusCodes.Status401Unauthorized);
    }

    private static IResult InvalidHunter(ILogger logger)
    {
        BackendDiagnostics.Rejected(logger, "cosmetics", "invalid_hunter");
        return BackendProblem.Create("invalid_hunter", "The Hunter is invalid.",
            StatusCodes.Status400BadRequest);
    }

    private static IResult InvalidLoadout(ILogger logger)
    {
        BackendDiagnostics.Rejected(logger, "cosmetics", "invalid_loadout");
        return BackendProblem.Create("invalid_cosmetic_loadout", "The cosmetic loadout is invalid.",
            StatusCodes.Status400BadRequest);
    }
}
