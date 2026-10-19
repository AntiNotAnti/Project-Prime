using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.Launcher;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; }
NativeCommunityRevisionsView Ready(NativeCommunityRevisionsController controller)
{
    var watch = Stopwatch.StartNew();
    do { var result = controller.Tick(); if (!result.Busy) return result; Thread.Sleep(2); } while (watch.Elapsed < TimeSpan.FromSeconds(4));
    throw new TimeoutException("synthetic history did not finish");
}
var fake = new Authority();
using (var controller = new NativeCommunityRevisionsController(fake))
{
    Check(!controller.Open(new(Guid.Empty, false, Authority.Hash(18))) && !controller.Open(new(Authority.Map, false, "BAD")), "invalid map/hash cannot start a read");
    Check(controller.Open(new(Authority.Map, false, Authority.Hash(18))), "public immutable history opens");
    var view = Ready(controller);
    Check(view.Connected && view.Rows.Count == 8 && view.Total == 18 && view.Selected?.Number == 18 && !view.CanPromote, "public history is bounded, sorted and cannot promote");
    Check(controller.TryGetInstallSelection(out var selected) && selected.MapId == Authority.Map && selected.LatestHash == Authority.Hash(18), "public install selection uses exact immutable package identity");
    Check(!controller.ProposePromotion() && !controller.ConfirmPromotion() && fake.Promotions.Count == 0, "public history cannot issue creator mutations");
    Check(controller.Page(1) && controller.Tick().Selected?.Number == 10 && controller.Page(1) && controller.Tick().Rows.Count == 2, "revision paging selects correct eight-row slices");
    Check(!controller.Page(1) && !controller.Page(0) && !controller.Select(8) && !controller.Select(-1), "paging and selection reject out of range choices");
    Check(controller.Select(1) && controller.TryGetInstallSelection(out selected) && selected.Revision == 1 && selected.LatestHash == Authority.Hash(1), "historical install retains selected package rather than latest");
    fake.Data.Revisions[0] = fake.Data.Revisions[0] with { Notes = "MUTATED SERVICE ARRAY" };
    Check(controller.Tick().Selected?.Notes != "MUTATED SERVICE ARRAY", "service array mutation cannot alter published snapshot");
    controller.Cancel();
    Check(!controller.Tick().Connected && controller.Tick().Rows.Count == 0 && !controller.TryGetInstallSelection(out _), "route cancellation removes all history authority");
}

fake = new Authority(owned: true);
using (var controller = new NativeCommunityRevisionsController(fake))
{
    Check(!controller.Open(new(Authority.Map, true, Authority.Hash(18))), "owned history requires displayed account lease");
    Check(controller.Open(fake.Request()), "owned history captures displayed account lease");
    var view = Ready(controller);
    Check(view.Connected && !view.CanPromote && view.Selected?.Number == 18, "current public revision cannot be promoted again");
    Check(controller.Select(1) && controller.ProposePromotion() && controller.Tick().Confirming && fake.Promotions.Count == 0, "promotion requires explicit confirmation before service mutation");
    Check(!controller.Select(2) && !controller.Page(1) && !controller.TryGetInstallSelection(out _), "confirmation locks exact revision and install choice");
    Check(controller.Dismiss() && !controller.Tick().Confirming && fake.Promotions.Count == 0, "dismissal never mutates project");
    Check(controller.ProposePromotion() && controller.ConfirmPromotion(), "confirmed promotion begins one asynchronous preflight");
    Check(!controller.ConfirmPromotion() && !controller.ProposePromotion() && !controller.Select(2), "pending promotion is single flight");
    view = Ready(controller);
    Check(view.Connected && view.CurrentHash == Authority.Hash(17) && fake.Promotions.Count == 1, "only verified readback changes displayed current public hash");
    var command = fake.Promotions.Single();
    Check(command.MapId == Authority.Map && command.Number == 17 && command.Hash == Authority.Hash(17)
        && command.CurrentHash == Authority.Hash(18) && command.LatestHash == Authority.Hash(18)
        && command.Account == Authority.Account && ReferenceEquals(command.AccountLease, fake.Lease), "confirmation captures exact account/project/current/latest/revision/hash witnesses");
    Check(controller.Tick().LatestHash == Authority.Hash(18), "promoting historical revision preserves newer immutable revision");
}

