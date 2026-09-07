using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Identity;

namespace MphRead.Mods.Network;

public sealed record ServerTicketOptions(Uri Backend, string Issuer, Guid ServerId, string ApiSecret)
{
    public override string ToString() => $"ServerTicketOptions {{ ServerId = {ServerId}, SessionId = {SessionId}, RequireTickets = {RequireTickets} }}";
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public bool RequireTickets { get; init; }
    public void Validate()
    {
        if (!Backend.IsAbsoluteUri || (Backend.Scheme != "https" && !(Backend.Scheme == "http" && Backend.IsLoopback))
            || !Uri.TryCreate(Issuer, UriKind.Absolute, out Uri? issuer) || (issuer.Scheme != "https" && !(issuer.Scheme == "http" && issuer.IsLoopback))
            || Backend.UserInfo.Length != 0 || Backend.Query.Length != 0 || Backend.Fragment.Length != 0
            || issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0
            || ServerId == Guid.Empty || SessionId == Guid.Empty || string.IsNullOrWhiteSpace(ApiSecret)
            || ApiSecret.Contains('\r') || ApiSecret.Contains('\n'))
            throw new ArgumentException("Ticket authentication requires HTTPS backend/issuer, server ID and operator secret (loopback HTTP is permitted for development).");
    }
}

public readonly record struct TicketIdentity(PlayerId PlayerId, Guid TicketId, long ExpiresAt, bool TrustedObserver = false);
public readonly record struct ValidatedTicketJoin(IPEndPoint Endpoint, JoinPacket Join, TicketIdentity? Identity);

/// <summary>Single background worker; at most 64 pending/results combined. Poll only
/// submits immutable requests and drains completed decisions. HTTP/signatures never
/// execute on the simulation owner. This instance owns one startup incarnation.</summary>
public sealed class ServerTicketAuthority : IDisposable
{
    private const int Capacity = 64;
    private readonly ServerTicketOptions _options;
    private readonly Uri _backend;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<(IPEndPoint Endpoint, JoinPacket Join)> _requests = Channel.CreateBounded<(IPEndPoint, JoinPacket)>(Capacity);
    private readonly ConcurrentQueue<ValidatedTicketJoin> _results = new();
    // Accessed exclusively by the server owner through Submit/TryRead.
    private readonly Dictionary<(IPEndPoint, ulong), JoinPacket> _pending = new();
    private readonly Task _worker;
    private readonly TicketVerifier _verifier;
    private DateTimeOffset _refreshDue;
    private DateTimeOffset _unknownRefreshDue;
    private DateTimeOffset _keysExpire;
    private bool _registered;
    public Guid ServerId => _options.ServerId;
    public Guid SessionId => _options.SessionId;
    public bool RequireTickets => _options.RequireTickets;
    public ServerTicketAuthority(ServerTicketOptions options, HttpClient? http = null)
    {
        options.Validate();
        _options = options;
        _backend = new Uri(options.Backend.AbsoluteUri.TrimEnd('/') + "/");
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 64 * 1024 };
        _ownsHttp = http == null;
        _verifier = new(options.Issuer, options.ServerId, options.SessionId);
        _worker = Task.Run(WorkAsync);
    }
    public bool Submit(IPEndPoint endpoint, in JoinPacket join)
    {
        var key = (endpoint, join.Nonce);
        if (_pending.TryGetValue(key, out JoinPacket existing)) return existing == join;
        if (_pending.Count >= Capacity) return false;
        var copy = new IPEndPoint(endpoint.Address, endpoint.Port);
        _pending.Add((copy, join.Nonce), join);
        if (_requests.Writer.TryWrite((copy, join))) return true;
        _pending.Remove(key);
        return false;
    }
    public bool TryRead(out ValidatedTicketJoin result)
    {
        if (!_results.TryDequeue(out result)) return false;
        _pending.Remove((result.Endpoint, result.Join.Nonce));
        return true;
    }
    private async Task WorkAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= _refreshDue) await RefreshAsync();
                while (_requests.Reader.TryRead(out var request))
                {
                    TicketIdentity? identity = null;
                    try
                    {
                        if (_registered && DateTimeOffset.UtcNow < _keysExpire)
                        {
                            if (!_verifier.KnowsKey(request.Join.Ticket) && DateTimeOffset.UtcNow >= _unknownRefreshDue)
                            {
                                _unknownRefreshDue = DateTimeOffset.UtcNow.AddSeconds(5);
                                await RefreshAsync();
                            }
                            identity = await _verifier.ValidateAsync(request.Join, request.Endpoint, DateTimeOffset.UtcNow);
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { identity = null; }
                    _results.Enqueue(new(request.Endpoint, request.Join, identity));
                }
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(1));
                try { await _requests.Reader.WaitToReadAsync(idle.Token); }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task RefreshAsync()
    {
        _refreshDue = DateTimeOffset.UtcNow.AddSeconds(30);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        CancellationToken token = deadline.Token;
        try
        {
            using var register = new HttpRequestMessage(HttpMethod.Put, new Uri(_backend, "v1/server/session"));
            register.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiSecret);
            register.Headers.Add("X-Server-Id", _options.ServerId.ToString("D"));
            register.Content = JsonContent.Create(new { serverIncarnation = _options.SessionId });
            using HttpResponseMessage registered = await _http.SendAsync(register, HttpCompletionOption.ResponseHeadersRead, token);
            // Previously cached keys remain valid during a transient outage, but
            // explicit authorization revocation immediately closes authentication.
            if (registered.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) _registered = false;
            registered.EnsureSuccessStatusCode();
            _registered = true;
            using HttpResponseMessage response = await _http.GetAsync(new Uri(_backend, "v1/game-ticket-keys"), HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 64 * 1024) throw new InvalidOperationException("Ticket key response exceeded bound.");
            using var stream = await response.Content.ReadAsStreamAsync(token);
            byte[] bytes = new byte[64 * 1024 + 1];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(length), token);
                if (read == 0) break;
                length += read;
            }
            if (length > 64 * 1024) throw new InvalidOperationException("Ticket key response exceeded bound.");
            _verifier.ReplaceKeys(System.Text.Encoding.UTF8.GetString(bytes, 0, length));
            _keysExpire = DateTimeOffset.UtcNow.AddMinutes(3);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or TaskCanceledException or JsonException or ArgumentException or InvalidOperationException or FormatException or SecurityTokenException)
        {
            // No credential/key bytes or operator secret are logged. Cached keys
            // can validate already-issued unexpired tickets; unknown keys fail closed.
        }
    }
    public void Dispose()
    {
        _stop.Cancel(); _requests.Writer.TryComplete();
        if (_ownsHttp) _http.Dispose();
        // Owner shutdown never blocks on a network request.
        _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}

