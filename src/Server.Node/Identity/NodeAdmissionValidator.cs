using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FruityPrime.Server.Shared;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FruityPrime.Server.Node.Identity;

public sealed record NodeIdentity(Guid PlayerId, string DisplayName);
public sealed class NodeAuthOptions
{
    public Guid NodeId { get; set; }
    public string Issuer { get; set; } = "";
    public List<NodeVerificationKey> Keys { get; set; } = [];
}
public sealed class NodeVerificationKey
{
    public string KeyId { get; set; } = "";
    public string PublicKeyPemPath { get; set; } = "";
}

/// <summary>Backend ES256 Node admissions only. Gameplay and opaque account tokens are different credentials.</summary>
public sealed class NodeAdmissionValidator : IDisposable
{
    public const string TokenType = "ph-node-admission+jwt";
    public static string Audience(Guid nodeId) => "urn:prime-hunters:node:" + nodeId.ToString("D");
    private readonly NodeAuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, ECDsaSecurityKey> _keys = [];
    private readonly Dictionary<Guid, long> _used = [];
    private readonly SemaphoreSlim _gate = new(1);
    public NodeAdmissionValidator(NodeAuthOptions options, TimeProvider clock)
    {
        if (options.NodeId == Guid.Empty || !Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer)
            || issuer.Scheme != "https" || issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0
            || options.Issuer.Length > 128 || options.Issuer.Any(c => c > 127 || char.IsControl(c))
            || options.Keys.Count is < 1 or > 8) throw new InvalidOperationException("Node authentication requires NodeId, HTTPS issuer and 1..8 public verification keys.");
        _options = options;
        _clock = clock;
        try
        {
            foreach (var configured in options.Keys)
            {
                if (configured.KeyId.Length is < 1 or > 64 || configured.KeyId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                    throw new InvalidOperationException("Invalid admission key ID.");
                var key = ECDsa.Create();
                try
                {
                    if (new FileInfo(configured.PublicKeyPemPath).Length > 16384) throw new InvalidOperationException("Verification PEM is oversized.");
                    string pem = File.ReadAllText(configured.PublicKeyPemPath);
                    if (!PemEncoding.TryFind(pem, out var fields) || pem[fields.Label] != "PUBLIC KEY")
                        throw new InvalidOperationException("Node verification accepts public SPKI PEM only.");
                    key.ImportFromPem(pem);
                    if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                        throw new InvalidOperationException("Admission verification requires P-256.");
                    _keys.Add(configured.KeyId, new(key) { KeyId = configured.KeyId });
                }
                catch { key.Dispose(); throw; }
            }
        }
        catch { foreach (var key in _keys.Values) key.ECDsa.Dispose(); throw; }
    }
    public async Task<NodeIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token.Length is < 1 or > 4096 || token.Any(c => c > 127 || char.IsWhiteSpace(c))) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jwt = new JsonWebToken(token);
            if (jwt.Alg != "ES256" || jwt.Typ != TokenType || !_keys.TryGetValue(jwt.Kid ?? "", out var key)) return null;
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedHeader));
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
            NodeControlCodec.RejectDuplicates(header.RootElement); NodeControlCodec.RejectDuplicates(payload.RootElement);
            if (header.RootElement.EnumerateObject().Any(p => p.Name is "crit" or "jku" or "jwk" or "x5u")) return null;
            var c = payload.RootElement;
            long now = _clock.GetUtcNow().ToUnixTimeSeconds();
            if (c.GetProperty("iss").GetString() != _options.Issuer || c.GetProperty("aud").GetString() != Audience(_options.NodeId)
                || !GuidClaim(c, "sub", out var player) || !GuidClaim(c, "jti", out var id)) return null;
            string? name = c.GetProperty("name").GetString();
            long issued = c.GetProperty("iat").GetInt64(), start = c.GetProperty("nbf").GetInt64(), expires = c.GetProperty("exp").GetInt64();
            if (name is not { Length: >= 1 and <= 16 } || string.IsNullOrWhiteSpace(name) || name.Any(ch => ch < 32 || ch > 126)
                || issued < 0 || start != issued || expires <= issued || expires - issued > 120 || issued > now + 5 || expires <= now) return null;
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                RequireSignedTokens = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
                IssuerSigningKey = key, ValidateIssuer = true, ValidIssuer = _options.Issuer,
                ValidateAudience = true, ValidAudience = Audience(_options.NodeId), ValidAlgorithms = ["ES256"], ValidTypes = [TokenType],
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(5),
                LifetimeValidator = (_, _, _, _) => issued <= now + 5 && expires > now
            });
            if (!result.IsValid) return null;
            foreach (var old in _used.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) _used.Remove(old);
            if (_used.ContainsKey(id) || _used.Count >= 8192) return null;
            _used.Add(id, expires);
            return new(player, name);
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return null; }
        finally { _gate.Release(); }
    }
    private static bool GuidClaim(JsonElement c, string key, out Guid id)
    { id = default; return Guid.TryParseExact(c.GetProperty(key).GetString(), "D", out id) && id != Guid.Empty; }
    public void Dispose() { foreach (var key in _keys.Values) key.ECDsa.Dispose(); _gate.Dispose(); }
}
