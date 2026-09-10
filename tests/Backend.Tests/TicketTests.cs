using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public async Task RetiredDirectGameRoutesAreNotMapped()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/v1/server/session", new { ServerIncarnation = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/v1/game-tickets", new { ServerId, Nonce = "42" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/v1/game-ticket-keys")).StatusCode);
    }

    [Fact]
    public void RegistryAuthenticatesConfiguredNodesAndReportsTrust()
    {
        var options = Options.Create(new GameServerOptions { Servers = [new()
        {
            Id = ServerId, Enabled = true, ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
        }] });
        var registry = new GameServerRegistry(options, TestHostEnvironment.Testing);
        Assert.False(registry.TryAuthenticate(Guid.NewGuid(), Secret, out _));
        Assert.False(registry.TryAuthenticate(ServerId, "wrong-secret", out _));
        Assert.True(registry.TryAuthenticate(ServerId, Secret, out MatchTrustClass trust));
        Assert.Equal(MatchTrustClass.Community, trust);
    }

    [Fact]
    public void PublicDeploymentDisablesRankedAuthenticationCentrally()
    {
        var options = Options.Create(new GameServerOptions { Servers = [new()
        {
            Id = ServerId, Enabled = true, TrustClass = MatchTrustClass.Ranked,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
        }] });
        var production = new GameServerRegistry(options, TestHostEnvironment.Production);
        Assert.False(production.RankedAvailability.Available);
        Assert.Equal(GameServerRegistry.RankedUnavailableReason, production.RankedAvailability.UnavailableReason);
        Assert.False(production.TryAuthenticate(ServerId, Secret, out _));

        var testing = new GameServerRegistry(options, TestHostEnvironment.Testing);
        Assert.True(testing.RankedAvailability.Available);
        Assert.True(testing.TryAuthenticate(ServerId, Secret, out MatchTrustClass trust));
        Assert.Equal(MatchTrustClass.Ranked, trust);
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
            ValidIssuer = Issuer, ValidAudience = "urn:project-prime:match-result", IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256], ValidTypes = ["pp-match-result+jwt"],
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
    public string ApplicationName { get; set; } = "ProjectPrime.Backend.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
