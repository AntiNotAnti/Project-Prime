using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Network;

namespace MphRead.Mods.Accounts;

public sealed record AccountIdentity(PlayerId PlayerId, bool EmailConfirmed, bool EmailEligibleForOfficialPlay);
public sealed record HunterLicense(PlayerId PlayerId, string DisplayName, int? FavoriteHunter, DateTimeOffset JoinedAt);
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

/// <summary>Account credentials live only in memory and are sent only to the configured backend.</summary>
public sealed partial class AccountSession : IDisposable
{
    private sealed record Tokens(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken)
    {
        public override string ToString() => "Account tokens (redacted)";
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _tokensGate = new(1, 1);
    private readonly TimeProvider _time;
    private Tokens? _tokens;
    private DateTimeOffset _expires;
    public Uri Backend { get; }
    public AccountIdentity? Identity { get; private set; }
    public bool IsSignedIn => Identity != null && _tokens != null;

    public AccountSession(Uri backend, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        if (!IsAllowedBackend(backend)) throw new ArgumentException("Use an HTTPS backend URL, or HTTP on loopback for local testing.", nameof(backend));
        Backend = new Uri(backend.AbsoluteUri.TrimEnd('/') + "/");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        _http.BaseAddress = Backend;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _time = time ?? TimeProvider.System;
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
            Identity = null;
            _tokens = null;
            SetTokens(await SendAsync<Tokens>(HttpMethod.Post, "v1/auth/login", new { email, password }, null, cancel).ConfigureAwait(false));
            Identity = await SendAsync<AccountIdentity>(HttpMethod.Get, "v1/me", null, _tokens!.AccessToken, cancel).ConfigureAwait(false);
            if (Identity.PlayerId.IsEmpty) throw new InvalidOperationException("The backend returned an invalid account identity.");
        }
        catch { _tokens = null; Identity = null; throw; }
        finally { _tokensGate.Release(); }
    }

    public async Task SignOutAsync(CancellationToken cancel = default)
    {
        await _tokensGate.WaitAsync(cancel).ConfigureAwait(false);
        try { _tokens = null; Identity = null; }
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
            || license.FavoriteHunter is < 0 or > 6)
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
            if (_tokens == null || Identity == null) throw new InvalidOperationException("Sign in before joining an authenticated server.");
            if (_expires <= _time.GetUtcNow().AddSeconds(30))
            {
                try { SetTokens(await SendAsync<Tokens>(HttpMethod.Post, "v1/auth/refresh", new { refreshToken = _tokens.RefreshToken }, null, cancel).ConfigureAwait(false)); }
                catch { _tokens = null; Identity = null; throw; }
            }
            return _tokens.AccessToken;
        }
        finally { _tokensGate.Release(); }
    }

    private void SetTokens(Tokens tokens)
    {
        if (tokens.TokenType != "Bearer" || string.IsNullOrEmpty(tokens.AccessToken) || tokens.AccessToken.Length > 16384
            || string.IsNullOrEmpty(tokens.RefreshToken) || tokens.RefreshToken.Length > 16384 || tokens.ExpiresIn is < 1 or > 86400)
            throw new InvalidOperationException("The backend returned an invalid login response.");
        _tokens = tokens;
        _expires = _time.GetUtcNow().AddSeconds(tokens.ExpiresIn);
    }

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

    public void Dispose() { _tokens = null; Identity = null; _http.Dispose(); }
}

public static class AccountSessions
{
    public static AccountSession? Current { get; private set; }
    public static AccountSession Configure(Uri backend)
    {
        if (Current?.Backend == new Uri(backend.AbsoluteUri.TrimEnd('/') + "/")) return Current;
        var next = new AccountSession(backend);
        Current?.Dispose();
        return Current = next;
    }
}
