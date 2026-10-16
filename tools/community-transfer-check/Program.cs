using System;
using System.Diagnostics;
using System.Reflection;
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
//
// Cross-process receipt files are published by rename, not created at their
// final path while still being written. On Windows, File.Exists can see an
// open File.WriteAllText target before it is closed; readers then fail with
// a sharing violation (or observe a partial GUID). Keep the rendezvous
// protocol deterministic on every supported filesystem.
static void PublishReceipt(string path, string contents)
{
    string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
    try
    {
        File.WriteAllText(pending, contents);
        File.Move(pending, path);
    }
    finally
    {
        if (File.Exists(pending)) File.Delete(pending);
    }
}

// The child mode is a REAL second dotnet process. It intentionally opens the
// same durable manager directory to prove index fencing and per-job ownership,
// not just two objects that happen to share one managed lock.
if (args.Length == 6 && args[0] == "--crossprocess-stalled-owner")
{
    string directory=args[1], endpoint=args[2], package=args[3],
        secret=args[4], receipt=args[5];
    var credentials = new DelegateCommunityCredentialSource(
        ct => Task.FromResult(secret), ct => Task.FromResult(secret));
    await using var manager = new CommunityTransferManager(directory, credentials);
    Guid id=await manager.EnqueueUploadAsync(endpoint,package,publish:null,
        listed:true,draft:false);
    PublishReceipt(receipt,id.ToString("D"));
    // Parent kills this process without a managed Dispose, deliberately
    // simulating an editor crash in the middle of an HTTP upload.
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

if (args.Length == 9 && args[0] == "--crossprocess-child")
{
    string directory = args[1], endpoint = args[2],
        package = args[3], exactHash = args[4],
        secret = args[5], rendezvous = args[6],
        receipt = args[7], stage = args[8];
    var credentials = new DelegateCommunityCredentialSource(
        ct => Task.FromResult(secret), ct => Task.FromResult(secret));
    await using var manager = new CommunityTransferManager(directory, credentials);
    PublishReceipt(rendezvous + ".ready", "ready");

    static async Task WaitFlag(string name)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (!File.Exists(name))
            await Task.Delay(40,deadline.Token);
    }

    await WaitFlag(rendezvous + ".upload");
    Guid upload = await manager.EnqueueUploadAsync(
        endpoint, package, publish: null, listed: true, draft: false);
    PublishReceipt(receipt + ".upload", upload.ToString("D"));
    var uploaded = await manager.WaitAsync(upload);
    if (uploaded.State != CommunityTransferState.Completed)
        throw new InvalidDataException("Secondary-process upload did not complete: " + uploaded.State);
    PublishReceipt(receipt + ".upload-complete", uploaded.PackageHash);

    await WaitFlag(rendezvous + ".download");
    using (var client = new MapCommunityClient(endpoint,secret))
    {
        CommunityMap exact = await client.GetPackageAsync(exactHash,CancellationToken.None)
            ?? throw new InvalidDataException("Missing published map for cross-process download.");
        Guid id = manager.EnqueueDownload(endpoint,exact);
        PublishReceipt(receipt + ".download",id.ToString("D"));
        var downloaded = await manager.WaitAsync(id);
        if (downloaded.State != CommunityTransferState.Completed)
            throw new InvalidDataException("Secondary-process download did not complete: " + downloaded.State);
        PublishReceipt(receipt + ".download-complete", downloaded.PackageHash);
    }
    PublishReceipt(receipt + ".jobs",
        string.Join(",",manager.ListJobs().Select(j => j.Id.ToString("D"))));
    await WaitFlag(rendezvous + ".stop");
    return;
}

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
    Check(!CommunityCutoverPolicy.Enabled(null)
        && !CommunityCutoverPolicy.Enabled("")
        && !CommunityCutoverPolicy.Enabled("true")
        && !CommunityCutoverPolicy.Enabled("0")
        && CommunityCutoverPolicy.Enabled("1"),
        "original strict feature flag parsing remains compatible");
    Check(CommunityCutoverPolicy.EnabledByDefault(null)
        && CommunityCutoverPolicy.EnabledByDefault("1")
        && CommunityCutoverPolicy.EnabledByDefault("true")
        && !CommunityCutoverPolicy.EnabledByDefault("0")
        && !CommunityCutoverPolicy.EnabledByDefault("false")
        && !CommunityCutoverPolicy.EnabledByDefault("invalid"),
        "Community features are default-on with explicit fail-closed rollback values");
    string? priorCreator = Environment.GetEnvironmentVariable(
        CommunityCutoverPolicy.StudioCreatorPreviewVariable);
    string? priorMirror = Environment.GetEnvironmentVariable(
        CommunityCutoverPolicy.ObjectMirrorVariable);
    string? priorNetRadiant = Environment.GetEnvironmentVariable(
        CommunityCutoverPolicy.NetRadiantDefaultVariable);
    try
    {
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.StudioCreatorPreviewVariable, null);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.ObjectMirrorVariable, null);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantDefaultVariable, null);
        Check(CommunityCutoverPolicy.StudioCreatorPreview
            && CommunityCutoverPolicy.ObjectMirrorEnabled,
            "creator Hub and verified public-map mirroring activate automatically");
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.StudioCreatorPreviewVariable, "0");
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.ObjectMirrorVariable, "0");
        Check(!CommunityCutoverPolicy.StudioCreatorPreview
            && !CommunityCutoverPolicy.ObjectMirrorEnabled,
            "old Studio dashboard and origin-only storage retain explicit rollback switches");
        Check(!CommunityCutoverPolicy.NetRadiantDefault,
            "NetRadiant does not replace native Studio without an installed configured binary");
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.StudioCreatorPreviewVariable, priorCreator);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.ObjectMirrorVariable, priorMirror);
        Environment.SetEnvironmentVariable(
            CommunityCutoverPolicy.NetRadiantDefaultVariable, priorNetRadiant);
    }

    // The transfer journal reloads this endpoint policy frequently; it must
    // agree with the networking constructor without opening an HTTP connection.
    foreach (string invalidEndpoint in new[]
    {
        "http://example.com/", "https://example.com/?ticket=private",
        "https://user@example.com/"
    })
    {
        bool rejected = false;
        try { using var invalid = new MapCommunityClient(invalidEndpoint); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "invalid Community endpoint policy rejects " + invalidEndpoint);
    }
    using (var local = new MapCommunityClient("http://127.0.0.1:14567/")) { }
    Check(true, "loopback HTTP remains supported for Community local acceptance");

    string package = MapPackageBuilder.Build(
        MapTemplates.Create("COMMUNITY_TRANSFER_CHECK", true).Definition,
        Path.Combine(root, "source.ppmap"));
    string hash = MapBuildFingerprint.HashFile(package);
    byte[] bytes = File.ReadAllBytes(package);

    // Run the same bounded atomic async copy used for host-owned map staging.
    // The previous host path materialized the entire package in a byte[].
    var atomicCopy = typeof(MapBuildFingerprint).Assembly
        .GetType("MphRead.Mods.MapGen.AtomicFile")!
        .GetMethod("CopyAsync", System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Static)!;
    var copyAsync = (Func<string, string, CancellationToken, Task<long>>)
        atomicCopy.CreateDelegate(typeof(Func<string, string, CancellationToken, Task<long>>));
    string clonedArchive = Path.Combine(root, "copied-source.ppmap");
    Check(await copyAsync(package, clonedArchive, CancellationToken.None) == bytes.LongLength
        && MapBuildFingerprint.HashFile(clonedArchive) == hash,
        "bounded async atomic archive copy preserves exact package hash");
    string interruptedCopy = Path.Combine(root, "cancelled-copy.ppmap");
    using (var cancelCopy = new CancellationTokenSource())
    {
        cancelCopy.Cancel();
        bool cancelled = false;
        try { await copyAsync(package, interruptedCopy, cancelCopy.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && !File.Exists(interruptedCopy),
            "cancelled hosted archive copy never publishes an incomplete destination");
    }


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

    // An old Completed receipt must not suppress download when LRU removed the
    // private archive, or when a local file was damaged between app sessions.
    await using (var cacheRecovery = new CommunityTransferManager(jobsDir, credentials))
    {
        string exact = Path.Combine(jobsDir, "archives", hash + ".ppmap");
        File.Delete(exact);
        Guid afterEviction = cacheRecovery.EnqueueDownload(endpoint, published);
        Check(afterEviction == downloadId, "evicted archive reuses its stable job identity");
        Check((await cacheRecovery.WaitAsync(afterEviction)).State ==
            CommunityTransferState.Completed
            && File.ReadAllBytes(cacheRecovery.VerifiedArchivePath(downloadId)!)
                .SequenceEqual(bytes),
            "missing completed archive is downloaded and verified again");

        File.WriteAllBytes(exact, new byte[] { 1, 2, 3, 4 });
        Guid afterCorruption = cacheRecovery.EnqueueDownload(endpoint, published);
        Check(afterCorruption == downloadId,
            "corrupt archive reuses its stable job identity");
        Check((await cacheRecovery.WaitAsync(afterCorruption)).State ==
            CommunityTransferState.Completed
            && File.ReadAllBytes(cacheRecovery.VerifiedArchivePath(downloadId)!)
                .SequenceEqual(bytes),
            "corrupted completed cache is repaired before reuse");
        Check(Directory.GetFiles(Path.Combine(jobsDir, "quarantine"),
            "*.corrupt").Length >= 1,
            "corrupted bytes are quarantined, never accepted as installed content");
    }

    // PR-14: publication from a frozen Studio intent shares the durable
    // upload manager and never retains the changing editor document.
    await using (var creatorTransfers = new CommunityTransferManager(
        Path.Combine(root, "creator-intent-jobs"), credentials))
    {
        MapDefinition mutable = MapTemplates.Create("CREATOR_HANDOFF", true).Definition;
        mutable.MapId = Guid.Parse("96f0d5ca-7255-45ec-a6b6-7c0e12fc327d");
        mutable.Name = "CREATOR_HANDOFF";
        CommunityStudioPublishIntent intent = CommunityStudioPublishIntent.Capture(mutable);
        Guid frozenMapId = intent.MapId;
        mutable.MapId = Guid.NewGuid();
        mutable.Name = "MUTATED_AFTER_HANDOFF";
        var operations = new CommunityMapOperations(endpoint, credentials);
        CommunityStudioPublishReceipt receipt = await intent.QueueAsync(
            endpoint, operations, creatorTransfers,
            CommunityPublishVisibility.Draft, "First creator draft");
        Check(receipt.MapId == frozenMapId && receipt.RuntimeName == "CREATOR_HANDOFF"
            && !receipt.IsRevision && receipt.ExpectedParentHash == null
            && receipt.Visibility == CommunityPublishVisibility.Draft,
            "creator intent captures one immutable map identity and begins a new draft");
        CommunityTransferJob creatorJob = await creatorTransfers.WaitAsync(receipt.JobId);
        Check(creatorJob.State == CommunityTransferState.Completed
            && creatorJob.PackageHash == receipt.PackageHash
            && creatorJob.Publish == new CommunityPublishRequest(false, null,
                "First creator draft", false),
            "creator publish handoff reaches authenticated durable exact-hash queue");
        CommunityMapProject[] mine = await operations.BrowseProjectsAsync(
            mine: true, favorites: false, sort: "updated",
            cancellation: CancellationToken.None);
        Check(mine.Any(p => p.MapId == frozenMapId)
            && mine.All(p => p.MapId != mutable.MapId),
            "published creator-owned draft has captured MapId, not subsequent editor changes");

        MapDefinition noId = MapProjectSerializer.Clone(mutable);
        noId.MapId = Guid.Empty;
        bool idRefused = false;
        try { CommunityStudioPublishIntent.Capture(noId); }
        catch (InvalidDataException) { idRefused = true; }
        Check(idRefused, "creator publication refuses identity-less source projects");

        var frozenAgain = CommunityStudioPublishIntent.Capture(mutable);
        bool notesRefused = false;
        try
        {
            await frozenAgain.QueueAsync(endpoint, operations, creatorTransfers,
                CommunityPublishVisibility.Published, new string('x', 4001));
        }
        catch (ArgumentOutOfRangeException) { notesRefused = true; }
        Check(notesRefused, "oversized creator release notes are rejected before network or package writes");
    }

    // Two real OS processes must share one index without overwriting each
    // other's jobs, preserve exact package SHA, and deduplicate same-hash
    // downloads while both windows remain open.
    string multiRoot = Path.Combine(root,"multiprocess");
    Directory.CreateDirectory(multiRoot);
    string pkgA = MapPackageBuilder.Build(
        MapTemplates.Create("CROSSPROCESS_A",true).Definition,
        Path.Combine(root,"cross-a.ppmap"));
    string pkgB = MapPackageBuilder.Build(
        MapTemplates.Create("CROSSPROCESS_B",true).Definition,
        Path.Combine(root,"cross-b.ppmap"));
    string hashA = MapBuildFingerprint.HashFile(pkgA);
    string hashB = MapBuildFingerprint.HashFile(pkgB);
    string rendezvous = Path.Combine(root,"cross-sync");
    string receipts = Path.Combine(root,"cross-child");
    await using (var parentManager = new CommunityTransferManager(multiRoot,credentials))
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach(string argument in new[]
        {
            "--crossprocess-child",multiRoot,endpoint,pkgB,hashA,
            secret,rendezvous,receipts,"simultaneous"
        }) start.ArgumentList.Add(argument);
        using Process child = Process.Start(start)
            ?? throw new InvalidDataException("Unable to start secondary Community transfer process.");
        static async Task WaitFile(string path)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (!File.Exists(path))
                await Task.Delay(50,deadline.Token);
        }
        try
        {
            await WaitFile(rendezvous+".ready");
            File.WriteAllText(rendezvous+".upload","go");
            Guid parentUpload = await parentManager.EnqueueUploadAsync(
                endpoint,pkgA,publish:null,listed:true,draft:false);
            await WaitFile(receipts+".upload");
            Guid childUpload = Guid.Parse(File.ReadAllText(receipts+".upload").Trim());
            Check(parentUpload != childUpload,
                "parallel processes enqueue distinct immutable uploads into one shared index");
            var parentCompleted = await parentManager.WaitAsync(parentUpload);
            await WaitFile(receipts+".upload-complete");
            Check(parentCompleted.State == CommunityTransferState.Completed
                && parentCompleted.PackageHash == hashA
                && File.ReadAllText(receipts+".upload-complete")==hashB,
                "both processes upload their own frozen packages without lost receipts");
            Check(parentManager.ListJobs().Any(j=>j.Id==childUpload
                && j.State == CommunityTransferState.Completed),
                "parent process observes child's completed upload in the same durable index");

            using(var client = new MapCommunityClient(endpoint,secret))
            {
                CommunityMap same = await client.GetPackageAsync(hashA,CancellationToken.None)
                    ?? throw new InvalidDataException("Missing exact map for concurrency fixture");
                File.WriteAllText(rendezvous+".download","go");
                Guid parentDownload = parentManager.EnqueueDownload(endpoint,same);
                await WaitFile(receipts+".download");
                Guid childDownload=Guid.Parse(File.ReadAllText(receipts+".download"));
                Check(parentDownload == childDownload,
                    "simultaneous Game and Studio processes deduplicate one exact download job");
                Check((await parentManager.WaitAsync(parentDownload)).State
                    == CommunityTransferState.Completed,
                    "parent waits successfully even when other process owns the download lease");
                await WaitFile(receipts+".download-complete");
                Check(File.ReadAllText(receipts+".download-complete")==hashA,
                    "secondary-process verified download retains source SHA-256 identity");
            }
            await WaitFile(receipts+".jobs");
            Check(File.ReadAllText(receipts+".jobs")
                .Contains(parentUpload.ToString("D"),StringComparison.OrdinalIgnoreCase),
                "secondary process sees parent-owned jobs in shared durable history");
            File.WriteAllText(rendezvous+".stop","stop");
            using var stopped=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await child.WaitForExitAsync(stopped.Token);
            Check(child.ExitCode == 0,
                "pop-out process exits without holding persistent index or job leases");
            await using var reopened = new CommunityTransferManager(multiRoot,credentials);
            Check(reopened.GetJob(parentUpload).State == CommunityTransferState.Completed
                && reopened.GetJob(childUpload).State == CommunityTransferState.Completed,
                "both processes' upload receipts survive independent window exit");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree:true);
                await child.WaitForExitAsync();
            }
            if (child.ExitCode != 0)
            {
                string output=await child.StandardOutput.ReadToEndAsync();
                string errors=await child.StandardError.ReadToEndAsync();
                Console.WriteLine("Secondary process diagnostics: "+output+" "+errors);
            }
        }
    }

    // A deliberately unresponsive loopback HTTP listener keeps a real upload
    // in progress while a second process pauses/resumes or crashes. No external
    // service, privileged IPC, sleeps-as-correctness or proprietary files.
    using (var blackhole = new TcpListener(IPAddress.Loopback,0))
    {
        blackhole.Start();
        string blockedEndpoint = "http://127.0.0.1:"
            + ((IPEndPoint)blackhole.LocalEndpoint).Port + "/";
        using var trapStop = new CancellationTokenSource();
        var clients = new System.Collections.Generic.List<TcpClient>();
        Task accept = Task.Run(async () =>
        {
            try
            {
                while(!trapStop.IsCancellationRequested)
                {
                    TcpClient connection =
                        await blackhole.AcceptTcpClientAsync(trapStop.Token);
                    lock(clients) clients.Add(connection);
                    // Keep the HTTP request open; the worker must terminate
                    // through its cancellation token rather than timeout.
                }
            }
            catch(OperationCanceledException) when(trapStop.IsCancellationRequested) { }
        });
        static async Task Eventually(Func<bool> condition)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while(!condition()) await Task.Delay(50,deadline.Token);
        }
        static bool LocalOwner(CommunityTransferManager manager,Guid id)
        {
            var field=typeof(CommunityTransferManager).GetField(
                "_running",System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            return field?.GetValue(manager) is System.Collections.IDictionary
                owners && owners.Contains(id);
        }
        static Process SpawnBlockedOwner(string queue,string endpoint,
            string package,string token,string receipt)
        {
            var start=new ProcessStartInfo("dotnet")
            {
                UseShellExecute=false,
                RedirectStandardError=true
            };
            foreach(string arg in new[]{
                Assembly.GetExecutingAssembly().Location,
                "--crossprocess-stalled-owner",queue,endpoint,package,token,receipt
            }) start.ArgumentList.Add(arg);
            return Process.Start(start)
                ?? throw new InvalidDataException("Cannot launch crashed-owner fixture.");
        }
        try
        {
            // The child owns the job BEFORE Game opens its transfer manager.
            // Loading a second manager must not relabel this live Uploading
            // record as Retryable, nor try to send a duplicate POST.
            string pauseRoot=Path.Combine(root,"remote-pause");
            string pauseReceipt=Path.Combine(root,"remote-pause-id");
            using (Process child=SpawnBlockedOwner(
                pauseRoot,blockedEndpoint,pkgA,secret,pauseReceipt))
            {
                try
                {
                    await Eventually(()=>File.Exists(pauseReceipt));
                    Guid id=Guid.Parse(File.ReadAllText(pauseReceipt));
                    await using var peer=new CommunityTransferManager(
                        pauseRoot,credentials);
                    await Eventually(()=>peer.GetJob(id).State ==
                        CommunityTransferState.Uploading);
                    Check(!LocalOwner(peer,id),
                        "joining Game process respects still-live Studio upload ownership");
                    peer.Pause(id);
                    await Eventually(()=>peer.GetJob(id).State ==
                        CommunityTransferState.Paused);
                    Check(peer.GetJob(id).State==CommunityTransferState.Paused,
                        "remote Game pause persists atomically into Studio-owned job state");
                    // The secondary worker observes the pause within 500ms and
                    // releases its exclusive lifetime lease before resuming.
                    await Eventually(()=>
                    {
                        try
                        {
                            using var lease=new FileStream(Path.Combine(
                                pauseRoot,"transfer-leases",id.ToString("N")+".job.lock"),
                                FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1);
                            return true;
                        }
                        catch(IOException){return false;}
                    });
                    peer.Resume(id);
                    await Eventually(()=>peer.GetJob(id).State ==
                        CommunityTransferState.Uploading);
                    Check(peer.GetJob(id).State == CommunityTransferState.Uploading,
                        "remote resume claims one new job owner without losing immutable source");
                    peer.Cancel(id);
                    await Eventually(()=>peer.GetJob(id).State ==
                        CommunityTransferState.Cancelled);
                    Check(peer.GetJob(id).State == CommunityTransferState.Cancelled,
                        "remote cancellation remains terminal and cannot publish stale bytes");
                }
                finally
                {
                    if(!child.HasExited)
                    {
                        child.Kill(entireProcessTree:true);
                        await child.WaitForExitAsync();
                    }
                }
            }

            // A true process death must release its OS job lease. An already
            // running sibling automatically observes and safely adopts that
            // orphan; manually paused/cancelled jobs are never adopted.
            string crashRoot=Path.Combine(root,"crashed-owner");
            string crashReceipt=Path.Combine(root,"crashed-owner-id");
            using(Process child=SpawnBlockedOwner(
                crashRoot,blockedEndpoint,pkgB,secret,crashReceipt))
            {
                try
                {
                    await Eventually(()=>File.Exists(crashReceipt));
                    Guid id=Guid.Parse(File.ReadAllText(crashReceipt));
                    await using var peer=new CommunityTransferManager(
                        crashRoot,credentials);
                    await Eventually(()=>peer.GetJob(id).State ==
                        CommunityTransferState.Uploading);
                    Check(!LocalOwner(peer,id),
                        "active sibling process does not steal an owned Studio job");
                    child.Kill(entireProcessTree:true);
                    await child.WaitForExitAsync();
                    await Eventually(()=>LocalOwner(peer,id));
                    Check(LocalOwner(peer,id),
                        "live Game process adopts abandoned transfer only after OS lease release");
                    peer.Cancel(id);
                    Check((await peer.WaitAsync(id)).State ==
                        CommunityTransferState.Cancelled,
                        "adopted crash recovery cancels cleanly without rewriting exact package identity");
                }
                finally
                {
                    if(!child.HasExited)
                    {
                        child.Kill(entireProcessTree:true);
                        await child.WaitForExitAsync();
                    }
                }
            }
        }
        finally
        {
            trapStop.Cancel();
            blackhole.Stop();
            lock(clients) foreach(var connection in clients) connection.Dispose();
            try {await accept;} catch(ObjectDisposedException) {}
        }
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
