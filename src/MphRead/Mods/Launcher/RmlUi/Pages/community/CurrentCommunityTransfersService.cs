#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Worker-only access to the one existing journal/transfer manager.</summary>
internal sealed class CurrentCommunityTransfersService : INativeCommunityTransfersAuthority
{
    public Task<NativeCommunityTransferReceipt[]> ReadAsync(CancellationToken token)
        => Task.Run(() => Read(CommunityTransferSession.Shared, token), token);

    public Task<NativeCommunityTransferReceipt[]> ControlAsync(NativeCommunityTransferReceipt expected,
        NativeCommunityTransferAction action, CancellationToken token)
        => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var queue = CommunityTransferSession.Shared;
            var current = queue.GetJob(expected.Id);
            if (expected.QueueWitness is not CommunityTransferJob captured
                || expected.Id != captured.Id || expected.Kind != captured.Kind
                || expected.PackageHash != captured.PackageHash.ToLowerInvariant() || expected.Total != captured.TotalBytes
                || current.Id != captured.Id || current.Kind != captured.Kind || current.PackageHash != captured.PackageHash
                || current.TotalBytes != captured.TotalBytes || current.Endpoint != captured.Endpoint
                || current.Attempt != captured.Attempt || current.Download?.MapId != captured.Download?.MapId
                || current.Download?.ContentHash != captured.Download?.ContentHash
                || (captured.Account == null ? current.Account != null : !captured.Account.Matches(current.Account))
                || !NativeCommunityTransfersController.Allows(ToReceipt(current), action))
                throw new InvalidOperationException("The transfer changed; refresh before trying again.");
            token.ThrowIfCancellationRequested();
            // Resume deliberately retains the manager's account/review guards.
            // History never silently reauthorizes or resolves a revision conflict.
            switch (action)
            {
                case NativeCommunityTransferAction.Pause: queue.Pause(current.Id); break;
                case NativeCommunityTransferAction.Resume: queue.Resume(current.Id); break;
                case NativeCommunityTransferAction.Cancel: queue.Cancel(current.Id); break;
                case NativeCommunityTransferAction.Forget: queue.Forget(current.Id); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
            return Read(queue, token);
        }, token);

    private static NativeCommunityTransferReceipt[] Read(CommunityTransferManager queue, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rows = queue.ListJobs().Take(32).Select(ToReceipt).ToArray();
        token.ThrowIfCancellationRequested(); return rows;
    }

    internal static NativeCommunityTransferReceipt ToReceipt(CommunityTransferJob job)
        => new(job.Id, job.Kind, job.State, job.PackageHash.ToLowerInvariant(),
            job.Download?.DisplayName ?? job.Download?.Name
                ?? (job.Kind == CommunityTransferKind.Upload ? "Creator upload" : "Community map"),
            job.AcknowledgedBytes, job.TotalBytes, job.UpdatedAt) { QueueWitness = job };
}
#endif
