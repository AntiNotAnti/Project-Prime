using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>Small self-hosted map library; place behind HTTPS for public use.
/// Upload credentials come from the environment, never from command-line arguments.</summary>
public static class MapCommunityServer
{
    private sealed record MapUploadMetadata(
        string CreatorId, string PackageHash, long Bytes, bool Listed, bool Draft)
    {
        public Guid? MapId { get; init; }
        public bool? ExistingMap { get; init; }
        public string? ExpectedParentHash { get; init; }
        public string? ReleaseNotes { get; init; }
        public bool AllowStaleParent { get; init; }
    }

    private sealed record PublishResult(
        int Status,
        CommunityMap? Entry = null,
        CommunityRevisionConflict? Conflict = null);
    private const long DefaultPublishedStorageBytes = 50L * 1024 * 1024 * 1024;
    private static readonly TimeSpan PartialUploadRetention = TimeSpan.FromHours(24);
    internal static readonly TimeSpan CommunityDeleteRetention = TimeSpan.FromDays(30);

    public static void Run(string prefix, string storage)
    {
        string secret = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_UPLOAD_TOKEN") ?? "";
        if (secret.Length < 24) throw new InvalidOperationException("Set PROJECT_PRIME_MAP_UPLOAD_TOKEN to a random token of at least 24 characters.");
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try { ServeAsync(prefix, storage, secret, stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; }
    }
    public static async Task ServeAsync(string prefix, string storage, string secret, CancellationToken token)
    {
        if (secret.Length < 24) throw new ArgumentException("Upload token must have at least 24 characters.");
        Directory.CreateDirectory(storage);
        CleanupStaleUploads(storage);
        long publishedStorageLimit = PublishedStorageLimit();
        var catalog = new MapCreatorCatalog(storage,secret);
        using var identities = new MapCommunityIdentityVerifier();
        var maps = new System.Collections.Concurrent.ConcurrentDictionary<string, CommunityMap>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(storage, "*.ppmap").Take(2000))
        {
            try { var entry = Inspect(file); if(Path.GetFileNameWithoutExtension(file)!=entry.Hash)throw new InvalidDataException("Package filename must match its archive hash."); string metadata = Path.Combine(storage, entry.Hash + ".catalog.json");
                if (File.Exists(metadata) && new FileInfo(metadata).Length <= 65536)
                {
                    var saved = JsonSerializer.Deserialize<CommunityMap>(File.ReadAllText(metadata), MapPackageReader.JsonOptions);
                    if (saved?.Hash == entry.Hash) entry = entry with { Listed = saved.Listed, PublishedAt = saved.PublishedAt, OwnerId = saved.OwnerId, Draft = saved.Draft };
                }
                maps.TryAdd(entry.Hash, entry); }
            catch (Exception ex) { Console.Error.WriteLine("[maphub] Skipped package: " + ex.Message); }
        }
        var revisionCatalog = new MapCommunityRevisionCatalog(storage, maps.Values);
        // Non-map v3 metadata is deliberately separate. Maps are projected
        // from the existing v2 revision catalog on every read, never dual-written.
        var contentRegistry = new CommunityContentRegistry(storage);
        // CDN exposure is explicit operator configuration. No draft content is
        // mirrored or given a globally readable URL.
        bool mirrorEnabled = CommunityCutoverPolicy.ObjectMirrorEnabled;
        var objectStore = new CommunityObjectStore(storage, mirrorEnabled
            ? Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_CDN_BASE")
            : null);
        long publishedBytes = maps.Values.Sum(m => m.Bytes);
        DateTimeOffset nextLifecycleSweep = DateTimeOffset.UtcNow.AddHours(1);
        object lifecycleSweepGate = new();
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix.TrimEnd('/') + "/"); listener.Start();
        using var registration = token.Register(listener.Close);
        Console.WriteLine("[maphub] Listening at " + prefix);
        // A separate metadata lane stays responsive when clients hold large
        // HTTP bodies open. Admission never builds an unbounded task queue.
        using var admission = MapCommunityServiceAdmission.FromEnvironment();
        using var publication = new SemaphoreSlim(1);
        var uploadLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        var running = new List<Task>();
        using var mirrorLane = new SemaphoreSlim(1);
        var mirrorTasks = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task>>(
            StringComparer.Ordinal);
        long completedDownloadBytes = 0;

