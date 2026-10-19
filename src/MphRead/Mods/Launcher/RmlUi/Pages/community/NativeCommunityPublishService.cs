#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Existing frozen Studio, package validator and shared durable queue; no editable document owner.</summary>
internal sealed class NativeCommunityPublishService(CommunityStudioPublishIntent intent) : INativeCommunityPublishAuthority
{
    private readonly string _endpoint = NetworkMapIdentity.ConfiguredDownloadSource();
    public bool IsCurrentAccountLease(object? lease) => HunterLicenseClient.IsNativeAccountLeaseCurrent(lease);
    public Task<NativeCommunityPublishDraft> ReadAsync(NativeCommunityPublishSource source, CancellationToken token)
    {
        if (source.MapId != intent.MapId || source.RuntimeName != intent.RuntimeName) throw new InvalidOperationException("Frozen project changed.");
        return HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null, async (subject, lease, credentials, ct) =>
        {
            var operations = new CommunityMapOperations(_endpoint, credentials);
            CommunityMapProject? project = await operations.GetOwnedProjectAsync(intent.MapId, ct).ConfigureAwait(false);
            ValidateProject(project);
            return new NativeCommunityPublishDraft(intent.MapId, intent.RuntimeName, project != null, project?.LatestHash ?? "")
                { Account = new CommunityTransferAccount(subject), AccountLease = lease };
        }, token);
    }
    public async Task<NativeCommunityPublishReceipt> QueueAsync(NativeCommunityPublishDraft draft,
        NativeCommunityPublishVisibility visibility, string notes, CancellationToken token)
    {
        CommunityTransferAccount account = Require(draft.Account, draft.AccountLease);
        if (draft.MapId != intent.MapId || draft.RuntimeName != intent.RuntimeName) throw new InvalidOperationException("Frozen project changed.");
        // This scope ends before queue admission: the queue independently verifies the
        // captured subject and must never re-enter the auth gate from inside this callback.
        await HunterLicenseClient.WithNativeCommunityIdentityAsync(draft.AccountLease, account.Subject,
            async (_, _, credentials, ct) =>
            {
                CommunityMapProject? current = await new CommunityMapOperations(_endpoint, credentials)
                    .GetOwnedProjectAsync(intent.MapId, ct).ConfigureAwait(false);
                ValidateProject(current);
                if ((current != null) != draft.IsRevision || (current?.LatestHash ?? "") != draft.ParentHash)
                    throw new InvalidOperationException("Confirmed project parent changed.");
                return true;
            }, token).ConfigureAwait(false);
        var receipt = await intent.QueueAsync(_endpoint, new CommunityMapOperations(_endpoint), CommunityTransferSession.Shared,
            (CommunityPublishVisibility)visibility, notes, token, account,
            new CommunityPublishRequest(draft.IsRevision, draft.IsRevision ? draft.ParentHash : null, notes, false)).ConfigureAwait(false);
        return new(receipt.JobId, receipt.MapId, receipt.RuntimeName, receipt.PackageHash, receipt.IsRevision,
            receipt.ExpectedParentHash ?? "", (NativeCommunityPublishVisibility)receipt.Visibility)
            { Account = draft.Account, AccountLease = draft.AccountLease };
    }
    public async Task<NativeCommunityPublishProgress> ReadTransferAsync(NativeCommunityPublishReceipt receipt, CancellationToken token)
    {
        CommunityTransferAccount account = Require(receipt.Account, receipt.AccountLease);
        CommunityTransferJob job = await Task.Run(() => CommunityTransferSession.Shared.GetJob(receipt.JobId), token).ConfigureAwait(false);
        ValidateJob(job, receipt, account);
        if (job.State == CommunityTransferState.Completed)
        {
            await HunterLicenseClient.WithNativeCommunityIdentityAsync(receipt.AccountLease, account.Subject,
                async (_, _, credentials, ct) =>
                {
                    var api = new CommunityMapOperations(_endpoint, credentials);
                    CommunityMapProject? project = await api.GetOwnedProjectAsync(intent.MapId, ct).ConfigureAwait(false);
                    ValidateProject(project);
                    CommunityMapRevision[] rows = await api.GetRevisionsAsync(intent.MapId, ct).ConfigureAwait(false);
                    CommunityMapRevision? row = rows.SingleOrDefault(row => row.Hash == receipt.Hash);
                    if (row == null || row.Package.MapId != receipt.MapId || row.Package.Hash != receipt.Hash
                        || row.Package.Name != receipt.RuntimeName || (row.ParentHash ?? "") != receipt.ParentHash
                        || row.Package.Draft != (receipt.Visibility == NativeCommunityPublishVisibility.Draft)
                        || row.Package.Listed != (receipt.Visibility == NativeCommunityPublishVisibility.Published) || row.DeletedAt != null)
                        throw new InvalidDataException("Published exact package could not be verified.");
                    return true;
                }, token).ConfigureAwait(false);
        }
        return Progress(job);
    }
    public async Task<NativeCommunityPublishProgress> ControlTransferAsync(NativeCommunityPublishReceipt receipt, bool pause, CancellationToken token)
    {
        CommunityTransferAccount account = Require(receipt.Account, receipt.AccountLease);
        CommunityTransferManager queue = CommunityTransferSession.Shared;
        CommunityTransferJob job = await Task.Run(() => queue.GetJob(receipt.JobId), token).ConfigureAwait(false);
        ValidateJob(job, receipt, account);
        await queue.CaptureUploadAccountAsync(account, token).ConfigureAwait(false);
        if (pause) queue.Pause(job.Id);
        else
        {
            if (job.State == CommunityTransferState.NeedsAuthorization)
                await queue.ReauthorizeUploadAsync(job.Id, account, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); queue.Resume(job.Id);
        }
        return await ReadTransferAsync(receipt, token).ConfigureAwait(false);
    }
    private CommunityTransferAccount Require(object? account, object? lease)
        => account is CommunityTransferAccount valid && IsCurrentAccountLease(lease) ? valid : throw new CommunityTransferAuthorizationException();
    private void ValidateProject(CommunityMapProject? project)
    {
        if (project != null && (project.MapId != intent.MapId || !project.CanManageLifecycle || project.ArchivedAt != null
            || project.DeletedAt != null || !MapCommunityClient.ValidHash(project.LatestHash)))
            throw new InvalidOperationException("Current account cannot publish this project.");
    }
    private void ValidateJob(CommunityTransferJob job, NativeCommunityPublishReceipt receipt, CommunityTransferAccount account)
    {
        if (receipt.MapId != intent.MapId || receipt.RuntimeName != intent.RuntimeName || job.Id != receipt.JobId
            || job.Kind != CommunityTransferKind.Upload || job.Endpoint != _endpoint || job.PackageHash != receipt.Hash
            || !account.Matches(job.Account) || job.Publish == null || job.Publish.ExistingMap != receipt.IsRevision
            || (job.Publish.ExpectedParentHash ?? "") != receipt.ParentHash || job.Publish.AllowStaleParent
            || job.Listed != (receipt.Visibility == NativeCommunityPublishVisibility.Published)
            || job.Draft != (receipt.Visibility == NativeCommunityPublishVisibility.Draft))
            throw new InvalidDataException("Exact upload receipt changed.");
    }
    private static NativeCommunityPublishProgress Progress(CommunityTransferJob job)
        => new(job.Id, job.PackageHash, job.State.ToString().ToUpperInvariant(), job.AcknowledgedBytes, job.TotalBytes,
            job.State == CommunityTransferState.Completed,
            job.State is CommunityTransferState.Queued or CommunityTransferState.Preparing or CommunityTransferState.Uploading or CommunityTransferState.Retryable,
            job.State is CommunityTransferState.Paused or CommunityTransferState.Retryable or CommunityTransferState.Failed or CommunityTransferState.NeedsAuthorization,
            job.State == CommunityTransferState.NeedsReview);
}
#endif
