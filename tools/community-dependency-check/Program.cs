using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.MapGen;

string root = Path.Combine(Path.GetTempPath(), "prime-optionals-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool result, string label)
{
    if (!result) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
using var stop = new CancellationTokenSource();
Task? server = null;
try
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    string url = $"http://127.0.0.1:{port}/";
    const string key = "community-optional-lockfile-test-secret";
    server = MapCommunityServer.ServeAsync(url, Path.Combine(root, "server"), key, stop.Token);

    string pngFile = Path.Combine(root, "hd.png");
    byte[] png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO/aegAAAABJRU5ErkJggg==");
    File.WriteAllBytes(pngFile, png);
    Guid id = Guid.Parse("5b75bea6-cdbc-43e9-9445-8e492b076a3a");
    using var creator = new MapCommunityClient(url, key);
    var uploaded = await creator.PublishContentAsync(pngFile,
        new CommunityContentRegistration("texture", id, MapCreatorCatalog.ServiceOwner,
            "Optional HD", null, null, null, new[] { "high-res" },
            "image/png", CommunityContentVisibility.Published), default);

    var pointer = new CommunityContentDependency(
        CommunityContentKinds.Texture, id, uploaded.CurrentHash!,
        CommunityDependencyRoles.OptionalVisual);
    var content = new[] { uploaded };
    Guid first = Guid.Parse("c93790b9-1a5a-431c-8576-43cc0d5277c5");
    Guid second = Guid.Parse("e47ab596-3384-4c4c-a720-94c47204913c");
    var a = CommunityDependencyResolver.Resolve(first, new string('a', 64),
        CommunityVisualQuality.Hd, new[] { pointer }, content);
    var b = CommunityDependencyResolver.Resolve(second, new string('b', 64),
        CommunityVisualQuality.Hd, new[] { pointer }, content);
    Check(a.Items.Length == 1 && b.Items.Length == 1
        && a.Items[0].Hash == b.Items[0].Hash,
        "different maps pin the same optional texture's immutable digest");
    Check(a.GameplayReadyUnaffected && b.GameplayReadyUnaffected,
        "optional visual lockfiles do not gate gameplay Ready or collision");

    var missing = CommunityDependencyResolver.Resolve(first, new string('a', 64),
        CommunityVisualQuality.Hd, new[] { new CommunityContentDependency(
            CommunityContentKinds.Texture, Guid.NewGuid(), new string('f', 64)) }, content);
    Check(missing.Items.Length == 0 && missing.MissingOptional.Length == 1
        && missing.GameplayReadyUnaffected,
        "missing HD asset is reported but cannot prevent a gameplay match");
    bool denied = false;
    try
    {
        CommunityDependencyResolver.Resolve(first, new string('a', 64),
            CommunityVisualQuality.Standard, new[] { pointer with
            { Role = "required-gameplay" } }, content);
    }
    catch (InvalidDataException) { denied = true; }
    Check(denied, "runtime-required dependencies are prohibited without a protocol bump");

    byte[] canonical = CommunityDependencyResolver.CanonicalBytes(a);
    Check(canonical.SequenceEqual(CommunityDependencyResolver.CanonicalBytes(a)),
        "same exact map dependency selection has deterministic lockfile bytes");

    using var publicClient = new MapCommunityClient(url);
    var cache = new CommunityOptionalAssetCache(Path.Combine(root, "visual-cache"));
    string pathA = await cache.GetOrDownloadAsync(publicClient, a.Items[0], default);
    string pathB = await cache.GetOrDownloadAsync(publicClient, b.Items[0], default);
    Check(pathA == pathB && File.ReadAllBytes(pathA).SequenceEqual(png),
        "two maps share one verified cached texture object");
    Check(Directory.GetFiles(Path.Combine(root, "visual-cache"), "*.blob").Length == 1,
        "shared asset digest creates only one materialized cached blob");

    File.WriteAllBytes(pathA, new byte[] { 4, 5, 6 });
    string restored = await cache.GetOrDownloadAsync(publicClient, b.Items[0], default);
    Check(restored == pathA && File.ReadAllBytes(restored).SequenceEqual(png),
        "corrupt optional cache is repaired from exact public hash");
    // The public project envelope includes only Current/Latest revisions.
    // Preserve exact older published SHA pins by using the authorized v3
    // revision history, rather than silently following mutable latest.
    string secondFile = Path.Combine(root, "hd-v2.png");
    File.WriteAllBytes(secondFile, png.Concat(new byte[] { 2 }).ToArray());
    var versionTwo = await creator.PublishContentAsync(secondFile,
        new CommunityContentRegistration(CommunityContentKinds.Texture, id,
            MapCreatorCatalog.ServiceOwner, "Optional HD", null, null, null,
            new[] { "high-res" }, "image/png",
            CommunityContentVisibility.Published,
            ExpectedParentHash: uploaded.CurrentHash), default);
    string thirdFile = Path.Combine(root, "hd-v3.png");
    File.WriteAllBytes(thirdFile, png.Concat(new byte[] { 3 }).ToArray());
    var versionThree = await creator.PublishContentAsync(thirdFile,
        new CommunityContentRegistration(CommunityContentKinds.Texture, id,
            MapCreatorCatalog.ServiceOwner, "Optional HD", null, null, null,
            new[] { "high-res" }, "image/png",
            CommunityContentVisibility.Published,
            ExpectedParentHash: versionTwo.CurrentHash), default);
    CommunityContentProject? currentProject = await publicClient.GetContentAsync(
        CommunityContentKinds.Texture, id, default);
    CommunityContentRevision[] publicHistory =
        await publicClient.GetContentRevisionsAsync(CommunityContentKinds.Texture,
            id, default);
    Check(currentProject?.CurrentHash == versionThree.CurrentHash
        && publicHistory.Length == 3,
        "service retains historical public optional revisions after publishing updates");
    var history = publicHistory.Select(rev => new CommunityAuthorizedRevision(
        CommunityContentKinds.Texture, id, rev)).ToArray();
    var oldWithoutHistory = CommunityDependencyResolver.Resolve(first,
        new string('a', 64), CommunityVisualQuality.Hd,
        new[] { pointer }, new[] { currentProject! });
    Check(oldWithoutHistory.Items.Length == 0
        && oldWithoutHistory.MissingOptional.Length == 1,
        "lightweight latest-only catalog cannot falsely resolve an older exact revision");
    var oldWithHistory = CommunityDependencyResolver.Resolve(first,
        new string('a', 64), CommunityVisualQuality.Hd,
        new[] { pointer }, new[] { currentProject! }, history);
    Check(oldWithHistory.Items.Length == 1
        && oldWithHistory.Items[0].Hash == uploaded.CurrentHash
        && oldWithHistory.MissingOptional.Length == 0,
        "authorized older revision history retains immutable map visual SHA pin");
    string historicFile = await cache.GetOrDownloadAsync(publicClient,
        oldWithHistory.Items[0], default);
    Check(File.ReadAllBytes(historicFile).SequenceEqual(png),
        "historical exact published optional blob is still downloadable for cache reuse");
    bool refusedPrivate = false;
    try
    {
        CommunityDependencyResolver.Resolve(first, new string('a', 64),
            CommunityVisualQuality.Hd, new[] { pointer }, new[] { currentProject! },
            new[] { history[^1] with
            {
                Revision = history[^1].Revision with
                { Visibility = CommunityContentVisibility.Draft }
            } });
    }
    catch (InvalidDataException) { refusedPrivate = true; }
    Check(refusedPrivate,
        "optional resolver rejects unpublished revision metadata supplied as history");

    Console.WriteLine($"Community optional resource checks: {checks} PASS.");
}
finally
{
    stop.Cancel();
    if (server != null)
    {
        try { await server.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { }
    }
    try { Directory.Delete(root, true); } catch (IOException) { }
}
