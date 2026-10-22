#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Update;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

internal static partial class HunterLicenseClient
{
    // The existing auth gate owns the entire transaction. No account ID or
    // response body crosses the native UI boundary, including on failure.
    internal static async Task<bool> ExecuteNativeAccountAsync(Guid expectedUser,
        NativeLicenseAccountAction action, string email, string password, CancellationToken token)
    {
        if (expectedUser == Guid.Empty || !Enum.IsDefined(action)) return false;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            AuthSession session = await AuthenticateAsync(token).ConfigureAwait(false);
            AuthUser user = await GetUserAsync(session.AccessToken, token).ConfigureAwait(false);
            if (!Guid.TryParse(user.Id, out Guid actual) || actual != expectedUser) return false;
            token.ThrowIfCancellationRequested();
            switch (action)
            {
                case NativeLicenseAccountAction.SendVerification:
                    email = email.Trim();
                    if (email.Length > 254 || !LooksLikeEmail(email)) return false;
                    await UpdateUserAsync(session.AccessToken,
                        new Dictionary<string, object?> { ["email"] = email }, token,
                        emailRedirectTo: WebLicenseUrl).ConfigureAwait(false);
                    break;
                case NativeLicenseAccountAction.SetPassword:
                    if (password.Length is < 8 or > 128 || user.IsAnonymous
                        || String.IsNullOrWhiteSpace(user.Email)) return false;
                    await UpdateUserAsync(session.AccessToken,
                        new Dictionary<string, object?> { ["password"] = password }, token).ConfigureAwait(false);
                    break;
                case NativeLicenseAccountAction.Recover:
                    if (NetSession.Active || !user.IsAnonymous || password.Length is < 1 or > 128
                        || email.Length > 254 || !LooksLikeEmail(email)) return false;
                    // Recheck server career while the same guest still owns
                    // the auth gate. A stale empty UI snapshot is insufficient.
                    HunterLicenseSnapshot guest = await FunctionAsync<HunterLicenseSnapshot>(
                        session.AccessToken, "hunter-license", new Dictionary<string, object?>
                        {
                            ["display_name"] = LauncherPrefs.PlayerName.Trim(),
                            ["favorite_hunter"] = Math.Clamp((int)Hunters.Resolve(LauncherPrefs.LastHunter), 0, 6)
                        }, token).ConfigureAwait(false);
                    if (!Guid.TryParse(guest.Profile.PlayerId, out Guid guestId)
                        || guestId != expectedUser || guest.Stats.GamesPlayed != 0) return false;
                    AuthSession recovered = await AuthRequestAsync("/auth/v1/token?grant_type=password",
                        new Dictionary<string, object?> { ["email"] = email.Trim(), ["password"] = password }, token)
                        .ConfigureAwait(false);
                    AuthUser recoveredUser = await GetUserAsync(recovered.AccessToken, token).ConfigureAwait(false);
                    if (recoveredUser.IsAnonymous || !Guid.TryParse(recoveredUser.Id, out Guid recoveredId)
                        || recoveredId == Guid.Empty) return false;
                    token.ThrowIfCancellationRequested();
                    if (NetSession.Active || !TrySaveStoredSession(recovered.RefreshToken)) return false;
                    _currentSession = recovered;
                    InvalidateCareerTicket();
                    InvalidateCommunityMapTicket();
                    break;
                default:
                    string provider = action switch
                    {
                        NativeLicenseAccountAction.LinkGoogle => "google",
                        NativeLicenseAccountAction.LinkGithub => "github",
                        NativeLicenseAccountAction.LinkDiscord => "discord",
                        _ => ""
                    };
                    if (!OAuthProviders.Contains(provider)) return false;
                    if (user.Identities.Any(i => provider.Equals(i.Provider, StringComparison.OrdinalIgnoreCase))) return true;
                    OAuthAuthorizeResponse link = await GetAsync<OAuthAuthorizeResponse>(
                        "/auth/v1/user/identities/authorize?provider=" + Uri.EscapeDataString(provider)
                        + "&redirect_to=" + Uri.EscapeDataString(WebLicenseUrl) + "&skip_http_redirect=true",
                        session.AccessToken, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    // Never pass an arbitrary protocol to the OS link handler.
                    if (!Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? uri)
                        || uri.Scheme != Uri.UriSchemeHttps || !Updater.OpenLink(link.Url)) return false;
                    break;
            }
            token.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException
            and not OutOfMemoryException and not StackOverflowException) { return false; }
        finally { Gate.Release(); }
    }
}
#endif
