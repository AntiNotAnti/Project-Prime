using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPrime.Server.Shared;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ProjectPrime.Server.Node.Identity;

public sealed record NodeIdentity(Guid? PlayerId, Guid? GuestSessionId, string DisplayName,
    bool PublicPresence = true)
{
    // Preserve the account-ticket construction shape used by existing callers.
    public NodeIdentity(Guid playerId, string displayName) : this(playerId, null, displayName, true) { }
    public HumanIdentityKey IdentityKey => HumanIdentityValidation.Require(PlayerId, GuestSessionId);
    public bool IsGuest => GuestSessionId.HasValue;
    public void Validate()
    {
        IdentityKey.ToString();
        if (DisplayName is not { Length: >= 1 and <= 16 } || string.IsNullOrWhiteSpace(DisplayName)
            || DisplayName.Any(ch => ch < 32 || ch > 126)) throw new ArgumentException("Invalid display name.");
    }
}
public sealed class NodeAuthOptions
{
    public Guid NodeId { get; set; }
    public string Issuer { get; set; } = "";
    public List<NodeVerificationKey> Keys { get; set; } = [];
    /// <summary>Optional exact HTTPS endpoint used to refresh rotated public keys.</summary>
    public string? KeyOrigin { get; set; }
    public int KeyRefreshSeconds { get; set; } = 30;
}
public sealed class NodeVerificationKey
{
    public string KeyId { get; set; } = "";
    public string PublicKeyPemPath { get; set; } = "";
}

/// <summary>Bounded, public JWK-compatible ES256 verification material.</summary>
public sealed record NodeAdmissionPublicKey(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg);

/// <summary>Backend ES256 Node admissions only. Gameplay and opaque account tokens are different credentials.</summary>
public sealed class NodeAdmissionValidator : IDisposable
{
    public const string TokenType = "pp-node-admission+jwt";
    public const int MaximumKeys = 8;
    public const int MaximumKeyResponseBytes = 64 * 1024;
    public static string Audience(Guid nodeId) => "urn:project-prime:node:" + nodeId.ToString("D");
    private readonly NodeAuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly Uri? _keyOrigin;
    private readonly TimeSpan _keyRefreshInterval;
    private readonly HttpClient _keyHttp;
    private readonly bool _ownsKeyHttp;
    private readonly SemaphoreSlim _keysGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Dictionary<string, ECDsaSecurityKey> _keys = [];
    private readonly HashSet<string> _bootstrapKeyIds = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, long> _used = [];
    private readonly SemaphoreSlim _gate = new(1);
    private long _lastRefreshTicks;

