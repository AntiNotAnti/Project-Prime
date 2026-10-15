using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Cross-process transaction and job ownership leases for one Community
/// transfer directory. Index transactions are short; network work never
/// holds the global index gate. A running job exclusively owns its own
/// durable lease file until the worker exits, including cancellation.
/// Lock files are permanent and MUST NOT be unlinked while in use.
/// </summary>
internal sealed class CommunityTransferCoordination
{
    private readonly string _indexLease;
    private readonly string _jobLeases;

    internal CommunityTransferCoordination(string root)
    {
        string locks = Path.Combine(root, "transfer-leases");
        _jobLeases = locks;
        Directory.CreateDirectory(locks);
        _indexLease = Path.Combine(locks, "index-v1.lock");
    }

    internal FileStream LockIndex(CancellationToken cancellation = default)
    {
        // Fail closed rather than silently writing stale index data when a
        // second process is unresponsive. 10 seconds exceeds normal JSON
        // load/atomic replace time while bounding UI/main-thread blocking.
        const int maxRetries = 400;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_indexLease,
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(25);
            }
        }
        throw new IOException("Community transfer index is locked by another process.");
    }

    internal FileStream? TryLeaseJob(Guid id)
    {
        if (id == Guid.Empty)
            throw new InvalidDataException("Invalid Community transfer job lease identity.");
        try
        {
            return new FileStream(Path.Combine(_jobLeases,
                id.ToString("N") + ".job.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
        }
        catch (IOException) { return null; }
    }

    internal bool IsJobOwned(Guid id)
    {
        using var lease = TryLeaseJob(id);
        return lease == null;
    }

    /// <summary>Fence concurrent queue writers by exact application version.
    /// Receipt schema and queue identity are unchanged. Crashed owners release
    /// their OS lock; stale metadata is retired under the existing index lock.</summary>
    internal IDisposable AcquireWriterVersion(string? version = null)
    {
        version ??= typeof(CommunityTransferCoordination).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";
        if (string.IsNullOrWhiteSpace(version) || version.Length > 256
            || version.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new InvalidDataException("Invalid Community writer version.");
        using var index = LockIndex();
        string directory = Path.Combine(_jobLeases, "writers-v1");
        Directory.CreateDirectory(directory);
        foreach (string recordPath in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(recordPath), "N", out _))
                throw new InvalidDataException("Invalid Community writer record.");
            FileStream? stale = null;
            try { stale = new FileStream(Path.ChangeExtension(recordPath, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { }
            if (stale != null)
            {
                using (stale) File.Delete(recordPath);
                continue;
            }
            if (new FileInfo(recordPath).Length is <= 0 or > 2048)
                throw new InvalidDataException("Invalid active Community writer metadata.");
            WriterVersion? active;
            try { active = JsonSerializer.Deserialize<WriterVersion>(File.ReadAllBytes(recordPath)); }
            catch (JsonException ex) { throw new InvalidDataException("Invalid active Community writer metadata.", ex); }
            if (active == null || active.Schema != 1 || active.Version != version)
                throw new IOException("Another Game or Studio version owns Community transfers. Close it and use a matching Game/Studio pair.");
        }
        string own = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        FileStream lease = new(own + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            using (FileStream metadata = new(own + ".json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(metadata, new WriterVersion(1, version));
                metadata.Flush(flushToDisk: true);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private sealed record WriterVersion(int Schema, string Version);
}
