using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Nodes;
using MphRead.Backend.Tickets;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class PresenceEndpointTests
{
    private const string Secret = "presence-endpoint-node-secret-at-least-thirty-two";

    [Fact]
    public async Task PublicPresenceIsAnonymousAndNodeReportsAreAuthenticated()
    {
        Guid nodeId = Guid.NewGuid();
        using var factory = new BackendFactory(configure: services =>
            services.Configure<GameServerOptions>(options => options.Servers.Add(new GameServerRegistration
            {
                Id = nodeId,
                Enabled = true,
                ApiKeySha256 = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(Secret)))
            })));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage emptyResponse = await client.GetAsync("/v1/presence");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        PresenceDirectoryPage empty = (await emptyResponse.Content
            .ReadFromJsonAsync<PresenceDirectoryPage>())!;
        Assert.Equal(0, empty.TotalOnline);
        Assert.Empty(empty.Entries);

        NodePresenceReport unregistered = new(Guid.NewGuid(), 1,
            ImmutableArray.Create(new NodePresenceEntry("Pilot", PlayerPresenceActivity.Online)));
        using HttpResponseMessage anonymousReport = await client.PutAsJsonAsync(
            "/v1/node/presence", unregistered);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousReport.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        client.DefaultRequestHeaders.Add("X-Server-Id", nodeId.ToString("D"));
        NodeRegistration registration = new(Guid.NewGuid(), "Node", "us-central",
            "wss://node.example/v1/control", 1, "build", new string('a', 64), 8);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/node/registration", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/v1/node/heartbeat",
                new NodeHeartbeat(registration.Incarnation, 2, 0, 0))).StatusCode);
        Assert.Equal(2, factory.Services.GetRequiredService<NodeDirectory>()
            .FindOnline(nodeId)!.OnlineUsers);

        NodePresenceReport report = new(registration.Incarnation, 1,
            ImmutableArray.Create(new NodePresenceEntry("Pilot", PlayerPresenceActivity.InLobby)));
        using HttpResponseMessage reported = await client.PutAsJsonAsync(
            "/v1/node/presence", report);
        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);

        using HttpResponseMessage publicResponse = await client.GetAsync("/v1/presence");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        string json = await publicResponse.Content.ReadAsStringAsync();
        PresenceDirectoryPage page = (await publicResponse.Content
            .ReadFromJsonAsync<PresenceDirectoryPage>())!;
        Assert.Equal(2, page.TotalOnline);
        Assert.Equal(1, page.VisibleOnline);
        PublicPresenceEntry entry = Assert.Single(page.Entries);
        Assert.Equal("Pilot", entry.DisplayName);
        Assert.Equal(PlayerPresenceActivity.InLobby, entry.Activity);
        Assert.Equal("us-central", entry.Region);
        foreach (string privateName in new[] { "NodeId", "SessionId", "AccountId", "MatchId", "ResumeToken" })
            Assert.DoesNotContain(privateName, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndpointRejectsStaleIncarnationRevisionAndInvalidPages()
    {
        Guid nodeId = Guid.NewGuid();
        using var factory = new BackendFactory(configure: services =>
            services.Configure<GameServerOptions>(options => options.Servers.Add(new GameServerRegistration
            {
                Id = nodeId,
                Enabled = true,
                ApiKeySha256 = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(Secret)))
            })));
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        client.DefaultRequestHeaders.Add("X-Server-Id", nodeId.ToString("D"));
        NodeRegistration first = new(Guid.NewGuid(), "Node", "us",
            "wss://node.example/v1/control", 1, "build", new string('a', 64), 8);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/node/registration", first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/node/presence", new NodePresenceReport(
                first.Incarnation, 4, ImmutableArray.Create(new NodePresenceEntry(
                    "Pilot", PlayerPresenceActivity.Online))))).StatusCode);

        using HttpResponseMessage lower = await client.PutAsJsonAsync("/v1/node/presence",
            new NodePresenceReport(first.Incarnation, 3, ImmutableArray.Create(
                new NodePresenceEntry("Lower", PlayerPresenceActivity.Online))));
        Assert.Equal(HttpStatusCode.Conflict, lower.StatusCode);
        Assert.Equal("stale_revision", await ProblemCode(lower));

        using HttpResponseMessage changed = await client.PutAsJsonAsync("/v1/node/presence",
            new NodePresenceReport(first.Incarnation, 4, ImmutableArray.Create(
                new NodePresenceEntry("Changed", PlayerPresenceActivity.InLobby))));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("stale_revision", await ProblemCode(changed));

        NodeRegistration replacement = first with { Incarnation = Guid.NewGuid(), Region = "eu" };
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/v1/node/registration", replacement)).StatusCode);
        using HttpResponseMessage stale = await client.PutAsJsonAsync("/v1/node/presence",
            new NodePresenceReport(first.Incarnation, 5, ImmutableArray.Create(
                new NodePresenceEntry("Replay", PlayerPresenceActivity.Online))));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stale_incarnation", await ProblemCode(stale));

        using HttpResponseMessage invalidPage = await client.GetAsync(
            $"/v1/presence?page={PresenceContract.MaximumPages}");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.Equal("invalid_page", await ProblemCode(invalidPage));
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out JsonElement code)
            ? code.GetString() : null;
    }
}
