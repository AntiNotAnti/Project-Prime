using System.Collections.Immutable;
using System.Reflection;
using MphRead.Mods.Launcher.Core;

int assertions = 0;
void Check(bool condition, string label)
{ if (!condition) throw new InvalidOperationException(label); assertions++; }
const string Player = "PP-1234-5678-9ABC-DEF0-1234";
SocialRow Row(string key = "friend", params SocialCommand[] commands) => new(key, SocialTab.Friends, Player,
    "Friend <unsafe markup>", "FRIEND", "IN LOBBY", "room", AllowedCommands: commands.ToImmutableArray());
var row = Row("friend", SocialCommand.RemoveFriend, SocialCommand.JoinFriend, SocialCommand.BlockPlayer, SocialCommand.InviteToParty);
var backend = new FakeSocial { Data = new() { Connected = true, Status = "READY", Rows = ImmutableArray.Create(row) } };
using (var controller = new SocialController(backend))
{
    Check(controller.Snapshot.VisibleRows.Length == 1, "uses copied actual rows");
    controller.SelectRow(0);
    Check(controller.Snapshot.AvailableCommands.Contains(SocialCommand.RemoveFriend), "selected-row capabilities published");
    var remove = controller.Intent(SocialCommand.RemoveFriend);
    Check(controller.Dispatch(remove).Success && backend.Executed.Count == 0, "destructive action waits for confirmation");
    Check(controller.Snapshot.PendingConfirmation?.Intent == remove, "confirmation retains exact witness");
    backend.Data = backend.Data with { Rows = ImmutableArray.Create(row with { DisplayName = "Replacement" }) };
    backend.Notify();
    Check(!controller.Confirm().Success && backend.Executed.Count == 0, "worker state change rejects stale confirmation before Pump");
    Check(controller.Snapshot.CommandError.Length > 0, "rejection persists in snapshot");
    controller.Pump();
    Check(controller.Snapshot.CommandError.Length > 0, "Pump cannot erase action error");
    controller.SelectRow(0);
    Check(controller.Snapshot.CommandError.Length == 0, "accepted next action clears error");
    controller.Dispatch(controller.Intent(SocialCommand.RemoveFriend));
    Check(controller.Confirm().Success, "current confirmation accepted");
    controller.Pump();
    Check(backend.Executed.Single().Command == SocialCommand.RemoveFriend, "confirmation dispatches once");
    Check(!controller.Confirm().Success && backend.Executed.Count == 1, "confirmation cannot be replayed");
    var stale = controller.Intent(SocialCommand.BlockPlayer);
    Check(!controller.Dispatch(stale with { Lifetime = Guid.NewGuid() }).Success, "foreign lifetime rejects");
    Check(!controller.Dispatch(stale with { DataRevision = stale.DataRevision - 1 }).Success, "stale revision rejects");
    Check(!controller.Dispatch(stale with { TargetKey = "gone" }).Success, "missing target rejects");
    Check(!controller.Dispatch(stale with { Command = (SocialCommand)999 }).Success, "unknown command rejects");
    backend.Data = backend.Data with { IsSessionActive = true }; controller.Pump(); controller.SelectRow(0);
    Check(!controller.Snapshot.AvailableCommands.Contains(SocialCommand.JoinFriend), "active session excludes join");
    Check(!controller.Dispatch(controller.Intent(SocialCommand.JoinFriend)).Success, "active session cannot join another lobby");
    Check(!controller.SetPrivacy(new(Presence: 3)).Success && backend.PrivacyWrites == 0, "privacy enums bounded");
    var privacy = new SocialPrivacy(2, 2, 2, true);
    Check(controller.SetPrivacy(privacy).Success && backend.Data.Privacy == privacy && backend.PrivacyWrites == 1, "real preference write boundary");
    Check(!controller.SetFilter(new string('x', 49)).Success, "search input bound");
    Check(!controller.Lookup("not-an-id").Success && backend.Lookups == 0, "malformed ID never invokes backend");
    Check(SocialController.ValidPrimeId(Player) && !SocialController.ValidPrimeId(Player.Replace("ABC", "XYZ")), "full Prime ID grammar");
    backend.Lookup = Row("lookup", SocialCommand.SendFriendRequest) with { Tab = SocialTab.Players };
    Check(controller.Lookup(Player.ToLowerInvariant()).Success, "canonical ID lookup accepted"); controller.Pump();
    Check(controller.Snapshot.SelectedRow?.Key == "lookup" && controller.Snapshot.Tab == SocialTab.Players, "lookup appears as selectable real row");
    var old = controller.Snapshot;
    backend.Data = backend.Data with { Rows = ImmutableArray<SocialRow>.Empty }; backend.Notify(); controller.Pump();
    Check(old.Data.Rows.Length == 1, "published data remains immutable after service refresh");
}
Check(backend.Disposed && backend.SubscriberCount == 0, "page retirement unsubscribes service events");

