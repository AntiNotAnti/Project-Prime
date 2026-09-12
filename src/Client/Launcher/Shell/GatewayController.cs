using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods;
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
        "Sign in or choose Guest access to play online.", false, false,
        false, false, null, "Guest");
}

public sealed record PendingRegistration(PlayerId PlayerId, string Email);

internal enum AutomaticRestoreCommitState
{
    NotStarted,
    Pending,
    Committed,
    NotRestored,
    Abandoned
}

/// <summary>Small account boundary used by GatewayController and its tests.</summary>
public interface IPrimeGatewayAccount : IAsyncDisposable
{
    Uri Backend { get; }
    bool IsSignedIn { get; }
    AccountIdentity? Identity { get; }
    Task<bool> RestoreAsync(CancellationToken cancellationToken);
    /// <summary>Returns the restore outcome without collapsing an unavailable
    /// account service into an absent local session. The default preserves the
    /// older adapter/fake contract while new adapters can forward the typed
    /// AccountSession result.</summary>
    async Task<AccountRestoreResult> RestoreDetailedAsync(CancellationToken cancellationToken)
    {
        bool restored = await RestoreAsync(cancellationToken).ConfigureAwait(false);
        return new(restored ? AccountRestoreState.Restored : AccountRestoreState.NoStoredSession);
    }
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
    public Task<AccountRestoreResult> RestoreDetailedAsync(CancellationToken cancellationToken)
        => _session.RestoreDetailedAsync(cancellationToken);
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
    private readonly Action? _beforeAutomaticRestoreCommit;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly object _restoreLock = new();
    private IPrimeGatewayAccount? _account;
    private Task<bool>? _restoreTask;
    private AutomaticRestoreCommitState _automaticRestoreState;
    private GatewayState _state = GatewayState.Initial;
    private long _generation;
    private int _disposed;
    // Register and confirmation are non-idempotent account operations. A
    // second click must wait for the first request's result rather than
    // advancing the generation and making the first request stale.
    private int _registrationInFlight;
    private int _confirmationInFlight;

    public GatewayController(PrimeShellState shell,
        Func<CancellationToken, Task<IPrimeGatewayAccount>>? resolve = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _resolve = resolve ?? ResolveDefaultAsync;
    }

    internal GatewayController(PrimeShellState shell,
        Func<CancellationToken, Task<IPrimeGatewayAccount>> resolve,
        Action beforeAutomaticRestoreCommit)
        : this(shell, resolve)
        => _beforeAutomaticRestoreCommit = beforeAutomaticRestoreCommit;

    public GatewayState State => _state;
    public HunterLicense? License { get; private set; }
    public PendingRegistration? PendingRegistration { get; private set; }
    internal AutomaticRestoreCommitState AutomaticRestoreState
    {
        get { lock (_restoreLock) return _automaticRestoreState; }
    }
    public event EventHandler? Changed;
    public event EventHandler? IdentityChanged;

