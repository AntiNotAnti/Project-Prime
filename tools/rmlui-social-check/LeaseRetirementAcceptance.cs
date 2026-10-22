using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;

static class LeaseRetirementAcceptance
{
    private static string Id(int value) => $"PP-AAAA-BBBB-CCCC-DDDD-{value:X4}";
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("SOCIAL LEASE FAIL: " + label);
        Console.WriteLine("SOCIAL LEASE PASS " + label);
    }
    private static NativeSocialDirectory Directory(object account, int self = 1)
    {
        var directory = new NativeSocialDirectory(Id(self), new[] {
            new NativeSocialPerson(Id(2), "Private friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(3), "Private request", NativeSocialRelation.IncomingRequest),
            new NativeSocialPerson(Id(4), "Private block", NativeSocialRelation.Blocked) });
        typeof(NativeSocialDirectory).GetProperty("AccountWitness", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(directory, account);
        return directory;
    }
    private static NativeSocialView Wait(NativeSocialFriendsController controller,
        Func<NativeSocialView, bool> ready)
    {
        for (int i = 0; i < 200; ++i)
        {
            var state = controller.Tick();
            if (ready(state)) return state;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Native Social account lease fixture did not finish.");
    }
    private static void Load(NativeSocialFriendsController controller, Service service, int self = 1)
    {
        controller.Refresh();
        service.TakeRead().Reply.SetResult(Directory(service.Account, self));
        Wait(controller, state => !state.Busy && state.Connected);
    }

    public static void Run()
    {
        var service = new Service();
        using var controller = new NativeSocialFriendsController(service);
        Load(controller, service);
        Check(controller.Select(0) && controller.Command(NativeSocialFriendAction.RemoveFriend),
            "current account can stage a cached-friend confirmation");
        service.Account = new object(); // Same Prime ID can also retire on token/session replacement.
        Check(!controller.Confirm() && service.Mutations.IsEmpty,
            "confirmation checks account lease before submitting any mutation");
        var retired = controller.Current();
        Check(!retired.Connected && !retired.Busy && retired.Rows.Length == 0
            && retired.SelfPrimeId.Length == 0 && retired.Selected == null && retired.Confirmation.Length == 0,
            "Current synchronously clears private rows, owner, selection and confirmation");
        Check(controller.SetTab(2) && controller.Current().Rows.Length == 0
            && controller.SetTab(6) && controller.Current().Rows.Length == 0
            && !controller.Select(0) && !controller.Command(NativeSocialFriendAction.UnblockPlayer),
            "retired request and blocked tabs retain no private rows or mutation admission");

        controller.SetTab(0);
        Load(controller, service);
        Check(controller.Lookup(Id(8)), "current account starts worker lookup");
        var lookup = service.TakeLookup();
        service.Account = new object();
        retired = controller.Current();
        Check(!retired.Busy && !retired.Connected && lookup.Token.IsCancellationRequested,
            "Current retires a pending lookup immediately when its cached account lease changes");
        lookup.Reply.SetResult(new(Id(8), "Late old-account lookup", NativeSocialRelation.Player));
        Load(controller, service, self: 9);
        Check(controller.Current().SelfPrimeId == Id(9) && controller.Current().Selected == null,
            "late lookup completion cannot replace a newly verified account view");

        controller.Refresh();
        var pending = service.TakeRead();
        object old = service.Account;
        service.Account = new object();
        pending.Reply.SetResult(Directory(old));
        retired = Wait(controller, state => !state.Busy);
        Check(!retired.Connected && retired.Rows.Length == 0 && retired.SelfPrimeId.Length == 0,
            "completed initial or refresh read with a retired lease never publishes private data");

        Load(controller, service);
        Check(controller.Select(0) && controller.Command(NativeSocialFriendAction.RemoveFriend)
            && controller.Confirm(), "current account starts one confirmed worker mutation");
        var mutation = service.TakeMutation();
        service.Account = new object();
        retired = controller.Tick();
        Check(!retired.Connected && !retired.Busy && mutation.Token.IsCancellationRequested,
            "Tick clears cached data and cancels pending mutation on account replacement");
        mutation.Reply.SetResult(new(true, "accepted by old request"));
        // The deliberately cancellation-ignoring service exercises the controller's
        // own post-await fence, rather than Task.WaitAsync cancellation behavior.
        for (int i = 0; i < 20; ++i) { controller.Tick(); Thread.Sleep(5); }
        Check(service.Reads.IsEmpty && !controller.Current().Connected
            && !controller.Can(NativeSocialFriendAction.BlockPlayer),
            "cancelled mutation cannot launch a readback or restore private rows after late success");

        var secret = new Secret();
        var serializable = Directory(secret);
        string json = JsonSerializer.Serialize(serializable);
        Check(!json.Contains("AccountWitness", StringComparison.Ordinal)
            && !json.Contains(secret.AccessToken, StringComparison.Ordinal)
            && !serializable.ToString().Contains(secret.AccessToken, StringComparison.Ordinal),
            "opaque lease is omitted from JSON and record display");
    }

    private sealed class Secret { public string AccessToken { get; } = "SYNTHETIC_PRIVATE_ACCOUNT_TOKEN"; }
    private sealed class Service : INativeSocialFriendsService
    {
        internal sealed record Pending<T>(CancellationToken Token, TaskCompletionSource<T> Reply);
        public object Account = new();
        public readonly ConcurrentQueue<Pending<NativeSocialDirectory>> Reads = new();
        public readonly ConcurrentQueue<Pending<NativeSocialPerson?>> Lookups = new();
        public readonly ConcurrentQueue<Pending<NativeSocialMutation>> Mutations = new();
        public bool IsCurrent(NativeSocialDirectory directory)
            => ReferenceEquals(typeof(NativeSocialDirectory).GetProperty("AccountWitness",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(directory), Account);
        private static Task<T> Enqueue<T>(ConcurrentQueue<Pending<T>> queue, CancellationToken token)
        {
            var pending = new Pending<T>(token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            queue.Enqueue(pending);
            return pending.Reply.Task;
        }
        private static Pending<T> Take<T>(ConcurrentQueue<Pending<T>> queue)
        {
            for (int i = 0; i < 200; ++i)
            {
                if (queue.TryDequeue(out var pending)) return pending;
                Thread.Sleep(5);
            }
            throw new TimeoutException("Native Social lease fixture worker did not start.");
        }
        public Pending<NativeSocialDirectory> TakeRead() => Take(Reads);
        public Pending<NativeSocialPerson?> TakeLookup() => Take(Lookups);
        public Pending<NativeSocialMutation> TakeMutation() => Take(Mutations);
        public Task<NativeSocialDirectory> ReadAsync(CancellationToken token) => Enqueue(Reads, token);
        public Task<NativeSocialPerson?> LookupAsync(string id, CancellationToken token) => Enqueue(Lookups, token);
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string id, CancellationToken token)
            => Enqueue(Mutations, token);
    }
}
