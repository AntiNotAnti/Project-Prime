using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MphRead.Identity;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Shared;

/// <summary>Node-approved identity and reservation. Guest identities never become PlayerId values.</summary>
public sealed record WorkerAdmissionClaims(NodeId NodeId, Guid NodeIncarnation, WorkerId WorkerId,
    Guid WorkerIncarnation, LobbyId LobbyId, MatchId MatchId, WireMatchId WireMatchId,
    Guid NodeSessionId, PlayerId? PlayerId, Guid? GuestSessionId, SeatRole Role, byte SeatId,
    string Name, ulong JoinNonce, long IssuedAt, long ExpiresAt, Guid TicketId)
{
    public override string ToString() => $"WorkerAdmissionClaims {{ MatchId = {MatchId}, SeatId = {SeatId}, Role = {Role} }}";
    public void Validate()
    {
        ContractGuard.Id(NodeId.Value); ContractGuard.Id(NodeIncarnation); ContractGuard.Id(WorkerId.Value);
        ContractGuard.Id(WorkerIncarnation); ContractGuard.Id(LobbyId.Value); ContractGuard.Id(MatchId.Value);
        ContractGuard.Id(NodeSessionId); ContractGuard.Id(TicketId);
        if (WireMatchId.Value == 0 || JoinNonce == 0 || IssuedAt < 0 || ExpiresAt <= IssuedAt || ExpiresAt - IssuedAt > 120
            || PlayerId.HasValue == GuestSessionId.HasValue || PlayerId is { IsEmpty: true } || GuestSessionId == Guid.Empty
            || Role is not (SeatRole.Player or SeatRole.Observer) || SeatId >= 32 || Role == SeatRole.Player && SeatId >= 8
            || String.IsNullOrWhiteSpace(Name) || Name.Length > 16 || Name.Any(c => c < 32 || c > 126))
            throw new ArgumentException("Invalid Worker admission claims.");
    }
}

/// <summary>Node-owned P-256 signer. Only ExportPublicKey crosses Node/Worker IPC.</summary>
public sealed class WorkerAdmissionIssuer : IDisposable
{
    // The signed payload is base64url encoded before crossing any HTML/JSON
    // boundary. Avoiding HTML-only escapes keeps every valid 16-byte roster
    // name within the authenticated 1,024-byte UDP envelope.
    private static readonly JsonSerializerOptions TicketJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly ECDsa _key;
    private readonly object _sync = new();
    public string KeyId { get; }
    public WorkerAdmissionIssuer(string keyId)
    {
        WorkerAdmissionEncoding.ValidateKeyId(keyId);
        KeyId = keyId;
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }
    public string ExportPublicKey() { lock (_sync) return Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()); }
    public string Issue(WorkerAdmissionClaims claims)
    {
        claims.Validate();
        string header = WorkerAdmissionEncoding.Encode(JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "ES256", typ = "fp-worker-admission", kid = KeyId }, TicketJson));
        // Compact names keep the complete signed reservation within the existing 1024-byte UDP cap.
        var payload = new Dictionary<string, object?>
        {
            ["n"] = claims.NodeId.Value.ToString("N"), ["ni"] = claims.NodeIncarnation.ToString("N"),
            ["w"] = claims.WorkerId.Value.ToString("N"), ["wi"] = claims.WorkerIncarnation.ToString("N"),
            ["l"] = claims.LobbyId.Value.ToString("N"), ["m"] = claims.MatchId.Value.ToString("N"),
            ["wm"] = claims.WireMatchId.Value, ["ns"] = claims.NodeSessionId.ToString("N"),
            ["p"] = claims.PlayerId?.Value.ToString("N"), ["g"] = claims.GuestSessionId?.ToString("N"),
            ["r"] = (byte)claims.Role, ["s"] = claims.SeatId, ["name"] = claims.Name,
            ["nonce"] = claims.JoinNonce, ["iat"] = claims.IssuedAt, ["exp"] = claims.ExpiresAt,
            ["jti"] = claims.TicketId.ToString("N")
        };
        string signingInput = header + "." + WorkerAdmissionEncoding.Encode(
            JsonSerializer.SerializeToUtf8Bytes(payload, TicketJson));
        byte[] signature;
        lock (_sync) signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string ticket = signingInput + "." + WorkerAdmissionEncoding.Encode(signature);
        if (String.IsNullOrEmpty(ticket) || ticket.Length > JoinPacket.MaxRoutedTicketBytes) throw new ArgumentException("Worker admission ticket exceeds the gameplay datagram bound.");
        return ticket;
    }
    public void Dispose() { lock (_sync) _key.Dispose(); }
}