    /// <summary>Restore exactly once, even when two startup callers race.</summary>
    public Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool>? owner = null;
        long generation = 0;
        Task<bool> restore;
        lock (_restoreLock)
        {
            if (_restoreTask is null)
            {
                _automaticRestoreState = AutomaticRestoreCommitState.Pending;
                generation = Interlocked.Increment(ref _generation);
                owner = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _restoreTask = owner.Task;
            }
            restore = _restoreTask;
        }
        // Start outside _restoreLock. A fully synchronous adapter is allowed
        // to reach the commit boundary immediately, which must not keep an
        // abandonment caller from acquiring that same boundary.
        if (owner is not null)
            _ = CompleteRestoreAsync(owner, generation, cancellationToken);
        return restore.WaitAsync(cancellationToken);
    }

    private async Task CompleteRestoreAsync(TaskCompletionSource<bool> owner,
        long generation, CancellationToken cancellationToken)
    {
        try
        {
            owner.TrySetResult(await RestoreCoreAsync(generation,
                cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException error)
        {
            owner.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            owner.TrySetException(error);
        }
    }

    /// <summary>Atomically abandon only an automatic restore whose identity
    /// commit has not won. Saved credentials are intentionally untouched.</summary>
    public bool TryAbandonAutomaticRestore()
    {
        bool resetState;
        lock (_restoreLock)
        {
            if (_automaticRestoreState != AutomaticRestoreCommitState.Pending)
                return false;
            _automaticRestoreState = AutomaticRestoreCommitState.Abandoned;
            Interlocked.Increment(ref _generation);
            resetState = _state.Phase == GatewayPhase.Restoring;
        }
        if (resetState) SetState(GatewayState.Initial);
        return true;
    }

    /// <summary>Populate the immutable confirmation context used by the
    /// deterministic offline capture surface. Production registration is the
    /// only other writer.</summary>
    internal void SetPendingRegistrationForCapture(PendingRegistration? pending)
    {
        ThrowIfDisposed();
        PendingRegistration = pending;
    }

    public async Task<bool> SignInAsync(string email, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Enter an email address.", nameof(email));
        long generation = BeginTransition();
        // The controller never retains this value. Callers should clear their
        // TextBox immediately after taking a local copy.
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            SetState(_state with { Phase = GatewayPhase.SigningIn, Message = "Signing in…" });
            // A successful sign-in can replace a guest or another account;
            // never leave the old identity attached to a Node session while
            // the new license is being applied.
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            _shell.SetNodeStatus(false);
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return false;
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.SignInAsync(email.Trim(), password, cancellationToken).ConfigureAwait(false);
            return await ApplyIdentityAsync(account, generation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (!CanCommit(generation, cancellationToken)) return false;
            Fail("Sign in", error);
            return false;
        }
        finally { _transition.Release(); }
    }

    public async Task<AccountRegistration?> RegisterAsync(string email, string password,
        string displayName, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _registrationInFlight, 1, 0) != 0)
            return null;
        bool entered = false;
        long generation = 0;
        try
        {
            string normalizedEmail = NormalizeEmail(email);
            generation = BeginTransition();
            await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return null;
            SetState(_state with { Phase = GatewayPhase.Registering, Message = "Creating account…" });
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return null;
            RequireSecureAuthenticationEndpoint(account.Backend);
            AccountRegistration result = await account.RegisterAsync(normalizedEmail, password,
                displayName.Trim(), cancellationToken).ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)) return null;
            PendingRegistration = result.ConfirmationRequired
                ? new PendingRegistration(result.PlayerId, normalizedEmail)
                : null;
            string registrationMessage = !result.ConfirmationRequired
                ? "Account created. You can sign in now."
                : result.ConfirmationDeliveryPending
                    ? "Account created. Your confirmation email is still being delivered. Enter the code when it arrives."
                    : "Account created. Enter the confirmation code, then sign in.";
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = registrationMessage });
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (generation != 0 && CanCommit(generation, cancellationToken))
                Fail("Account creation", error);
            return null;
        }
        finally
        {
            if (entered) _transition.Release();
            Volatile.Write(ref _registrationInFlight, 0);
        }
    }

    public async Task<bool> ConfirmPendingAsync(string code,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _confirmationInFlight, 1, 0) != 0)
            return false;
        bool entered = false;
        long generation = 0;
        try
        {
            PendingRegistration pending = PendingRegistration
                ?? throw new InvalidOperationException("No email confirmation is pending.");
            generation = BeginTransition();
            await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)
                || PendingRegistration != pending) return false;
            SetState(_state with { Phase = GatewayPhase.Confirming, Message = "Confirming email…" });
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return false;
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.ConfirmEmailAsync(pending.PlayerId, code.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)
                || PendingRegistration != pending) return false;
            PendingRegistration = null;
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = "Email confirmed. Sign in to refresh eligibility." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (generation != 0 && CanCommit(generation, cancellationToken))
                Fail("Email confirmation", error);
            return false;
        }
        finally
        {
            if (entered) _transition.Release();
            Volatile.Write(ref _confirmationInFlight, 0);
        }
    }

    public async Task<bool> ResendPendingAsync(
        CancellationToken cancellationToken = default)
    {
        PendingRegistration pending = PendingRegistration
            ?? throw new InvalidOperationException("No email confirmation is pending.");
        long generation = BeginTransition();
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)
                || PendingRegistration != pending) return false;
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return false;
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.ResendConfirmationAsync(pending.Email, cancellationToken)
                .ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)
                || PendingRegistration != pending) return false;
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = "If confirmation is needed, a new code will be sent." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (CanCommit(generation, cancellationToken)) Fail("Resending confirmation", error);
            return false;
        }
        finally { _transition.Release(); }
    }

    public async Task<bool> ResendForEmailAsync(string email,
        CancellationToken cancellationToken = default)
    {
        bool entered = false;
        long generation = 0;
        try
        {
            generation = BeginTransition();
            string normalizedEmail = NormalizeEmail(email);
            await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return false;
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.ResendConfirmationAsync(normalizedEmail, cancellationToken)
                .ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)) return false;
            SetState(_state with { Phase = GatewayPhase.Gateway,
                Message = "If confirmation is needed, a new code will be sent." });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (generation != 0 && CanCommit(generation, cancellationToken))
                Fail("Resending confirmation", error);
            return false;
        }
        finally
        {
            if (entered) _transition.Release();
        }
    }

    /// <summary>Explicitly select anonymous access; auth failure never calls this.</summary>
    public async Task<bool> UseGuestAsync(CancellationToken cancellationToken = default)
    {
        long generation = BeginTransition();
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return false;
            if (account.IsSignedIn) await account.SignOutAsync(cancellationToken).ConfigureAwait(false);
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)) return false;
            _shell.SetNodeStatus(false);
            License = null;
            PendingRegistration = null;
            _shell.SelectGuest(LauncherPrefs.PlayerName);
            SetState(new GatewayState(GatewayPhase.Guest,
                "Guest access selected. Choose a server from Play.", false, true,
                false, false, null, LauncherPrefs.PlayerName));
            IdentityChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (CanCommit(generation, cancellationToken)) Fail("Guest access", error);
            return false;
        }
        finally { _transition.Release(); }
    }

    public async Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        long generation = BeginTransition();
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            if (_account != null && _account.IsSignedIn)
            {
                RequireSecureAuthenticationEndpoint(_account.Backend);
                await _account.SignOutAsync(cancellationToken).ConfigureAwait(false);
            }
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)) return false;
            _shell.SetNodeStatus(false);
            License = null;
            PendingRegistration = null;
            _shell.ClearIdentity();
            SetState(GatewayState.Initial with { Message = "Signed out. Choose sign in or explicit Guest access." });
            IdentityChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (CanCommit(generation, cancellationToken)) Fail("Sign out", error);
            return false;
        }
        finally { _transition.Release(); }
    }

    public async Task<bool> UpdateProfileAsync(string displayName, int favoriteHunter,
        CancellationToken cancellationToken = default)
    {
        long generation = BeginTransition();
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            IPrimeGatewayAccount account = RequireSignedIn();
            RequireSecureAuthenticationEndpoint(account.Backend);
            await account.UpdateProfileAsync(displayName.Trim(), favoriteHunter, cancellationToken)
                .ConfigureAwait(false);
            return await ApplyIdentityAsync(account, generation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (CanCommit(generation, cancellationToken)) Fail("Profile update", error);
            return false;
        }
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
            throw new ArgumentException("Use an HTTPS server address or HTTP on loopback for local testing.", nameof(backend));
        long generation = BeginTransition();
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool identityCleared = false;
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            await NodeSessions.DisconnectAsync().ConfigureAwait(false);
            if (!CanCommit(generation, cancellationToken)) return false;
            // Detach before the awaited disposal. A newer auth intent may be
            // queued while disposal is pending; it must never observe or
            // reuse an adapter whose replacement has already begun.
            IPrimeGatewayAccount? previousAccount = _account;
            _account = null;
            License = null;
            PendingRegistration = null;
            _shell.ClearIdentity();
            _shell.SetBackendStatus(false);
            identityCleared = true;
            if (previousAccount != null)
                await previousAccount.DisposeAsync().ConfigureAwait(false);
            // A queued auth transition may supersede the generation, but it
            // cannot execute until this serialized replacement completes.
            // Disposal of the controller itself is the only reason to stop.
            if (Volatile.Read(ref _disposed) != 0) return false;
            LauncherPrefs.BackendAddress = new Uri(backend.AbsoluteUri.TrimEnd('/') + "/").AbsoluteUri;
            LauncherPrefs.Save();
            lock (_restoreLock)
            {
                _restoreTask = null;
                _automaticRestoreState = AutomaticRestoreCommitState.NotStarted;
            }
            SetState(GatewayState.Initial with { Message = "Server saved. Sign in or choose Guest access." });
            IdentityChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (identityCleared && IsCurrentGeneration(generation))
                SetBackendFailure("Server change canceled. Sign in or choose Guest access.",
                    notifyIdentity: true);
            throw;
        }
        catch (Exception error)
        {
            // Account disposal is part of the replacement boundary. If it
            // fails, the old adapter is no longer safe to reuse, so leave the
            // shell explicitly signed out and keep the previous preference
            // untouched. A later explicit sign-in can resolve a fresh adapter.
            if (identityCleared && IsCurrentGeneration(generation))
                SetBackendFailure("The online service could not be changed. Try again.",
                    notifyIdentity: true, error);
            else if (CanCommit(generation, cancellationToken))
                Fail("Backend change", error);
            return false;
        }
        finally { _transition.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation);
        PendingRegistration = null;
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

    private async Task<bool> RestoreCoreAsync(long generation,
        CancellationToken cancellationToken)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!CanCommit(generation, cancellationToken)) return false;
            SetState(_state with { Phase = GatewayPhase.Restoring, Message = "Restoring secure session…" });
            IPrimeGatewayAccount? account = await GetAccountAsync(generation,
                cancellationToken).ConfigureAwait(false);
            if (account is null) return false;
            if (!IsSecureAuthenticationEndpoint(account.Backend))
            {
                if (!CanCommit(generation, cancellationToken)) return false;
                SetState(GatewayState.Initial with
                {
                    Message = "Continue as Guest or sign in with an available account service."
                });
                return false;
            }
            RequireSecureAuthenticationEndpoint(account.Backend);
            AccountRestoreResult restore = await account.RestoreDetailedAsync(cancellationToken)
                .ConfigureAwait(false);
            if (restore.State != AccountRestoreState.Restored)
            {
                if (!CanCommit(generation, cancellationToken)) return false;
                string message = restore.State switch
                {
                    AccountRestoreState.NoStoredSession
                        => "No saved session was found. Sign in or continue as Guest.",
                    AccountRestoreState.TemporarilyUnavailable
                        => "The account service is temporarily unavailable. Try again, or continue as Guest.",
                    AccountRestoreState.InvalidStoredSession
                        => "Your saved session is no longer valid. Sign in or continue as Guest.",
                    _ => "Your saved session could not be restored. Sign in or continue as Guest."
                };
                SetState(GatewayState.Initial with { Message = message });
                _shell.SetBackendStatus(true);
                return false;
            }
            if (!CanCommit(generation, cancellationToken)) return false;
            return await ApplyIdentityAsync(account, generation, cancellationToken,
                    automaticRestore: true)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (CanCommit(generation, cancellationToken)) Fail("Session restore", error);
            return false;
        }
        finally
        {
            MarkAutomaticRestoreNotRestored();
            _transition.Release();
        }
    }

    private async Task<bool> ApplyIdentityAsync(IPrimeGatewayAccount account,
        long generation, CancellationToken cancellationToken,
        bool automaticRestore = false)
    {
        if (!CanCommit(generation, cancellationToken)) return false;
        AccountIdentity identity = account.Identity
            ?? throw new InvalidOperationException("The account service returned no identity.");
        HunterLicense license = await account.GetLicenseAsync(identity.PlayerId, cancellationToken)
            .ConfigureAwait(false);
        if (automaticRestore)
        {
            _beforeAutomaticRestoreCommit?.Invoke();
            lock (_restoreLock)
            {
                if (!CanCommit(generation, cancellationToken)
                    || _automaticRestoreState != AutomaticRestoreCommitState.Pending)
                    return false;
                _automaticRestoreState = AutomaticRestoreCommitState.Committed;
                CommitIdentity(identity, license);
            }
        }
        else
        {
            if (!CanCommit(generation, cancellationToken)) return false;
            CommitIdentity(identity, license);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        IdentityChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void CommitIdentity(AccountIdentity identity, HunterLicense license)
    {
        License = license;
        PendingRegistration = null;
        // Account display name is an authenticated identity, not the local
        // guest preference. Keep LauncherPrefs.PlayerName untouched so a
        // signed-out/guest session can retain its own device-local name.
        _shell.SetIdentity(identity.PlayerId, license.DisplayName,
            identity.EmailEligibleForOfficialPlay);
        _state = new GatewayState(GatewayPhase.SignedIn, "Identity restored.", true,
            false, identity.EmailConfirmed, identity.EmailEligibleForOfficialPlay,
            identity.PlayerId, license.DisplayName);
    }

    private void MarkAutomaticRestoreNotRestored()
    {
        lock (_restoreLock)
        {
            if (_automaticRestoreState == AutomaticRestoreCommitState.Pending)
                _automaticRestoreState = AutomaticRestoreCommitState.NotRestored;
        }
    }

    private async Task<IPrimeGatewayAccount?> GetAccountAsync(long generation,
        CancellationToken cancellationToken)
    {
        if (_account != null)
            return CanCommit(generation, cancellationToken) ? _account : null;
        IPrimeGatewayAccount account = await _resolve(cancellationToken).ConfigureAwait(false);
        if (!CanCommit(generation, cancellationToken))
        {
            await account.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        // Installing a new account object is an account replacement boundary.
        PendingRegistration = null;
        _account = account;
        _shell.SetBackendStatus(true);
        return account;
    }

    private IPrimeGatewayAccount RequireSignedIn()
        => _account is { IsSignedIn: true } account
            ? account
            : throw new InvalidOperationException("Sign in before updating your profile.");

    private static async Task<IPrimeGatewayAccount> ResolveDefaultAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(LauncherPrefs.BackendAddress?.Trim(), UriKind.Absolute, out Uri? backend)
            || !AccountSession.IsAllowedBackend(backend))
            throw new InvalidOperationException("Configure a valid HTTPS server address in Settings first.");
        AccountSession account = await AccountSessions.ConfigureAsync(backend, restore: false,
            cancellationToken).ConfigureAwait(false);
        return new AccountSessionGatewayAdapter(account);
    }

    private static void RequireSecureAuthenticationEndpoint(Uri backend)
    {
        if (IsSecureAuthenticationEndpoint(backend)) return;
        throw new InvalidOperationException("Account sign-in is unavailable for this server.");
    }

    private static bool IsSecureAuthenticationEndpoint(Uri backend)
        => backend.Scheme == Uri.UriSchemeHttps
            || backend.Scheme == Uri.UriSchemeHttp && backend.IsLoopback;

    private long BeginTransition() => Interlocked.Increment(ref _generation);

    private bool CanCommit(long generation, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && Volatile.Read(ref _disposed) == 0
            && Volatile.Read(ref _generation) == generation;

    private bool IsCurrentGeneration(long generation)
        => Volatile.Read(ref _disposed) == 0
            && Volatile.Read(ref _generation) == generation;

    private static string NormalizeEmail(string email)
    {
        if (String.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Enter an email address.", nameof(email));
        return email.Trim().ToLowerInvariant();
    }

    private void SetState(GatewayState state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string operation, Exception error)
    {
        DebugLog.Line("gateway", $"{operation} failed ({error.GetType().Name}).");
        string message = operation switch
        {
            "Account creation" => RegistrationFailureMessage(error),
            "Sign in" => "Sign in could not be completed. Check your details and try again.",
            "Email confirmation" => "Email confirmation could not be completed. Check the code and try again.",
            "Resending confirmation" => "A new confirmation code could not be sent. Try again.",
            "Guest access" => "Guest access could not be selected. Try again.",
            "Sign out" => "Sign out could not be completed. Try again.",
            "Profile update" => "Your profile could not be updated. Check your details and try again.",
            "Session restore" => "Your saved session could not be restored. Sign in or choose Guest access.",
            "Backend change" => "The online service could not be changed. Try again.",
            _ => "Could not complete that request. Try again."
        };
        SetState(_state with { Phase = GatewayPhase.Failed, Message = message });
        _shell.NotifyRoute("gateway-error", PrimeNotificationKind.Error, message);
    }

    private static string RegistrationFailureMessage(Exception error)
    {
        if (error is ArgumentException)
            return "Enter a valid email address.";
        if (error is not AccountServiceException accountError)
            return "Could not create the account. Try again.";
        return accountError.ErrorCode?.ToLowerInvariant() switch
        {
            "invalid_email" => "Enter a valid email address.",
            "invalid_password" =>
                "Use at least 12 characters with uppercase, lowercase, a number, and a symbol.",
            "invalid_display_name" => "Display name must be 1–16 standard characters.",
            "duplicate_account" or "registration_conflict" =>
                "An account with that email already exists. Sign in or resend confirmation.",
            "confirmation_delivery_unavailable" =>
                "Confirmation email is temporarily unavailable. Try again later.",
            "rate_limited" => "Too many registration attempts. Try again shortly.",
            _ => accountError.Kind switch
            {
                AccountFailureKind.RateLimited =>
                    "Too many registration attempts. Try again shortly.",
                AccountFailureKind.ServiceUnavailable or AccountFailureKind.TransportUnavailable
                    or AccountFailureKind.Timeout =>
                    "The account service is temporarily unavailable. Try again later.",
                _ => "Could not create the account. Check your details and try again."
            }
        };
    }

    private void SetBackendFailure(string message, bool notifyIdentity,
        Exception? error = null)
    {
        if (error != null)
        {
            DebugLog.Line("gateway", "Backend replacement could not release the previous "
                + $"account ({error.GetType().Name}).");
        }
        SetState(GatewayState.Initial with
        {
            Phase = GatewayPhase.Failed,
            Message = message
        });
        _shell.NotifyRoute("gateway-error", PrimeNotificationKind.Error, message);
        if (notifyIdentity) IdentityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
