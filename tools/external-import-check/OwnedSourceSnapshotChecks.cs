using System.Buffers.Binary;
using MphRead.Mods.Import;

// Invented transport headers only. These fixtures do not establish a source
// edition, archive identity, or eligibility for the actual content importer.
internal static class OwnedSourceSnapshotChecks
{
    private static int _checks, _ownerThread;
    internal static void Run()
    {
        _checks = 0; _ownerThread = Environment.CurrentManagedThreadId;
        string root = Path.Combine(Path.GetTempPath(), "prime-source-snapshot-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Success(root).GetAwaiter().GetResult();
            BoundsAndFailures(root).GetAwaiter().GetResult();
            Cancellation(root).GetAwaiter().GetResult();
            Recovery(root).GetAwaiter().GetResult();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Check(!Directory.Exists(root), "fixture root cleaned");
        Console.WriteLine($"OWNED SOURCE SNAPSHOT PASS: {_checks} synthetic transport/lifetime checks");
    }

    private static byte[] Header(int length = 97, bool rvz = false)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 17 + 3)).ToArray();
        if (rvz) BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x52565a01);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x1c), 0xc2339f3d);
        return bytes;
    }
    private static string Case(string root, string name) => Path.Combine(root, name);
    private static void Clean(string root)
        => Check(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any(), "no partial snapshot or abandoned lock: " + Path.GetFileName(root));
    private static async Task Reject<T>(Func<Task> action, string why) where T : Exception
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why);
    }
    private static void Check(bool value, string why)
    { if (!value) throw new InvalidOperationException(why); _checks++; }

    private static async Task Success(string root)
    {
        foreach (bool rvz in new[] { false, true })
        {
            string scratch = Case(root, rvz ? "rvz" : "iso"); var bytes = Header(rvz: rvz);
            var source = new Source(bytes) { Chunk = 3 }; var progress = new List<long>();
            int opened = 0;
            var snapshot = await OwnedSourceSnapshot.AcquireAsync(_ =>
            { Interlocked.Increment(ref opened); Check(Environment.CurrentManagedThreadId != _ownerThread, "document open runs off blocked UI-owner thread"); return Task.FromResult<Stream>(source); },
                scratch, CancellationToken.None, progress.Add, maximumBytes: bytes.Length);
            string directory = Path.GetDirectoryName(snapshot.Path)!;
            Check(opened == 1 && source.Disposals == 1 && source.LengthRequests == 0 && source.SeekRequests == 0,
                "nonseekable unknown-length provider is opened/disposed once without seeking or size access");
            Check(Path.GetFileName(snapshot.Path) == (rvz ? "source.rvz" : "source.iso") && snapshot.Length == bytes.Length
                && File.ReadAllBytes(snapshot.Path).SequenceEqual(bytes), "short reads preserve every source byte and choose canonical sniffed filename");
            Check(Guid.TryParseExact(Path.GetFileName(directory), "N", out _) && File.Exists(Path.Combine(directory, "owner.lock")), "snapshot has unique owned directory/lock");
            using (var input = File.OpenRead(snapshot.Path))
                Check(input.CanRead && input.CanSeek && input.Length == bytes.Length, "existing importer receives a regular seekable local snapshot");
            Check(progress[0] == 32 && progress[^1] == bytes.Length && progress.Zip(progress.Skip(1)).All(p => p.First < p.Second), "bounded monotonic copy progress");
            Check(OwnedSourceSnapshot.RecoverAbandoned(scratch) == 0 && File.Exists(snapshot.Path), "recovery cannot delete a live snapshot");
            snapshot.Dispose(); snapshot.Dispose();
            Check(!Directory.Exists(directory) && source.Disposals == 1, "idempotent lease disposal deletes bytes after use without disposing provider twice"); Clean(scratch);
        }
        var capSource = new Source(Header(32)); string capRoot = Case(root, "minimum-exact");
        using (var exact = await OwnedSourceSnapshot.AcquireAsync(_ => Task.FromResult<Stream>(capSource), capRoot, CancellationToken.None, maximumBytes: 32))
            Check(exact.Length == 32 && capSource.BytesReturned == 32, "exact minimum cap admits complete transport header, not an importer qualification");
        Clean(capRoot);
    }

    private static async Task BoundsAndFailures(string root)
    {
        foreach (bool infinite in new[] { false, true })
        {
            string scratch = Case(root, infinite ? "infinite" : "cap-plus-one");
            var source = new Source(Header(68)) { Infinite = infinite }; Output? output = null;
            await Reject<InvalidDataException>(() => OwnedSourceSnapshot.AcquireAsync(_ => Task.FromResult<Stream>(source), scratch,
                CancellationToken.None, maximumBytes: 67, createOutput: p => output = new Output(p)), "cap+1 producer");
            Check(source.BytesReturned == 68 && output!.Written <= 67 && source.Disposals == 1 && output.Disposals == 1,
                "finite/infinite producer stops after cap+1 read without writing beyond budget and releases both streams"); Clean(scratch);
        }
        foreach (int length in new[] { 0, 1, 31, 32 })
        {
            string scratch = Case(root, "bad-header-" + length); var source = new Source(new byte[length]) { Chunk = 2 }; bool created = false;
            await Reject<InvalidDataException>(() => OwnedSourceSnapshot.AcquireAsync(_ => Task.FromResult<Stream>(source), scratch,
                CancellationToken.None, createOutput: p => { created = true; return new Output(p); }), "empty/truncated/unrecognized header");
            Check(!created && source.Disposals == 1, "invalid header is refused before output creation and disposes provider"); Clean(scratch);
        }
        foreach (string kind in new[] { "open", "null", "unreadable", "header-read", "body-read", "create", "header-write", "body-write", "flush", "progress" })
        {
            string scratch = Case(root, kind); var source = new Source(Header()) { Readable = kind != "unreadable", ThrowReadAt = kind == "header-read" ? 1 : kind == "body-read" ? 2 : 0 };
            Output? output = null;
            Func<CancellationToken, Task<Stream>> open = _ => kind == "open" ? Task.FromException<Stream>(new IOException("own open fault")) : Task.FromResult<Stream>(kind == "null" ? null! : source);
            Func<Task> attempt = () => OwnedSourceSnapshot.AcquireAsync(open, scratch, CancellationToken.None,
                kind == "progress" ? _ => throw new IOException("own progress fault") : null, maximumBytes: 100,
                createOutput: p => kind == "create" ? throw new IOException("own create fault") : output = new Output(p)
                { ThrowWriteAt = kind == "header-write" ? 1 : kind == "body-write" ? 2 : 0, ThrowFlush = kind == "flush" });
            if (kind == "unreadable") await Reject<InvalidDataException>(attempt, kind);
            else await Reject<IOException>(attempt, kind);
            Check(source.Disposals == (kind is "open" or "null" ? 0 : 1) && (output == null || output.Disposals == 1), "fault releases every acquired resource exactly once: " + kind); Clean(scratch);
        }
        foreach (int invalid in new[] { -1, 0, 31 })
        {
            string scratch = Case(root, "invalid-limit-" + invalid); bool opened = false;
            await Reject<ArgumentOutOfRangeException>(() => OwnedSourceSnapshot.AcquireAsync(_ => { opened = true; return Task.FromResult<Stream>(new Source(Header())); }, scratch, CancellationToken.None, maximumBytes: invalid), "invalid budget");
            Check(!opened && !Directory.Exists(scratch), "invalid budget refuses before opening document or allocating scratch");
        }
        await Reject<ArgumentOutOfRangeException>(() => OwnedSourceSnapshot.AcquireAsync(_ => throw new Exception("must not open"), Case(root, "oversize-limit"), CancellationToken.None,
            maximumBytes: OwnedSourceSnapshot.MaximumBytes + 1), "overproduction transport budget");
    }

    private static async Task Cancellation(string root)
    {
        string earlyRoot = Case(root, "pre-cancel"); using (var stop = new CancellationTokenSource())
        {
            stop.Cancel(); bool opened = false;
            await Reject<OperationCanceledException>(() => OwnedSourceSnapshot.AcquireAsync(_ => { opened = true; return Task.FromResult<Stream>(new Source(Header())); }, earlyRoot, stop.Token), "pre-cancel");
            Check(!opened && !Directory.Exists(earlyRoot), "pre-cancel performs no document or filesystem access");
        }
        foreach (string phase in new[] { "open", "header", "body", "progress", "flush" })
        {
            string scratch = Case(root, "cancel-" + phase); using var stop = new CancellationTokenSource();
            var source = new Source(Header()); Output? output = null;
            source.AfterRead = count => { if (count == (phase == "header" ? 1 : phase == "body" ? 2 : -1)) stop.Cancel(); };
            await Reject<OperationCanceledException>(() => OwnedSourceSnapshot.AcquireAsync(_ =>
                { if (phase == "open") stop.Cancel(); return Task.FromResult<Stream>(source); }, scratch, stop.Token,
                phase == "progress" ? _ => stop.Cancel() : null,
                createOutput: p => output = new Output(p) { AfterFlush = phase == "flush" ? stop.Cancel : null }), phase + " cancellation");
            Check(source.Disposals == 1 && (output == null || output.Disposals == 1), "cancellation disposes stream once despite concurrent abort: " + phase); Clean(scratch);
        }
        string lateRoot = Case(root, "late-open"); using (var stop = new CancellationTokenSource())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var opened = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously); var source = new Source(Header());
            var work = OwnedSourceSnapshot.AcquireAsync(_ => { entered.SetResult(); return opened.Task; }, lateRoot, stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop.Cancel();
            Check(!work.IsCompleted, "noncooperative late-open cannot falsely report resources already retired");
            opened.SetResult(source);
            await Reject<OperationCanceledException>(() => work, "late open after cancellation");
            Check(source.ReadCalls == 0 && source.Disposals == 1, "unclaimed late provider stream is never read and disposed exactly once"); Clean(lateRoot);
        }
        string blockedRoot = Case(root, "blocked-read"); using (var stop = new CancellationTokenSource())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new Source(Header()) { BlockBody = entered, ReleaseDispose = releaseDispose.Task };
            var work = OwnedSourceSnapshot.AcquireAsync(_ => Task.FromResult<Stream>(source), blockedRoot, stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop.Cancel();
            Check(!releaseDispose.Task.IsCompleted && !work.IsCompleted, "cancelling caller does not synchronously wait for blocked provider disposal");
            releaseDispose.SetResult(); await Reject<OperationCanceledException>(() => work, "dispose-abort wakes token-ignoring read");
            Check(source.Disposals == 1 && source.ReadCalls == 2, "abort/finally share one disposal task even when read ignores token"); Clean(blockedRoot);
        }
    }

    private static async Task Recovery(string root)
    {
        string scratch = Case(root, "recover"); Directory.CreateDirectory(scratch);
        string Make(string name, string data = "source.iso")
        { string dir = Path.Combine(scratch, name); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, "owner.lock"), Array.Empty<byte>()); File.WriteAllBytes(Path.Combine(dir, data), Header()); return dir; }
        var source = new Source(Header()); var active = await OwnedSourceSnapshot.AcquireAsync(_ => Task.FromResult<Stream>(source), scratch, CancellationToken.None);
        try
        {
            string abandonedIso = Make(Guid.NewGuid().ToString("N")), abandonedRvz = Make(Guid.NewGuid().ToString("N"), "source.rvz");
            string unrelated = Make("unrelated"), unknown = Make(Guid.NewGuid().ToString("N")); File.WriteAllText(Path.Combine(unknown, "keep.txt"), "own marker");
            string noLock = Make(Guid.NewGuid().ToString("N")); File.Delete(Path.Combine(noLock, "owner.lock"));
            string nested = Make(Guid.NewGuid().ToString("N")); File.Delete(Path.Combine(nested, "source.iso")); Directory.CreateDirectory(Path.Combine(nested, "source.iso"));
            Check(OwnedSourceSnapshot.RecoverAbandoned(scratch) == 2 && !Directory.Exists(abandonedIso) && !Directory.Exists(abandonedRvz), "only abandoned well-formed owned ISO/RVZ directories recover");
            Check(File.Exists(active.Path) && Directory.Exists(unrelated) && File.Exists(Path.Combine(unknown, "keep.txt"))
                && Directory.Exists(noLock) && Directory.Exists(Path.Combine(nested, "source.iso")), "recovery preserves active, unrelated, unknown, missing-lock and nested entries");
            Check(OwnedSourceSnapshot.RecoverAbandoned(scratch) == 0, "repeat recovery does not reclaim busy or unrelated entries");
        }
        finally { active.Dispose(); }
        await Symlinks(root);
        Check(OwnedSourceSnapshot.RecoverAbandoned(Case(root, "absent-recovery")) == 0, "missing scratch root recovery is harmless");
    }

    private static async Task Symlinks(string root)
    {
        string scratch = Case(root, "symlinks"), outside = Case(root, "own-outside"); Directory.CreateDirectory(scratch); Directory.CreateDirectory(outside);
        string target = Path.Combine(outside, "keep.bin"); File.WriteAllBytes(target, Header());
        string dirLink = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        try { Directory.CreateSymbolicLink(dirLink, outside); }
        catch (Exception e) when (e is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        { Console.WriteLine("OWNED SOURCE SNAPSHOT NOTE: host cannot create synthetic symlinks; symlink fixtures skipped"); return; }
        string ownerLinkDir = Path.Combine(scratch, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(ownerLinkDir);
        File.CreateSymbolicLink(Path.Combine(ownerLinkDir, "owner.lock"), target);
        string sourceLinkDir = Path.Combine(scratch, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sourceLinkDir);
        File.WriteAllBytes(Path.Combine(sourceLinkDir, "owner.lock"), Array.Empty<byte>()); File.CreateSymbolicLink(Path.Combine(sourceLinkDir, "source.iso"), target);
        string abandoned = Path.Combine(scratch, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(abandoned);
        File.WriteAllBytes(Path.Combine(abandoned, "owner.lock"), Array.Empty<byte>()); File.WriteAllBytes(Path.Combine(abandoned, "source.rvz"), Header(rvz: true));
        Check(OwnedSourceSnapshot.RecoverAbandoned(scratch) == 1 && !Directory.Exists(abandoned), "unsafe symlink entries are skipped without blocking safe abandoned sibling recovery");
        Check(new DirectoryInfo(dirLink).LinkTarget != null && new FileInfo(Path.Combine(ownerLinkDir, "owner.lock")).LinkTarget != null
            && new FileInfo(Path.Combine(sourceLinkDir, "source.iso")).LinkTarget != null && File.ReadAllBytes(target).SequenceEqual(Header()), "recovery never follows or alters GUID-directory/owner/source links and their targets");
        bool opened = false;
        await Reject<InvalidDataException>(() => OwnedSourceSnapshot.AcquireAsync(_ => { opened = true; return Task.FromResult<Stream>(new Source(Header())); }, dirLink, CancellationToken.None), "unsafe symlink scratch root");
        Check(!opened && File.ReadAllBytes(target).SequenceEqual(Header()), "unsafe root refuses before document open and preserves target");
        Directory.Delete(dirLink); File.Delete(Path.Combine(ownerLinkDir, "owner.lock")); File.Delete(Path.Combine(sourceLinkDir, "source.iso"));
    }

    private sealed class Source(byte[] data) : Stream
    {
        internal int Chunk = int.MaxValue, ThrowReadAt, Disposals, ReadCalls, LengthRequests, SeekRequests;
        internal long BytesReturned; internal bool Infinite, Readable = true;
        internal Action<int>? AfterRead; internal TaskCompletionSource? BlockBody;
        internal Task? ReleaseDispose; private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;
        public override bool CanRead => Readable; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length { get { LengthRequests++; throw new NotSupportedException("unknown source length"); } }
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            if (ThrowReadAt == ReadCalls) throw new IOException("own read fault");
            if (BlockBody != null && ReadCalls == 2) { BlockBody.SetResult(); await _disposed.Task; return 0; }
            int count = Math.Min(buffer.Length, Math.Min(Chunk, Infinite ? int.MaxValue : data.Length - _position));
            for (int i = 0; i < count; i++) buffer.Span[i] = _position + i < data.Length ? data[_position + i] : (byte)0x5a;
            _position += count; BytesReturned += count; AfterRead?.Invoke(ReadCalls); return count;
        }
        public override async ValueTask DisposeAsync()
        { Interlocked.Increment(ref Disposals); if (ReleaseDispose != null) await ReleaseDispose; _disposed.TrySetResult(); GC.SuppressFinalize(this); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("async fixture");
        public override long Seek(long offset, SeekOrigin origin) { SeekRequests++; throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Output(string path) : Stream
    {
        private readonly FileStream _file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        internal int ThrowWriteAt, Disposals, Writes; internal long Written; internal bool ThrowFlush; internal Action? AfterFlush;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _file.Length; public override long Position { get => _file.Position; set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Writes++; if (Writes == ThrowWriteAt) throw new IOException("own write fault"); await _file.WriteAsync(buffer, cancellationToken); Written += buffer.Length; }
        public override async Task FlushAsync(CancellationToken cancellationToken)
        { if (ThrowFlush) throw new IOException("own flush fault"); await _file.FlushAsync(cancellationToken); AfterFlush?.Invoke(); }
        public override async ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); await _file.DisposeAsync(); GC.SuppressFinalize(this); }
        public override void Flush() => _file.Flush(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("async fixture");
    }
}