fake = new Authority(owned: true);
using (var controller = new NativeCommunityRevisionsController(fake))
{
    fake.Data = fake.Data with { Revisions = fake.Data.Revisions.Select(row => row.Number == 17 ? row with { Draft = true, Listed = false } : row).ToArray() };
    controller.Open(fake.Request()); Ready(controller); controller.Select(1);
    Check(!controller.TryGetInstallSelection(out _) && controller.Tick().CanPromote, "private draft is not anonymously installed but may be explicitly made public");
    controller.ProposePromotion();
    fake.CurrentLease = new SecretLease();
    Check(!controller.ConfirmPromotion() && !controller.Tick().Connected && controller.Tick().Rows.Count == 0 && fake.Promotions.Count == 0, "account switch clears private history and rejects old confirmation");
}

foreach (string change in new[] { "current", "latest", "account", "revision", "permission" })
{
    fake = new Authority(owned: true);
    using var controller = new NativeCommunityRevisionsController(fake);
    controller.Open(fake.Request()); Ready(controller); controller.Select(1); controller.ProposePromotion();
    fake.PreflightChange = change;
    Check(controller.ConfirmPromotion(), "confirmed action dispatches preflight once");
    Check(!Ready(controller).Connected && !controller.TryGetInstallSelection(out _) && fake.Accepted == 0,
        "changed project/account/hash/permission preflight is rejected without optimistic state");
}

var bad = new List<NativeCommunityRevisionCatalog>();
foreach (string mismatch in new[] { "current", "latest", "account", "owner", "draft", "number" })
{
    fake = new Authority(owned: true) { WrongReadback = mismatch };
    using var controller = new NativeCommunityRevisionsController(fake);
    controller.Open(fake.Request()); Ready(controller); controller.Select(1); controller.ProposePromotion(); controller.ConfirmPromotion();
    Check(!Ready(controller).Connected && !controller.TryGetInstallSelection(out _) && fake.Accepted == 1,
        "mismatched promotion response cannot publish unverified current/account/owner/package authority");
}
var valid = new Authority().Data;
bad.Add(valid with { MapId = Guid.NewGuid() }); bad.Add(valid with { Name = "" }); bad.Add(valid with { LatestHash = "BAD" });
bad.Add(valid with { CurrentHash = Authority.Hash(99) }); bad.Add(valid with { Revisions = null! });
bad.Add(valid with { CanManage = true }); bad.Add(valid with { Account = Authority.Account });
bad.Add(valid with { AccountLease = new SecretLease() });
bad.Add(valid with { Revisions = valid.Revisions.Append(valid.Revisions[0]).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { Hash = Authority.Hash(18) } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { ParentHash = "BAD" } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { Package = row.Package with { MapId = Guid.NewGuid() } } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { Package = row.Package with { LatestHash = Authority.Hash(99) } } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { Notes = new string('x', 4001) } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { Deleted = true } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Select(row => row.Number == 1 ? row with { Draft = true } : row).ToArray() });
bad.Add(valid with { Revisions = valid.Revisions.Where(row => row.Number != 18).ToArray() });
foreach (var catalog in bad)
{
    fake = new Authority { Data = catalog };
    using var controller = new NativeCommunityRevisionsController(fake);
    controller.Open(new(Authority.Map, false, Authority.Hash(18)));
    var view = Ready(controller);
    Check(!view.Connected && view.Rows.Count == 0 && !view.CanPromote && !controller.TryGetInstallSelection(out _), "malformed or unauthorized history fails closed without private rows");
    Check(!view.Status.Contains("secret", StringComparison.OrdinalIgnoreCase), "failure text never exposes service payload");
}

