using System.Collections.Concurrent;
using System.Diagnostics;
using MphRead.Mods.Launcher;

int assertions = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException("Native Party: " + message);
    assertions++;
}
static string Id(int n) => $"PP-AAAA-BBBB-CCCC-DDDD-{n:X4}";
static string Uuid(int n) => $"{n:X8}-1111-4111-8111-111111111111";
static NativePartySnapshot State(long rev = 10) => new(
    Id(1), Uuid(1), Id(1), rev,
    new[] { new NativePartyMember(Id(1), "SYNTHETIC Leader", NativePresence.Unknown),
            new NativePartyMember(Id(2), "SYNTHETIC Member", NativePresence.InLobby) },
    new[] { new NativePartyInvite(Uuid(11), Uuid(2), Id(4), Id(1), DateTimeOffset.UtcNow.AddMinutes(10)),
            new NativePartyInvite(Uuid(12), Uuid(3), Id(5), Id(1), DateTimeOffset.UtcNow.AddMinutes(10)) },
    new[] { new NativePartyInvite(Uuid(13), Uuid(1), Id(1), Id(3), DateTimeOffset.UtcNow.AddMinutes(10)) });
static NativePartySnapshot Empty(long rev = 1) => new(Id(1), "", "", rev,
    Array.Empty<NativePartyMember>(), Array.Empty<NativePartyInvite>(), Array.Empty<NativePartyInvite>());
static NativePartyView Await(NativePartyAuthorityController controller, Func<NativePartyView, bool> match)
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(3))
    {
        NativePartyView view = controller.Tick();
        if (match(view)) return view;
        Thread.Sleep(5);
    }
    throw new TimeoutException("Party fixture did not settle.");
}
static void Load(NativePartyAuthorityController controller, Stub transport, NativePartySnapshot state)
{
    controller.Refresh();
    transport.TakeRead().Completion.SetResult(state);
    Await(controller, view => !view.Busy);
}

var transport = new Stub();
using (var controller = new NativePartyAuthorityController(transport))
{
    controller.Refresh();
    var stale = transport.TakeRead();
    controller.Refresh();
    var current = transport.TakeRead();
    Check(stale.Token.IsCancellationRequested, "superseding refresh cancels previous generation");
    stale.Completion.SetResult(State(1));
    current.Completion.SetResult(State());
    var loaded = Await(controller, view => !view.Busy && view.Connected);
    Check(loaded.Snapshot?.ServerRevision == 10 && loaded.Snapshot.Members.Length == 2,
        "late stale generation cannot overwrite verified party");
    Check(!controller.Command(NativePartyAction.Kick, Id(1))
        && !controller.Command(NativePartyAction.Kick, Id(4))
        && !controller.Command(NativePartyAction.Invite, "../escape")
        && !controller.Command((NativePartyAction)999), "self, nonmember, malformed identity and unknown actions rejected");
    Check(!controller.Command(NativePartyAction.AcceptInvite, inviteId: Uuid(99)), "unknown invitation rejected");
    Check(controller.Command(NativePartyAction.Kick, Id(2).ToLowerInvariant()), "leader can request selected member removal");
    var sent = transport.TakeCommand();
    Check(sent.Command == new NativePartyCommand(NativePartyAction.Kick, 10, Uuid(1), Id(2), "", Id(1))
        && !controller.Command(NativePartyAction.Kick, Id(2)) && controller.Tick().Snapshot == null,
        "single-flight command captures exact self, party, revision and canonical target while hiding old snapshot");
    sent.Completion.SetResult(true);
    transport.TakeRead().Completion.SetResult(State(11));
    Check(Await(controller, view => !view.Busy).Snapshot?.ServerRevision == 11,
        "accepted mutation rereads authority without optimistic local membership edits");
    controller.Refresh();
    var failed = transport.TakeRead();
    Check(controller.Tick().Snapshot == null, "refresh clears prior account private state immediately");
    failed.Completion.SetException(new InvalidOperationException("secret bearer data"));
    var unavailable = Await(controller, view => !view.Busy);
    Check(!unavailable.Connected && unavailable.Snapshot == null && !unavailable.Status.Contains("secret"),
        "transport failures redact detail and discard account state");
}

foreach (NativePartyAction operation in new[] { NativePartyAction.AcceptInvite, NativePartyAction.DeclineInvite })
{
    var authority = new Stub();
    using var controller = new NativePartyAuthorityController(authority);
    Load(controller, authority, State());
    Check(controller.Command(operation, inviteId: Uuid(12)), "selected invitation can dispatch " + operation);
    var sent = authority.TakeCommand();
    Check(sent.Command.PartyId == Uuid(3) && sent.Command.InviteId == Uuid(12)
        && sent.Command.ExpectedSelfPrimeId == Id(1) && sent.Command.ExpectedRevision == 10,
        operation + " witnesses selected incoming invitation party rather than current party");
    sent.Completion.SetResult(false);
    var rejected = Await(controller, view => !view.Busy);
    Check(!rejected.Connected && rejected.Snapshot == null && authority.Reads.Count == 0,
        "rejected mutation cannot publish local outcome or start an accepted reread");
}

