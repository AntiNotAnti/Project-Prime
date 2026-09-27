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
        var maps = new Dictionary<string, CommunityMap>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(storage, "*.ppmap").Take(2000))
        {
            try { var entry = Inspect(file); if(Path.GetFileNameWithoutExtension(file)!=entry.Hash)throw new InvalidDataException("Package filename must match its archive hash."); maps.Add(entry.Hash, entry); }
            catch (Exception ex) { Console.Error.WriteLine("[maphub] Skipped package: " + ex.Message); }
        }
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix.TrimEnd('/') + "/"); listener.Start();
        using var registration = token.Register(listener.Close);
        Console.WriteLine("[maphub] Listening at " + prefix);
        // One bounded request at a time prevents concurrent decompression and quota races.
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception) when (token.IsCancellationRequested) { break; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                string route = context.Request.Url!.AbsolutePath.TrimEnd('/');
                string root = new Uri(prefix).AbsolutePath.TrimEnd('/');
                if (route == root + "/health" && context.Request.HttpMethod == "GET")
                    await Json(context.Response, new { Status="ok", Service="prime-maps", Maps=maps.Count }, deadline.Token);
                else if (route == root + "/maps" && context.Request.HttpMethod == "GET")
                    await Json(context.Response, maps.Values.OrderBy(m => m.DisplayName ?? m.Name).ToArray(), deadline.Token);
                else if (route.StartsWith(root + "/maps/", StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
                {
                    string hash = route[(root.Length + 6)..];
                    if (!MapCommunityClient.ValidHash(hash) || !maps.ContainsKey(hash)) { context.Response.StatusCode = 404; continue; }
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength64 = maps[hash].Bytes;
                    await using var file = File.OpenRead(Path.Combine(storage, hash + ".ppmap"));
                    await file.CopyToAsync(context.Response.OutputStream, deadline.Token);
                }
                else if (route == root + "/maps" && context.Request.HttpMethod == "POST")
                {
                    byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + secret));
                    byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["Authorization"] ?? ""));
                    if (!CryptographicOperations.FixedTimeEquals(expected, actual)) { context.Response.StatusCode = 401; continue; }
                    if (context.Request.ContentLength64 > MapPackageReader.MaxArchiveBytes) { context.Response.StatusCode = 413; continue; }
                    string temporary = Path.Combine(storage, Guid.NewGuid().ToString("N") + ".upload");
                    try
                    {
                        await using (var file = File.Create(temporary))
                            await MapCommunityClient.CopyBoundedAsync(context.Request.InputStream, file, MapPackageReader.MaxArchiveBytes, deadline.Token);
                        var entry = Inspect(temporary);
                        if (!maps.ContainsKey(entry.Hash) && (maps.Count >= 2000 || maps.Values.Sum(m => m.Bytes) + entry.Bytes > 2L * 1024 * 1024 * 1024))
                        { context.Response.StatusCode=507; continue; }
                        string destination = Path.Combine(storage, entry.Hash + ".ppmap");
                        if (!File.Exists(destination)) File.Move(temporary, destination);
                        maps[entry.Hash] = entry;
                        context.Response.StatusCode = 201;
                        await Json(context.Response, entry, deadline.Token);
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
            finally { context.Response.Close(); }
        }
    }
    private static CommunityMap Inspect(string path)
    {
        using var package = new MapPackageReader(path);
        var manifest = package.Manifest ?? throw new InvalidDataException("A version 2 manifest is required.");
        if (manifest.Name.Length>128 || manifest.DisplayName?.Length>128 || manifest.Author?.Length>128 || manifest.MapVersion?.Length>64)
            throw new InvalidDataException("Map listing metadata is too long.");
        return new(MapBuildFingerprint.HashFile(path), manifest.MapId, manifest.ContentHash,
            manifest.Name, manifest.DisplayName, manifest.Author, manifest.MapVersion, new FileInfo(path).Length);
    }
    private static async Task Json(HttpListenerResponse response, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, MapPackageReader.JsonOptions);
        response.ContentType = "application/json"; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, token);
    }
}