fake = new Authority(owned: true) { Hold = true };
using (var controller = new NativeCommunityRevisionsController(fake))
{
    controller.Open(fake.Request());
    var watch = Stopwatch.StartNew();
    while (fake.Pending.IsEmpty && watch.Elapsed < TimeSpan.FromSeconds(4)) Thread.Sleep(2);
    Check(fake.Pending.TryDequeue(out var pending), "synthetic delayed read captured");
    controller.Cancel();
    fake.Hold = false; controller.Open(fake.Request()); Ready(controller);
    pending!.TrySetResult(fake.Data with { Name = "STALE ROUTE RESULT" }); Thread.Sleep(30);
    Check(controller.Tick().Connected && controller.Tick().Name != "STALE ROUTE RESULT", "cancelled older route result cannot replace reopened history");
    fake.CurrentLease = new SecretLease();
    Check(!controller.Tick().Connected && !controller.TryGetInstallSelection(out _), "idle private rows expire immediately on lease change");
}

fake = new Authority();
using (var controller = new NativeCommunityRevisionsController(fake))
{
    Exception? caught = null;
    var worker = new Thread(() => { try { controller.Open(new(Authority.Map, false, Authority.Hash(18))); } catch (Exception error) { caught = error; } });
    worker.Start(); worker.Join();
    Check(caught is InvalidOperationException && fake.Reads == 0, "foreign thread cannot start Community work");
    controller.Dispose();
    Check(fake.Promotions.Count == 0, "disposal never creates remote work");
}

var lease = new SecretLease();
var creatorAuthority = new CreatorAuthority(lease);
using (var creator = new NativeCommunityCreatorController(creatorAuthority))
{
    creator.Refresh();
    var watch = Stopwatch.StartNew();
    while (creator.Tick().Busy && watch.Elapsed < TimeSpan.FromSeconds(4)) Thread.Sleep(2);
    Check(creator.Tick().Connected && ReferenceEquals(creator.Tick().AccountLease, lease), "owned catalog preserves actual service account lease outside bindings");
    Check(creator.Propose(NativeCreatorAction.Archive), "owned lifecycle confirmation initially belongs to captured lease");
    creatorAuthority.CurrentLease = new SecretLease();
    Check(!creator.Tick().Connected && creator.Tick().Rows.Length == 0 && creator.Tick().AccountLease == null,
        "account switch also removes cached My Uploads project rows");
    Check(!creator.Confirm() && creatorAuthority.Executions == 0, "old owned-project confirmation cannot execute after lease switch");
}
object[] serializable = {
    new NativeCreatorPage(Array.Empty<NativeCreatorEntry>(), 0, 1) { AccountLease = lease },
    new NativeCreatorView(1,false,true,"",1,0,Array.Empty<NativeCreatorEntry>(),-1,null,"") { AccountLease = lease },
    new NativeCommunityHistoryRequest(Authority.Map,true,Authority.Hash(18)) { AccountLease = lease },
    valid with { AccountLease = lease, Account = Authority.Account, OwnerId = "SYNTHETIC PRIVATE OWNER" },
    new NativeCommunityPromotion(Authority.Map,Authority.Hash(18),Authority.Hash(18),17,Authority.Hash(17))
        { AccountLease=lease, Account=Authority.Account, OwnerId="SYNTHETIC PRIVATE OWNER" }
};
foreach (object value in serializable)
{
    string json = JsonSerializer.Serialize(value, value.GetType());
    Check(!json.Contains("AccessToken") && !json.Contains("RefreshToken") && !json.Contains("SYNTHETIC SECRET")
        && !json.Contains("AccountLease") && !json.Contains("SYNTHETIC PRIVATE OWNER") && !json.Contains(Authority.Account.ToString()),
        "generic JSON cannot traverse private account lease or identity metadata");
}
LifecycleAcceptance.Run(Check);
Console.WriteLine($"PASS {checks} Community revision checks (synthetic authority only; no backend writes or installs).");

