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
        var catalog = new MapCreatorCatalog(storage,secret);
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

        async Task Handle(HttpListenerContext context)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                string route = context.Request.Url!.AbsolutePath.TrimEnd('/');
                string root = new Uri(prefix).AbsolutePath.TrimEnd('/');
                var creator=catalog.Authenticate(context.Request.Headers["Authorization"]);
                string[] parts=route[(root.Length+1)..].Split('/');
                async Task<T> Body<T>()
                {using var data=new MemoryStream();await MapCommunityClient.CopyBoundedAsync(context.Request.InputStream,data,16384,deadline.Token);return JsonSerializer.Deserialize<T>(data.ToArray(),MapPackageReader.JsonOptions)??throw new InvalidDataException("Missing request body.");}
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
                        var entry = Inspect(temporary) with { Listed = context.Request.QueryString["listed"] != "false" && context.Request.QueryString["draft"] != "true", Draft = context.Request.QueryString["draft"] == "true", OwnerId = creator.CreatorId, PublishedAt = DateTimeOffset.UtcNow };
                        if (route != root + "/maps" && (!Guid.TryParse(route[(root.Length + 6)..^9], out Guid mapId) || mapId != entry.MapId))
                        { context.Response.StatusCode=400; return; }
                        var owner=maps.Values.FirstOrDefault(m=>m.MapId==entry.MapId);
                        if(owner!=null&&!catalog.CanPublish(creator.CreatorId,owner)){context.Response.StatusCode=403;return;}
                        if(owner!=null)entry=entry with{OwnerId=owner.OwnerId};
                        if (maps.TryGetValue(entry.Hash, out var existing)) entry = existing;
                        if (maps.Values.Any(m => m.MapId == entry.MapId && m.Version == entry.Version && m.Hash != entry.Hash))
                        { context.Response.StatusCode = 409; return; }
                        if (!maps.ContainsKey(entry.Hash) && (maps.Count >= 2000 || maps.Values.Sum(m => m.Bytes) + entry.Bytes > 2L * 1024 * 1024 * 1024))
                        { context.Response.StatusCode=507; return; }
                        string destination = Path.Combine(storage, entry.Hash + ".ppmap");
                        // Persist visibility before exposing the archive, so a crash cannot publish an unlisted upload.
                        AtomicFile.Write(Path.Combine(storage, entry.Hash + ".catalog.json"), JsonSerializer.SerializeToUtf8Bytes(entry, MapPackageReader.JsonOptions));
                        if (!File.Exists(destination)) File.Move(temporary, destination);
                        maps[entry.Hash] = entry;
                        context.Response.StatusCode = 201;
                        await Json(context.Response, entry, deadline.Token);
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
