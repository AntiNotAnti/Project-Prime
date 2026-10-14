using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

// Real authenticated typed asset HTTP campaign. Never uses gameplay assets.
string root = Path.Combine(Path.GetTempPath(),
    "prime-typed-publish-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var stop = new CancellationTokenSource();
Task? running = null;
int checks = 0;
void Check(bool pass, string description)
{
    if (!pass) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS " + description);
}
try
{
    using var portProbe = new TcpListener(IPAddress.Loopback, 0);
    portProbe.Start();
    int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();
    string address = $"http://127.0.0.1:{port}/community/";
    const string ownerToken = "typed-community-publish-fixture-admin-2026";
    running = MapCommunityServer.ServeAsync(address, Path.Combine(root, "server"),
        ownerToken, stop.Token);
    using var creator = new MapCommunityClient(address, ownerToken);
    using var publicClient = new MapCommunityClient(address);
    string image = Path.Combine(root, "fixture.png");
    byte[] png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO/aegAAAABJRU5ErkJggg==");
    File.WriteAllBytes(image, png);
    Guid id = Guid.Parse("0fa17589-9c7a-4caf-87e4-6dd5db2cb351");
    var record = new CommunityContentRegistration("texture", id,
        MapCreatorCatalog.ServiceOwner, "Author texture", "Texture A",
        "Fixture", "Original tiny image", new[] { "hd" }, "image/png",
        CommunityContentVisibility.Published);
    var published = await creator.PublishContentAsync(image, record,
        CancellationToken.None);
    string hash = MapBuildFingerprint.HashFile(image);
    Check(published.LatestHash == hash
        && published.RevisionCount == 1
        && published.CurrentRevision?.ContentHash == hash,
        "typed PNG is authenticated, content-addressed and published");

    using (var anonymous = new HttpClient { BaseAddress = new Uri(address) })
    using (var rejected = await anonymous.PostAsync(
        "v3/content/texture/" + id + "/revisions",
        new ByteArrayContent(png)))
        Check(rejected.StatusCode == HttpStatusCode.Unauthorized,
            "uncredentialed binary publishing is prohibited");

    string download = Path.Combine(root, "content.part");
    long length = await publicClient.DownloadContentAsync(hash, download,
        png.Length, CancellationToken.None);
    Check(length == png.Length && File.ReadAllBytes(download).SequenceEqual(png),
        "public raw typed object streams exact SHA-256 bytes");

    byte[] changed = png.Concat(new byte[] { 1 }).ToArray();
    File.WriteAllBytes(image, changed);
    var next = await creator.PublishContentAsync(image, record with
    {
        Visibility = CommunityContentVisibility.Draft,
        ExpectedParentHash = hash
    }, CancellationToken.None);
    string draftHash = MapBuildFingerprint.HashFile(image);
    Check(next.LatestHash == draftHash && next.CurrentHash == hash,
        "private revision keeps previous published content pointer");
    var visitor = await publicClient.GetContentAsync("texture", id,
        CancellationToken.None);
    Check(visitor?.CurrentHash == hash && visitor.LatestHash == hash,
        "anonymous catalog cannot enumerate private typed revisions");

    using (var anonymous = new HttpClient { BaseAddress = new Uri(address) })
    using (var missing = await anonymous.GetAsync("v3/objects/" + draftHash))
        Check(missing.StatusCode == HttpStatusCode.NotFound,
            "draft raw object bytes cannot leak to the anonymous public");
    string draft = Path.Combine(root, "private.part");
    await creator.DownloadContentAsync(draftHash, draft,
        changed.Length, CancellationToken.None);
    Check(File.ReadAllBytes(draft).SequenceEqual(changed),
        "authorized creator can download exact private draft bytes");

    await creator.ArchiveContentAsync("texture", id, restore: false,
        CancellationToken.None);
    Check(await publicClient.GetContentAsync("texture", id,
        CancellationToken.None) == null,
        "archived typed content disappears from public catalog");
    await creator.ArchiveContentAsync("texture", id, restore: true,
        CancellationToken.None);
    Check((await publicClient.GetContentAsync("texture", id,
        CancellationToken.None))?.CurrentHash == hash,
        "restoring metadata never changes original immutable revision identity");

    string corrupt = Path.Combine(root, "not-a-png.png");
    File.WriteAllText(corrupt, "not a PNG file");
    bool refused = false;
    try
    {
        await creator.PublishContentAsync(corrupt, record with
        {
            ContentId = Guid.NewGuid(), ExpectedParentHash = null
        }, CancellationToken.None);
    }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "sandbox refuses invalid image signature before transmission");

    string malicious = Path.Combine(root, "active-material.json");
    File.WriteAllText(malicious, "{\"script\":\"fetch('https://bad.example')\"}");
    refused = false;
    try
    {
        await creator.PublishContentAsync(malicious, new CommunityContentRegistration(
            "material", Guid.NewGuid(), MapCreatorCatalog.ServiceOwner,
            "Unsafe", null, null, null, Array.Empty<string>(),
            "application/vnd.projectprime.material+json",
            CommunityContentVisibility.Published), CancellationToken.None);
    }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "executable descriptor fields are never accepted");

    string material = Path.Combine(root, "material.json");
    File.WriteAllText(material, "{\"name\":\"safe\",\"roughness\":0.6}");
    var materialItem = await creator.PublishContentAsync(material,
        new CommunityContentRegistration("material", Guid.NewGuid(),
            MapCreatorCatalog.ServiceOwner, "Safe material", null, "Fixture",
            null, Array.Empty<string>(), "application/vnd.projectprime.material+json",
            CommunityContentVisibility.Published), CancellationToken.None);
    Check(materialItem.Kind == "material"
        && materialItem.CurrentHash == MapBuildFingerprint.HashFile(material),
        "second original type, material descriptor, publishes with pinned identity");
    Console.WriteLine($"Community typed publishing checks: {checks} PASS.");
}
finally
{
    stop.Cancel();
    if (running != null)
    {
        try { await running.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { }
    }
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}
