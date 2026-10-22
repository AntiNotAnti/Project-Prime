using MphRead.Mods.Launcher;

static class AccountWitnessAcceptance
{
    private static string Id(int value) => $"PP-AAAA-BBBB-CCCC-DDDD-{value:X4}";
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("SOCIAL WITNESS FAIL: " + label);
        Console.WriteLine("SOCIAL WITNESS PASS " + label);
    }
    private static NativeSocialView Wait(NativeSocialFriendsController controller,
        Func<NativeSocialView, bool> ready)
    {
        for (int i = 0; i < 200; i++)
        {
            var state = controller.Tick();
            if (ready(state)) return state;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Native Friends witness fixture did not finish.");
    }

    public static void Run()
    {
        var service = new WitnessService();
        using var controller = new NativeSocialFriendsController(service);
        controller.Refresh();
        Wait(controller, state => !state.Busy && state.Connected);
        Check(controller.Select(0) && controller.Command(NativeSocialFriendAction.RemoveFriend)
            && controller.Confirm(), "confirmed native action captures verified self");
        Wait(controller, _ => Volatile.Read(ref service.Mutations) == 1);
        Check(service.Expected == Id(1) && service.Target == Id(2) && service.LegacyCalls == 0,
            "native controller uses exact account witness overload and captured target");
        service.Directory = new(Id(3), new[] { new NativeSocialPerson(Id(4), "Wrong account private friend", NativeSocialRelation.Friend) });
        service.Reply.SetResult(new(true, "accepted"));
        var mismatch = Wait(controller, state => !state.Busy);
        Check(!mismatch.Connected && mismatch.SelfPrimeId.Length == 0 && mismatch.Rows.Length == 0
            && !mismatch.Status.Contains("Wrong account", StringComparison.Ordinal)
            && !controller.Command(NativeSocialFriendAction.BlockPlayer),
            "different account readback cannot publish a roster or preserve mutation admission");

        service.Directory = Original();
        controller.Refresh();
        Wait(controller, state => !state.Busy && state.Connected);
        service.Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(controller.Select(0) && controller.Command(NativeSocialFriendAction.RemoveFriend)
            && controller.Confirm(), "fresh verified identity can submit another action");
        Wait(controller, _ => Volatile.Read(ref service.Mutations) == 2);
        int reads = Volatile.Read(ref service.Reads);
        service.Reply.SetResult(new(false, "account_changed private response"));
        var refused = Wait(controller, state => !state.Busy);
        Check(!refused.Connected && service.Reads == reads && !refused.Status.Contains("private", StringComparison.Ordinal),
            "server account mismatch clears stale private rows without reread or unsafe error text");

        controller.Refresh();
        Wait(controller, state => !state.Busy && state.Connected);
        service.Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(controller.Select(0) && controller.Command(NativeSocialFriendAction.RemoveFriend)
            && controller.Confirm(), "same-account success fixture starts");
        Wait(controller, _ => Volatile.Read(ref service.Mutations) == 3);
        service.Directory = new(Id(1), Array.Empty<NativeSocialPerson>());
        service.Reply.SetResult(new(true, "accepted"));
        var success = Wait(controller, state => !state.Busy);
        Check(success.Connected && success.SelfPrimeId == Id(1) && success.Rows.Length == 0,
            "only exact same-account server readback publishes successful relationship update");

        INativeSocialFriendsService legacy = new LegacyService();
        Check(legacy.ExecuteForAsync(NativeSocialFriendAction.SendRequest, Id(2), Id(1), default)
            .GetAwaiter().GetResult().Accepted, "default witness method preserves older injectable fixture compatibility");
    }

    private static NativeSocialDirectory Original() => new(Id(1), new[] {
        new NativeSocialPerson(Id(2), "Verified friend", NativeSocialRelation.Friend) });

    private sealed class WitnessService : INativeSocialFriendsService
    {
        public NativeSocialDirectory Directory = Original();
        public TaskCompletionSource<NativeSocialMutation> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, Mutations, LegacyCalls;
        public string Expected = "", Target = "";
        public Task<NativeSocialDirectory> ReadAsync(CancellationToken token)
        { Interlocked.Increment(ref Reads); return Task.FromResult(Directory); }
        public Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken token)
            => Task.FromResult<NativeSocialPerson?>(null);
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId, CancellationToken token)
        { Interlocked.Increment(ref LegacyCalls); throw new InvalidOperationException("Native action bypassed witness."); }
        public Task<NativeSocialMutation> ExecuteForAsync(NativeSocialFriendAction action, string primeId,
            string expectedSelf, CancellationToken token)
        { Expected = expectedSelf; Target = primeId; Interlocked.Increment(ref Mutations); return Reply.Task.WaitAsync(token); }
    }
    private sealed class LegacyService : INativeSocialFriendsService
    {
        public Task<NativeSocialDirectory> ReadAsync(CancellationToken token) => Task.FromResult(Original());
        public Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken token) => Task.FromResult<NativeSocialPerson?>(null);
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId, CancellationToken token)
            => Task.FromResult(new NativeSocialMutation(true, "legacy fixture"));
    }
}
