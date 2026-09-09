using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FruityPrime.Server.Node.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FruityPrime.Server.Node.Tests;

internal sealed class NodeHostFixture : IAsyncDisposable
{
    public Guid NodeId { get; } = Guid.NewGuid();
    public ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _path = Path.Combine(Path.GetTempPath(), "node-test-" + Guid.NewGuid().ToString("N") + ".pem");
    private readonly X509Certificate2 _certificate;
    public WebApplication App { get; }
    public string CertificateThumbprint => _certificate.Thumbprint;
    public NodeHostFixture(TimeProvider? clock = null, Action<WebApplicationBuilder>? configure = null)
    {
        File.WriteAllText(_path, SigningKey.ExportSubjectPublicKeyInfoPem());
        using var tlsKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        App = NodeApplication.Build([], builder =>
        {
            if (clock != null) builder.Services.AddSingleton(clock);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Node:Authentication:NodeId"] = NodeId.ToString("D"), ["Node:Authentication:Issuer"] = "https://backend.example",
                ["Node:Authentication:Keys:0:KeyId"] = "test", ["Node:Authentication:Keys:0:PublicKeyPemPath"] = _path
            });
            configure?.Invoke(builder);
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate)));
        });
    }
    public string Ticket(Guid player, string? audience = null, string? type = null, int expiresIn = 120, string? kind = null)
    {
        DateTime now = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sub"] = player.ToString("D"), ["jti"] = Guid.NewGuid().ToString("D"), ["name"] = "Player" };
        if (kind != null) claims["kind"] = kind;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://backend.example", Audience = audience ?? NodeAdmissionValidator.Audience(NodeId),
            TokenType = type ?? NodeAdmissionValidator.TokenType, IssuedAt = now, NotBefore = now,
            Expires = now.AddSeconds(expiresIn), SigningCredentials = new(new ECDsaSecurityKey(SigningKey) { KeyId = "test" }, "ES256"),
            Claims = claims
        });
    }
    public async ValueTask DisposeAsync() { await App.DisposeAsync(); _certificate.Dispose(); SigningKey.Dispose(); File.Delete(_path); }
}
