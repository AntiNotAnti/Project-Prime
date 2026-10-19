using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;

static class PartyLeaseRetirementAcceptance
{
    private static string Id(int n) => $"PP-AAAA-BBBB-CCCC-DDDD-{n:X4}";
    private static NativePartySnapshot State(object account, long revision = 10)
    {
        var state = new NativePartySnapshot(Id(1), "00000001-1111-4111-8111-111111111111", Id(1), revision,
            new[] { new NativePartyMember(Id(1), "Private leader", NativePresence.Unknown),
                new NativePartyMember(Id(2), "Private member", NativePresence.Unknown) },
            Array.Empty<NativePartyInvite>(), Array.Empty<NativePartyInvite>());
        typeof(NativePartySnapshot).GetProperty("AccountWitness", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(state, account);
        return state;
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("PARTY LEASE FAIL " + label);
        Console.WriteLine("PARTY LEASE PASS " + label);
    }
    private static NativePartyView Wait(NativePartyAuthorityController controller, Func<NativePartyView, bool> ready)
    {
        for (int i = 0; i < 200; ++i)
        {
            var state = controller.Tick();
            if (ready(state)) return state;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Party account lease fixture did not finish.");
    }
    private static void Load(NativePartyAuthorityController controller, Service authority, long revision = 10)
    {
        controller.Refresh();
        authority.TakeRead().Reply.SetResult(State(authority.Account, revision));
        Wait(controller, state => !state.Busy && state.Connected);
    }
    public static void Run()
    {
        var authority = new Service();
        using var controller = new NativePartyAuthorityController(authority);
        Load(controller, authority);
        Check(controller.Can(NativePartyAction.Kick, Id(2)), "current lease admits advisory member action");
        authority.Account = new();
        Check(!controller.Command(NativePartyAction.Kick, Id(2)) && authority.Commands.Count == 0,
            "account replacement retires cached command admission before any server request");
        var retired = controller.Tick();
        Check(!retired.Connected && !retired.Busy && retired.Snapshot == null,
            "owner entry immediately clears cached private party and invitations");

        Load(controller, authority);
        Check(controller.Command(NativePartyAction.Kick, Id(2)), "verified account starts one captured command");
        var command = authority.TakeCommand();
        Check(command.Command.ExpectedSelfPrimeId == Id(1) && command.Command.ExpectedRevision == 10,
            "lease hardening preserves mandatory expected-self and server-revision fields");
        authority.Account = new();
        retired = controller.Tick();
        Check(!retired.Busy && retired.Snapshot == null && command.Token.IsCancellationRequested,
            "hidden snapshot still retains its lease so pending command retires synchronously");
        command.Reply.SetResult(true);
        for (int i = 0; i < 20; ++i) { controller.Tick(); Thread.Sleep(5); }
        Check(authority.Reads.Count == 0 && !controller.Tick().Connected,
            "cancellation-ignoring late command cannot start readback or publish membership");

        controller.Refresh();
        var pending = authority.TakeRead();
        object old = authority.Account;
        authority.Account = new();
        pending.Reply.SetResult(State(old));
        retired = Wait(controller, state => !state.Busy);
        Check(!retired.Connected && retired.Snapshot == null,
            "initial or explicit-refresh read with a retired lease never exposes private data");

        Load(controller, authority);
        controller.Command(NativePartyAction.Leave);
        authority.TakeCommand().Reply.SetResult(true);
        var readback = authority.TakeRead();
        old = authority.Account; authority.Account = new();
        controller.Tick();
        Check(readback.Token.IsCancellationRequested && !controller.Can(NativePartyAction.Invite, Id(3)),
            "account change during command readback cancels work and clears followup admission");
        readback.Reply.SetResult(State(old, 99));
        Load(controller, authority, 200);
        Check(controller.Tick().Snapshot?.ServerRevision == 200,
            "late old-account readback cannot overwrite newly verified party state");

        var secret = new Secret();
        var witnessed = State(secret);
        string json = JsonSerializer.Serialize(witnessed);
        Check(!json.Contains("AccountWitness", StringComparison.Ordinal)
            && !json.Contains(secret.AccessToken, StringComparison.Ordinal)
            && !witnessed.ToString().Contains(secret.AccessToken, StringComparison.Ordinal),
            "opaque party lease stays out of JSON and record display");
    }
    private sealed class Secret { public string AccessToken { get; } = "SYNTHETIC_PARTY_PRIVATE_TOKEN"; }
    private sealed class Service : INativePartyAuthority
    {
        public sealed record Read(CancellationToken Token, TaskCompletionSource<NativePartySnapshot> Reply);
        public sealed record Sent(NativePartyCommand Command, CancellationToken Token, TaskCompletionSource<bool> Reply);
        public object Account = new();
        public readonly BlockingCollection<Read> Reads = new();
        public readonly BlockingCollection<Sent> Commands = new();
        public bool IsCurrent(NativePartySnapshot snapshot) => ReferenceEquals(
            typeof(NativePartySnapshot).GetProperty("AccountWitness", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(snapshot), Account);
        public Task<NativePartySnapshot> ReadAsync(CancellationToken token)
        {
            var value = new Read(token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Reads.Add(value); return value.Reply.Task;
        }
        public Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken token)
        {
            var value = new Sent(command, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Commands.Add(value); return value.Reply.Task;
        }
        public Read TakeRead() => Reads.TryTake(out var value, TimeSpan.FromSeconds(3)) ? value : throw new TimeoutException("No Party lease read");
        public Sent TakeCommand() => Commands.TryTake(out var value, TimeSpan.FromSeconds(3)) ? value : throw new TimeoutException("No Party lease command");
    }
}
