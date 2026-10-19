using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;

internal static class TransfersAcceptance
{
    public static void Run()
    {
        BoundsAndValidation();
        SelectionAndExactControls();
        ConfirmationsAndRestrictedStates();
        PollingAndLifetimes();
        ReceiptProjection();
        ActualQueueNeedsReviewGuard().GetAwaiter().GetResult();
        Console.WriteLine("RML-82 COMMUNITY TRANSFERS ACCEPTANCE COMPLETE");
    }

    private static void BoundsAndValidation()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityTransfersController(authority, new ManualTime());
        controller.Open();
        Check(controller.Tick().Busy, "history opens without blocking on its authority");
        Call read = authority.Take();
        read.Complete(Enumerable.Range(1, 40).Select(i => Row(i)).ToArray());
        var view = Settled(controller);
        Check(view.Total == 32 && view.Rows.Count == 8 && view.Pages == 4 && view.Page == 0,
            "history retains at most 32 receipts and presents eight per page");
        Check(!controller.MovePage(-1) && !controller.MovePage(2) && !controller.Select(8)
            && !controller.Select(-1), "history rejects out-of-range pages and visible slots");
        for (int page = 1; page < 4; page++)
        {
            Check(controller.MovePage(1), "next history page is available");
            view = controller.Tick();
            Check(view.Page == page && view.Rows.Count == 8 && view.Selected?.Id == view.Rows[0].Id,
                "changing a page selects its first actual receipt");
        }
        Check(!controller.MovePage(1), "fourth retained page is the final page");
        Check(authority.Controls == 0, "opening, reading, selecting and paging never save or control queue jobs");

