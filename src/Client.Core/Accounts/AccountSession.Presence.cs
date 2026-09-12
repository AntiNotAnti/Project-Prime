using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Accounts;

public sealed record PresenceDirectorySnapshot(long Revision, int TotalOnline,
    int VisibleOnline, ImmutableArray<PublicPresenceEntry> Entries,
    DateTimeOffset GeneratedAt);

public sealed partial class AccountSession
{
    /// <summary>
    /// Reads one complete, revision-pinned public presence snapshot. The call
    /// is deliberately anonymous: a presence outage or authorization failure
    /// must never refresh, clear, or otherwise affect the account session.
    /// </summary>
    public async Task<PresenceDirectorySnapshot> GetPresenceAsync(
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await ReadPresenceSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AccountServiceException error) when (
                error.StatusCode == HttpStatusCode.Conflict
                && StringComparer.Ordinal.Equals(error.ErrorCode,
                    "directory_revision_changed")
                && attempt < 2)
            {
                // Restart from page zero. Never publish a snapshot assembled
                // across two public-directory revisions.
            }
        }

        throw new InvalidOperationException(
            "The online player directory changed repeatedly while it was being read.");
    }

    private async Task<PresenceDirectorySnapshot> ReadPresenceSnapshotAsync(
        CancellationToken cancellationToken)
    {
        PresenceDirectoryPage first = await GetPresencePageAsync(0, null,
            cancellationToken).ConfigureAwait(false);
        ValidatePresencePage(first, expectedPage: 0, firstPage: null);

        var entries = new List<PublicPresenceEntry>(first.VisibleOnline);
        entries.AddRange(first.Entries);
        for (int page = 1; page < first.PageCount; page++)
        {
            PresenceDirectoryPage next = await GetPresencePageAsync(page,
                first.Revision, cancellationToken).ConfigureAwait(false);
            ValidatePresencePage(next, page, first);
            entries.AddRange(next.Entries);
        }

        if (entries.Count != first.VisibleOnline
            || entries.Count > PresenceContract.MaximumEntries)
        {
            throw new InvalidOperationException(
                "Backend returned an incomplete online player directory.");
        }

        // Duplicate display names are valid and intentionally retained. The
        // Backend identity and session keys never cross this public boundary.
        return new PresenceDirectorySnapshot(first.Revision, first.TotalOnline,
            first.VisibleOnline, entries.ToImmutableArray(), first.GeneratedAt);
    }

    private Task<PresenceDirectoryPage> GetPresencePageAsync(int page,
        long? revision, CancellationToken cancellationToken)
    {
        string path = $"v1/presence?page={page.ToString(CultureInfo.InvariantCulture)}";
        if (revision is { } pinned)
        {
            path += $"&revision={pinned.ToString(CultureInfo.InvariantCulture)}";
        }

        return SendAsync<PresenceDirectoryPage>(HttpMethod.Get, path, null,
            accessToken: null, cancellationToken);
    }

    private static void ValidatePresencePage(PresenceDirectoryPage page,
        int expectedPage, PresenceDirectoryPage? firstPage)
    {
        if (page is null || page.Revision < 0 || page.TotalOnline < 0
            || page.VisibleOnline is < 0 or > PresenceContract.MaximumEntries
            || page.Page != expectedPage
            || page.PageCount is < 1 or > PresenceContract.MaximumPages
            || page.Page >= page.PageCount || page.Entries.IsDefault
            || page.Entries.Length > PresenceContract.PageSize
            || page.GeneratedAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Backend returned an invalid online player directory page.");
        }

        int expectedPageCount = Math.Max(1,
            (page.VisibleOnline + PresenceContract.PageSize - 1)
            / PresenceContract.PageSize);
        int expectedEntries = Math.Min(PresenceContract.PageSize,
            Math.Max(0, page.VisibleOnline
                - expectedPage * PresenceContract.PageSize));
        if (page.PageCount != expectedPageCount
            || page.Entries.Length != expectedEntries
            || firstPage is { Revision: var revision }
                && page.Revision != revision
            || firstPage is { TotalOnline: var total }
                && page.TotalOnline != total
            || firstPage is { VisibleOnline: var visible }
                && page.VisibleOnline != visible
            || firstPage is { PageCount: var pageCount }
                && page.PageCount != pageCount)
        {
            throw new InvalidOperationException(
                "Backend returned an inconsistent online player directory page.");
        }

        foreach (PublicPresenceEntry? entry in page.Entries)
        {
            if (entry is null || !PresenceContract.IsValidDisplayName(entry.DisplayName)
                || !Enum.IsDefined(entry.Activity)
                || !PresenceContract.IsValidRegion(entry.Region))
            {
                throw new InvalidOperationException(
                    "Backend returned an invalid online player entry.");
            }
        }
    }
}
