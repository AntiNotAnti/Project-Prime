using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class GatewayControllerRobustnessTests
{
    [Fact]
    public async Task DuplicateRegistrationDoesNotInvalidateTheFirstRequest()
    {
        var account = new GatewayAccountFake
        {
            RegistrationCompletion = new TaskCompletionSource<AccountRegistration>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);

        Task<AccountRegistration?> first = gateway.RegisterAsync(
            "pilot@example.test", "secret", "Pilot");
        await account.RegisterStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(await gateway.RegisterAsync(
            "pilot@example.test", "secret", "Pilot"));
        Assert.Equal(1, account.RegisterCalls);

        account.RegistrationCompletion.TrySetResult(
            new AccountRegistration(account.PlayerId, ConfirmationRequired: true));
        Assert.NotNull(await first);
        Assert.NotNull(gateway.PendingRegistration);
    }

    [Fact]
    public async Task DuplicateConfirmationDoesNotSendTheCodeTwice()
    {
        var account = new GatewayAccountFake
        {
            ConfirmationCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);
        await gateway.RegisterAsync("pilot@example.test", "secret", "Pilot");

        Task<bool> first = gateway.ConfirmPendingAsync("123456");
        await account.ConfirmationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(await gateway.ConfirmPendingAsync("123456"));
        Assert.Equal(1, account.ConfirmationCalls);

        account.ConfirmationCompletion.TrySetResult();
        Assert.True(await first);
        Assert.Null(gateway.PendingRegistration);
    }

    [Fact]
    public async Task AccountFailureUsesPlayerSafeCopyWhileDiagnosticsStayOutOfState()
    {
        var account = new GatewayAccountFake
        {
            RegistrationError = new InvalidOperationException(
                "HTTP 500 https://internal.example.test/token=secret")
        };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);

        Assert.Null(await gateway.RegisterAsync(
            "pilot@example.test", "secret", "Pilot"));

        Assert.Equal(GatewayPhase.Failed, gateway.State.Phase);
        Assert.Equal("Could not complete that request. Check your details and try again.",
            gateway.State.Message);
        Assert.DoesNotContain("internal.example.test", gateway.State.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret", gateway.State.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackendDisposalFailureAbortsReplacementWithSignedOutState()
    {
        string previousBackend = LauncherPrefs.BackendAddress;
        var account = new GatewayAccountFake { ThrowOnDispose = true };
        using var shell = new PrimeShellState();
        await using var gateway = Create(shell, account);
        try
        {
            Assert.True(await gateway.SignInAsync("pilot@example.test", "secret"));
            int identityEvents = 0;
            gateway.IdentityChanged += (_, _) => identityEvents++;

            Assert.False(await gateway.ConfigureBackendAsync(
                new Uri("https://replacement-backend.test/")));

            Assert.Equal(GatewayPhase.Failed, gateway.State.Phase);
            Assert.False(gateway.State.SignedIn);
            Assert.Null(gateway.State.PlayerId);
            Assert.False(shell.HasNetworkIdentity);
            Assert.Equal(previousBackend, LauncherPrefs.BackendAddress);
            Assert.Equal(1, identityEvents);
        }
        finally
        {
            LauncherPrefs.BackendAddress = previousBackend;
            LauncherPrefs.Save();
        }
    }

    private static GatewayController Create(PrimeShellState shell,
        GatewayAccountFake account)
        => new(shell, _ => Task.FromResult<IPrimeGatewayAccount>(account));

    private sealed class GatewayAccountFake : IPrimeGatewayAccount
    {
        public PlayerId PlayerId { get; } = new(Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        public Uri Backend => new("https://backend.test/");
        public bool IsSignedIn { get; private set; }
        public AccountIdentity? Identity { get; private set; }
        public TaskCompletionSource<AccountRegistration>? RegistrationCompletion { get; init; }
        public TaskCompletionSource? ConfirmationCompletion { get; init; }
        public TaskCompletionSource RegisterStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConfirmationStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? RegistrationError { get; init; }
        public bool ThrowOnDispose { get; init; }
        public int RegisterCalls { get; private set; }
        public int ConfirmationCalls { get; private set; }

        public Task<bool> RestoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

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
        {
            RegisterCalls++;
            RegisterStarted.TrySetResult();
            if (RegistrationError != null) throw RegistrationError;
            return RegistrationCompletion?.Task
                ?? Task.FromResult(new AccountRegistration(PlayerId, true));
        }

        public async Task ConfirmEmailAsync(PlayerId playerId, string code,
            CancellationToken cancellationToken)
        {
            ConfirmationCalls++;
            ConfirmationStarted.TrySetResult();
            if (ConfirmationCompletion != null)
                await ConfirmationCompletion.Task.ConfigureAwait(false);
        }

        public Task ResendConfirmationAsync(string email,
            CancellationToken cancellationToken) => Task.CompletedTask;

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

        public ValueTask DisposeAsync()
        {
            if (ThrowOnDispose)
                throw new InvalidOperationException(
                    "The old account adapter could not release private transport state.");
            return ValueTask.CompletedTask;
        }
    }
}