        Check(controller.Refresh(), "validation refresh starts");
        read = authority.Take();
        var valid = Row(100) with { Title = "A\t\n\0B" + new string('x', 120), PackageHash = new string('A', 64) };
        read.Complete(new[]
        {
            Row(1) with { Id = Guid.Empty },
            Row(2) with { Kind = (CommunityTransferKind)99 },
            Row(3) with { State = (CommunityTransferState)99 },
            Row(4) with { PackageHash = new string('z', 64) },
            Row(5) with { PackageHash = new string('a', 63) },
            Row(6) with { Total = 0 },
            Row(7) with { Total = -1 },
            Row(8) with { Received = -1 },
            Row(9) with { Received = 101 },
            Row(10) with { State = CommunityTransferState.Completed, Received = 99 },
            Row(11) with { Title = null! },
            Row(12) with { QueueWitness = null },
            null!, valid, valid with { Title = "DUPLICATE SHOULD NOT APPEAR" }
        });
        view = Settled(controller);
        Check(view.Total == 1 && view.Rows.Count == 1 && view.Page == 0,
            "malformed identifiers, enums, hashes, byte counts, completion claims and missing witnesses are discarded; GUIDs deduplicate");
        Check(view.Selected?.Title == "AB" + new string('x', 94)
            && view.Selected.PackageHash == new string('a', 64)
            && ReferenceEquals(view.Selected.QueueWitness, valid.QueueWitness),
            "titles remove controls and truncate to 96 characters while hashes normalize and opaque witnesses survive");
        Check(authority.Controls == 0, "validation and sanitization never mutate the durable queue");
    }

    private static void SelectionAndExactControls()
    {
        var authority = new Authority();
        var time = new ManualTime();
        using var controller = new NativeCommunityTransfersController(authority, time);
        var original = Enumerable.Range(1, 10).Select(i => Row(i)).ToArray();
        Open(controller, authority, original);
        Check(controller.Select(3), "a visible transfer may be selected");
        Guid selectedId = controller.Tick().Rows[3].Id;
        time.Advance(TimeSpan.FromSeconds(1));
        controller.Tick();
        Call poll = authority.Take();
        long pollRevision = controller.Tick().Revision;
        var reordered = original.Select(r => r with
        {
            UpdatedAt = r.Id == selectedId ? time.GetUtcNow().AddHours(1) : r.UpdatedAt
        }).ToArray();
        poll.Complete(reordered);
        var view = Settled(controller, pollRevision);
        Check(view.Selected?.Id == selectedId && view.Rows[0].Id == selectedId,
            "poll reorder preserves actual job GUID selection instead of the previous row index");
        time.Advance(TimeSpan.FromSeconds(1));
        controller.Tick();
        poll = authority.Take();
        pollRevision = controller.Tick().Revision;
        poll.Complete(original.Select(r => r.Id == selectedId
            ? r with { UpdatedAt = DateTimeOffset.UnixEpoch.AddSeconds(-1) } : r).ToArray());
        view = Settled(controller, pollRevision);
        Check(view.Selected?.Id == view.Rows[0].Id && view.Selected?.Id != selectedId,
            "when reorder moves a selected GUID off the visible page, actions target the first visible receipt");
        selectedId = view.Selected!.Id;
        NativeCommunityTransferReceipt expected = view.Selected!;
        Check(controller.Control(NativeCommunityTransferAction.Pause), "running transfer pause starts immediately");
        Call pause = authority.Take();
        Check(pause.Action == NativeCommunityTransferAction.Pause && pause.Expected?.Id == selectedId
            && pause.Expected.PackageHash == expected.PackageHash
            && ReferenceEquals(pause.Expected.QueueWitness, expected.QueueWitness),
            "pause carries the captured exact GUID, hash and opaque queue witness");
        Check(!controller.Select(1) && !controller.MovePage(1)
            && !controller.Control(NativeCommunityTransferAction.Cancel),
            "an active control excludes selection changes and duplicate controls");
        var paused = expected with { State = CommunityTransferState.Paused, QueueWitness = new object() };
        pause.Complete(new[] { paused });
        view = Settled(controller);
        Check(view.Selected?.State == CommunityTransferState.Paused
            && controller.Control(NativeCommunityTransferAction.Resume), "resume uses the fresh paused receipt");
        Call resume = authority.Take();
        Check(resume.Action == NativeCommunityTransferAction.Resume && resume.Expected?.Id == paused.Id
            && ReferenceEquals(resume.Expected.QueueWitness, paused.QueueWitness),
            "resume carries the post-pause witness rather than the original running receipt");
        resume.Complete(new[] { paused with { State = CommunityTransferState.Downloading } });
        Settled(controller);

        Exception? crossThread = null;
        Task.Run(() =>
        {
            try { controller.Tick(); }
            catch (Exception error) { crossThread = error; }
        }).GetAwaiter().GetResult();
        Check(crossThread is InvalidOperationException, "only the UI owner can apply transfer completions");
    }

    private static void ConfirmationsAndRestrictedStates()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityTransfersController(authority, new ManualTime());
        NativeCommunityTransferReceipt downloading = Row(1);
        Open(controller, authority, new[] { downloading });
        Check(controller.Control(NativeCommunityTransferAction.Cancel)
            && controller.Tick().Confirmation == NativeCommunityTransferAction.Cancel && authority.Controls == 0,
            "cancel only proposes a destructive change until explicit confirmation");
        Check(!controller.Select(0) && !controller.Refresh() && !controller.MovePage(1)
            && !controller.Control(NativeCommunityTransferAction.Pause),
            "confirmation pins its receipt and excludes competing navigation and controls");
        Check(controller.Dismiss() && authority.Controls == 0, "dismissed cancellation never reaches the queue");
        Check(controller.Control(NativeCommunityTransferAction.Cancel) && controller.Confirm(),
            "explicit confirmation submits cancellation");
        Call cancel = authority.Take();
        Check(cancel.Action == NativeCommunityTransferAction.Cancel && cancel.Expected?.Id == downloading.Id
            && ReferenceEquals(cancel.Expected.QueueWitness, downloading.QueueWitness),
            "cancel confirmation submits the originally captured receipt witness");
        NativeCommunityTransferReceipt cancelled = downloading with { State = CommunityTransferState.Cancelled, QueueWitness = new object() };
        cancel.Complete(new[] { cancelled });
        Settled(controller);
        Check(!controller.Control(NativeCommunityTransferAction.Resume), "cancelled jobs cannot resume");
        int controls = authority.Controls;
        Check(controller.Control(NativeCommunityTransferAction.Forget)
            && controller.Tick().Confirmation == NativeCommunityTransferAction.Forget && authority.Controls == controls,
            "forget requires a separate explicit confirmation");
        Check(controller.Dismiss() && authority.Controls == controls, "dismissed forget preserves the receipt");
        Check(controller.Control(NativeCommunityTransferAction.Forget) && controller.Confirm(), "confirmed forget starts");
        Call forget = authority.Take();
        Check(forget.Action == NativeCommunityTransferAction.Forget && forget.Expected?.Id == cancelled.Id
            && ReferenceEquals(forget.Expected.QueueWitness, cancelled.QueueWitness),
            "forget targets the captured cancelled receipt");
        forget.Complete(Array.Empty<NativeCommunityTransferReceipt>());
        Check(Settled(controller).Total == 0, "successful forget displays the authority's empty journal snapshot");

        foreach (CommunityTransferState state in new[] { CommunityTransferState.NeedsReview, CommunityTransferState.NeedsAuthorization })
        {
            Check(controller.Refresh(), "restricted-state read starts");
            authority.Take().Complete(new[] { Row(20) with { State = state, Kind = CommunityTransferKind.Upload } });
            Settled(controller);
            controls = authority.Controls;
            Check(!controller.Control(NativeCommunityTransferAction.Resume) && authority.Controls == controls,
                state + " cannot bypass upload review or authorization by resuming");
        }
        Check(!controller.Control((NativeCommunityTransferAction)99), "undefined control actions are rejected");
    }

    private static void PollingAndLifetimes()
    {
        int owner = Environment.CurrentManagedThreadId;
        var authority = new Authority();
        var time = new ManualTime();
        using var controller = new NativeCommunityTransfersController(authority, time);
        Open(controller, authority, new[] { Row(1) });
        Check(authority.Threads.All(thread => thread != owner), "history reads execute on workers rather than the UI owner");
        for (int i = 0; i < 20; i++) controller.Tick();
        Check(authority.Reads == 1, "polling waits for its one-second deadline");
        time.Advance(TimeSpan.FromSeconds(1));
        controller.Tick();
        Call oldPoll = authority.Take();
        time.Advance(TimeSpan.FromSeconds(20));
        for (int i = 0; i < 20; i++) controller.Tick();
        Check(authority.Reads == 2 && authority.Peak == 1,
            "a delayed poll never starts overlapping timer reads even after multiple deadlines");
        Check(!controller.Tick().Busy, "background observation leaves receipt actions available");
        controller.Close();
        Check(oldPoll.Token.IsCancellationRequested && authority.Controls == 0 && controller.Tick().Total == 0,
            "close cancels only observation and never issues queue pause, cancel or forget");
        controller.Open();
        Call newRead = authority.Take();
        newRead.Complete(new[] { Row(2) with { Title = "CURRENT OWNER" } });
        var current = Settled(controller);
        oldPoll.Complete(new[] { Row(3) with { Title = "STALE POLL" } });
        Drain(authority, controller);
        Check(controller.Tick().Selected?.Title == "CURRENT OWNER" && controller.Tick().Revision == current.Revision,
            "a cancellation-ignoring poll from the closed lifetime cannot repaint the reopened page");

        Check(controller.Control(NativeCommunityTransferAction.Pause), "lifetime control starts");
        Call oldControl = authority.Take();
        Check(oldControl.Thread != owner, "queue controls also execute on workers rather than the UI owner");
        int controls = authority.Controls;
        controller.Close();
        Check(oldControl.Token.IsCancellationRequested && authority.Controls == controls,
            "closing an in-flight control cancels its observer without issuing another queue mutation");
        controller.Open();
        authority.Take().Complete(new[] { Row(4) with { Title = "REOPENED OWNER" } });
        current = Settled(controller);
        oldControl.Complete(new[] { Row(2) with { Title = "STALE CONTROL", State = CommunityTransferState.Paused } });
        Drain(authority, controller);
        Check(controller.Tick().Selected?.Title == "REOPENED OWNER" && controller.Tick().Revision == current.Revision,
            "a late control completion cannot apply its old receipt to a reopened owner lifetime");

        Check(controller.Refresh(), "redaction read starts");
        authority.Take().Fail(new InvalidOperationException("secret ticket /private/uploads/source.ppmap"));
        var failed = Settled(controller);
        Check(failed.Status.Contains("COULD NOT BE VERIFIED", StringComparison.Ordinal)
            && !failed.Status.Contains("secret", StringComparison.Ordinal)
            && !failed.Status.Contains("/private", StringComparison.Ordinal),
            "authority failures produce safe fixed status text rather than raw filesystem or credential details");
    }

    private static NativeCommunityTransferReceipt Row(int number)
        => new(new Guid(number, 0, 0, new byte[8]), CommunityTransferKind.Download,
            CommunityTransferState.Downloading, new string('a', 64), "TRANSFER " + number,
            25, 100, DateTimeOffset.UnixEpoch.AddSeconds(number)) { QueueWitness = new object() };

    private static async Task ActualQueueNeedsReviewGuard()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rml82-review-owner-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root, "uploads"));
        try
        {
            Guid id = Guid.NewGuid(), account = Guid.NewGuid();
            string snapshot = Path.Combine(root, "uploads", id.ToString("N") + ".ppmap");
            byte[] immutableBytes = { 0x42 };
            File.WriteAllBytes(snapshot, immutableBytes);
            string hash = Convert.ToHexString(SHA256.HashData(immutableBytes)).ToLowerInvariant();
            var receipt = new CommunityTransferJob(id, CommunityTransferKind.Upload,
                CommunityTransferState.NeedsReview, "https://unused-review-fixture.invalid/", hash,
                immutableBytes.Length, 0, DateTimeOffset.UnixEpoch, SnapshotPath: snapshot,
                Publish: new CommunityPublishRequest(true, new string('b', 64), "Review required"),
                Error: "A newer immutable revision requires explicit review.");
            string index = Path.Combine(root, "jobs-v1.json");
            File.WriteAllText(index, JsonSerializer.Serialize(new
            {
                Schema = 2,
                Jobs = new[] { new { Receipt = receipt, AccountVersion = 1, AccountSubject = account, Attempt = 7L } }
            }, MapPackageReader.JsonOptions));
            byte[] journalBefore = File.ReadAllBytes(index);

            // This is a real manager over a private canonical schema-2 journal.
            // No shared session, credential source, HTTP client or StartPending is
            // used. The one-byte upload need not be an archive: neither operation
            // is permitted to reach preparation, upload or network admission.
            await using var queue = new CommunityTransferManager(root, credentials: null);
            Check(queue.GetJob(id).State == CommunityTransferState.NeedsReview,
                "actual queue loads a NeedsReview upload with its persisted account binding");
            queue.Pause(id);
            CommunityTransferJob afterPause = queue.GetJob(id);
            Check(afterPause.State == CommunityTransferState.NeedsReview
                && afterPause.PackageHash == hash && afterPause.TotalBytes == immutableBytes.Length
                && afterPause.SnapshotPath == snapshot && afterPause.UpdatedAt == receipt.UpdatedAt,
                "actual queue Pause preserves NeedsReview instead of laundering a conflict into Paused");
            bool rejected = false;
            try { queue.Resume(id); }
            catch (InvalidOperationException error) when (error.Message.Contains("revision conflict", StringComparison.OrdinalIgnoreCase))
            { rejected = true; }
            Check(rejected && queue.GetJob(id).State == CommunityTransferState.NeedsReview,
                "actual queue Resume still rejects the review-required owner state after Pause");
            Check(File.ReadAllBytes(snapshot).SequenceEqual(immutableBytes)
                && File.ReadAllBytes(index).SequenceEqual(journalBefore),
                "actual queue review guard retains immutable bytes and its unchanged durable receipt without HTTP or reauthorization");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void ReceiptProjection()
    {
        const string endpoint = "https://private-transfer-endpoint.invalid/secret";
        const string snapshot = "/private/uploads/secret-source.ppmap";
        var job = new CommunityTransferJob(Guid.NewGuid(), CommunityTransferKind.Upload,
            CommunityTransferState.NeedsAuthorization, endpoint, new string('A', 64), 100, 25,
            DateTimeOffset.UnixEpoch, SnapshotPath: snapshot, Error: "private authorization payload");
        var receipt = Row(1) with { QueueWitness = job };
        string json = JsonSerializer.Serialize(receipt);
        Check(!json.Contains("QueueWitness", StringComparison.Ordinal) && !json.Contains(endpoint, StringComparison.Ordinal)
            && !json.Contains(snapshot, StringComparison.Ordinal) && !json.Contains(job.Error!, StringComparison.Ordinal),
            "display receipt serialization omits its opaque witness, endpoint, private source path and queue error");

        // Reflection keeps this CPU acceptance executable usable against a build
        // that omits the desktop-only adapter, without initializing a real journal.
        Type? adapter = typeof(NativeCommunityTransfersController).Assembly.GetType(
            "MphRead.Mods.Launcher.RmlUi.Pages.Community.CurrentCommunityTransfersService");
        if (adapter == null)
        {
            Console.WriteLine("RML TRANSFERS NOTE desktop queue projection is excluded by this build configuration");
            return;
        }
        MethodInfo projection = adapter.GetMethod("ToReceipt", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Desktop transfer projection contract is unavailable.");
        receipt = (NativeCommunityTransferReceipt)projection.Invoke(null, new object[] { job })!;
        Check(receipt.Id == job.Id && receipt.Kind == job.Kind && receipt.State == job.State
            && receipt.PackageHash == new string('a', 64) && receipt.Received == job.AcknowledgedBytes
            && receipt.Total == job.TotalBytes && receipt.UpdatedAt == job.UpdatedAt
            && ReferenceEquals(receipt.QueueWitness, job),
            "production queue projection preserves exact identity, byte progress, state and original receipt witness");
        Check(receipt.Title == "Creator upload" && !receipt.Title.Contains(snapshot, StringComparison.Ordinal),
            "upload history uses a safe fallback title rather than its private archive path");
        var map = new CommunityMap(new string('b', 64), Guid.NewGuid(), new string('c', 64),
            "runtime_room", "Published Arena", "Creator", "1", 100);
        var download = job with { Kind = CommunityTransferKind.Download, Download = map };
        receipt = (NativeCommunityTransferReceipt)projection.Invoke(null, new object[] { download })!;
        Check(receipt.Title == "Published Arena" && ReferenceEquals(receipt.QueueWitness, download),
            "download history presents published metadata while retaining its exact queue witness");
        receipt = (NativeCommunityTransferReceipt)projection.Invoke(null,
            new object[] { download with { Download = map with { DisplayName = null } } })!;
        Check(receipt.Title == "runtime_room", "download history falls back to the runtime name when no display title exists");
    }

    private static void Open(NativeCommunityTransfersController controller, Authority authority,
        NativeCommunityTransferReceipt[] rows)
    {
        controller.Open(); authority.Take().Complete(rows); Settled(controller);
    }

    private static NativeCommunityTransfersView Settled(NativeCommunityTransfersController controller, long afterRevision = -1)
    {
        NativeCommunityTransfersView? view = null;
        Wait(() => { view = controller.Tick(); return !view.Busy && view.Revision > afterRevision; },
            "transfer owner did not apply completion");
        return view!;
    }

    private static void Drain(Authority authority, NativeCommunityTransfersController controller)
    {
        Wait(() => authority.InFlight == 0, "held authority operation did not finish");
        // The authority completion precedes publication by one continuation. A worker
        // fence gives that continuation a chance to attempt stale publication.
        Task.Run(() => { }).GetAwaiter().GetResult();
        for (int i = 0; i < 30; i++) { controller.Tick(); Thread.Sleep(1); }
    }

    private static void Wait(Func<bool> condition, string failure)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5))) throw new TimeoutException(failure);
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("RML TRANSFERS FAIL " + label);
        Console.WriteLine("RML TRANSFERS PASS " + label);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(1);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class Call
    {
        internal readonly NativeCommunityTransferReceipt? Expected;
        internal readonly NativeCommunityTransferAction? Action;
        internal readonly CancellationToken Token;
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal readonly TaskCompletionSource<NativeCommunityTransferReceipt[]> Response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Call(CancellationToken token, NativeCommunityTransferReceipt? expected = null,
            NativeCommunityTransferAction? action = null) { Token = token; Expected = expected; Action = action; }
        internal void Complete(NativeCommunityTransferReceipt[] rows) => Response.SetResult(rows);
        internal void Fail(Exception error) => Response.SetException(error);
    }

    private sealed class Authority : INativeCommunityTransfersAuthority
    {
        private readonly ConcurrentQueue<Call> _calls = new();
        private int _reads, _controls, _inFlight, _peak;
        internal readonly ConcurrentQueue<int> Threads = new();
        internal int Reads => Volatile.Read(ref _reads);
        internal int Controls => Volatile.Read(ref _controls);
        internal int InFlight => Volatile.Read(ref _inFlight);
        internal int Peak => Volatile.Read(ref _peak);

        public Task<NativeCommunityTransferReceipt[]> ReadAsync(CancellationToken token)
        { Interlocked.Increment(ref _reads); return Execute(new(token)); }
        public Task<NativeCommunityTransferReceipt[]> ControlAsync(NativeCommunityTransferReceipt expected,
            NativeCommunityTransferAction action, CancellationToken token)
        { Interlocked.Increment(ref _controls); return Execute(new(token, expected, action)); }

        private async Task<NativeCommunityTransferReceipt[]> Execute(Call call)
        {
            Threads.Enqueue(call.Thread);
            int current = Interlocked.Increment(ref _inFlight);
            int peak;
            do { peak = _peak; if (peak >= current) break; }
            while (Interlocked.CompareExchange(ref _peak, current, peak) != peak);
            _calls.Enqueue(call);
            // Deliberately ignore cancellation: actual owner fencing must protect
            // a page even when an I/O provider completes after its token is retired.
            try { return await call.Response.Task.ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        internal Call Take()
        {
            Call? call = null;
            Wait(() => _calls.TryDequeue(out call), "transfer authority was not invoked on a worker");
            return call!;
        }
    }
}
