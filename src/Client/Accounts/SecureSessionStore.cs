using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Accounts;

/// <summary>
/// Opaque protected-storage boundary. Platform implementations must protect the bytes at rest;
/// the common client owns their format and backend scope.
/// </summary>
public interface ISecureSessionStore
{
    ValueTask<byte[]?> ReadAsync(string backendScope, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(string backendScope, ReadOnlyMemory<byte> record,
        CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string backendScope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-local fallback for platforms without an approved protected store. Nothing survives
/// process exit, and callers receive copies so secret buffers are not shared.
/// </summary>
public sealed class MemoryOnlySessionStore : ISecureSessionStore
{
    private readonly ConcurrentDictionary<string, byte[]> _records = new(StringComparer.Ordinal);

    public ValueTask<byte[]?> ReadAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        return ValueTask.FromResult(_records.TryGetValue(backendScope, out byte[]? value)
            ? value.ToArray() : null);
    }

    public ValueTask WriteAsync(string backendScope, ReadOnlyMemory<byte> record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        if (record.Length is 0 or > SecureSessionRecordCodec.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(record));
        _records[backendScope] = record.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string backendScope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureSessionRecordCodec.ValidateScope(backendScope);
        _records.TryRemove(backendScope, out _);
        return ValueTask.CompletedTask;
    }
}

internal static class SecureSessionRecordCodec
{
    internal const int Version = 1;
    internal const int MaximumRefreshTokenCharacters = 16 * 1024;
    internal const int MaximumBytes = 20 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 4
    };
    // The protected client removes private constructor parameter names. Use a
    // parameterless contract with pinned wire names so session persistence is
    // stable before and after protection.
    private sealed class Record
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("backendScope")]
        public string BackendScope { get; init; } = "";

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; init; } = "";
    }

    internal static byte[] Encode(string backendScope, string refreshToken)
    {
        ValidateScope(backendScope);
        ValidateRefreshToken(refreshToken);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Record
        {
            Version = Version,
            BackendScope = backendScope,
            RefreshToken = refreshToken
        }, Json);
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("The protected session record is too large.");
        return bytes;
    }

    internal static bool TryDecode(string expectedBackendScope, byte[]? bytes, out string refreshToken)
    {
        refreshToken = "";
        ValidateScope(expectedBackendScope);
        if (bytes is not { Length: > 0 and <= MaximumBytes }) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            int count = 0;
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                count++;
                if (!names.Add(property.Name)
                    || property.Name is not ("version" or "backendScope" or "refreshToken")) return false;
            }
            if (count != 3
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int value) || value != Version
                || !root.TryGetProperty("backendScope", out JsonElement backend)
                || backend.ValueKind != JsonValueKind.String || backend.GetString() != expectedBackendScope
                || !root.TryGetProperty("refreshToken", out JsonElement token)
                || token.ValueKind != JsonValueKind.String || token.GetString() is not { } parsed)
                return false;
            ValidateRefreshToken(parsed);
            refreshToken = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return false;
        }
    }

    internal static void ValidateScope(string backendScope)
    {
        if (backendScope is not { Length: >= 1 and <= 2048 }
            || !Uri.TryCreate(backendScope, UriKind.Absolute, out Uri? backend)
            || backend.AbsoluteUri != backendScope || !AccountSession.IsAllowedBackend(backend))
            throw new ArgumentException("Invalid protected-session backend scope.", nameof(backendScope));
    }

    private static void ValidateRefreshToken(string refreshToken)
    {
        if (refreshToken.Length is < 1 or > MaximumRefreshTokenCharacters
            || refreshToken.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("Invalid protected-session refresh material.", nameof(refreshToken));
    }
}
