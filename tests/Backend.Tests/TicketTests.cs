using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class TicketTests
{
    private const string Issuer = "https://backend.example.test";
    private const string Secret = "test-only-server-credential-not-for-production-123456";
    private static readonly Guid ServerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Incarnation = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthenticatedServerSessionAndConfirmedPlayerIssueBoundedSignedTicket(bool endpointConfigured)
    {
        using var keys = new TestKeys();
        using var factory = new BackendFactory(configure: services =>
        {
            services.Configure<TicketOptions>(options => keys.Configure(options));
            services.Configure<GameServerOptions>(options => options.Servers.Add(new()
            {
                Id = ServerId, Enabled = true, PublicAddress = endpointConfigured ? "203.0.113.7" : null, PublicPort = endpointConfigured ? 5000 : 0, ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
            }));
        });
        using var client = factory.CreateDatabaseClient();
        const string password = "Test-Strong-Password123!";
        await client.PostAsJsonAsync("/v1/auth/register", new { Email = "test@example.test", Password = password, DisplayName = "Hunter" });
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { Email = "test@example.test", Password = password });
        var access = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "42" })).StatusCode);
        var email = Assert.Single(factory.Email.Sent);
        await client.PostAsJsonAsync("/v1/auth/confirm-email", new { email.PlayerId, email.Code });
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "42" })).StatusCode);

        using var server = factory.CreateClient();
        server.DefaultRequestHeaders.Add("X-Server-Id", ServerId.ToString("D"));
        server.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.PutAsJsonAsync("/v1/server/session", new { ServerIncarnation = Incarnation })).StatusCode);
        server.DefaultRequestHeaders.Authorization = new("Bearer", Secret);
        Assert.Equal(HttpStatusCode.OK, (await server.PutAsJsonAsync("/v1/server/session", new { ServerIncarnation = Incarnation })).StatusCode);
        var response = await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "42" });
        if (!endpointConfigured)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotNull(ticket);
        Assert.Equal("203.0.113.7", ticket.PublicAddress); Assert.Equal(5000, ticket.PublicPort);
        Assert.True(Encoding.UTF8.GetByteCount(ticket.Ticket) <= GameTicketIssuer.MaximumTicketBytes);
        var jwt = new JsonWebToken(ticket.Ticket);
        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal("current", jwt.Kid);
        Assert.Equal(email.PlayerId.ToString(), jwt.Subject);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(Incarnation.ToString("D"), jwt.GetClaim("sid").Value);
        Assert.Equal("42", jwt.GetClaim("nonce").Value);
        Assert.Equal("Hunter", jwt.GetClaim("name").Value);
        Assert.Equal(TimeSpan.FromSeconds(120), jwt.ValidTo - jwt.IssuedAt);
        Assert.True(Guid.TryParse(jwt.Id, out _));
        var jwks = await client.GetFromJsonAsync<JsonElement>("/v1/game-ticket-keys");
        string publicJson = jwks.GetRawText();
        Assert.DoesNotContain("\"d\"", publicJson);
        Assert.Equal(2, jwks.GetProperty("keys").GetArrayLength());
        var publicKey = new JsonWebKey(jwks.GetProperty("keys")[0].GetRawText());
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(ticket.Ticket, new TokenValidationParameters
        {
            ValidIssuer = Issuer, ValidAudience = ServerId.ToString("D"), IssuerSigningKey = publicKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256], ClockSkew = TimeSpan.Zero
        });
        Assert.True(validation.IsValid, validation.Exception?.Message);
        var next = await (await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "42" })).Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotEqual(jwt.Id, new JsonWebToken(next!.Ticket).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "042" })).StatusCode);
        await client.PostAsync("/v1/auth/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "42" })).StatusCode);
    }

    [Theory]
    [InlineData("127.1", 5000)]
    [InlineData("::1", 5000)]
    [InlineData("0.0.0.0", 5000)]
    [InlineData("224.0.0.1", 5000)]
    [InlineData("255.255.255.255", 5000)]
    [InlineData("203.0.113.7", 0)]
    [InlineData("203.0.113.7", 65536)]
    [InlineData(null, 5000)]
    public void ConfiguredTicketDestinationRejectsNoncanonicalOrInvalidEndpoints(string? address, int port)
    {
        var options = Options.Create(new GameServerOptions { Servers = [new()
        {
            Id = ServerId, Enabled = true, PublicAddress = address, PublicPort = port,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
        }] });
        Assert.Throws<InvalidOperationException>(() => new GameServerRegistry(options, TestHostEnvironment.Testing));
    }

    [Fact]
    public void StartupRegistryIsAuthenticatedAndDoesNotSurviveRestart()
    {
        var options = Options.Create(new GameServerOptions { Servers = [new()
        {
            Id = ServerId, Enabled = true, ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
        }] });
        var registry = new GameServerRegistry(options, TestHostEnvironment.Testing);
        Assert.False(registry.TryRegister(Guid.NewGuid(), Secret, Incarnation));
        Assert.False(registry.TryRegister(ServerId, Secret, Guid.Empty));
        Assert.True(registry.TryRegister(ServerId, Secret, Incarnation));
        Assert.False(registry.TryGetTicketDestination(ServerId, out _, out _, out _));
        Assert.True(registry.TryGetIncarnation(ServerId, out Guid sid));
        Assert.Equal(Incarnation, sid);
        Assert.False(new GameServerRegistry(options, TestHostEnvironment.Testing).TryGetIncarnation(ServerId, out _));
        var replacement = Guid.NewGuid();
        Assert.True(registry.TryRegister(ServerId, Secret, replacement));
        Assert.True(registry.TryGetIncarnation(ServerId, out sid));
        Assert.Equal(replacement, sid);
    }

    [Fact]
    public void PublicDeploymentDisablesRankedRegistrationAndAuthenticationCentrally()
    {
        var options = Options.Create(new GameServerOptions { Servers = [new()
        {
            Id = ServerId, Enabled = true, TrustClass = MatchTrustClass.Ranked,
            PublicAddress = "203.0.113.7", PublicPort = 5000,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
        }] });
        var production = new GameServerRegistry(options, TestHostEnvironment.Production);
        Assert.False(production.RankedAvailability.Available);
        Assert.Equal(GameServerRegistry.RankedUnavailableReason, production.RankedAvailability.UnavailableReason);
        Assert.False(production.TryRegister(ServerId, Secret, Incarnation));
        Assert.False(production.TryAuthenticate(ServerId, Secret, out _));
        Assert.False(production.TryGetTicketDestination(ServerId, out _, out _, out _));

        var testing = new GameServerRegistry(options, TestHostEnvironment.Testing);
        Assert.True(testing.RankedAvailability.Available);
        Assert.True(testing.TryRegister(ServerId, Secret, Incarnation));
        Assert.True(testing.TryAuthenticate(ServerId, Secret, out MatchTrustClass trust));
        Assert.Equal(MatchTrustClass.Ranked, trust);
    }

    [Fact]
    public void WorstPermittedIssuerAndNameFitTheJoinCarrier()
    {
        using var keys = new TestKeys();
        var options = new TicketOptions();
        keys.Configure(options);
        options.Issuer = "https://" + new string('a', 120);
        options.KeyId = new string('k', 32);
        var trusted = new PlayerId(Guid.NewGuid());
        options.TrustedObservers.Add(trusted.Value);
        using var issuer = new GameTicketIssuer(Options.Create(options), TimeProvider.System);
        var response = issuer.Issue(trusted, new string('"', 16), ServerId, Incarnation, ulong.MaxValue, "203.0.113.7", 5000);
        Assert.True(response.Ticket.Length <= GameTicketIssuer.MaximumTicketBytes);
        var token = new JsonWebToken(response.Ticket);
        Assert.True(token.GetPayloadValue<bool>("observerTrusted"));
        var ordinary = new JsonWebToken(issuer.Issue(new PlayerId(Guid.NewGuid()), "ordinary", ServerId, Incarnation, 1, "203.0.113.7", 5000).Ticket);
        Assert.False(ordinary.TryGetPayloadValue<bool>("observerTrusted", out _));
        options.Issuer += "a";
        Assert.Throws<InvalidOperationException>(() => new GameTicketIssuer(Options.Create(options), TimeProvider.System));
    }

    [Fact]
    public async Task SignedResultsHaveDistinctAudiencePurposeAndNoTicketAuthority()
    {
        using var keys = new TestKeys(); var options = new TicketOptions(); keys.Configure(options);
        using var issuer = new GameTicketIssuer(Options.Create(options), TimeProvider.System);
        Guid match = Guid.NewGuid(); string hash = new string('A', 64);
        string token = issuer.SignMatchResult(match, hash, 9, 2);
        var jwk = issuer.PublicKeys[0];
        var key = new JsonWebKey { Kty = jwk.Kty, Crv = jwk.Crv, X = jwk.X, Y = jwk.Y, Kid = jwk.Kid };
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer, ValidAudience = "urn:prime-hunters:match-result", IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256], ValidTypes = ["ph-match-result+jwt"],
            ValidateLifetime = false, RequireExpirationTime = false
        };
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        Assert.True(result.IsValid, result.Exception?.Message);
        var jwt = new JsonWebToken(token); Assert.Equal("match-result-v1", jwt.GetClaim("purpose").Value);
        Assert.Equal(match.ToString("D"), jwt.GetClaim("matchId").Value); Assert.Equal(hash, jwt.GetClaim("payloadHash").Value);
        Assert.False(jwt.TryGetPayloadValue<long>("exp", out _));
        parameters.ValidAudience = ServerId.ToString("D");
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters)).IsValid);
    }

    internal sealed class TestKeys : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("prime-ticket-test-").FullName;
        public TestKeys()
        {
            using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var previous = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string path = Path.Combine(_directory, "private.pem");
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) { fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            using (var stream = new FileStream(path, fileOptions))
            using (var writer = new StreamWriter(stream)) { writer.Write(current.ExportPkcs8PrivateKeyPem()); }
            File.WriteAllText(Path.Combine(_directory, "previous.pem"), previous.ExportSubjectPublicKeyInfoPem());
        }
        public void Configure(TicketOptions options)
        {
            options.Issuer = Issuer; options.KeyId = "current";
            options.SigningKeyPemPath = Path.Combine(_directory, "private.pem");
            options.PreviousKeys = [new() { KeyId = "previous", PublicKeyPemPath = Path.Combine(_directory, "previous.pem") }];
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public static readonly TestHostEnvironment Testing = new("Testing");
    public static readonly TestHostEnvironment Production = new(Environments.Production);
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "PrimeHunters.Backend.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
