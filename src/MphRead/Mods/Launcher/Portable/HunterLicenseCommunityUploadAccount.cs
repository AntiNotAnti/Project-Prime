#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

internal static partial class HunterLicenseClient
{
    internal static ICommunityAccountCredentialSource CommunityUploadCredentials { get; } =
        new DelegateCommunityAccountCredentialSource(
            VerifyCommunityUploadAccountAsync, MintCommunityUploadTicketAsync, CommitCommunityUploadAccountAsync);

    private static Task<CommunityTransferAccount> VerifyCommunityUploadAccountAsync(CommunityTransferAccount? expected, CancellationToken token)
        => WithCommunityUploadAccountAsync(expected, token, (_, subject) => Task.FromResult(new CommunityTransferAccount(subject)));

    private static Task<string> MintCommunityUploadTicketAsync(CommunityTransferAccount expected, bool refresh, CancellationToken token)
        => WithCommunityUploadAccountAsync(expected, token, async (session, _) =>
        {
            // Verify subject and mint under the same auth gate. Never reuse the global cached
            // Community ticket, whose account can differ from this durable job's witness.
            var credential = await FunctionAsync<CommunityMapTicketResponse>(session.AccessToken, "community-map-ticket",
                new Dictionary<string, object?> { ["action"] = "mint" }, token).ConfigureAwait(false);
            if (!credential.Ticket.StartsWith("ppm1.", StringComparison.Ordinal)
                || credential.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
                throw new CommunityTransferAuthorizationException();
            return credential.Ticket;
        });

    private static async Task CommitCommunityUploadAccountAsync(CommunityTransferAccount expected, Action commit, CancellationToken token)
        => await WithCommunityUploadAccountAsync(expected, token, (_, _) =>
        { token.ThrowIfCancellationRequested(); commit(); return Task.FromResult(true); }).ConfigureAwait(false);

    private static async Task<T> WithCommunityUploadAccountAsync<T>(CommunityTransferAccount? expected, CancellationToken token,
        Func<AuthSession, Guid, Task<T>> action)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            AuthSession session = await AuthenticateAsync(token).ConfigureAwait(false);
            AuthUser user = await GetUserAsync(session.AccessToken, token).ConfigureAwait(false);
            if (!Guid.TryParse(user.Id, out Guid subject) || subject == Guid.Empty || expected != null && !expected.Matches(subject))
                throw new CommunityTransferAuthorizationException();
            token.ThrowIfCancellationRequested();
            return await action(session, subject).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { throw new CommunityTransferAuthorizationException(); }
        finally { Gate.Release(); }
    }
}
#endif
