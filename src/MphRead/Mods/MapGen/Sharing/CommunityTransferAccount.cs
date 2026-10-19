using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>Noncredential account witness. Only the private transfer index persists its subject.</summary>
public sealed class CommunityTransferAccount
{
    [JsonIgnore] internal int Version => 1;
    [JsonIgnore] internal Guid Subject { get; }
    public CommunityTransferAccount(Guid subject)
    {
        if (subject == Guid.Empty) throw new ArgumentException("A verified Community account is required.", nameof(subject));
        Subject = subject;
    }
    public bool Matches(Guid subject) => Subject == subject;
    internal bool Matches(CommunityTransferAccount? other) => other != null && Subject == other.Subject;
}

/// <summary>Each call verifies the current account independently; no ticket may cross subjects.</summary>
public interface ICommunityAccountCredentialSource : ICommunityCredentialSource
{
    Task<CommunityTransferAccount> VerifyAccountAsync(CommunityTransferAccount? expected, CancellationToken cancellation);
    Task<string> GetTicketForAccountAsync(CommunityTransferAccount expected, bool refresh, CancellationToken cancellation);
    /// <summary>Verify and run a short receipt admission while the auth owner prevents account replacement.</summary>
    Task CommitForAccountAsync(CommunityTransferAccount expected, Action commit, CancellationToken cancellation);
}

public sealed class CommunityTransferAuthorizationException : InvalidOperationException
{
    public CommunityTransferAuthorizationException() : base("Community upload account authorization must be verified; immutable bytes retained and publication outcome may be unverified.") { }
}

public sealed class DelegateCommunityAccountCredentialSource(
    Func<CommunityTransferAccount?, CancellationToken, Task<CommunityTransferAccount>> verify,
    Func<CommunityTransferAccount, bool, CancellationToken, Task<string>> ticket,
    Func<CommunityTransferAccount, Action, CancellationToken, Task> commit) : ICommunityAccountCredentialSource
{
    public Task<CommunityTransferAccount> VerifyAccountAsync(CommunityTransferAccount? expected, CancellationToken cancellation)
        => verify(expected, cancellation);
    public Task<string> GetTicketForAccountAsync(CommunityTransferAccount expected, bool refresh, CancellationToken cancellation)
        => ticket(expected, refresh, cancellation);
    public Task CommitForAccountAsync(CommunityTransferAccount expected, Action action, CancellationToken cancellation)
        => commit(expected, action, cancellation);
    public Task<string> GetTicketAsync(CancellationToken cancellation) => Acquire(false, cancellation);
    public Task<string> RefreshTicketAsync(CancellationToken cancellation) => Acquire(true, cancellation);
    private async Task<string> Acquire(bool refresh, CancellationToken cancellation)
        => await ticket(await verify(null, cancellation).ConfigureAwait(false), refresh, cancellation).ConfigureAwait(false);
}