var sentBackend = new FakeSocial { Data = new() { Connected = true, Rows = ImmutableArray.Create(row) }, ExecuteCompletion = new() };
using (var controller = new SocialController(sentBackend))
{
    controller.SelectRow(0); controller.Dispatch(controller.Intent(SocialCommand.BlockPlayer)); controller.Confirm();
    Check(controller.Snapshot.Busy && !controller.Snapshot.CanCancel && !controller.Cancel().Success, "sent mutation cannot be unsent through Cancel");
    Check(!controller.Refresh().Success, "refresh cannot overlap sent mutation");
    sentBackend.ExecuteCompletion.SetResult(new(false, "blocked_by_policy")); controller.Pump();
    Check(controller.Snapshot.CommandError == "blocked by policy" && !controller.Snapshot.Busy, "real mutation failure persists explicit feedback");
    controller.Pump(); Check(controller.Snapshot.CommandError == "blocked by policy", "background Pump retains backend failure");
}
var lateBackend = new FakeSocial { Data = new() { Connected = true, Rows = ImmutableArray.Create(row) }, ExecuteCompletion = new() };
var lateController = new SocialController(lateBackend);
lateController.SelectRow(0); lateController.Dispatch(lateController.Intent(SocialCommand.JoinFriend)); lateController.Cancel();
var lateJoin = new SocialJoinRequest("127.0.0.1", 1, "server", "room");
lateBackend.ExecuteCompletion.SetResult(new(true, "verified", Join: lateJoin)); lateController.Pump();
Check(lateJoin.IsDisposed && !lateController.TryTakeJoin(out _), "cancelled late admission is released instead of leaking a reserved seat");
lateController.Dispose(); lateController.Dispose();
var retiredBackend = new FakeSocial { Data = new() { Connected = true, Rows = ImmutableArray.Create(row) }, ExecuteCompletion = new() };
var retiredController = new SocialController(retiredBackend);
retiredController.SelectRow(0); retiredController.Dispatch(retiredController.Intent(SocialCommand.JoinFriend)); retiredController.Dispose();
var retiredJoin = new SocialJoinRequest("127.0.0.1", 1, "server", "room");
retiredBackend.ExecuteCompletion.SetResult(new(true, "verified", Join: retiredJoin));
Check(retiredJoin.IsDisposed && retiredBackend.SubscriberCount == 0, "retired controller releases later admission on worker completion");
using (var request = new SocialJoinRequest("127.0.0.1", 1, "server", "room"))
{
    request.TakeAdmission(); bool duplicate = false;
    try { request.TakeAdmission(); } catch (InvalidOperationException) { duplicate = true; }
    Check(duplicate, "admission handle cannot transfer twice");
}

for (int i = 0; i < 50; i++)
{
    var fake = new FakeSocial { Data = new() { Connected = true, Rows = ImmutableArray.Create(row) } };
    using var controller = new SocialController(fake);
    controller.Refresh(); Check(controller.Snapshot.Busy && controller.Snapshot.CanCancel, "refresh cancellable");
    controller.Cancel(); fake.LoadCompletion.SetResult(fake.Data with { Status = "STALE" }); controller.Pump();
    Check(controller.Snapshot.Status != "STALE" && !controller.Snapshot.Busy, "late cancelled refresh never publishes");
}
var partyBackend = new FakeSocial { Data = new() { Connected = true, Party = new("party", Player, true, 3),
    Reservation = new("reservation", 9, true, 3, "reserved", DateTimeOffset.UtcNow.AddMinutes(1), ImmutableArray<SocialReservedMember>.Empty) } };
