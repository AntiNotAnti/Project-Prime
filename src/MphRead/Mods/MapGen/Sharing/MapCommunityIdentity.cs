using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Validates the narrow, short-lived Community credential minted from Hunter License.
/// The player's Supabase access token never reaches the map service. Ticket signatures
/// remain private to Supabase; this service asks the Edge Function to verify them and
/// caches only the resulting creator identity until shortly before expiry.
/// </summary>
internal sealed class MapCommunityIdentityVerifier : IDisposable
{
    private const string DefaultUrl = "https://hwcjaygoistufktorbmf.supabase.co";
    private const string DefaultKey = "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedIdentity> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<MapCreatorCredential?>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _verificationSlots = new(4, 4);
    private readonly CancellationTokenSource _stop = new();
    private const int CacheCapacity = 1024;
    private long _access;

    public MapCommunityIdentityVerifier()
    {
        string url = (Environment.GetEnvironmentVariable("PROJECT_PRIME_SUPABASE_URL")
            ?? DefaultUrl).TrimEnd('/') + "/";
        string key = Environment.GetEnvironmentVariable("PROJECT_PRIME_SUPABASE_KEY")
            ?? DefaultKey;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(url, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
    }

    public async Task<MapCreatorCredential?> AuthenticateAsync(
        string? authorization, CancellationToken token)
    {
        if (authorization == null
            || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            || authorization.Length > 4096)
        {
            return null;
        }
        string ticket = authorization[7..].Trim();
        if (!ticket.StartsWith("ppm1.", StringComparison.Ordinal)
            || ticket.Length > 3072)
        {
            return null;
        }

        string cacheKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(ticket))).ToLowerInvariant();
        Task<MapCreatorCredential?> verification;
        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out CachedIdentity? cached)
                && cached.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(20))
            {
                _cache[cacheKey] = cached with { Access = ++_access };
                return cached.Credential;
            }
            _cache.Remove(cacheKey);
            if (!_pending.TryGetValue(cacheKey, out verification!))
            {
                if (_pending.Count >= CacheCapacity)
                    throw new HttpRequestException("Community identity verification is busy.", null, HttpStatusCode.ServiceUnavailable);
                // Each caller cancels only its wait. Verification is bounded by
                // HttpClient's timeout and can still serve another waiter.
                verification = Task.Run(() => VerifyAsync(ticket, cacheKey));
                _pending.Add(cacheKey, verification);
                _ = verification.ContinueWith(task => { _ = task.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        return await verification.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<MapCreatorCredential?> VerifyAsync(string ticket, string cacheKey)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        bool entered = false;
        try
        {
        await _verificationSlots.WaitAsync(deadline.Token).ConfigureAwait(false);
        entered = true;
        using var content = new StringContent(
            JsonSerializer.Serialize(new { action = "verify", ticket }),
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(
            "functions/v1/community-map-ticket", content, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        VerifiedTicket? verified = JsonSerializer.Deserialize<VerifiedTicket>(
            body, MapPackageReader.JsonOptions);
        if (verified == null
            || !verified.CreatorId.StartsWith("hunter:", StringComparison.Ordinal)
            || !Guid.TryParse(verified.CreatorId["hunter:".Length..], out _)
            || verified.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var credential = new MapCreatorCredential(
            verified.CreatorId, new string('0', 64), verified.Moderator);
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(20);
            foreach (string expired in _cache.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                _cache.Remove(expired);
            while (_cache.Count >= CacheCapacity)
                _cache.Remove(_cache.MinBy(pair => pair.Value.Access).Key);
            _cache[cacheKey] = new(credential, verified.ExpiresAt, ++_access);
        }
        return credential;
        }
        finally
        {
            if (entered) _verificationSlots.Release();
            lock (_gate) _pending.Remove(cacheKey);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
        Task[] pending;
        lock (_gate) pending = _pending.Values.Cast<Task>().ToArray();
        _ = Task.WhenAll(pending).ContinueWith(task =>
        {
            _ = task.Exception;
            _stop.Dispose();
            _verificationSlots.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record CachedIdentity(
        MapCreatorCredential Credential, DateTimeOffset ExpiresAt, long Access);

    private sealed class VerifiedTicket
    {
        [JsonPropertyName("creator_id")]
        public string CreatorId { get; set; } = "";
        [JsonPropertyName("moderator")]
        public bool Moderator { get; set; }
        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
