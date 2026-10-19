using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// Public receipts are safe UI data. Only the private durable envelope stores the
// independently verified account subject; no bearer/session data is ever used.
string root = Path.Combine(Path.GetTempPath(), "community-account-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine("PASS " + description);
    checks++;
}
static async Task<CommunityTransferJob> Finished(CommunityTransferManager manager, Guid id)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    return await manager.WaitAsync(id, deadline.Token);
}
static async Task Rejected(Func<Task> action)
{
    bool rejected = false;
    try { await action(); }
    catch (InvalidOperationException) { rejected = true; }
    catch (UnauthorizedAccessException) { rejected = true; }
    if (!rejected) throw new InvalidOperationException("Expected account authorization refusal.");
}
try
{
    string package = MapPackageBuilder.Build(MapTemplates.Create("ACCOUNT_BOUND_UPLOAD", true).Definition,
        Path.Combine(root, "source.ppmap"));
    var identity = MapContentIdentity.FromPackage(package);
    string hash = MapBuildFingerprint.HashFile(package);
    long bytes = new FileInfo(package).Length;
    var map = new CommunityMap(hash, identity.MapId, identity.ContentHash.ToString(), identity.RoomKey,
        "Account fixture", "Fixture author", "1", bytes);
    Guid accountA = Guid.Parse("916cafc9-799c-4905-8c87-c7131357430e");
    Guid accountB = Guid.Parse("cc7bd03f-6cb7-427f-b1d8-17115477bd1d");
    var captured = new CommunityTransferAccount(accountA);
    Check(captured.Matches(accountA) && !captured.Matches(accountB),
        "opaque account witness compares only the intended verified subject");
    string carrierJson = JsonSerializer.Serialize(captured, MapPackageReader.JsonOptions);
    Check(!carrierJson.Contains(accountA.ToString(), StringComparison.OrdinalIgnoreCase)
        && !carrierJson.Contains("Subject", StringComparison.OrdinalIgnoreCase)
        && !carrierJson.Contains("Version", StringComparison.OrdinalIgnoreCase),
        "generic account witness JSON contains no private subject or version");
    bool emptyAccountRejected = false;
    try { _ = new CommunityTransferAccount(Guid.Empty); }
    catch (ArgumentException) { emptyAccountRejected = true; }
    Check(emptyAccountRejected, "empty account subject cannot authorize durable uploads");

    await using var service = new UploadService(map);
    var authority = new AccountAuthority(accountA);
    string noAccountDir = Path.Combine(root, "legacy-ticket-only");
    var oldCredentials = new DelegateCommunityCredentialSource(
        _ => Task.FromResult(AccountAuthority.Secret), _ => Task.FromResult(AccountAuthority.Secret));
    await using (var legacyTicketOnly = new CommunityTransferManager(noAccountDir, oldCredentials))
    {
        await Rejected(async () => await legacyTicketOnly.EnqueueUploadAsync(service.Endpoint,
            Path.Combine(root, "not-a-package.ppmap"), null, true, false));
        Check(legacyTicketOnly.ListJobs().Count == 0 && service.RequestCount == 0,
            "ticket-only credential sources cannot silently authorize durable uploads");
    }
    string validDir = Path.Combine(root, "valid");
    Guid validId;
    await using (var manager = new CommunityTransferManager(validDir, authority))
    {
        int completedNotifications = 0;
        manager.Changed += _ => throw new IOException("SYNTHETIC OBSERVER FAILURE");
        manager.Changed += job => { if (job.State == CommunityTransferState.Completed) Interlocked.Increment(ref completedNotifications); };
        validId = await manager.EnqueueUploadAsync(service.Endpoint, package, null, true, false,
            expectedAccount: captured);
        var result = await Finished(manager, validId);
        Check(result.State == CommunityTransferState.Completed && result.PackageHash == hash,
            "matching verified account completes the exact immutable upload");
        Check(!File.Exists(result.SnapshotPath), "verified completion releases its private snapshot");
        Check(completedNotifications == 1, "throwing observer cannot block another subscriber or archive cleanup");
        string receiptJson = JsonSerializer.Serialize(result, MapPackageReader.JsonOptions);
        Check(!receiptJson.Contains(accountA.ToString(), StringComparison.OrdinalIgnoreCase)
            && !receiptJson.Contains(AccountAuthority.Secret, StringComparison.Ordinal)
            && !receiptJson.Contains("attempt", StringComparison.OrdinalIgnoreCase),
            "generic completed receipt serialization contains no private subject, ticket or attempt witness");
        string journal = File.ReadAllText(Path.Combine(validDir, "jobs-v1.json"));
        using var persisted = JsonDocument.Parse(journal);
        var envelope = persisted.RootElement.GetProperty("jobs")[0];
        long receiptAttempt = (long)typeof(CommunityTransferJob).GetProperty("Attempt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
        Check(persisted.RootElement.GetProperty("schema").GetInt32() == 2
            && envelope.GetProperty("accountVersion").GetInt32() == 1
            && envelope.GetProperty("accountSubject").GetGuid() == accountA
            && envelope.GetProperty("attempt").GetInt64() == receiptAttempt
            && envelope.GetProperty("receipt").GetProperty("id").GetGuid() == validId,
            "schema two privately binds the receipt to one verified versioned subject");
        Check(!journal.Contains(AccountAuthority.Secret, StringComparison.Ordinal)
            && !journal.Contains("AccessToken", StringComparison.OrdinalIgnoreCase)
            && !journal.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase),
            "durable journal persists no credentials or session material");
        Check(authority.ExpectedTickets.All(a => a.Matches(accountA)),
            "every original upload ticket uses the admitted account witness");
        var update = typeof(CommunityTransferManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Community terminal update seam is missing.");
        update.Invoke(manager, new object[] { validId,
            (Func<CommunityTransferJob, CommunityTransferJob>)(job => job with { State = CommunityTransferState.Failed, Error = "SYNTHETIC LATE CLEANUP FAILURE" }), true });
        Check(manager.GetJob(validId).State == CommunityTransferState.Completed && manager.GetJob(validId).Error == null,
            "late forced worker failure cannot rewrite an admitted Completed receipt");
        manager.Forget(validId);
        Check(manager.ListJobs().Count == 0, "terminal update guard still allows explicit completed receipt retirement");
    }

    // An actual post-admission File.Delete failure must retain the completed
    // outcome and its private orphan bytes. All permissions/handles are local
    // to this invocation's temporary tree and are restored before cleanup.
    if (OperatingSystem.IsWindows() || Environment.UserName != "root")
    {
        string cleanupDir = Path.Combine(root, "cleanup-failure");
        string uploads = Path.Combine(cleanupDir, "uploads");
        FileStream? denyDelete = null;
        UnixFileMode? savedMode = null;
        await using var cleanup = new CommunityTransferManager(cleanupDir, new AccountAuthority(accountA));
        cleanup.Changed += job =>
        {
            if (job.State != CommunityTransferState.Completed) return;
            if (OperatingSystem.IsWindows())
                denyDelete = new FileStream(job.SnapshotPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
            {
                savedMode = File.GetUnixFileMode(uploads);
                File.SetUnixFileMode(uploads, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
        };
        Guid cleanupId = Guid.Empty;
        try
        {
            cleanupId = await cleanup.EnqueueUploadAsync(service.Endpoint, package, null, true, false, expectedAccount: captured);
            var retained = await Finished(cleanup, cleanupId);
            Check(retained.State == CommunityTransferState.Completed && retained.Error == null && File.Exists(retained.SnapshotPath),
                "actual archive deletion failure preserves Completed and retains bytes for later cleanup");
        }
        finally
        {
            denyDelete?.Dispose();
            if (!OperatingSystem.IsWindows() && savedMode is { } mode) File.SetUnixFileMode(uploads, mode);
        }
        cleanup.Forget(cleanupId);
        Check(cleanup.ListJobs().Count == 0 && Directory.GetFiles(uploads).Length == 0,
            "explicit Forget can clean retained completed archive after filesystem access recovers");
    }

    // This source does not exist. The identity check must fail before opening or
    // copying package bytes, rather than discovering the missing file first.
    authority.Current = accountB;
    string refusalDir = Path.Combine(root, "admission-refusal");
    int requestsBefore = service.RequestCount;
    await using (var manager = new CommunityTransferManager(refusalDir, authority))
    {
        await Rejected(async () => await manager.EnqueueUploadAsync(service.Endpoint,
            Path.Combine(root, "not-a-package.ppmap"), null, true, false, expectedAccount: captured));
        Check(manager.ListJobs().Count == 0 && Directory.GetFiles(Path.Combine(refusalDir, "uploads")).Length == 0,
            "account switch before admission rejects before any package copy or receipt");
        Check(service.RequestCount == requestsBefore, "rejected admission performs no HTTP request");
    }

    authority.Current = accountA;
    authority.BeforeVerify = count => { if (count == 2) authority.Current = accountB; };
    authority.ResetCounts();
    string buildSwitchDir = Path.Combine(root, "copy-switch");
    await using (var manager = new CommunityTransferManager(buildSwitchDir, authority))
    {
        await Rejected(async () => await manager.EnqueueUploadAsync(service.Endpoint, package,
            null, true, false, expectedAccount: captured));
        Check(manager.ListJobs().Count == 0
            && Directory.GetFiles(Path.Combine(buildSwitchDir, "uploads")).Length == 0,
            "account revalidation after frozen package preparation rejects a changed account");
    }
    authority.BeforeVerify = null;

    // A 401 forces the facade's refresh lane. Switch only after the original
    // authenticated request reached the server, before a retry ticket is minted.
    authority.Current = accountA;
    authority.ResetCounts();
    service.UnauthorizedOnce = () => authority.Current = accountB;
    string retryDir = Path.Combine(root, "retry-switch");
    await using (var manager = new CommunityTransferManager(retryDir, authority))
    {
        int uploadsBefore = service.UploadCount;
        Guid id = await manager.EnqueueUploadAsync(service.Endpoint, package, null, true, false,
            expectedAccount: captured);
        var result = await Finished(manager, id);
        Check(result.State == CommunityTransferState.NeedsAuthorization && File.Exists(result.SnapshotPath),
            "account switch before retry ticket retains bytes and refuses completion");
        Check(authority.RefreshCalls == 0 && authority.ExpectedTickets.Count == 1
            && service.UploadCount == uploadsBefore,
            "401 retry refuses changed account before minting a replacement ticket");
        Check(authority.ExpectedTickets.All(a => a.Matches(accountA)),
            "refresh never substitutes the newly current account witness");
        Check(!JsonSerializer.Serialize(result, MapPackageReader.JsonOptions)
            .Contains(accountB.ToString(), StringComparison.OrdinalIgnoreCase),
            "failed retry receipt does not disclose the replacement account");
    }

    // The response may confirm server acceptance while the local account has
    // changed. The durable owner must retain the archive and refuse Completed.
    authority.Current = accountA;
    authority.ResetCounts();
    service.HoldNextUpload();
    string inFlightDir = Path.Combine(root, "response-switch");
    Guid heldId;
    await using (var manager = new CommunityTransferManager(inFlightDir, authority))
    {
        heldId = await manager.EnqueueUploadAsync(service.Endpoint, package, null, true, false,
            expectedAccount: captured);
        await service.UploadArrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        authority.Current = accountB;
        service.ReleaseUpload.TrySetResult();
        var result = await Finished(manager, heldId);
        Check(result.State == CommunityTransferState.NeedsAuthorization && File.Exists(result.SnapshotPath),
            "account switch during accepted HTTP response cannot complete or delete frozen bytes");
        Check(MapBuildFingerprint.HashFile(result.SnapshotPath!) == hash,
            "unconfirmed accepted upload retains exact immutable snapshot");
        Check(authority.ExpectedTickets.All(a => a.Matches(accountA)),
            "in-flight upload remains bound to the original account witness");
        await Rejected(() => manager.ReauthorizeUploadAsync(heldId, new CommunityTransferAccount(accountB)));
        Check(manager.GetJob(heldId).State == CommunityTransferState.NeedsAuthorization,
            "explicit reauthorization cannot rebind an existing upload to another account");
    }
    var freshAuthority = new AccountAuthority(accountA);
    await using (var resumed = new CommunityTransferManager(inFlightDir, freshAuthority))
    {
        int before = service.RequestCount;
        await resumed.ReauthorizeUploadAsync(heldId, new CommunityTransferAccount(accountA));
        Check(resumed.GetJob(heldId).State == CommunityTransferState.Paused
            && freshAuthority.ExpectedTickets.Count == 0 && service.RequestCount == before,
            "same-account explicit reauthorization pauses without ticket acquisition or network");
        resumed.Resume(heldId);
        var result = await Finished(resumed, heldId);
        Check(result.State == CommunityTransferState.Completed && result.PackageHash == hash,
            "fresh independently verified same-account session can resume bound immutable work");
        Check(freshAuthority.VerifyCalls > 0 && freshAuthority.ExpectedTickets.All(a => a.Matches(accountA)),
            "restart reacquires account verification without persisted credentials");
    }

    // Old queues have no reliable owner witness. They remain inspectable but an
    // unfinished upload cannot silently acquire whichever account is current.
    string migrationDir = Path.Combine(root, "schema-one");
    Directory.CreateDirectory(Path.Combine(migrationDir, "uploads"));
    var legacy = Enum.GetValues<CommunityTransferState>()
        .Where(s => s != CommunityTransferState.NeedsAuthorization)
        .Select(s => {
            Guid id = Guid.NewGuid();
            string snapshot = Path.Combine(migrationDir, "uploads", id.ToString("N") + ".ppmap");
            File.Copy(package, snapshot);
            return new CommunityTransferJob(id, CommunityTransferKind.Upload, s, service.Endpoint,
                hash, bytes, 0, DateTimeOffset.UtcNow, SnapshotPath: snapshot);
        }).ToArray();
    var download = new CommunityTransferJob(Guid.NewGuid(), CommunityTransferKind.Download,
        CommunityTransferState.Completed, service.Endpoint, hash, bytes, bytes,
        DateTimeOffset.UtcNow, Download: map);
    File.WriteAllText(Path.Combine(migrationDir, "jobs-v1.json"), JsonSerializer.Serialize(
        new { Schema = 1, Jobs = legacy.Append(download).ToArray() }, MapPackageReader.JsonOptions));
    authority.Current = accountB;
    authority.ResetCounts();
    await using (var migrated = new CommunityTransferManager(migrationDir, authority))
    {
        Check(migrated.ListJobs().Count == legacy.Length + 1,
            "schema one migration preserves every transfer identity");
        foreach (var original in legacy)
        {
            var result = migrated.GetJob(original.Id);
            bool terminal = original.State is CommunityTransferState.Completed or CommunityTransferState.Cancelled;
            Check(result.State == (terminal ? original.State : CommunityTransferState.NeedsAuthorization)
                && result.PackageHash == original.PackageHash && result.SnapshotPath == original.SnapshotPath
                && File.Exists(result.SnapshotPath),
                "legacy " + original.State + " upload preserves bytes with safe authorization state");
            if (!terminal)
                await Rejected(() => { migrated.Resume(original.Id); return Task.CompletedTask; });
        }
        Check(migrated.GetJob(download.Id).State == download.State
            && migrated.GetJob(download.Id).PackageHash == download.PackageHash
            && migrated.GetJob(download.Id).Download?.MapId == download.Download?.MapId,
            "schema migration preserves unrelated completed download receipt");
        int before = service.RequestCount;
        migrated.StartPending();
        Check(authority.VerifyCalls == 0 && service.RequestCount == before,
            "legacy unauthorized uploads never auto-adopt the current account");
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(migrationDir, "jobs-v1.json")));
        Check(index.RootElement.GetProperty("schema").GetInt32() == 2,
            "legacy receipt migration is durably rewritten as schema two");
        Check(migrated.ListJobs().All(j => !JsonSerializer.Serialize(j, MapPackageReader.JsonOptions)
            .Contains(accountB.ToString(), StringComparison.OrdinalIgnoreCase)),
            "migrated public receipts disclose no current account identity");

        var adopt = legacy.First(j => j.State == CommunityTransferState.Queued);
        await Rejected(() => migrated.ReauthorizeUploadAsync(adopt.Id, captured));
        Check(migrated.GetJob(adopt.Id).State == CommunityTransferState.NeedsAuthorization,
            "unbound historical upload requires independently verified explicit account");
        File.WriteAllBytes(adopt.SnapshotPath!, [1, 2, 3, 4]);
        bool corruptRejected = false;
        try { await migrated.ReauthorizeUploadAsync(adopt.Id, new CommunityTransferAccount(accountB)); }
        catch (InvalidDataException) { corruptRejected = true; }
        Check(corruptRejected && migrated.GetJob(adopt.Id).State == CommunityTransferState.NeedsAuthorization,
            "historical account adoption rejects changed frozen package bytes");
        File.Copy(package, adopt.SnapshotPath!, true);
        before = service.RequestCount;
        await migrated.ReauthorizeUploadAsync(adopt.Id, new CommunityTransferAccount(accountB));
        Check(migrated.GetJob(adopt.Id).State == CommunityTransferState.Paused
            && service.RequestCount == before,
            "explicit historical adoption binds verified account and leaves upload paused");
        migrated.Resume(adopt.Id);
        Check((await Finished(migrated, adopt.Id)).State == CommunityTransferState.Completed,
            "separate explicit resume publishes reauthorized historical immutable bytes");
    }
    Console.WriteLine($"Community transfer account checks: {checks} PASS.");
}
finally
{
    try { Directory.Delete(root, true); }
    catch (IOException) { }
}

internal sealed class AccountAuthority(Guid current) : ICommunityAccountCredentialSource
{
    public const string Secret = "account-bound-loopback-fixture-ticket";
    private readonly object _gate = new();
    private Guid _current = current;
    public Guid Current { get { lock (_gate) return _current; } set { lock (_gate) _current = value; } }
    public int VerifyCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public Action<int>? BeforeVerify { get; set; }
    public List<CommunityTransferAccount> ExpectedTickets { get; } = [];
    public void ResetCounts() { VerifyCalls = 0; RefreshCalls = 0; ExpectedTickets.Clear(); }
    public Task<CommunityTransferAccount> VerifyAccountAsync(CommunityTransferAccount? expected,
        CancellationToken cancellation)
    {
        lock (_gate)
        {
            cancellation.ThrowIfCancellationRequested();
            VerifyCalls++;
            BeforeVerify?.Invoke(VerifyCalls);
            if (expected != null && !expected.Matches(_current))
                throw new CommunityTransferAuthorizationException();
            return Task.FromResult(new CommunityTransferAccount(_current));
        }
    }
    public Task CommitForAccountAsync(CommunityTransferAccount expected, Action commit,
        CancellationToken cancellation)
    {
        lock (_gate)
        {
            VerifyAccountAsync(expected, cancellation).GetAwaiter().GetResult();
            commit();
            return Task.CompletedTask;
        }
    }
    public async Task<string> GetTicketForAccountAsync(CommunityTransferAccount expected,
        bool refresh, CancellationToken cancellation)
    {
        ExpectedTickets.Add(expected);
        if (refresh) RefreshCalls++;
        await VerifyAccountAsync(expected, cancellation);
        return Secret;
    }
    public async Task<string> GetTicketAsync(CancellationToken cancellation)
        => await GetTicketForAccountAsync(await VerifyAccountAsync(null, cancellation), false, cancellation);
    public async Task<string> RefreshTicketAsync(CancellationToken cancellation)
        => await GetTicketForAccountAsync(await VerifyAccountAsync(null, cancellation), true, cancellation);
}

internal sealed class UploadService : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly CommunityMap _map;
    private bool _hold;
    private int _requests, _uploads;
    public string Endpoint { get; }
    public int RequestCount => Volatile.Read(ref _requests);
    public int UploadCount => Volatile.Read(ref _uploads);
    public Action? UnauthorizedOnce { get; set; }
    public TaskCompletionSource UploadArrived { get; private set; } = NewSignal();
    public TaskCompletionSource ReleaseUpload { get; private set; } = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public UploadService(CommunityMap map)
    {
        _map = map;
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Endpoint = "http://127.0.0.1:" + ((IPEndPoint)probe.LocalEndpoint).Port + "/";
        probe.Stop();
        _listener.Prefixes.Add(Endpoint);
        _listener.Start();
        _serve = Task.Run(Serve);
    }
    public void HoldNextUpload()
    {
        UploadArrived = NewSignal(); ReleaseUpload = NewSignal(); _hold = true;
    }
    private async Task Serve()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                Interlocked.Increment(ref _requests);
                string path = context.Request.Url!.AbsolutePath;
                if (context.Request.HttpMethod == "GET" && path == "/maps")
                {
                    if (UnauthorizedOnce is { } refused)
                    {
                        UnauthorizedOnce = null; refused(); context.Response.StatusCode = 401;
                    }
                    await Respond(context, "{}");
                }
                else if (context.Request.HttpMethod == "POST" && path.StartsWith("/uploads/"))
                {
                    await context.Request.InputStream.CopyToAsync(Stream.Null, _stop.Token);
                    context.Response.StatusCode = 404;
                    await Respond(context, "{}");
                }
                else if (context.Request.HttpMethod == "POST" && path == "/maps")
                {
                    await context.Request.InputStream.CopyToAsync(Stream.Null, _stop.Token);
                    Interlocked.Increment(ref _uploads);
                    if (_hold)
                    {
                        _hold = false; UploadArrived.TrySetResult();
                        await ReleaseUpload.Task.WaitAsync(_stop.Token);
                    }
                    await Respond(context, JsonSerializer.Serialize(_map, MapPackageReader.JsonOptions));
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await Respond(context, "{}");
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (HttpListenerException) when (_stop.IsCancellationRequested) { }
    }
    private static async Task Respond(HttpListenerContext context, string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = data.Length;
        await context.Response.OutputStream.WriteAsync(data);
        context.Response.Close();
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Close();
        await _serve; _stop.Dispose();
    }
}
