using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class GatewayControllerAuthTests
{
    [Fact]
    public async Task RegistrationOwnsNormalizedConfirmationContext()
    {
        var account = new AccountFake();
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);

        AccountRegistration? registration = await gateway.RegisterAsync(
            "  Pilot@Example.TEST  ", "secret", "Pilot");

        Assert.NotNull(registration);
        Assert.Equal(new PendingRegistration(account.PlayerId,
            "pilot@example.test"), gateway.PendingRegistration);

        Assert.True(await gateway.ResendPendingAsync());
        Assert.Equal("pilot@example.test", account.ResentEmail);

        Assert.True(await gateway.ConfirmPendingAsync(" 123456 "));
        Assert.Equal(account.PlayerId, account.ConfirmedPlayerId);
        Assert.Equal("123456", account.ConfirmationCode);
        Assert.Null(gateway.PendingRegistration);
    }

    [Theory]
    [InlineData(false, false, "Account created. You can sign in now.")]
    [InlineData(true, false, "Account created. Enter the confirmation code, then sign in.")]
    [InlineData(true, true, "Account created. Your confirmation email is still being delivered. Enter the code when it arrives.")]
    public async Task RegistrationMessageReflectsConfirmationDeliveryState(bool required,
        bool deliveryPending, string expectedMessage)
    {
        var account = new AccountFake
        {
            RegistrationConfirmationRequired = required,
            RegistrationDeliveryPending = deliveryPending
        };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);

        Assert.NotNull(await gateway.RegisterAsync("pilot@example.test", "secret", "Pilot"));
        Assert.Equal(expectedMessage, gateway.State.Message);
    }

    [Fact]
    public async Task SuccessfulIdentityAndSignOutClearPendingRegistration()
    {
        var account = new AccountFake();
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);

        await gateway.RegisterAsync("pilot@example.test", "secret", "Pilot");
        Assert.NotNull(gateway.PendingRegistration);
        Assert.True(await gateway.SignInAsync("pilot@example.test", "secret"));
        Assert.Null(gateway.PendingRegistration);

        await gateway.RegisterAsync("pilot@example.test", "secret", "Pilot");
        Assert.NotNull(gateway.PendingRegistration);
        Assert.True(await gateway.SignOutAsync());
        Assert.Null(gateway.PendingRegistration);
    }

    [Fact]
    public async Task AccountReplacementAndDisposalClearPendingRegistration()
    {
        var account = new AccountFake { FailSignIn = true };
        using var shell = new PrimeShellState();
        var gateway = Create(shell, account);
        gateway.SetPendingRegistrationForCapture(new PendingRegistration(
            account.PlayerId, "pilot@example.test"));

        Assert.False(await gateway.SignInAsync("pilot@example.test", "secret"));
        Assert.Null(gateway.PendingRegistration);

        gateway.SetPendingRegistrationForCapture(new PendingRegistration(
            account.PlayerId, "pilot@example.test"));
        await gateway.DisposeAsync();
        Assert.Null(gateway.PendingRegistration);
    }

    [Fact]
    public async Task BackendReplacementClearsPendingRegistration()
    {
        string previousBackend = MphRead.Mods.Launcher.LauncherPrefs.BackendAddress;
        var account = new AccountFake();
        using var shell = new PrimeShellState();
        var gateway = Create(shell, account);
        try
        {
            await gateway.RegisterAsync("pilot@example.test", "secret", "Pilot");
            Assert.NotNull(gateway.PendingRegistration);

            Assert.True(await gateway.ConfigureBackendAsync(
                new Uri("https://replacement-backend.test/")));

            Assert.Null(gateway.PendingRegistration);
            Assert.Equal(1, account.DisposeCalls);
        }
        finally
        {
            MphRead.Mods.Launcher.LauncherPrefs.BackendAddress = previousBackend;
            MphRead.Mods.Launcher.LauncherPrefs.Save();
            await gateway.DisposeAsync();
        }
    }

    [Fact]
    public async Task QueuedSignInAfterDelayedBackendDisposalUsesFreshAccount()
    {
        string previousBackend = MphRead.Mods.Launcher.LauncherPrefs.BackendAddress;
        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDispose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var oldAccount = new AccountFake
        {
            DisposeStarted = disposeStarted,
            AllowDispose = allowDispose.Task
        };
        var freshAccount = new AccountFake();
        int resolutions = 0;
        using var shell = new PrimeShellState();
        var gateway = new GatewayController(shell, _ =>
        {
            IPrimeGatewayAccount account = Interlocked.Increment(ref resolutions) == 1
                ? oldAccount : freshAccount;
            return Task.FromResult(account);
        });
        try
        {
            Assert.True(await gateway.SignInAsync("old@example.test", "secret"));
            Task<bool> configure = gateway.ConfigureBackendAsync(
                new Uri("https://replacement-backend.test/"));
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Task<bool> queuedSignIn = gateway.SignInAsync(
                "fresh@example.test", "secret");
            allowDispose.TrySetResult();

            Assert.True(await configure);
            Assert.True(await queuedSignIn);
            Assert.Equal(2, resolutions);
            Assert.Equal(1, oldAccount.SignInCalls);
            Assert.Equal(0, oldAccount.UsesAfterDispose);
            Assert.Equal(1, freshAccount.SignInCalls);
            Assert.True(shell.SignedIn);
        }
        finally
        {
            allowDispose.TrySetResult();
            MphRead.Mods.Launcher.LauncherPrefs.BackendAddress = previousBackend;
            MphRead.Mods.Launcher.LauncherPrefs.Save();
            await gateway.DisposeAsync();
        }
    }

    [Fact]
    public async Task LateAbandonedRestoreCannotRaiseIdentityAfterSuccessfulSignIn()
    {
        var account = new DeferredAccountFake();
        using var shell = new PrimeShellState();
        await using var gateway = new GatewayController(shell,
            _ => Task.FromResult<IPrimeGatewayAccount>(account));
        int identityEvents = 0;
        gateway.IdentityChanged += (_, _) => identityEvents++;

        Task<bool> restore = gateway.RestoreAsync();
        await account.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(gateway.TryAbandonAutomaticRestore());
        Assert.Equal(0, account.SignOutCalls);

        account.CompleteRestore();
        Assert.False(await restore);
        Assert.Equal(0, identityEvents);
        Assert.False(shell.HasNetworkIdentity);

        Assert.True(await gateway.SignInAsync("pilot@example.test", "secret"));
        Assert.True(shell.SignedIn);
        Assert.Equal(1, identityEvents);
        Assert.Equal(0, account.SignOutCalls);
    }

    [Fact]
    public async Task DisposalClearsPendingAndRejectsLateRestoreCommit()
    {
        var account = new DeferredAccountFake();
        using var shell = new PrimeShellState();
        var gateway = new GatewayController(shell,
            _ => Task.FromResult<IPrimeGatewayAccount>(account));
        gateway.SetPendingRegistrationForCapture(new PendingRegistration(
            account.PlayerId, "pilot@example.test"));
        int identityEvents = 0;
        gateway.IdentityChanged += (_, _) => identityEvents++;

        Task<bool> restore = gateway.RestoreAsync();
        await account.RestoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task dispose = gateway.DisposeAsync().AsTask();
        Assert.Null(gateway.PendingRegistration);

        account.CompleteRestore();
        Assert.False(await restore);
        await dispose;
        Assert.False(shell.HasNetworkIdentity);
        Assert.Equal(0, identityEvents);
    }

    [Fact]
    public async Task AbandonAtIdentityCommitBarrierPreventsLateShellMutationAndEvent()
    {
        var account = new AccountFake { RestoreResult = true };
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var shell = new PrimeShellState();
        await using var gateway = new GatewayController(shell,
            _ => Task.FromResult<IPrimeGatewayAccount>(account), () =>
            {
                entered.TrySetResult();
                release.Wait();
            });
        int identityEvents = 0;
        gateway.IdentityChanged += (_, _) => identityEvents++;

        Task<bool> restore = Task.Run(() => gateway.RestoreAsync());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(gateway.TryAbandonAutomaticRestore());
        release.Set();

        Assert.False(await restore);
        Assert.Equal(AutomaticRestoreCommitState.Abandoned,
            gateway.AutomaticRestoreState);
        Assert.False(shell.HasNetworkIdentity);
        Assert.Equal(0, identityEvents);
    }

    [Fact]
    public async Task CompletedRestoreCommitCannotBeRetroactivelyAbandoned()
    {
        var account = new AccountFake { RestoreResult = true };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);
        int identityEvents = 0;
        gateway.IdentityChanged += (_, _) => identityEvents++;

        Assert.True(await gateway.RestoreAsync());
        Assert.False(gateway.TryAbandonAutomaticRestore());

        Assert.Equal(AutomaticRestoreCommitState.Committed,
            gateway.AutomaticRestoreState);
        Assert.True(shell.SignedIn);
        Assert.Equal(1, identityEvents);
    }

    [Fact]
    public async Task TemporaryRestoreShowsRecoverableGatewayStateWithoutForcingSignIn()
    {
        var account = new AccountFake
        {
            RestoreState = AccountRestoreState.TemporarilyUnavailable,
            RestoreError = new AccountServiceException("service unavailable",
                AccountFailureKind.ServiceUnavailable)
        };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);

        Assert.False(await gateway.RestoreAsync());
        Assert.Equal(GatewayPhase.Gateway, gateway.State.Phase);
        Assert.Contains("temporarily unavailable", gateway.State.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(shell.SignedIn);
        Assert.False(shell.GuestSelected);
        Assert.True(await gateway.UseGuestAsync());
        Assert.True(shell.GuestSelected);
    }

    private static GatewayController Create(PrimeShellState shell,
        AccountFake account)
        => new(shell, _ => Task.FromResult<IPrimeGatewayAccount>(account));

    private sealed class AccountFake : IPrimeGatewayAccount
    {
        public PlayerId PlayerId { get; } = new(Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        public Uri Backend => new("https://backend.test/");
        public bool IsSignedIn { get; private set; }
        public AccountIdentity? Identity { get; private set; }
        public bool RestoreResult { get; init; }
        public AccountRestoreState RestoreState { get; init; } = AccountRestoreState.NoStoredSession;
        public AccountServiceException? RestoreError { get; init; }
        public bool RegistrationConfirmationRequired { get; init; } = true;
        public bool RegistrationDeliveryPending { get; init; }
        public bool FailSignIn { get; init; }
        public TaskCompletionSource? DisposeStarted { get; init; }
        public Task? AllowDispose { get; init; }
        public PlayerId? ConfirmedPlayerId { get; private set; }
        public string? ConfirmationCode { get; private set; }
        public string? ResentEmail { get; private set; }
        public int DisposeCalls { get; private set; }
        public int SignInCalls { get; private set; }
        public int UsesAfterDispose { get; private set; }
        public bool Disposed { get; private set; }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            AccountRestoreResult result = await RestoreDetailedAsync(cancellationToken);
            if (result.State == AccountRestoreState.InvalidStoredSession && result.Error is { } error)
                throw error;
            return result.State == AccountRestoreState.Restored;
        }

        public Task<AccountRestoreResult> RestoreDetailedAsync(CancellationToken cancellationToken)
        {
            if (RestoreResult || RestoreState == AccountRestoreState.Restored)
            {
                IsSignedIn = true;
                Identity = new AccountIdentity(PlayerId, true, true);
                return Task.FromResult(new AccountRestoreResult(AccountRestoreState.Restored));
            }
            return Task.FromResult(new AccountRestoreResult(RestoreState, RestoreError));
        }

        public Task SignInAsync(string email, string password,
            CancellationToken cancellationToken)
        {
            SignInCalls++;
            if (Disposed) UsesAfterDispose++;
            if (FailSignIn)
                throw new InvalidOperationException("Sign in rejected.");
            IsSignedIn = true;
            Identity = new AccountIdentity(PlayerId, true, true);
            return Task.CompletedTask;
        }

        public Task<AccountRegistration> RegisterAsync(string email,
            string password, string displayName,
            CancellationToken cancellationToken)
            => Task.FromResult(new AccountRegistration(PlayerId,
                RegistrationConfirmationRequired, RegistrationDeliveryPending));

        public Task ConfirmEmailAsync(PlayerId playerId, string code,
            CancellationToken cancellationToken)
        {
            ConfirmedPlayerId = playerId;
            ConfirmationCode = code;
            return Task.CompletedTask;
        }

        public Task ResendConfirmationAsync(string email,
            CancellationToken cancellationToken)
        {
            ResentEmail = email;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            IsSignedIn = false;
            Identity = null;
            return Task.CompletedTask;
        }

        public Task<HunterLicense> GetLicenseAsync(PlayerId playerId,
            CancellationToken cancellationToken)
            => Task.FromResult(new HunterLicense(playerId, "Pilot", 0,
                DateTimeOffset.UnixEpoch));

        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            DisposeStarted?.TrySetResult();
            if (AllowDispose is not null)
                await AllowDispose.ConfigureAwait(false);
            Disposed = true;
        }
    }

    private sealed class DeferredAccountFake : IPrimeGatewayAccount
    {
        private readonly TaskCompletionSource<bool> _restore = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PlayerId PlayerId { get; } = new(Guid.Parse(
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        public TaskCompletionSource RestoreStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri Backend => new("https://backend.test/");
        public bool IsSignedIn { get; private set; }
        public AccountIdentity? Identity { get; private set; }
        public int SignOutCalls { get; private set; }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            RestoreStarted.TrySetResult();
            bool restored = await _restore.Task.ConfigureAwait(false);
            IsSignedIn = restored;
            Identity = restored ? new AccountIdentity(PlayerId, true, true) : null;
            return restored;
        }

        public void CompleteRestore() => _restore.TrySetResult(true);

        public Task SignInAsync(string email, string password,
            CancellationToken cancellationToken)
        {
            IsSignedIn = true;
            Identity = new AccountIdentity(PlayerId, true, true);
            return Task.CompletedTask;
        }

        public Task<AccountRegistration> RegisterAsync(string email,
            string password, string displayName,
            CancellationToken cancellationToken)
            => Task.FromResult(new AccountRegistration(PlayerId, true));
        public Task ConfirmEmailAsync(PlayerId playerId, string code,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResendConfirmationAsync(string email,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            SignOutCalls++;
            IsSignedIn = false;
            Identity = null;
            return Task.CompletedTask;
        }
        public Task<HunterLicense> GetLicenseAsync(PlayerId playerId,
            CancellationToken cancellationToken)
            => Task.FromResult(new HunterLicense(playerId, "Pilot", 0,
                DateTimeOffset.UnixEpoch));
        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
