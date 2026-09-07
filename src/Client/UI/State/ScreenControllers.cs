using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.UI.State;

public sealed class ServerBrowserScreenModel : IDisposable
{
    private readonly IServerBrowserController _controller;
    private CancellationTokenSource? _load;
    private IReadOnlyList<UiServerEntry> _all = [];

    public ServerBrowserScreenModel(IServerBrowserController controller) => _controller = controller;
    public AsyncScreenState Status { get; } = new();
    public UiServerFilter Filter { get; private set; } = new("", null, false, true, 0,
        UiServerSort.Ping, UiServerGroup.All);
    public IReadOnlyList<UiServerEntry> Visible { get; private set; } = [];
    public UiServerEntry? Selected { get; private set; }
    public IReadOnlyList<string> AvailableModes => _all.Select(entry => entry.Mode)
        .Where(mode => !string.IsNullOrWhiteSpace(mode))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(mode => mode, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource request = _load;
        Status.Loading("Finding servers…");
        try
        {
            IReadOnlyList<UiServerEntry> loaded = await _controller.QueryAsync(request.Token)
                .ConfigureAwait(false);
            if (!ReferenceEquals(_load, request)) return;
            _all = loaded;
            ApplyFilter(Filter);
            if (Visible.Count == 0) Status.Empty("No servers match these filters.");
            else Status.Ready();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_load, request)) return;
            Status.Failed(AsyncScreenState.FriendlyFailure(error, "The server list"));
        }
    }

    public void ApplyFilter(UiServerFilter filter)
    {
        Filter = filter;
        Visible = UiServerSelection.Apply(_all, filter);
        if (Selected is not null && !Visible.Contains(Selected)) Selected = null;
        Selected ??= Visible.Count > 0 ? Visible[0] : null;
        if (Status.State is not (UiLoadState.Idle or UiLoadState.Loading
            or UiLoadState.Offline or UiLoadState.Failed))
        {
            if (Visible.Count == 0) Status.Empty("No servers match these filters.");
            else Status.Ready();
        }
    }

    public void Select(UiServerEntry server)
    {
        if (Visible.Contains(server)) Selected = server;
    }

    public Task<UiActionResult> JoinAsync(bool spectate, CancellationToken cancellationToken = default)
        => Selected is null
            ? Task.FromResult(UiActionResult.Failure("Choose a server first."))
            : _controller.JoinAsync(Selected, spectate, cancellationToken);

    public bool? ToggleFavorite()
    {
        if (Selected is null) return null;
        string selectedId = Selected.Id;
        bool favorite = !Selected.Favorite;
        _controller.SetFavorite(Selected, favorite);
        _all = _all.Select(entry => entry.Id == selectedId
            ? entry with { Favorite = favorite }
            : entry).ToArray();
        Selected = _all.FirstOrDefault(entry => entry.Id == selectedId);
        ApplyFilter(Filter);
        return favorite;
    }

    public void Dispose()
    {
        _load?.Cancel();
        _load?.Dispose();
    }
}

public sealed class MapPickerScreenModel : IDisposable
{
    private readonly IMapCatalogController _controller;
    private readonly BoundedAsyncCache<string, byte[]?> _thumbnails;
    private CancellationTokenSource? _lifetime;

    public MapPickerScreenModel(IMapCatalogController controller, int cacheCapacity = 24,
        int concurrentLoads = 3)
    {
        _controller = controller;
        _thumbnails = new BoundedAsyncCache<string, byte[]?>(cacheCapacity, concurrentLoads);
    }

    public AsyncScreenState Status { get; } = new();
    public IReadOnlyList<UiMapEntry> Maps { get; private set; } = [];
    public UiMapEntry? Selected { get; private set; }
    public int CachedThumbnailCount => _thumbnails.Count;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource request = _lifetime;
        Status.Loading("Loading maps…");
        try
        {
            IReadOnlyList<UiMapEntry> loaded = await _controller.ListAsync(request.Token)
                .ConfigureAwait(false);
            if (!ReferenceEquals(_lifetime, request)) return;
            Maps = loaded;
            Selected = null;
            if (Maps.Count == 0) Status.Empty("No compatible maps are installed.");
            else Status.Ready();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_lifetime, request)) return;
            Status.Failed(AsyncScreenState.FriendlyFailure(error, "The map catalog"));
        }
    }

    public void Select(UiMapEntry map)
    {
        if (Maps.Contains(map)) Selected = map;
    }

    public async Task<byte[]?> ThumbnailAsync(UiMapEntry map, CancellationToken cancellationToken = default)
    {
        if (_lifetime is null)
        {
            return await _thumbnails.GetAsync(map.Id,
                cancel => _controller.LoadThumbnailAsync(map.Id, cancel), cancellationToken)
                .ConfigureAwait(false);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token, cancellationToken);
        return await _thumbnails.GetAsync(map.Id,
            cancel => _controller.LoadThumbnailAsync(map.Id, cancel), linked.Token)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _thumbnails.Clear();
    }
}

public sealed class HunterLicenseScreenModel : IDisposable
{
    private readonly IHunterLicenseController _controller;
    private CancellationTokenSource? _request;

