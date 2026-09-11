using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using MphRead.Backend.Data;
using MphRead.Backend.Nodes;
using MphRead.Backend.Tickets;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class GuestAdmissionTests
{
    private const string Issuer = "https://backend.example.test";
    private const string Secret = "guest-test-node-secret-at-least-thirty-two-characters";
    private static readonly Guid NodeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task GuestEndpointIsMappedByDefaultAndUsesIpRateLimit()
    {
        using var factory = new BackendFactory(configure: ConfigureNode);
        using var client = CreateHttpsClient(factory);
        await RegisterNodeAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/v1/guest-node-admissions",
                new { NodeId, DisplayName = "Guest" })).StatusCode);

        for (int i = 1; i < 20; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/guest-node-admissions")
            {
                Content = JsonContent.Create(new { NodeId, DisplayName = "Guest" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"different-{i}");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/v1/guest-node-admissions",
                new { NodeId, DisplayName = "Guest" })).StatusCode);
    }

    [Fact]
    public async Task GuestAdmissionRejectsInvalidNodeAndDisplayName()
    {
        using var keys = new TicketTests.TestKeys();
        using var factory = CreateFactory(keys, configureNode: true);
        using var client = CreateHttpsClient(factory);
        await RegisterNodeAsync(client);

        foreach (string displayName in new[] { "", "   ", "\t\n", "é", new string('x', 17) })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/v1/guest-node-admissions",
                    new { NodeId, DisplayName = displayName })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/v1/guest-node-admissions",
                new { NodeId = Guid.Empty, DisplayName = "Guest" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/v1/guest-node-admissions",
                new { NodeId = Guid.NewGuid(), DisplayName = "Guest" })).StatusCode);
    }

    [Fact]
    public async Task GuestAdmissionIssuesDistinctTypedTicketsWithoutCreatingAccounts()
    {
        using var keys = new TicketTests.TestKeys();
        using var factory = CreateFactory(keys, configureNode: true);
        using var client = CreateHttpsClient(factory);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<BackendDbContext>().Database.EnsureCreated();
        }

        await RegisterNodeAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/v1/node-admissions", new { NodeId })).StatusCode);

        var first = await IssueGuestAsync(client, "  Guest One  ");
        var second = await IssueGuestAsync(client, "Guest Two");
        var firstJwt = new JsonWebToken(first.Ticket);
        var secondJwt = new JsonWebToken(second.Ticket);

        Assert.Equal(NodeId, first.NodeId);
        Assert.Equal("wss://node.example/v1/control", first.PublicControlUri);
        Assert.Equal("Guest One", firstJwt.GetClaim("name").Value);
        Assert.Equal("guest", firstJwt.GetClaim("kind").Value);
        Assert.Equal("pp-node-admission+jwt", firstJwt.Typ);
        Assert.Equal("ES256", firstJwt.Alg);
        Assert.Equal(Issuer, firstJwt.Issuer);
        Assert.Equal("urn:project-prime:node:" + NodeId.ToString("D"),
            Assert.Single(firstJwt.Audiences));
        Assert.Equal(120, (firstJwt.ValidTo - firstJwt.IssuedAt).TotalSeconds);
        Assert.Equal(firstJwt.IssuedAt, firstJwt.ValidFrom);
        Assert.True(Guid.TryParseExact(firstJwt.Subject, "D", out Guid firstGuestId));
        Assert.NotEqual(Guid.Empty, firstGuestId);
        Assert.True(Guid.TryParseExact(secondJwt.Subject, "D", out Guid secondGuestId));
        Assert.NotEqual(Guid.Empty, secondGuestId);
        Assert.NotEqual(firstGuestId, secondGuestId);
        Assert.True(Guid.TryParseExact(firstJwt.Id, "D", out _));
        Assert.True(Guid.TryParseExact(secondJwt.Id, "D", out _));
        Assert.NotEqual(firstJwt.Id, secondJwt.Id);

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BackendDbContext>();
        Assert.Equal(0, db.Users.Count());
        Assert.Equal(0, db.Profiles.Count());
    }

    private static BackendFactory CreateFactory(TicketTests.TestKeys keys, bool configureNode)
        => new(configure: services =>
        {
            services.Configure<TicketOptions>(keys.Configure);
            if (configureNode) ConfigureNode(services);
        });

    private static void ConfigureNode(IServiceCollection services)
        => services.Configure<GameServerOptions>(options => options.Servers.Add(new GameServerRegistration
        {
            Id = NodeId,
            Enabled = true,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
        }));

    private static HttpClient CreateHttpsClient(BackendFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task RegisterNodeAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Server-Id", NodeId.ToString("D"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        var registration = new NodeRegistration(Guid.NewGuid(), "Node", "us-central",
            "wss://node.example/v1/control", 1, "build1", new string('a', 64), 100);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/node/registration", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/v1/node/heartbeat",
                new NodeHeartbeat(registration.Incarnation, 0, 0, 0))).StatusCode);
    }

    private static async Task<NodeAdmissionResponse> IssueGuestAsync(HttpClient client, string displayName)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/v1/guest-node-admissions", new { NodeId, DisplayName = displayName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NodeAdmissionResponse>())!;
    }
}
