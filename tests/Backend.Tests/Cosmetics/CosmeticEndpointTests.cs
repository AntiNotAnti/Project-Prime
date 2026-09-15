using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MphRead.Backend.Cosmetics;
using MphRead.Backend.Data;
using MphRead.Cosmetics;
using Xunit;

namespace MphRead.Backend.Tests.Cosmetics;

public sealed class CosmeticEndpointTests
{
    private const string Password = "Test-Only-Strong123!";
    [Fact]
    public async Task CosmeticRoutesRequireAnAuthenticatedAccount()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/v1/me/cosmetics")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/v1/me/cosmetics/Samus")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/v1/me/cosmetics/Samus", Request(Hunter.Samus))).StatusCode);
    }

    [Fact]
    public async Task NewAccountReadsOneDefaultLoadoutPerPlayableHunterWithoutCreatingRows()
    {
        using var factory = new BackendFactory();
        using var client = await AuthenticatedClient(factory);

        CosmeticLoadoutResponse[] all = (await client.GetFromJsonAsync<CosmeticLoadoutResponse[]>(
            "/v1/me/cosmetics"))!;
        Assert.Equal(8, all.Length);
        Assert.Collection(all,
            item => AssertDefault(item, Hunter.Samus), item => AssertDefault(item, Hunter.Kanden),
            item => AssertDefault(item, Hunter.Trace), item => AssertDefault(item, Hunter.Sylux),
            item => AssertDefault(item, Hunter.Noxus), item => AssertDefault(item, Hunter.Spire),
            item => AssertDefault(item, Hunter.Weavel), item => AssertDefault(item, Hunter.Guardian));

        CosmeticLoadoutResponse samus = (await client.GetFromJsonAsync<CosmeticLoadoutResponse>(
            "/v1/me/cosmetics/samus"))!;
        AssertDefault(samus, Hunter.Samus);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BackendDbContext>()
            .CosmeticLoadouts.ToListAsync());
    }

    [Fact]
    public async Task PutPersistsAndAtomicallyUpdatesOneRowPerHunter()
    {
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        using var factory = new BackendFactory(configure: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        using var client = await AuthenticatedClient(factory);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/v1/me/cosmetics/Samus", new
        {
            SkinKey = "prime.skin.samus.obsidian",
            ArmorEffectKey = CosmeticKeys.NoArmorEffect,
            DeathEffectKey = CosmeticKeys.DefaultDeathEffect
        })).StatusCode);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/v1/me/cosmetics/0", new
        {
            SkinKey = "prime.skin.samus.solar",
            ArmorEffectKey = CosmeticKeys.NoArmorEffect,
            DeathEffectKey = CosmeticKeys.DefaultDeathEffect
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/me/cosmetics/Kanden", Request(Hunter.Kanden))).StatusCode);

        using var scope = factory.Services.CreateScope();
        PlayerCosmeticLoadout[] rows = await scope.ServiceProvider.GetRequiredService<BackendDbContext>()
            .CosmeticLoadouts.OrderBy(x => x.Hunter).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.Equal(clock.GetUtcNow(), rows[0].UpdatedAt);
        Assert.Equal(Hunter.Samus, rows[0].Hunter);
        Assert.Equal("prime.skin.samus.solar", rows[0].SkinKey);
        Assert.Equal(Hunter.Kanden, rows[1].Hunter);
    }

    [Fact]
    public async Task CosmeticSelectionsAreIsolatedByAuthenticatedPlayer()
    {
        using var factory = new BackendFactory();
        using var first = await AuthenticatedClient(factory, "first-cosmetics@example.test");
        using var second = await AuthenticatedClient(factory, "second-cosmetics@example.test");
        Assert.Equal(HttpStatusCode.OK, (await first.PutAsJsonAsync("/v1/me/cosmetics/Samus", new
        {
            SkinKey = "prime.skin.samus.obsidian",
            ArmorEffectKey = CosmeticKeys.NoArmorEffect,
            DeathEffectKey = CosmeticKeys.DefaultDeathEffect
        })).StatusCode);

        CosmeticLoadoutResponse firstLoadout = (await first.GetFromJsonAsync<CosmeticLoadoutResponse>(
            "/v1/me/cosmetics/Samus"))!;
        CosmeticLoadoutResponse secondLoadout = (await second.GetFromJsonAsync<CosmeticLoadoutResponse>(
            "/v1/me/cosmetics/Samus"))!;
        Assert.Equal("prime.skin.samus.obsidian", firstLoadout.SkinKey);
        Assert.Equal(CosmeticCatalog.DefaultSkinKey(Hunter.Samus), secondLoadout.SkinKey);
    }

    [Theory]
    [InlineData("Random")]
    [InlineData("255")]
    [InlineData("unknown")]
    public async Task PutRejectsNonPlayableHunter(string hunter)
    {
        using var factory = new BackendFactory();
        using var client = await AuthenticatedClient(factory);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/v1/me/cosmetics/{hunter}", Request(Hunter.Samus))).StatusCode);
    }

    [Fact]
    public async Task GuardianCosmeticRoundTripIsOfficial()
    {
        using var factory = new BackendFactory();
        using var client = await AuthenticatedClient(factory);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/me/cosmetics/Guardian",
                Request(Hunter.Guardian))).StatusCode);
        CosmeticLoadoutResponse response =
            (await client.GetFromJsonAsync<CosmeticLoadoutResponse>(
                "/v1/me/cosmetics/Guardian"))!;
        AssertDefault(response, Hunter.Guardian);
    }

    [Fact]
    public async Task PutRejectsUnknownMismatchedAndOversizedCatalogKeysWithoutMutation()
    {
        using var factory = new BackendFactory();
        using var client = await AuthenticatedClient(factory);

        object[] invalid =
        [
            new { SkinKey = "prime.skin.samus.unknown", ArmorEffectKey = CosmeticKeys.NoArmorEffect, DeathEffectKey = CosmeticKeys.DefaultDeathEffect },
            Request(Hunter.Kanden),
            new { SkinKey = CosmeticCatalog.DefaultSkinKey(Hunter.Samus), ArmorEffectKey = "prime.armor_fx.unknown", DeathEffectKey = CosmeticKeys.DefaultDeathEffect },
            new { SkinKey = CosmeticCatalog.DefaultSkinKey(Hunter.Samus), ArmorEffectKey = CosmeticKeys.NoArmorEffect, DeathEffectKey = "prime.death.unknown" },
            new { SkinKey = new string('x', 97), ArmorEffectKey = CosmeticKeys.NoArmorEffect, DeathEffectKey = CosmeticKeys.DefaultDeathEffect }
        ];
        foreach (object request in invalid)
        {
            using HttpResponseMessage response = await client.PutAsJsonAsync("/v1/me/cosmetics/Samus", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_cosmetic_loadout", await ProblemCode(response));
        }
        using HttpResponseMessage restricted = await client.PutAsJsonAsync(
            "/v1/me/cosmetics/Kanden", new
            {
                SkinKey = CosmeticCatalog.DefaultSkinKey(Hunter.Kanden),
                ArmorEffectKey = CosmeticKeys.NoArmorEffect,
                DeathEffectKey = "prime.death.samus_backward_collapse"
            });
        Assert.Equal(HttpStatusCode.BadRequest, restricted.StatusCode);
        Assert.Equal("invalid_cosmetic_loadout", await ProblemCode(restricted));

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BackendDbContext>()
            .CosmeticLoadouts.ToListAsync());
    }

    [Fact]
    public async Task PutUsesTheDefaultJsonBodyBound()
    {
        using var factory = new BackendFactory();
        using var client = await AuthenticatedClient(factory);
        using var response = await client.PutAsJsonAsync("/v1/me/cosmetics/Samus", new
        {
            SkinKey = new string('x', BackendRequestLimits.DefaultJsonBytes),
            ArmorEffectKey = CosmeticKeys.NoArmorEffect,
            DeathEffectKey = CosmeticKeys.DefaultDeathEffect
        });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request_too_large", await ProblemCode(response));
    }

    [Fact]
    public async Task DeletingAnAccountCascadesItsCosmeticLoadouts()
    {
        using var factory = new BackendFactory();
        using var client = await AuthenticatedClient(factory);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/me/cosmetics/Samus", Request(Hunter.Samus))).StatusCode);

        using var scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        db.Users.Remove(await db.Users.SingleAsync());
        await db.SaveChangesAsync();
        Assert.Empty(await db.CosmeticLoadouts.ToListAsync());
    }

    private static object Request(Hunter hunter) => new
    {
        SkinKey = CosmeticCatalog.DefaultSkinKey(hunter),
        ArmorEffectKey = CosmeticKeys.NoArmorEffect,
        DeathEffectKey = CosmeticKeys.DefaultDeathEffect
    };

    private static async Task<HttpClient> AuthenticatedClient(BackendFactory factory,
        string email = "cosmetics@example.test")
    {
        HttpClient client = factory.CreateDatabaseClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/auth/register", new
        {
            Email = email, Password, DisplayName = "Cosmetics"
        })).StatusCode);
        using HttpResponseMessage login = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            Email = email, Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        JsonElement tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private static void AssertDefault(CosmeticLoadoutResponse response, Hunter hunter)
    {
        Assert.Equal(hunter.ToString(), response.Hunter);
        Assert.Equal(CosmeticCatalog.DefaultSkinKey(hunter), response.SkinKey);
        Assert.Equal(CosmeticKeys.NoArmorEffect, response.ArmorEffectKey);
        Assert.Equal(CosmeticKeys.DefaultDeathEffect, response.DeathEffectKey);
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}
