using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MphRead.Backend.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MphRead.Backend.Tickets;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class BackendRouteContractTests
{
    [Fact]
    public async Task PublicAdmissionKeysAreAnonymousBoundedAndCorrelated()
    {
        using var keys = new TicketTests.TestKeys();
        using var factory = new BackendFactory(configure: services => services.Configure<TicketOptions>(keys.Configure));
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        string requestId = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/node-admission-keys");
        request.Headers.Add("X-Request-Id", requestId);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(requestId, response.Headers.GetValues("X-Request-Id").Single());
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.InRange(body.RootElement.GetArrayLength(), 1, 8);
        JsonElement key = body.RootElement[0];
        Assert.Equal("EC", key.GetProperty("kty").GetString());
        Assert.Equal("P-256", key.GetProperty("crv").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal("ES256", key.GetProperty("alg").GetString());
        Assert.False(key.TryGetProperty("d", out _));
    }

    [Fact]
    public async Task NodeCredentialFailureUsesStableProblemAndMethodMappingIsExplicit()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/node/registration")
        {
            Content = JsonContent.Create(new
            {
                incarnation = Guid.NewGuid(), name = "Node", region = "us",
                publicControlUri = "wss://node.example/v1/control", protocolVersion = 1,
                buildVersion = "build", contentHash = new string('a', 64), capacity = 8
            })
        };
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credential", await ProblemCode(response));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/v1/node/registration", null)).StatusCode);
    }

    [Fact]
    public async Task ChunkedNodeRegistrationOverflowIsRejectedBeforeBindingWithCorrelation()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        string requestId = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/node/registration")
        {
            Content = new ChunkedContent(new string('x', BackendRequestLimits.NodeRegistrationBytes + 1))
        };
        request.Headers.Add("X-Request-Id", requestId);
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(requestId, response.Headers.GetValues("X-Request-Id").Single());
        Assert.Equal("request_too_large", await ProblemCode(response));
    }

    [Fact]
    public async Task AnonymousDirectoryPageHasBoundedDtoAndInvalidPageUsesStableProblem()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient();
        string content = new string('a', 64);
        using HttpResponseMessage response = await client.GetAsync(
            $"/v1/nodes?protocol=1&build=build&content={content}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NodeDirectoryPage page = (await response.Content.ReadFromJsonAsync<NodeDirectoryPage>())!;
        Assert.True(page.Revision > 0);
        Assert.Equal(0, page.Page);
        Assert.Equal(1, page.PageCount);
        Assert.Equal(0, page.TotalEntries);
        Assert.Empty(page.Entries);

        using HttpResponseMessage invalid = await client.GetAsync(
            $"/v1/nodes?protocol=1&build=build&content={content}&page=-1");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_page", await ProblemCode(invalid));
    }

    [Fact]
    public async Task AuthAndGuestRoutesExposeStableStatusAndDtoContracts()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient();

        using HttpResponseMessage me = await client.GetAsync("/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using HttpResponseMessage admission = await client.PostAsJsonAsync(
            "/v1/node-admissions", new { nodeId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, admission.StatusCode);
        using HttpResponseMessage guest = await client.PostAsJsonAsync(
            "/v1/guest-node-admissions", new { nodeId = Guid.Empty, displayName = "Guest" });
        Assert.Equal(HttpStatusCode.BadRequest, guest.StatusCode);
        Assert.Equal("invalid_request", await ProblemCode(guest));
        using HttpResponseMessage report = await client.PostAsync("/v1/server/matches", null);
        Assert.Equal(HttpStatusCode.Unauthorized, report.StatusCode);
        Assert.Equal("invalid_credential", await ProblemCode(report));
    }

    [Fact]
    public async Task AccountProfileDirectoryAndExportRoutesExposeStableErrorContracts()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();

        using HttpResponseMessage register = await client.PostAsJsonAsync("/v1/auth/register",
            new { Email = "", Password = "", DisplayName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);
        Assert.Equal("invalid_email", await ProblemCode(register));

        using HttpResponseMessage login = await client.PostAsJsonAsync("/v1/auth/login",
            new { Email = "", Password = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("invalid_credential", await ProblemCode(login));

        using HttpResponseMessage refresh = await client.PostAsJsonAsync("/v1/auth/refresh",
            new { RefreshToken = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("invalid_refresh", await ProblemCode(refresh));

        using HttpResponseMessage confirm = await client.PostAsJsonAsync("/v1/auth/confirm-email",
            new { PlayerId = Guid.Empty, Code = "" });
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal("invalid_confirmation", await ProblemCode(confirm));

        using HttpResponseMessage resend = await client.PostAsJsonAsync("/v1/auth/resend-confirmation",
            new { Email = "not-an-email" });
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
        Assert.Equal("invalid_email", await ProblemCode(resend));

        using HttpResponseMessage revoke = await client.PostAsync("/v1/auth/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
        Assert.Equal("invalid_credential", await ProblemCode(revoke));

        using HttpResponseMessage profile = await client.PatchAsJsonAsync("/v1/me/profile",
            new { DisplayName = "Pilot" });
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        Assert.Equal("invalid_credential", await ProblemCode(profile));

        using HttpResponseMessage license = await client.GetAsync("/v1/players/not-a-guid/license");
        Assert.Equal(HttpStatusCode.BadRequest, license.StatusCode);
        Assert.Equal("invalid_request", await ProblemCode(license));

        using HttpResponseMessage export = await client.GetAsync(
            "/v1/matches/00000000-0000-0000-0000-000000000000/export");
        Assert.Equal(HttpStatusCode.NotFound, export.StatusCode);
        Assert.Equal("invalid_request", await ProblemCode(export));

        using HttpResponseMessage ranked = await client.GetAsync("/v1/ranked-availability");
        Assert.Equal(HttpStatusCode.OK, ranked.StatusCode);
        using JsonDocument rankedBody = JsonDocument.Parse(await ranked.Content.ReadAsStringAsync());
        Assert.True(rankedBody.RootElement.TryGetProperty("available", out _));

        using HttpResponseMessage page = await client.GetAsync(
            "/v1/nodes?protocol=1&build=build&content=" + new string('a', 64));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.InRange((await page.Content.ReadAsByteArrayAsync()).Length, 1, 64 * 1024);
    }

    [Fact]
    public async Task AuthenticatedNodeRoutesRoundTripRegistrationHeartbeatAndDeregistration()
    {
        const string secret = "route-contract-node-secret-at-least-thirty-two";
        Guid nodeId = Guid.NewGuid();
        using var factory = new BackendFactory(configure: services => services.Configure<GameServerOptions>(options =>
            options.Servers.Add(new GameServerRegistration
            {
                Id = nodeId, Enabled = true,
                ApiKeySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(secret)))
            })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        client.DefaultRequestHeaders.Add("X-Server-Id", nodeId.ToString("D"));
        var registration = new NodeRegistration(Guid.NewGuid(), "Node", "us", "wss://node.example/v1/control",
            1, "build", new string('a', 64), 100, 7, 1, new string('b', 64));
        using HttpResponseMessage registered = await client.PutAsJsonAsync("/v1/node/registration", registration);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using HttpResponseMessage heartbeat = await client.PostAsJsonAsync("/v1/node/heartbeat",
            new NodeHeartbeat(registration.Incarnation, 2, 1, 0));
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        using HttpResponseMessage listingResponse = await client.GetAsync(
            $"/v1/nodes?protocol=1&build=build&content={registration.ContentHash}");
        NodeDirectoryPage listing = (await listingResponse.Content.ReadFromJsonAsync<NodeDirectoryPage>())!;
        NodeDirectoryEntry entry = Assert.Single(listing.Entries);
        Assert.Equal(registration.MapCatalogRevision, entry.MapCatalogRevision);
        Assert.Equal(registration.MapCount, entry.MapCount);
        Assert.Equal(registration.MapCatalogHash, entry.MapCatalogHash);
        using HttpResponseMessage removed = await client.DeleteAsync(
            $"/v1/node/registration?incarnation={registration.Incarnation:D}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using HttpResponseMessage after = await client.GetAsync(
            $"/v1/nodes?protocol=1&build=build&content={registration.ContentHash}");
        NodeDirectoryPage afterPage = (await after.Content.ReadFromJsonAsync<NodeDirectoryPage>())!;
        Assert.Empty(afterPage.Entries);
    }

    [Theory]
    [InlineData("GET", "/v1/auth/register")]
    [InlineData("GET", "/v1/auth/login")]
    [InlineData("GET", "/v1/auth/refresh")]
    [InlineData("GET", "/v1/auth/confirm-email")]
    [InlineData("GET", "/v1/auth/resend-confirmation")]
    [InlineData("GET", "/v1/auth/revoke-sessions")]
    [InlineData("POST", "/v1/me")]
    [InlineData("POST", "/v1/me/profile")]
    [InlineData("POST", "/v1/nodes")]
    [InlineData("POST", "/v1/node-admission-keys")]
    [InlineData("GET", "/v1/node-admissions")]
    [InlineData("GET", "/v1/guest-node-admissions")]
    [InlineData("GET", "/v1/server/matches")]
    [InlineData("POST", "/v1/matches/00000000-0000-0000-0000-000000000000/export")]
    [InlineData("POST", "/v1/ranked-availability")]
    public async Task RouteMethodsAreExplicitAndRetiredPathsRemainUnmapped(string method, string path)
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task RetiredDirectRoutesRemain404InCanonicalContractSuite()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient();
        foreach (string path in new[] { "/v1/server/session", "/v1/game-tickets", "/v1/game-ticket-keys" })
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private sealed class ChunkedContent(string value) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(value);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => await stream.WriteAsync(_bytes);
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