/// <summary>Worker-confined strict ES256 ticket verification and bounded replay ownership.</summary>
internal sealed class TicketVerifier
{
    private readonly string _issuer;
    private readonly Guid _serverId, _sessionId;
    private readonly JsonWebTokenHandler _handler = new() { MaximumTokenSizeInBytes = JoinPacket.MaxTicketBytes };
    private Dictionary<string, JsonWebKey> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (TicketIdentity Identity, string Ticket, IPEndPoint Endpoint, ulong Nonce)> _used = new();
    public TicketVerifier(string issuer, Guid serverId, Guid sessionId) { _issuer = issuer; _serverId = serverId; _sessionId = sessionId; }
    public void ReplaceKeys(string json)
    {
        var set = new JsonWebKeySet(json);
        if (set.Keys.Count is < 1 or > 16) throw new ArgumentException("Invalid key set count.");
        var keys = new Dictionary<string, JsonWebKey>(StringComparer.Ordinal);
        foreach (JsonWebKey key in set.Keys)
        {
            if (key.Kty != "EC" || key.Crv != "P-256" || key.Alg != "ES256" || key.Use != "sig"
                || string.IsNullOrWhiteSpace(key.Kid) || key.Kid.Length > 32 || key.Kid.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) || !string.IsNullOrEmpty(key.D)
                || Base64UrlEncoder.DecodeBytes(key.X).Length != 32 || Base64UrlEncoder.DecodeBytes(key.Y).Length != 32
                || !keys.TryAdd(key.Kid, key)) throw new ArgumentException("Invalid public ticket key.");
        }
        _keys = keys;
    }
    public bool KnowsKey(string ticket)
    {
        try { return _keys.ContainsKey(new JsonWebToken(ticket).Kid ?? ""); }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException) { return true; }
    }
    public async Task<TicketIdentity?> ValidateAsync(JoinPacket join, IPEndPoint endpoint, DateTimeOffset now)
    {
        if (!JoinPacket.ValidTicketText(join.Ticket)) return null;
        JsonWebToken jwt;
        try { jwt = new JsonWebToken(join.Ticket); }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException) { return null; }
        if (jwt.Alg != "ES256" || !_keys.TryGetValue(jwt.Kid ?? "", out JsonWebKey? key)) return null;
        using JsonDocument claims = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
        using JsonDocument header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedHeader));
        if (HasDuplicateNames(claims.RootElement) || HasDuplicateNames(header.RootElement)
            || header.RootElement.TryGetProperty("crit", out _) || header.RootElement.TryGetProperty("jku", out _)
            || header.RootElement.TryGetProperty("jwk", out _) || header.RootElement.TryGetProperty("x5u", out _)) return null;
        JsonElement c = claims.RootElement;
        if (!Text(c, "iss", out string issuer) || issuer != _issuer
            || !Text(c, "sub", out string sub) || !PlayerId.TryParse(sub, out PlayerId player)
            || !GuidClaim(c, "aud", out Guid audience) || audience != _serverId
            || !GuidClaim(c, "sid", out Guid session) || session != _sessionId
            || !GuidClaim(c, "jti", out Guid id)
            || !Text(c, "name", out string name) || name != join.Name || name.Length is < 1 or > 16
            || name.Any(ch => ch < 32 || ch > 126) || string.IsNullOrWhiteSpace(name)
            || !Text(c, "nonce", out string nonce) || nonce != join.Nonce.ToString(CultureInfo.InvariantCulture)
            || !Number(c, "iat", out long issued) || !Number(c, "nbf", out long start) || !Number(c, "exp", out long expires)
            || issued < 0 || start != issued || expires <= issued || expires - issued > 120
            || issued > now.ToUnixTimeSeconds() + 5 || expires <= now.ToUnixTimeSeconds()) return null;
        foreach (Guid expired in _used.Where(p => p.Value.Identity.ExpiresAt <= now.ToUnixTimeSeconds()).Select(p => p.Key).ToArray()) _used.Remove(expired);
        if (_used.TryGetValue(id, out var used))
            return used.Ticket == join.Ticket && used.Nonce == join.Nonce && used.Endpoint.Equals(endpoint) ? used.Identity : null;
        if (_used.Count >= 4096) return null;
        TokenValidationResult result = await _handler.ValidateTokenAsync(join.Ticket, new TokenValidationParameters
        {
            RequireSignedTokens = true, RequireExpirationTime = true,
            ValidateIssuerSigningKey = true, IssuerSigningKey = key,
            ValidateIssuer = true, ValidIssuer = _issuer,
            ValidateAudience = true, ValidAudience = _serverId.ToString("D"),
            ValidAlgorithms = new[] { "ES256" }, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(5)
        });
        if (!result.IsValid) return null;
        bool trustedObserver = false;
        if (c.TryGetProperty("observerTrusted", out JsonElement observerClaim))
        {
            if (observerClaim.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            trustedObserver = observerClaim.GetBoolean();
        }
        var identity = new TicketIdentity(player, id, expires, trustedObserver);
        _used.Add(id, (identity, join.Ticket, new IPEndPoint(endpoint.Address, endpoint.Port), join.Nonce));
        return identity;
    }
    private static bool HasDuplicateNames(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject()) if (!names.Add(property.Name)) return true;
        return false;
    }
    private static bool Text(JsonElement c, string name, out string value)
    { value = ""; if (!c.TryGetProperty(name, out JsonElement p) || p.ValueKind != JsonValueKind.String) return false; value = p.GetString()!; return true; }
    private static bool Number(JsonElement c, string name, out long value)
    { value = 0; return c.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out value); }
    private static bool GuidClaim(JsonElement c, string name, out Guid value)
    { value = default; return Text(c, name, out string text) && text.Length == 36 && Guid.TryParseExact(text, "D", out value) && value != Guid.Empty; }
}
