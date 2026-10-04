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
        string storage = Path.Combine(root, "catalog");Directory.CreateDirectory(storage);
        const string creatorToken="creator-one-local-fixture-token", collaboratorToken="creator-two-local-fixture-token";
        string TokenHash(string token)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(storage,"creators.json"),JsonSerializer.Serialize(new[]{new MapCreatorCredential("creator-one",TokenHash(creatorToken)),new MapCreatorCredential("creator-two",TokenHash(collaboratorToken))},MapPackageReader.JsonOptions));
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        string address = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port + "/"; port.Stop();
        const string secret = "deterministic-local-test-token-only";
        using var stop = new CancellationTokenSource();
        Task service = MapCommunityServer.ServeAsync(address, storage, secret, stop.Token);
        using var client = new MapCommunityClient(address, secret);
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        Guid id = Guid.NewGuid();
        string? resumablePath = null, resumableHash = null;
        long resumableOffset = 0;
        string PackageBytes(string version, string name, int? projectLength = null, string projectEntry = "project.json",
            Action<MapDefinition>? configure = null)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".ppmap");
            var definition = new MapDefinition { FormatVersion = 2, MapId = id, Name = name, Version = version, Author = "Fixture" };
            configure?.Invoke(definition);
            byte[] serialized = Encoding.UTF8.GetBytes(definition.Serialize());
            int length = projectLength ?? serialized.Length;
            if (length < serialized.Length) throw new ArgumentOutOfRangeException(nameof(projectLength));
            byte[] project = new byte[length];
            Buffer.BlockCopy(serialized, 0, project, 0, serialized.Length);
            if (length > serialized.Length) Array.Fill(project, (byte)' ', serialized.Length, length - serialized.Length);
            var manifest = new MapPackageManifest { MapId = id, Name = name, MapVersion = version, Author = definition.Author,
                ContentHash = MapPackageReader.ContentHash(new[] { projectEntry }, _ => project) };
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var stream = zip.CreateEntry(projectEntry).Open()) stream.Write(project);
            using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, MapPackageReader.JsonOptions));
            return path;
        }
        string Package(string version, string name = "COMMUNITY_TEST") => PackageBytes(version, name);
        string CatalogOnlyFixture()
        {
            string path = Path.Combine(root, "catalog-lightweight.ppmap");
            Guid mapId = Guid.NewGuid();
            const string previewPath = "preview/card.png";
            var definition = new MapDefinition
            {
                FormatVersion = 2, MapId = mapId, Name = "CATALOG_FAST_PATH", Version = "1"
            };
            definition.Assets.Add(new MapAsset { Path = previewPath, Kind = "preview" });
            byte[] project = Encoding.UTF8.GetBytes(definition.Serialize());
            byte[] preview = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13,
                73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1 };
            // Deliberately wrong digest. Catalog/presentation reads should not
            // stream every asset to verify it; strict use must still reject it.
            var manifest = new MapPackageManifest
            {
                MapId = mapId, Name = definition.Name, MapVersion = definition.Version,
                ContentHash = new string('0', 64), Preview = previewPath
            };
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var stream = zip.CreateEntry("project.json").Open()) stream.Write(project);
            using (var stream = zip.CreateEntry(previewPath).Open()) stream.Write(preview);
            using (var stream = zip.CreateEntry("manifest.json").Open())
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, MapPackageReader.JsonOptions));
            return path;
        }
        try
        {
            string animatedPackage = PackageBytes("animated-roundtrip", "ANIMATED_PACKAGE", configure: definition =>
            {
                definition.Materials.Add(new MapMaterial
                {
                    Name = "water",
                    SourceMaterial = 0,
                    Alpha = 22,
                    TwoSided = true,
                    Animation = new MapMaterialAnimation
                    {
                        UvScroll = new[] { 0f, -0.8f },
                        UvRotationDegreesPerSecond = 180f,
                        UvScale = new[] { 1f, 0.9f },
                        UvScalePulse = new[] { 0.05f, 0.1f },
                        EmissiveIntensity = 0.6f,
                        EmissivePulse = 0.3f,
                        FlipbookFrames = new() { "textures/frame2.tex", "textures/frame3.tex" },
                        FlipbookHoldFrames = 5,
                        LoopFrames = 3000,
                        PhaseFrames = 15
                    }
                });
            });
            MapDefinition animatedRoundTrip = MapDefinition.Load(animatedPackage);
            var animatedMaterial = animatedRoundTrip.Materials.Single();
            check(animatedMaterial.Alpha == 22 && animatedMaterial.TwoSided
                && animatedMaterial.Animation?.UvScroll.SequenceEqual(new[] { 0f, -0.8f }) == true
                && animatedMaterial.Animation.UvRotationDegreesPerSecond == 180f
                && animatedMaterial.Animation.UvScale.SequenceEqual(new[] { 1f, 0.9f })
                && animatedMaterial.Animation.UvScalePulse.SequenceEqual(new[] { 0.05f, 0.1f })
                && animatedMaterial.Animation.EmissiveIntensity == 0.6f
                && animatedMaterial.Animation.EmissivePulse == 0.3f
                && animatedMaterial.Animation.FlipbookFrames.SequenceEqual(new[] { "textures/frame2.tex", "textures/frame3.tex" })
                && animatedMaterial.Animation.FlipbookHoldFrames == 5
                && animatedMaterial.Animation.LoopFrames == 3000
                && animatedMaterial.Animation.PhaseFrames == 15,
                "animated material metadata survives editable .ppmap package roundtrip");

            string catalogFixture = CatalogOnlyFixture();
            string catalogProject = MapPackageReader.ReadProjectForCatalog(catalogFixture);
            check(catalogProject.Contains("CATALOG_FAST_PATH", StringComparison.Ordinal),
                "catalog package read does not require full content hashing");
            check(MapPackageReader.ReadCatalogEntry(catalogFixture, "preview/card.png")?.Length == 24,
                "catalog preview reads one bounded entry without full package hashing");
            bool strictCatalogRejected = false;
            try { using var strict = new MapPackageReader(catalogFixture); }
            catch (InvalidDataException) { strictCatalogRejected = true; }
            check(strictCatalogRejected,
                "strict package reader still rejects a digest mismatch at trust boundaries");

            string validLargeProject = PackageBytes("reader-large", "PROJECT_LIMIT_OK",
                checked((int)(MapPackageReader.MaxProjectBytes / 2)));
            using (var reader = new MapPackageReader(validLargeProject))
                check(Encoding.UTF8.GetByteCount(reader.ReadProject()) == MapPackageReader.MaxProjectBytes / 2,
                    "multi-megabyte project.json remains valid below the dedicated project limit");

            string exactLimitProject = PackageBytes("reader-exact", "PROJECT_LIMIT_EXACT",
                checked((int)MapPackageReader.MaxProjectBytes));
            using (var reader = new MapPackageReader(exactLimitProject))
                check(Encoding.UTF8.GetByteCount(reader.ReadProject()) == MapPackageReader.MaxProjectBytes,
                    "project.json at the exact dedicated limit is accepted");

            string oversizedProject = PackageBytes("reader-oversized", "PROJECT_LIMIT_TOO_LARGE",
                checked((int)MapPackageReader.MaxProjectBytes + 1));
            string oversizedError = "";
            try { using var ignored = new MapPackageReader(oversizedProject); }
            catch (InvalidDataException ex) { oversizedError = ex.Message; }
            check(oversizedError.Contains("project.json", StringComparison.Ordinal)
                && oversizedError.Contains("oversized", StringComparison.OrdinalIgnoreCase)
                && !oversizedError.Contains("Missing or oversized", StringComparison.Ordinal),
                "oversized project.json reports the actual entry size failure");

            string missingProject = PackageBytes("reader-missing", "PROJECT_MISSING", projectEntry: "nested/project.json");
            string missingError = "";
            try { using var ignored = new MapPackageReader(missingProject); }
            catch (InvalidDataException ex) { missingError = ex.Message; }
            check(missingError == "Missing package entry: project.json",
                "missing root project.json is distinguished from an oversized project");

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

            string storedV1 = Path.Combine(storage, v1.Hash + ".ppmap");
            byte[] storedBytes = File.ReadAllBytes(storedV1);
            byte[] corruptBytes = (byte[])storedBytes.Clone();
            corruptBytes[corruptBytes.Length / 2] ^= 0x5A;
            File.WriteAllBytes(storedV1, corruptBytes);
            bool rejectedCorruptDownload = false;
            try { await client.InstallAsync(v1, Path.Combine(root, "corrupt-install"), default); }
            catch (InvalidDataException ex)
            {
                rejectedCorruptDownload = ex.Message.Contains("SHA-256", StringComparison.Ordinal)
                    && ex.Message.Contains("discarded after a retry", StringComparison.OrdinalIgnoreCase);
            }
            finally { File.WriteAllBytes(storedV1, storedBytes); }
            check(rejectedCorruptDownload,
                "same-length corrupted community download is rejected by package hash before ZIP parsing");

            long rangeStart = Math.Max(1, bytes.LongLength / 2);
            using (var request = new HttpRequestMessage(HttpMethod.Get, "packages/" + v1.Hash))
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, null);
                using var response = await http.SendAsync(request);
                byte[] suffix = await response.Content.ReadAsByteArrayAsync();
                check(response.StatusCode == HttpStatusCode.PartialContent
                    && response.Headers.AcceptRanges.Contains("bytes")
                    && response.Content.Headers.ContentRange?.From == rangeStart
                    && response.Content.Headers.ContentRange?.Length == bytes.LongLength
                    && suffix.SequenceEqual(bytes.AsSpan(checked((int)rangeStart)).ToArray()),
                    "package endpoint returns exact byte ranges for resumable downloads");
            }
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
            using var creator=new MapCommunityClient(address,creatorToken);
            bool forbidden=false;try{await creator.UploadAsync(one,default);}catch(HttpRequestException ex){forbidden=ex.StatusCode==HttpStatusCode.Forbidden;}
            check(forbidden,"another authenticated creator cannot publish an owner's map");
            await creator.SetFavoriteAsync(id,true,default);await creator.SetFavoriteAsync(id,true,default);
            var favorites=await creator.BrowseAsync(default,favorites:true,sort:"favorites");
            check(favorites.Length==2&&favorites.All(m=>m.FavoriteCount==1&&m.Favorited),"favorites are idempotent and visible in personal filter");
            await creator.ReportAsync(id,new("1","Broken map","Fixture report"),default);
            check((await client.BrowseAsync(default)).Length==2,"reports never automatically delist packages");
            using(var request=new HttpRequestMessage(HttpMethod.Get,"reports"))
            {request.Headers.Authorization=new("Bearer",secret);using var response=await http.SendAsync(request);var reports=JsonSerializer.Deserialize<CommunityMapReport[]>(await response.Content.ReadAsStringAsync(),MapPackageReader.JsonOptions)!;check(reports.Length==1&&reports[0].Status=="Open"&&reports[0].ReporterId=="creator-one","moderator can inspect persisted report identity");}
            var draft=await client.UploadAsync(Package("4"),default,draft:true);
            check((await client.BrowseAsync(default,mine:true)).Length==4,"My Maps includes published, unlisted and draft versions");
            forbidden=false;try{await anonymous.GetPackageAsync(draft.Hash,default);}catch(HttpRequestException ex){forbidden=ex.StatusCode==HttpStatusCode.Forbidden;}check(forbidden,"unpublished draft packages require creator authorization");
            using(var request=new HttpRequestMessage(HttpMethod.Post,"maps/"+id+"/collaborators"){Content=new StringContent("[\"creator-two\"]",Encoding.UTF8,"application/json")})
            {request.Headers.Authorization=new("Bearer",secret);using var response=await http.SendAsync(request);check(response.IsSuccessStatusCode,"owner can authorize a collaborator");}
            using var collaborator=new MapCommunityClient(address,collaboratorToken);
            check((await collaborator.UploadAsync(Package("5"),default,listed:false)).OwnerId==MapCreatorCatalog.ServiceOwner,"collaborator publication preserves original owner");
            var reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => http.GetByteArrayAsync("packages/" + v1.Hash)));
            check(reads.All(b => b.SequenceEqual(bytes)), "concurrent downloads preserve package bytes");
            bool mismatch = false;
            try { using var prepared = await client.PrepareExactAsync(new(id, v1.Name, default, MapHash256.Parse(v1.Hash), true), default); }
            catch (InvalidDataException) { mismatch = true; }
            check(mismatch, "invalid exact identity rejected before installation");

            // Leave a valid archive half-uploaded, stop the service, and let the
            // normal client continue it after restart. This exercises persistence
            // rather than merely retrying another request in the same process.
            resumablePath = Package("6");
            resumableHash = MapBuildFingerprint.HashFile(resumablePath);
            byte[] resumableBytes = File.ReadAllBytes(resumablePath);
            resumableOffset = Math.Max(1, resumableBytes.LongLength / 2);
            using (var begin = new HttpRequestMessage(HttpMethod.Post, "uploads/" + resumableHash)
            {
                Content = new StringContent(JsonSerializer.Serialize(
                    new MapUploadStartRequest(resumableBytes.LongLength, Listed: false, Draft: false), MapPackageReader.JsonOptions),
                    Encoding.UTF8, "application/json")
            })
            {
                begin.Headers.Authorization = new("Bearer", secret);
                using var response = await http.SendAsync(begin);
                check(response.IsSuccessStatusCode, "resumable upload session begins");
            }
            using (var chunk = new HttpRequestMessage(HttpMethod.Put,
                "uploads/" + resumableHash + "?offset=0")
            {
                Content = new ByteArrayContent(resumableBytes.AsSpan(0, checked((int)resumableOffset)).ToArray())
            })
            {
                chunk.Headers.Authorization = new("Bearer", secret);
                using var response = await http.SendAsync(chunk);
                var state = JsonSerializer.Deserialize<MapUploadState>(
                    await response.Content.ReadAsByteArrayAsync(), MapPackageReader.JsonOptions)!;
                check(response.IsSuccessStatusCode && state.Offset == resumableOffset && !state.Complete,
                    "partial map upload records an exact resumable offset");
            }
        }
        finally { stop.Cancel(); try { await service; } catch (OperationCanceledException) { } }
        using var restarted = new CancellationTokenSource();
        service = MapCommunityServer.ServeAsync(address, storage, secret, restarted.Token);
        try
        {
            var resumed = await client.UploadAsync(resumablePath!, default, listed: false);
            check(resumed.Hash == resumableHash && resumed.Bytes == new FileInfo(resumablePath!).Length,
                "client resumes a persisted partial upload after service restart");
            check(!Directory.EnumerateFiles(storage, "upload-*.json").Any()
                && !Directory.EnumerateFiles(storage, "upload-*.part").Any(),
                "completed resumable upload removes partial session files");
            check((await client.BrowseAsync(default)).Length == 2, "unlisted visibility persists across service restart");
        }
        finally { restarted.Cancel(); try { await service; } catch (OperationCanceledException) { } }
    }
}
