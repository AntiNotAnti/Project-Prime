using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

static void Check(bool pass, string label)
{
    if (!pass) throw new InvalidOperationException("RML SOCIAL FAIL: " + label);
    Console.WriteLine("RML SOCIAL PASS " + label);
}
static NativeSocialView Wait(NativeSocialFriendsController c,
    Func<NativeSocialView, bool> accepted)
{
    for (int i = 0; i < 200; i++)
    {
        NativeSocialView state = c.Tick();
        if (accepted(state)) return state;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Social fixture failed to complete in a bounded interval.");
}
static string Id(int i) => $"PP-AAAA-BBBB-CCCC-DDDD-{i:X4}";
static NativeSocialDirectory Directory(int count, bool extra = true)
{
    NativeSocialPerson[] friends = Enumerable.Range(1, count)
        .Select(i => new NativeSocialPerson(Id(i), $"Friend {i:00}", NativeSocialRelation.Friend))
        .ToArray();
    if (!extra) return new(Id(999), friends);
    return new(Id(999), friends.Concat(new[]
    {
        new NativeSocialPerson(Id(300), "Incoming", NativeSocialRelation.IncomingRequest),
        new NativeSocialPerson(Id(301), "Outgoing", NativeSocialRelation.OutgoingRequest),
        new NativeSocialPerson(Id(302), "Blocked", NativeSocialRelation.Blocked),
        new NativeSocialPerson(Id(999), "Fake self relation", NativeSocialRelation.Friend),
        new NativeSocialPerson("arbitrary/path", "Forged", NativeSocialRelation.Friend)
    }).ToArray());
}

Check(NativeSocialFriendsController.ValidPrimeId(Id(100))
    && NativeSocialFriendsController.ValidPrimeId(Id(100).ToLowerInvariant())
    && !NativeSocialFriendsController.ValidPrimeId("not a prime id")
    && !NativeSocialFriendsController.ValidPrimeId("PP-0000-0000-0000-0000-000Z"),
    "exact Prime ID syntax rejects arbitrary targets and malformed hex");

var service = new Stub();
using var controller = new NativeSocialFriendsController(service);
var firstPending = service.ReadReply;
controller.Refresh();
Wait(controller, s => service.ReadCalls == 1 && s.Busy);
service.ReadReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
controller.Refresh();
Wait(controller, s => service.ReadCalls == 2 && s.Busy);
firstPending.TrySetResult(Directory(5, extra: false));
service.ReadReply.TrySetResult(Directory(11));
NativeSocialView first = Wait(controller, s => !s.Busy && s.Connected);
Check(first.SelfPrimeId == Id(999) && first.Total == 11
    && first.Rows.Length == 8 && first.Pages == 2
    && first.Rows[0].DisplayName == "Friend 01",
    "owner adopts latest authenticated 11-player snapshot with 8-row pagination");
Check(controller.Page(1) && controller.Tick().Rows.Length == 3
    && !controller.Page(1), "page bounds and three-row tail are preserved");
Check(controller.Search("friend 05") && controller.Tick().Total == 1
    && controller.Tick().Rows[0].PrimeId == Id(5)
    && !controller.Search(new string('X',129)), "search is bounded and case insensitive");
Check(controller.Search("") && controller.SetTab(2)
    && controller.Tick().Total == 2 && controller.Select(0)
    && controller.Tick().Selected?.Relation == NativeSocialRelation.IncomingRequest
    && controller.Can(NativeSocialFriendAction.AcceptRequest),
    "incoming/outgoing request tabs preserve server-authored relation permissions");
Check(!controller.Can(NativeSocialFriendAction.RemoveFriend)
    && !controller.Command(NativeSocialFriendAction.RemoveFriend),
    "a request target cannot be removed as an existing friend");
Check(controller.SetTab(0) && controller.Select(0)
    && controller.Can(NativeSocialFriendAction.RemoveFriend)
    && controller.Command(NativeSocialFriendAction.RemoveFriend)
    && controller.Tick().Confirmation.Contains("REMOVE", StringComparison.Ordinal)
    && service.MutationCalls == 0,
    "destructive friend removal remains local until explicit confirmation");
Check(controller.Dismiss() && service.MutationCalls == 0
    && controller.Tick().Confirmation == "",
    "dismiss leaves authoritative friendship unchanged");
Check(controller.Command(NativeSocialFriendAction.RemoveFriend)
    && controller.Confirm(), "confirmed current target passes stable epoch check");
Wait(controller, s => service.MutationCalls == 1 && s.Busy);
Check(service.LastAction == NativeSocialFriendAction.RemoveFriend
    && service.LastTarget == Id(1)
    && !controller.Command(NativeSocialFriendAction.RemoveFriend)
    && !controller.Confirm(),
    "one exact account ID is submitted once while worker is pending");
service.ReadReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
service.MutateReply.SetResult(new(true, "OK"));
// Mutation worker now waits for a fresh authenticated read.
Wait(controller, _ => service.ReadCalls == 3);
service.ReadReply.SetResult(new NativeSocialDirectory(Id(999),
    Directory(11).People.Where(p => p.PrimeId != Id(1)).ToArray()));
NativeSocialView updated = Wait(controller, s => !s.Busy && s.Connected && s.Total == 10);
Check(updated.Confirmation == "" && service.MutationCalls == 1,
    "server reread, not optimistic mutation, defines visible friendship state");

Check(controller.SetTab(1) && !controller.Lookup("../../../private")
    && service.LookupCalls == 0, "lookup cannot emit unvalidated remote account IDs");
service.LookupReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Lookup(Id(450)), "valid Prime ID is eligible for authenticated lookup");
Wait(controller, _ => service.LookupCalls == 1);
service.LookupReply.SetResult(null);
Check(Wait(controller, s => !s.Busy).Status == "ACCOUNT ID NOT FOUND",
    "empty server lookup does not leave a spinner or invent a player");
