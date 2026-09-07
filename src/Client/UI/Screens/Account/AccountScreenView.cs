using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.Account;

public sealed class AccountScreenView : ScreenViewBase
{
    private readonly IAccountScreenController _controller;
    private readonly StackPanel _identity = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _form = new() { Spacing = UiSpacing.Space3 };
    private readonly TextBox _email;
    private readonly TextBox _password;
    private readonly PrimaryButton _signIn;
    private readonly SecondaryButton _restore;
    private readonly SecondaryButton _signOut;
    private readonly ErrorBanner _feedback = new() { IsVisible = false };
    private bool _pending;

    public AccountScreenView(IAccountScreenController controller)
        : base("Account", "Sign in or restore a saved session. Account eligibility is always labeled.",
            "account:email")
    {
        _controller = controller;
        _email = Register("account:email", new TextBox
        {
            Watermark = "Email", MaxLength = 254, MinWidth = 260
        });
        AutomationProperties.SetName(_email, "Email address");
        _password = Register("account:password", new TextBox
        {
            Watermark = "Password", PasswordChar = '•', MaxLength = 256, MinWidth = 260
        });
        AutomationProperties.SetName(_password, "Password");
        _signIn = Register("account:signin", new PrimaryButton
        {
            Content = "Sign In", AccessibleName = "Sign in"
        });
        _restore = Register("account:restore", new SecondaryButton
        {
            Content = "Restore Session", AccessibleName = "Restore saved account session"
        });
        _signOut = Register("account:signout", new SecondaryButton
        {
            Content = "Sign Out", AccessibleName = "Sign out"
        });
        _signIn.Click += async (_, _) => await SignInAsync();
        _restore.Click += async (_, _) => await RunAsync(_controller.RestoreAsync);
        _signOut.Click += async (_, _) => await RunAsync(_controller.SignOutAsync);

        var buttons = new WrapPanel { Children = { _signIn, _restore } };
        _signIn.Margin = _restore.Margin = _signOut.Margin
            = new Thickness(0, 0, UiSpacing.Space2, UiSpacing.Space2);
        _form.Children.Add(_email);
        _form.Children.Add(_password);
        _form.Children.Add(buttons);
        Body.Content = new StackPanel
        {
            Spacing = UiSpacing.Space4,
            Children = { _identity, _form, _signOut, _feedback }
        };
        Rebuild();
    }

    private async System.Threading.Tasks.Task SignInAsync()
    {
        string email = _email.Text?.Trim() ?? string.Empty;
        string password = _password.Text ?? string.Empty;
        if (email.Length == 0 || password.Length == 0)
        {
            ShowFailure("Enter both email and password.");
            return;
        }
        await RunAsync(cancellationToken => _controller.SignInAsync(email, password,
            cancellationToken));
        _password.Text = string.Empty;
    }

    private async System.Threading.Tasks.Task RunAsync(
        Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<UiActionResult>> operation)
    {
        if (_pending) return;
        _pending = true;
        SetEnabled(false);
        Rebuild();
        UiActionResult result;
        try
        {
            System.Threading.Tasks.Task<UiActionResult> request = operation(ScreenCancellation);
            Rebuild();
            result = await request;
        }
        catch (OperationCanceledException)
        {
            result = UiActionResult.Failure("Account request canceled.");
        }
        catch (Exception error)
        {
            result = UiActionResult.Failure(AsyncScreenState.FriendlyFailure(error,
                "The account request"));
        }
        _pending = false;
        SetEnabled(true);
        Rebuild();
        if (!result.Succeeded) ShowFailure(result.Message);
        else if (result.Message.Length > 0)
        {
            _feedback.Message = result.Message;
            _feedback.IsVisible = true;
        }
    }

    private void Rebuild()
    {
        UiAccountSnapshot account = _controller.Snapshot;
        _identity.Children.Clear();
        _identity.Children.Add(Text("SESSION", size: UiTypography.TextHeading));
        _identity.Children.Add(Text(AccountStatus(account)));
        _form.IsVisible = account.Phase != UiAccountPhase.SignedIn;
        _signOut.IsVisible = account.Phase == UiAccountPhase.SignedIn;
        if (account.Message.Length > 0)
        {
            _feedback.Message = account.Message;
            _feedback.IsVisible = true;
        }
    }

    private void SetEnabled(bool enabled)
    {
        _email.IsEnabled = enabled;
        _password.IsEnabled = enabled;
        _signIn.IsEnabled = enabled;
        _restore.IsEnabled = enabled;
        _signOut.IsEnabled = enabled;
    }

    private void ShowFailure(string message)
    {
        _feedback.Message = message;
        _feedback.IsVisible = true;
    }

    private static string AccountStatus(UiAccountSnapshot account)
    {
        string phase = account.Phase switch
        {
            UiAccountPhase.Guest => "Guest",
            UiAccountPhase.Restoring => "Restoring saved session…",
            UiAccountPhase.SigningIn => "Signing in…",
            UiAccountPhase.SignedIn => "Signed in",
            UiAccountPhase.Offline => "Offline",
            _ => "Account error"
        };
        if (account.Phase != UiAccountPhase.SignedIn) return phase;
        string email = account.EmailConfirmed ? "Email confirmed" : "Email not confirmed";
        string eligible = account.OfficialPlayEligible
            ? "Official play eligible" : "Not eligible for official play";
        return $"{phase} as {account.DisplayName}\nPlayer {account.PlayerId}\n{email} · {eligible}";
    }
}
