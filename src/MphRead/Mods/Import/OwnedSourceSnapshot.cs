using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Import;

// A selected document is not necessarily a seekable file. Only a complete,
// bounded private snapshot may cross into the existing path-based disc reader.
// Header recognition is transport validation; the importer still verifies the
// actual source edition and original archive hashes.
internal sealed class OwnedSourceSnapshot : IDisposable
{
    internal const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private const int BufferBytes = 128 * 1024;
    private readonly string _root, _directory;
    private FileStream? _owner;
    internal string Path { get; private set; } = "";
    internal long Length { get; private set; }
    private OwnedSourceSnapshot(string root, string directory, FileStream owner)
        => (_root, _directory, _owner) = (root, directory, owner);

    internal static Task<OwnedSourceSnapshot> AcquireAsync(Func<CancellationToken, Task<Stream>> open,
        string scratchRoot, CancellationToken cancellation, Action<long>? progress = null,
        long maximumBytes = MaximumBytes, Func<string, Stream>? createOutput = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (maximumBytes is < 32 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        string capturedRoot = System.IO.Path.GetFullPath(scratchRoot);
        return Task.Run(() => CaptureAsync(open, capturedRoot, cancellation, progress, maximumBytes, createOutput), CancellationToken.None);
    }

    private static async Task<OwnedSourceSnapshot> CaptureAsync(Func<CancellationToken, Task<Stream>> open,
        string root, CancellationToken token, Action<long>? progress, long cap, Func<string, Stream>? createOutput)
    {
        token.ThrowIfCancellationRequested();
        LocalImportBundle.CheckPath(root, root); Directory.CreateDirectory(root); LocalImportBundle.CheckPath(root, root);
        string directory = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); LocalImportBundle.CheckPath(directory, root);
        OwnedSourceSnapshot? snapshot = null;
        try
        {
            string ownerPath = System.IO.Path.Combine(directory, "owner.lock");
            LocalImportBundle.CheckPath(ownerPath, root);
            snapshot = new(root, directory, new FileStream(ownerPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
            token.ThrowIfCancellationRequested();
            Stream source = await open(token).ConfigureAwait(false) ?? throw new IOException("The selected source could not be opened.");
            // Dispose exactly once. A provider may ignore ReadAsync's token;
            // abort disposal runs on a worker, never on the cancelling UI thread.
            object disposeGate = new(); Task? disposing = null;
            Task DisposeSource()
            {
                lock (disposeGate) return disposing ??= Task.Run(async () => await source.DisposeAsync().ConfigureAwait(false));
            }
            using var abort = token.Register(() => { _ = DisposeSource(); });
            try
            {
                token.ThrowIfCancellationRequested();
                if (!source.CanRead) throw new InvalidDataException("The selected source is not readable.");
                byte[] buffer = new byte[BufferBytes]; int header = 0;
                while (header < 32)
                {
                    token.ThrowIfCancellationRequested();
                    int read = await source.ReadAsync(buffer.AsMemory(header, 32 - header), token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (read <= 0 || read > 32 - header) throw new InvalidDataException("The selected source header is incomplete.");
                    header += read;
                }
                string suffix = BinaryPrimitives.ReadUInt32BigEndian(buffer) == 0x52565a01 ? ".rvz"
                    : BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0x1c)) == 0xc2339f3d ? ".iso"
                    : throw new InvalidDataException("Select a supported GameCube ISO, GCM or RVZ source.");
                snapshot.Path = System.IO.Path.Combine(directory, "source" + suffix);
                LocalImportBundle.CheckPath(snapshot.Path, root); token.ThrowIfCancellationRequested();
                await using (Stream output = createOutput != null ? createOutput(snapshot.Path) ?? throw new IOException("The private source copy could not be created.")
                    : new FileStream(snapshot.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await output.WriteAsync(buffer.AsMemory(0, header), token).ConfigureAwait(false);
                    long total = header; progress?.Invoke(total);
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        int requested = (int)Math.Min(buffer.Length, cap - total + 1);
                        int read = await source.ReadAsync(buffer.AsMemory(0, requested), token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (read < 0 || read > requested) throw new InvalidDataException("Invalid source stream read.");
                        if (read == 0) break;
                        if (read > cap - total) throw new InvalidDataException("The selected source exceeds the 2 GiB transport limit.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        total += read; progress?.Invoke(total);
                    }
                    await output.FlushAsync(token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                    snapshot.Length = total;
                }
            }
            finally { await DisposeSource().ConfigureAwait(false); }
            token.ThrowIfCancellationRequested(); LocalImportBundle.CheckPath(snapshot.Path, root);
            return snapshot;
        }
        catch
        {
            if (snapshot != null) snapshot.Dispose();
            else if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null); if (owner == null) return;
        owner.Dispose(); DeleteOwnedDirectory(_directory, _root);
    }
    private static void DeleteOwnedDirectory(string directory, string root)
    {
        if (!Directory.Exists(directory)) return;
        LocalImportBundle.CheckPath(directory, root);
        string[] entries = Directory.GetFileSystemEntries(directory);
        if (entries.Any(p => System.IO.Path.GetFileName(p) is not ("owner.lock" or "source.iso" or "source.rvz")
            || (File.GetAttributes(p) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0))
            throw new IOException("Unexpected files prevent private source cleanup.");
        foreach (string path in entries) { LocalImportBundle.CheckPath(path, root); File.Delete(path); }
        try { Directory.Delete(directory); } catch (DirectoryNotFoundException) { }
    }
    // Only our GUID directories with an exclusive, abandoned owner lock are
    // recoverable. An active operation or an unrelated file is never deleted.
    internal static int RecoverAbandoned(string scratchRoot)
    {
        string root = System.IO.Path.GetFullPath(scratchRoot); LocalImportBundle.CheckPath(root, root);
        if (!Directory.Exists(root)) return 0; int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(root).Take(128))
        {
            if (!Guid.TryParseExact(System.IO.Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                LocalImportBundle.CheckPath(directory, root); string owner = System.IO.Path.Combine(directory, "owner.lock");
                if (!File.Exists(owner)) continue;
                LocalImportBundle.CheckPath(owner, root);
                using (var claim = new FileStream(owner, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // Holding the claim prevents another recovery/process from
                    // treating this directory as available while it is checked.
                    string[] files = Directory.GetFileSystemEntries(directory);
                    if (files.Any(p => System.IO.Path.GetFileName(p) is not ("owner.lock" or "source.iso" or "source.rvz")
                        || (File.GetAttributes(p) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)) continue;
                    foreach (string file in files.Where(p => p != owner)) { LocalImportBundle.CheckPath(file, root); File.Delete(file); }
                }
                DeleteOwnedDirectory(directory, root); removed++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            { /* Busy, removed concurrently, or not safely recoverable. */ }
        }
        return removed;
    }
}
