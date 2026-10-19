#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

internal sealed class NativeCommunityRevisionsService : INativeCommunityRevisionsAuthority
{
    public bool IsCurrentAccountLease(object? lease) => HunterLicenseClient.IsNativeAccountLeaseCurrent(lease);
    public Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request, CancellationToken token)
    {
        if (!request.Owned) return ReadCore(new(NetworkMapIdentity.ConfiguredDownloadSource()), request, Guid.Empty, null, token);
        if (request.AccountLease == null) throw new InvalidOperationException("Community account must be verified.");
        return HunterLicenseClient.WithNativeCommunityIdentityAsync(request.AccountLease, null,
            (account, lease, credentials, ct) => ReadCore(new(NetworkMapIdentity.ConfiguredDownloadSource(), credentials),
                request, account, lease, ct, requireLatestAnchor: !request.ReconcileAfterWrite), token);
    }

    public Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion expected, CancellationToken token)
        => ChangeAsync(expected with { Action = NativeCommunityRevisionAction.Promote }, token);

    public Task<NativeCommunityRevisionCatalog> ChangeAsync(NativeCommunityPromotion expected, CancellationToken token)
    {
        if (expected.Account == Guid.Empty || expected.AccountLease == null)
            throw new InvalidOperationException("Community account must be verified.");
        return HunterLicenseClient.WithNativeCommunityIdentityAsync(expected.AccountLease, expected.Account,
            async (account, lease, credentials, ct) =>
            {
                var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource(), credentials);
                var request = new NativeCommunityHistoryRequest(expected.MapId, true, expected.LatestHash) { AccountLease = lease };
                var fresh = await ReadCore(api, request, account, lease, ct).ConfigureAwait(false);
                if (!NativeCommunityRevisionPolicy.MatchesPreflight(expected, fresh))
                    throw new InvalidOperationException("Community project changed after confirmation.");
                ct.ThrowIfCancellationRequested();
                // These established endpoints do not accept an expected revision/hash CAS witness.
                // Re-read first and verify afterwards; cancellation cannot undo a server-accepted write.
                switch (expected.Action)
                {
                    case NativeCommunityRevisionAction.Promote:
                        _ = await api.PromoteRevisionAsync(expected.MapId, expected.Number, ct).ConfigureAwait(false); break;
                    case NativeCommunityRevisionAction.Publish:
                        await api.SetRevisionVisibilityAsync(expected.Hash, "Published", ct).ConfigureAwait(false); break;
                    case NativeCommunityRevisionAction.Unlist:
                        await api.SetRevisionVisibilityAsync(expected.Hash, "Unlisted", ct).ConfigureAwait(false); break;
                    case NativeCommunityRevisionAction.Draft:
                        await api.SetRevisionVisibilityAsync(expected.Hash, "Draft", ct).ConfigureAwait(false); break;
                    case NativeCommunityRevisionAction.Delete:
                        _ = await api.UseClientAsync(true, client => client.DeleteRevisionAsync(expected.MapId, expected.Number, ct), ct).ConfigureAwait(false); break;
                    case NativeCommunityRevisionAction.Restore:
                        _ = await api.RestoreRevisionAsync(expected.MapId, expected.Number, ct).ConfigureAwait(false); break;
                    default: throw new ArgumentOutOfRangeException(nameof(expected.Action));
                }
                ct.ThrowIfCancellationRequested();
                var result = await ReadCore(api, request, account, lease, ct, requireLatestAnchor: false).ConfigureAwait(false);
                if (!NativeCommunityRevisionPolicy.VerifiedReadback(expected, result))
                    throw new InvalidOperationException("Community revision readback changed.");
                return result;
            }, token);
    }

    private static async Task<NativeCommunityRevisionCatalog> ReadCore(CommunityMapOperations api,
        NativeCommunityHistoryRequest request, Guid account, object? lease, CancellationToken token, bool requireLatestAnchor = true)
    {
        if (request.MapId == Guid.Empty) throw new InvalidOperationException("Invalid Community map.");
        CommunityMapProject? project = request.Owned
            ? await api.GetOwnedProjectAsync(request.MapId, token).ConfigureAwait(false)
            : await api.UseClientAsync(false, client => client.GetProjectAsync(request.MapId, token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (project == null || project.MapId != request.MapId || requireLatestAnchor && request.Owned && project.LatestHash != request.AnchorHash)
            throw new InvalidOperationException("Community project changed.");
        if (request.ReconcileAfterWrite && (!project.CanManageLifecycle || project.ArchivedAt != null || project.DeletedAt != null))
            throw new InvalidOperationException("Community project authority changed.");
        CommunityMapRevision[] history = request.Owned
            ? await api.GetRevisionsAsync(request.MapId, token).ConfigureAwait(false)
            : await api.UseClientAsync(false, client => client.GetRevisionsAsync(request.MapId, token), token).ConfigureAwait(false);
        if (history == null || history.Length > 4096 || history.Any(row => row?.Package == null
            || row.Package.MapId != request.MapId || !String.Equals(row.Hash, row.Package.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Invalid Community history.");
        var rows = history.Select(row => new NativeCommunityRevision(row.RevisionNumber, row.Hash,
            row.ParentHash ?? "", row.ReleaseNotes ?? "", row.CreatedAt, row.Package.Listed, row.Package.Draft, row.DeletedAt != null,
            new(request.MapId, row.Package.DisplayName ?? row.Package.Name, row.Package.Author ?? "Unknown creator",
                row.Hash, row.RevisionNumber, String.Join(", ", row.Package.SupportedModes ?? Array.Empty<string>()),
                row.Package.MinPlayers, row.Package.MaxPlayers)) { DeleteAfter = row.DeleteAfter }).ToArray();
        return new(request.MapId, project.DisplayName ?? project.Name, project.CurrentHash ?? "", project.LatestHash,
            request.Owned && project.CanManageLifecycle, project.ArchivedAt != null, project.DeletedAt != null, rows)
        { Account = account, AccountLease = lease, OwnerId = request.Owned ? project.OwnerId : "" };
    }
}
#endif
