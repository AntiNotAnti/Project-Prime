using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>
/// The Community credential supplier. The shared map service does not depend on
/// Hunter License, an editor, a UI toolkit, or a persisted bearer token.
/// </summary>
public interface ICommunityCredentialSource
{
    Task<string> GetTicketAsync(CancellationToken cancellation);
    Task<string> RefreshTicketAsync(CancellationToken cancellation);
}

/// <summary>
/// UI-neutral bridge to the caller's account/session implementation.
/// Credentials are requested for each operation; neither this class nor the
/// Community map service serializes or stores them.
/// </summary>
public sealed class DelegateCommunityCredentialSource(
    Func<CancellationToken, Task<string>> getTicket,
    Func<CancellationToken, Task<string>> refreshTicket) : ICommunityCredentialSource
{
    public Task<string> GetTicketAsync(CancellationToken cancellation)
        => getTicket(cancellation);
    public Task<string> RefreshTicketAsync(CancellationToken cancellation)
        => refreshTicket(cancellation);
}

/// <summary>
/// Map-specific application facade over the existing v2/legacy Community client.
/// It owns temporary HTTP client lifetimes and 401 refresh/retry, but not map
/// compilation, UI state, editable documents, installations or gameplay authority.
///
/// This is intentionally a small compatibility layer for PR-01: the general v3
/// content model, job manager, persistent cache and new routes are later slices.
/// </summary>
public sealed class CommunityMapOperations
{
    private readonly string _endpoint;
    private readonly ICommunityCredentialSource? _credentials;

    public CommunityMapOperations(string endpoint, ICommunityCredentialSource? credentials = null)
    {
        // MapCommunityClient remains the sole URL/TLS policy validator. Creating
        // an HTTP client just to validate every browse/publish request is wasteful.
        _endpoint = !string.IsNullOrWhiteSpace(endpoint) ? endpoint
            : throw new ArgumentException("A Community service address is required.", nameof(endpoint));
        _credentials = credentials;
    }

    /// <summary>
    /// Execute one operation against an owned, short-lived client. A 401 from an
    /// authenticated call mints a fresh ticket and retries exactly once, as the
    /// existing editor did. Operation callbacks must be safe to retry on a 401;
    /// the existing publishing path is idempotent by immutable package hash.
    /// </summary>
    public async Task<T> UseClientAsync<T>(bool authenticated,
        Func<MapCommunityClient, Task<T>> action, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellation.ThrowIfCancellationRequested();

        if (!authenticated)
        {
            using var anonymous = new MapCommunityClient(_endpoint);
            return await action(anonymous).ConfigureAwait(false);
        }

        if (_credentials == null)
            throw new InvalidOperationException(
                "An authenticated Community operation requires a credential source.");

        string ticket = await _credentials.GetTicketAsync(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        using (var original = new MapCommunityClient(_endpoint, ticket))
        {
            try
            {
                return await action(original).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized
                && !cancellation.IsCancellationRequested)
            {
                // Do not reuse a possibly cached-but-rejected ticket.
            }
        }

        cancellation.ThrowIfCancellationRequested();
        ticket = await _credentials.RefreshTicketAsync(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        using var refreshed = new MapCommunityClient(_endpoint, ticket);
        try
        {
            return await action(refreshed).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized
            && !cancellation.IsCancellationRequested)
        {
            throw new HttpRequestException(
                "The Community service rejected a freshly minted Hunter License credential. "
                + "Redeploy/restart the map service so it includes Hunter License ticket verification.",
                ex, HttpStatusCode.Unauthorized);
        }
    }

    public Task<CommunityContentPage> BrowseContentAsync(
        CancellationToken cancellation, string? kind = null, bool mine = false,
        string? query = null, string? sort = null, int page = 1, int pageSize = 24)
        => UseClientAsync(mine, client => client.BrowseContentAsync(
            cancellation, kind, mine, query, sort, page, pageSize), cancellation);

    public Task<CommunityContentProject?> GetContentAsync(
        string kind, Guid contentId, CancellationToken cancellation,
        bool ownerView = false)
        => UseClientAsync(ownerView, client => client.GetContentAsync(
            kind, contentId, cancellation), cancellation);

    public Task<CommunityContentRevision[]> GetContentRevisionsAsync(
        string kind, Guid contentId, CancellationToken cancellation,
        bool ownerView = false)
        => UseClientAsync(ownerView, client => client.GetContentRevisionsAsync(
            kind, contentId, cancellation), cancellation);

    public Task<CommunityMapProject[]> BrowseProjectsAsync(
        bool mine, bool favorites, string? sort, CancellationToken cancellation)
        => UseClientAsync(mine || favorites,
            client => client.BrowseProjectsAsync(cancellation, mine, favorites, sort),
            cancellation);

    public Task<CommunityMapProjectPage> BrowseProjectsPageAsync(
        bool mine, bool favorites, string? sort, string? query,
        int page, int pageSize, CancellationToken cancellation,
        string? mode = null, int? players = null)
        => UseClientAsync(mine || favorites,
            client => client.BrowseProjectsPageAsync(
                cancellation, mine, favorites, sort, query, page, pageSize, mode, players),
            cancellation);

    public Task<byte[]?> GetPreviewAsync(string packageHash, CancellationToken cancellation)
        => UseClientAsync(false, client => client.GetPreviewAsync(packageHash, cancellation),
            cancellation);

    public Task<CommunityMap?> GetPackageAsync(string packageHash, CancellationToken cancellation)
        => UseClientAsync(false, client => client.GetPackageAsync(packageHash, cancellation),
            cancellation);

    public Task<MapDefinition> InstallAsync(CommunityMap map, string library,
        CancellationToken cancellation, Action<long, long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        return UseClientAsync(map.Draft,
            client => client.InstallAsync(map, library, cancellation, progress), cancellation);
    }

    public Task<CommunityMap> UploadPackageAsync(string archivePath, CancellationToken cancellation,
        bool listed = true, bool draft = false, Action<long, long>? progress = null)
        => UseClientAsync(true, client => client.UploadAsync(
            archivePath, cancellation, listed, draft, progress), cancellation);

    public Task<CommunityPublishResult> PublishPackageAsync(string archivePath,
        CommunityPublishRequest request, CancellationToken cancellation,
        bool listed = true, bool draft = false, Action<long, long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return UseClientAsync(true,
            client => client.PublishAsync(archivePath, request, cancellation, listed, draft, progress),
            cancellation);
    }
}
