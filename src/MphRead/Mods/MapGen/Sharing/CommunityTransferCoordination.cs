using System;
using System.IO;
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
}
