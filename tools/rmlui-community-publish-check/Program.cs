using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks++; }
NativeCommunityPublishView Wait(NativeCommunityPublishController controller, Func<NativeCommunityPublishView, bool> predicate)
{
    var until = DateTime.UtcNow.AddSeconds(4);
    while (DateTime.UtcNow < until) { var view = controller.Tick(); if (predicate(view)) return view; Thread.Sleep(2); }
    throw new TimeoutException("Synthetic publication completion did not settle.");
}
var source = new NativeCommunityPublishSource(Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111"), "frozen_studio");
string parent = new('A', 64), package = new('B', 64);
var authority = new Authority(source, parent, package);
using (var controller = new NativeCommunityPublishController(authority))
{
    Check(!controller.Open(source with { MapId = Guid.Empty }), "empty map identity rejected");
    Check(!controller.Open(source with { RuntimeName = "" }), "empty runtime name rejected");
    Check(controller.Open(source), "frozen source accepted");
    Check(!controller.Confirm(), "cannot queue before verified parent or confirmation");
    var ready = Wait(controller, state => state.Ready);
    Check(ready.Visibility == NativeCommunityPublishVisibility.Draft && ready.Name == source.RuntimeName && ready.ParentHash == parent, "default is private draft with exact parent");
    Check(!controller.SetVisibility(-1) && !controller.SetVisibility(3), "invalid visibility rejected");
    Check(!controller.Propose(new string('x', 4001)) && !controller.Propose("a\0b"), "invalid notes rejected without preparation");
    Check(controller.SetVisibility(0) && controller.Propose("captured notes"), "published request requires proposal");
    var confirm = controller.Tick();
    Check(confirm.Confirmation.Contains(parent) && confirm.Confirmation.Contains("PUBLISHED") && confirm.Confirmation.Contains(source.RuntimeName), "confirmation captures exact source, visibility and parent");
    Check(authority.Queues == 0 && !controller.SetVisibility(2) && !controller.Propose("changed"), "proposal is immutable and performs no write");
    Check(controller.Dismiss() && controller.Propose("final captured notes"), "dismiss keeps frozen project ready for new proposal");
    authority.HoldQueue = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Check(controller.Confirm() && !controller.Confirm(), "one confirmed preparation only");
    Wait(controller, _ => authority.Queues == 1);
    Check(authority.QueuedNotes == "final captured notes" && authority.QueuedVisibility == NativeCommunityPublishVisibility.Published
        && ReferenceEquals(authority.QueuedDraft?.AccountLease, authority.Lease), "queue receives captured notes, visibility and private account witness");
    Check(!controller.Refresh() && !controller.SetVisibility(1), "busy queue cannot alter parent or visibility");
    authority.HoldQueue.SetResult(authority.Receipt(authority.QueuedDraft!, authority.QueuedVisibility));
    var queued = Wait(controller, state => state.PackageHash == package);
    Check(queued.Status.Contains("NOT YET VERIFIED") && !queued.Ready, "queue receipt is distinct from verified publication");
    var progress = Wait(controller, state => state.TransferState == "UPLOADING");
    Check(progress.Progress.Contains("100") && progress.CanPause, "bounded durable progress comes from receipt owner");
    Check(controller.ControlTransfer(true), "pause action uses existing receipt");
    Wait(controller, state => state.TransferState == "PAUSED" && state.CanResume);
    Check(authority.Controls == 1 && authority.Paused, "pause acknowledged without dropping immutable archive witness");
    Check(controller.ControlTransfer(false), "same receipt resumes through account-aware authority");
    Wait(controller, state => state.TransferState == "UPLOADING");
    Check(authority.Controls == 2 && !authority.Paused, "resume is separate from proposal or requeue");
    authority.Complete = true; authority.Time.Advance(TimeSpan.FromSeconds(1));
    var completed = Wait(controller, state => state.Status.Contains("PUBLICATION VERIFIED"));
    Check(!completed.CanPause && !completed.CanResume && authority.Queues == 1, "only complete verified receipt reports publication success");
    bool wrongThread = Task.Run(() => { try { controller.Tick(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
    Check(wrongThread, "owner thread enforced");
}
for (int corruption = 0; corruption < 8; corruption++)
{
    var fake = new Authority(source, parent, package) { CorruptReceipt = corruption };
    using var controller = new NativeCommunityPublishController(fake, fake.Time);
    controller.Open(source); Wait(controller, state => state.Ready); controller.Propose("notes"); controller.Confirm();
    var rejected = Wait(controller, state => state.Status.Contains("INVALID COMMUNITY RESPONSE"));
    Check(rejected.PackageHash == "" && !rejected.Ready, "malformed exact queue receipt rejected " + corruption);
}
for (int corruption = 0; corruption < 6; corruption++)
{
    var fake = new Authority(source, parent, package) { CorruptDraft = corruption };
    using var controller = new NativeCommunityPublishController(fake, fake.Time);
    controller.Open(source);
    var rejected = Wait(controller, state => state.Status.Contains("INVALID COMMUNITY RESPONSE"));
    Check(!rejected.Ready && !controller.Propose("notes"), "malformed private preparation rejected " + corruption);
}
{
    var fake = new Authority(source, parent, package);
    using var controller = new NativeCommunityPublishController(fake, fake.Time);
    controller.Open(source); Wait(controller, state => state.Ready); controller.Propose("private notes");
    fake.CurrentLease = new Secret();
    var invalid = controller.Tick();
    Check(invalid.ParentHash == "" && invalid.Confirmation == "" && !controller.Confirm() && invalid.Status.Contains("ACCOUNT CHANGED"), "account switch retires parent and confirmation before any write");
    Check(fake.Queues == 0 && !JsonSerializer.Serialize(invalid).Contains("SECRET"), "account retirement exposes no private carrier");
}
{
    var fake = new Authority(source, parent, package) { FailQueue = true };
    using var controller = new NativeCommunityPublishController(fake, fake.Time);
    controller.Open(source); Wait(controller, state => state.Ready); controller.Propose("notes"); controller.Confirm();
    var failed = Wait(controller, state => state.Status.Contains("OUTCOME UNVERIFIED"));
    Check(!failed.Ready && !failed.Status.Contains("SYNTHETIC SECRET") && !controller.Confirm(), "failed queue removes unchecked authority and redacts exception detail");
    fake.FailQueue = false;
    Check(controller.Refresh(), "explicit refresh rereads account and parent after uncertain outcome");
    Wait(controller, state => state.Ready);
    Check(!controller.Confirm(), "refresh requires a new proposal and confirmation");
}
{
    var fake = new Authority(source, parent, package) { HoldQueue = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    using var controller = new NativeCommunityPublishController(fake, fake.Time);
    controller.Open(source); Wait(controller, state => state.Ready); controller.Propose("notes"); controller.Confirm();
    Wait(controller, _ => fake.Queues == 1); controller.CancelPreparation();
    Check(!controller.Tick().Busy && controller.Tick().PackageHash == "" && !controller.Confirm(), "cancel preparation retires UI authority without claiming durable rollback");
    fake.HoldQueue.SetResult(fake.Receipt(fake.QueuedDraft!, fake.QueuedVisibility)); Thread.Sleep(20);
    Check(controller.Tick().PackageHash == "", "late queue result cannot resurrect a cancelled route");
}
{
    var fake = new Authority(source, parent, package);
    var draft = fake.Draft(); var receipt = fake.Receipt(draft, NativeCommunityPublishVisibility.Draft);
    Check(!JsonSerializer.Serialize(draft).Contains("Account") && !JsonSerializer.Serialize(receipt).Contains("SECRET"), "draft and receipt serialization excludes opaque account and auth session");
}
Console.WriteLine($"PASS {checks} native Community publication controller checks (synthetic only; no disk, accounts or HTTP).");

sealed class Secret { public string AccessToken => "SYNTHETIC SECRET ACCESS"; public string RefreshToken => "SYNTHETIC SECRET REFRESH"; }
sealed class Clock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => _now;
    internal void Advance(TimeSpan delta) => _now += delta;
}
sealed class Authority(NativeCommunityPublishSource source, string parent, string hash) : INativeCommunityPublishAuthority
{
    internal readonly object Lease = new Secret(), Account = new Secret();
    internal object CurrentLease = null!;
    internal readonly Clock Time = new();
    internal int Queues, Controls, CorruptReceipt = -1, CorruptDraft = -1;
    internal bool Complete, Paused, FailQueue;
    internal string? QueuedNotes;
    internal NativeCommunityPublishVisibility QueuedVisibility;
    internal NativeCommunityPublishDraft? QueuedDraft;
    internal TaskCompletionSource<NativeCommunityPublishReceipt>? HoldQueue;
    private readonly Guid _job = Guid.NewGuid();
    public bool IsCurrentAccountLease(object? lease) => ReferenceEquals(lease, CurrentLease ?? Lease);
    internal NativeCommunityPublishDraft Draft() => new(source.MapId, source.RuntimeName, true, parent) { Account = Account, AccountLease = Lease };
    public Task<NativeCommunityPublishDraft> ReadAsync(NativeCommunityPublishSource captured, CancellationToken token)
    {
        var draft = Draft();
        return Task.FromResult(CorruptDraft switch
        {
            0 => draft with { MapId = Guid.NewGuid() }, 1 => draft with { RuntimeName = "other" },
            2 => draft with { ParentHash = "bad" }, 3 => draft with { Account = null },
            4 => draft with { AccountLease = null }, 5 => draft with { AccountLease = new Secret() }, _ => draft
        });
    }
    internal NativeCommunityPublishReceipt Receipt(NativeCommunityPublishDraft draft, NativeCommunityPublishVisibility visibility)
    {
        var receipt = new NativeCommunityPublishReceipt(_job, source.MapId, source.RuntimeName, hash, true, parent, visibility)
            { Account = draft.Account, AccountLease = draft.AccountLease };
        return CorruptReceipt switch
        {
            0 => receipt with { JobId = Guid.Empty }, 1 => receipt with { MapId = Guid.NewGuid() },
            2 => receipt with { RuntimeName = "other" }, 3 => receipt with { Hash = "bad" },
            4 => receipt with { ParentHash = new string('C', 64) }, 5 => receipt with { Visibility = (NativeCommunityPublishVisibility)99 },
            6 => receipt with { Account = new Secret() }, 7 => receipt with { AccountLease = new Secret() }, _ => receipt
        };
    }
    public Task<NativeCommunityPublishReceipt> QueueAsync(NativeCommunityPublishDraft draft, NativeCommunityPublishVisibility visibility, string notes, CancellationToken token)
    {
        QueuedDraft = draft; QueuedVisibility = visibility; QueuedNotes = notes; Interlocked.Increment(ref Queues);
        if (FailQueue) throw new InvalidOperationException("SYNTHETIC SECRET backend response");
        return HoldQueue?.Task ?? Task.FromResult(Receipt(draft, visibility));
    }
    public Task<NativeCommunityPublishProgress> ReadTransferAsync(NativeCommunityPublishReceipt receipt, CancellationToken token)
        => Task.FromResult(new NativeCommunityPublishProgress(_job, hash, Complete ? "COMPLETED" : Paused ? "PAUSED" : "UPLOADING",
            Complete ? 1000 : 100, 1000, Complete, !Complete && !Paused, !Complete && Paused, false));
    public Task<NativeCommunityPublishProgress> ControlTransferAsync(NativeCommunityPublishReceipt receipt, bool pause, CancellationToken token)
    { Controls++; Paused = pause; return ReadTransferAsync(receipt, token); }
}