foreach (NativePartyAction operation in new[] { NativePartyAction.Leave, NativePartyAction.Disband, NativePartyAction.CancelInvite })
{
    var authority = new Stub();
    using var controller = new NativePartyAuthorityController(authority);
    Load(controller, authority, State());
    Check(controller.Command(operation, inviteId: operation == NativePartyAction.CancelInvite ? Uuid(13) : ""),
        "leader can request " + operation);
    var sent = authority.TakeCommand();
    Check(sent.Command.PartyId == Uuid(1) && sent.Command.ExpectedSelfPrimeId == Id(1), operation + " retains exact current party owner witness");
    sent.Completion.SetResult(true);
    authority.TakeRead().Completion.SetResult(Empty(11));
    Check(Await(controller, view => !view.Busy).Snapshot?.PartyId == "", "authority alone confirms empty party after " + operation);
}

{
    var authority = new Stub();
    using var controller = new NativePartyAuthorityController(authority);
    Load(controller, authority, Empty());
    Check(controller.Can(NativePartyAction.Invite, Id(2)) && !controller.Can(NativePartyAction.Leave)
        && !controller.Can(NativePartyAction.Disband), "verified empty account may invite first friend but has no membership actions");
    Check(controller.Command(NativePartyAction.Invite, Id(2)), "first invite dispatches from empty party");
    var sent = authority.TakeCommand();
    Check(sent.Command.PartyId == "" && sent.Command.ExpectedRevision == 1 && sent.Command.ExpectedSelfPrimeId == Id(1),
        "first invite carries exact empty-party and expected-self witness");
    sent.Completion.SetResult(true);
    authority.TakeRead().Completion.SetResult(State(2));
    Check(Await(controller, view => !view.Busy).Snapshot?.PartyId == Uuid(1), "first invite requires fresh authoritative party response");
    Load(controller, authority, State() with { LeaderPrimeId = Id(2) });
    Check(controller.Can(NativePartyAction.Leave) && !controller.Can(NativePartyAction.Disband)
        && !controller.Can(NativePartyAction.Invite, Id(3)) && !controller.Can(NativePartyAction.Promote, Id(2)),
        "follower may leave but cannot exercise leader commands");
    Load(controller, authority, State() with { Incoming = State().Incoming.Select(invite => invite with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).ToArray() });
    Check(!controller.Can(NativePartyAction.AcceptInvite, inviteId: Uuid(11))
        && !controller.Can(NativePartyAction.DeclineInvite, inviteId: Uuid(11)), "expired incoming invitations cannot dispatch");
}

var valid = State();
var invalidStates = new (string Label, NativePartySnapshot? State)[]
{
    ("null snapshot", null), ("malformed self", valid with { SelfPrimeId = "private UUID" }),
    ("negative revision", valid with { ServerRevision = -1 }),
    ("unsafe integer revision", valid with { ServerRevision = 9007199254740992L }),
    ("malformed party UUID", valid with { PartyId = "party-one" }),
    ("null party", valid with { PartyId = null! }), ("null leader", valid with { LeaderPrimeId = null! }),
    ("missing self membership", valid with { Members = valid.Members.Skip(1).ToArray() }),
    ("missing leader membership", valid with { LeaderPrimeId = Id(3) }),
    ("empty party containing members", valid with { PartyId = "", LeaderPrimeId = "" }),
    ("empty party containing leader", Empty() with { LeaderPrimeId = Id(1) }),
    ("party without members", valid with { Members = Array.Empty<NativePartyMember>() }),
    ("null members", valid with { Members = null! }), ("null incoming", valid with { Incoming = null! }),
    ("null outgoing", valid with { Outgoing = null! }),
    ("null member row", valid with { Members = new[] { valid.Members[0], null! } }),
    ("duplicate member identity", valid with { Members = new[] { valid.Members[0], valid.Members[0] with { PrimeId = Id(1).ToLowerInvariant() } } }),
    ("invalid member name", valid with { Members = new[] { valid.Members[0] with { DisplayName = " " }, valid.Members[1] } }),
    ("oversized member name", valid with { Members = new[] { valid.Members[0] with { DisplayName = new string('x',129) }, valid.Members[1] } }),
    ("invalid presence enum", valid with { Members = new[] { valid.Members[0] with { Presence = (NativePresence)999 }, valid.Members[1] } }),
    ("over capacity", valid with { Members = Enumerable.Range(1,9).Select(i => new NativePartyMember(Id(i),"Synthetic",NativePresence.Unknown)).ToArray() }),
    ("null invite row", valid with { Incoming = new NativePartyInvite[] { null! } }),
    ("invalid invite UUID", valid with { Incoming = new[] { valid.Incoming[0] with { InviteId = "inv-in" } } }),
    ("invalid invite party UUID", valid with { Incoming = new[] { valid.Incoming[0] with { PartyId = "party-other" } } }),
    ("incoming wrong recipient", valid with { Incoming = new[] { valid.Incoming[0] with { ToPrimeId = Id(2) } } }),
    ("self invitation", valid with { Incoming = new[] { valid.Incoming[0] with { FromPrimeId = Id(1) } } }),
    ("outgoing wrong sender", valid with { Outgoing = new[] { valid.Outgoing[0] with { FromPrimeId = Id(2) } } }),
    ("outgoing wrong party", valid with { Outgoing = new[] { valid.Outgoing[0] with { PartyId = Uuid(3) } } }),
    ("outgoing without membership", Empty() with { Outgoing = valid.Outgoing }),
    ("duplicate invite UUID", valid with { Incoming = new[] { valid.Incoming[0], valid.Incoming[0] with { InviteId = valid.Incoming[0].InviteId.ToUpperInvariant() } } }),
    ("same invitation in both directions", valid with { Outgoing = new[] { valid.Outgoing[0] with { InviteId = valid.Incoming[0].InviteId } } }),
    ("oversized invitation list", valid with { Incoming = Enumerable.Repeat(valid.Incoming[0],257).ToArray() })
};
Check(NativePartyAuthorityController.ValidSnapshot(valid) && NativePartyAuthorityController.ValidSnapshot(Empty()), "actual UUID member and empty snapshots validate");
foreach (var malformed in invalidStates)
{
    Check(!NativePartyAuthorityController.ValidSnapshot(malformed.State), "validation rejects " + malformed.Label);
    if (malformed.State == null) continue;
    var authority = new Stub();
    using var controller = new NativePartyAuthorityController(authority);
    Load(controller, authority, malformed.State);
    var state = controller.Tick();
    Check(!state.Connected && state.Snapshot == null && !state.Busy,
        "malformed service response never publishes: " + malformed.Label);
}

