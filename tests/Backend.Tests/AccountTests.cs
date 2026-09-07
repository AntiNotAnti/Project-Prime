using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Data;
using MphRead.Identity;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class AccountTests
{
    private const string Password = "Test-Only-Strong123!";
    private static object Registration(string email = "first@example.test", string name = "First")
        => new { Email = email, Password, DisplayName = name };

    private static async Task<JsonElement> Login(HttpClient client, string email = "first@example.test")
    {
        var response = await client.PostAsJsonAsync("/v1/auth/login", new { Email = email, Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task RegistrationCreatesOneIdentityProfileAndLicenseWithHashedPassword()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        var response = await client.PostAsJsonAsync("/v1/auth/register", Registration());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var playerId = PlayerId.Parse(body.GetProperty("playerId").GetString()!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.Equal(playerId.Value, user.Id);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<HunterAccount>>()
                .VerifyHashedPassword(user, user.PasswordHash!, Password));
        Assert.Equal(user.Id, (await db.Profiles.SingleAsync()).PlayerId);
        Assert.Equal(user.Id, (await db.Licenses.SingleAsync()).PlayerId);
        Assert.False(user.EmailConfirmed);
        response = await client.PostAsJsonAsync("/v1/auth/register", Registration("FIRST@example.test", "Other"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Profiles.CountAsync());
        Assert.Equal(1, await db.Licenses.CountAsync());
    }

    [Fact]
    public async Task AuthenticatedProfileCannotChooseAnotherOwnerOrMutateRating()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        var first = await client.PostAsJsonAsync("/v1/auth/register", Registration());
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetString();
        var second = await client.PostAsJsonAsync("/v1/auth/register", Registration("second@example.test", "Second"));
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync("/v1/me/profile", new { DisplayName = "Renamed" })).StatusCode);
        var tokens = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync("/v1/me/profile", new { DisplayName = "Renamed", FavoriteHunter = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/v1/me/profile", new { PlayerId = secondId, DisplayName = "Stolen" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/v1/me/profile", new { RankingPoints = 850 })).StatusCode);
        string license = await client.GetStringAsync($"/v1/players/{firstId}/license");
        Assert.Contains("Renamed", license);
        Assert.DoesNotContain("example.test", license);
        Assert.DoesNotContain("password", license, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Second", await client.GetStringAsync($"/v1/players/{secondId}/license"));
        var me = await client.GetFromJsonAsync<JsonElement>("/v1/me");
        Assert.Equal(firstId, me.GetProperty("playerId").GetString());
        Assert.False(me.GetProperty("emailEligibleForOfficialPlay").GetBoolean());
    }

    [Fact]
    public async Task RequiredConfirmationBlocksLoginUntilRealIdentityTokenIsConfirmed()
    {
        using var factory = new BackendFactory(requireConfirmation: true);
        using var client = factory.CreateDatabaseClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/auth/register", Registration())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/login", new { Email = "first@example.test", Password })).StatusCode);
        var email = Assert.Single(factory.Email.Sent);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/auth/confirm-email", new { email.PlayerId, Code = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/v1/auth/confirm-email", new { email.PlayerId, email.Code })).StatusCode);
        var token = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/v1/me")).GetProperty("emailEligibleForOfficialPlay").GetBoolean());
    }

    [Fact]
    public async Task MissingEmailDeliveryFailsClosedBeforeRegistration()
    {
        using var factory = new BackendFactory(requireConfirmation: true);
        factory.Email.IsConfigured = false;
        using var client = factory.CreateDatabaseClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/v1/auth/register", Registration())).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<BackendDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task RefreshUsesFrameworkTokensAndRevocationInvalidatesRefresh()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        await client.PostAsJsonAsync("/v1/auth/register", Registration());
        var tokens = await Login(client);
        string refresh = tokens.GetProperty("refreshToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/refresh", new { RefreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/refresh", new { RefreshToken = "invalid" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/v1/auth/revoke-sessions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/refresh", new { RefreshToken = refresh })).StatusCode);
    }

    [Fact]
    public async Task PasswordFailuresLockTheAccountWithoutDisclosure()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        await client.PostAsJsonAsync("/v1/auth/register", Registration());
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/login", new { Email = "first@example.test", Password = "Wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/login", new { Email = "first@example.test", Password })).StatusCode);
    }

    [Theory]
    [InlineData("bad-address", "Good", Password)]
    [InlineData("first@example.test", "", Password)]
    [InlineData("first@example.test", "longer-than-sixteen", Password)]
    [InlineData("first@example.test", " Good", Password)]
    [InlineData("first@example.test", "Good", "weak")]
    public async Task InvalidRegistrationDoesNotLeavePartialRows(string email, string displayName, string password)
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/auth/register", new { email, displayName, password })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Profiles.CountAsync());
    }

    [Fact]
    public async Task RequestLimitsAndInvalidIdsAreEnforced()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/v1/auth/register",
            new StringContent(new string('x', 17000), Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/players/00000000-0000-0000-0000-000000000000/license")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/auth/confirm-email", new { Code = "x" })).StatusCode);
        for (int i = 0; i < 20; i++) await client.PostAsJsonAsync("/v1/auth/login", new { Email = "bad", Password = "bad" });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/v1/auth/login", new { Email = "bad", Password = "bad" })).StatusCode);
    }
}
