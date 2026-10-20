#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Existing catalog/account and immutable installed-map owners, read off the compositor thread.</summary>
internal sealed class NativeCommunityReader : INativeCommunityLibraryReader, INativeCommunityPreviewSource, INativeCommunityReportAuthority
{
    private sealed record Witness(NativeCommunityCatalog Catalog, string Root, object? Account);

    public bool IsCurrentWitness(object? witness) => witness switch
    {
        null => true,
        Witness { Catalog: NativeCommunityCatalog.Installed } local
            => local.Root == GameFiles.Root && GameFiles.Ready,
        Witness { Account: not null } account => HunterLicenseClient.IsNativeAccountLeaseCurrent(account.Account),
        _ => false
    };

    public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken token)
        => BrowseLibraryAsync(NativeCommunityCatalog.Discover, query, sort, page, token);

    public Task<NativeCommunityPage> BrowseLibraryAsync(NativeCommunityCatalog catalog,
        string query, string sort, int page, CancellationToken token)
        => Task.Run(async () =>
        {
            if (catalog == NativeCommunityCatalog.Installed) return ReadInstalled(query, sort, page, token);
            if (catalog == NativeCommunityCatalog.Favorites)
                return await HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null,
                    async (account, lease, credentials, ct) => await ReadCatalog(
                        new(NetworkMapIdentity.ConfiguredDownloadSource(), credentials), true,
                        query, sort, page, new Witness(catalog, "", lease), ct).ConfigureAwait(false), token).ConfigureAwait(false);
            return await ReadCatalog(new(NetworkMapIdentity.ConfiguredDownloadSource()), false,
                query, sort, page, null, token).ConfigureAwait(false);
        }, token);

    private static async Task<NativeCommunityPage> ReadCatalog(CommunityMapOperations api,
        bool favorites, string query, string sort, int page, object? witness, CancellationToken token)
    {
        CommunityMapProjectPage result = await api.BrowseProjectsPageAsync(false, favorites,
            sort, query, page, 8, token).ConfigureAwait(false);
        var entries = result.Items.Where(p => p != null && p.MapId != Guid.Empty).Select(p =>
        {
            CommunityMapRevision revision = p.CurrentRevision ?? p.LatestRevision;
            CommunityMap map = revision.Package;
            return new NativeCommunityEntry(p.MapId, p.DisplayName ?? p.Name,
                p.Author ?? "Unknown creator", map.Hash, revision.RevisionNumber,
                string.Join(", ", map.SupportedModes), map.MinPlayers, map.MaxPlayers)
            {
                Version = map.Version ?? "", Description = revision.ReleaseNotes ?? "",
                Favorited = favorites || map.Favorited, FavoriteCount = Math.Max(0, map.FavoriteCount),
                MinimumProtocol = map.MinimumProtocol, ArchiveBytes = Math.Max(0, map.Bytes)
            };
        }).ToArray();
        return new(entries, result.Total, result.Page, result.PageSize) { Witness = witness };
    }

    private static NativeCommunityPage ReadInstalled(string query, string sort, int page, CancellationToken token)
    {
        string root = GameFiles.Root;
        using var files = GameFiles.TryUseConfiguredFiles(root)
            ?? throw new InvalidOperationException("Game-file configuration is unavailable.");
        if (!GameFiles.Ready) throw new InvalidOperationException("Game files are not configured.");
        var definitions = CustomRooms.Definitions.ToArray();
        InstalledMapRegistry registry = CustomRooms.Installed;
        var rows = definitions.Where(map => map.BundlePath != null && !map.RequiresPrivateSource
            && registry.TryGet(map.MapId, out _) && ((map.InGameName ?? map.Name).Contains(query, StringComparison.OrdinalIgnoreCase)
                || (map.Author ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)));
        rows = sort == "name" ? rows.OrderBy(m => m.InGameName ?? m.Name, StringComparer.OrdinalIgnoreCase)
            : rows.OrderByDescending(m => File.GetLastWriteTimeUtc(m.BundlePath!))
                .ThenBy(m => m.InGameName ?? m.Name, StringComparer.OrdinalIgnoreCase);
        var matches = rows.ToArray(); token.ThrowIfCancellationRequested();
        var entries = matches.Skip((page - 1) * 8).Take(8).Select(map =>
        {
            if (!registry.TryGet(map.MapId, out var exact)) throw new InvalidOperationException("Installed map changed.");
            return new NativeCommunityEntry(map.MapId, map.InGameName ?? map.Name, map.Author ?? "Local author",
                exact.Identity.PackageHash.ToString(), 0, "LOCAL INSTALLED PACKAGE", 0, 0)
            {
                Installed = true, RoomKey = exact.Identity.RoomKey, Version = exact.Version ?? "",
                Description = map.Description ?? "", ArchiveBytes = new FileInfo(exact.PackagePath).Length
            };
        }).ToArray();
        return new(entries, matches.Length, page, 8) { Witness = new Witness(NativeCommunityCatalog.Installed, root, null) };
    }

    public Task<byte[]?> ReadPreviewAsync(string exactHash, CancellationToken token)
        => Task.Run(() => new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource())
            .GetPreviewAsync(exactHash, token), token);

    public Task<object> SetFavoriteAsync(NativeCommunityEntry selected, bool favorite,
        object? expectedWitness, CancellationToken token)
    {
        if (!NativeCommunityInstallController.IsVerifiedSelection(selected)
            || selected.Installed || !IsCurrentWitness(expectedWitness))
            throw new InvalidOperationException("Community selection/account changed.");
        object? lease = expectedWitness is Witness account ? account.Account : null;
        return HunterLicenseClient.WithNativeCommunityIdentityAsync<object>(lease, null,
            async (account, current, credentials, ct) =>
            {
                var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource(), credentials);
                await api.UseClientAsync(true, async client =>
                {
                    await client.SetFavoriteAsync(selected.MapId, favorite, ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
                return new Witness(NativeCommunityCatalog.Favorites, "", current);
            }, token);
    }

    public Task<object> PrepareAsync(NativeCommunityEntry selection, object? catalogWitness, CancellationToken token)
    {
        if (!NativeCommunityInstallController.IsVerifiedSelection(selection) || selection.Installed
            || selection.Version is null || selection.Version.Length > 64 || !IsCurrentWitness(catalogWitness))
            throw new InvalidOperationException("Community report selection changed.");
        object? expected = catalogWitness is Witness witness ? witness.Account : null;
        return HunterLicenseClient.WithNativeCommunityIdentityAsync<object>(expected, null,
            (account, lease, credentials, ct) => Task.FromResult(lease), token);
    }

    public bool IsCurrentLease(object lease) => HunterLicenseClient.IsNativeAccountLeaseCurrent(lease);

    public Task SubmitAsync(NativeCommunityEntry selection, string reason, string details, object lease, CancellationToken token)
    {
        if (!NativeCommunityInstallController.IsVerifiedSelection(selection) || selection.Installed
            || selection.Version is null || selection.Version.Length > 64
            || !MapCreatorCatalog.ReportReasons.Contains(reason, StringComparer.Ordinal)
            || details is null || details.Length > 4000)
            throw new InvalidOperationException("Invalid Community report.");
        return HunterLicenseClient.WithNativeCommunityIdentityAsync(lease, null,
            async (account, current, credentials, ct) =>
            {
                var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource(), credentials);
                return await api.UseClientAsync(true, async client =>
                {
                    await client.ReportAsync(selection.MapId, new MapReportRequest(selection.Version, reason, details), ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
            }, token);
    }
}
#endif
