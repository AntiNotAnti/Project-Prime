using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Identity;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class GatewayPresentationTests
{
    [Theory]
    [InlineData("pilot@example.com", "p•••••@example.com")]
    [InlineData("  J@example.com  ", "J•••••@example.com")]
    [InlineData("invalid", "your email address")]
    public void EmailMaskNeverExposesTheFullAddress(string email, string expected)
        => Assert.Equal(expected, PrimeShellView.MaskEmail(email));

    [AvaloniaTheory]
    [InlineData(GatewayPhase.Gateway, 0, "ENTER THE ARENA")]
    [InlineData(GatewayPhase.SigningIn, 2, "Sign in")]
    [InlineData(GatewayPhase.Registering, 3, "Create account")]
    [InlineData(GatewayPhase.Confirming, 1, "CHECK YOUR EMAIL")]
    public void GatewayRendersExactlyOneExplicitForm(GatewayPhase phase,
        int expectedInputs, string expectedHeading)
    {
        PlayerId player = new(Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        PendingRegistration? pending = phase == GatewayPhase.Confirming
            ? new PendingRegistration(player, "pilot@example.com") : null;
        var capture = new PrimeShellCaptureState(
            Gateway: new GatewayState(phase, "Ready", false, false, false,
                false, phase == GatewayPhase.Confirming ? player : null, "Guest"),
            PendingRegistration: pending);
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Gateway, capture);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            IReadOnlyList<string> copy = VisibleCopy(shell);
            Assert.Contains(expectedHeading, copy);
            Assert.Equal(expectedInputs, shell.GetVisualDescendants()
                .OfType<TextBox>().Count());
            Assert.DoesNotContain(copy, value => value.Contains("Player ID",
                StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(copy, value => value.Contains(player.ToString(),
                StringComparison.OrdinalIgnoreCase));

            if (phase == GatewayPhase.Confirming)
            {
                Assert.Equal(pending, shell.Gateway.PendingRegistration);
                Assert.Contains("p•••••@example.com", copy);
                Assert.DoesNotContain("pilot@example.com", copy);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void ConfirmationActionsRetainControllerContextAcrossFormReplacement()
    {
        var account = new AccountFake();
        PrimeShellView shell = new(new MenuSettings(), Array.Empty<string>(),
            restoreOnActivate: false, ignoreGameFileGate: true,
            captureMode: true, showTitleScreen: false,
            gatewayFactory: state => new GatewayController(state,
                _ => Task.FromResult<IPrimeGatewayAccount>(account)));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Click(shell, "Create Account");
            FindInput(shell, "Email address").Text = " Pilot@Example.TEST ";
            FindInput(shell, "Password").Text = "secret";
            FindInput(shell, "Display name").Text = "Pilot";
            Click(shell, "Register");
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.Gateway.PendingRegistration is not null
                    && VisibleCopy(shell).Contains("CHECK YOUR EMAIL");
            }, TimeSpan.FromSeconds(2)));

            Assert.Equal("pilot@example.test",
                shell.Gateway.PendingRegistration?.Email);
            Assert.Contains("p•••••@example.test", VisibleCopy(shell));
            Click(shell, "Resend Code");
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return account.ResendCalls == 1;
            }, TimeSpan.FromSeconds(2)));

            FindInput(shell, "Confirmation code").Text = "123456";
            Click(shell, "Confirm");
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.Gateway.PendingRegistration is null
                    && VisibleCopy(shell).Contains("Sign in");
            }, TimeSpan.FromSeconds(2)));
            Assert.Equal(account.PlayerId, account.ConfirmedPlayerId);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void GatewayConfirmCaptureCarriesTruthfulPendingRegistration()
    {
        PrimeShellView shell = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "gateway-confirm", new MenuSettings(), Array.Empty<string>()));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            PendingRegistration pending = Assert.IsType<PendingRegistration>(
                shell.Gateway.PendingRegistration);
            Assert.Equal("pilot@example.com", pending.Email);
            Assert.Contains("p•••••@example.com", VisibleCopy(shell));
            Assert.Contains("Confirm", VisibleCopy(shell));
            Assert.Contains("Resend Code", VisibleCopy(shell));
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void FailedRegistrationStaysOnRegisterAndShowsSanitizedSummary()
    {
        var account = new AccountFake { FailRegistration = true };
        PrimeShellView shell = CreateShell(account);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Click(shell, "Create Account");
            FindInput(shell, "Email address").Text = "pilot@example.test";
            FindInput(shell, "Password").Text = "secret";
            FindInput(shell, "Display name").Text = "Pilot";
            Click(shell, "Register");

            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.Gateway.State.Phase == GatewayPhase.Failed
                    && VisibleCopy(shell).Contains("Create account");
            }, TimeSpan.FromSeconds(2)));
            Assert.Contains("Could not complete that request. Check your details and try again.",
                VisibleCopy(shell));
            Assert.Equal(3, shell.GetVisualDescendants().OfType<TextBox>().Count());
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void RegistrationCompletionDoesNotReplaceAFormThePlayerLeft()
    {
        var registration = new TaskCompletionSource<AccountRegistration>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var account = new AccountFake { RegistrationCompletion = registration };
        PrimeShellView shell = CreateShell(account);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Click(shell, "Create Account");
            FindInput(shell, "Email address").Text = "pilot@example.test";
            FindInput(shell, "Password").Text = "secret";
            FindInput(shell, "Display name").Text = "Pilot";
            Click(shell, "Register");
            account.RegisterStarted.Task.Wait(TimeSpan.FromSeconds(2));
            Click(shell, "Back");
            Assert.Contains("ENTER THE ARENA", VisibleCopy(shell));

            registration.TrySetResult(new AccountRegistration(account.PlayerId, true));
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return shell.Gateway.PendingRegistration is not null;
            }, TimeSpan.FromSeconds(2)));
            Assert.Contains("ENTER THE ARENA", VisibleCopy(shell));
            Assert.DoesNotContain("CHECK YOUR EMAIL", VisibleCopy(shell));
        }
        finally
        {
            registration.TrySetCanceled();
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static IReadOnlyList<string> VisibleCopy(Control root)
        => root.GetVisualDescendants().Select(control => control switch
        {
            TextBlock text => text.Text,
            Avalonia.Controls.Button { Content: string content } => content,
            TextBox input => input.Watermark?.ToString(),
            _ => null
        }).Where(value => !String.IsNullOrWhiteSpace(value))
            .Select(value => value!).ToArray();

    private static TextBox FindInput(Control root, string watermark)
        => root.GetVisualDescendants().OfType<TextBox>().Single(input
            => String.Equals(input.Watermark?.ToString(), watermark,
                StringComparison.Ordinal));

    private static void Click(Control root, string content)
        => root.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
            .Single(button => String.Equals(button.Content?.ToString(), content,
                StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private static PrimeShellView CreateShell(AccountFake account)
        => new(new MenuSettings(), Array.Empty<string>(), restoreOnActivate: false,
            ignoreGameFileGate: true, captureMode: true, showTitleScreen: false,
            gatewayFactory: state => new GatewayController(state,
                _ => Task.FromResult<IPrimeGatewayAccount>(account)));

    private sealed class AccountFake : IPrimeGatewayAccount
    {
        public PlayerId PlayerId { get; } = new(Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        public Uri Backend => new("https://backend.test/");
        public bool IsSignedIn => false;
        public AccountIdentity? Identity => null;
        public int ResendCalls { get; private set; }
        public PlayerId? ConfirmedPlayerId { get; private set; }
        public bool FailRegistration { get; init; }
        public TaskCompletionSource<AccountRegistration>? RegistrationCompletion
        {
            get;
            init;
        }
        public TaskCompletionSource RegisterStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AccountRegistration> RegisterAsync(string email,
            string password, string displayName,
            CancellationToken cancellationToken)
        {
            RegisterStarted.TrySetResult();
            if (FailRegistration)
                throw new InvalidOperationException(
                    "HTTP exception contained sensitive server internals.");
            return RegistrationCompletion?.Task
                ?? Task.FromResult(new AccountRegistration(PlayerId, true));
        }
        public Task ConfirmEmailAsync(PlayerId playerId, string code,
            CancellationToken cancellationToken)
        {
            ConfirmedPlayerId = playerId;
            return Task.CompletedTask;
        }
        public Task ResendConfirmationAsync(string email,
            CancellationToken cancellationToken)
        {
            ResendCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> RestoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task SignInAsync(string email, string password,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<HunterLicense> GetLicenseAsync(PlayerId playerId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProfileAsync(string displayName, int favoriteHunter,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
