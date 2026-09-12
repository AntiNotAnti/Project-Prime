using System;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Resources;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.Presentation;

internal enum PrimeUserMessageSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Safe player-facing translation of a low-level account, network, or update
/// diagnostic. The caller retains the original diagnostic at its logging
/// boundary; presentation code receives stable copy plus an optional
/// diagnostic code rather than exception types, response text, or transport
/// data.
/// </summary>
internal sealed record PrimeUserMessage(
    string Text,
    PrimeUserMessageSeverity Severity,
    string? DiagnosticCode = null)
{
    public string Message => Text;
    public bool IsError => Severity == PrimeUserMessageSeverity.Error;

    public static PrimeUserMessage ForUpdate(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            UpdateCheckResult.UpToDate => Create(PrimeUiCopy.Update_UpToDate,
                PrimeUserMessageSeverity.Success, "UpToDate"),
            UpdateCheckResult.Available => Create(PrimeUiCopy.Update_Available,
                PrimeUserMessageSeverity.Info, "Available"),
            UpdateCheckResult.NotApplicable notApplicable => Translate(
                notApplicable.Reason, PrimeUserMessageSeverity.Info),
            UpdateCheckResult.Failed failed => Translate(failed.Message,
                PrimeUserMessageSeverity.Error),
            _ => Create(PrimeUiCopy.Error_Generic,
                PrimeUserMessageSeverity.Error, "UnknownUpdateResult")
        };
    }

    /// <summary>Compatibility alias for callers that prefer a From-style name.</summary>
    public static PrimeUserMessage FromUpdate(UpdateCheckResult result)
        => ForUpdate(result);

    public static PrimeUserMessage ForUpdateStatus(UpdateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.State switch
        {
            UpdateState.NotApplicable => Translate(status.Message,
                PrimeUserMessageSeverity.Info),
            UpdateState.UpToDate => Create(PrimeUiCopy.Update_UpToDate,
                PrimeUserMessageSeverity.Success, "UpToDate"),
            UpdateState.Available => Create(PrimeUiCopy.Update_Available,
                PrimeUserMessageSeverity.Info, "Available"),
            UpdateState.Failed => Translate(status.Message,
                PrimeUserMessageSeverity.Error),
            UpdateState.Checking => Create(PrimeUiCopy.Update_Checking,
                PrimeUserMessageSeverity.Info, "Checking"),
            _ => Translate(status.Message, PrimeUserMessageSeverity.Info)
        };
    }

    public static PrimeUserMessage ForAccount(string? diagnostic,
        PrimeUserMessageSeverity fallbackSeverity = PrimeUserMessageSeverity.Error)
    {
        string value = diagnostic?.Trim() ?? "";
        if (Contains(value, "unable to connect")
            || Contains(value, "connection refused")
            || Contains(value, "name or service not known")
            || Contains(value, "account network")
            || Contains(value, "account request timed out"))
        {
            return Create(PrimeUiCopy.Account_NetworkUnavailable,
                PrimeUserMessageSeverity.Warning, "AccountNetworkUnavailable");
        }
        if (Contains(value, "rate limit") || Contains(value, "too many")
            || Is(value, "RateLimited") || Is(value, "rate_limited"))
        {
            return Create(PrimeUiCopy.Account_RateLimited,
                PrimeUserMessageSeverity.Warning, "AccountRateLimited");
        }
        return Translate(diagnostic, fallbackSeverity);
    }

    public static PrimeUserMessage ForAccountError(AccountServiceException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind switch
        {
            AccountFailureKind.InvalidCredential => Translate(
                error.ErrorCode ?? "InvalidCredential", PrimeUserMessageSeverity.Error),
            AccountFailureKind.RateLimited => Create(PrimeUiCopy.Account_RateLimited,
                PrimeUserMessageSeverity.Warning, "AccountRateLimited"),
            AccountFailureKind.ServiceUnavailable => Create(
                PrimeUiCopy.Account_ServiceUnavailable,
                PrimeUserMessageSeverity.Warning, "AccountServiceUnavailable"),
            AccountFailureKind.TransportUnavailable or AccountFailureKind.Timeout
                => Create(PrimeUiCopy.Account_NetworkUnavailable,
                    PrimeUserMessageSeverity.Warning, "AccountNetworkUnavailable"),
            _ => Translate(error.ErrorCode ?? error.Message,
                error.Kind == AccountFailureKind.Cancelled
                    ? PrimeUserMessageSeverity.Info
                    : PrimeUserMessageSeverity.Warning)
        };
    }

    public static PrimeUserMessage ForNetwork(string? diagnostic,
        PrimeUserMessageSeverity fallbackSeverity = PrimeUserMessageSeverity.Warning)
    {
        string value = diagnostic?.Trim() ?? "";
        if (Contains(value, "network") || Contains(value, "connection")
            || Contains(value, "timed out") || Contains(value, "timeout")
            || Contains(value, "unable to connect"))
        {
            return Create(PrimeUiCopy.Network_Unavailable,
                PrimeUserMessageSeverity.Warning, "NetworkUnavailable");
        }
        return Translate(diagnostic, fallbackSeverity);
    }

    public static PrimeUserMessage TranslateException(Exception? error,
        PrimeUserMessageSeverity fallbackSeverity = PrimeUserMessageSeverity.Error)
    {
        if (error is AccountServiceException accountError)
            return ForAccountError(accountError);
        return Translate(error?.Message, fallbackSeverity);
    }

    /// <summary>
    /// Maps known diagnostics to the smallest useful player instruction.
    /// Unknown text is intentionally not returned to the UI.
    /// </summary>
    public static PrimeUserMessage Translate(string? diagnostic,
        PrimeUserMessageSeverity fallbackSeverity = PrimeUserMessageSeverity.Warning)
    {
        string value = diagnostic?.Trim() ?? "";
        if (Is(value, "LocalBuild") || Contains(value, "local build"))
            return Create(PrimeUiCopy.Update_LocalBuild,
                PrimeUserMessageSeverity.Info, "LocalBuild");

        if (Contains(value, "update feed is not configured")
            || Contains(value, "updates are unavailable in this build"))
            return Create(PrimeUiCopy.Update_FeedUnavailable,
                PrimeUserMessageSeverity.Info, "FeedUnavailable");
        if (Contains(value, "updates are disabled")
            || Contains(value, "updates disabled")
            || Contains(value, "automatic updates are off")
            || Is(value, "UpdatesDisabled"))
            return Create(PrimeUiCopy.Update_Disabled,
                PrimeUserMessageSeverity.Info, "UpdatesDisabled");
        if (Contains(value, "installed version is unknown")
            || Is(value, "VersionUnknown"))
            return Create(PrimeUiCopy.Update_VersionUnknown,
                PrimeUserMessageSeverity.Warning, "VersionUnknown");
        if (Contains(value, "cannot install updates automatically")
            || Contains(value, "automatic installation is unavailable"))
            return Create(PrimeUiCopy.Update_InstallUnavailable,
                PrimeUserMessageSeverity.Warning, "InstallUnavailable");
        if (Contains(value, "verification failed")
            || Contains(value, "could not be verified")
            || Contains(value, "signature"))
            return Create(PrimeUiCopy.Update_VerificationFailed,
                PrimeUserMessageSeverity.Error, "UpdateVerificationFailed");
        bool accountDiagnostic = Contains(value, "account")
            || Contains(value, "credential")
            || Contains(value, "sign-in");
        if (!accountDiagnostic && (Contains(value, "update check timed out")
            || Contains(value, "update host returned")
            || Contains(value, "could not check for updates")
            || Contains(value, "update feed address")
            || Contains(value, "network")
            || Contains(value, "connection")))
            return Create(PrimeUiCopy.Update_NetworkUnavailable,
                PrimeUserMessageSeverity.Warning, "UpdateNetworkUnavailable");

        if (Contains(value, "invalid credential")
            || Contains(value, "invalid credentials")
            || Contains(value, "sign-in failed")
            || Is(value, "InvalidCredential")
            || Is(value, "invalid_credentials"))
            return Create(PrimeUiCopy.Account_SignInFailed,
                PrimeUserMessageSeverity.Error, "InvalidCredential");
        if (Contains(value, "confirm the account")
            || Contains(value, "account is not confirmed")
            || Is(value, "AccountUnconfirmed")
            || Is(value, "account_unconfirmed")
            || Is(value, "email_confirmation_required"))
            return Create(PrimeUiCopy.Account_ConfirmationRequired,
                PrimeUserMessageSeverity.Warning, "AccountUnconfirmed");
        if (Contains(value, "already exists")
            || Contains(value, "duplicate account")
            || Is(value, "DuplicateAccount")
            || Is(value, "duplicate_account")
            || Is(value, "account_exists"))
            return Create(PrimeUiCopy.Account_AlreadyExists,
                PrimeUserMessageSeverity.Warning, "DuplicateAccount");
        if (Contains(value, "account service") &&
            (Contains(value, "unavailable") || Contains(value, "temporarily")))
            return Create(PrimeUiCopy.Account_ServiceUnavailable,
                PrimeUserMessageSeverity.Warning, "AccountServiceUnavailable");
        if (Contains(value, "unable to connect")
            || Contains(value, "connection refused")
            || Contains(value, "name or service not known"))
            return Create(PrimeUiCopy.Account_NetworkUnavailable,
                PrimeUserMessageSeverity.Warning, "AccountNetworkUnavailable");

        return Create(PrimeUiCopy.Error_Generic, fallbackSeverity, "Unknown");
    }

    private static PrimeUserMessage Create(string text,
        PrimeUserMessageSeverity severity, string code)
        => new(text, severity, code);

    private static bool Is(string value, string code)
        => String.Equals(value, code, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string fragment)
        => value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
