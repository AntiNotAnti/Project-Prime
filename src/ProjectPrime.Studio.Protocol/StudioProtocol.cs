using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectPrime.Studio.Protocol;

/// <summary>Independent of gameplay, replay and package versions.</summary>
public static class StudioProtocol
{
    public const int StudioIpcVersion = 1;
    public const int MaximumFrameBytes = 64 * 1024;
    public const int MaximumPathCharacters = 4096;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan GameRequestTimeout = TimeSpan.FromMinutes(2);
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 8
    };
}

public enum StudioOpenKind { Home, Map, Replay, Clip, MapWorkspace, ReplayWorkspace }

public sealed record StudioOpenRequest(Guid RequestId, StudioOpenKind Kind, string? Path = null,
    bool Recover = false, bool SafeMode = false)
{
    public string? Validate()
    {
        if (RequestId == Guid.Empty) return "The Studio request ID is missing.";
        if (!Enum.IsDefined(Kind)) return "The Studio workspace kind is invalid.";
        if (Kind is StudioOpenKind.Home or StudioOpenKind.MapWorkspace or StudioOpenKind.ReplayWorkspace)
            return Path == null ? null : "A workspace request cannot contain a document path.";
        if (string.IsNullOrWhiteSpace(Path) || Path.Length > StudioProtocol.MaximumPathCharacters
            || Path.IndexOf('\0') >= 0 || !System.IO.Path.IsPathFullyQualified(Path))
            return "A bounded absolute document path is required.";
        string extension = System.IO.Path.GetExtension(Path);
        bool supported = Kind switch
        {
            StudioOpenKind.Map => extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ppmap", StringComparison.OrdinalIgnoreCase),
            StudioOpenKind.Replay => extension.Equals(".ppdemo", StringComparison.OrdinalIgnoreCase),
            StudioOpenKind.Clip => extension.Equals(".ppclip", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return supported ? null : "The document extension does not match its workspace.";
    }
}

public sealed record StudioRequestResult(bool Accepted, string? Error = null)
{
    public static StudioRequestResult Success { get; } = new(true);
    public static StudioRequestResult Rejected(string error) => new(false, error);
}

/// <summary>Small control messages only. Document bytes never travel in this envelope.</summary>
public sealed record StudioIpcEnvelope(int Version, string Type, Guid RequestId = default,
    StudioOpenRequest? OpenRequest = null, StudioRequestResult? Result = null,
    string? Nonce = null, string? Proof = null, string? Error = null,
    StudioGameRequest? GameRequest = null, StudioGameResult? GameResult = null)
{
    [JsonRequired]
    public long CreatedUtc { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public string? ValidateShape()
    {
        bool open = OpenRequest != null, result = Result != null, game = GameRequest != null, gameResult = GameResult != null;
        bool nonce = Nonce != null, proof = Proof != null, error = Error != null;
        bool valid = Type switch
        {
            "Challenge" => RequestId == Guid.Empty && nonce && !proof && !error && !open && !result && !game && !gameResult,
            "Hello" or "Welcome" => RequestId == Guid.Empty && proof && !nonce && !error && !open && !result && !game && !gameResult,
            "Open" => RequestId != Guid.Empty && open && OpenRequest!.RequestId == RequestId && !nonce && !proof && !error && !result && !game && !gameResult,
            "Game" => RequestId != Guid.Empty && game && GameRequest!.RequestId == RequestId && !nonce && !proof && !error && !result && !open && !gameResult,
            "Ping" or "Cancel" => RequestId != Guid.Empty && !nonce && !proof && !error && !open && !result && !game && !gameResult,
            "Result" => RequestId != Guid.Empty && (result ^ gameResult) && !nonce && !proof && !error && !open && !game,
            "Error" => error && Error!.Length <= 2048 && !nonce && !proof && !open && !result && !game && !gameResult,
            _ => false
        };
        return valid ? null : "The Studio IPC message shape is invalid.";
    }
}

public sealed class StudioProtocolException(string message) : IOException(message);

public static class StudioIpcFraming
{
    public static async Task WriteAsync(Stream stream, StudioIpcEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.ValidateShape() is { } error) throw new StudioProtocolException(error);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, StudioProtocol.JsonOptions);
        if (bytes.Length == 0 || bytes.Length > StudioProtocol.MaximumFrameBytes)
            throw new StudioProtocolException("The Studio IPC frame exceeds the size limit.");
        byte[] length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<StudioIpcEnvelope> ReadAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        byte[] length = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        int count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= 0 || count > StudioProtocol.MaximumFrameBytes)
            throw new StudioProtocolException("The Studio IPC frame length is invalid.");
        byte[] bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        try
        {
            var envelope = JsonSerializer.Deserialize<StudioIpcEnvelope>(bytes, StudioProtocol.JsonOptions)
                ?? throw new StudioProtocolException("The Studio IPC frame is empty.");
            if (string.IsNullOrEmpty(envelope.Type) || envelope.Type.Length > 32)
                throw new StudioProtocolException("The Studio IPC message type is invalid.");
            if (envelope.ValidateShape() is { } error) throw new StudioProtocolException(error);
            return envelope;
        }
        catch (JsonException)
        {
            throw new StudioProtocolException("The Studio IPC frame is malformed.");
        }
    }
}

/// <summary>Mutual challenge proofs. These secrets are local IPC capabilities, never account credentials.</summary>
public static class StudioIpcAuthentication
{
    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string CreateProof(string secret, string nonce, string side, int version)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > 64 || string.IsNullOrEmpty(nonce) || nonce.Length > 64)
            throw new StudioProtocolException("The Studio IPC authentication data is invalid.");
        byte[] key;
        byte[] challenge;
        try
        {
            key = Convert.FromBase64String(secret);
            challenge = Convert.FromBase64String(nonce);
        }
        catch (FormatException) { throw new StudioProtocolException("The Studio IPC authentication data is invalid."); }
        if (key.Length != 32 || challenge.Length != 32 || (side != "client" && side != "server"))
            throw new StudioProtocolException("The Studio IPC authentication data is invalid.");
        try
        {
            byte[] transcript = Encoding.UTF8.GetBytes($"ProjectPrime.Studio|{version}|{side}|{nonce}");
            return Convert.ToBase64String(HMACSHA256.HashData(key, transcript));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static bool VerifyProof(string secret, string nonce, string side, int version, string? proof)
    {
        if (proof == null || proof.Length > 64) return false;
        try
        {
            byte[] actual = Convert.FromBase64String(proof);
            byte[] expected = Convert.FromBase64String(CreateProof(secret, nonce, side, version));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or StudioProtocolException) { return false; }
    }
}