        // A public package mirror is a non-authoritative optimization. Queue
        // one checksum-verified copy per immutable SHA, never in the upload or
        // metadata admission lane. Origin remains usable throughout failure,
        // cancellation, slow storage and initial backfill.
        void QueuePublicMirror(CommunityMap map)
        {
            if (!mirrorEnabled || !map.Listed || map.Draft
                || revisionCatalog.IsProjectDeleted(map.MapId)
                || !revisionCatalog.IsProjectDiscoverable(map.MapId)
                || revisionCatalog.IsRevisionDeleted(map.Hash))
                return;

            // Publishing threads race with request readers. Lazily create one
            // task only after the concurrent dictionary owns its key. That
            // avoids retaining a completed placeholder or double-mirroring
            // a hash when the first copy finishes quickly.
            var proposed = new Lazy<Task>(() => Task.Run(async () =>
            {
                try
                {
                    await mirrorLane.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (!maps.TryGetValue(map.Hash, out var current)
                            || !current.Listed || current.Draft
                            || revisionCatalog.IsProjectDeleted(current.MapId)
                            || !revisionCatalog.IsProjectDiscoverable(current.MapId)
                            || revisionCatalog.IsRevisionDeleted(current.Hash))
                            return;
                        objectStore.MirrorPublic(
                            Path.Combine(storage, current.Hash + ".ppmap"),
                            current.Hash, current.Bytes);
                    }
                    finally { mirrorLane.Release(); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or InvalidDataException)
                {
                    Console.Error.WriteLine("[maphub] Optional public map mirror "
                        + map.Hash[..8] + " unavailable: " + ex.Message
                        + ". Origin remains authoritative.");
                }
                finally { mirrorTasks.TryRemove(map.Hash, out _); }
            }), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<Task> claimed = mirrorTasks.GetOrAdd(map.Hash, proposed);
            if (ReferenceEquals(claimed, proposed)) _ = claimed.Value;
        }

        // Reconcile once at startup, after abandoned upload cleanup. Full
        // expected bytes are reserved by upload session rather than rescanning
        // all .part files before every 50 MiB chunk.
        var retained = new List<(string Key, string Creator, long Bytes)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string metadata in Directory.EnumerateFiles(storage, "upload-*.json"))
        {
            try
            {
                if (new FileInfo(metadata).Length > 65536) continue;
                var state = JsonSerializer.Deserialize<MapUploadMetadata>(
                    File.ReadAllText(metadata), MapPackageReader.JsonOptions);
                if (state == null || state.Bytes is <= 0 or > MapPackageReader.MaxArchiveBytes
                    || string.IsNullOrWhiteSpace(state.CreatorId)) continue;
                string key = Path.GetFileNameWithoutExtension(metadata)["upload-".Length..];
                if (!MapCommunityClient.ValidHash(key)) continue;
                retained.Add((key, state.CreatorId, state.Bytes));
                keys.Add(key);
            }
            catch (Exception ex) when (ex is IOException or JsonException
                or UnauthorizedAccessException) { }
        }
        long orphanPartialBytes = 0;
        foreach (string part in Directory.EnumerateFiles(storage, "upload-*.part"))
        {
            string key = Path.GetFileNameWithoutExtension(part)["upload-".Length..];
            if (keys.Contains(key)) continue;
            try { orphanPartialBytes += new FileInfo(part).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var uploadQuota = new MapCommunityUploadReservations(retained, orphanPartialBytes);
        // Lifecycle purge can remove resumable uploads. Initialize the ledger
        // first so each purge releases the corresponding reservation safely.
        PurgeExpiredLifecycle();
        string requestRoot = new Uri(prefix).AbsolutePath.TrimEnd('/');
        try
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) when (token.IsCancellationRequested) { break; }
                var lane = MapCommunityServiceAdmission.Classify(context.Request, requestRoot);
                if (!admission.TryEnter(lane, context.Request.RemoteEndPoint?.Address,
                    out IDisposable? lease, out var refusal))
                {
                    try
                    {
                        context.Response.StatusCode = refusal
                            == MapCommunityServiceAdmission.Refusal.RateLimited ? 429 : 503;
                        context.Response.Headers["Retry-After"] = "1";
                        context.Response.Close();
                    }
                    catch (Exception ex) when (ex is IOException or HttpListenerException
                        or ObjectDisposedException) { }
                    continue;
                }
                running.RemoveAll(t => t.IsCompleted);
                running.Add(Task.Run(async () =>
                {
                    using (lease) await Handle(context).ConfigureAwait(false);
                }));
            }
        }
        finally
        {
            await Task.WhenAll(running).ConfigureAwait(false);
            await Task.WhenAll(mirrorTasks.Values.Select(x => x.Value)).ConfigureAwait(false);
        }

        string UploadKey(string creatorId, string hash)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(creatorId + "\n" + hash))).ToLowerInvariant();
        string UploadMetadataPath(string key) => Path.Combine(storage, "upload-" + key + ".json");
        string UploadPartPath(string key) => Path.Combine(storage, "upload-" + key + ".part");
        MapUploadMetadata? LoadUpload(string key)
        {
            string path = UploadMetadataPath(key);
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            return JsonSerializer.Deserialize<MapUploadMetadata>(File.ReadAllText(path), MapPackageReader.JsonOptions);
        }
        void SaveUpload(string key, MapUploadMetadata upload)
            => AtomicFile.Write(UploadMetadataPath(key), JsonSerializer.SerializeToUtf8Bytes(upload, MapPackageReader.JsonOptions));
        void DeleteUpload(string key)
        {
            string metadata = UploadMetadataPath(key), part = UploadPartPath(key);
            if (File.Exists(metadata)) File.Delete(metadata);
            if (File.Exists(part)) File.Delete(part);
            uploadQuota.Release(key);
        }
        MapUploadState UploadState(string key, MapUploadMetadata upload)
        {
            string part = UploadPartPath(key);
            long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset < 0 || offset > upload.Bytes) throw new InvalidDataException("Partial upload has an invalid size.");
            return new(upload.PackageHash, upload.Bytes, offset, MapCommunityClient.UploadChunkBytes, false);
        }
        int PurgeLifecycleHashes(IEnumerable<string> requested)
        {
            string[] hashes = requested.Where(MapCommunityClient.ValidHash)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (hashes.Length == 0) return 0;

            var removed = new List<string>();
            var mapIds = new HashSet<Guid>();
            foreach (string hash in hashes)
            {
                if (maps.TryGetValue(hash, out var entry)) mapIds.Add(entry.MapId);
                string packagePath = Path.Combine(storage, hash + ".ppmap");
                try
                {
                    if (File.Exists(packagePath)) File.Delete(packagePath);
                    if (File.Exists(packagePath)) continue;
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine("[maphub] Deferred lifecycle purge for "
                        + hash[..8] + ": " + ex.Message);
                    continue;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.Error.WriteLine("[maphub] Deferred lifecycle purge for "
                        + hash[..8] + ": " + ex.Message);
                    continue;
                }

                // The archive is the authoritative retained object. Once it is
                // gone, remove the live catalog entry even if cleaning the tiny
                // sidecar has to be retried later.
                if (maps.TryRemove(hash, out var removedEntry))
                    publishedBytes -= removedEntry.Bytes;
                removed.Add(hash);
                string metadataPath = Path.Combine(storage, hash + ".catalog.json");
                try
                {
                    if (File.Exists(metadataPath)) File.Delete(metadataPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("[maphub] Orphaned catalog sidecar for "
                        + hash[..8] + ": " + ex.Message);
                }
            }

            if (removed.Count == 0) return 0;

            var removedSet = removed.ToHashSet(StringComparer.Ordinal);
            foreach (string metadata in Directory.EnumerateFiles(storage, "upload-*.json"))
            {
                try
                {
                    if (new FileInfo(metadata).Length > 65536) continue;
                    var upload = JsonSerializer.Deserialize<MapUploadMetadata>(
                        File.ReadAllBytes(metadata), MapPackageReader.JsonOptions);
                    if (upload == null || !removedSet.Contains(upload.PackageHash)) continue;
                    string key = Path.GetFileNameWithoutExtension(metadata)["upload-".Length..];
                    DeleteUpload(key);
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    Console.Error.WriteLine("[maphub] Skipped pending-upload cleanup: "
                        + ex.Message);
                }
            }

            revisionCatalog.RemovePurgedHashes(removed, maps.Values);
            foreach (Guid mapId in mapIds)
            {
                if (!maps.Values.Any(m => m.MapId == mapId))
                    catalog.PurgeMapData(mapId);
            }
            return removed.Count;
        }

        int PurgeExpiredLifecycle()
            => PurgeLifecycleHashes(
                revisionCatalog.PurgeCandidates(DateTimeOffset.UtcNow));

        async Task SweepLifecycleIfDue(CancellationToken sweepToken)
        {
            bool due;
            lock (lifecycleSweepGate)
            {
                due = DateTimeOffset.UtcNow >= nextLifecycleSweep;
                if (due) nextLifecycleSweep = DateTimeOffset.UtcNow.AddHours(1);
            }
            if (!due) return;
            await publication.WaitAsync(sweepToken);
            try { PurgeExpiredLifecycle(); }
            finally { publication.Release(); }
        }

        CommunityMapProject? ManagedProject(Guid mapId, MapCreatorCredential viewer,
            bool includeDeleted = true)
        {
            CommunityMap[] visible = maps.Values.Where(m => m.MapId == mapId)
                .Select(m => catalog.Decorate(m, viewer.CreatorId)).ToArray();
            bool owner = visible.Any(m => m.OwnerId == viewer.CreatorId);
            return revisionCatalog.BuildProject(mapId, visible,
                revealCreatorIdentity: true, includeDeleted: includeDeleted,
                canManageLifecycle: owner);
        }

        (int Status, CommunityRevisionConflict? Conflict) ValidateRevisionIntent(
            Guid? mapId, bool? existingMap, string? expectedParentHash,
            bool allowStaleParent, MapCreatorCredential creator, bool resumeAvailable)
        {
            // No MapId means an older client. Preserve its historical behavior.
            if (mapId == null)
            {
                if (existingMap != null || expectedParentHash != null || allowStaleParent)
                    return (400, null);
                return (0, null);
            }
            if (mapId == Guid.Empty || existingMap == null)
                return (400, null);
            if (expectedParentHash != null && !MapCommunityClient.ValidHash(expectedParentHash))
                return (400, null);

            CommunityMap? owned = maps.Values.FirstOrDefault(m => m.MapId == mapId.Value);
            bool exists = owned != null;
            if (exists && !catalog.CanPublish(creator.CreatorId, owned!))
                return (403, null);

            string? latest = revisionCatalog.LatestHash(mapId.Value);
            string? currentHash = revisionCatalog.CurrentHash(mapId.Value);
            int? latestRevision = revisionCatalog.LatestRevisionNumber(mapId.Value);

            CommunityRevisionConflict Conflict(string code, string message)
                => new(code, mapId.Value, expectedParentHash, latest, currentHash,
                    latestRevision, resumeAvailable, message);

            if (exists && revisionCatalog.IsProjectDeleted(mapId.Value))
                return (409, Conflict("map_deleted",
                    "This map is in Deleted Maps. Restore it before publishing another revision."));

            if (existingMap == false)
            {
                if (exists)
                    return (409, Conflict("map_already_exists",
                        "This project is already published. Refresh My Maps and upload it as a revision."));
                if (expectedParentHash != null)
                    return (400, null);
                return (0, null);
            }

            if (!exists || latest == null)
                return (409, Conflict("map_not_found",
                    "This revision target no longer exists. Refresh My Maps before publishing."));
            if (expectedParentHash == null)
                return (400, null);
            if (!revisionCatalog.ContainsRevision(mapId.Value, expectedParentHash))
                return (409, Conflict("unknown_parent",
                    "The revision you edited from is no longer part of this map's history."));
            if (!allowStaleParent
                && !latest.Equals(expectedParentHash, StringComparison.Ordinal))
                return (409, Conflict("stale_parent",
                    "A newer revision was published while you were working. Review it before publishing, or explicitly publish your revision anyway."));
            return (0, null);
        }

        PublishResult PublishTemporary(string temporary, MapCreatorCredential creator,
            bool listed, bool draft, Guid? expectedMapId = null,
            MapUploadMetadata? revisionUpload = null)
        {
            var entry = Inspect(temporary) with
            {
                Listed = listed && !draft,
                Draft = draft,
                OwnerId = creator.CreatorId,
                PublishedAt = DateTimeOffset.UtcNow
            };
            if (expectedMapId is Guid required && required != entry.MapId)
                return new PublishResult(400);
            if (revisionUpload?.MapId is Guid uploadMapId && uploadMapId != entry.MapId)
                return new PublishResult(400);

            if (revisionUpload != null)
            {
                var intent = ValidateRevisionIntent(
                    revisionUpload.MapId, revisionUpload.ExistingMap,
                    revisionUpload.ExpectedParentHash, revisionUpload.AllowStaleParent,
                    creator, resumeAvailable: true);
                if (intent.Status != 0)
                    return new PublishResult(intent.Status, Conflict: intent.Conflict);
            }

            var owner = maps.Values.FirstOrDefault(m => m.MapId == entry.MapId);
            if (owner != null && revisionCatalog.IsProjectDeleted(entry.MapId))
                return new PublishResult(409,
                    Conflict: revisionUpload == null ? null : new CommunityRevisionConflict(
                        "map_deleted", entry.MapId, revisionUpload.ExpectedParentHash,
                        revisionCatalog.LatestHash(entry.MapId),
                        revisionCatalog.CurrentHash(entry.MapId),
                        revisionCatalog.LatestRevisionNumber(entry.MapId),
                        false,
                        "This map is in Deleted Maps. Restore it before publishing."));
            if (owner != null && !catalog.CanPublish(creator.CreatorId, owner))
                return new PublishResult(403);
            if (owner != null) entry = entry with { OwnerId = owner.OwnerId };
            if (maps.TryGetValue(entry.Hash, out var existing)) entry = existing;

            // Keep the old human-version uniqueness rule for legacy clients.
            // v2 revisions have a server-assigned revision number, so creators
            // no longer need to mutate the project's display Version to publish.
            if (revisionUpload?.ExistingMap == null
                && maps.Values.Any(m => m.MapId == entry.MapId
                    && m.Version == entry.Version && m.Hash != entry.Hash))
                return new PublishResult(409);

            bool newPackage = !maps.ContainsKey(entry.Hash);
            if (newPackage && (maps.Count >= 2000
                || entry.Bytes > publishedStorageLimit - publishedBytes))
                return new PublishResult(507);
            string destination = Path.Combine(storage, entry.Hash + ".ppmap");
            AtomicFile.Write(Path.Combine(storage, entry.Hash + ".catalog.json"),
                JsonSerializer.SerializeToUtf8Bytes(entry, MapPackageReader.JsonOptions));
            if (!File.Exists(destination)) File.Move(temporary, destination);
            maps[entry.Hash] = entry;
            if (newPackage) publishedBytes += entry.Bytes;
            revisionCatalog.Register(entry, creator.CreatorId,
                revisionUpload?.ExpectedParentHash,
                revisionUpload?.ReleaseNotes,
                useProvidedParent: revisionUpload?.ExistingMap != null);
            QueuePublicMirror(entry);
            return new PublishResult(201, entry);
        }

        // Conditional ETags are calculated on the exact authorized JSON
        // representation. Private creator/favorite responses cannot be cached
        // as public results or mixed with another viewer's permissions.
        async Task CatalogJson(HttpListenerContext request, object value,
            CancellationToken cancellation, bool privateResult)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, MapPackageReader.JsonOptions);
            string etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "\"";
            request.Response.Headers["ETag"] = etag;
            request.Response.Headers["Cache-Control"] = privateResult
                ? "private, max-age=0, must-revalidate"
                : "public, max-age=0, must-revalidate";
            if (request.Request.Headers["If-None-Match"] == etag)
            {
                request.Response.StatusCode = 304;
                return;
            }
            request.Response.ContentType = "application/json";
            request.Response.ContentLength64 = bytes.Length;
            await request.Response.OutputStream.WriteAsync(bytes, cancellation);
        }

        async Task Handle(HttpListenerContext context)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                await SweepLifecycleIfDue(deadline.Token);
                string route = context.Request.Url!.AbsolutePath.TrimEnd('/');
                string root = new Uri(prefix).AbsolutePath.TrimEnd('/');
                string? authorization=context.Request.Headers["Authorization"];
                var creator=catalog.Authenticate(authorization)
                    ?? await identities.AuthenticateAsync(authorization,deadline.Token).ConfigureAwait(false);
                string[] parts=route[(root.Length+1)..].Split('/');
                if (parts.Length > 0 && parts[0] == "uploads") deadline.CancelAfter(TimeSpan.FromMinutes(10));
                else if (context.Request.HttpMethod == "GET" && parts.Length > 1
                    && parts[0] is "maps" or "packages") deadline.CancelAfter(TimeSpan.FromMinutes(15));
                async Task<T> Body<T>()
                {using var data=new MemoryStream();await MapCommunityClient.CopyBoundedAsync(context.Request.InputStream,data,16384,deadline.Token);return JsonSerializer.Deserialize<T>(data.ToArray(),MapPackageReader.JsonOptions)??throw new InvalidDataException("Missing request body.");}

                if (parts.Length == 4 && parts[0] == "v3"
                    && parts[1] == "distribution" && parts[2] == "maps"
                    && context.Request.HttpMethod == "GET")
                {
                    string hash = parts[3];
                    if (!MapCommunityClient.ValidHash(hash)
                        || !maps.TryGetValue(hash, out var distributed)
                        || !distributed.Listed || distributed.Draft
                        || !revisionCatalog.IsProjectDiscoverable(distributed.MapId)
                        || revisionCatalog.IsRevisionDeleted(hash))
                    { context.Response.StatusCode = 404; return; }

                    // Metadata does not bypass the origin: if no verified
                    // mirrored immutable object exists, advertise origin only.
                    Uri? cdn = mirrorEnabled
                        ? objectStore.PublicLocation(hash, distributed.Bytes, published: true)
                        : null;
                    if (cdn == null) QueuePublicMirror(distributed);
                    await Json(context.Response, new CommunityPackageDistribution(
                        1, hash, distributed.Bytes,
                        "packages/" + hash, cdn?.ToString(),
                        CdnEnabled: cdn != null), deadline.Token);
                    return;
                }
                if (route == root + "/v3/capabilities" && context.Request.HttpMethod == "GET")
                {
                    await Json(context.Response, new MapCommunityServiceCapabilities
                    {
                        PublishedStorageLimitBytes = publishedStorageLimit
                    }, deadline.Token);
                    return;
                }
                // Typed upload is separate from legacy .ppmap publication.
                // Request metadata is bounded and authenticated before a byte
                // is written. The server derives OwnerId from the ticket.
                if (parts.Length == 5 && parts[0] == "v3"
                    && parts[1] == "content" && parts[4] == "revisions"
                    && context.Request.HttpMethod == "POST")
                {
                    if (creator == null) { context.Response.StatusCode = 401; return; }
                    if (!Guid.TryParse(parts[3], out Guid assetId))
                    { context.Response.StatusCode = 400; return; }
                    string kind;
                    try { kind = CommunityContentKinds.Normalize(parts[2]); }
                    catch (ArgumentException) { context.Response.StatusCode = 400; return; }
                    if (kind == CommunityContentKinds.Map)
                    { context.Response.StatusCode = 405; return; }
                    string? encoded = context.Request.Headers["X-Prime-Content-Metadata"];
                    string? expectedHash = context.Request.Headers["X-Prime-SHA256"];
                    if (encoded == null || encoded.Length > 8192
                        || !MapCommunityClient.ValidHash(expectedHash ?? ""))
                    { context.Response.StatusCode = 400; return; }
                    long length = context.Request.ContentLength64;
                    if (length <= 0 || length > CommunityAssetValidator.Limit(kind))
                    { context.Response.StatusCode = 413; return; }
                    CommunityContentRegistration metadata;
                    try
                    {
                        byte[] json = Convert.FromBase64String(encoded);
                        if (json.Length > 6144) throw new InvalidDataException("Metadata too large.");
                        metadata = JsonSerializer.Deserialize<CommunityContentRegistration>(
                            json, MapPackageReader.JsonOptions)
                            ?? throw new InvalidDataException("Missing v3 metadata.");
                        if (metadata.Kind != kind || metadata.ContentId != assetId
                            || metadata.OwnerId != creator.CreatorId
                            || context.Request.ContentType?.Split(';')[0].Trim()
                                != metadata.MimeType)
                            throw new InvalidDataException("V3 metadata does not match authenticated request.");
                    }
                    catch (Exception ex) when (ex is JsonException
                        or FormatException or InvalidDataException)
                    { context.Response.StatusCode = 400; return; }

                    string staging = Path.Combine(storage,
                        "content-v3-" + Guid.NewGuid().ToString("N") + ".upload");
                    try
                    {
                        await using (var file = new FileStream(staging,
                            FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            await MapCommunityClient.CopyBoundedAsync(
                                context.Request.InputStream, file, length, deadline.Token);
                            await file.FlushAsync(deadline.Token);
                            if (file.Length != length)
                                throw new InvalidDataException("Truncated v3 upload.");
                        }
                        if (MapBuildFingerprint.HashFile(staging) != expectedHash)
                            throw new InvalidDataException("V3 upload changed SHA-256 identity.");
                        CommunityAssetValidator.Validate(staging, kind, metadata.MimeType);
                        // The registry serializes metadata revisions and retains
                        // immutable bytes; it cannot mutate any v2 map package.
                        CommunityContentProject saved =
                            contentRegistry.RegisterVerifiedBlob(staging, metadata);
                        context.Response.StatusCode = 201;
                        await Json(context.Response, saved, deadline.Token);
                    }
                    catch (InvalidDataException)
                    { context.Response.StatusCode = 400; }
                    finally { if (File.Exists(staging)) File.Delete(staging); }
                    return;
                }
                if (parts.Length is 4 or 5
                    && parts[0] == "v3" && parts[1] == "content"
                    && context.Request.HttpMethod is "DELETE" or "POST"
                    && Guid.TryParse(parts[3], out Guid manageId))
                {
                    if (creator == null) { context.Response.StatusCode = 401; return; }
                    string kind;
                    try { kind = CommunityContentKinds.Normalize(parts[2]); }
                    catch (ArgumentException) { context.Response.StatusCode = 400; return; }
                    if (kind == CommunityContentKinds.Map)
                    { context.Response.StatusCode = 405; return; }
                    if (parts.Length == 4 && context.Request.HttpMethod == "DELETE")
                    {
                        context.Response.StatusCode =
                            contentRegistry.Archive(kind, manageId, creator.CreatorId, true)
                                ? 204 : 404;
                        return;
                    }
                    if (parts.Length == 5 && parts[4] == "restore"
                        && context.Request.HttpMethod == "POST")
                    {
                        context.Response.StatusCode =
                            contentRegistry.Archive(kind, manageId, creator.CreatorId, false)
                                ? 204 : 404;
                        return;
                    }
                }
                // Raw generic objects never use map archive install routes.
                // Only an authorized published or owner-private exact digest
                // can be retrieved. No bytes are served on metadata alone.
                if (parts.Length == 3 && parts[0] == "v3"
                    && parts[1] == "objects" && context.Request.HttpMethod == "GET")
                {
                    string hash = parts[2];
                    if (!MapCommunityClient.ValidHash(hash))
                    { context.Response.StatusCode = 400; return; }
                    string? path = contentRegistry.AuthorizedBlob(hash, creator?.CreatorId);
                    if (path == null) { context.Response.StatusCode = 404; return; }
                    long total = new FileInfo(path).Length, offset = 0;
                    context.Response.Headers["ETag"] = "\"" + hash + "\"";
                    context.Response.Headers["Accept-Ranges"] = "bytes";
                    // Different projects may share a hash with different
                    // visibility. Mark authenticated responses no-store.
                    context.Response.Headers["Cache-Control"] =
                        creator == null ? "public, max-age=31536000, immutable"
                                        : "private, no-store";
                    if (context.Request.Headers["Range"] == null
                        && context.Request.Headers["If-None-Match"] == "\"" + hash + "\"")
                    { context.Response.StatusCode = 304; return; }
                    if (context.Request.Headers["Range"] is { } range)
                    {
                        if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
                            || !range.EndsWith("-", StringComparison.Ordinal)
                            || !long.TryParse(range[6..^1], out offset) || offset < 0)
                        { context.Response.StatusCode = 400; return; }
                        if (offset >= total)
                        {
                            context.Response.StatusCode = 416;
                            context.Response.Headers["Content-Range"] = "bytes */" + total;
                            return;
                        }
                        context.Response.StatusCode = 206;
                        context.Response.Headers["Content-Range"] =
                            "bytes " + offset + "-" + (total - 1) + "/" + total;
                    }
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength64 = total - offset;
                    await using var file = new FileStream(path, FileMode.Open,
                        FileAccess.Read, FileShare.Read, 65536,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    file.Position = offset;
                    await file.CopyToAsync(context.Response.OutputStream, 65536, deadline.Token);
                    return;
                }

                if (parts.Length >= 2 && parts[0] == "v3"
                    && parts[1] == "content" && context.Request.HttpMethod == "GET")
                {
                    string? viewer = creator?.CreatorId;
                    var query = context.Request.QueryString;

                    CommunityMapProject[] VisibleMaps(bool own)
                    {
                        CommunityMap[] visible = maps.Values.Where(m =>
                            own ? creator != null && catalog.CanPublish(creator.CreatorId, m)
                            : m.Listed && !m.Draft
                                && revisionCatalog.IsProjectDiscoverable(m.MapId)
                                && !revisionCatalog.IsRevisionDeleted(m.Hash))
                            .Select(m => catalog.Decorate(m, viewer))
                            .ToArray();
                        return visible.GroupBy(m => m.MapId)
                            .Select(g => revisionCatalog.BuildProject(g.Key, g,
                                revealCreatorIdentity: own,
                                includeDeleted: own,
                                canManageLifecycle: own && creator != null
                                    && g.Any(m => catalog.CanPublish(creator.CreatorId, m))))
                            .OfType<CommunityMapProject>().ToArray();
                    }

                    CommunityContentProject? Lookup(string kind, Guid id)
                    {
                        if (kind != CommunityContentKinds.Map)
                            return contentRegistry.GetVisible(kind, id, viewer);
                        CommunityMap[] versions = maps.Values.Where(m => m.MapId == id).ToArray();
                        if (versions.Length == 0) return null;
                        bool owned = creator != null
                            && versions.Any(m => catalog.CanPublish(creator.CreatorId, m));
                        if (!owned && !revisionCatalog.IsProjectDiscoverable(id)) return null;
                        CommunityMap[] visible = versions
                            .Where(m => owned || m.Listed && !m.Draft
                                && !revisionCatalog.IsRevisionDeleted(m.Hash))
                            .Select(m => catalog.Decorate(m, viewer))
                            .ToArray();
                        var project = revisionCatalog.BuildProject(id, visible,
                            revealCreatorIdentity: owned,
                            includeDeleted: owned, canManageLifecycle: owned);
                        return project == null ? null : CommunityContentProjection.FromMap(project);
                    }

                    if (parts.Length == 2)
                    {
                        bool mine = query["mine"] == "true";
                        if (mine && creator == null)
                        { context.Response.StatusCode = 401; return; }
                        string? kind = query["kind"];
                        if (kind != null)
                        {
                            try { kind = CommunityContentKinds.Normalize(kind); }
                            catch (ArgumentException)
                            { context.Response.StatusCode = 400; return; }
                        }
                        string search = query["query"] ?? "";
                        string tag = query["tag"] ?? "";
                        if (search.Length > 128 || tag.Length > 32)
                        { context.Response.StatusCode = 400; return; }

                        var all = new List<CommunityContentProject>();
                        if (kind == null || kind == CommunityContentKinds.Map)
                            all.AddRange(VisibleMaps(mine).Select(CommunityContentProjection.FromMap));
                        if (kind == null || kind != CommunityContentKinds.Map)
                            all.AddRange(contentRegistry.ListVisible(mine ? viewer : null)
                                .Where(item => !mine || item.CanManage)
                                .Where(item => kind == null || item.Kind == kind));
                        if (!string.IsNullOrWhiteSpace(search))
                            all = all.Where(item =>
                                (item.Name + " " + item.DisplayName + " " + item.Author
                                    + " " + item.Description)
                                .Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (!string.IsNullOrWhiteSpace(tag))
                            all = all.Where(item => item.Tags.Contains(
                                tag, StringComparer.OrdinalIgnoreCase)).ToList();
                        if (query["author"] is { } author)
                            all = all.Where(item => string.Equals(item.Author, author,
                                StringComparison.OrdinalIgnoreCase)).ToList();

                        IOrderedEnumerable<CommunityContentProject> ordered =
                            query["sort"] is "new" or "updated"
                                ? all.OrderByDescending(item => item.UpdatedAt)
                                : all.OrderBy(item => item.DisplayName ?? item.Name,
                                    StringComparer.OrdinalIgnoreCase);
                        ordered = ordered.ThenBy(item => item.Kind, StringComparer.Ordinal)
                            .ThenBy(item => item.ContentId);
                        int total = all.Count;
                        int page = int.TryParse(query["page"], out int p) ? Math.Clamp(p, 1, 2000) : 1;
                        int size = int.TryParse(query["pageSize"], out int n)
                            ? Math.Clamp(n, 1, 100) : 24;
                        var result = new CommunityContentPage(3, total, page, size,
                            ordered.Skip((page - 1) * size).Take(size).ToArray());
                        context.Response.Headers["X-Total-Count"] =
                            total.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        await CatalogJson(context, result, deadline.Token,
                            privateResult: creator != null);
                        return;
                    }

                    if ((parts.Length is 4 or 5)
                        && Guid.TryParse(parts[3], out Guid contentId))
                    {
                        string kind;
                        try { kind = CommunityContentKinds.Normalize(parts[2]); }
                        catch (ArgumentException)
                        { context.Response.StatusCode = 400; return; }
                        CommunityContentProject? project = Lookup(kind, contentId);
                        if (project == null) { context.Response.StatusCode = 404; return; }

                        if (parts.Length == 4)
                        {
                            await CatalogJson(context, project, deadline.Token,
                                privateResult: creator != null);
                            return;
                        }
                        if (parts[4] == "revisions")
                        {
                            CommunityContentRevision[] revisions;
                            if (kind == CommunityContentKinds.Map)
                            {
                                bool own = project.CanManage;
                                CommunityMap[] eligible = maps.Values
                                    .Where(m => m.MapId == contentId
                                        && (own || m.Listed && !m.Draft
                                            && !revisionCatalog.IsRevisionDeleted(m.Hash)))
                                    .Select(m => catalog.Decorate(m, viewer)).ToArray();
                                revisions = revisionCatalog.BuildRevisions(
                                    contentId, eligible, revealCreatorIdentity: own,
                                    includeDeleted: own)
                                    .Select(CommunityContentProjection.FromMap).ToArray();
                            }
                            else
                                revisions = contentRegistry.RevisionsVisible(kind, contentId, viewer);
                            await CatalogJson(context, revisions, deadline.Token,
                                privateResult: creator != null);
                            return;
                        }
                    }
                    context.Response.StatusCode = 404;
                    return;
                }

                if (route == root + "/v2/metrics" && context.Request.HttpMethod == "GET")
                {
                    if (creator?.Moderator != true)
                    { context.Response.StatusCode = 403; return; }
                    await Json(context.Response, new
                    {
                        Admission = admission.Snapshot(),
                        UploadQuota = uploadQuota.Snapshot(),
                        PublishedBytes = publishedBytes,
                        PublishedStorageLimitBytes = publishedStorageLimit,
                        DownloadBytesServed = Interlocked.Read(ref completedDownloadBytes)
                    }, deadline.Token);
                    return;
                }

                if (parts.Length >= 2 && parts[0] == "uploads")
                {
                    if (creator == null) { context.Response.StatusCode = 401; return; }
                    string hash = parts[1];
                    if (!MapCommunityClient.ValidHash(hash)) { context.Response.StatusCode = 400; return; }

                    if (maps.TryGetValue(hash, out var completed))
                    {
                        if (!catalog.CanPublish(creator.CreatorId, completed)) { context.Response.StatusCode = 403; return; }
                        if (parts.Length == 2 && context.Request.HttpMethod is "GET" or "POST")
                        {
                            await Json(context.Response, new MapUploadState(hash, completed.Bytes, completed.Bytes,
                                MapCommunityClient.UploadChunkBytes, true), deadline.Token);
                            return;
                        }
                    }

                    string key = UploadKey(creator.CreatorId, hash);
                    var gate = uploadLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(deadline.Token);
                    try
                    {
                        if (parts.Length == 2 && context.Request.HttpMethod == "POST")
                        {
                            var start = await Body<MapUploadStartRequest>();
                            if (start.Bytes <= 0 || start.Bytes > MapPackageReader.MaxArchiveBytes)
                            { context.Response.StatusCode = 413; return; }
                            if (start.ReleaseNotes?.Length > 4000)
                            { context.Response.StatusCode = 400; return; }

                            var upload = LoadUpload(key);
                            bool resumableBytes = upload != null
                                && File.Exists(UploadPartPath(key))
                                && new FileInfo(UploadPartPath(key)).Length > 0;
                            var intent = ValidateRevisionIntent(
                                start.MapId, start.ExistingMap, start.ExpectedParentHash,
                                start.AllowStaleParent, creator,
                                resumeAvailable: resumableBytes);
                            if (intent.Status != 0)
                            {
                                context.Response.StatusCode = intent.Status;
                                if (intent.Conflict != null)
                                    await Json(context.Response, intent.Conflict, deadline.Token);
                                return;
                            }

                            if (upload == null || upload.CreatorId != creator.CreatorId || upload.PackageHash != hash || upload.Bytes != start.Bytes)
                            {
                                DeleteUpload(key);
                                if (!HasAvailableUploadDisk(storage, start.Bytes))
                                { context.Response.StatusCode = 507; return; }
                                if (!uploadQuota.TryReserve(key, creator.CreatorId,
                                    start.Bytes, out HttpStatusCode quotaFailure))
                                {
                                    context.Response.StatusCode = (int)quotaFailure;
                                    if (quotaFailure == HttpStatusCode.TooManyRequests)
                                        context.Response.Headers["Retry-After"] = "2";
                                    return;
                                }
                                upload = new(creator.CreatorId, hash, start.Bytes, start.Listed, start.Draft)
                                {
                                    MapId = start.MapId,
                                    ExistingMap = start.ExistingMap,
                                    ExpectedParentHash = start.ExpectedParentHash,
                                    ReleaseNotes = start.ReleaseNotes?.Trim(),
                                    AllowStaleParent = start.AllowStaleParent
                                };
                                try { SaveUpload(key, upload); }
                                catch
                                {
                                    uploadQuota.Release(key);
                                    throw;
                                }
                            }
                            else if (upload.Listed != start.Listed || upload.Draft != start.Draft
                                || upload.MapId != start.MapId
                                || upload.ExistingMap != start.ExistingMap
                                || upload.ExpectedParentHash != start.ExpectedParentHash
                                || upload.ReleaseNotes != start.ReleaseNotes?.Trim()
                                || upload.AllowStaleParent != start.AllowStaleParent)
                            {
                                upload = upload with
                                {
                                    Listed = start.Listed,
                                    Draft = start.Draft,
                                    MapId = start.MapId,
                                    ExistingMap = start.ExistingMap,
                                    ExpectedParentHash = start.ExpectedParentHash,
                                    ReleaseNotes = start.ReleaseNotes?.Trim(),
                                    AllowStaleParent = start.AllowStaleParent
                                };
                                SaveUpload(key, upload);
                            }

                            var state = UploadState(key, upload);
                            await Json(context.Response, state, deadline.Token);
                            return;
                        }

                        var current = LoadUpload(key);
                        if (current == null || current.CreatorId != creator.CreatorId || current.PackageHash != hash)
                        { context.Response.StatusCode = 404; return; }

                        if (parts.Length == 2 && context.Request.HttpMethod == "GET")
                        {
                            await Json(context.Response, UploadState(key, current), deadline.Token);
                            return;
                        }

                        if (parts.Length == 2 && context.Request.HttpMethod == "DELETE")
                        {
                            DeleteUpload(key);
                            context.Response.StatusCode = 204;
                            return;
                        }

                        if (parts.Length == 2 && context.Request.HttpMethod == "PUT")
                        {
                            if (!long.TryParse(context.Request.QueryString["offset"],
                                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long offset))
                            { context.Response.StatusCode = 400; return; }
                            var state = UploadState(key, current);
                            if (offset != state.Offset)
                            {
                                context.Response.StatusCode = 409;
                                await Json(context.Response, state, deadline.Token);
                                return;
                            }
                            long remaining = current.Bytes - state.Offset;
                            if (remaining <= 0)
                            {
                                await Json(context.Response, state, deadline.Token);
                                return;
                            }
                            long maximum = Math.Min(MapCommunityClient.UploadChunkBytes, remaining);
                            if (context.Request.ContentLength64 > maximum)
                            { context.Response.StatusCode = 413; return; }
                            // Expected bytes were reserved at upload start,
                            // including the entire remaining sequential transfer.
                            // No per-chunk directory walk or race-prone quota sum.
                            long incoming = context.Request.ContentLength64 >= 0
                                ? context.Request.ContentLength64 : maximum;
                            if (!HasAvailableUploadDisk(storage, incoming))
                            { context.Response.StatusCode = 507; return; }
                            string partial = UploadPartPath(key);
                            await using (var file = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
                                65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                            {
                                if (file.Length != state.Offset) throw new InvalidDataException("Partial upload changed during append.");
                                file.Position = file.Length;
                                long before = file.Length;
                                await MapCommunityClient.CopyBoundedAsync(context.Request.InputStream, file, maximum, deadline.Token);
                                await file.FlushAsync(deadline.Token);
                                long written = file.Length - before;
                                if (context.Request.ContentLength64 >= 0 && written != context.Request.ContentLength64)
                                    throw new InvalidDataException("Incomplete upload chunk.");
                            }
                            await Json(context.Response, UploadState(key, current), deadline.Token);
                            return;
                        }

                        if (parts.Length == 3 && parts[2] == "complete" && context.Request.HttpMethod == "POST")
                        {
                            var state = UploadState(key, current);
                            if (state.Offset != current.Bytes)
                            {
                                context.Response.StatusCode = 409;
                                await Json(context.Response, state, deadline.Token);
                                return;
                            }
                            string partial = UploadPartPath(key);
                            if (!MapBuildFingerprint.HashFile(partial).Equals(hash, StringComparison.Ordinal))
                            {
                                DeleteUpload(key);
                                context.Response.StatusCode = 400;
                                return;
                            }

                            await publication.WaitAsync(deadline.Token);
                            try
                            {
                                var result = PublishTemporary(
                                    partial, creator, current.Listed, current.Draft,
                                    revisionUpload: current);
                                context.Response.StatusCode = result.Status;
                                if (result.Entry != null)
                                {
                                    DeleteUpload(key);
                                    await Json(context.Response, result.Entry, deadline.Token);
                                }
                                else if (result.Conflict != null)
                                {
                                    // Keep the completed resumable session so an explicit
                                    // "publish anyway" retry can update only the metadata
                                    // and complete without uploading hundreds of MiB again.
                                    await Json(context.Response, result.Conflict, deadline.Token);
                                }
                                else if (result.Status == 409)
                                {
                                    // Legacy human-version conflict cannot be repaired by
                                    // changing revision metadata, so release its partial pool.
                                    DeleteUpload(key);
                                }
                            }
                            finally { publication.Release(); }
                            return;
                        }

                        context.Response.StatusCode = 405;
                        return;
                    }
                    finally { gate.Release(); }
                }
                if (parts.Length==3&&parts[0]=="maps"&&Guid.TryParse(parts[1],out Guid target)&&parts[2] is "favorite" or "reports" or "collaborators")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    var existing=maps.Values.FirstOrDefault(m=>m.MapId==target);if(existing==null){context.Response.StatusCode=404;return;}
                    if(parts[2]=="favorite"&&context.Request.HttpMethod is "PUT" or "DELETE")catalog.Favorite(creator.CreatorId,target,context.Request.HttpMethod=="PUT");
                    else if(parts[2]=="reports"&&context.Request.HttpMethod=="POST")
                    {var report=await Body<MapReportRequest>();if(!maps.Values.Any(m=>m.MapId==target&&m.Version==report.Version)){context.Response.StatusCode=400;return;}await Json(context.Response,catalog.Report(creator.CreatorId,target,report),deadline.Token);return;}
                    else if(parts[2]=="collaborators"&&context.Request.HttpMethod=="POST")
                    {if(existing.OwnerId!=creator.CreatorId){context.Response.StatusCode=403;return;}catalog.SetCollaborators(target,await Body<string[]>());}
                    else{context.Response.StatusCode=405;return;}
                    context.Response.StatusCode=204;return;
                }
                if(parts.Length==3&&parts[0]=="packages"&&parts[2]=="visibility"&&context.Request.HttpMethod=="POST")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    await publication.WaitAsync(deadline.Token);
                    try
                    {
                        if(!maps.TryGetValue(parts[1],out var map)){context.Response.StatusCode=404;return;}
                        if(!catalog.CanPublish(creator.CreatorId,map)){context.Response.StatusCode=403;return;}
                        if(revisionCatalog.IsProjectDeleted(map.MapId)
                            ||revisionCatalog.IsRevisionDeleted(map.Hash))
                        {context.Response.StatusCode=410;return;}
                        string visibility=await Body<string>();if(visibility is not ("Published" or "Unlisted" or "Draft"))throw new InvalidDataException("Invalid visibility.");
                        var updated=map with{Listed=visibility=="Published",Draft=visibility=="Draft"};
                        AtomicFile.Write(Path.Combine(storage,map.Hash+".catalog.json"),JsonSerializer.SerializeToUtf8Bytes(updated,MapPackageReader.JsonOptions));
                        maps[map.Hash]=updated;
                        revisionCatalog.ApplyVisibility(updated,maps.Values);
                        QueuePublicMirror(updated);
                        context.Response.StatusCode=204;
                    }
                    finally{publication.Release();}return;
                }
                if(parts.Length==3&&parts[0]=="v2"&&parts[1]=="maps"
                    &&Guid.TryParse(parts[2],out Guid deleteMapId)
                    &&context.Request.HttpMethod=="DELETE")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    CommunityMap? owned=maps.Values.FirstOrDefault(m=>m.MapId==deleteMapId);
                    if(owned==null){context.Response.StatusCode=404;return;}
                    if(owned.OwnerId!=creator.CreatorId){context.Response.StatusCode=403;return;}
                    await publication.WaitAsync(deadline.Token);
                    try
                    {
                        if(!revisionCatalog.DeleteMap(
                            deleteMapId,CommunityDeleteRetention))
                        {context.Response.StatusCode=404;return;}
                        var project=ManagedProject(deleteMapId,creator,includeDeleted:true);
                        if(project==null){context.Response.StatusCode=404;return;}
                        await Json(context.Response,project,deadline.Token);
                    }
                    finally{publication.Release();}
                    return;
                }
                if(parts.Length==4&&parts[0]=="v2"&&parts[1]=="maps"
                    &&Guid.TryParse(parts[2],out Guid lifecycleMapId)
                    &&parts[3] is "archive" or "restore" or "purge")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    CommunityMap? owned=maps.Values.FirstOrDefault(m=>m.MapId==lifecycleMapId);
                    if(owned==null){context.Response.StatusCode=404;return;}

                    if(parts[3]=="purge")
                    {
                        if(context.Request.HttpMethod!="DELETE")
                        {context.Response.StatusCode=405;return;}
                        if(!creator.Moderator){context.Response.StatusCode=403;return;}
                        await publication.WaitAsync(deadline.Token);
                        try
                        {
                            string[] hashes=revisionCatalog.MapHashes(lifecycleMapId);
                            if(hashes.Length==0){context.Response.StatusCode=404;return;}
                            PurgeLifecycleHashes(hashes);
                            context.Response.StatusCode=204;
                        }
                        finally{publication.Release();}
                        return;
                    }

                    if(context.Request.HttpMethod!="POST")
                    {context.Response.StatusCode=405;return;}
                    if(owned.OwnerId!=creator.CreatorId){context.Response.StatusCode=403;return;}
                    await publication.WaitAsync(deadline.Token);
                    try
                    {
                        if(parts[3]=="archive")
                        {
                            if(!revisionCatalog.Archive(lifecycleMapId))
                            {context.Response.StatusCode=409;return;}
                        }
                        else
                        {
                            var state=revisionCatalog.ProjectState(lifecycleMapId);
                            if(state?.DeleteAfter is DateTimeOffset deadlineAt
                                &&deadlineAt<=DateTimeOffset.UtcNow)
                            {context.Response.StatusCode=410;return;}
                            if(!revisionCatalog.RestoreMap(lifecycleMapId))
                            {context.Response.StatusCode=404;return;}
                        }
                        var project=ManagedProject(
                            lifecycleMapId,creator,includeDeleted:true);
                        if(project==null){context.Response.StatusCode=404;return;}
                        await Json(context.Response,project,deadline.Token);
                    }
                    finally{publication.Release();}
                    return;
                }
                if(parts.Length==5&&parts[0]=="v2"&&parts[1]=="maps"
                    &&Guid.TryParse(parts[2],out Guid revisionLifecycleMapId)
                    &&parts[3]=="revisions"
                    &&int.TryParse(parts[4],out int lifecycleRevision)
                    &&context.Request.HttpMethod=="DELETE")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    CommunityMap? owned=maps.Values.FirstOrDefault(
                        m=>m.MapId==revisionLifecycleMapId);
                    if(owned==null){context.Response.StatusCode=404;return;}
                    if(owned.OwnerId!=creator.CreatorId){context.Response.StatusCode=403;return;}
                    await publication.WaitAsync(deadline.Token);
                    try
                    {
                        try
                        {
                            if(!revisionCatalog.DeleteRevision(
                                revisionLifecycleMapId,lifecycleRevision,
                                CommunityDeleteRetention,maps.Values))
                            {context.Response.StatusCode=404;return;}
                        }
                        catch(InvalidOperationException)
                        {context.Response.StatusCode=409;return;}
                        var project=ManagedProject(
                            revisionLifecycleMapId,creator,includeDeleted:true);
                        if(project==null){context.Response.StatusCode=404;return;}
                        await Json(context.Response,project,deadline.Token);
                    }
                    finally{publication.Release();}
                    return;
                }
                if(parts.Length==6&&parts[0]=="v2"&&parts[1]=="maps"
                    &&Guid.TryParse(parts[2],out Guid revisionActionMapId)
                    &&parts[3]=="revisions"
                    &&int.TryParse(parts[4],out int revisionActionNumber)
                    &&parts[5] is "restore" or "purge")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    CommunityMap? owned=maps.Values.FirstOrDefault(
                        m=>m.MapId==revisionActionMapId);
                    if(owned==null){context.Response.StatusCode=404;return;}

                    if(parts[5]=="purge")
                    {
                        if(context.Request.HttpMethod!="DELETE")
                        {context.Response.StatusCode=405;return;}
                        if(!creator.Moderator){context.Response.StatusCode=403;return;}
                        string? hash=revisionCatalog.RevisionHashForLifecycle(
                            revisionActionMapId,revisionActionNumber);
                        if(hash==null){context.Response.StatusCode=404;return;}
                        await publication.WaitAsync(deadline.Token);
                        try
                        {
                            PurgeLifecycleHashes(new[]{hash});
                            context.Response.StatusCode=204;
                        }
                        finally{publication.Release();}
                        return;
                    }

                    if(context.Request.HttpMethod!="POST")
                    {context.Response.StatusCode=405;return;}
                    if(owned.OwnerId!=creator.CreatorId){context.Response.StatusCode=403;return;}
                    await publication.WaitAsync(deadline.Token);
                    try
                    {
                        var state=revisionCatalog.RevisionState(
                            revisionActionMapId,revisionActionNumber);
                        if(state?.DeleteAfter is DateTimeOffset deadlineAt
                            &&deadlineAt<=DateTimeOffset.UtcNow)
                        {context.Response.StatusCode=410;return;}
                        if(!revisionCatalog.RestoreRevision(
                            revisionActionMapId,revisionActionNumber,maps.Values))
                        {context.Response.StatusCode=404;return;}
                        var project=ManagedProject(
                            revisionActionMapId,creator,includeDeleted:true);
                        if(project==null){context.Response.StatusCode=404;return;}
                        await Json(context.Response,project,deadline.Token);
                    }
                    finally{publication.Release();}
                    return;
                }
                if(parts.Length==6&&parts[0]=="v2"&&parts[1]=="maps"
                    &&Guid.TryParse(parts[2],out Guid promoteMapId)
                    &&parts[3]=="revisions"
                    &&int.TryParse(parts[4],out int promoteRevision)
                    &&parts[5]=="promote"
                    &&context.Request.HttpMethod=="POST")
                {
                    if(creator==null){context.Response.StatusCode=401;return;}
                    CommunityMap? owned=maps.Values.FirstOrDefault(m=>m.MapId==promoteMapId);
                    if(owned==null){context.Response.StatusCode=404;return;}
                    if(!catalog.CanPublish(creator.CreatorId,owned)){context.Response.StatusCode=403;return;}
                    if(revisionCatalog.IsProjectDeleted(promoteMapId))
                    {context.Response.StatusCode=410;return;}
                    string? promoteHash=revisionCatalog.RevisionHash(promoteMapId,promoteRevision);
                    if(promoteHash==null||!maps.TryGetValue(promoteHash,out var promoteMap))
                    {context.Response.StatusCode=404;return;}
                    if(revisionCatalog.IsRevisionDeleted(promoteHash))
                    {context.Response.StatusCode=410;return;}

                    await publication.WaitAsync(deadline.Token);
                    try
                    {
                        var updated=promoteMap with{Listed=true,Draft=false};
                        AtomicFile.Write(Path.Combine(storage,updated.Hash+".catalog.json"),
                            JsonSerializer.SerializeToUtf8Bytes(updated,MapPackageReader.JsonOptions));
                        maps[updated.Hash]=updated;
                        revisionCatalog.Promote(promoteMapId,updated.Hash);
                        QueuePublicMirror(updated);
                        CommunityMap[] visible=maps.Values.Where(m=>m.MapId==promoteMapId)
                            .Select(m=>catalog.Decorate(m,creator.CreatorId)).ToArray();
                        var project=revisionCatalog.BuildProject(
                            promoteMapId,visible,revealCreatorIdentity:true,
                            canManageLifecycle:visible.Any(
                                m=>m.OwnerId==creator.CreatorId));
                        if(project==null){context.Response.StatusCode=404;return;}
                        await Json(context.Response,project,deadline.Token);
                    }
                    finally{publication.Release();}
                    return;
                }
                if(parts.Length>=2&&parts[0]=="v2"&&parts[1]=="maps"&&context.Request.HttpMethod=="GET")
                {
                    var query=context.Request.QueryString;
                    bool mine=query["mine"]=="true",favorites=query["favorites"]=="true";
                    if((mine||favorites)&&creator==null){context.Response.StatusCode=401;return;}

                    if(parts.Length==2)
                    {
                        CommunityMap[] visible=maps.Values
                            .Where(m=>mine
                                ? creator!=null&&catalog.CanPublish(creator.CreatorId,m)
                                : m.Listed&&!m.Draft
                                    &&revisionCatalog.IsProjectDiscoverable(m.MapId)
                                    &&!revisionCatalog.IsRevisionDeleted(m.Hash))
                            .Select(m=>catalog.Decorate(m,creator?.CreatorId))
                            .ToArray();
                        IEnumerable<CommunityMapProject> projects=visible
                            .GroupBy(m=>m.MapId)
                            .Select(g=>revisionCatalog.BuildProject(
                                g.Key,g,revealCreatorIdentity:mine,
                                includeDeleted:mine,
                                canManageLifecycle:mine&&creator!=null
                                    &&g.Any(m=>m.OwnerId==creator.CreatorId)))
                            .OfType<CommunityMapProject>();
                        CommunityMap Presentation(CommunityMapProject project)
                            => project.CurrentRevision?.Package ?? project.LatestRevision.Package;
                        if(favorites)projects=projects.Where(p=>Presentation(p).Favorited);
                        if(query["query"] is { } search)projects=projects.Where(p=>
                            (p.Name+" "+p.DisplayName+" "+p.Author+" "+Presentation(p).Version)
                                .Contains(search,StringComparison.OrdinalIgnoreCase));
                        if(query["mode"] is { } mode)projects=projects.Where(p=>
                            Presentation(p).SupportedModes.Length==0
                            ||Presentation(p).SupportedModes.Contains(mode,StringComparer.OrdinalIgnoreCase));
                        if(query["author"] is { } author)projects=projects.Where(p=>
                            string.Equals(p.Author,author,StringComparison.OrdinalIgnoreCase));
                        projects=query["sort"]=="favorites"
                            ? projects.OrderByDescending(p=>Presentation(p).FavoriteCount).ThenBy(p=>p.Name)
                            : query["sort"] is "new" or "updated"
                                ? projects.OrderByDescending(p=>p.UpdatedAt)
                                : projects.OrderBy(p=>p.DisplayName??p.Name);
                        CommunityMapProject[] allItems = projects.ToArray();
                        context.Response.Headers["X-Total-Count"] = allItems.Length.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                        if(query["page"]!=null||query["pageSize"]!=null)
                        {
                            int page=int.TryParse(query["page"],out int p)?Math.Clamp(p,1,2000):1;
                            int size=int.TryParse(query["pageSize"],out int n)?Math.Clamp(n,1,100):24;
                            allItems=allItems.Skip((page-1)*size).Take(size).ToArray();
                        }
                        CommunityMapProject[] pageItems = allItems;
                        await CatalogJson(context, pageItems, deadline.Token,
                            privateResult: creator != null);
                        return;
                    }

                    if((parts.Length is 3 or 4)&&Guid.TryParse(parts[2],out Guid projectId))
                    {
                        CommunityMap[] all=maps.Values.Where(m=>m.MapId==projectId).ToArray();
                        if(all.Length==0){context.Response.StatusCode=404;return;}
                        bool canManage=creator!=null&&all.Any(m=>catalog.CanPublish(creator.CreatorId,m));
                        if(!canManage&&!revisionCatalog.IsProjectDiscoverable(projectId))
                        {context.Response.StatusCode=404;return;}
                        CommunityMap[] visible=all
                            .Where(m=>canManage
                                ||m.Listed&&!m.Draft
                                    &&!revisionCatalog.IsRevisionDeleted(m.Hash))
                            .Select(m=>catalog.Decorate(m,creator?.CreatorId))
                            .ToArray();
                        if(visible.Length==0){context.Response.StatusCode=404;return;}
                        if(parts.Length==3)
                        {
                            var project=revisionCatalog.BuildProject(
                                projectId,visible,revealCreatorIdentity:canManage,
                                includeDeleted:canManage,
                                canManageLifecycle:creator!=null
                                    &&all.Any(m=>m.OwnerId==creator.CreatorId));
                            if(project==null){context.Response.StatusCode=404;return;}
                            await CatalogJson(context, project, deadline.Token,
                                privateResult: canManage || creator != null);
                            return;
                        }
                        if(parts[3]=="revisions")
                        {
                            await CatalogJson(context, revisionCatalog.BuildRevisions(
                                projectId,visible,revealCreatorIdentity:canManage,
                                includeDeleted:canManage),deadline.Token,
                                privateResult: canManage || creator != null);
                            return;
                        }
                    }
                    context.Response.StatusCode=404;
                    return;
                }
                if(parts[0]=="reports")
                {
                    if(creator?.Moderator!=true){context.Response.StatusCode=403;return;}
                    if(parts.Length==1&&context.Request.HttpMethod=="GET")await Json(context.Response,catalog.Reports(),deadline.Token);
                    else if(parts.Length==2&&Guid.TryParse(parts[1],out var report)&&context.Request.HttpMethod=="POST"){catalog.SetReportStatus(report,await Body<string>());context.Response.StatusCode=204;}
                    else context.Response.StatusCode=404;return;
                }
                if (route == root + "/health" && context.Request.HttpMethod == "GET")
                    await Json(context.Response, new
                    {
                        Status="ok", Service="prime-maps", ApiVersion=2, Maps=maps.Count,
                        Projects=revisionCatalog.ProjectCount,
                        PublishedBytes=publishedBytes,
                        StorageLimitBytes=publishedStorageLimit
                    }, deadline.Token);
                else if (route == root + "/maps" && context.Request.HttpMethod == "GET")
                    {
                    var query = context.Request.QueryString;
                    if((query["mine"]=="true"||query["favorites"]=="true")&&creator==null){context.Response.StatusCode=401;return;}
                    IEnumerable<CommunityMap> found = maps.Values
                        .Where(m => revisionCatalog.IsProjectDiscoverable(m.MapId)
                            && !revisionCatalog.IsRevisionDeleted(m.Hash)
                            && (query["mine"]=="true"
                                ? creator!=null&&catalog.CanPublish(creator.CreatorId,m)
                                : m.Listed&&!m.Draft))
                        .Select(m=>catalog.Decorate(m,creator?.CreatorId))
                        .Select(revisionCatalog.LegacyPresentation);
                    if(query["favorites"]=="true")found=found.Where(m=>m.Favorited);
                    if (query["query"] is { } search) found = found.Where(m => (m.Name + " " + m.DisplayName + " " + m.Author).Contains(search, StringComparison.OrdinalIgnoreCase));
                    if (query["mode"] is { } mode) found = found.Where(m => m.SupportedModes.Length == 0 || m.SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase));
                    if (query["author"] is { } author) found = found.Where(m => string.Equals(m.Author, author, StringComparison.OrdinalIgnoreCase));
                    found = query["sort"]=="favorites" ? found.OrderByDescending(m=>m.FavoriteCount).ThenBy(m=>m.Name) : query["sort"] is "new" or "updated" ? found.OrderByDescending(m => m.PublishedAt) : found.OrderBy(m => m.DisplayName ?? m.Name);
                    CommunityMap[] allFound = found.ToArray();
                    context.Response.Headers["X-Total-Count"] = allFound.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (query["page"] != null || query["pageSize"] != null)
                    {
                        int page = int.TryParse(query["page"], out int p) ? Math.Clamp(p, 1, 2000) : 1;
                        int size = int.TryParse(query["pageSize"], out int n) ? Math.Clamp(n, 1, 100) : 24;
                        allFound = allFound.Skip((page - 1) * size).Take(size).ToArray();
                    }
                    await CatalogJson(context, allFound, deadline.Token,
                        privateResult: creator != null);
                }
                else if ((route.StartsWith(root + "/maps/", StringComparison.Ordinal) || route.StartsWith(root + "/packages/", StringComparison.Ordinal)) && context.Request.HttpMethod == "GET")
                {
                    string tail = route[(root.Length + 1)..];
                    string hash = tail[(tail.IndexOf('/') + 1)..];
                    string idText = hash.Split('/')[0];
                    if (tail.StartsWith("maps/", StringComparison.Ordinal) && Guid.TryParse(idText, out Guid id))
                    {
                        var versions = maps.Values
                            .Where(m => m.MapId == id
                                && revisionCatalog.IsProjectDiscoverable(id)
                                && !revisionCatalog.IsRevisionDeleted(m.Hash)
                                && (m.Listed&&!m.Draft
                                    || creator!=null&&catalog.CanPublish(creator.CreatorId,m)))
                            .Select(revisionCatalog.LegacyPresentation)
                            .OrderByDescending(m => m.PublishedAt).ToArray();
                        if (versions.Length == 0 || hash != idText && hash != idText + "/versions") { context.Response.StatusCode = 404; return; }
                        await Json(context.Response, hash.EndsWith("/versions", StringComparison.Ordinal) ? (object)versions : versions[0], deadline.Token);
                        return;
                    }
                    if (hash.EndsWith("/preview", StringComparison.Ordinal))
                    {
                        // A thumbnail is presentation-only. Never ask clients
                        // to download 512 MiB merely to display a map preview.
                        string packageHash = hash[..^8];
                        if (!MapCommunityClient.ValidHash(packageHash)
                            || !maps.TryGetValue(packageHash, out CommunityMap? previewMap))
                        { context.Response.StatusCode = 404; return; }
                        if (previewMap.Draft && (creator == null
                            || !catalog.CanPublish(creator.CreatorId, previewMap)))
                        { context.Response.StatusCode = 403; return; }
                        string archivePath = Path.Combine(storage, packageHash + ".ppmap");
                        byte[]? manifestBytes = MapPackageReader.ReadCatalogEntry(
                            archivePath, "manifest.json", 65536);
                        if (manifestBytes == null)
                        { context.Response.StatusCode = 404; return; }
                        var manifest = JsonSerializer.Deserialize<MapPackageManifest>(
                            manifestBytes, MapPackageReader.JsonOptions);
                        if (string.IsNullOrWhiteSpace(manifest?.Preview))
                        { context.Response.StatusCode = 404; return; }
                        byte[]? preview;
                        try
                        {
                            preview = MapPackageReader.ReadCatalogEntry(archivePath,
                                manifest.Preview, 1024 * 1024);
                        }
                        catch (InvalidDataException ex) when (ex.Message.Contains(
                            "oversized", StringComparison.OrdinalIgnoreCase))
                        { context.Response.StatusCode = 413; return; }
                        if (preview == null)
                        { context.Response.StatusCode = 404; return; }
                        byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10 };
                        if (preview.Length < png.Length || !preview.AsSpan(0, 8).SequenceEqual(png))
                        { context.Response.StatusCode = 415; return; }
                        string previewEtag = "\"" + Convert.ToHexString(
                            SHA256.HashData(preview)).ToLowerInvariant() + "\"";
                        context.Response.Headers["ETag"] = previewEtag;
                        context.Response.Headers["Cache-Control"] =
                            previewMap.Listed && !previewMap.Draft
                                ? "public, max-age=300"
                                : "private, no-store";
                        if (context.Request.Headers["If-None-Match"] == previewEtag)
                        { context.Response.StatusCode = 304; return; }
                        context.Response.ContentType = "image/png";
                        context.Response.ContentLength64 = preview.LongLength;
                        await context.Response.OutputStream.WriteAsync(preview, deadline.Token);
                        return;
                    }
                    if (hash.EndsWith("/metadata", StringComparison.Ordinal))
                    {
                        string packageHash = hash[..^9];
                        if (!MapCommunityClient.ValidHash(packageHash) || !maps.TryGetValue(packageHash, out var entry)) { context.Response.StatusCode=404; return; }
                        if(entry.Draft&&(creator==null||!catalog.CanPublish(creator.CreatorId,entry))){context.Response.StatusCode=403;return;}
                        await Json(context.Response,catalog.Decorate(entry,creator?.CreatorId),deadline.Token); return;
                    }
                    if (!MapCommunityClient.ValidHash(hash) || !maps.ContainsKey(hash)) { context.Response.StatusCode = 404; return; }
                    if(maps[hash].Draft&&(creator==null||!catalog.CanPublish(creator.CreatorId,maps[hash]))){context.Response.StatusCode=403;return;}
                    CommunityMap downloadMap = maps[hash];
                    string packageEtag = "\"" + hash + "\"";
                    context.Response.Headers["ETag"] = packageEtag;
                    context.Response.Headers["Cache-Control"] = downloadMap.Listed
                        && !downloadMap.Draft ? "public, max-age=31536000, immutable"
                        : "private, no-store";
                    if (context.Request.Headers["Range"] == null
                        && context.Request.Headers["If-None-Match"] == packageEtag)
                    { context.Response.StatusCode = 304; return; }
                    long total = downloadMap.Bytes, start = 0;
                    context.Response.Headers["Accept-Ranges"] = "bytes";
                    if (context.Request.Headers["Range"] is { Length: > 0 } range)
                    {
                        const string prefix = "bytes=";
                        if (!range.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            || range.Contains(',') || !range.EndsWith("-", StringComparison.Ordinal)
                            || !long.TryParse(range.AsSpan(prefix.Length, range.Length - prefix.Length - 1),
                                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out start)
                            || start < 0)
                        { context.Response.StatusCode = 400; return; }
                        if (start >= total)
                        {
                            context.Response.StatusCode = 416;
                            context.Response.Headers["Content-Range"] = "bytes */" + total.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            return;
                        }
                        context.Response.StatusCode = 206;
                        context.Response.Headers["Content-Range"] = "bytes "
                            + start.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-"
                            + (total - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"
                            + total.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength64 = total - start;
                    await using var file = File.OpenRead(Path.Combine(storage, hash + ".ppmap"));
                    if (start > 0) file.Position = start;
                    await file.CopyToAsync(context.Response.OutputStream, 65536, deadline.Token);
                    Interlocked.Add(ref completedDownloadBytes, total - start);
                }
                else if (context.Request.HttpMethod == "POST" && (route == root + "/maps"
                    || route.StartsWith(root + "/maps/", StringComparison.Ordinal) && route.EndsWith("/versions", StringComparison.Ordinal)))
                {
                    if (creator==null) { context.Response.StatusCode = 401; return; }
                    if (context.Request.ContentLength64 > MapPackageReader.MaxArchiveBytes) { context.Response.StatusCode = 413; return; }
                    // Legacy one-shot uploads remain supported. Do not hold
                    // the catalog publication lock while awaiting network body
                    // bytes, even if the sender is slow or disconnects.
                    string temporary = Path.Combine(storage, Guid.NewGuid().ToString("N") + ".upload");
                    try
                    {
                        await using (var file = File.Create(temporary))
                            await MapCommunityClient.CopyBoundedAsync(context.Request.InputStream, file, MapPackageReader.MaxArchiveBytes, deadline.Token);
                        Guid? expected = null;
                        if (route != root + "/maps")
                        {
                            if (!Guid.TryParse(route[(root.Length + 6)..^9], out Guid mapId))
                            { context.Response.StatusCode = 400; return; }
                            expected = mapId;
                        }
                        if (!await publication.WaitAsync(0, deadline.Token))
                        { context.Response.StatusCode = 429; return; }
                        try
                        {
                            var result = PublishTemporary(temporary, creator,
                                context.Request.QueryString["listed"] != "false",
                                context.Request.QueryString["draft"] == "true", expected);
                            context.Response.StatusCode = result.Status;
                            if (result.Entry != null)
                                await Json(context.Response, result.Entry, deadline.Token);
                        }
                        finally { publication.Release(); }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                else context.Response.StatusCode = 404;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[maphub] Request failed: " + ex.GetType().Name);
                try { context.Response.StatusCode = 400; } catch { }
            }
            finally
            {
                // Callers can abort half-written PUT bodies or close Range reads
                // mid-stream. Closing their response must never fault the
                // server's active-worker drain or leak an admission slot.
                try { context.Response.Close(); }
                catch (Exception ex) when (ex is ObjectDisposedException
                    or IOException or HttpListenerException) { }
            }
        }
    }
    private static void CleanupStaleUploads(string storage)
    {
        DateTime cutoff = DateTime.UtcNow - PartialUploadRetention;
        foreach (string metadata in Directory.EnumerateFiles(storage, "upload-*.json"))
        {
            string part = Path.ChangeExtension(metadata, ".part");
            DateTime newest = File.GetLastWriteTimeUtc(metadata);
            if (File.Exists(part) && File.GetLastWriteTimeUtc(part) > newest) newest = File.GetLastWriteTimeUtc(part);
            if (newest >= cutoff) continue;
            try { File.Delete(metadata); } catch { }
            try { if (File.Exists(part)) File.Delete(part); } catch { }
        }
        foreach (string part in Directory.EnumerateFiles(storage, "upload-*.part"))
        {
            string metadata = Path.ChangeExtension(part, ".json");
            if (File.Exists(metadata) || File.GetLastWriteTimeUtc(part) >= cutoff) continue;
            try { File.Delete(part); } catch { }
        }
    }

    /// <summary>
    /// Cheap disk-space preflight at admission boundaries. This does not
    /// promise the whole compressed upload fits (another process may write);
    /// it avoids beginning a chunk when even a bounded request cannot fit.
    /// Real I/O errors still fail closed, preserving partial offset recovery.
    /// </summary>
    private static bool HasAvailableUploadDisk(string storage, long requestBytes)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(storage))
                ?? throw new IOException("Cannot resolve Community storage volume.");
            long available = new DriveInfo(root).AvailableFreeSpace;
            long needed = Math.Min(requestBytes, MapCommunityClient.UploadChunkBytes)
                + 16L * 1024 * 1024;
            return available > needed;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException
            or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("[maphub] Cannot verify storage disk budget: "
                + ex.GetType().Name);
            return false;
        }
    }

    private static long PublishedStorageLimit()
    {
        string? configured = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_STORAGE_GIB");
        if (string.IsNullOrWhiteSpace(configured)) return DefaultPublishedStorageBytes;
        if (!long.TryParse(configured, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out long gib) || gib is < 1 or > 1024)
            throw new InvalidOperationException("PROJECT_PRIME_MAP_STORAGE_GIB must be an integer from 1 through 1024.");
        return checked(gib * 1024L * 1024 * 1024);
    }

    private static CommunityMap Inspect(string path)
    {
        using var package = new MapPackageReader(path);
        var manifest = package.Manifest ?? throw new InvalidDataException("A version 2 manifest is required.");
        if (manifest.Name.Length>128 || manifest.DisplayName?.Length>128 || manifest.Author?.Length>128 || manifest.MapVersion?.Length>64)
            throw new InvalidDataException("Map listing metadata is too long.");
        return new(MapBuildFingerprint.HashFile(path), manifest.MapId, manifest.ContentHash,
            manifest.Name, manifest.DisplayName, manifest.Author, manifest.MapVersion, new FileInfo(path).Length)
        { MinimumProtocol = manifest.MinimumProtocol, SupportedModes = manifest.SupportedModes, MinPlayers = manifest.MinPlayers, MaxPlayers = manifest.MaxPlayers };
    }
    private static async Task Json(HttpListenerResponse response, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, MapPackageReader.JsonOptions);
        response.ContentType = "application/json"; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, token);
    }
}
