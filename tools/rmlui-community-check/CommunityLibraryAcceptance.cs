using System.Collections.Concurrent;
using System.Text.Json;
using MphRead.Mods.Launcher;

internal static class CommunityLibraryAcceptance
{
    public static void Run()
    {
        RoutingAndIsolation();
        WitnessesAndRetirement();
        WitnessSerialization();
        FavoriteReadback();
        FavoriteFailureAndRetirement();
        LifetimeOwnership();
        Console.WriteLine("RML COMMUNITY LIBRARY ACCEPTANCE COMPLETE");
    }

    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        Console.WriteLine("RML COMMUNITY LIBRARY PASS " + label);
    }
    private static NativeCommunityEntry Row(string name, char hash = 'a')
        => new(Guid.NewGuid(), name, "Creator", new string(hash, 64), 3, "Battle", 2, 8)
        { Version = "3", Favorited = false, FavoriteCount = 4, Installed = true };
    private static NativeCommunityDiscoverController.Snapshot Wait(NativeCommunityDiscoverController controller,
        Func<NativeCommunityDiscoverController.Snapshot, bool> ready)
    {
        for (int i = 0; i < 200; i++)
        {
            var view = controller.Tick();
            if (ready(view)) return view;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Community library fixture did not settle");
    }
    private static void Drain(NativeCommunityDiscoverController controller)
    {
        // A deliberately cancellation-ignoring authority can complete after retirement.
        for (int i = 0; i < 30; i++) { controller.Tick(); Thread.Sleep(2); }
    }
    private static NativeCommunityDiscoverController.Snapshot Read(NativeCommunityDiscoverController controller,
        Authority authority, NativeCommunityEntry row, object? witness, int total = 1, int page = 1)
    {
        authority.TakeRead().Result.SetResult(new(new[] { row }, total, page, 8) { Witness = witness });
        return Wait(controller, s => !s.Busy);
    }

    private static void RoutingAndIsolation()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityDiscoverController(authority);
        controller.Refresh();
        var first = authority.TakeRead();
        Check(first.Catalog == NativeCommunityCatalog.Discover && first.Query == ""
            && first.Sort == "updated" && first.Page == 1, "initial library request carries exact public category and paging");
        first.Result.SetResult(new(new[] { Row("Public") }, 17, 1, 8));
        Check(Wait(controller, s => !s.Busy).SelectedEntry?.Name == "Public",
            "public catalog accepts an anonymous null witness");
        Check(controller.Search("  arena creator  "), "library search accepts trimmed query");
        var search = authority.TakeRead();
        Check(search.Query == "arena creator" && search.Page == 1, "exact trimmed query reaches authority");
        search.Result.SetResult(new(new[] { Row("Found") }, 17, 1, 8));
        Wait(controller, s => !s.Busy);
        Check(controller.Sort(0), "library sort may change");
        var sort = authority.TakeRead();
        Check(sort.Query == "arena creator" && sort.Sort == "name" && sort.Page == 1,
            "sort preserves exact query and restarts page one");
        sort.Result.SetResult(new(new[] { Row("Alpha") }, 17, 1, 8));
        Wait(controller, s => !s.Busy);
        Check(controller.Page(1), "library next page is enabled by server total");
        var next = authority.TakeRead();
        Check(next.Page == 2 && next.Sort == "name" && next.Query == "arena creator",
            "pagination forwards category query sort and exact page");
        next.Result.SetResult(new(new[] { Row("Page two") }, 17, 2, 8));
        Wait(controller, s => !s.Busy);
        Check(controller.Catalog(NativeCommunityCatalog.Favorites), "favorites category is supported");
        Check(controller.Current().Rows.Count == 0 && controller.Current().SelectedEntry == null,
            "category switch immediately clears old actionable rows");
        var favorites = authority.TakeRead();
        Check(favorites.Catalog == NativeCommunityCatalog.Favorites && favorites.Page == 1
            && favorites.Query == "arena creator" && favorites.Sort == "name",
            "favorites request preserves query and sort and resets pagination");
        Check(controller.Catalog(NativeCommunityCatalog.Installed), "installed category is supported");
        var installed = authority.TakeRead();
        Check(installed.Catalog == NativeCommunityCatalog.Installed && installed.Page == 1,
            "installed reads use the library authority");
        Check(controller.Catalog(NativeCommunityCatalog.Discover), "public category can supersede pending private reads");
        var publicRead = authority.TakeRead();
        favorites.Result.SetResult(new(new[] { Row("Late favorite") }, 1, 1, 8) { Witness = authority.Witness });
        installed.Result.SetResult(new(new[] { Row("Late installed") }, 1, 1, 8) { Witness = authority.Witness });
        publicRead.Result.SetResult(new(new[] { Row("Current public") }, 1, 1, 8));
        Wait(controller, s => !s.Busy);
        Drain(controller);
        Check(controller.Current().SelectedEntry?.Name == "Current public",
            "late canceled favorites and installed reads cannot repopulate another category");
        Check(controller.Sort(2), "public popular sort is accepted");
        var popular = authority.TakeRead();
        Check(popular.Catalog == NativeCommunityCatalog.Discover && popular.Sort == "favorites"
            && popular.Page == 1 && popular.Query == "arena creator",
            "popular sort forwards favorites ordering without selecting the Favorites category");
        popular.Result.SetResult(new(new[] { Row("Popular") }, 1, 1, 8));
        Wait(controller, s => !s.Busy);
        Check(!controller.Catalog((NativeCommunityCatalog)99), "unknown library category is rejected");
    }

    private static void WitnessesAndRetirement()
    {
        foreach (var catalog in new[] { NativeCommunityCatalog.Favorites, NativeCommunityCatalog.Installed })
        {
            var authority = new Authority();
            using var controller = new NativeCommunityDiscoverController(authority);
            controller.Catalog(catalog);
            Check(Read(controller, authority, Row("Missing lease"), null).Rows.Count == 0,
                catalog + " rejects a missing private witness");
            controller.Refresh();
            Check(Read(controller, authority, Row("Old lease"), new object()).Rows.Count == 0,
                catalog + " rejects a stale private witness");
            controller.Refresh();
            var view = Read(controller, authority, Row("Verified local"), authority.Witness);
            Check(view.SelectedEntry != null && ReferenceEquals(view.Witness, authority.Witness)
                && view.Catalog == catalog, catalog + " exposes only the verified category witness");
            authority.Witness = new object();
            view = controller.Tick();
            Check(view.Rows.Count == 0 && view.SelectedEntry == null && view.Witness == null
                && !controller.SetFavorite(true), catalog + " account or source retirement clears actionable selection");
        }
    }

    private static void WitnessSerialization()
    {
        const string sentinel = "PRIVATE_ACCOUNT_WITNESS_SENTINEL";
        var authority = new Authority { Witness = new { Session = sentinel } };
        using var controller = new NativeCommunityDiscoverController(authority);
        controller.Catalog(NativeCommunityCatalog.Favorites);
        var page = new NativeCommunityPage(new[] { Row("Safe published metadata") }, 1, 1, 8)
            { Witness = authority.Witness };
        authority.TakeRead().Result.SetResult(page);
        var snapshot = Wait(controller, s => !s.Busy);
        Check(ReferenceEquals(snapshot.Witness, authority.Witness),
            "serialization fixture contains a live opaque witness before projection");
        foreach (string json in new[] { JsonSerializer.Serialize(page), JsonSerializer.Serialize(snapshot) })
        {
            using var document = JsonDocument.Parse(json);
            Check(!document.RootElement.TryGetProperty("Witness", out _)
                && !json.Contains(sentinel, StringComparison.Ordinal)
                && json.Contains("Safe published metadata", StringComparison.Ordinal),
                "serialized Community page or snapshot excludes opaque witness property and session content");
        }
    }

    private static void FavoriteReadback()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityDiscoverController(authority);
        controller.Catalog(NativeCommunityCatalog.Favorites);
        var selected = Row("Exact favorite", 'b');
        Read(controller, authority, selected, authority.Witness);
        Check(controller.SetFavorite(true) && !controller.SetFavorite(false),
            "only one pending favorite mutation is accepted");
        var mutation = authority.TakeMutation();
        Check(mutation.Selected.MapId == selected.MapId && mutation.Selected.LatestHash == selected.LatestHash
            && mutation.Favorite && ReferenceEquals(mutation.ExpectedWitness, authority.Witness),
            "favorite mutation captures selected map exact hash desired state and expected witness");
        Check(controller.Current().SelectedEntry?.Favorited == false
            && controller.Current().SelectedEntry?.FavoriteCount == 4, "favorite does not optimistically change flags or counts");
        mutation.Result.SetResult(authority.Witness);
        Wait(controller, s => authority.Reads.Count > 0 && s.Busy);
        Check(controller.Current().SelectedEntry?.Favorited == false,
            "mutation completion starts authoritative readback before updating visible state");
        var refreshed = Read(controller, authority, selected with { Favorited = true, FavoriteCount = 5 }, authority.Witness);
        Check(refreshed.SelectedEntry?.Favorited == true && refreshed.SelectedEntry?.FavoriteCount == 5,
            "fresh authoritative read alone changes favorite state and count");
        Check(controller.SetFavorite(false), "favorite removal is accepted");
        var remove = authority.TakeMutation();
        Check(!remove.Favorite && remove.Selected.MapId == selected.MapId, "removal forwards false for the captured map");
        controller.Catalog(NativeCommunityCatalog.Installed);
        Read(controller, authority, Row("Installed only"), authority.Witness);
        Check(!controller.Sort(2) && authority.Reads.IsEmpty,
            "Installed rejects popular sort without scheduling a catalog read");
        remove.Result.SetResult(authority.Witness);
        Drain(controller);
        Check(!controller.SetFavorite(true) && authority.Mutations.IsEmpty
            && controller.Current().Catalog == NativeCommunityCatalog.Installed,
            "installed category rejects favorites and ignores retired mutation completion");
    }

    private static void FavoriteFailureAndRetirement()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityDiscoverController(authority);
        controller.Catalog(NativeCommunityCatalog.Favorites);
        Read(controller, authority, Row("Private selected"), authority.Witness);
        controller.SetFavorite(true);
        authority.TakeMutation().Result.SetException(new InvalidOperationException("secret ticket /private/account"));
        var failed = Wait(controller, s => !s.Busy);
        Check(failed.Status.Contains("UNAVAILABLE") && !failed.Status.Contains("secret")
            && !failed.Status.Contains("/private") && failed.SelectedEntry?.Favorited == false,
            "failed favorite mutation is redacted and never changes favorite state");
        controller.SetFavorite(true);
        var late = authority.TakeMutation();
        var oldWitness = authority.Witness;
        authority.Witness = new object();
        Check(controller.Tick().SelectedEntry == null, "account change retires pending favorite selection");
        late.Result.SetResult(oldWitness);
        Drain(controller);
        Check(controller.Current().Rows.Count == 0 && authority.Reads.IsEmpty,
            "late mutation from retired account cannot trigger readback or repopulate rows");
        controller.Refresh();
        var read = authority.TakeRead();
        var readWitness = authority.Witness;
        authority.Witness = new object();
        read.Result.SetResult(new(new[] { Row("Retired read") }, 1, 1, 8) { Witness = readWitness });
        Check(Wait(controller, s => !s.Busy).SelectedEntry == null,
            "account change during a private read rejects its later result");
    }

    private static void LifetimeOwnership()
    {
        var authority = new Authority();
        var controller = new NativeCommunityDiscoverController(authority);
        controller.Catalog(NativeCommunityCatalog.Favorites);
        var pending = authority.TakeRead();
        Exception? crossThread = null;
        Task.Run(() => { try { controller.SetFavorite(true); } catch (Exception ex) { crossThread = ex; } }).GetAwaiter().GetResult();
        Check(crossThread is InvalidOperationException, "favorite commands enforce owner thread ownership");
        controller.Dispose();
        pending.Result.SetResult(new(new[] { Row("After disposal") }, 1, 1, 8) { Witness = authority.Witness });
        bool disposed = false;
        try { controller.Tick(); } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "disposed library controller refuses later completion projection");
        controller.Dispose();
    }

    private sealed record ReadCall(NativeCommunityCatalog Catalog, string Query, string Sort, int Page)
    {
        public TaskCompletionSource<NativeCommunityPage> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record MutationCall(NativeCommunityEntry Selected, bool Favorite, object? ExpectedWitness)
    {
        public TaskCompletionSource<object> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Authority : INativeCommunityLibraryReader
    {
        public object Witness = new();
        public ConcurrentQueue<ReadCall> Reads { get; } = new();
        public ConcurrentQueue<MutationCall> Mutations { get; } = new();
        public bool IsCurrentWitness(object? witness) => witness == null || ReferenceEquals(witness, Witness);
        public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken token)
            => throw new InvalidOperationException("Library must use category-aware authority");
        public Task<NativeCommunityPage> BrowseLibraryAsync(NativeCommunityCatalog catalog,
            string query, string sort, int page, CancellationToken token)
        {
            var call = new ReadCall(catalog, query, sort, page); Reads.Enqueue(call);
            return call.Result.Task; // Intentionally ignore cancellation to exercise epoch fences.
        }
        public Task<object> SetFavoriteAsync(NativeCommunityEntry selected, bool favorite, object? expectedWitness, CancellationToken token)
        {
            var call = new MutationCall(selected, favorite, expectedWitness); Mutations.Enqueue(call);
            return call.Result.Task;
        }
        public ReadCall TakeRead() => Take(Reads);
        public MutationCall TakeMutation() => Take(Mutations);
        private static T Take<T>(ConcurrentQueue<T> calls)
        {
            for (int i = 0; i < 200; i++)
            { if (calls.TryDequeue(out var call)) return call; Thread.Sleep(5); }
            throw new TimeoutException("Expected Community library authority call");
        }
    }
}
