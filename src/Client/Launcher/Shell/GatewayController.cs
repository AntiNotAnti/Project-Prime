using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

public enum GatewayPhase
{
    Gateway,
    Restoring,
    SigningIn,
    Registering,
    Confirming,
    SignedIn,
    Guest,
    Failed
}

public sealed record GatewayState(GatewayPhase Phase, string Message, bool SignedIn,
    bool GuestSelected, bool EmailConfirmed, bool OfficialEligible, PlayerId? PlayerId,
    string DisplayName)
{
    public static GatewayState Initial => new(GatewayPhase.Gateway,
        "Sign in or choose explicit Guest access to enter the network.", false, false,
        false, false, null, "Guest");
}

/// <summary>Small account boundary used by GatewayController and its tests.</summary>
public interface IPrimeGatewayAccount : IAsyncDisposable
{
    Uri Backend { get; }
    bool IsSignedIn { get; }
    AccountIdentity? Identity { get; }
    Task<bool> RestoreAsync(CancellationToken cancellationToken);
    Task SignInAsync(string email, string password, CancellationToken cancellationToken);
    Task<AccountRegistration> RegisterAsync(string email, string password, string displayName,
        CancellationToken cancellationToken);
    Task ConfirmEmailAsync(PlayerId playerId, string code, CancellationToken cancellationToken);
    Task ResendConfirmationAsync(string email, CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
    Task<HunterLicense> GetLicenseAsync(PlayerId playerId, CancellationToken cancellationToken);
    Task UpdateProfileAsync(string displayName, int favoriteHunter, CancellationToken cancellationToken);
}

internal sealed class AccountSessionGatewayAdapter : IPrimeGatewayAccount
{
    private readonly AccountSession _session;

    public AccountSessionGatewayAdapter(AccountSession session) => _session = session;
    public Uri Backend => _session.Backend;
    public bool IsSignedIn => _session.IsSignedIn;
    public AccountIdentity? Identity => _session.Identity;
    public Task<bool> RestoreAsync(CancellationToken cancellationToken) => _session.RestoreAsync(cancellationToken);
    public Task SignInAsync(string email, string password, CancellationToken cancellationToken)
        => _session.SignInAsync(email, password, cancellationToken);
    public Task<AccountRegistration> RegisterAsync(string email, string password, string displayName,
        CancellationToken cancellationToken)
        => _session.RegisterAsync(email, password, displayName, cancellationToken);
    public Task ConfirmEmailAsync(PlayerId playerId, string code, CancellationToken cancellationToken)
        => _session.ConfirmEmailAsync(playerId, code, cancellationToken);
    public Task ResendConfirmationAsync(string email, CancellationToken cancellationToken)
        => _session.ResendConfirmationAsync(email, cancellationToken);
    public Task SignOutAsync(CancellationToken cancellationToken) => _session.SignOutAsync(cancellationToken);
    public Task<HunterLicense> GetLicenseAsync(PlayerId playerId, CancellationToken cancellationToken)
        => _session.GetLicenseAsync(playerId, cancellationToken);
    public Task UpdateProfileAsync(string displayName, int favoriteHunter, CancellationToken cancellationToken)
        => _session.UpdateProfileAsync(displayName, favoriteHunter, cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Account/session commands for the conditional Gateway screen. All identity
/// transitions are serialized and restore is attempted at most once per
/// controller lifetime. Guest mode is only entered through UseGuestAsync.
/// </summary>
public sealed class GatewayController : IAsyncDisposable
{
    private readonly PrimeShellState _shell;
    private readonly Func<CancellationToken, Task<IPrimeGatewayAccount>> _resolve;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly object _restoreLock = new();
    private IPrimeGatewayAccount? _account;
    private Task<bool>? _restoreTask;
    private GatewayState _state = GatewayState.Initial;
    private int _disposed;

    public GatewayController(PrimeShellState shell,
        Func<CancellationToken, Task<IPrimeGatewayAccount>>? resolve = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _resolve = resolve ?? ResolveDefaultAsync;
    }

    public GatewayState State => _state;
    public HunterLicense? License { get; private set; }
    public PlayerId? PendingConfirmationPlayerId { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler? IdentityChanged;

    /// <summary>Restore exactly once, even when two startup callers race.</summary>
    public Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        lock (_restoreLock)
        {
            _restoreTask ??= RestoreCoreAsync(cancellationToken);
            return _restoreTask.WaitAsync(cancellationToken);
        }
    }

    public async Task<bool> SignInAsync(string email, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Enter an email address.", nameof(email));
        // The controller never retains this value. Callers should clear their
        // TextBox immediately after taking a local copy.
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            SetState(_state with { Phase = GatewayPhase.SigningIn, Message = "Signing in…" });
            // A successful sign-in can replace a guest or another account;
            // never leave the old identity attached to a Node session while
            // the new license is being applied.
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            _shell.SetNodeStatus(false);
            IPrimeGatewayAccount account = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.SignInAsync(email.Trim(), password, cancellationToken).ConfigureAwait(false);
            return await ApplyIdentityAsync(account, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Fail("Sign in", error);
            return false;
        }
        finally { _transition.Release(); }
    }

    public async Task<AccountRegistration?> RegisterAsync(string email, string password,
        string displayName, CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            SetState(_state with { Phase = GatewayPhase.Registering, Message = "Creating account…" });
            IPrimeGatewayAccount account = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
            RequireSecureAuthenticationEndpoint(account.Backend);
            AccountRegistration result = await account.RegisterAsync(email.Trim(), password,
                displayName.Trim(), cancellationToken).ConfigureAwait(false);
            PendingConfirmationPlayerId = result.ConfirmationRequired ? result.PlayerId : null;
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = result.ConfirmationRequired
                    ? "Account created. Enter the confirmation code, then sign in."
                    : "Account created. You can sign in now." });
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Account creation", error); return null; }
        finally { _transition.Release(); }
    }

