using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// PR-08 is a read-only HTTP schema migration. Non-map registration here uses
// only trusted local service-side metadata, never a remote upload endpoint.
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
string root = Path.Combine(Path.GetTempPath(),
    "prime-community-v3-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string store = Path.Combine(root, "store");
Directory.CreateDirectory(store);
const string secret = "prime-community-v3-readthrough-fixture-secret";
CancellationTokenSource? stop = null;
Task? server = null;

async Task<(string Endpoint, HttpClient Http)> StartAsync()
{
    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    string endpoint = "http://127.0.0.1:" + port + "/community/";
    stop = new CancellationTokenSource();
    server = MapCommunityServer.ServeAsync(endpoint, store, secret, stop.Token);
    var client = new HttpClient { BaseAddress = new Uri(endpoint),
        Timeout = TimeSpan.FromSeconds(20) };
    var deadline = DateTime.UtcNow.AddSeconds(12);
    while (true)
    {
        try
        {
            using var response = await client.GetAsync("health");
            if (response.IsSuccessStatusCode) return (endpoint, client);
        }
        catch (HttpRequestException) { }
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException("V3 test service did not become ready.");
        await Task.Delay(40);
    }
}

async Task StopAsync()
{
    if (stop == null) return;
    stop.Cancel();
    if (server != null)
    {
        try { await server.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { }
    }
    stop.Dispose();
    stop = null;
    server = null;
}

try
{
    var registry = new CommunityContentRegistry(store);
    Guid textureId = Guid.Parse("be2b9795-c1d0-4d70-b5a4-27c74de5d3ae");
    string image = Path.Combine(root, "texture.png");
    File.WriteAllBytes(image, Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO/aegAAAABJRU5ErkJggg=="));
    var textureMeta = new CommunityContentRegistration(
        CommunityContentKinds.Texture, textureId, MapCreatorCatalog.ServiceOwner,
        "Example texture", "HD Texture", "Fixture Creator",
        "Small authored image", new[] { "hd", "materials" },
        "image/png", CommunityContentVisibility.Published);
    CommunityContentProject texture = registry.RegisterVerifiedBlob(image, textureMeta);
    string firstTextureHash = texture.CurrentHash!;
    Check(texture.Kind == CommunityContentKinds.Texture
        && texture.LatestHash == MapBuildFingerprint.HashFile(image)
        && texture.CurrentRevision?.ArchiveHash == null,
        "generic texture content is SHA-256 addressed, not a synthetic .ppmap archive");

    CommunityContentProject duplicate = registry.RegisterVerifiedBlob(image, textureMeta);
    Check(duplicate.RevisionCount == 1,
        "identical immutable asset registration never makes a duplicate revision");

    byte[] edited = File.ReadAllBytes(image);
    Array.Resize(ref edited, edited.Length + 1);
    edited[^1] = 0;
    File.WriteAllBytes(image, edited);
    string privateHash = MapBuildFingerprint.HashFile(image);
    bool parentFailed = false;
    try
    {
        registry.RegisterVerifiedBlob(image, textureMeta with
        {
            Visibility = CommunityContentVisibility.Draft,
            ExpectedParentHash = new string('f', 64)
        });
    }
    catch (InvalidDataException) { parentFailed = true; }
    Check(parentFailed, "new version rejects a stale parent without changing catalog");

    var privateTexture = registry.RegisterVerifiedBlob(image, textureMeta with
    {
        Visibility = CommunityContentVisibility.Draft,
        ExpectedParentHash = firstTextureHash,
        ReleaseNotes = "Draft retouch"
    });
    Check(privateTexture.RevisionCount == 2
        && privateTexture.LatestHash == privateHash
        && privateTexture.CurrentHash == firstTextureHash,
        "draft revision advances latest without changing public current pointer");
    Check(registry.GetVisible(CommunityContentKinds.Texture, textureId, null) is { } pub
        && pub.LatestHash == firstTextureHash
        && pub.RevisionCount == 1 && !pub.CanManage
        && pub.OwnerId == null,
        "anonymous readers cannot discover or infer an owner's draft revision");
    Check(registry.GetVisible(CommunityContentKinds.Texture, textureId,
        MapCreatorCatalog.ServiceOwner) is { } own && own.CanManage
        && own.OwnerId == MapCreatorCatalog.ServiceOwner
        && own.LatestHash == privateHash,
        "owner view includes both immutable revisions");

    Guid materialId = Guid.Parse("cc9da765-4606-4eb0-9e98-34093d7b3f09");
    string materialFile = Path.Combine(root, "material.json");
    File.WriteAllText(materialFile, "{\"shader\":\"test\",\"color\":\"blue\"}");
    var material = registry.RegisterVerifiedBlob(materialFile,
        new CommunityContentRegistration(CommunityContentKinds.Material,
            materialId, MapCreatorCatalog.ServiceOwner, "Blue material",
            "Blue PBR", "Fixture Creator", "Material metadata",
            new[] { "materials" }, "application/vnd.projectprime.material+json",
            CommunityContentVisibility.Published,
            Dependencies: new[]
            {
                new CommunityContentDependency(CommunityContentKinds.Texture,
                    textureId, firstTextureHash)
            }));
    Check(material.CurrentRevision?.Dependencies is { Length: 1 }
        && material.CurrentRevision.Dependencies[0].Hash == firstTextureHash,
        "generic dependencies pin immutable upstream content hashes");

    Guid prefabId = Guid.Parse("a58e3ce6-ae4a-43e8-af17-302691a2f51f");
    string prefabFile = Path.Combine(root, "prefab.json");
    File.WriteAllText(prefabFile, "{\"entities\":[]}");
    registry.RegisterVerifiedBlob(prefabFile,
        new CommunityContentRegistration(CommunityContentKinds.Prefab,
            prefabId, MapCreatorCatalog.ServiceOwner, "Private prefab",
            "Draft bundle", "Fixture Creator", "Not yet published",
            Array.Empty<string>(), "application/vnd.projectprime.prefab+json",
            CommunityContentVisibility.Draft));

    Check(registry.GetVisible(CommunityContentKinds.Prefab, prefabId, null) == null
        && registry.GetVisible(CommunityContentKinds.Prefab, prefabId,
            MapCreatorCatalog.ServiceOwner) != null,
        "private prefab has no public discovery or detail view");

    using (var reloaded = new RegistryCheck(new CommunityContentRegistry(store)))
    {
        Check(reloaded.Value.GetVisible(CommunityContentKinds.Texture, textureId,
                MapCreatorCatalog.ServiceOwner)?.RevisionCount == 2
            && reloaded.Value.GetVisible(CommunityContentKinds.Material, materialId, null) != null,
            "typed v3 project and revision metadata survives exact service restart");
    }

    var mapDefinition = MapTemplates.Create("V3_READTHROUGH", true).Definition;
    string mapPath = MapPackageBuilder.Build(mapDefinition,
        Path.Combine(root, "map.ppmap"));
    string firstMapHash = MapBuildFingerprint.HashFile(mapPath);
    Guid mapId = mapDefinition.MapId;

    string address;
    {
        var instance = await StartAsync();
        address = instance.Endpoint;
        using var http = instance.Http;
        using var publisher = new MapCommunityClient(address, secret);
        CommunityMap map = await publisher.UploadAsync(mapPath, CancellationToken.None);
        Check(map.Hash == firstMapHash,
            "v2 publishing remains authoritative for maps despite generic v3 store");

        using var viewer = new MapCommunityClient(address);
        var capability = await viewer.GetCapabilitiesAsync();
        Check(capability is { SupportsGenericContentCatalog: true,
            SupportsGenericContentPublishing: false }
            && capability.SupportedKinds.Contains(CommunityContentKinds.Map)
            && capability.SupportedKinds.Contains(CommunityContentKinds.Model),
            "v3 capability negotiation advertises read-only typed catalog, no asset upload");

        CommunityContentPage publicAll = await viewer.BrowseContentAsync(CancellationToken.None);
        Check(publicAll.SchemaVersion == 3
            && publicAll.Total == 3
            && publicAll.Items.Count(item => item.Kind == CommunityContentKinds.Map) == 1
            && publicAll.Items.All(item => item.OwnerId == null),
            "public v3 catalog includes published map and assets, not private drafts");

        CommunityContentPage onlyTexture = await viewer.BrowseContentAsync(
            CancellationToken.None, kind: CommunityContentKinds.Texture, pageSize: 1);
        Check(onlyTexture.Total == 1 && onlyTexture.Items.Length == 1
            && onlyTexture.Items[0].CurrentRevision?.ContentHash == firstTextureHash,
            "kind/paging returns stable typed project envelope");
        CommunityContentPage search = await viewer.BrowseContentAsync(
            CancellationToken.None, query: "Blue material", pageSize: 1);
        Check(search.Total == 1 && search.Items[0].ContentId == materialId,
            "v3 search filters on material presentation metadata");
        using (var list = await http.GetAsync("v3/content?pageSize=1"))
        {
            Check(list.IsSuccessStatusCode && list.Headers.ETag != null
                && list.Headers.TryGetValues("X-Total-Count", out var total)
                && total.Single() == "3",
                "v3 page publishes authorized total count and conditional ETag");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "v3/content?pageSize=1");
            request.Headers.IfNoneMatch.Add(list.Headers.ETag!);
            using var unchanged = await http.SendAsync(request);
            Check(unchanged.StatusCode == HttpStatusCode.NotModified,
                "v3 catalog conditional GET keeps stable authorized representation");
        }

        CommunityContentProject mapped = await viewer.GetContentAsync(
            CommunityContentKinds.Map, mapId, CancellationToken.None)
            ?? throw new InvalidDataException("Missing v3 map projection.");
        CommunityMapProject legacy = await viewer.GetProjectAsync(
            mapId, CancellationToken.None)
            ?? throw new InvalidDataException("Missing original map revision project.");
        Check(mapped.ContentId == legacy.MapId
            && mapped.CurrentRevision?.ArchiveHash == legacy.CurrentHash
            && mapped.LatestHash == legacy.LatestHash
            && mapped.CurrentRevision.ContentHash == legacy.CurrentRevision!.Package.ContentHash,
            "v3 map metadata is read-through and preserves exact v2 revision/hash identities");

        CommunityContentRevision[] revisions = await viewer.GetContentRevisionsAsync(
            CommunityContentKinds.Map, mapId, CancellationToken.None);
        Check(revisions.Length == legacy.RevisionCount
            && revisions[0].ArchiveHash == map.Hash,
            "v3 map revision history matches authoritative v2 archive hashes");

        // A newer private map revision is owned by v2, not by v3 storage.
        // The v3 adapter must preserve public current vs owner latest pointers.
        MapDefinition revisionDraft = MapProjectSerializer.Clone(mapDefinition);
        revisionDraft.Version = "2";
        revisionDraft.Description = "Private work-in-progress map revision";
        string revisionPath = MapPackageBuilder.Build(revisionDraft,
            Path.Combine(root, "map-draft.ppmap"));
        CommunityMap draftMap = await publisher.UploadAsync(
            revisionPath, CancellationToken.None, draft: true);
        var mapOwner = await publisher.GetContentAsync(
            CommunityContentKinds.Map, mapId, CancellationToken.None);
        var mapVisitor = await viewer.GetContentAsync(
            CommunityContentKinds.Map, mapId, CancellationToken.None);
        Check(mapOwner?.CurrentHash == map.Hash
            && mapOwner.LatestHash == draftMap.Hash
            && mapOwner.RevisionCount == 2
            && mapVisitor?.CurrentHash == map.Hash
            && mapVisitor.LatestHash == map.Hash
            && mapVisitor.RevisionCount == 1,
            "v3 map projections preserve draft latest vs public current pointer without leaks");
        Check(!File.ReadAllText(Path.Combine(store, "content_catalog_v3.json"))
            .Contains(mapId.ToString(), StringComparison.OrdinalIgnoreCase),
            "v3 registry stores no copy of authoritative v2 map revision identity");

        CommunityContentProject? privateResult = await viewer.GetContentAsync(
            CommunityContentKinds.Prefab, prefabId, CancellationToken.None);
        Check(privateResult == null,
            "anonymous HTTP metadata endpoint hides draft prefab");
        using (var denied = await http.GetAsync("v3/content?mine=true"))
            Check(denied.StatusCode == HttpStatusCode.Unauthorized,
                "v3 personal catalog requires authenticated creator");
        using (var noWriter = await http.PostAsync("v3/content",
            new StringContent("{}", Encoding.UTF8, "application/json")))
            Check(!noWriter.IsSuccessStatusCode,
                "PR-08 does not expose unvalidated generic file publication");

        var ownItems = await publisher.BrowseContentAsync(
            CancellationToken.None, mine: true);
        Check(ownItems.Total == 4
            && ownItems.Items.Any(item => item.ContentId == textureId
                && item.RevisionCount == 2)
            && ownItems.Items.Any(item => item.ContentId == prefabId),
            "owner view exposes private generic asset revisions separately from public browse");

        // The exact original map remains retrievable through its legacy
        // route; v3 metadata must not mutate its bytes or package identity.
        using var raw = await http.GetAsync("packages/" + map.Hash);
        Check(raw.IsSuccessStatusCode
            && (await raw.Content.ReadAsByteArrayAsync())
                .SequenceEqual(File.ReadAllBytes(mapPath)),
            "legacy package route returns unchanged immutable map archive bytes");
    }
    await StopAsync();

    {
        var instance = await StartAsync();
        address = instance.Endpoint;
        using var http = instance.Http;
        using var viewer = new MapCommunityClient(address);
        var afterRestart = await viewer.BrowseContentAsync(CancellationToken.None);
        Check(afterRestart.Total == 3
            && afterRestart.Items.Any(item => item.Kind == CommunityContentKinds.Map
                && item.LatestHash == firstMapHash),
            "generic and map read-through catalog survives complete Community service restart");
    }
    await StopAsync();

    // Registration is trusted local metadata only: reject unsupported MIME,
    // foreign map kind, oversized fields and self dependencies.
    bool rejected = false;
    try
    {
        registry.RegisterVerifiedBlob(image,
            textureMeta with { Kind = CommunityContentKinds.Map });
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "v3 generic registration refuses duplication of v2 map state");

    rejected = false;
    try
    {
        registry.RegisterVerifiedBlob(materialFile,
            new CommunityContentRegistration(CommunityContentKinds.Model,
                Guid.NewGuid(), MapCreatorCatalog.ServiceOwner,
                "Broken", null, null, null, Array.Empty<string>(),
                "application/json", CommunityContentVisibility.Published));
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "typed MIME policy rejects unsupported kind/file classification");

    Console.WriteLine($"Community v3 content checks: {checks} PASS.");
}
finally
{
    await StopAsync();
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}

internal sealed class RegistryCheck(CommunityContentRegistry value) : IDisposable
{
    public CommunityContentRegistry Value { get; } = value;
    public void Dispose() { }
}
