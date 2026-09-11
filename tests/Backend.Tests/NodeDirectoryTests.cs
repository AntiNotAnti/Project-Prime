using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Backend.Nodes;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class NodeDirectoryTests
{
    private const string Secret = "test-node-secret-at-least-thirty-two-characters";
    private static readonly Guid Node = Guid.NewGuid();
    private static NodeRegistration Registration() => new(Guid.NewGuid(), "Node", "us-central", "wss://node.example/v1/control", 1, "build1", new string('a', 64), 100);
    private static NodeDirectory Directory(Clock clock, MatchTrustClass trust = MatchTrustClass.Community) => new(new GameServerRegistry(Options.Create(new GameServerOptions
    {
        Servers = [new() { Id = Node, Enabled = true, ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))), TrustClass = trust }]
    }), new Environment()), clock);

    [Fact]
    public void AuthenticatedRegistrationFiltersCompatibilityAndExpiresAtExactTtl()
    {
        var clock = new Clock(); var directory = Directory(clock); var registration = Registration();
        Assert.False(directory.Register(Guid.NewGuid(), Secret, registration));
        Assert.False(directory.Register(Node, "wrong", registration));
        Assert.True(directory.Register(Node, Secret, registration));
        Assert.Equal("community", Assert.Single(directory.Browse(1, "build1", registration.ContentHash)).TrustClass);
        Assert.Empty(directory.Browse(2, "build1", registration.ContentHash));
        Assert.Empty(directory.Browse(1, "build2", registration.ContentHash));
        Assert.Empty(directory.Browse(1, "build1", new string('b', 64)));
        clock.Now += NodeDirectory.OnlineLifetime;
        Assert.Null(directory.FindOnline(Node));
        Assert.Empty(directory.Browse(1, "build1", registration.ContentHash));
        Assert.False(directory.Heartbeat(Node, Secret, new(registration.Incarnation, 3, 2, 1)));
        Assert.True(directory.Register(Node, Secret, registration));
        Assert.True(directory.Heartbeat(Node, Secret, new(registration.Incarnation, 3, 2, 1)));
        Assert.Equal(3, directory.FindOnline(Node)!.OnlineUsers);
    }

    [Fact]
    public void PublicDirectoryPagesPinExpiryChangesToAnExplicitRevision()
    {
        var clock = new Clock();
        var directory = Directory(clock);
        var registration = Registration();
        Assert.True(directory.Register(Node, Secret, registration));

        NodeDirectoryPage first = directory.BrowsePage(1, "build1", registration.ContentHash);
        Assert.Equal(0, first.Page);
        // The first registration increments the initial revision from one to
        // two; expiry is another revision change before the pinned read.
        clock.Now += NodeDirectory.OnlineLifetime;

        NodeDirectoryPage empty = directory.BrowsePage(1, "build1", registration.ContentHash);
        Assert.Empty(empty.Entries);
        Assert.True(empty.Revision > first.Revision);
        NodeDirectoryPageException error = Assert.Throws<NodeDirectoryPageException>(() =>
            directory.BrowsePage(1, "build1", registration.ContentHash, revision: first.Revision));
        Assert.Equal("directory_revision_changed", error.Code);
    }

    [Fact]
    public void MapCatalogMetadataRoundTripsThroughPublicListings()
    {
        var directory = Directory(new Clock());
        var registration = Registration() with
        {
            MapCatalogRevision = 17, MapCount = 2,
            MapCatalogHash = new string('b', 64)
        };

        Assert.True(directory.Register(Node, Secret, registration));

        NodeListing listed = Assert.Single(directory.Browse(1, "build1", registration.ContentHash));
        Assert.Equal(17, listed.MapCatalogRevision);
        Assert.Equal(2, listed.MapCount);
        Assert.Equal(new string('b', 64), listed.MapCatalogHash);
    }

    [Fact]
    public void InvalidMapCatalogEntriesAreRejected()
    {
        foreach ((long Revision, int Count, string? Hash) metadata in new[]
        {
            (0L, 1, null),
            (1L, 257, null),
            (1L, 1, "not-a-sha256")
        })
        {
            var directory = Directory(new Clock());
            Assert.Throws<ArgumentException>(() => directory.Register(Node, Secret,
                Registration() with { MapCatalogRevision = metadata.Revision,
                    MapCount = metadata.Count, MapCatalogHash = metadata.Hash }));
        }
    }

    [Fact]
    public void DeregistrationHonorsAuthenticatedIncarnation()
    {
        var clock = new Clock();
        var directory = Directory(clock);
        var registration = Registration();
        Assert.True(directory.Register(Node, Secret, registration));
        Assert.True(directory.Deregister(Node, Secret, Guid.NewGuid()));
        Assert.NotNull(directory.FindOnline(Node));
        Assert.True(directory.Deregister(Node, Secret, registration.Incarnation));
        Assert.Null(directory.FindOnline(Node));
    }

    [Fact]
    public void IncarnationReplacementRejectsStaleHeartbeatAndTrustComesFromOperator()
    {
        var directory = Directory(new Clock(), MatchTrustClass.VerifiedCasual);
        var first = Registration(); var second = Registration();
        directory.Register(Node, Secret, first); directory.Register(Node, Secret, second);
        Assert.False(directory.Heartbeat(Node, Secret, new(first.Incarnation, 10, 0, 0)));
        Assert.True(directory.Heartbeat(Node, Secret, new(second.Incarnation, 4, 1, 1)));
        Assert.Equal("verified", directory.FindOnline(Node)!.TrustClass);
        Assert.Throws<ArgumentException>(() => directory.Heartbeat(Node, Secret, new(second.Incarnation, 101, 0, 0)));
        Assert.Equal(4, directory.FindOnline(Node)!.OnlineUsers);
        Assert.Throws<ArgumentException>(() => directory.Register(Node, Secret, second with { PublicControlUri = "ws://node.example" }));
        Assert.Throws<ArgumentException>(() => directory.Register(Node, Secret, second with { PublicControlUri = "wss://user:pass@node.example" }));
    }

    [Fact]
    public async Task NodeAdmissionUsesDistinctPurposeAudienceAndVerifiableEs256Signature()
    {
        string path = Path.GetTempFileName();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
            var clock = new Clock();
            using var issuer = new GameTicketIssuer(Options.Create(new TicketOptions
                { Issuer = "https://backend.example", KeyId = "node-test", SigningKeyPemPath = path }), clock);
            var player = new PlayerId(Guid.NewGuid());
            var response = issuer.IssueNodeAdmission(player, "Hunter", Node, "wss://node.example/v1/control");
            var jwt = new JsonWebToken(response.Ticket);
            Assert.Equal("pp-node-admission+jwt", jwt.Typ);
            Assert.Equal("ES256", jwt.Alg); Assert.Equal("node-test", jwt.Kid);
            Assert.Equal(player.ToString(), jwt.Subject); Assert.Equal("Hunter", jwt.GetClaim("name").Value);
            Assert.False(jwt.TryGetClaim("kind", out _));
            Assert.Equal("urn:project-prime:node:" + Node.ToString("D"), Assert.Single(jwt.Audiences));
            Assert.Equal(120, (jwt.ValidTo - jwt.IssuedAt).TotalSeconds);
            Assert.Equal(jwt.IssuedAt, jwt.ValidFrom);
            Assert.False(jwt.TryGetClaim("sid", out _)); Assert.False(jwt.TryGetClaim("nonce", out _));
            Assert.True(Guid.TryParseExact(jwt.Id, "D", out _));
            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(response.Ticket, new TokenValidationParameters
            {
                ValidIssuer = "https://backend.example", ValidAudience = "urn:project-prime:node:" + Node.ToString("D"),
                IssuerSigningKey = new ECDsaSecurityKey(key), ValidAlgorithms = ["ES256"], ValidTypes = ["pp-node-admission+jwt"],
                ValidateLifetime = false
            });
            Assert.True(validation.IsValid, validation.Exception?.Message);
            Assert.NotEqual(jwt.Id, new JsonWebToken(issuer.IssueNodeAdmission(player, "Hunter", Node, "wss://node.example/v1/control").Ticket).Id);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task HttpDirectoryRejectsSpoofedTrustAndAdmissionRequiresConfirmedCurrentAccount()
    {
        string path = Path.GetTempFileName();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
            using var factory = new BackendFactory(configure: services =>
            {
                services.Configure<TicketOptions>(x => { x.Issuer = "https://backend.example"; x.KeyId = "test"; x.SigningKeyPemPath = path; });
                services.Configure<GameServerOptions>(x => x.Servers.Add(new() { Id = Node, Enabled = true,
                    ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))) }));
            });
            using var client = factory.CreateDatabaseClient();
            using var server = factory.CreateClient();
            var registration = Registration();
            Assert.Equal(HttpStatusCode.Unauthorized, (await server.PutAsJsonAsync("/v1/node/registration", registration)).StatusCode);
            server.DefaultRequestHeaders.Add("X-Server-Id", Node.ToString("D"));
            server.DefaultRequestHeaders.Authorization = new("Bearer", Secret);
            Assert.Equal(HttpStatusCode.OK, (await server.PutAsJsonAsync("/v1/node/registration", registration)).StatusCode);
            var spoof = JsonSerializer.SerializeToNode(registration)!; spoof["TrustClass"] = "verified";
            Assert.Equal(HttpStatusCode.BadRequest, (await server.PutAsJsonAsync("/v1/node/registration", spoof)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await server.PostAsJsonAsync("/v1/node/heartbeat", new NodeHeartbeat(registration.Incarnation, 2, 1, 0))).StatusCode);
            var listing = await client.GetFromJsonAsync<NodeDirectoryPage>("/v1/nodes?protocol=1&build=build1&content=" + registration.ContentHash);
            Assert.Equal("community", Assert.Single(listing!.Entries).TrustClass);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/node-admissions", new { NodeId = Node })).StatusCode);
            const string password = "Test-Strong-Password123!";
            await client.PostAsJsonAsync("/v1/auth/register", new { Email = "node@example.test", Password = password, DisplayName = "Hunter" });
            var login = await client.PostAsJsonAsync("/v1/auth/login", new { Email = "node@example.test", Password = password });
            client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/node-admissions", new { NodeId = Node })).StatusCode);
            var email = Assert.Single(factory.Email.Sent);
            await client.PostAsJsonAsync("/v1/auth/confirm-email", new { email.PlayerId, email.Code });
            var response = await client.PostAsJsonAsync("/v1/node-admissions", new { NodeId = Node });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Node, (await response.Content.ReadFromJsonAsync<NodeAdmissionResponse>())!.NodeId);
            await client.PostAsync("/v1/auth/revoke-sessions", null);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/node-admissions", new { NodeId = Node })).StatusCode);
        }
        finally { File.Delete(path); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
