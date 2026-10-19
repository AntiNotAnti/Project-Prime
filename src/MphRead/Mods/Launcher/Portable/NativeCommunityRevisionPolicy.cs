using System;
using System.Linq;

namespace MphRead.Mods.Launcher;

/// <summary>Client preflight/readback checks over existing non-CAS Community endpoints.</summary>
internal static class NativeCommunityRevisionPolicy
{
    internal static bool Can(NativeCommunityRevisionAction action, NativeCommunityRevisionCatalog catalog, NativeCommunityRevision row)
    {
        if (!catalog.CanManage || catalog.Archived || catalog.Deleted || !Enum.IsDefined(action)) return false;
        if (action == NativeCommunityRevisionAction.Restore)
            return row.Deleted && (row.DeleteAfter == null || row.DeleteAfter > DateTimeOffset.UtcNow);
        if (row.Deleted) return false;
        return action switch
        {
            NativeCommunityRevisionAction.Promote => row.Hash != catalog.CurrentHash,
            NativeCommunityRevisionAction.Publish => !row.Listed || row.Draft,
            NativeCommunityRevisionAction.Unlist => row.Listed || row.Draft,
            NativeCommunityRevisionAction.Draft => !row.Draft,
            NativeCommunityRevisionAction.Delete => catalog.Revisions.Count(item => !item.Deleted) > 1,
            _ => false
        };
    }

    internal static bool MatchesPreflight(NativeCommunityPromotion expected, NativeCommunityRevisionCatalog fresh)
    {
        if (!SameOwner(expected, fresh) || fresh.CurrentHash != expected.CurrentHash || fresh.LatestHash != expected.LatestHash
            || fresh.Revisions == null || expected.ExpectedHistory.Count != fresh.Revisions.Length) return false;
        foreach (var before in expected.ExpectedHistory)
            if (fresh.Revisions.Count(row => row == before) != 1) return false;
        var selected = fresh.Revisions.SingleOrDefault(row => row.Number == expected.Number && row.Hash == expected.Hash);
        return selected != null && Can(expected.Action, fresh, selected);
    }

    internal static bool VerifiedReadback(NativeCommunityPromotion expected, NativeCommunityRevisionCatalog? result)
    {
        if (result == null || !SameOwner(expected, result) || result.Revisions == null
            || expected.ExpectedHistory.Count == 0 || expected.ExpectedHistory.Count != result.Revisions.Length) return false;
        foreach (var before in expected.ExpectedHistory)
        {
            var matches = result.Revisions.Where(row => row != null && row.Number == before.Number).ToArray();
            if (matches.Length != 1) return false;
            var after = matches[0];
            if (after.Hash != before.Hash || after.ParentHash != before.ParentHash || after.Notes != before.Notes
                || after.CreatedAt != before.CreatedAt || after.Package != before.Package) return false;
            if (before.Hash != expected.Hash)
            {
                if (after != before) return false;
                continue;
            }
            bool valid = expected.Action switch
            {
                NativeCommunityRevisionAction.Promote or NativeCommunityRevisionAction.Publish => !after.Deleted && after.Listed && !after.Draft && after.DeleteAfter == before.DeleteAfter,
                NativeCommunityRevisionAction.Unlist => !after.Deleted && !after.Listed && !after.Draft && after.DeleteAfter == before.DeleteAfter,
                NativeCommunityRevisionAction.Draft => !after.Deleted && !after.Listed && after.Draft && after.DeleteAfter == before.DeleteAfter,
                NativeCommunityRevisionAction.Delete => after.Deleted && after.Listed == before.Listed && after.Draft == before.Draft && after.DeleteAfter > DateTimeOffset.UtcNow,
                NativeCommunityRevisionAction.Restore => !after.Deleted && after.Listed == before.Listed && after.Draft == before.Draft && after.DeleteAfter == null,
                _ => false
            };
            if (!valid) return false;
        }
        var active = result.Revisions.Where(row => !row.Deleted).OrderByDescending(row => row.Number).ToArray();
        if (active.Length == 0) return false;
        string latest = expected.Action is NativeCommunityRevisionAction.Delete or NativeCommunityRevisionAction.Restore
            ? active[0].Hash : expected.LatestHash;
        string current = expected.Action is NativeCommunityRevisionAction.Promote or NativeCommunityRevisionAction.Publish
            ? expected.Hash : active.Any(row => row.Hash == expected.CurrentHash && row.Listed && !row.Draft)
                ? expected.CurrentHash : active.FirstOrDefault(row => row.Listed && !row.Draft)?.Hash ?? "";
        return result.LatestHash == latest && result.CurrentHash == current;
    }

    private static bool SameOwner(NativeCommunityPromotion expected, NativeCommunityRevisionCatalog actual)
        => expected.Account != Guid.Empty && expected.AccountLease != null
            && actual.MapId == expected.MapId && actual.Account == expected.Account
            && ReferenceEquals(actual.AccountLease, expected.AccountLease) && actual.OwnerId == expected.OwnerId
            && actual.CanManage && !actual.Archived && !actual.Deleted;

    internal static string Confirmation(NativeCommunityPromotion change)
    {
        string action = change.Action switch
        {
            NativeCommunityRevisionAction.Promote => $"MAKE REVISION {change.Number} THE CURRENT PUBLIC REVISION?",
            NativeCommunityRevisionAction.Publish => $"PUBLISH RETAINED REVISION {change.Number} AND MAKE IT CURRENT?",
            NativeCommunityRevisionAction.Unlist => $"UNLIST REVISION {change.Number}?",
            NativeCommunityRevisionAction.Draft => $"MAKE REVISION {change.Number} A PRIVATE DRAFT?",
            NativeCommunityRevisionAction.Delete => $"DELETE REVISION {change.Number} FROM THE ACTIVE CATALOG?",
            _ => $"RESTORE DELETED REVISION {change.Number}?"
        };
        string consequence = change.Action switch
        {
            NativeCommunityRevisionAction.Unlist or NativeCommunityRevisionAction.Draft => "The current public pointer may change. Previously shared CDN links may remain accessible.",
            NativeCommunityRevisionAction.Delete => "Immutable history is retained until the service restore deadline. Current/latest pointers may change.",
            NativeCommunityRevisionAction.Restore => "The service restore deadline applies. Restoring does not replace a valid current public revision.",
            _ => "Newer immutable revisions are preserved."
        };
        return action + "\nExact package: " + change.Hash + "\n" + consequence + " Confirm or cancel.";
    }
}
