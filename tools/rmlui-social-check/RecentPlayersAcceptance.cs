using System.Text.Json;
using MphRead.Mods.Launcher;

internal static class RecentPlayersAcceptance
{
    private static string Id(int i) => $"PP-AAAA-BBBB-CCCC-DDDD-{i:X4}";
    internal static void Run()
    {
        var stamp = DateTimeOffset.UtcNow.AddDays(-1);
        string json = "[{\"prime_id\":\"" + Id(1).ToLowerInvariant() + "\",\"display_name\":\"Verified person\",\"last_seen\":\""
            + stamp.ToString("O") + "\",\"encounters\":3}]";
        var decoded = JsonSerializer.Deserialize<NativeSocialRecentPlayer[]>(json)!;
        var exact = NativeSocialRecentPlayers.Verify(Id(999), decoded);
        Check(exact.Single().PrimeId == Id(1) && exact[0].LastSeen == stamp && exact[0].Encounters == 3,
            "authoritative recent JSON maps exact ID, timestamp and encounter fields");
        foreach (var bad in new IReadOnlyList<NativeSocialRecentPlayer>?[]
        {
            null, Enumerable.Repeat(exact[0], 31).ToArray(), new[] { exact[0], exact[0] },
            new[] { exact[0] with { PrimeId = Id(999) } }, new[] { exact[0] with { PrimeId = "bad" } },
            new[] { exact[0] with { LastSeen = default } }, new[] { exact[0] with { LastSeen = DateTimeOffset.UtcNow.AddDays(1) } },
            new[] { exact[0] with { Encounters = 0 } }, new[] { exact[0] with { Encounters = 1000001 } },
            new[] { exact[0] with { DisplayName = "\nprivate" } }
        })
        {
            bool refused = false;
            try { NativeSocialRecentPlayers.Verify(Id(999), bad); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "malformed, absent, oversized, duplicate or self recent source is unavailable");
        }
        var rows = Enumerable.Range(1, 18).Select(i => new NativeSocialRecentPlayer(Id(i), "Recent " + i, stamp.AddMinutes(-i), i)).ToArray();
        var service = new Service { Directory = new(Id(999), new[]
        {
            new NativeSocialPerson(Id(1), "Existing friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(2), "Incoming request", NativeSocialRelation.IncomingRequest),
            new NativeSocialPerson(Id(3), "Outgoing request", NativeSocialRelation.OutgoingRequest),
            new NativeSocialPerson(Id(4), "Wrong friend duplicate", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(4), "Private blocked", NativeSocialRelation.Blocked)
        }) { RecentAvailable = true, RecentPlayers = rows } };
        using var controller = new NativeSocialFriendsController(service);
        controller.Refresh(); Wait(controller);
        Check(controller.SetTab(5) && controller.Current().RecentAvailable && controller.Current().Rows.Length == 8
            && controller.Current().Total == 17 && controller.Current().Pages == 3,
            "verified recent tab pages all people and omits blocked identities");
        Check(controller.Select(0) && controller.Current().Selected is { Relation: NativeSocialRelation.Friend, RecentEncounters: 1 }
            && !controller.Can(NativeSocialFriendAction.SendRequest) && controller.Can(NativeSocialFriendAction.RemoveFriend),
            "recent friend retains exact existing relationship and metadata");
        Check(controller.Select(1) && controller.Can(NativeSocialFriendAction.AcceptRequest)
            && !controller.Can(NativeSocialFriendAction.SendRequest), "recent incoming request uses existing request actions");
        Check(controller.Select(2) && controller.Can(NativeSocialFriendAction.CancelRequest), "recent outgoing request keeps cancel authority");
        Check(controller.Page(1) && controller.Select(0) && controller.Current().Selected?.PrimeId == Id(10)
            && controller.Page(1) && controller.Current().Rows.Single().PrimeId == Id(18)
            && !controller.Select(1), "recent pages preserve exact person identities and final slot bounds");
        Check(controller.Search(Id(18)) && controller.Current().Rows.Single().PrimeId == Id(18), "recent filter applies to actual IDs");
        controller.Search(""); controller.Page(-1); controller.Select(0);
        Check(controller.Command(NativeSocialFriendAction.BlockPlayer), "recent stranger block requires confirmation");
        service.Current = false;
        Check(!controller.Confirm() && !controller.Current().RecentAvailable && controller.Current().Rows.Length == 0
            && service.Mutations == 0, "account retirement clears recent private rows and rejects stale mutation confirmation");

        var unavailable = new Service { Directory = new(Id(999), new[] { new NativeSocialPerson(Id(1), "Friend", NativeSocialRelation.Friend) }) };
        using var missing = new NativeSocialFriendsController(unavailable);
        missing.Refresh(); Wait(missing);
        Check(!missing.SetTab(5) && !missing.Current().RecentAvailable && missing.Current().Rows.Length == 1
            && missing.Current().Status.Contains("RECENT PLAYERS UNAVAILABLE"), "optional missing source preserves friends and reports unavailable instead of empty recent history");
        var invalidService = new Service { Directory = unavailable.Directory with { RecentAvailable = true, RecentPlayers = new[] { rows[0], rows[0] } } };
        using var invalid = new NativeSocialFriendsController(invalidService);
        invalid.Refresh(); Wait(invalid);
        Check(!invalid.Current().RecentAvailable && invalid.Current().Rows.Length == 1, "bad recent response cannot discard independent valid friend directory");

        var held = new Service { Held = new(TaskCreationOptions.RunContinuationsAsynchronously), Directory = service.Directory };
        using var canceled = new NativeSocialFriendsController(held);
        canceled.Refresh(); canceled.Cancel(); held.Held.SetResult(held.Directory);
        for (int i = 0; i < 20; i++) { canceled.Tick(); Thread.Sleep(1); }
        Check(!canceled.Current().RecentAvailable && !canceled.SetTab(5), "late canceled recent completion cannot publish private people");
    }
    private static void Wait(NativeSocialFriendsController controller)
    {
        for (int i = 0; i < 300; i++) { if (!controller.Tick().Busy) return; Thread.Sleep(5); }
        throw new TimeoutException("Recent directory completion");
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("RECENT PLAYERS PASS " + label); }
    private sealed class Service : INativeSocialFriendsService
    {
        public NativeSocialDirectory Directory = new(Id(999), Array.Empty<NativeSocialPerson>());
        public TaskCompletionSource<NativeSocialDirectory>? Held;
        public bool Current = true;
        public int Mutations;
        public bool IsCurrent(NativeSocialDirectory directory) => Current;
        public Task<NativeSocialDirectory> ReadAsync(CancellationToken cancellation) => Held?.Task ?? Task.FromResult(Directory);
        public Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken cancellation) => Task.FromResult<NativeSocialPerson?>(null);
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId, CancellationToken cancellation)
        { Mutations++; return Task.FromResult(new NativeSocialMutation(true, "DONE")); }
    }
}
