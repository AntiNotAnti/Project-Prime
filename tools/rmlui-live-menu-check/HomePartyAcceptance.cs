using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;

internal static class HomePartyAcceptance
{
    internal static void Run()
    {
        var service = new Stub { SocialOverride = new(true, true, new[] { "FRIEND IS NOT A MEMBER" }, 0) };
        using var controller = new RmlUiMenuController(service);
        Expect(() => Text("home_party_count") == "1 / 4" && Empty(),
            "friends and shared presence never infer party membership");
        string Id(int i) => "PP-0000-0000-0000-0000-" + i.ToString("X4");
        NativePartySnapshot Party(int count) => new(Id(0), "00000000-0000-0000-0000-000000000001", Id(0), 4,
            Enumerable.Range(0, count).Select(i => new NativePartyMember(Id(i), "MEMBER " + i,
                i == 1 ? NativePresence.Unknown : NativePresence.Online)).ToArray(),
            Array.Empty<NativePartyInvite>(), Array.Empty<NativePartyInvite>());
        VerifyRefreshOwnership(Party(4));
        service.HomeParty = new(true, false, "SYNTHETIC VERIFIED", 1, Party(4));
        Expect(() => Text("home_party_count") == "4 / 4" && Text("home_session_state") == "PARTY"
            && Enumerable.Range(1, 3).All(i => Text("home_party_slot" + i + "_name") == "MEMBER " + i
                && Flag("home_party_slot" + i + "_occupied")),
            "verified party uses local seat plus three distinct remote member names");
        Expect(() => Text("home_party_slot1_detail") == "PRESENCE NOT SHARED"
            && Text("player_name") == "Pilot" && Text("hunter_name") == "SAMUS",
            "private remote presence stays unknown and local appearance remains Hub-owned");
        service.HomeParty = service.HomeParty with { Snapshot = Party(6) };
        Expect(() => Text("home_party_count") == "4 / 4" && Text("home_party_capacity_hint").StartsWith("+2 MORE MEMBERS"),
            "parties larger than four retain bounded seats and explicit extra-member count");
        service.HomeParty = service.HomeParty with { Snapshot = Party(4) with { Members = Party(4).Members.Skip(1).ToArray() } };
        Expect(() => Text("home_party_count") == "1 / 4" && Empty(),
            "invalid party without self never populates Home seats");
        service.HomeParty = new(true, false, "SYNTHETIC RECONNECTED", 2, Party(4));
        Expect(() => Text("home_party_count") == "4 / 4",
            "valid replacement party restores members before disconnect");
        service.HomeParty = new(false, false, "DISCONNECTED", 3, Party(4));
        Expect(() => Text("home_party_count") == "1 / 4" && Empty(),
            "disconnected party clears remote labels even when an old snapshot remains");
        service.HomeParty = new(false, true, "VERIFYING", 4, null);
        Expect(() => Text("home_party_capacity_hint") == "VERIFYING PARTY MEMBERSHIP" && Empty(),
            "pending verification never invents party members");

        void Expect(Func<bool> predicate, string label)
        {
            Await(predicate);
            Check(predicate(), label);
        }
        string Text(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) ? value.Text : "";
        bool Flag(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;
        bool Empty() => Enumerable.Range(1, 3).All(i => !Flag("home_party_slot" + i + "_occupied")
            && Text("home_party_slot" + i + "_name") == "OPEN PARTY SLOT");
    }
    private static void VerifyRefreshOwnership(NativePartySnapshot snapshot)
    {
        var authority = new PendingAuthority();
        using var owner = new NativePartyAuthorityController(authority);
        owner.Refresh();
        authority.Pending.TrySetResult(snapshot);
        Await(() => owner.Tick().Connected);
        authority.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Refresh(preserveCurrent: true);
        Await(() => Volatile.Read(ref authority.Reads) == 2);
        var pending = owner.Tick();
        Check(pending.Busy && pending.Connected && pending.Snapshot?.Members[1].DisplayName == "MEMBER 1",
            "same-account periodic refresh preserves verified party labels while pending");
        authority.Pending.TrySetException(new InvalidOperationException("EXPLICIT SYNTHETIC AUTHORITY FAILURE"));
        Await(() => !owner.Tick().Busy);
        Check(!owner.Tick().Connected && owner.Tick().Snapshot == null,
            "failed periodic refresh clears the formerly verified party snapshot");

        authority.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Refresh();
        authority.Pending.TrySetResult(snapshot);
        Await(() => owner.Tick().Connected);
        authority.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Refresh(preserveCurrent: true);
        Await(() => Volatile.Read(ref authority.Reads) == 4);
        authority.Current = false;
        var retired = owner.Tick();
        Check(!retired.Connected && retired.Snapshot == null && !retired.Busy,
            "account witness retirement clears preserved labels during pending refresh");
        authority.Pending.TrySetResult(snapshot);
        Await(() => Volatile.Read(ref authority.CompletedReads) == 4);
        // The authority task completion cannot restore state after owner retirement.
        Check(!owner.Tick().Connected && owner.Tick().Snapshot == null,
            "retired account remains absent after stale authority completion");
    }
    private static void Await(Func<bool> predicate)
    {
        for (int i = 0; i < 200; i++)
        {
            if (predicate()) return;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Synthetic Party authority completion did not settle.");
    }
    private sealed class PendingAuthority : INativePartyAuthority
    {
        internal TaskCompletionSource<NativePartySnapshot> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Current = true;
        internal int Reads, CompletedReads;
        public bool IsCurrent(NativePartySnapshot snapshot) => Current;
        public async Task<NativePartySnapshot> ReadAsync(CancellationToken cancellation)
        {
            Task<NativePartySnapshot> pending = Pending.Task;
            Interlocked.Increment(ref Reads);
            try { return await pending.ConfigureAwait(false); }
            finally { Interlocked.Increment(ref CompletedReads); }
        }
        public Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken cancellation) => Task.FromResult(false);
    }
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        Console.WriteLine("HOME PARTY PASS " + label);
    }
}