    public async Task<bool> ConfirmEmailAsync(PlayerId playerId, string code,
        CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            SetState(_state with { Phase = GatewayPhase.Confirming, Message = "Confirming email…" });
            IPrimeGatewayAccount account = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.ConfirmEmailAsync(playerId, code.Trim(), cancellationToken).ConfigureAwait(false);
            PendingConfirmationPlayerId = null;
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = "Email confirmed. Sign in to refresh eligibility." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Email confirmation", error); return false; }
        finally { _transition.Release(); }
    }

    public async Task<bool> ResendConfirmationAsync(string email,
        CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            IPrimeGatewayAccount account = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.ResendConfirmationAsync(email.Trim(), cancellationToken).ConfigureAwait(false);
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = "If confirmation is needed, a new code will be sent." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Resending confirmation", error); return false; }
        finally { _transition.Release(); }
    }

    /// <summary>Explicitly select anonymous access; auth failure never calls this.</summary>
    public async Task<bool> UseGuestAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            IPrimeGatewayAccount account = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
            if (account.IsSignedIn) await account.SignOutAsync(cancellationToken).ConfigureAwait(false);
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            _shell.SetNodeStatus(false);
            License = null;
            PendingConfirmationPlayerId = null;
            _shell.SelectGuest(LauncherPrefs.PlayerName);
            SetState(new GatewayState(GatewayPhase.Guest,
                "Guest access selected. Choose a Node from Play.", false, true,
                false, false, null, LauncherPrefs.PlayerName));
            IdentityChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Guest access", error); return false; }
        finally { _transition.Release(); }
    }

