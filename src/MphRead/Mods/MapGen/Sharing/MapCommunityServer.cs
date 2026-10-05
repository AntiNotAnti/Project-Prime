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
    private const int MaxPartialUploads = 16;
    private const long MaxPartialUploadBytes = 2L * 1024 * 1024 * 1024;
    private const long DefaultPublishedStorageBytes = 50L * 1024 * 1024 * 1024;
    private static readonly TimeSpan PartialUploadRetention = TimeSpan.FromHours(24);

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
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix.TrimEnd('/') + "/"); listener.Start();
        using var registration = token.Register(listener.Close);
        Console.WriteLine("[maphub] Listening at " + prefix);
        using var slots = new SemaphoreSlim(8);
        using var publication = new SemaphoreSlim(1);
        var uploadLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        var running = new List<Task>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                await slots.WaitAsync(token);
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) when (token.IsCancellationRequested) { slots.Release(); break; }
                running.RemoveAll(t => t.IsCompleted);
                running.Add(Task.Run(async () => { try { await Handle(context); } finally { slots.Release(); } }));
            }
        }
        finally { await Task.WhenAll(running); }

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
        }
        MapUploadState UploadState(string key, MapUploadMetadata upload)
        {
            string part = UploadPartPath(key);
            long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset < 0 || offset > upload.Bytes) throw new InvalidDataException("Partial upload has an invalid size.");
            return new(upload.PackageHash, upload.Bytes, offset, MapCommunityClient.UploadChunkBytes, false);
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

            if (!maps.ContainsKey(entry.Hash) && (maps.Count >= 2000
                || maps.Values.Sum(m => m.Bytes) + entry.Bytes > publishedStorageLimit))
                return new PublishResult(507);
            string destination = Path.Combine(storage, entry.Hash + ".ppmap");
            AtomicFile.Write(Path.Combine(storage, entry.Hash + ".catalog.json"),
                JsonSerializer.SerializeToUtf8Bytes(entry, MapPackageReader.JsonOptions));
            if (!File.Exists(destination)) File.Move(temporary, destination);
            maps[entry.Hash] = entry;
            revisionCatalog.Register(entry, creator.CreatorId,
                revisionUpload?.ExpectedParentHash,
                revisionUpload?.ReleaseNotes,
                useProvidedParent: revisionUpload?.ExistingMap != null);
            return new PublishResult(201, entry);
        }

        async Task Handle(HttpListenerContext context)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
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

                            var upload = LoadUpload(key);
                            if (upload == null || upload.CreatorId != creator.CreatorId || upload.PackageHash != hash || upload.Bytes != start.Bytes)
                            {
                                DeleteUpload(key);
                                int sessions = Directory.EnumerateFiles(storage, "upload-*.json").Take(MaxPartialUploads).Count();
                                long partialBytes = Directory.EnumerateFiles(storage, "upload-*.part")
                                    .Select(p => { try { return new FileInfo(p).Length; } catch { return 0L; } }).Sum();
                                if (sessions >= MaxPartialUploads) { context.Response.StatusCode = 429; return; }
                                if (partialBytes + start.Bytes > MaxPartialUploadBytes) { context.Response.StatusCode = 507; return; }
                                upload = new(creator.CreatorId, hash, start.Bytes, start.Listed, start.Draft);
                                SaveUpload(key, upload);
                            }
                            else if (upload.Listed != start.Listed || upload.Draft != start.Draft)
                            {
                                upload = upload with { Listed = start.Listed, Draft = start.Draft };
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
                            long incoming = context.Request.ContentLength64 >= 0 ? context.Request.ContentLength64 : maximum;
                            long partialBytes = Directory.EnumerateFiles(storage, "upload-*.part")
                                .Select(p => { try { return new FileInfo(p).Length; } catch { return 0L; } }).Sum();
                            if (partialBytes + incoming > MaxPartialUploadBytes)
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
                                var result = PublishTemporary(partial, creator, current.Listed, current.Draft);
                                context.Response.StatusCode = result.Status;
                                if (result.Entry != null)
                                {
                                    DeleteUpload(key);
                                    await Json(context.Response, result.Entry, deadline.Token);
                                }
                                else if (result.Status == 409)
                                {
                                    // The completed bytes cannot be published under this
                                    // human version. Keeping a full rejected session only
                                    // consumes partial-upload storage and makes later cleanup
                                    // dependent on the 24-hour stale-session sweep.
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
                        string visibility=await Body<string>();if(visibility is not ("Published" or "Unlisted" or "Draft"))throw new InvalidDataException("Invalid visibility.");
                        var updated=map with{Listed=visibility=="Published",Draft=visibility=="Draft"};
                        AtomicFile.Write(Path.Combine(storage,map.Hash+".catalog.json"),JsonSerializer.SerializeToUtf8Bytes(updated,MapPackageReader.JsonOptions));
                        maps[map.Hash]=updated;
                        revisionCatalog.ApplyVisibility(updated,maps.Values);
                        context.Response.StatusCode=204;
                    }
                    finally{publication.Release();}return;
                }
                if(parts.Length>=2&&parts[0]=="v2"&&parts[1]=="maps"&&context.Request.HttpMethod=="GET")
                {
                    var query=context.Request.QueryString;
                    bool mine=query["mine"]=="true",favorites=query["favorites"]=="true";
                    if((mine||favorites)&&creator==null){context.Response.StatusCode=401;return;}

                    if(parts.Length==2)
                    {
                        CommunityMap[] visible=maps.Values
                            .Where(m=>mine ? creator!=null&&catalog.CanPublish(creator.CreatorId,m) : m.Listed&&!m.Draft)
                            .Select(m=>catalog.Decorate(m,creator?.CreatorId))
                            .ToArray();
                        IEnumerable<CommunityMapProject> projects=visible
                            .GroupBy(m=>m.MapId)
                            .Select(g=>revisionCatalog.BuildProject(g.Key,g,revealCreatorIdentity:mine))
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
                        if(query["page"]!=null||query["pageSize"]!=null)
                        {
                            int page=int.TryParse(query["page"],out int p)?Math.Clamp(p,1,2000):1;
                            int size=int.TryParse(query["pageSize"],out int n)?Math.Clamp(n,1,100):24;
                            projects=projects.Skip((page-1)*size).Take(size);
                        }
                        await Json(context.Response,projects.ToArray(),deadline.Token);
                        return;
                    }

                    if((parts.Length is 3 or 4)&&Guid.TryParse(parts[2],out Guid projectId))
                    {
                        CommunityMap[] all=maps.Values.Where(m=>m.MapId==projectId).ToArray();
                        if(all.Length==0){context.Response.StatusCode=404;return;}
                        bool canManage=creator!=null&&all.Any(m=>catalog.CanPublish(creator.CreatorId,m));
                        CommunityMap[] visible=all
                            .Where(m=>canManage||m.Listed&&!m.Draft)
                            .Select(m=>catalog.Decorate(m,creator?.CreatorId))
                            .ToArray();
                        if(visible.Length==0){context.Response.StatusCode=404;return;}
                        if(parts.Length==3)
                        {
                            var project=revisionCatalog.BuildProject(projectId,visible,revealCreatorIdentity:canManage);
                            if(project==null){context.Response.StatusCode=404;return;}
                            await Json(context.Response,project,deadline.Token);
                            return;
                        }
                        if(parts[3]=="revisions")
                        {
                            await Json(context.Response,revisionCatalog.BuildRevisions(projectId,visible,revealCreatorIdentity:canManage),deadline.Token);
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
                        PublishedBytes=maps.Values.Sum(m => m.Bytes),
                        StorageLimitBytes=publishedStorageLimit
                    }, deadline.Token);
                else if (route == root + "/maps" && context.Request.HttpMethod == "GET")
                    {
                    var query = context.Request.QueryString;
                    if((query["mine"]=="true"||query["favorites"]=="true")&&creator==null){context.Response.StatusCode=401;return;}
                    IEnumerable<CommunityMap> found = maps.Values.Where(m => query["mine"]=="true" ? creator!=null&&catalog.CanPublish(creator.CreatorId,m) : m.Listed&&!m.Draft).Select(m=>catalog.Decorate(m,creator?.CreatorId));
                    if(query["favorites"]=="true")found=found.Where(m=>m.Favorited);
                    if (query["query"] is { } search) found = found.Where(m => (m.Name + " " + m.DisplayName + " " + m.Author).Contains(search, StringComparison.OrdinalIgnoreCase));
                    if (query["mode"] is { } mode) found = found.Where(m => m.SupportedModes.Length == 0 || m.SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase));
                    if (query["author"] is { } author) found = found.Where(m => string.Equals(m.Author, author, StringComparison.OrdinalIgnoreCase));
                    found = query["sort"]=="favorites" ? found.OrderByDescending(m=>m.FavoriteCount).ThenBy(m=>m.Name) : query["sort"] is "new" or "updated" ? found.OrderByDescending(m => m.PublishedAt) : found.OrderBy(m => m.DisplayName ?? m.Name);
                    if (query["page"] != null || query["pageSize"] != null)
                    {
                        int page = int.TryParse(query["page"], out int p) ? Math.Clamp(p, 1, 2000) : 1;
                        int size = int.TryParse(query["pageSize"], out int n) ? Math.Clamp(n, 1, 100) : 24;
                        found = found.Skip((page - 1) * size).Take(size);
                    }
                    await Json(context.Response, found.ToArray(), deadline.Token);
                }
                else if ((route.StartsWith(root + "/maps/", StringComparison.Ordinal) || route.StartsWith(root + "/packages/", StringComparison.Ordinal)) && context.Request.HttpMethod == "GET")
                {
                    string tail = route[(root.Length + 1)..];
                    string hash = tail[(tail.IndexOf('/') + 1)..];
                    string idText = hash.Split('/')[0];
                    if (tail.StartsWith("maps/", StringComparison.Ordinal) && Guid.TryParse(idText, out Guid id))
                    {
                        var versions = maps.Values.Where(m => m.MapId == id && (m.Listed&&!m.Draft || creator!=null&&catalog.CanPublish(creator.CreatorId,m))).OrderByDescending(m => m.PublishedAt).ToArray();
                        if (versions.Length == 0 || hash != idText && hash != idText + "/versions") { context.Response.StatusCode = 404; return; }
                        await Json(context.Response, hash.EndsWith("/versions", StringComparison.Ordinal) ? (object)versions : versions[0], deadline.Token);
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
                    long total = maps[hash].Bytes, start = 0;
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
                }
                else if (context.Request.HttpMethod == "POST" && (route == root + "/maps"
                    || route.StartsWith(root + "/maps/", StringComparison.Ordinal) && route.EndsWith("/versions", StringComparison.Ordinal)))
                {
                    if (creator==null) { context.Response.StatusCode = 401; return; }
                    if (context.Request.ContentLength64 > MapPackageReader.MaxArchiveBytes) { context.Response.StatusCode = 413; return; }
                    if (!await publication.WaitAsync(0, deadline.Token)) { context.Response.StatusCode = 429; return; }
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
                        var result = PublishTemporary(temporary, creator,
                            context.Request.QueryString["listed"] != "false",
                            context.Request.QueryString["draft"] == "true", expected);
                        context.Response.StatusCode = result.Status;
                        if (result.Entry != null) await Json(context.Response, result.Entry, deadline.Token);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); publication.Release(); }
                }
                else context.Response.StatusCode = 404;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[maphub] Request failed: " + ex.GetType().Name);
                try { context.Response.StatusCode = 400; } catch { }
            }
            finally { try { context.Response.Close(); } catch (ObjectDisposedException) { } }
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
