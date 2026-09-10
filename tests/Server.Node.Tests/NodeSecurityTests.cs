using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectPrime.Server.Node.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeSecurityTests
{
    [Fact]
    public async Task ConcurrentReplayAdmitsExactlyOneAndBadSignatureAdmitsNone()
    {
        await using var host = new NodeHostFixture();
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();
        string token = host.Ticket(Guid.NewGuid());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => validator.ValidateAsync(token)));
        Assert.Single(attempts, identity => identity != null);
        string fresh = host.Ticket(Guid.NewGuid()); int signature = fresh.LastIndexOf('.') + 1;
        string changed = fresh[..signature] + (fresh[signature] == 'A' ? 'B' : 'A') + fresh[(signature + 1)..];
        Assert.Null(await validator.ValidateAsync(changed));
    }
    [Fact]
    public async Task EvenCorrectlySignedDuplicateIdentityClaimsAreRejected()
    {
        await using var host = new NodeHostFixture();
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = "https://backend.example", ["aud"] = NodeAdmissionValidator.Audience(host.NodeId),
            ["sub"] = Guid.NewGuid().ToString("D"), ["jti"] = Guid.NewGuid().ToString("D"), ["name"] = "Player",
            ["iat"] = now, ["nbf"] = now, ["exp"] = now + 120
        };
        string payload = JsonSerializer.Serialize(claims);
        payload = payload[..^1] + ",\"sub\":\"" + Guid.NewGuid().ToString("D") + "\"}";
        string input = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"typ\":\"pp-node-admission+jwt\",\"kid\":\"test\"}")) + "." + Encode(Encoding.UTF8.GetBytes(payload));
        string token = input + "." + Encode(host.SigningKey.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Null(await validator.ValidateAsync(token));
    }
    [Fact]
    public void BackendPrivateSigningMaterialIsNotAcceptedAsNodeVerificationConfiguration()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string path = Path.Combine(Path.GetTempPath(), "node-private-test-" + Guid.NewGuid().ToString("N") + ".pem");
        try
        {
            File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
            Assert.Throws<InvalidOperationException>(() => new NodeAdmissionValidator(new()
            { NodeId = Guid.NewGuid(), Issuer = "https://backend.example", Keys = [new() { KeyId = "test", PublicKeyPemPath = path }] }, TimeProvider.System));
        }
        finally { File.Delete(path); }
    }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
