using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Network;

namespace MphRead.Mods.Accounts;

public sealed record AccountIdentity(PlayerId PlayerId, bool EmailConfirmed, bool EmailEligibleForOfficialPlay);
public sealed record HunterLicense(
    [property: JsonRequired] PlayerId PlayerId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] int? FavoriteHunter,
    [property: JsonRequired] DateTimeOffset JoinedAt,
    [property: JsonRequired] int Points = 0,
    [property: JsonRequired] int Tier = 1,
    [property: JsonRequired] string Title = "Bounty Hunter",
    [property: JsonRequired] int? NextThreshold = 40,
    [property: JsonRequired] int? LastOfficialDelta = null,
    [property: JsonRequired] string Policy = "PairwiseNormalizedV1",
    [property: JsonRequired] Guid? LastOfficialMatchId = null);
public sealed record AccountRegistration(PlayerId PlayerId, bool ConfirmationRequired);
public sealed record GameTicket(string Ticket, DateTimeOffset ExpiresAt, Guid ServerId, Guid ServerIncarnation,
    string PublicAddress = "", int PublicPort = 0)
{
    public override string ToString() => $"Game ticket for {ServerId}";
    public bool TryGetEndpoint(out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (PublicPort is < 1 or > 65535 || !IPAddress.TryParse(PublicAddress, out IPAddress? address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || address.ToString() != PublicAddress) return false;
        byte first = address.GetAddressBytes()[0];
        if (first == 0 || first >= 224) return false;
        endpoint = new(address, PublicPort); return true;
    }
}

/// <summary>Account credentials are sent only to the configured backend. Only refresh material may
/// cross the protected-storage boundary; passwords and access tokens remain in memory.</summary>
public sealed partial class AccountSession : IDisposable
{
    private sealed record Tokens(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken)
    {
        public override string ToString() => "Account tokens (redacted)";
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _tokensGate = new(1, 1);
    private readonly ISecureSessionStore _sessionStore;
    private readonly TimeProvider _time;
    private Tokens? _tokens;
    private DateTimeOffset _expires;
    public Uri Backend { get; }
    public string BackendScope => Backend.AbsoluteUri;
    public AccountIdentity? Identity { get; private set; }
    public bool IsSignedIn => Identity != null && _tokens != null;

    public AccountSession(Uri backend, HttpMessageHandler? handler = null, TimeProvider? time = null,
        ISecureSessionStore? sessionStore = null)
    {
        if (!IsAllowedBackend(backend)) throw new ArgumentException("Use an HTTPS backend URL, or HTTP on loopback for local testing.", nameof(backend));
        Backend = new Uri(backend.AbsoluteUri.TrimEnd('/') + "/");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        _http.BaseAddress = Backend;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _time = time ?? TimeProvider.System;
        _sessionStore = sessionStore ?? new MemoryOnlySessionStore();
    }

    public static bool IsAllowedBackend(Uri? uri) => uri is { IsAbsoluteUri: true }
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    public Task<AccountRegistration> RegisterAsync(string email, string password, string displayName, CancellationToken cancel = default)
        => SendAsync<AccountRegistration>(HttpMethod.Post, "v1/auth/register", new { email, password, displayName }, null, cancel);

    public async Task SignInAsync(string email, string password, CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            ResetMemory();
            await _sessionStore.DeleteAsync(BackendScope, cancel).ConfigureAwait(false);
            Tokens tokens = ValidateTokens(await SendAsync<Tokens>(HttpMethod.Post, "v1/auth/login",
                new { email, password }, null, cancel).ConfigureAwait(false));
            AccountIdentity identity = ValidateIdentity(await SendAsync<AccountIdentity>(HttpMethod.Get,
                "v1/me", null, tokens.AccessToken, cancel).ConfigureAwait(false));
            await PersistAsync(tokens.RefreshToken, cancel).ConfigureAwait(false);
            SetMemory(tokens, identity);
        }
        catch { ResetMemory(); throw; }
        finally { _tokensGate.Release(); }
    }

    /// <summary>Attempts automatic sign-in from protected refresh material.</summary>
    public async Task<bool> RestoreAsync(CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            ResetMemory();
            byte[]? record = await _sessionStore.ReadAsync(BackendScope, cancel).ConfigureAwait(false);
            if (record == null) return false;
            if (!SecureSessionRecordCodec.TryDecode(BackendScope, record, out string refreshToken))
            {
                await _sessionStore.DeleteAsync(BackendScope, cancel).ConfigureAwait(false);
                return false;
            }
            try
            {
                Tokens tokens = ValidateTokens(await SendAsync<Tokens>(HttpMethod.Post, "v1/auth/refresh",
                    new { refreshToken }, null, cancel).ConfigureAwait(false));
                AccountIdentity identity = ValidateIdentity(await SendAsync<AccountIdentity>(HttpMethod.Get,
                    "v1/me", null, tokens.AccessToken, cancel).ConfigureAwait(false));
                await PersistAsync(tokens.RefreshToken, cancel).ConfigureAwait(false);
                SetMemory(tokens, identity);
                return true;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception operationError)
            {
                ResetMemory();
                await ClearStoredAfterFailureAsync(operationError).ConfigureAwait(false);
                throw;
            }
        }
        finally { _tokensGate.Release(); }
    }

    public async Task SignOutAsync(CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            ResetMemory();
            await _sessionStore.DeleteAsync(BackendScope, cancel).ConfigureAwait(false);
        }
        finally { _tokensGate.Release(); }
    }

    public async Task RevokeSessionsAsync(CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        Exception? operationError = null;
        try
        {
            string accessToken = await AccessTokenLockedAsync(cancel).ConfigureAwait(false);
            await SendAsync<JsonElement>(HttpMethod.Post, "v1/auth/revoke-sessions", null,
                accessToken, cancel).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            operationError = exception;
            throw;
        }
        finally
        {
            ResetMemory();
            try
            {
                await _sessionStore.DeleteAsync(BackendScope, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupError) when (operationError != null)
            {
                throw new AggregateException("Session revocation failed and protected session material could not be cleared.",
                    operationError, cleanupError);
            }
            finally { _tokensGate.Release(); }
        }
    }

    internal async Task ClearStoredSessionAsync(CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            ResetMemory();
            await _sessionStore.DeleteAsync(BackendScope, cancel).ConfigureAwait(false);
        }
        finally { _tokensGate.Release(); }
    }

    public async Task<HunterLicense> GetLicenseAsync(PlayerId player, CancellationToken cancel = default)
    {
        if (player.IsEmpty) throw new ArgumentException("A player identity is required.", nameof(player));
        HunterLicense license = await SendAsync<HunterLicense>(HttpMethod.Get,
            $"v1/players/{player}/license", null, null, cancel).ConfigureAwait(false);
        if (license.PlayerId != player || license.DisplayName is not { Length: >= 1 and <= 16 }
            || license.DisplayName != license.DisplayName.Trim()
            || license.DisplayName.Any(c => c is < ' ' or > '~')
            || license.FavoriteHunter is < 0 or > 6 || license.JoinedAt.Offset != TimeSpan.Zero
            || !ValidRating(new(license.Points, license.Tier, license.Title, license.NextThreshold,
                license.LastOfficialDelta, license.Policy, license.LastOfficialMatchId)))
        {
            throw new InvalidOperationException("The backend returned an invalid Hunter License.");
        }
        return license;
    }

    public async Task UpdateProfileAsync(string displayName, int favoriteHunter, CancellationToken cancel = default)
    {
        if (favoriteHunter is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(favoriteHunter));
        await SendAsync<JsonElement>(HttpMethod.Patch, "v1/me/profile", new { displayName, favoriteHunter },
            await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
    }

    public async Task ConfirmEmailAsync(PlayerId playerId, string code, CancellationToken cancel = default)
        => await SendAsync<JsonElement>(HttpMethod.Post, "v1/auth/confirm-email", new { playerId, code }, null, cancel).ConfigureAwait(false);

    public async Task ResendConfirmationAsync(string email, CancellationToken cancel = default)
        => await SendAsync<JsonElement>(HttpMethod.Post, "v1/auth/resend-confirmation", new { email }, null, cancel).ConfigureAwait(false);

    public async Task<GameTicket> GetTicketAsync(Guid serverId, ulong nonce, CancellationToken cancel = default)
    {
        if (serverId == Guid.Empty || nonce == 0) throw new ArgumentException("A server identity and join nonce are required.");
        GameTicket ticket = await SendAsync<GameTicket>(HttpMethod.Post, "v1/game-tickets",
            new { serverId, nonce = nonce.ToString(CultureInfo.InvariantCulture) },
            await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
        if (ticket.ServerId != serverId || ticket.ServerIncarnation == Guid.Empty || ticket.ExpiresAt <= _time.GetUtcNow()
            || string.IsNullOrEmpty(ticket.Ticket) || ticket.Ticket.Length > JoinPacket.MaxTicketBytes
            || !ticket.TryGetEndpoint(out _))
            throw new InvalidOperationException("The backend returned an invalid game ticket.");
        return ticket;
    }

    private async Task<string> AccessTokenAsync(CancellationToken cancel)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            return await AccessTokenLockedAsync(cancel).ConfigureAwait(false);
        }
        finally { _tokensGate.Release(); }
    }

    private async Task<string> AccessTokenLockedAsync(CancellationToken cancel)
    {
        if (_tokens == null || Identity == null)
            throw new InvalidOperationException("Sign in before joining an authenticated server.");
        if (_expires <= _time.GetUtcNow().AddSeconds(30))
        {
            try
            {
                Tokens tokens = ValidateTokens(await SendAsync<Tokens>(HttpMethod.Post, "v1/auth/refresh",
                    new { refreshToken = _tokens.RefreshToken }, null, cancel).ConfigureAwait(false));
                await PersistAsync(tokens.RefreshToken, cancel).ConfigureAwait(false);
                _tokens = tokens;
                _expires = _time.GetUtcNow().AddSeconds(tokens.ExpiresIn);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception operationError)
            {
                ResetMemory();
                await ClearStoredAfterFailureAsync(operationError).ConfigureAwait(false);
                throw;
            }
        }
        return _tokens.AccessToken;
    }

    private static Tokens ValidateTokens(Tokens tokens)
    {
        if (tokens.TokenType != "Bearer" || string.IsNullOrEmpty(tokens.AccessToken) || tokens.AccessToken.Length > 16384
            || string.IsNullOrEmpty(tokens.RefreshToken) || tokens.RefreshToken.Length > 16384 || tokens.ExpiresIn is < 1 or > 86400)
            throw new InvalidOperationException("The backend returned an invalid login response.");
        return tokens;
    }

    private static AccountIdentity ValidateIdentity(AccountIdentity identity)
    {
        if (identity.PlayerId.IsEmpty) throw new InvalidOperationException("The backend returned an invalid account identity.");
        return identity;
    }

    private void SetMemory(Tokens tokens, AccountIdentity identity)
    {
        _tokens = tokens;
        _expires = _time.GetUtcNow().AddSeconds(tokens.ExpiresIn);
        Identity = identity;
    }

    private ValueTask PersistAsync(string refreshToken, CancellationToken cancel)
        => _sessionStore.WriteAsync(BackendScope,
            SecureSessionRecordCodec.Encode(BackendScope, refreshToken), cancel);

    private async ValueTask ClearStoredAfterFailureAsync(Exception operationError)
    {
        try { await _sessionStore.DeleteAsync(BackendScope, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception cleanupError)
        {
            throw new AggregateException("The account operation failed and protected session material could not be cleared.",
                operationError, cleanupError);
        }
    }

    private void ResetMemory() { _tokens = null; _expires = default; Identity = null; }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? accessToken, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        if (accessToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Sign-in failed or the session expired.",
                HttpStatusCode.Forbidden => "This account is not eligible for that action. Check email confirmation and server access.",
                HttpStatusCode.TooManyRequests => "Too many requests. Please try again shortly.",
                HttpStatusCode.BadRequest => "Check the entered values. Passwords need at least 12 characters with uppercase, lowercase, a number and a symbol.",
                HttpStatusCode.Conflict => "The requested account or session already exists.",
                _ => $"The account service returned HTTP {(int)response.StatusCode}."
            }, null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        const int limit = 65536;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("The account response is too large.");
        using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        byte[] bytes = new byte[limit + 1];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length), cancel).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        if (length > limit) throw new InvalidOperationException("The account response is too large.");
        if (length == 0 && typeof(T) == typeof(JsonElement)) return default!;
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, length), Json)
            ?? throw new InvalidOperationException("The account response was empty.");
    }

    public void Dispose() { ResetMemory(); _http.Dispose(); }
}