/// <summary>Public-key-only, match-bound verification with atomic bounded nonce consumption.</summary>
public sealed class WorkerAdmissionVerifier : IDisposable
{
    private readonly object _sync = new();
    private sealed record VerificationKey(ECDsa Key, long? RetireAt);
    private readonly Dictionary<string, VerificationKey> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, long> _used = new();
    private readonly MatchSpec _spec;
    private readonly MatchPlacement _placement;
    private readonly int _capacity;
    public WorkerAdmissionVerifier(MatchSpec spec, MatchPlacement placement, int replayCapacity = 4096)
    {
        spec.Validate(); placement.Validate();
        if (spec.MatchId != placement.MatchId || replayCapacity is < 1 or > 65536) throw new ArgumentException("Invalid admission scope.");
        _spec = spec; _placement = placement; _capacity = replayCapacity;
    }
    // A new key starts a fixed 125-second overlap for the previous active key.
    // Later rotations never extend that deadline; at most 16 unexpired IDs are retained.
    public bool RetirePublicKey(string keyId)
    {
        lock (_sync)
        {
            if (!_keys.Remove(keyId, out VerificationKey? key)) return false;
            key.Key.Dispose(); return true;
        }
    }
    public void UpdatePublicKey(string keyId, string publicKey, DateTimeOffset? now = null)
    {
        WorkerAdmissionEncoding.ValidateKeyId(keyId);
        if (publicKey.Length > 1024) throw new ArgumentException("Public key exceeds bound.");
        ECDsa key = ECDsa.Create();
        try
        {
            byte[] bytes = Convert.FromBase64String(publicKey);
            key.ImportSubjectPublicKeyInfo(bytes, out int read);
            if (read != bytes.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new ArgumentException("Expected a P-256 public key.");
            lock (_sync)
            {
                long seconds = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
                foreach (string expired in _keys.Where(pair => pair.Value.RetireAt <= seconds).Select(pair => pair.Key).ToArray())
                { _keys[expired].Key.Dispose(); _keys.Remove(expired); }
                if (!_keys.ContainsKey(keyId) && _keys.Count >= 16) throw new ArgumentException("Public key set is full.");
                foreach (string prior in _keys.Where(pair => pair.Key != keyId && !pair.Value.RetireAt.HasValue).Select(pair => pair.Key).ToArray())
                    _keys[prior] = _keys[prior] with { RetireAt = seconds + 125 };
                if (_keys.Remove(keyId, out VerificationKey? previous)) previous.Key.Dispose();
                _keys.Add(keyId, new VerificationKey(key, null));
            }
        }
        catch { key.Dispose(); throw; }
    }
    public bool TryConsume(string ticket, in JoinPacket join, DateTimeOffset now, out WorkerAdmissionClaims? claims)
    {
        claims = null;
        if (String.IsNullOrEmpty(ticket) || ticket.Length > JoinPacket.MaxRoutedTicketBytes || !JoinPacket.ValidTicketText(ticket)
            || join.Protocol != NetHeader.Version || join.WireMatchId != _placement.WireMatchId.Value) return false;
        try
        {
            string[] parts = ticket.Split('.');
            using JsonDocument header = JsonDocument.Parse(WorkerAdmissionEncoding.Decode(parts[0]));
            using JsonDocument payload = JsonDocument.Parse(WorkerAdmissionEncoding.Decode(parts[1]));
            JsonElement h = header.RootElement, c = payload.RootElement;
            if (!WorkerAdmissionEncoding.ExactFields(h, "alg", "typ", "kid")
                || h.GetProperty("alg").GetString() != "ES256" || h.GetProperty("typ").GetString() != "fp-worker-admission"
                || !WorkerAdmissionEncoding.ExactFields(c, "n", "ni", "w", "wi", "l", "m", "wm", "ns", "p", "g", "r", "s", "name", "nonce", "iat", "exp", "jti")) return false;
            var decoded = new WorkerAdmissionClaims(new(WorkerAdmissionEncoding.Id(c, "n")), WorkerAdmissionEncoding.Id(c, "ni"),
                new(WorkerAdmissionEncoding.Id(c, "w")), WorkerAdmissionEncoding.Id(c, "wi"), new(WorkerAdmissionEncoding.Id(c, "l")),
                new(WorkerAdmissionEncoding.Id(c, "m")), new(c.GetProperty("wm").GetUInt32()), WorkerAdmissionEncoding.Id(c, "ns"),
                c.GetProperty("p").ValueKind == JsonValueKind.Null ? null : new PlayerId(WorkerAdmissionEncoding.Id(c, "p")),
                c.GetProperty("g").ValueKind == JsonValueKind.Null ? null : WorkerAdmissionEncoding.Id(c, "g"),
                (SeatRole)c.GetProperty("r").GetByte(), c.GetProperty("s").GetByte(), c.GetProperty("name").GetString()!,
                c.GetProperty("nonce").GetUInt64(), c.GetProperty("iat").GetInt64(), c.GetProperty("exp").GetInt64(), WorkerAdmissionEncoding.Id(c, "jti"));
            decoded.Validate();
            long seconds = now.ToUnixTimeSeconds();
            RosterSeat? seat = _spec.Roster.FirstOrDefault(seat => seat.SeatId == decoded.SeatId);
            if (decoded.NodeId != _spec.NodeId || decoded.NodeIncarnation != _spec.NodeIncarnation
                || decoded.WorkerId != _placement.WorkerId || decoded.WorkerIncarnation != _placement.WorkerIncarnation
                || decoded.LobbyId != _spec.LobbyId || decoded.MatchId != _spec.MatchId || decoded.WireMatchId != _placement.WireMatchId
                || decoded.JoinNonce != join.Nonce || decoded.Name != join.Name || decoded.IssuedAt > seconds + 5 || decoded.ExpiresAt <= seconds
                || (decoded.Role == SeatRole.Observer) != join.Observer || seat == null || seat.Role != decoded.Role
                || seat.PlayerId != decoded.PlayerId || seat.GuestSessionId != decoded.GuestSessionId || seat.DisplayName != decoded.Name
                || seat.Hunter != join.Hunter) return false;
            byte[] signature = WorkerAdmissionEncoding.Decode(parts[2]);
            if (signature.Length != 64) return false;
            lock (_sync)
            {
                if (!_keys.TryGetValue(h.GetProperty("kid").GetString()!, out VerificationKey? key) || key.RetireAt <= seconds
                    || !key.Key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return false;
                foreach (Guid expired in _used.Where(pair => pair.Value <= seconds).Select(pair => pair.Key).ToArray()) _used.Remove(expired);
                if (_used.Count >= _capacity || !_used.TryAdd(decoded.TicketId, decoded.ExpiresAt)) return false;
                claims = decoded;
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or InvalidOperationException or CryptographicException or OverflowException)
        { return false; }
    }
    public void Dispose() { lock (_sync) { foreach (VerificationKey key in _keys.Values) key.Key.Dispose(); _keys.Clear(); _used.Clear(); } }
}

internal static class WorkerAdmissionEncoding
{
    internal static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] Decode(string value)
    {
        byte[] bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (Encode(bytes) != value) throw new FormatException("Noncanonical ticket encoding.");
        return bytes;
    }
    internal static Guid Id(JsonElement value, string name)
    {
        string? text = value.GetProperty(name).GetString();
        if (text?.Length != 32 || !Guid.TryParseExact(text, "N", out Guid id) || id == Guid.Empty) throw new FormatException("Invalid ticket identity.");
        return id;
    }
    internal static bool ExactFields(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject()) if (!remaining.Remove(property.Name)) return false;
        return remaining.Count == 0;
    }
    internal static void ValidateKeyId(string keyId)
    {
        if (String.IsNullOrEmpty(keyId) || keyId.Length > 32 || keyId.Any(c => !(Char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Invalid signing key ID.");
    }
}