    public async Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_account != null && _account.IsSignedIn)
            {
                RequireSecureAuthenticationEndpoint(_account.Backend);
                await _account.SignOutAsync(cancellationToken).ConfigureAwait(false);
            }
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            _shell.SetNodeStatus(false);
            License = null;
            PendingConfirmationPlayerId = null;
            _shell.ClearIdentity();
            SetState(GatewayState.Initial with { Message = "Signed out. Choose sign in or explicit Guest access." });
            IdentityChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Sign out", error); return false; }
        finally { _transition.Release(); }
    }

    public async Task<bool> UpdateProfileAsync(string displayName, int favoriteHunter,
        CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            IPrimeGatewayAccount account = RequireSignedIn();
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.UpdateProfileAsync(displayName.Trim(), favoriteHunter, cancellationToken)
                .ConfigureAwait(false);
            return await ApplyIdentityAsync(account, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Profile update", error); return false; }
        finally { _transition.Release(); }
    }

    /// <summary>
    /// Switch Backend origins through the same serialized transition as auth.
    /// This disconnects Node first and never leaves an identity scoped to the
    /// old origin in shell state.
    /// </summary>
    public async Task<bool> ConfigureBackendAsync(Uri backend,
        CancellationToken cancellationToken = default)
    {
        if (!AccountSession.IsAllowedBackend(backend))
            throw new ArgumentException("Use an HTTPS Backend address or HTTP on loopback for local testing.", nameof(backend));
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            if (_account != null) await _account.DisposeAsync().ConfigureAwait(false);
            _account = null;
            License = null;
            PendingConfirmationPlayerId = null;
            _shell.ClearIdentity();
            _shell.SetBackendStatus(false);
            LauncherPrefs.BackendAddress = new Uri(backend.AbsoluteUri.TrimEnd('/') + "/").AbsoluteUri;
            LauncherPrefs.Save();
            lock (_restoreLock) _restoreTask = null;
            SetState(GatewayState.Initial with { Message = "Backend saved. Sign in or choose explicit Guest access." });
            IdentityChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally { _transition.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Wait for the current serialized transition before disposing the
        // semaphore or account. Deactivate cancels shell-owned requests, but
        // a caller may have supplied a longer-lived token and disposal must
        // not race that operation.
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_account != null) await _account.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _transition.Release();
            _transition.Dispose();
        }
    }

    private async Task<bool> RestoreCoreAsync(CancellationToken cancellationToken)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            SetState(_state with { Phase = GatewayPhase.Restoring, Message = "Restoring secure session…" });
            IPrimeGatewayAccount account = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
            if (!IsSecureAuthenticationEndpoint(account.Backend))
            {
                SetState(GatewayState.Initial with
                {
                    Message = "Continue as Guest or sign in with an available account service."
                });
                return false;
            }
            RequireSecureAuthenticationEndpoint(account.Backend);
            if (!await account.RestoreAsync(cancellationToken).ConfigureAwait(false))
            {
                SetState(GatewayState.Initial with { Message = "No saved session was found." });
                _shell.SetBackendStatus(true);
                return false;
            }
            return await ApplyIdentityAsync(account, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail("Session restore", error); return false; }
        finally { _transition.Release(); }
    }

    private async Task<bool> ApplyIdentityAsync(IPrimeGatewayAccount account,
        CancellationToken cancellationToken)
    {
        AccountIdentity identity = account.Identity
            ?? throw new InvalidOperationException("The account service returned no identity.");
        HunterLicense license = await account.GetLicenseAsync(identity.PlayerId, cancellationToken)
            .ConfigureAwait(false);
        License = license;
        // Account display name is an authenticated identity, not the local
        // guest preference. Keep LauncherPrefs.PlayerName untouched so a
        // signed-out/guest session can retain its own device-local name.
        _shell.SetIdentity(identity.PlayerId, license.DisplayName, identity.EmailEligibleForOfficialPlay);
        SetState(new GatewayState(GatewayPhase.SignedIn, "Identity restored.", true, false,
            identity.EmailConfirmed, identity.EmailEligibleForOfficialPlay, identity.PlayerId,
            license.DisplayName));
        IdentityChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task<IPrimeGatewayAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        if (_account != null) return _account;
        _account = await _resolve(cancellationToken).ConfigureAwait(false);
        _shell.SetBackendStatus(true);
        return _account;
    }

    private IPrimeGatewayAccount RequireSignedIn()
        => _account is { IsSignedIn: true } account
            ? account
            : throw new InvalidOperationException("Sign in before updating your profile.");

    private static async Task<IPrimeGatewayAccount> ResolveDefaultAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(LauncherPrefs.BackendAddress?.Trim(), UriKind.Absolute, out Uri? backend)
            || !AccountSession.IsAllowedBackend(backend))
            throw new InvalidOperationException("Configure a valid HTTPS Backend address in Settings first.");
        AccountSession account = await AccountSessions.ConfigureAsync(backend, restore: false,
            cancellationToken).ConfigureAwait(false);
        return new AccountSessionGatewayAdapter(account);
    }

    private static void RequireSecureAuthenticationEndpoint(Uri backend)
    {
        if (IsSecureAuthenticationEndpoint(backend)) return;
        throw new InvalidOperationException("Account sign-in is unavailable for this Backend.");
    }

    private static bool IsSecureAuthenticationEndpoint(Uri backend)
        => backend.Scheme == Uri.UriSchemeHttps
            || backend.Scheme == Uri.UriSchemeHttp && backend.IsLoopback;

    private void SetState(GatewayState state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string operation, Exception error)
    {
        string message = error.Message.Length > 0 ? error.Message : $"{operation} failed.";
        SetState(_state with { Phase = GatewayPhase.Failed, Message = message });
        _shell.Notify(PrimeNotificationKind.Error, message);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