    public NodeAdmissionValidator(NodeAuthOptions options, TimeProvider clock, HttpClient? keyHttp = null)
    {
        if (options.NodeId == Guid.Empty || !Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer)
            || issuer.Scheme != "https" || issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0
            || options.Issuer.Length > 128 || options.Issuer.Any(c => c > 127 || char.IsControl(c))
            || options.Keys.Count > MaximumKeys || options.KeyRefreshSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Node authentication requires NodeId, HTTPS issuer and at most 8 public verification keys.");
        if (!string.IsNullOrWhiteSpace(options.KeyOrigin))
        {
            if (!Uri.TryCreate(options.KeyOrigin, UriKind.Absolute, out Uri? origin)
                || origin.Scheme != Uri.UriSchemeHttps || origin.UserInfo.Length != 0
                || origin.Query.Length != 0 || origin.Fragment.Length != 0
                || origin.AbsoluteUri.Length > 256)
                throw new InvalidOperationException("Node key origin must be an exact HTTPS URL without credentials, query, or fragment.");
            _keyOrigin = origin;
        }
        else if (options.Keys.Count == 0)
            throw new InvalidOperationException("Node authentication requires bootstrap keys or an HTTPS key origin.");
        _options = options;
        _clock = clock;
        _keyRefreshInterval = TimeSpan.FromSeconds(options.KeyRefreshSeconds);
        _keyHttp = keyHttp ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(5) };
        _ownsKeyHttp = keyHttp == null;
        Dictionary<string, ECDsaSecurityKey>? bootstrap = null;
        try
        {
            bootstrap = new Dictionary<string, ECDsaSecurityKey>(StringComparer.Ordinal);
            foreach (NodeVerificationKey configured in options.Keys)
            {
                ECDsaSecurityKey key = LoadPemKey(configured);
                if (!bootstrap.TryAdd(configured.KeyId, key))
                {
                    key.ECDsa.Dispose();
                    throw new InvalidOperationException("Admission key IDs must be unique.");
                }
            }
            _bootstrapKeyIds.UnionWith(bootstrap.Keys);
            _keys = bootstrap;
            bootstrap = null; // _keys now owns the bootstrap key handles.
        }
        catch
        {
            DisposeKeys(bootstrap);
            foreach (ECDsaSecurityKey key in _keys.Values) key.ECDsa.Dispose();
            if (_ownsKeyHttp) _keyHttp.Dispose();
            throw;
        }
    }
    public async Task<NodeIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token.Length is < 1 or > 4096 || token.Any(c => c > 127 || char.IsWhiteSpace(c))) return null;
        JsonWebToken jwt;
        try
        {
            jwt = new JsonWebToken(token);
            using (JsonDocument header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedHeader)))
            {
                NodeControlCodec.RejectDuplicates(header.RootElement);
                if (jwt.Alg != "ES256" || jwt.Typ != TokenType
                    || header.RootElement.EnumerateObject().Any(p => p.Name is "crit" or "jku" or "jwk" or "x5u")) return null;
            }
            if (!await HasKeyAsync(jwt.Kid ?? "", cancellationToken))
            {
                await RefreshKeysAsync(jwt.Kid ?? "", cancellationToken);
                if (!await HasKeyAsync(jwt.Kid ?? "", cancellationToken)) return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException or JsonException or FormatException)
        { return null; }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
            NodeControlCodec.RejectDuplicates(payload.RootElement);
            var c = payload.RootElement;
            // Identity is represented only by sub + the optional kind tag. Do not
            // accept a second identity field that could disagree with either one.
            if (c.EnumerateObject().Any(p => p.Name is "playerId" or "guestId" or "guestSessionId" or "identity")) return null;
            long now = _clock.GetUtcNow().ToUnixTimeSeconds();
            if (c.GetProperty("iss").GetString() != _options.Issuer || c.GetProperty("aud").GetString() != Audience(_options.NodeId)
                || !GuidClaim(c, "sub", out var subject) || !GuidClaim(c, "jti", out var id)) return null;
            string kind = "registered";
            if (c.TryGetProperty("kind", out JsonElement kindClaim))
            {
                if (kindClaim.ValueKind != JsonValueKind.String) return null;
                kind = kindClaim.GetString() ?? "";
                if (kind is not ("registered" or "guest")) return null;
            }
            bool publicPresence = true;
            if (c.TryGetProperty("publicPresence", out JsonElement publicPresenceClaim))
            {
                if (publicPresenceClaim.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return null;
                publicPresence = publicPresenceClaim.GetBoolean();
            }
            string? name = c.GetProperty("name").GetString();
            long issued = c.GetProperty("iat").GetInt64(), start = c.GetProperty("nbf").GetInt64(), expires = c.GetProperty("exp").GetInt64();
            if (name is not { Length: >= 1 and <= 16 } || string.IsNullOrWhiteSpace(name) || name.Any(ch => ch < 32 || ch > 126)
                || issued < 0 || start != issued || expires <= issued
                || expires - issued > NodeAdmissionContract.LifetimeSeconds
                || issued > now + NodeAdmissionContract.MaximumClockSkewSeconds || expires <= now) return null;
            await _keysGate.WaitAsync(cancellationToken);
            bool valid;
            try
            {
                if (!_keys.TryGetValue(jwt.Kid ?? "", out ECDsaSecurityKey? key)) return null;
                var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
                {
                    RequireSignedTokens = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key, ValidateIssuer = true, ValidIssuer = _options.Issuer,
                    ValidateAudience = true, ValidAudience = Audience(_options.NodeId), ValidAlgorithms = ["ES256"], ValidTypes = [TokenType],
                    ValidateLifetime = true, ClockSkew = NodeAdmissionContract.MaximumClockSkew,
                    LifetimeValidator = (_, _, _, _) =>
                        issued <= now + NodeAdmissionContract.MaximumClockSkewSeconds && expires > now
                });
                valid = result.IsValid;
            }
            finally { _keysGate.Release(); }
            if (!valid) return null;
            foreach (var old in _used.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) _used.Remove(old);
            if (_used.ContainsKey(id) || _used.Count >= 8192) return null;
            _used.Add(id, expires);
            var identity = kind == "guest"
                ? new NodeIdentity(null, subject, name, publicPresence)
                : new NodeIdentity(subject, null, name, publicPresence);
            identity.Validate();
            return identity;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return null; }
        finally { _gate.Release(); }
    }
    private static bool GuidClaim(JsonElement c, string key, out Guid id)
    { id = default; return Guid.TryParseExact(c.GetProperty(key).GetString(), "D", out id) && id != Guid.Empty; }

    private async Task<bool> HasKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        await _keysGate.WaitAsync(cancellationToken);
        try { return _keys.ContainsKey(keyId); }
        finally { _keysGate.Release(); }
    }

    private async Task RefreshKeysAsync(string requestedKeyId, CancellationToken cancellationToken)
    {
        if (_keyOrigin == null) return;
        await _refreshGate.WaitAsync(cancellationToken);
        Dictionary<string, ECDsaSecurityKey>? refreshed = null;
        try
        {
            DateTimeOffset now = _clock.GetUtcNow();
            if (_lastRefreshTicks != 0 && now.Ticks - _lastRefreshTicks < _keyRefreshInterval.Ticks) return;
            _lastRefreshTicks = now.Ticks;
            refreshed = await FetchKeysAsync(cancellationToken);
            if (refreshed == null) return;
            if (!refreshed.ContainsKey(requestedKeyId))
            {
                // A response which does not contain the kid that triggered the
                // refresh is not evidence of a usable replacement generation.
                // Keep the current snapshot intact; the outer finally owns and
                // disposes every fetched key that was not published.
                return;
            }
            await _keysGate.WaitAsync(cancellationToken);
            try
            {
                Dictionary<string, ECDsaSecurityKey> old = _keys;
                var merged = new Dictionary<string, ECDsaSecurityKey>(StringComparer.Ordinal);
                // Bootstrap material is the trust anchor. Dynamic material is
                // intentionally replaced as one immutable generation instead
                // of growing forever with every rotation. The backend may
                // return keys in any order, so deterministic ID ordering keeps
                // the bounded snapshot stable across equivalent responses.
                foreach (string id in _bootstrapKeyIds.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (old.TryGetValue(id, out ECDsaSecurityKey? key)) merged.Add(id, key);
                }
                // The key which caused this refresh is security-critical. It
                // gets the first available dynamic slot, regardless of its
                // lexical position in the response. If all eight slots are
                // occupied by bootstrap keys, the contract is an explicit
                // validation failure rather than silently selecting another
                // fetched key.
                if (refreshed.TryGetValue(requestedKeyId, out ECDsaSecurityKey? requested)
                    && !merged.ContainsKey(requestedKeyId)
                    && merged.Count < MaximumKeys)
                    merged.Add(requestedKeyId, requested);
                foreach ((string id, ECDsaSecurityKey key) in refreshed.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    // A refresh cannot replace a bootstrap key. This also
                    // prevents a compromised endpoint from evicting the
                    // configured overlap key under the same kid.
                    if (merged.ContainsKey(id)) continue;
                    if (merged.Count < MaximumKeys) merged.Add(id, key);
                }
                _keys = merged;
                HashSet<ECDsa> retained = merged.Values.Select(value => value.ECDsa).ToHashSet();
                foreach (ECDsaSecurityKey key in refreshed.Values)
                    if (!retained.Contains(key.ECDsa)) key.ECDsa.Dispose();
                foreach (ECDsaSecurityKey key in old.Values)
                    if (!retained.Contains(key.ECDsa)) key.ECDsa.Dispose();
                refreshed = null; // all fetched handles are now owned/disposed.
            }
            finally { _keysGate.Release(); }
        }
        finally
        {
            // Fetch may be canceled or fail while waiting for the publication
            // gate. In either case no fetched ECDsa handle may escape.
            DisposeKeys(refreshed);
            _refreshGate.Release();
        }
    }

    private async Task<Dictionary<string, ECDsaSecurityKey>?> FetchKeysAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, ECDsaSecurityKey>? result = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, _keyOrigin);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using HttpResponseMessage response = await _keyHttp.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is >= 300 and < 400
                || !response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength > MaximumKeyResponseBytes) return null;
            await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(), deadline.Token);
                if (read == 0) break;
                if (buffer.Length + read > MaximumKeyResponseBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
            NodeControlCodec.RejectDuplicates(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() is < 1 or > MaximumKeys) return null;
            result = new Dictionary<string, ECDsaSecurityKey>(StringComparer.Ordinal);
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(property =>
                    property.Name is not ("kty" or "crv" or "x" or "y" or "kid" or "use" or "alg")))
                    throw new InvalidDataException("Node admission key contains an unsupported member.");
                NodeAdmissionPublicKey? key = element.Deserialize<NodeAdmissionPublicKey>();
                if (key == null || !ValidPublicKeyText(key))
                    throw new InvalidDataException("Node admission key is not a valid ES256 JWK.");
                ECDsaSecurityKey imported = ImportJwk(key);
                if (!result.TryAdd(key.Kid, imported))
                {
                    imported.ECDsa.Dispose();
                    throw new InvalidDataException("Node admission key IDs must be unique.");
                }
            }
            Dictionary<string, ECDsaSecurityKey> completed = result;
            result = null; // caller owns the complete immutable generation.
            return completed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisposeKeys(result);
            return null;
        }
        catch (OperationCanceledException)
        {
            DisposeKeys(result);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or CryptographicException
            or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return null;
        }
        finally { DisposeKeys(result); }
    }

    private static void DisposeKeys(Dictionary<string, ECDsaSecurityKey>? keys)
    {
        if (keys == null) return;
        foreach (ECDsaSecurityKey key in keys.Values) key.ECDsa.Dispose();
        keys.Clear();
    }

    private static bool ValidPublicKeyText(NodeAdmissionPublicKey key)
        => key.Kty == "EC" && key.Crv == "P-256" && key.Use == "sig" && key.Alg == "ES256"
            && key.Kid is { Length: >= 1 and <= 64 }
            && key.Kid.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static ECDsaSecurityKey LoadPemKey(NodeVerificationKey configured)
    {
        if (configured.KeyId.Length is < 1 or > 64
            || configured.KeyId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
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
            return new(key) { KeyId = configured.KeyId };
        }
        catch { key.Dispose(); throw; }
    }

    private static ECDsaSecurityKey ImportJwk(NodeAdmissionPublicKey key)
    {
        byte[] x = Base64UrlEncoder.DecodeBytes(key.X);
        byte[] y = Base64UrlEncoder.DecodeBytes(key.Y);
        if (x.Length != 32 || y.Length != 32) throw new CryptographicException("JWK coordinates must be P-256 width.");
        ECDsa? ecdsa = null;
        try
        {
            ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y }
            });
            ECDsaSecurityKey securityKey = new(ecdsa) { KeyId = key.Kid };
            ecdsa = null; // securityKey now owns the handle.
            return securityKey;
        }
        finally { ecdsa?.Dispose(); }
    }

    public void Dispose()
    {
        foreach (ECDsaSecurityKey key in _keys.Values) key.ECDsa.Dispose();
        _keysGate.Dispose(); _refreshGate.Dispose(); _gate.Dispose();
        if (_ownsKeyHttp) _keyHttp.Dispose();
    }
}
