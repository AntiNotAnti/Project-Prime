using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProjectPrime.Server.Node.Identity;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeAdmissionKeyRotationTests
{
    [Fact]
    public async Task UnknownKidRefreshesOnceAndAdmitsRotatedKey()
    {
        Guid node = Guid.NewGuid();
        string directory = Directory.CreateTempSubdirectory("prime-node-keys-").FullName;
        string bootstrapPath = Path.Combine(directory, "bootstrap.pem");
        using var bootstrap = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rotated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(bootstrapPath, bootstrap.ExportSubjectPublicKeyInfoPem());
        var handler = new KeyHandler([Jwk(rotated, "rotated")]);
        using var http = new HttpClient(handler);
        using var validator = new NodeAdmissionValidator(new NodeAuthOptions
        {
            NodeId = node, Issuer = "https://backend.example",
            Keys = [new() { KeyId = "bootstrap", PublicKeyPemPath = bootstrapPath }],
            KeyOrigin = "https://backend.example/v1/node-admission-keys"
        }, TimeProvider.System, http);
        try
        {
            string token = Token(rotated, "rotated", node, Guid.NewGuid());
            NodeIdentity? identity = await validator.ValidateAsync(token);
            Assert.NotNull(identity);
            Assert.Equal(1, handler.Requests);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentUnknownKidsUseOneThrottledRefresh()
    {
        Guid node = Guid.NewGuid();
        string directory = Directory.CreateTempSubdirectory("prime-node-keys-").FullName;
        string bootstrapPath = Path.Combine(directory, "bootstrap.pem");
        using var bootstrap = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rotated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(bootstrapPath, bootstrap.ExportSubjectPublicKeyInfoPem());
        var handler = new KeyHandler([Jwk(rotated, "rotated")]) { Delay = TimeSpan.FromMilliseconds(25) };
        using var http = new HttpClient(handler);
        using var validator = new NodeAdmissionValidator(new NodeAuthOptions
        {
            NodeId = node, Issuer = "https://backend.example",
            Keys = [new() { KeyId = "bootstrap", PublicKeyPemPath = bootstrapPath }],
            KeyOrigin = "https://backend.example/v1/node-admission-keys"
        }, TimeProvider.System, http);
        try
        {
            string token = Token(rotated, "rotated", node, Guid.NewGuid());
            NodeIdentity?[] identities = await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => validator.ValidateAsync(token)));
            Assert.Single(identities, value => value != null);
            Assert.Equal(1, handler.Requests);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RefreshFailureRetainsBootstrapKeyAndRedirectIsRejected()
    {
        Guid node = Guid.NewGuid();
        string directory = Directory.CreateTempSubdirectory("prime-node-keys-").FullName;
        string bootstrapPath = Path.Combine(directory, "bootstrap.pem");
        using var bootstrap = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(bootstrapPath, bootstrap.ExportSubjectPublicKeyInfoPem());
        var handler = new KeyHandler([], HttpStatusCode.Redirect);
        using var http = new HttpClient(handler);
        using var validator = new NodeAdmissionValidator(new NodeAuthOptions
        {
            NodeId = node, Issuer = "https://backend.example",
            Keys = [new() { KeyId = "bootstrap", PublicKeyPemPath = bootstrapPath }],
            KeyOrigin = "https://backend.example/v1/node-admission-keys"
        }, TimeProvider.System, http);
        try
        {
            Assert.NotNull(await validator.ValidateAsync(Token(bootstrap, "bootstrap", node, Guid.NewGuid())));
            Assert.Equal(0, handler.Requests); // known bootstrap key never performs a refresh
            Assert.Null(await validator.ValidateAsync(Token(bootstrap, "unknown", node, Guid.NewGuid())));
            Assert.Equal(1, handler.Requests);
            Assert.NotNull(await validator.ValidateAsync(Token(bootstrap, "bootstrap", node, Guid.NewGuid())));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task JkuIsRejectedBeforeAnyKeyRefresh()
    {
        Guid node = Guid.NewGuid();
        string directory = Directory.CreateTempSubdirectory("prime-node-keys-").FullName;
        string bootstrapPath = Path.Combine(directory, "bootstrap.pem");
        using var bootstrap = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(bootstrapPath, bootstrap.ExportSubjectPublicKeyInfoPem());
        var handler = new KeyHandler([]);
        using var http = new HttpClient(handler);
        using var validator = new NodeAdmissionValidator(new NodeAuthOptions
        {
            NodeId = node, Issuer = "https://backend.example",
            Keys = [new() { KeyId = "bootstrap", PublicKeyPemPath = bootstrapPath }],
            KeyOrigin = "https://backend.example/v1/node-admission-keys"
        }, TimeProvider.System, http);
        try
        {
            Assert.Null(await validator.ValidateAsync(TokenWithJku(bootstrap, "unknown", node)));
            Assert.Equal(0, handler.Requests);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SuccessfulRefreshReplacesStaleDynamicKeysAndRetainsBootstrap()
    {
        Guid node = Guid.NewGuid();
        string directory = Directory.CreateTempSubdirectory("prime-node-keys-").FullName;
        string bootstrapPath = Path.Combine(directory, "bootstrap.pem");
        using var bootstrap = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var firstDynamic = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var secondDynamic = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(bootstrapPath, bootstrap.ExportSubjectPublicKeyInfoPem());
        var clock = new RotationClock(DateTimeOffset.UtcNow);
        var handler = new KeyHandler([Jwk(firstDynamic, "old-dynamic")]);
        using var http = new HttpClient(handler);
        using var validator = new NodeAdmissionValidator(new NodeAuthOptions
        {
            NodeId = node, Issuer = "https://backend.example",
            Keys = [new() { KeyId = "bootstrap", PublicKeyPemPath = bootstrapPath }],
            KeyOrigin = "https://backend.example/v1/node-admission-keys",
            KeyRefreshSeconds = 30
        }, clock, http);
        try
        {
            Assert.NotNull(await validator.ValidateAsync(Token(firstDynamic, "old-dynamic", node,
                Guid.NewGuid(), clock.UtcNow)));

            handler.Keys = [Jwk(secondDynamic, "new-dynamic")];
            clock.Advance(TimeSpan.FromSeconds(31));
            Assert.NotNull(await validator.ValidateAsync(Token(secondDynamic, "new-dynamic", node,
                Guid.NewGuid(), clock.UtcNow)));

            // The previous dynamic generation was retired. A new token signed
            // by it must not remain valid merely because refresh succeeded.
            Assert.Null(await validator.ValidateAsync(Token(firstDynamic, "old-dynamic", node,
                Guid.NewGuid(), clock.UtcNow)));
            Assert.NotNull(await validator.ValidateAsync(Token(bootstrap, "bootstrap", node,
                Guid.NewGuid(), clock.UtcNow)));
            Assert.Equal(2, handler.Requests); // old and new refresh generations
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Token(ECDsa key, string kid, Guid node, Guid jti,
        DateTimeOffset? now = null, string? headerExtra = null)
    {
        DateTime issued = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "https://backend.example", Audience = NodeAdmissionValidator.Audience(node),
            TokenType = NodeAdmissionValidator.TokenType, IssuedAt = issued,
            NotBefore = issued, Expires = issued.AddSeconds(120),
            SigningCredentials = new(new ECDsaSecurityKey(key) { KeyId = kid }, "ES256"),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString("D"), ["name"] = "Player", ["jti"] = jti.ToString("D")
            }
        };
        string token = new JsonWebTokenHandler().CreateToken(descriptor);
        if (headerExtra == null) return token;
        string[] parts = token.Split('.');
        using JsonDocument header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
        var values = header.RootElement.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.Clone());
        values["jku"] = JsonDocument.Parse("\"https://attacker.example/keys\"").RootElement.Clone();
        string encoded = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(values));
        string input = encoded + "." + parts[1];
        byte[] signature = key.SignData(System.Text.Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return input + "." + Base64UrlEncoder.Encode(signature);
    }

    private static string TokenWithJku(ECDsa key, string kid, Guid node)
        => Token(key, kid, node, Guid.NewGuid(), headerExtra: "jku");

    private static NodeAdmissionPublicKey Jwk(ECDsa key, string kid)
    {
        var parameters = key.ExportParameters(false);
        return new("EC", "P-256", Base64UrlEncoder.Encode(parameters.Q.X!),
            Base64UrlEncoder.Encode(parameters.Q.Y!), kid, "sig", "ES256");
    }

    private sealed class KeyHandler(IReadOnlyList<NodeAdmissionPublicKey> keys,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests;
        public TimeSpan Delay { get; init; }
        public IReadOnlyList<NodeAdmissionPublicKey> Keys { get; set; } = keys;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
            {
                response.Content = new StringContent(JsonSerializer.Serialize(Keys), System.Text.Encoding.UTF8,
                    "application/json");
            }
            return response;
        }
    }

    private sealed class RotationClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _utcNow = now;
        public DateTimeOffset UtcNow => _utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