    public HunterLicenseScreenModel(IHunterLicenseController controller) => _controller = controller;
    public AsyncScreenState Status { get; } = new();
    public HunterLicenseTab Tab { get; private set; }
    public HunterLicenseOverview? Overview { get; private set; }
    public IReadOnlyList<HunterLicenseStat> Stats { get; private set; } = [];
    public ImmutableArray<HunterLicenseMatch> Matches { get; private set; } = [];
    public string? NextCursor { get; private set; }
    public bool CanLoadMore => Tab == HunterLicenseTab.Matches && NextCursor is not null;

    public async Task SelectTabAsync(HunterLicenseTab tab, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource request = ResetRequest(cancellationToken);
        Tab = tab;
        Status.Loading($"Loading {tab}…");
        try
        {
            if (tab == HunterLicenseTab.Overview)
            {
                HunterLicenseOverview loaded = await _controller.LoadOverviewAsync(request.Token)
                    .ConfigureAwait(false);
                if (!ReferenceEquals(_request, request)) return;
                Overview = loaded;
            }
            else if (tab == HunterLicenseTab.Matches)
            {
                HunterLicenseMatchPage page = await _controller.LoadMatchesAsync(null,
                    request.Token).ConfigureAwait(false);
                if (!ReferenceEquals(_request, request)) return;
                Matches = page.Matches;
                NextCursor = page.NextCursor;
            }
            else
            {
                IReadOnlyList<HunterLicenseStat> loaded = await _controller.LoadStatsAsync(tab,
                    request.Token).ConfigureAwait(false);
                if (!ReferenceEquals(_request, request)) return;
                Stats = loaded;
            }
            bool empty = tab switch
            {
                HunterLicenseTab.Overview => Overview is null,
                HunterLicenseTab.Matches => Matches.IsEmpty,
                _ => Stats.Count == 0
            };
            if (empty) Status.Empty($"No {tab.ToString().ToLowerInvariant()} data is available yet.");
            else Status.Ready();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_request, request)) return;
            Status.Failed(AsyncScreenState.FriendlyFailure(error, "Hunter License"));
        }
    }

    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadMore) return;
        string cursor = NextCursor!;
        CancellationTokenSource request = ResetRequest(cancellationToken);
        try
        {
            HunterLicenseMatchPage page = await _controller.LoadMatchesAsync(cursor,
                request.Token).ConfigureAwait(false);
            if (!ReferenceEquals(_request, request)) return;
            Matches = Matches.AddRange(page.Matches);
            NextCursor = page.NextCursor;
            Status.Ready();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_request, request)) return;
            Status.Failed(AsyncScreenState.FriendlyFailure(error, "Match history"));
        }
    }

    public void Dispose()
    {
        _request?.Cancel();
        _request?.Dispose();
    }

    private CancellationTokenSource ResetRequest(CancellationToken external)
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = CancellationTokenSource.CreateLinkedTokenSource(external);
        return _request;
    }
}

public sealed class ReplayLibraryScreenModel : IDisposable
{
    private readonly IReplayLibraryController _controller;
    private readonly IReplayDeleteConfirmation _confirmation;
    private CancellationTokenSource? _request;

    public ReplayLibraryScreenModel(IReplayLibraryController controller,
        IReplayDeleteConfirmation confirmation)
    {
        _controller = controller;
        _confirmation = confirmation;
    }

    public AsyncScreenState Status { get; } = new();
    public IReadOnlyList<ReplayMetadata> Replays { get; private set; } = [];
    public ReplayMetadata? Selected { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource request = ResetRequest(cancellationToken);
        Status.Loading("Loading replays…");
        try
        {
            IReadOnlyList<ReplayMetadata> loaded = await _controller.LoadAsync(request.Token)
                .ConfigureAwait(false);
            if (!ReferenceEquals(_request, request)) return;
            Replays = loaded;
            Selected = Replays.Count > 0 ? Replays[0] : null;
            if (Replays.Count == 0) Status.Empty("No local replays found.");
            else Status.Ready();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_request, request)) return;
            Status.Failed(AsyncScreenState.FriendlyFailure(error, "The replay library"));
        }
    }

    public void Select(ReplayMetadata replay)
    {
        if (Replays.Contains(replay)) Selected = replay;
    }

    public async Task<UiActionResult> DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null) return UiActionResult.Failure("Choose a replay first.");
        ReplayMetadata replay = Selected;
        if (!await _confirmation.ConfirmDeleteAsync(replay, cancellationToken).ConfigureAwait(false))
            return UiActionResult.Failure("Delete canceled.");
        if (Selected?.Id != replay.Id)
            return UiActionResult.Failure("Selection changed; delete canceled.");
        UiActionResult result;
        try
        {
            result = await _controller.DeleteAsync(replay, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            return UiActionResult.Failure(AsyncScreenState.FriendlyFailure(error, "Replay deletion"));
        }
        if (result.Succeeded)
        {
            Replays = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(Replays,
                item => item.Id != replay.Id));
            Selected = Replays.Count > 0 ? Replays[0] : null;
            if (Replays.Count == 0) Status.Empty("No local replays found.");
        }
        return result;
    }

    public void Dispose()
    {
        _request?.Cancel();
        _request?.Dispose();
    }

    private CancellationTokenSource ResetRequest(CancellationToken external)
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = CancellationTokenSource.CreateLinkedTokenSource(external);
        return _request;
    }
}
