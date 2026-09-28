using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

internal static class CommunityMapChecks
{
    public static async Task Run(Action<bool, string> check, string root)
    {
        string storage = Path.Combine(root, "catalog");
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        string address = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port + "/"; port.Stop();
        const string secret = "deterministic-local-test-token-only";
        using var stop = new CancellationTokenSource();
        Task service = MapCommunityServer.ServeAsync(address, storage, secret, stop.Token);
        using var client = new MapCommunityClient(address, secret);
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        Guid id = Guid.NewGuid();
        string Package(string version, string name = "COMMUNITY_TEST")
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".ppmap");
            var definition = new MapDefinition { FormatVersion = 2, MapId = id, Name = name, Version = version, Author = "Fixture" };
            byte[] project = Encoding.UTF8.GetBytes(definition.Serialize());
            var manifest = new MapPackageManifest { MapId = id, Name = name, MapVersion = version, Author = definition.Author,
                ContentHash = MapPackageReader.ContentHash(new[] { "project.json" }, _ => project) };
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var stream = zip.CreateEntry("project.json").Open()) stream.Write(project);
            using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, MapPackageReader.JsonOptions));
            return path;
        }
        try
        {
            string one = Package("1"); var v1 = await client.UploadAsync(one, default);
            var v2 = await client.UploadAsync(Package("2"), default);
            var hidden = await client.UploadAsync(Package("3"), default, listed: false);
            check((await client.GetPackageAsync(hidden.Hash, default))?.Hash == hidden.Hash, "unlisted exact metadata lookup");
            using (var request = new HttpRequestMessage(HttpMethod.Post, "maps/" + id + "/versions") { Content = new ByteArrayContent(File.ReadAllBytes(one)) })
            {
                request.Headers.Authorization = new("Bearer", secret);
                using var response = await http.SendAsync(request);
                check(response.IsSuccessStatusCode, "version publication endpoint accepts matching map ID");
            }
            check((await client.BrowseAsync(default)).Length == 2, "unlisted version excluded from catalog");
            byte[] bytes = await http.GetByteArrayAsync("packages/" + v1.Hash);
            check(bytes.SequenceEqual(File.ReadAllBytes(one)), "historical exact-package endpoint returns immutable bytes");
            check((await http.GetByteArrayAsync("packages/" + hidden.Hash)).Length == hidden.Bytes, "unlisted package remains available by exact hash");
            var versions = JsonSerializer.Deserialize<CommunityMap[]>(await http.GetStringAsync("maps/" + id + "/versions"), MapPackageReader.JsonOptions)!;
            check(versions.Length == 2 && versions[0].Hash == v2.Hash, "version history resolves newest listed package");
            var search = JsonSerializer.Deserialize<CommunityMap[]>(await http.GetStringAsync("maps?query=community&author=Fixture&page=2&pageSize=1"), MapPackageReader.JsonOptions)!;
            check(search.Length == 1, "catalog search and pagination");
            bool conflict = false;
            try { await client.UploadAsync(Package("1", "CHANGED_NAME"), default); }
            catch (HttpRequestException ex) { conflict = ex.StatusCode == HttpStatusCode.Conflict; }
            check(conflict, "changed contents cannot overwrite a published version");
            using var anonymous = new MapCommunityClient(address);
            bool unauthorized = false;
            try { await anonymous.UploadAsync(one, default); } catch (HttpRequestException ex) { unauthorized = ex.StatusCode == HttpStatusCode.Unauthorized; }
            check(unauthorized, "publication requires upload credential");
            var reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => http.GetByteArrayAsync("packages/" + v1.Hash)));
            check(reads.All(b => b.SequenceEqual(bytes)), "concurrent downloads preserve package bytes");
            bool mismatch = false;
            try { using var prepared = await client.PrepareExactAsync(new(id, v1.Name, default, MapHash256.Parse(v1.Hash), true), default); }
            catch (InvalidDataException) { mismatch = true; }
            check(mismatch, "invalid exact identity rejected before installation");
        }
        finally { stop.Cancel(); try { await service; } catch (OperationCanceledException) { } }
        using var restarted = new CancellationTokenSource();
        service = MapCommunityServer.ServeAsync(address, storage, secret, restarted.Token);
        try { check((await client.BrowseAsync(default)).Length == 2, "unlisted visibility persists across service restart"); }
        finally { restarted.Cancel(); try { await service; } catch (OperationCanceledException) { } }
    }
}
