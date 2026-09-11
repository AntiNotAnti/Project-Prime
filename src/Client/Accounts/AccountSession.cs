using System;
using System.IO;
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
    [property: JsonRequired] string Policy = "PairwiseNormalizedV1");
public sealed record AccountRegistration(PlayerId PlayerId, bool ConfirmationRequired,
    bool ConfirmationDeliveryPending = false);

public enum AccountFailureKind
{
    InvalidCredential,
    InvalidResponse,
    RateLimited,
    ServiceUnavailable,
    TransportUnavailable,
    Timeout,
    Cancelled
}

/// <summary>
/// Typed failure from the bounded Backend HTTP boundary. Callers must classify
/// the operation before deciding whether protected refresh material may be
/// removed; HTTP status text is not a safe policy input.
/// </summary>
public sealed class AccountServiceException : HttpRequestException
{
    public AccountServiceException(string message, AccountFailureKind kind,
        HttpStatusCode? statusCode = null, string? errorCode = null, Exception? inner = null)
        : base(message, inner, statusCode)
    {
        Kind = kind;
        ErrorCode = errorCode;
    }

    public AccountFailureKind Kind { get; }
    public string? ErrorCode { get; }
}

public enum AccountRestoreState
{
    Restored,
    NoStoredSession,
    InvalidStoredSession,
    TemporarilyUnavailable
}

public sealed record AccountRestoreResult(AccountRestoreState State,
    AccountServiceException? Error = null);
/// <summary>Account credentials are sent only to the configured backend. Only refresh material may
/// cross the protected-storage boundary; passwords and access tokens remain in memory.</summary>
public sealed partial class AccountSession : IDisposable
{
    private const int MaximumSafeGetAttempts = 3;
    private const int MaximumRetryAfterSeconds = 30;
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
    /// <remarks>
    /// The bool API remains for the launcher compatibility surface. New callers
    /// should use <see cref="RestoreDetailedAsync"/> so an offline service is a
    /// recoverable state rather than an implicit sign-out.
    /// </remarks>
    public async Task<bool> RestoreAsync(CancellationToken cancel = default)
    {
        AccountRestoreResult result = await RestoreDetailedAsync(cancel).ConfigureAwait(false);
        // Preserve the established terminal failure signal for callers that
        // display a sign-in error, while temporary failures deliberately return
        // false and leave the protected record intact.
        if (result.Error is { } error && result.State == AccountRestoreState.InvalidStoredSession)
            throw error;
        return result.State == AccountRestoreState.Restored;
    }