using (var controller = new SocialController(partyBackend))
{
    Check(controller.Snapshot.AvailableCommands.Contains(SocialCommand.DisbandParty)
        && controller.Snapshot.AvailableCommands.Contains(SocialCommand.CancelReservation), "leader owns party and reservation actions");
    var intent = controller.Intent(SocialCommand.DisbandParty); controller.Dispatch(intent);
    partyBackend.Data = partyBackend.Data with { Party = partyBackend.Data.Party! with { IsLeader = false } };
    partyBackend.Notify(); Check(!controller.Confirm().Success && partyBackend.Executed.Count == 0, "leadership change invalidates confirmation");
    controller.Pump(); Check(!controller.Snapshot.AvailableCommands.Contains(SocialCommand.CancelReservation), "follower cannot cancel leader reservation");
    var travel = new SocialTravelView("travel", 4, "rematch", "Leader", "room", "server", 9,
        DateTimeOffset.UtcNow.AddMinutes(1), false, "pending", ImmutableArray<SocialTravelMember>.Empty);
    partyBackend.Data = partyBackend.Data with { Travel = travel }; partyBackend.Notify(); controller.Pump();
    Check(controller.Snapshot.AvailableCommands.Contains(SocialCommand.FollowPartyTravel), "explicit current travel consent available");
    var follow = controller.Intent(SocialCommand.FollowPartyTravel);
    partyBackend.Data = partyBackend.Data with { Travel = travel with { Revision = 5 } }; partyBackend.Notify();
    Check(!controller.Dispatch(follow).Success, "travel revision cannot follow replaced locator");
    controller.Pump();
    partyBackend.NextResult = new(true, "verified", Join: new("127.0.0.1", 27015, "server", "room", authorityEpoch: 9));
    Check(controller.Dispatch(controller.Intent(SocialCommand.FollowPartyTravel)).Success, "verified travel invokes existing backend");
    controller.Pump(); Check(controller.TryTakeJoin(out var joined) && joined?.AuthorityEpoch == 9, "verified authority epoch survives join handoff");
    joined?.Dispose(); Check(!controller.TryTakeJoin(out _), "join ownership transfers exactly once");
    partyBackend.Data = partyBackend.Data with { Travel = travel with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) } }; partyBackend.Notify(); controller.Pump();
    Check(!controller.Snapshot.AvailableCommands.Contains(SocialCommand.FollowPartyTravel), "expired travel disabled");
}
var pagesBackend = new FakeSocial { Data = new() { Connected = true, Rows = Enumerable.Range(0, 25)
    .Select(index => row with { Key = "person" + index, DisplayName = "Name" + index }).ToImmutableArray() } };
using (var controller = new SocialController(pagesBackend))
{
    Check(controller.Snapshot.PageCount == 4 && controller.Snapshot.VisibleRows.Length == 8, "pagination preserves all rows");
    controller.ChangePage(1); controller.ChangePage(1); controller.ChangePage(1);
    Check(controller.Snapshot.VisibleRows.Length == 1 && !controller.ChangePage(1).Success, "last page and bounds");
    controller.SetFilter("Name24"); Check(controller.Snapshot.TotalRows == 1 && controller.Snapshot.PageIndex == 0, "search resets pagination");
    Check(!controller.SelectRow(1).Success, "selection index bounded to current rows");
    Exception? threadError = null; var thread = new Thread(() => { try { controller.SetTab(SocialTab.Party); } catch (Exception ex) { threadError = ex; } });
    thread.Start(); thread.Join(); Check(threadError is InvalidOperationException, "foreign thread cannot mutate owner");
}
Check(typeof(SocialViewSnapshot).GetProperties().All(property => !property.Name.Contains("Token") && !property.Name.Contains("Password")), "public snapshot exposes no credentials");
Console.WriteLine($"SOCIAL PASS {assertions} assertions; fake service boundaries only, no account/friend/message mutations.");

public sealed class FakeSocial : ISocialBackend
{
    private Action? _changed;
    public event Action? Changed { add { _changed += value; SubscriberCount++; } remove { _changed -= value; SubscriberCount--; } }
    public int SubscriberCount, PrivacyWrites, Lookups;
    public bool Disposed;
    public SocialData Data = new();
    public SocialRow? Lookup;
    public SocialBackendResult NextResult = new(true, "accepted");
    public TaskCompletionSource<SocialBackendResult>? ExecuteCompletion;
    public readonly List<SocialIntent> Executed = new();
    public TaskCompletionSource<SocialData> LoadCompletion = new();
    public SocialData Current() => Data;
    public Task<SocialData> LoadAsync(CancellationToken token) => LoadCompletion.Task;
    public Task<SocialRow?> LookupAsync(string primeId, CancellationToken token) { Lookups++; return Task.FromResult(Lookup); }
    public Task<SocialBackendResult> ExecuteAsync(SocialIntent intent, SocialRow? target, CancellationToken token)
    { Executed.Add(intent); return ExecuteCompletion?.Task ?? Task.FromResult(NextResult); }
    public void SavePrivacy(SocialPrivacy privacy) { PrivacyWrites++; Data = Data with { Privacy = privacy }; }
    public void Notify() => _changed?.Invoke();
    public void Dispose() => Disposed = true;
}
