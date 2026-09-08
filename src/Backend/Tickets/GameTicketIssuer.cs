using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Identity;

namespace MphRead.Backend.Tickets;

public sealed class TicketOptions
{
    public string? Issuer { get; set; }
    public string? KeyId { get; set; }
    public string? SigningKeyPemPath { get; set; }
    public List<PreviousTicketKey> PreviousKeys { get; set; } = [];
}

public sealed class PreviousTicketKey
{
    public string KeyId { get; set; } = "";
    public string PublicKeyPemPath { get; set; } = "";
}

public sealed record NodeAdmissionResponse(string Ticket, DateTimeOffset ExpiresAt, Guid NodeId, string PublicControlUri);
public sealed record PublicTicketKey(string Kty, string Crv, string X, string Y, string Kid, string Use, string Alg);

public sealed class GameTicketIssuer : IDisposable
{
    public const int LifetimeSeconds = 120;
    private readonly TimeProvider _clock;
    private readonly string? _issuer;
    private readonly ECDsa? _signer;
    private readonly SigningCredentials? _credentials;
    public IReadOnlyList<PublicTicketKey> PublicKeys { get; }
    public bool IsConfigured => _credentials != null;

    public GameTicketIssuer(IOptions<TicketOptions> options, TimeProvider clock)
    {
        _clock = clock;
        var settings = options.Value;
        PublicKeys = Array.Empty<PublicTicketKey>();
        if (settings.Issuer == null && settings.KeyId == null && settings.SigningKeyPemPath == null) { return; }
        if (settings.Issuer is not { Length: >= 1 and <= 128 }
            || !Uri.TryCreate(settings.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != "https"
            || settings.Issuer.Any(c => c > 127 || char.IsControl(c))
            || issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0
            || !ValidKeyId(settings.KeyId) || string.IsNullOrWhiteSpace(settings.SigningKeyPemPath))
            throw new InvalidOperationException("Tickets require an HTTPS issuer (max128 characters), key ID and signing PEM path.");
        _issuer = settings.Issuer;
        _signer = ECDsa.Create();
        try
        {
            _signer.ImportFromPem(File.ReadAllText(settings.SigningKeyPemPath));
            if (_signer.KeySize != 256 || _signer.ExportParameters(true).D == null)
                throw new InvalidOperationException("Ticket signing requires a P-256 private key.");
            var key = new ECDsaSecurityKey(_signer) { KeyId = settings.KeyId };
            _credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
            var publicKeys = new List<PublicTicketKey> { Export(_signer, settings.KeyId!) };
            foreach (var previous in settings.PreviousKeys)
            {
                if (!ValidKeyId(previous.KeyId) || publicKeys.Any(x => x.Kid == previous.KeyId))
                    throw new InvalidOperationException("Ticket key IDs must be valid and unique.");
                using var old = ECDsa.Create();
                old.ImportFromPem(File.ReadAllText(previous.PublicKeyPemPath));
                publicKeys.Add(Export(old, previous.KeyId));
            }
            PublicKeys = publicKeys.AsReadOnly();
        }
        catch { _signer.Dispose(); throw; }
    }

    public NodeAdmissionResponse IssueNodeAdmission(PlayerId playerId, string name, Guid nodeId, string publicControlUri)
    {
        if (_credentials == null) throw new InvalidOperationException("Tickets are not configured.");
        if (playerId.IsEmpty || nodeId == Guid.Empty || !Profiles.ProfileEndpoints.ValidDisplayName(name))
            throw new ArgumentException("Invalid Node admission identity.");
        var now = DateTimeOffset.FromUnixTimeSeconds(_clock.GetUtcNow().ToUnixTimeSeconds());
        var expires = now.AddSeconds(LifetimeSeconds);
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer, Audience = "urn:prime-hunters:node:" + nodeId.ToString("D"),
            TokenType = "ph-node-admission+jwt", IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime, SigningCredentials = _credentials,
            Claims = new Dictionary<string, object> { ["sub"] = playerId.ToString(),
                ["name"] = name, ["jti"] = Guid.NewGuid().ToString("D") }
        });
        return new(token, expires, nodeId, publicControlUri);
    }

    public string SignMatchResult(Guid matchId, string payloadHash, long processingOrder, int effectiveTrustClass)
    {
        if (_credentials == null) throw new InvalidOperationException("Result signing is not configured.");
        if (matchId == Guid.Empty || payloadHash.Length != 64 || !payloadHash.All(char.IsAsciiHexDigit) || processingOrder <= 0)
            throw new ArgumentException("Invalid accepted result identity.");
        // Historical results are durable assertions, not bearer credentials. Purpose, audience
        // and type are disjoint from short-lived game admission tickets; no default expiry.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer, Audience = "urn:prime-hunters:match-result", TokenType = "ph-match-result+jwt",
            IssuedAt = _clock.GetUtcNow().UtcDateTime, SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                ["purpose"] = "match-result-v1", ["matchId"] = matchId.ToString("D"),
                ["payloadHash"] = payloadHash, ["processingOrder"] = processingOrder, ["trustClass"] = effectiveTrustClass
            }
        });
    }

    private static bool ValidKeyId(string? value) => value is { Length: >= 1 and <= 32 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static PublicTicketKey Export(ECDsa key, string keyId)
    {
        var parameters = key.ExportParameters(false);
        if (key.KeySize != 256 || parameters.Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new InvalidOperationException("Ticket public keys must use P-256.");
        return new("EC", "P-256", Base64UrlEncoder.Encode(parameters.Q.X!),
            Base64UrlEncoder.Encode(parameters.Q.Y!), keyId, "sig", "ES256");
    }
    public void Dispose() => _signer?.Dispose();
}
