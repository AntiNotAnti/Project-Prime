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
    private sealed record MapUploadMetadata(string CreatorId, string PackageHash, long Bytes, bool Listed, bool Draft);
    private const int MaxPartialUploads = 16;
    private const long MaxPartialUploadBytes = 2L * 1024 * 1024 * 1024;
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
        (int Status, CommunityMap? Entry) PublishTemporary(string temporary, MapCreatorCredential creator,
            bool listed, bool draft, Guid? expectedMapId = null)
        {
            var entry = Inspect(temporary) with
            {
                Listed = listed && !draft,
                Draft = draft,
                OwnerId = creator.CreatorId,
                PublishedAt = DateTimeOffset.UtcNow
            };
            if (expectedMapId is Guid required && required != entry.MapId) return (400, null);
            var owner = maps.Values.FirstOrDefault(m => m.MapId == entry.MapId);
            if (owner != null && !catalog.CanPublish(creator.CreatorId, owner)) return (403, null);
            if (owner != null) entry = entry with { OwnerId = owner.OwnerId };
            if (maps.TryGetValue(entry.Hash, out var existing)) entry = existing;
            if (maps.Values.Any(m => m.MapId == entry.MapId && m.Version == entry.Version && m.Hash != entry.Hash))
                return (409, null);
            if (!maps.ContainsKey(entry.Hash) && (maps.Count >= 2000
                || maps.Values.Sum(m => m.Bytes) + entry.Bytes > 2L * 1024 * 1024 * 1024))
                return (507, null);
            string destination = Path.Combine(storage, entry.Hash + ".ppmap");
            // Persist visibility before exposing the archive, so a crash cannot publish an unlisted upload.
            AtomicFile.Write(Path.Combine(storage, entry.Hash + ".catalog.json"),
                JsonSerializer.SerializeToUtf8Bytes(entry, MapPackageReader.JsonOptions));
            if (!File.Exists(destination)) File.Move(temporary, destination);
            maps[entry.Hash] = entry;
            return (201, entry);
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
                                    if (File.Exists(UploadMetadataPath(key))) File.Delete(UploadMetadataPath(key));
                                    if (File.Exists(partial)) File.Delete(partial);
                                    await Json(context.Response, result.Entry, deadline.Token);
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
                        AtomicFile.Write(Path.Combine(storage,map.Hash+".catalog.json"),JsonSerializer.SerializeToUtf8Bytes(updated,MapPackageReader.JsonOptions));maps[map.Hash]=updated;context.Response.StatusCode=204;
                    }
                    finally{publication.Release();}return;
                }
                if(parts[0]=="reports")
                {
                    if(creator?.Moderator!=true){context.Response.StatusCode=403;return;}
                    if(parts.Length==1&&context.Request.HttpMethod=="GET")await Json(context.Response,catalog.Reports(),deadline.Token);
                    else if(parts.Length==2&&Guid.TryParse(parts[1],out var report)&&context.Request.HttpMethod=="POST"){catalog.SetReportStatus(report,await Body<string>());context.Response.StatusCode=204;}
                    else context.Response.StatusCode=404;return;
                }
                if (route == root + "/health" && context.Request.HttpMethod == "GET")
                    await Json(context.Response, new { Status="ok", Service="prime-maps", Maps=maps.Count }, deadline.Token);
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
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength64 = maps[hash].Bytes;
                    await using var file = File.OpenRead(Path.Combine(storage, hash + ".ppmap"));
                    await file.CopyToAsync(context.Response.OutputStream, deadline.Token);
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
