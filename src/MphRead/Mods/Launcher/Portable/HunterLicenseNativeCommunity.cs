#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

internal static partial class HunterLicenseClient
{
    /// <summary>Pin the existing auth session for one Community transaction; no auth token enters UI state.</summary>
    internal static async Task<T> WithNativeCommunityIdentityAsync<T>(object? expectedLease, Guid? expectedUser,
        Func<Guid, object, ICommunityCredentialSource, CancellationToken, Task<T>> action, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            AuthSession session = await AuthenticateAsync(token).ConfigureAwait(false);
            if (expectedLease != null && !ReferenceEquals(expectedLease, session))
                throw new InvalidOperationException("Community account changed.");
            AuthUser user = await GetUserAsync(session.AccessToken, token).ConfigureAwait(false);
            if (!Guid.TryParse(user.Id, out Guid account) || account == Guid.Empty
                || expectedUser is { } expected && account != expected)
                throw new InvalidOperationException("Community account could not be verified.");
            token.ThrowIfCancellationRequested();
            var credential = await FunctionAsync<CommunityMapTicketResponse>(session.AccessToken, "community-map-ticket",
                new Dictionary<string, object?> { ["action"] = "mint" }, token).ConfigureAwait(false);
            if (!credential.Ticket.StartsWith("ppm1.", StringComparison.Ordinal)
                || credential.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
                throw new InvalidOperationException("Community credential could not be verified.");
            Task<string> Pinned(CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!IsNativeAccountLeaseCurrent(session)) throw new InvalidOperationException("Community account changed.");
                return Task.FromResult(credential.Ticket);
            }
            // The existing facade may retry one 401, but cannot acquire a credential for
            // another session while this transaction owns Gate. A rejected pinned ticket fails closed.
            var credentials = new DelegateCommunityCredentialSource(Pinned, Pinned);
            token.ThrowIfCancellationRequested();
            T value = await action(account, session, credentials, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!IsNativeAccountLeaseCurrent(session)) throw new InvalidOperationException("Community account changed.");
            return value;
        }
        finally { Gate.Release(); }
    }
}
#endif
