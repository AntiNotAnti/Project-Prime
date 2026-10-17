using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

static void Assert(bool value, string message)
{
    if (!value) throw new InvalidOperationException("RML-34 " + message);
    Console.WriteLine("PASS " + message);
}
static string Id(int n) => $"PP-AAAA-BBBB-CCCC-DDDD-{n:X4}";
static NativePartySnapshot State(long rev = 10) => new(
    Id(1), "party-one", Id(1), rev,
    new[] { new NativePartyMember(Id(1), "Leader", NativePresence.Unknown),
            new NativePartyMember(Id(2), "Member", NativePresence.Unknown) },
    new[] { new NativePartyInvite("inv-in", "party-other", Id(4), Id(1), DateTimeOffset.UtcNow.AddMinutes(10)) },
    new[] { new NativePartyInvite("inv-out", "party-one", Id(1), Id(3), DateTimeOffset.UtcNow.AddMinutes(10)) });
static NativePartyView Await(NativePartyAuthorityController c, Func<NativePartyView,bool> match)
{
    for (int i = 0; i < 200; ++i)
    {
        var state = c.Tick();
        if (match(state)) return state;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Party fixture did not settle.");
}

var transport = new Stub();
using var controller = new NativePartyAuthorityController(transport);
controller.Refresh();
Await(controller, x => Volatile.Read(ref transport.ReadCount) == 1 && x.Busy);
var stale = transport.ReadResponse;
transport.ReadResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
controller.Refresh();
Await(controller, x => Volatile.Read(ref transport.ReadCount) == 2 && x.Busy);
stale.TrySetResult(State(1));
transport.ReadResponse.TrySetResult(State());
var loaded = Await(controller, x => !x.Busy && x.Connected);
Assert(loaded.Snapshot?.ServerRevision == 10 && loaded.Snapshot.Members.Length == 2,
    "stale generation cannot overwrite verified party");
Assert(!controller.Command(NativePartyAction.Kick, Id(1))
    && !controller.Command(NativePartyAction.Kick, Id(4))
    && !controller.Command(NativePartyAction.Invite, "../escape"),
    "actions reject self, non-members and malformed Prime IDs");
Assert(!controller.Command(NativePartyAction.AcceptInvite, inviteId: "forged"),
    "invite action rejects unknown invitation");
Assert(controller.Command(NativePartyAction.Kick, Id(2)),
    "leader may request server-verified kick");
Await(controller, x => Volatile.Read(ref transport.CommandCount) == 1 && x.Busy);
Assert(transport.Last?.ExpectedRevision == 10 && transport.Last.TargetPrimeId == Id(2)
    && transport.Last.PartyId == "party-one"
    && !controller.Command(NativePartyAction.Kick, Id(2)),
    "command is single-flight and pinned to exact server revision");
transport.ReadResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
transport.CommandResponse.TrySetResult(true);
Await(controller, x => Volatile.Read(ref transport.ReadCount) == 3);
transport.ReadResponse.TrySetResult(State(11));
Assert(Await(controller, x => !x.Busy && x.Connected).Snapshot?.ServerRevision == 11,
    "accepted mutation must reread authority rather than alter local party");
transport.ReadResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
controller.Refresh();
Await(controller, x => Volatile.Read(ref transport.ReadCount) == 4);
Assert(controller.Tick().Snapshot == null, "refresh clears private prior-account party");
transport.ReadResponse.SetException(new InvalidOperationException("secret bearer data"));
var failed = Await(controller, x => !x.Busy);
Assert(!failed.Connected && failed.Snapshot == null && !failed.Status.Contains("secret"),
    "auth failures redact transport and drop party");
Console.WriteLine("RML-34 PARTY CONTRACT ACCEPTANCE COMPLETE");

sealed class Stub : INativePartyAuthority
{
    public TaskCompletionSource<NativePartySnapshot> ReadResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> CommandResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ReadCount, CommandCount;
    public NativePartyCommand? Last;
    public Task<NativePartySnapshot> ReadAsync(CancellationToken token)
    {
        Interlocked.Increment(ref ReadCount);
        return ReadResponse.Task.WaitAsync(token);
    }
    public Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken token)
    {
        Last = command;
        Interlocked.Increment(ref CommandCount);
        return CommandResponse.Task.WaitAsync(token);
    }
}