    /// <summary>
    /// Restores a session and classifies expected Backend failures. A successful
    /// refresh is persisted before the independent <c>/v1/me</c> request so a
    /// rotated refresh token cannot be lost when identity lookup times out.
    /// </summary>
    public async Task<AccountRestoreResult> RestoreDetailedAsync(CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            ResetMemory();
            byte[]? record = await _sessionStore.ReadAsync(BackendScope, cancel).ConfigureAwait(false);
            if (record == null) return new(AccountRestoreState.NoStoredSession);
            if (!SecureSessionRecordCodec.TryDecode(BackendScope, record, out string refreshToken))
            {
                // A locally corrupt or differently scoped record can never be
                // made valid by retrying the remote service.
                await _sessionStore.DeleteAsync(BackendScope, cancel).ConfigureAwait(false);
                return new(AccountRestoreState.InvalidStoredSession);
            }
            bool refreshAccepted = false;
            try
            {
                Tokens tokens = ValidateTokens(await SendAsync<Tokens>(HttpMethod.Post, "v1/auth/refresh",
                    new { refreshToken }, null, cancel).ConfigureAwait(false));
                await PersistAsync(tokens.RefreshToken, cancel).ConfigureAwait(false);
                refreshAccepted = true;
                AccountIdentity identity = ValidateIdentity(await SendAsync<AccountIdentity>(HttpMethod.Get,
                    "v1/me", null, tokens.AccessToken, cancel).ConfigureAwait(false));
                SetMemory(tokens, identity);
                return new(AccountRestoreState.Restored);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (AccountServiceException operationError)
            {
                ResetMemory();
                // Once refresh succeeded, a failure from the independent /me
                // lookup says nothing about the refresh credential. Keep the
                // rotated token for the next attempt.
                await ClearStoredAfterFailureAsync(operationError, refreshAccepted).ConfigureAwait(false);
                return operationError.Kind == AccountFailureKind.InvalidCredential && !refreshAccepted
                    ? new(AccountRestoreState.InvalidStoredSession, operationError)
                    : new(AccountRestoreState.TemporarilyUnavailable, operationError);
            }
            catch (HttpRequestException operationError)
            {
                ResetMemory();
                // Transport failures have no proof that the refresh material is
                // invalid. Keep it for a later explicit retry.
                await ClearStoredAfterFailureAsync(operationError).ConfigureAwait(false);
                return new(AccountRestoreState.TemporarilyUnavailable,
                    new AccountServiceException("The account service is temporarily unavailable.",
                        AccountFailureKind.TransportUnavailable, inner: operationError));
            }
            catch (OperationCanceledException operationError) when (!cancel.IsCancellationRequested)
            {
                ResetMemory();
                // HttpClient uses TaskCanceledException for its request timeout.
                // It is not proof that the protected refresh material is stale.
                await ClearStoredAfterFailureAsync(operationError).ConfigureAwait(false);
                return new(AccountRestoreState.TemporarilyUnavailable,
                    new AccountServiceException("The account service request timed out.",
                        AccountFailureKind.Timeout, inner: operationError));
            }
            catch (Exception operationError) when (operationError is InvalidOperationException or JsonException)
            {
                ResetMemory();
                await ClearStoredAfterFailureAsync(operationError).ConfigureAwait(false);
                return new(AccountRestoreState.TemporarilyUnavailable,
                    new AccountServiceException("The account service returned an invalid response.",
                        AccountFailureKind.InvalidResponse, inner: operationError));
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
                license.LastOfficialDelta, license.Policy)))
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