internal sealed class SecretLease { public string AccessToken { get; } = "SYNTHETIC SECRET ACCESS"; public string RefreshToken { get; } = "SYNTHETIC SECRET REFRESH"; }
internal sealed class CreatorAuthority : INativeCommunityCreatorAuthority, INativeCommunityCreatorAccountLease
{
    private readonly object _originalLease;
    internal object CurrentLease;
    internal CreatorAuthority(object lease) => _originalLease = CurrentLease = lease;
    internal int Executions;
    public bool IsCurrentAccountLease(object? lease) => ReferenceEquals(lease,CurrentLease);
    private static NativeCreatorEntry Entry => new(Authority.Map,"SYNTHETIC OWNED PROJECT",Authority.Hash(18),Authority.Hash(18),18,false,false,true);
    public Task<NativeCreatorPage> BrowseMineAsync(int page,CancellationToken token)
        => Task.FromResult(new NativeCreatorPage(new[]{Entry},1,1){AccountLease=_originalLease});
    public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid map,CancellationToken token) => Task.FromResult<NativeCreatorEntry?>(Entry);
    public Task<bool> ExecuteAsync(NativeCreatorAction action,Guid map,CancellationToken token) { Executions++; return Task.FromResult(true); }
}
internal sealed class Authority : INativeCommunityRevisionsAuthority
{
    internal static readonly Guid Map = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    internal static readonly Guid Account = Guid.Parse("bbbbbbbb-1111-4111-8111-111111111111");
    internal readonly object Lease = new SecretLease();
    internal object CurrentLease;
    internal NativeCommunityRevisionCatalog Data;
    internal readonly List<NativeCommunityPromotion> Promotions = new();
    internal readonly ConcurrentQueue<TaskCompletionSource<NativeCommunityRevisionCatalog>> Pending = new();
    internal bool Hold;
    internal int Accepted, Reads;
    internal string PreflightChange = "";
    internal string WrongReadback = "";
    internal Authority(bool owned = false)
    {
        CurrentLease = Lease;
        var rows = Enumerable.Range(1,18).Select(i => new NativeCommunityRevision(i,Hash(i),i==1?"":Hash(i-1),"SYNTHETIC NOTES "+i,
            DateTimeOffset.UnixEpoch.AddDays(i),true,false,false,new(Map,"SYNTHETIC MAP","SYNTHETIC CREATOR",Hash(i),i,"BATTLE",1,8))).ToArray();
        Data = new(Map,"SYNTHETIC PROJECT",Hash(18),Hash(18),owned,false,false,rows)
            { Account=owned?Account:Guid.Empty, AccountLease=owned?Lease:null, OwnerId=owned?"SYNTHETIC PRIVATE OWNER":"" };
    }
    internal static string Hash(int value) => value.ToString("X64");
    internal NativeCommunityHistoryRequest Request() => new(Map,true,Hash(18)) { AccountLease=Lease };
    public bool IsCurrentAccountLease(object? lease) => ReferenceEquals(lease,CurrentLease);
    public Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request, CancellationToken token)
    {
        Interlocked.Increment(ref Reads);
        if (!Hold) return Task.FromResult(Data);
        var pending = new TaskCompletionSource<NativeCommunityRevisionCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending.Enqueue(pending); return pending.Task; // Deliberately ignores cancellation to test epoch fencing.
    }
    public Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Promotions.Add(request);
        if (PreflightChange.Length != 0 || !ReferenceEquals(request.AccountLease,CurrentLease)
            || request.Account != Account || request.MapId != Map || request.CurrentHash != Data.CurrentHash
            || request.LatestHash != Data.LatestHash || !Data.CanManage)
            throw new InvalidOperationException("SYNTHETIC SECRET preflight failure");
        var selected = Data.Revisions.SingleOrDefault(row => row.Number == request.Number && row.Hash == request.Hash);
        if (selected == null || selected.Deleted) throw new InvalidOperationException("SYNTHETIC SECRET changed revision");
        Accepted++;
        Data = Data with { CurrentHash=request.Hash, Revisions=Data.Revisions.Select(row=>row.Hash==request.Hash?row with{Listed=true,Draft=false}:row).ToArray() };
        var result = WrongReadback switch
        {
            "current" => Data with { CurrentHash = Hash(18) },
            "latest" => Data with { LatestHash = Hash(16) },
            "account" => Data with { Account = Guid.NewGuid() },
            "owner" => Data with { OwnerId = "REPLACEMENT OWNER" },
            "draft" => Data with { Revisions = Data.Revisions.Select(row => row.Hash == request.Hash ? row with { Draft = true, Listed = false } : row).ToArray() },
            "number" => Data with { Revisions = Data.Revisions.Select(row => row.Hash == request.Hash ? row with { Number = 99 } : row).ToArray() },
            _ => Data
        };
        return Task.FromResult(result);
    }
}
