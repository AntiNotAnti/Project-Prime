using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// Asset-free local Community transfer contract. Does not install into a live
// scene or claim to validate Android UI or a real remote-service throughput.
string root = Path.Combine(Path.GetTempPath(),
    "prime-community-transfers-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string title)
{
    if (!condition) throw new InvalidOperationException(title);
    Console.WriteLine("PASS " + title);
    checks++;
}
using var stop = new CancellationTokenSource();
Task? service = null;
try
{
    string package = MapPackageBuilder.Build(
        MapTemplates.Create("COMMUNITY_TRANSFER_CHECK", true).Definition,
        Path.Combine(root, "source.ppmap"));
    string hash = MapBuildFingerprint.HashFile(package);
    byte[] bytes = File.ReadAllBytes(package);

    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    string endpoint = "http://127.0.0.1:"
        + ((IPEndPoint)probe.LocalEndpoint).Port + "/";
    probe.Stop();

    const string secret = "prime-community-transfer-fixture-admin";
    service = MapCommunityServer.ServeAsync(endpoint,
        Path.Combine(root, "server"), secret, stop.Token);
    var credentials = new DelegateCommunityCredentialSource(
        ct => Task.FromResult(secret),
        ct => Task.FromResult(secret));
    string jobsDir = Path.Combine(root, "jobs");
    Guid uploadId, downloadId;
    CommunityMap published;
    string archivePath;

    await using (var transfers = new CommunityTransferManager(jobsDir, credentials))
    {
        uploadId = await transfers.EnqueueUploadAsync(endpoint, package,
            publish: new CommunityPublishRequest(ExistingMap: false,
                ExpectedParentHash: null),
            listed: true, draft: false);
        // The snapshot already belongs to the job. Editing/removing the
        // author's source cannot change immutable bytes being published.
        File.Delete(package);
        CommunityTransferJob uploaded = await transfers.WaitAsync(uploadId);
        Check(uploaded.State == CommunityTransferState.Completed,
            "upload completes from a frozen snapshot after the source disappears");
        Check(uploaded.PackageHash == hash,
            "uploaded exact package hash matches immutable source");
        Check(!File.Exists(Path.Combine(jobsDir, "uploads",
            uploadId.ToString("N") + ".ppmap")),
            "completed upload releases its frozen staging copy");

        using var client = new MapCommunityClient(endpoint, secret);
        published = await client.GetPackageAsync(hash, CancellationToken.None)
            ?? throw new InvalidOperationException("Server did not retain upload.");
        Check(published.Hash == hash && published.Bytes == bytes.LongLength,
            "published catalog preserves exact package metadata");

        // Persist an interrupted prior network prefix on the same volume.
        // The downloader must issue an HTTP Range continuation for it.
        string partial = Path.Combine(jobsDir, "partials", hash + ".part");
        File.WriteAllBytes(partial, bytes.AsSpan(0, Math.Min(128, bytes.Length)).ToArray());
        downloadId = transfers.EnqueueDownload(endpoint, published);
        Check(transfers.EnqueueDownload(endpoint, published) == downloadId,
            "concurrent requests share one exact download job");
        CommunityTransferJob downloaded = await transfers.WaitAsync(downloadId);
        Check(downloaded.State == CommunityTransferState.Completed,
            "partial HTTP Range download completes and verifies");
        archivePath = transfers.VerifiedArchivePath(downloadId)
            ?? throw new InvalidOperationException("Verified download was not promoted.");
        Check(File.ReadAllBytes(archivePath).SequenceEqual(bytes),
            "Range continuation yields byte-for-byte original archive");
        Check(!File.Exists(partial),
            "successful transfer no longer exposes a partial archive");

        string metadata = File.ReadAllText(Path.Combine(jobsDir, "jobs-v1.json"));
        Check(!metadata.Contains(secret, StringComparison.Ordinal),
            "job metadata contains no bearer or Community credential");
        Check(metadata.Contains(hash, StringComparison.Ordinal),
            "job metadata persists exact immutable content identity");
    }

    await using (var recovered = new CommunityTransferManager(jobsDir, credentials))
    {
        Check(recovered.GetJob(uploadId).State == CommunityTransferState.Completed
            && recovered.GetJob(downloadId).State == CommunityTransferState.Completed,
            "transfer receipts survive application/service object restart");
        Check(recovered.VerifiedArchivePath(downloadId) == archivePath,
            "reopened transfer store finds the same verified archive");

        // Repeated exact selection reuses a complete immutable download even
        // if the author source is now gone. No network request is required.
        Guid again = recovered.EnqueueDownload(endpoint, published);
        Check(again == downloadId && recovered.GetJob(again).State
            == CommunityTransferState.Completed,
            "repeat map selection does not re-download verified bytes");
    }

    // Rehydrate interrupted private bytes and metadata exactly as if the
    // process had exited while the HTTP Range request was active. Restart is
    // allowed to resume a Retryable job, not one deliberately paused by a user.
    string indexFile = Path.Combine(jobsDir, "jobs-v1.json");
    void SimulateInterrupted(CommunityTransferState state, int prefixBytes)
    {
        string archive = Path.Combine(jobsDir, "archives", hash + ".ppmap");
        File.Delete(archive);
        File.WriteAllBytes(Path.Combine(jobsDir, "partials", hash + ".part"),
            bytes.AsSpan(0, prefixBytes).ToArray());
        var index = JsonSerializer.Deserialize<TransferIndexFixture>(
            File.ReadAllText(indexFile), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("Missing durable transfer index.");
        CommunityTransferJob[] recovered = index.Jobs.Select(job => job.Id == downloadId
            ? job with { State = state, AcknowledgedBytes = prefixBytes }
            : job).ToArray();
        File.WriteAllText(indexFile, JsonSerializer.Serialize(
            new TransferIndexFixture(index.Schema, recovered), MapPackageReader.JsonOptions));
    }

    SimulateInterrupted(CommunityTransferState.Paused, Math.Min(64, bytes.Length / 2));
    await using (var paused = new CommunityTransferManager(jobsDir, credentials))
    {
        Check(paused.GetJob(downloadId).State == CommunityTransferState.Paused,
            "user pause survives restart without auto-resume");
        paused.StartPending();
        Check(paused.GetJob(downloadId).State == CommunityTransferState.Paused,
            "restart does not override an intentional pause");
        paused.Resume(downloadId);
        Check((await paused.WaitAsync(downloadId)).State == CommunityTransferState.Completed,
            "manually resumed download reaches exact verified archive");
    }
    SimulateInterrupted(CommunityTransferState.Downloading, Math.Min(64, bytes.Length / 2));
    await using (var interrupted = new CommunityTransferManager(jobsDir, credentials))
    {
        Check(interrupted.GetJob(downloadId).State == CommunityTransferState.Retryable,
            "crashed in-progress transfer is recovered as Retryable");
        interrupted.StartPending();
        Check((await interrupted.WaitAsync(downloadId)).State == CommunityTransferState.Completed,
            "restart resumes interrupted HTTP Range without republishing an archive");
        Check(File.ReadAllBytes(interrupted.VerifiedArchivePath(downloadId)!)
            .SequenceEqual(bytes), "recovered archive is SHA-256/byte exact");
    }

    // Auth is not persisted: reopening without credentials cannot create new
    // authenticated upload operations, but already completed receipts remain.
    await using (var noCredentials = new CommunityTransferManager(jobsDir))
        Check(noCredentials.ListJobs().Count == 2,
            "receipts can be read without loading an account credential");

    Console.WriteLine($"Community transfer checks: {checks} PASS.");
}
finally
{
    stop.Cancel();
    if (service != null)
    {
        try { await service; }
        catch (OperationCanceledException) { }
    }
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}

internal sealed record TransferIndexFixture(int Schema, CommunityTransferJob[] Jobs);