    private async ValueTask ClearStoredAfterFailureAsync(Exception operationError,
        bool refreshAccepted = false)
    {
        if (refreshAccepted || !IsPermanentRefreshFailure(operationError)) return;
        try { await _sessionStore.DeleteAsync(BackendScope, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception cleanupError)
        {
            throw new AggregateException("The account operation failed and protected session material could not be cleared.",
                operationError, cleanupError);
        }
    }

    private static bool IsPermanentRefreshFailure(Exception operationError)
        => operationError switch
        {
            AccountServiceException { Kind: AccountFailureKind.InvalidCredential } => true,
            AggregateException aggregate => aggregate.InnerExceptions.Any(IsPermanentRefreshFailure),
            _ => false
        };

    private void ResetMemory() { _tokens = null; _expires = default; Identity = null; }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? accessToken, CancellationToken cancel)
    {
        bool retryableGet = method == HttpMethod.Get;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, path);
                if (body != null) request.Content = JsonContent.Create(body, options: Json);
                if (accessToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using HttpResponseMessage response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    AccountFailureKind kind = ClassifyFailure(response.StatusCode);
                    if (retryableGet && IsTransient(kind) && attempt + 1 < MaximumSafeGetAttempts)
                    {
                        await DelayForRetryAsync(response, attempt, cancel).ConfigureAwait(false);
                        continue;
                    }
                    string? errorCode = await ReadProblemCodeAsync(response, cancel).ConfigureAwait(false);
                    throw new AccountServiceException(DescribeFailure(response.StatusCode, errorCode),
                        kind, response.StatusCode, errorCode);
                }
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
            catch (AccountServiceException exception) when (retryableGet
                && IsTransient(exception.Kind) && attempt + 1 < MaximumSafeGetAttempts)
            {
                await DelayForRetryAsync(null, attempt, cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (retryableGet
                && attempt + 1 < MaximumSafeGetAttempts)
            {
                await DelayForRetryAsync(null, attempt, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (retryableGet
                && !cancel.IsCancellationRequested && attempt + 1 < MaximumSafeGetAttempts)
            {
                await DelayForRetryAsync(null, attempt, cancel).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(AccountFailureKind kind)
        => kind is AccountFailureKind.RateLimited or AccountFailureKind.ServiceUnavailable
            or AccountFailureKind.TransportUnavailable or AccountFailureKind.Timeout;

    private static async Task DelayForRetryAsync(HttpResponseMessage? response, int attempt,
        CancellationToken cancel)
    {
        TimeSpan delay = response?.Headers.RetryAfter?.Delta
            ?? (response?.Headers.RetryAfter?.Date is { } date
                ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
        delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0,
            MaximumRetryAfterSeconds * 1000));
        if (delay == TimeSpan.Zero)
            delay = TimeSpan.FromMilliseconds(attempt == 0 ? 150 : 400);
        delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
        await Task.Delay(delay, cancel).ConfigureAwait(false);
    }

    private static AccountFailureKind ClassifyFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AccountFailureKind.InvalidCredential,
        HttpStatusCode.RequestTimeout => AccountFailureKind.Timeout,
        HttpStatusCode.TooManyRequests => AccountFailureKind.RateLimited,
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
            => AccountFailureKind.ServiceUnavailable,
        _ => AccountFailureKind.InvalidResponse
    };

    private static string DescribeFailure(HttpStatusCode statusCode, string? code)
    {
        return code?.ToLowerInvariant() switch
        {
            "invalid_credential" or "invalid_credentials" or "invalid_refresh" =>
                "Sign-in failed or the session expired.",
            "account_unconfirmed" or "email_unconfirmed" =>
                "Confirm the account email before using this service.",
            "confirmation_delivery_pending" =>
                "The account was created, but confirmation delivery is still pending.",
            "invalid_email" or "invalid_password" or "invalid_display_name" or "invalid_confirmation" =>
                "Check the entered account values.",
            "duplicate_account" or "registration_conflict" =>
                "The requested account already exists.",
            "rate_limited" => "Too many requests. Please try again shortly.",
            "service_unavailable" or "service_busy" =>
                "The account service is temporarily unavailable.",
            _ => statusCode switch
            {
                HttpStatusCode.Unauthorized => "Sign-in failed or the session expired.",
                HttpStatusCode.Forbidden => "This account is not eligible for that action. Check email confirmation and server access.",
                HttpStatusCode.TooManyRequests => "Too many requests. Please try again shortly.",
                HttpStatusCode.BadRequest => "Check the entered account values.",
                HttpStatusCode.Conflict => "The requested account or session already exists.",
                _ => $"The account service returned HTTP {(int)statusCode}."
            }
        };
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response,
        CancellationToken cancel)
    {
        const int problemLimit = 8 * 1024;
        if (response.Content.Headers.ContentLength is > problemLimit) return null;
        try
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            byte[] buffer = new byte[problemLimit + 1];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), cancel).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > problemLimit) return null;
            using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, length));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (string name in new[] { "code", "errorCode", "error" })
            {
                if (document.RootElement.TryGetProperty(name, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 and <= 64 } code
                    && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                    return code;
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        return null;
    }

    public void Dispose() { ResetMemory(); _http.Dispose(); }
}

public static class AccountSessions
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ISecureSessionStore _store = new MemoryOnlySessionStore();
    private static int _storeConfigured;
    public static AccountSession? Current { get; private set; }

    public static void UseSecureStore(ISecureSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        // Android Activity instances are recreated while the process and its
        // AccountSession remain alive. Installing the same platform boundary
        // again must be a harmless no-op rather than a store swap or an
        // exception at the first account access.
        if (Volatile.Read(ref _storeConfigured) != 0) return;
        if (Current != null) throw new InvalidOperationException("Configure the secure session store before creating an account session.");
        _store = store;
        Volatile.Write(ref _storeConfigured, 1);
    }

    /// <summary>Installs the platform store before any AccountSession is
    /// created. Explicit test/application stores always win.</summary>
    public static void ConfigurePlatformStore()
    {
        if (Volatile.Read(ref _storeConfigured) != 0) return;
        if (Current != null) throw new InvalidOperationException("Configure the secure session store before creating an account session.");
        _store = SecureSessionStoreFactory.CreateDefault();
        Volatile.Write(ref _storeConfigured, 1);
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
            var next = new AccountSession(normalized, sessionStore: _store);
            Current = next;
            previous?.Dispose();
            if (restore) await next.RestoreAsync(cancel).ConfigureAwait(false);
            return next;
        }
        finally { Gate.Release(); }
    }
}
