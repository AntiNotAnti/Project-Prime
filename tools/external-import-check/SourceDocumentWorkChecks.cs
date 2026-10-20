using System.Buffers.Binary;
using System.Text;
using MphRead.Mods.Import;

// Authored transport bytes and fake providers only. These checks exercise the
// real picker/coordinator and bundle publisher, never a source disc or Android.
internal static class SourceDocumentWorkChecks
{
    private static int _checks;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    internal static void Run()
    {
        _checks = 0;
        int ownerThread = Environment.CurrentManagedThreadId;
        string root = Path.Combine(Path.GetTempPath(), "prime-source-work-" + Guid.NewGuid().ToString("N"));
        try
        {
            RunAsync(root, ownerThread).GetAwaiter().GetResult();
            Check(!SourceCharacterImportWork.IsBusy, "all source worker slots retired");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Check(!Directory.Exists(root), "all authored temporary files removed");
        Console.WriteLine($"Source document work: {_checks} checks passed (synthetic transport/picker/coordinator only).");
    }

    private static async Task RunAsync(string root, int ownerThread)
    {
        await PickerCases();
        RegistrationCases(root);
        await CapturedInputsAndCommit(root, ownerThread);
        await CancelledOwnersAndLateOpen(root);
        await BlockedProviderRetainsSlot(root);
        await PrecommitCancellationPreservesBundle(root);
        await ExclusiveCases();
    }

    private static async Task PickerCases()
    {
        int callbacks = 0, opens = 0;
        var document = new OwnedSourceDocument(_ => { opens++; return Task.FromResult<Stream>(new TrackedInput(Iso())); });
        using (var session = new SourceDocumentPickerSession())
        {
            int before = SourceDocumentPickerSession.HighWater;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Expect<OperationCanceledException>(() => session.Begin(cancelled.Token), "pre-cancelled picker is refused");
            Check(!session.HasPending && SourceDocumentPickerSession.HighWater == before, "pre-cancelled picker allocates no request");
            using var cancel = new CancellationTokenSource();
            var pending = session.Begin(cancel.Token); cancel.Cancel();
            Check(!session.TryLaunch(pending.Code, () => callbacks++), "cancel before launch never launches chooser");
            Check(callbacks == 0 && !session.HasPending, "unlaunched cancelled request retires without callback");
            await ExpectAsync<OperationCanceledException>(pending.Task, "unlaunched task is cancelled");
            Check(!session.TryComplete(pending.Code, () => { callbacks++; return document; }), "late unlaunched completion is ignored");
        }
        using (var session = new SourceDocumentPickerSession())
        {
            using var cancel = new CancellationTokenSource();
            var a = session.Begin(cancel.Token);
            Check(session.TryLaunch(a.Code, () => callbacks++), "first actual chooser launches");
            cancel.Cancel();
            await ExpectAsync<OperationCanceledException>(a.Task, "launched task cancels promptly");
            Check(session.HasPending, "launched cancelled chooser remains a tombstone");
            int beforeBlocked = SourceDocumentPickerSession.HighWater;
            Expect<InvalidOperationException>(() => session.Begin(CancellationToken.None), "B cannot replace undrained A");
            Check(SourceDocumentPickerSession.HighWater == beforeBlocked, "blocked B consumes no request code");
            Check(!session.TryComplete(a.Code + 1, () => { callbacks++; return document; }), "unrelated result cannot drain A");
            int beforeDrain = callbacks;
            Check(session.TryComplete(a.Code, () => { callbacks++; return document; }), "late A result drains its own tombstone");
            Check(callbacks == beforeDrain && !session.HasPending && opens == 0, "cancelled result never creates or opens a document");
            var b = session.Begin(CancellationToken.None);
            Check(b.Code > a.Code && session.TryLaunch(b.Code, () => callbacks++), "B gets a fresh unique code after A drains");
            int beforeStale = callbacks;
            Check(!session.TryComplete(a.Code, () => { callbacks++; return document; }), "stale A cannot satisfy B");
            Check(callbacks == beforeStale && !b.Task.IsCompleted, "B remains pending after stale A");
            Check(session.TryComplete(b.Code, () => { callbacks++; return document; }), "current result creates its opaque selection");
            Check(ReferenceEquals(await Wait(b.Task), document) && opens == 0, "selection captures factory without opening provider on callback thread");
            Check(!session.TryComplete(b.Code, () => { callbacks++; return document; }), "duplicate result is ignored");
        }
        using (var session = new SourceDocumentPickerSession())
        {
            var failed = session.Begin(CancellationToken.None);
            Check(!session.TryLaunch(failed.Code, () => throw new IOException("authored launch failure")), "launch failure clears pending chooser");
            await ExpectAsync<IOException>(failed.Task, "launch failure reaches its original task");
            Check(!session.HasPending, "failed launch leaves no tombstone");
        }
        var disposed = new SourceDocumentPickerSession();
        var closing = disposed.Begin(CancellationToken.None); disposed.Dispose(); disposed.Dispose();
        await ExpectAsync<OperationCanceledException>(closing.Task, "session disposal cancels pending chooser");
        Check(!disposed.HasPending && !disposed.TryLaunch(closing.Code, () => callbacks++), "disposed session cannot launch");
        Expect<ObjectDisposedException>(() => disposed.Begin(CancellationToken.None), "disposed session cannot reopen");
        int high = SourceDocumentPickerSession.HighWater;
        SourceDocumentPickerSession.RestoreHighWater(high - 1);
        Check(SourceDocumentPickerSession.HighWater == high, "restoration never rewinds request identity");
        SourceDocumentPickerSession.RestoreHighWater(high + 2);
        Check(SourceDocumentPickerSession.HighWater == high + 2, "restoration reserves previously issued identity range");
        using var first = new SourceDocumentPickerSession(); using var second = new SourceDocumentPickerSession();
        var one = first.Begin(CancellationToken.None); var two = second.Begin(CancellationToken.None);
        Check(one.Code > high + 2 && two.Code > one.Code, "request identities remain unique across activity sessions");
        Check(first.TryComplete(one.Code, () => null) && second.TryComplete(two.Code, () => null), "independent sessions drain their own results");
        Check(await Wait(one.Task) == null && await Wait(two.Task) == null, "chooser cancellation remains null selection");
    }

    private static void RegistrationCases(string root)
    {
        using var old = SourceDocumentPlatform.Install(_ => Task.FromResult<OwnedSourceDocument?>(null), Path.Combine(root, "old-scratch"));
        var captured = SourceDocumentPlatform.Capture();
        Check(ReferenceEquals(captured, old) && !captured.Lifetime.IsCancellationRequested, "capture owns current activity lifetime");
        using var current = SourceDocumentPlatform.Install(_ => Task.FromResult<OwnedSourceDocument?>(null), Path.Combine(root, "new-scratch"));
        Check(captured.Lifetime.IsCancellationRequested, "replacement cancels old captured activity lifetime");
        old.Dispose();
        Check(ReferenceEquals(SourceDocumentPlatform.Capture(), current) && !current.Lifetime.IsCancellationRequested,
            "late old activity disposal cannot clear newer registration");
        current.Dispose();
        Check(current.Lifetime.IsCancellationRequested, "current registration cancellation survives its CTS disposal");
        Expect<InvalidOperationException>(() => SourceDocumentPlatform.Capture(), "disposed current registration is unavailable");
    }

    private static async Task CapturedInputsAndCommit(string root, int ownerThread)
    {
        string scratch = Path.Combine(root, "captured-scratch"), destination = Path.Combine(root, "captured-bundles");
        var opened = Signal(); var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new TrackedInput(Iso()); string? snapshot = null; int executions = 0;
        using var registration = SourceDocumentPlatform.Install(_ => Task.FromResult<OwnedSourceDocument?>(
            new(_ => { opened.TrySetResult(); return release.Task; })), scratch);
        string chosenRoot = destination;
        var ids = new List<string> { "darksamus-echoes" };
        SourceCharacterImportWork.Operation? operation = null;
        try
        {
            operation = SourceCharacterImportWork.Start(chosenRoot, ids, CancellationToken.None, (path, target, token, progress, selected) =>
            {
                executions++;
                Check(Environment.CurrentManagedThreadId != ownerThread, "executor never runs on the blocked host UI owner thread");
                Check(target == Path.GetFullPath(destination) && selected.SequenceEqual(new[] { "darksamus-echoes" }), "destination and selected IDs are detached before awaiting provider");
                Check(Path.GetFileName(path) == "source.iso" && path.StartsWith(Path.GetFullPath(scratch) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "provider becomes a canonical private source path");
                snapshot = path;
                using var input = File.OpenRead(path);
                Check(input.CanSeek && input.Length == Iso().Length && File.ReadAllBytes(path).SequenceEqual(Iso()), "nonseekable short-read provider becomes an exact ordinary seekable snapshot");
                LocalImportBundle.Publish(target, Spec(), Parts("committed"), token, progress);
                // Existing atomic commit wins over a later UI cancellation.
                operation!.Cancel();
                return selected.ToArray();
            });
            await Wait(opened.Task);
            chosenRoot = Path.Combine(root, "changed-root"); ids[0] = "trooper-power"; ids.Add("trooper-ice");
            Check(SourceCharacterImportWork.IsBusy, "job owns slot while provider open is pending");
            release.TrySetResult(source);
            var result = await Wait(operation.Completion);
            Check(result.Success && !result.Cancelled && result.Published == 1 && executions == 1, "returned committed bundle is not relabelled cancellation");
            Check(LocalImportBundle.Inspect(destination, Spec().Id, Spec()).State == ImportBundleState.Ready, "fake executor publishes a real authored receipt");
            Check(!Directory.Exists(chosenRoot) && source.Disposals == 1 && snapshot != null && !File.Exists(snapshot), "mutable caller root is unused and provider/snapshot retire exactly once");
            ScratchEmpty(scratch); Check(!SourceCharacterImportWork.IsBusy, "success releases worker slot");
        }
        finally { release.TrySetResult(source); if (operation != null) { operation.Cancel(); await Drain(operation.Completion); } }
    }

    private static async Task CancelledOwnersAndLateOpen(string root)
    {
        int picks = 0, executions = 0;
        string scratch = Path.Combine(root, "cancelled-scratch");
        using (var registration = SourceDocumentPlatform.Install(_ => { picks++; return Task.FromResult<OwnedSourceDocument?>(null); }, scratch))
        using (var owner = new CancellationTokenSource())
        {
            owner.Cancel();
            var operation = SourceCharacterImportWork.Start(Path.Combine(root, "cancelled-bundles"), new[] { "darksamus-echoes" }, owner.Token,
                (_, _, _, _, _) => { executions++; return Array.Empty<string>(); });
            var result = await Wait(operation.Completion);
            Check(result.Cancelled && !result.Success && picks == 0 && executions == 0 && !SourceCharacterImportWork.IsBusy,
                "cancelled owner never invokes picker/provider/importer or strands busy gate");
        }
        var entered = Signal(); var late = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new TrackedInput(Iso()); SourceCharacterImportWork.Operation? job = null;
        using var platform = SourceDocumentPlatform.Install(_ => Task.FromResult<OwnedSourceDocument?>(new(_ => { entered.TrySetResult(); return late.Task; })), scratch);
        try
        {
            job = SourceCharacterImportWork.Start(Path.Combine(root, "late-bundles"), new[] { "darksamus-echoes" }, CancellationToken.None,
                (_, _, _, _, _) => { executions++; return Array.Empty<string>(); });
            await Wait(entered.Task); job.Cancel();
            Check(SourceCharacterImportWork.IsBusy && !job.Completion.IsCompleted, "cancelled late-open provider retains the single worker slot");
            Expect<InvalidOperationException>(() => SourceCharacterImportWork.Start(Path.Combine(root, "wrong-bundles"), new[] { "trooper-power" }, CancellationToken.None), "replacement job cannot race blocked provider");
            late.TrySetResult(stream);
            var result = await Wait(job.Completion);
            Check(result.Cancelled && !result.Success && executions == 0 && stream.Disposals == 1, "late opened unclaimed stream is disposed once without import");
            ScratchEmpty(scratch); Check(!SourceCharacterImportWork.IsBusy, "late-open retirement releases slot");
        }
        finally { late.TrySetResult(stream); if (job != null) { job.Cancel(); await Drain(job.Completion); } }
    }

    private static async Task BlockedProviderRetainsSlot(string root)
    {
        string scratch = Path.Combine(root, "blocked-scratch"); var stream = new BlockedInput();
        using var registration = SourceDocumentPlatform.Install(_ => Task.FromResult<OwnedSourceDocument?>(new(_ => Task.FromResult<Stream>(stream))), scratch);
        SourceCharacterImportWork.Operation? job = null; int executions = 0;
        try
        {
            job = SourceCharacterImportWork.Start(Path.Combine(root, "blocked-bundles"), new[] { "darksamus-echoes" }, CancellationToken.None,
                (_, _, _, _, _) => { executions++; return Array.Empty<string>(); });
            await Wait(stream.ReadEntered.Task); job.Cancel();
            await Wait(stream.DisposeEntered.Task);
            Check(SourceCharacterImportWork.IsBusy && !job.Completion.IsCompleted, "ignored read cancellation plus blocked disposal cannot release ownership early");
            Expect<InvalidOperationException>(() => SourceCharacterImportWork.RunExclusive(() => 1, CancellationToken.None), "Check/Remove exclusive work cannot overlap retired provider");
            stream.ReadRelease.TrySetResult();
            Check(SourceCharacterImportWork.IsBusy && !job.Completion.IsCompleted, "read release alone cannot bypass still-running provider disposal");
            stream.DisposeRelease.TrySetResult();
            var result = await Wait(job.Completion);
            Check(result.Cancelled && !result.Success && executions == 0 && stream.Disposals == 1, "provider retirement finally completes cancellation exactly once");
            ScratchEmpty(scratch); Check(!SourceCharacterImportWork.IsBusy, "blocked provider cleanup releases worker slot");
        }
        finally
        {
            stream.ReadRelease.TrySetResult(); stream.DisposeRelease.TrySetResult();
            if (job != null) { job.Cancel(); await Drain(job.Completion); }
        }
    }

    private static async Task PrecommitCancellationPreservesBundle(string root)
    {
        string destination = Path.Combine(root, "old-good-bundle"), scratch = Path.Combine(root, "precommit-scratch");
        LocalImportBundle.Publish(destination, Spec(), Parts("old-good"));
        string pointer = Path.Combine(destination, LocalImportBundle.StorageDirectory, Spec().Id, "current.json");
        byte[] before = File.ReadAllBytes(pointer); var stream = new TrackedInput(Iso());
        var entered = Signal(); var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = SourceDocumentPlatform.Install(_ => Task.FromResult<OwnedSourceDocument?>(new(_ => { entered.TrySetResult(); return release.Task; })), scratch);
        SourceCharacterImportWork.Operation? operation = null;
        try
        {
            operation = SourceCharacterImportWork.Start(destination, new[] { "darksamus-echoes" }, CancellationToken.None, (path, target, token, progress, ids) =>
            {
                LocalImportBundle.Publish(target, Spec(), Parts("replacement"), token, new Progress(stage =>
                { progress.Report(stage); if (stage == ImportPublishStage.BeforeCommit) operation!.Cancel(); }));
                return ids;
            });
            await Wait(entered.Task); release.TrySetResult(stream);
            var result = await Wait(operation.Completion);
            Check(result.Cancelled && !result.Success && result.Published == 0, "actual BeforeCommit cancellation reaches bundle publisher");
            Check(File.ReadAllBytes(pointer).SequenceEqual(before) && LocalImportBundle.Inspect(destination, Spec().Id, Spec()).State == ImportBundleState.Ready,
                "coordinator cancellation preserves the byte-identical old good receipt");
            Check(stream.Disposals == 1, "precommit cancellation disposes source once"); ScratchEmpty(scratch);
        }
        finally { release.TrySetResult(stream); if (operation != null) { operation.Cancel(); await Drain(operation.Completion); } }
    }

    private static async Task ExclusiveCases()
    {
        var entered = Signal(); var release = Signal();
        Task<int> task = SourceCharacterImportWork.RunExclusive(() => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); return 23; }, CancellationToken.None);
        try
        {
            await Wait(entered.Task); Check(SourceCharacterImportWork.IsBusy, "exclusive worker owns shared slot");
            Expect<InvalidOperationException>(() => SourceCharacterImportWork.RunExclusive(() => 0, CancellationToken.None), "second exclusive worker refused");
        }
        finally { release.TrySetResult(); }
        Check(await Wait(task) == 23 && !SourceCharacterImportWork.IsBusy, "successful exclusive work releases slot");
        await ExpectAsync<IOException>(SourceCharacterImportWork.RunExclusive<int>(() => throw new IOException("authored failure"), CancellationToken.None), "exclusive failure propagates");
        Check(!SourceCharacterImportWork.IsBusy, "throwing exclusive work releases slot");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); int calls = 0;
        await ExpectAsync<OperationCanceledException>(SourceCharacterImportWork.RunExclusive(() => ++calls, cancellation.Token), "cancelled exclusive work is skipped");
        Check(calls == 0 && !SourceCharacterImportWork.IsBusy, "cancelled exclusive work releases slot without callback");
    }

    private sealed class Progress(Action<ImportPublishStage> report) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage value) => report(value); }
    private sealed class TrackedInput(byte[] bytes) : Stream
    {
        private readonly MemoryStream _input = new(bytes, writable: false);
        internal int Disposals;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, Math.Min(count, 7));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _input.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], token);
        protected override void Dispose(bool disposing) { if (disposing) { Interlocked.Increment(ref Disposals); _input.Dispose(); } base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class BlockedInput : Stream
    {
        internal readonly TaskCompletionSource ReadEntered = Signal(), ReadRelease = Signal(), DisposeEntered = Signal(), DisposeRelease = Signal();
        internal int Disposals;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { ReadEntered.TrySetResult(); await ReadRelease.Task.ConfigureAwait(false); return 0; }
        public override async ValueTask DisposeAsync()
        { Interlocked.Increment(ref Disposals); DisposeEntered.TrySetResult(); await DisposeRelease.Task.ConfigureAwait(false); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static byte[] Iso()
    {
        byte[] bytes = Enumerable.Range(0, 257).Select(i => (byte)(i % 251)).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x1c), 0xc2339f3d); return bytes;
    }
    private static ImportBundleSpec Spec() => new("darksamus-echoes", 1, new string('a', 64), new string('b', 40), new[] { "fixture.json" });
    private static Dictionary<string, byte[]> Parts(string value) => new() { ["fixture.json"] = Encoding.UTF8.GetBytes(value) };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task<T> Wait<T>(Task<T> task) => await task.WaitAsync(Deadline);
    private static async Task Wait(Task task) => await task.WaitAsync(Deadline);
    private static async Task Drain(Task task) { try { await Wait(task); } catch { } }
    private static async Task ExpectAsync<T>(Task task, string why) where T : Exception
    { try { await Wait(task); } catch (T) { Check(true, why); return; } throw new InvalidDataException("Source document work check failed: " + why); }
    private static void Expect<T>(Action action, string why) where T : Exception
    { try { action(); } catch (T) { Check(true, why); return; } throw new InvalidDataException("Source document work check failed: " + why); }
    private static void ScratchEmpty(string scratch) => Check(!Directory.Exists(scratch) || !Directory.EnumerateFileSystemEntries(scratch).Any(), "source snapshot leaves no derived scratch payload");
    private static void Check(bool condition, string why)
    { if (!condition) throw new InvalidDataException("Source document work check failed: " + why); Interlocked.Increment(ref _checks); }
}