public static class AccountSessions
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ISecureSessionStore _store = new MemoryOnlySessionStore();
    public static AccountSession? Current { get; private set; }

    public static void UseSecureStore(ISecureSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Current != null) throw new InvalidOperationException("Configure the secure session store before creating an account session.");
        _store = store;
    }

    public static AccountSession Configure(Uri backend)
        => ConfigureAsync(backend, restore: false).GetAwaiter().GetResult();

    public static async Task<AccountSession> ConfigureAsync(Uri backend, bool restore = true,
        CancellationToken cancel = default)
    {
        await Gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (!AccountSession.IsAllowedBackend(backend))
                throw new ArgumentException("Use an HTTPS backend URL, or HTTP on loopback for local testing.", nameof(backend));
            Uri normalized = new(backend.AbsoluteUri.TrimEnd('/') + "/");
            if (Current?.Backend == normalized)
            {
                if (restore && !Current.IsSignedIn) await Current.RestoreAsync(cancel).ConfigureAwait(false);
                return Current;
            }
            AccountSession? previous = Current;
            if (previous != null) await previous.ClearStoredSessionAsync(cancel).ConfigureAwait(false);
            var next = new AccountSession(normalized, sessionStore: _store);
            Current = next;
            previous?.Dispose();
            if (restore) await next.RestoreAsync(cancel).ConfigureAwait(false);
            return next;
        }
        finally { Gate.Release(); }
    }
}