service.LookupReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Lookup(Id(450)), "second valid lookup starts");
Wait(controller, _ => service.LookupCalls == 2);
service.LookupReply.SetResult(new(Id(450), "Opponent", NativeSocialRelation.Player));
NativeSocialView found = Wait(controller, s => !s.Busy && s.Selected?.PrimeId == Id(450));
Check(found.Selected!.DisplayName == "Opponent"
    && controller.Can(NativeSocialFriendAction.SendRequest),
    "only a verified looked-up account can receive a friend request");
service.MutateReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Command(NativeSocialFriendAction.SendRequest),
    "non-destructive friend request starts one authorized mutation");
Wait(controller, _ => service.MutationCalls == 2);
controller.Cancel();
service.MutateReply.SetResult(new(true, "OK"));
Thread.Sleep(25);
Check(!controller.Tick().Busy && controller.Tick().Status == "SOCIAL REQUEST CANCELLED"
    && service.ReadCalls == 3,
    "cancelled mutation completion cannot update a retired UI owner");

service.ReadReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
controller.Refresh();
Wait(controller, _ => service.ReadCalls == 4);
service.ReadReply.SetException(new InvalidOperationException("sensitive private service body"));
NativeSocialView offline = Wait(controller, s => !s.Busy && !s.Connected);
Check(offline.Rows.Length == 0 && offline.SelfPrimeId.Length == 0
    && !offline.Status.Contains("sensitive", StringComparison.OrdinalIgnoreCase)
    && !controller.Can(NativeSocialFriendAction.SendRequest),
    "authentication failure clears private cache and redacts unsafe network errors");

var wrong = new Stub();
using(var isolated = new NativeSocialFriendsController(wrong))
{
    Exception? failure = null;
    Task.Run(() =>
    {
        try { isolated.SetTab(2); }
        catch (Exception e) { failure = e; }
    }).GetAwaiter().GetResult();
    Check(failure is InvalidOperationException,
        "owner-thread contract rejects cross-thread writes");
}
AccountWitnessAcceptance.Run();
LeaseRetirementAcceptance.Run();
InvitationAcceptance.Run();
RecentPlayersAcceptance.Run();
Console.WriteLine("RML-33 SOCIAL ACCEPTANCE COMPLETE");

sealed class Stub : INativeSocialFriendsService
{
    public TaskCompletionSource<NativeSocialDirectory> ReadReply
        = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<NativeSocialPerson?> LookupReply
        = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<NativeSocialMutation> MutateReply
        = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ReadCalls, MutationCalls, LookupCalls;
    public string LastTarget = "";
    public NativeSocialFriendAction LastAction;

    public Task<NativeSocialDirectory> ReadAsync(CancellationToken cancel)
    {
        Interlocked.Increment(ref ReadCalls);
        TaskCompletionSource<NativeSocialDirectory> reply = ReadReply;
        return reply.Task.WaitAsync(cancel);
    }
    public Task<NativeSocialPerson?> LookupAsync(string id, CancellationToken cancel)
    {
        Interlocked.Increment(ref LookupCalls);
        return LookupReply.Task.WaitAsync(cancel);
    }
    public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action,
        string id, CancellationToken cancel)
    {
        LastTarget = id; LastAction = action;
        Interlocked.Increment(ref MutationCalls);
        return MutateReply.Task.WaitAsync(cancel);
    }
}