foreach (bool duringCommand in new[] { false, true })
{
    var authority = new Stub();
    using var controller = new NativePartyAuthorityController(authority);
    TaskCompletionSource<NativePartySnapshot>? lateRead = null;
    TaskCompletionSource<bool>? lateCommand = null;
    CancellationToken cancelled;
    if (duringCommand)
    {
        Load(controller, authority, State());
        controller.Command(NativePartyAction.Leave);
        var pending = authority.TakeCommand();
        lateCommand = pending.Completion; cancelled = pending.Token;
    }
    else
    {
        controller.Refresh();
        var pending = authority.TakeRead();
        lateRead = pending.Completion; cancelled = pending.Token;
    }
    controller.Cancel();
    Check(cancelled.IsCancellationRequested && !controller.Tick().Busy && controller.Tick().Snapshot == null,
        "cancel retires " + (duringCommand ? "command" : "read") + " lifetime and private state");
    lateRead?.SetResult(State(100)); lateCommand?.SetResult(true);
    Load(controller, authority, Empty(200));
    Check(controller.Tick().Snapshot?.ServerRevision == 200, "late cancelled completion cannot overwrite later account read");
}

{
    var authority = new Stub();
    using var controller = new NativePartyAuthorityController(authority);
    Load(controller, authority, State());
    controller.Command(NativePartyAction.Leave);
    authority.TakeCommand().Completion.SetResult(true);
    var changed = Empty(11) with { SelfPrimeId = Id(9) };
    authority.TakeRead().Completion.SetResult(changed);
    Check(!Await(controller, view => !view.Busy).Connected && controller.Tick().Snapshot == null,
        "accepted command reread rejects a different authenticated account");
    Exception? ownerError = Task.Run(() => { try { controller.Tick(); return null; } catch (Exception error) { return error; } }).GetAwaiter().GetResult();
    Check(ownerError is InvalidOperationException, "foreign worker cannot mutate UI projection");
}
PartyLeaseRetirementAcceptance.Run();
PartyTravelAcceptance.Run();
Console.WriteLine($"NATIVE PARTY PASS {assertions} assertions plus lease checks; synthetic authority only; no network/account/session mutations.");

sealed class Stub : INativePartyAuthority
{
    public sealed record Read(CancellationToken Token, TaskCompletionSource<NativePartySnapshot> Completion);
    public sealed record Sent(NativePartyCommand Command, CancellationToken Token, TaskCompletionSource<bool> Completion);
    public readonly BlockingCollection<Read> Reads = new();
    public readonly BlockingCollection<Sent> Commands = new();
    public Task<NativePartySnapshot> ReadAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<NativePartySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Reads.Add(new(token, completion));
        return completion.Task; // Deliberately ignore token to exercise stale callback fencing.
    }
    public Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Commands.Add(new(command, token, completion));
        return completion.Task;
    }
    public Read TakeRead() => Reads.TryTake(out var value, TimeSpan.FromSeconds(3)) ? value : throw new TimeoutException("No fixture authority read");
    public Sent TakeCommand() => Commands.TryTake(out var value, TimeSpan.FromSeconds(3)) ? value : throw new TimeoutException("No fixture authority command");
}
