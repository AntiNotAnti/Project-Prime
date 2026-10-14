using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

// PR-00: repeatable, asset-free current-path measurements and identity checks.
// This is not a live remote-service throughput test or a multiplayer Ready benchmark.
string? output = null;
bool stress = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && ++i < args.Length) output = Path.GetFullPath(args[i]);
    else if (args[i] == "--stress") stress = true;
    else throw new ArgumentException("Usage: community-baseline-check [--output FILE] [--stress]");
}

string root = Path.Combine(Path.GetTempPath(), "prime-community-baseline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var timings = new System.Collections.Generic.Dictionary<string, double>(StringComparer.Ordinal);
var watch = Stopwatch.StartNew();
long peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;
string? oldMaps = CustomRooms.MapDirectory, oldUserMaps = CustomRooms.UserMapDirectory;
using var stop = new CancellationTokenSource();
Task? server = null;
try
{
    Paths.SetPath(Paths.MphKey, Path.Combine(root, "runtime"));
    Directory.CreateDirectory(Paths.FileSystem);
    CustomRooms.MapDirectory = Path.Combine(root, "maps");
    CustomRooms.UserMapDirectory = Path.Combine(root, "installed");
    Directory.CreateDirectory(CustomRooms.MapDirectory);
    Directory.CreateDirectory(CustomRooms.UserMapDirectory);

    // This source has stable GUIDs, names and bytes. No cartridge assets are shipped.
    using (var tex = new BinaryWriter(File.Create(Path.Combine(root, "tile.tex"))))
    {
        tex.Write(Encoding.ASCII.GetBytes("FPTX"));
        tex.Write((ushort)1); tex.Write((ushort)1); tex.Write((ushort)0);
        tex.Write((ushort)8); tex.Write((ushort)8); tex.Write((ushort)1);
        tex.Write((ushort)0); tex.Write((ushort)32767); tex.Write(new byte[64]);
    }
    Guid mapId = Guid.Parse("2766f10f-e6c2-4dbd-98c0-f3e38a2dfd51");
    var definition = new MapDefinition
    {
        FormatVersion = 2, MapId = mapId, Name = "COMMUNITY_BASELINE",
        InGameName = "Community baseline", Version = "1",
        Author = "Project Prime CI", BaseDirectory = root
    };
    definition.Materials.Add(new MapMaterial
    {
        Id = Guid.Parse("68f7cdba-8bb5-40ac-91b5-a061ba2c2b18"), Texture = "tile.tex"
    });
    definition.Assets.Add(new MapAsset { Path = "tile.tex", Kind = "texture" });
    definition.Geometry.Add(new MapBox
    {
        Id = Guid.Parse("7dc98e1f-ce61-4d51-94be-26054b8825cb"),
        Transform = new() { Position = new[] { 0f, -1f, 0f }, Scale = new[] { 8f, 1f, 8f } }
    });
    definition.Spawns.Add(new MapSpawn { Id = Guid.Parse("4ca1386d-eaa0-48f4-869c-0b32c4e9af2c"), Position = new[] { -2f, 2f, 0f } });
    definition.Spawns.Add(new MapSpawn { Id = Guid.Parse("2d004c4f-7069-4cb9-9eae-dc43452e55cc"), Position = new[] { 2f, 2f, 0f } });

    string first = Path.Combine(root, "first.ppmap");
    string second = Path.Combine(root, "second.ppmap");
    watch.Restart();
    await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(definition), first);
    timings["package_first_ms"] = watch.Elapsed.TotalMilliseconds;
    watch.Restart();
    await MapBuildScheduler.Shared.PackageAsync(MapBuildSnapshot.Capture(definition), second);
    timings["package_repeat_ms"] = watch.Elapsed.TotalMilliseconds;

    watch.Restart();
    string hash = MapBuildFingerprint.HashFile(first);
    string repeatHash = MapBuildFingerprint.HashFile(second);
    timings["hash_both_ms"] = watch.Elapsed.TotalMilliseconds;
    if (hash != repeatHash || !File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second)))
        throw new InvalidDataException("The same frozen source produced different archive bytes.");
    if (new FileInfo(first).Length > MapPackageReader.MaxArchiveBytes)
        throw new InvalidDataException("Baseline archive exceeds the 512 MiB package limit.");

    watch.Restart();
    using (var reader = new MapPackageReader(first))
    {
        if (reader.Manifest?.MapId != mapId || JsonSerializer.Deserialize<MapDefinition>(reader.ReadProject(), MapPackageReader.JsonOptions)?.MapId != mapId)
            throw new InvalidDataException("Strict reader changed the baseline map identity.");
    }
    timings["strict_verify_ms"] = watch.Elapsed.TotalMilliseconds;
    MapContentIdentity identity = MapContentIdentity.FromPackage(first);
    if (identity.MapId != mapId || identity.PackageHash.ToString() != hash)
        throw new InvalidDataException("Community identity diverged from the exact archive hash.");

    // Optional checked-in package golden digest: populated from the reviewed,
    // frozen baseline fixture, not generated/overwritten by test execution.
    string golden = Path.Combine(AppContext.BaseDirectory, "community-baseline-golden.sha256");
    if (File.Exists(golden))
    {
        string expected = File.ReadAllText(golden).Trim();
        if (!MapCommunityClient.ValidHash(expected) || hash != expected)
            throw new InvalidDataException("Community baseline package hash changed: " + hash);
    }

    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    string endpoint = "http://127.0.0.1:" + port + "/baseline/";
    const string fixtureSecret = "community-baseline-local-fixture-only";
    server = MapCommunityServer.ServeAsync(endpoint, Path.Combine(root, "service"), fixtureSecret, stop.Token);
    using var client = new MapCommunityClient(endpoint, fixtureSecret);
    watch.Restart();
    CommunityMap published = await client.UploadAsync(first, CancellationToken.None);
    timings["loopback_upload_ms"] = watch.Elapsed.TotalMilliseconds;
    if (published.Hash != hash || published.MapId != mapId)
        throw new InvalidDataException("Upload did not preserve immutable map identity.");

    watch.Restart();
    var browse = await client.BrowseProjectsAsync(CancellationToken.None);
    timings["loopback_catalog_ms"] = watch.Elapsed.TotalMilliseconds;
    if (browse.Length != 1 || browse[0].MapId != mapId)
        throw new InvalidDataException("The v2 catalog did not preserve the map project.");

    string download = Path.Combine(root, "downloaded.ppmap");
    watch.Restart();
    await client.DownloadExactAsync(identity, download, CancellationToken.None);
    timings["loopback_download_ms"] = watch.Elapsed.TotalMilliseconds;
    if (MapBuildFingerprint.HashFile(download) != hash)
        throw new InvalidDataException("Exact download changed package bytes.");

    watch.Restart();
    using (var prepared = await client.PrepareExactAsync(identity, CancellationToken.None))
    {
        var installed = prepared.Commit(CustomRooms.UserMapDirectory);
        if (installed.MapId != mapId)
            throw new InvalidDataException("Installed map identity changed during preparation.");
    }
    timings["loopback_prepare_build_install_ms"] = watch.Elapsed.TotalMilliseconds;
    watch.Restart();
    string installedArchive = Path.Combine(CustomRooms.UserMapDirectory, mapId.ToString("N") + ".ppmap");
    if (!File.Exists(installedArchive) || !MapContentIdentity.FromPackage(installedArchive).Matches(identity))
        throw new InvalidDataException("Installed archive is missing its exact identity.");
    timings["installed_exact_lookup_ms"] = watch.Elapsed.TotalMilliseconds;

    if (stress)
    {
        // Sparse file: tests the strict compressed-size fence, not a valid
        // near-limit gameplay archive or 512 MiB network throughput.
        string oversized = Path.Combine(root, "oversized.ppmap");
        using (var file = File.Create(oversized))
            file.SetLength(MapPackageReader.MaxArchiveBytes + 1);
        try
        {
            using var invalid = new MapPackageReader(oversized);
            throw new InvalidOperationException("Oversized archive unexpectedly passed.");
        }
        catch (InvalidDataException) { }
    }

    peakWorkingSet = Math.Max(peakWorkingSet, Process.GetCurrentProcess().PeakWorkingSet64);
    var report = new
    {
        schemaVersion = 1,
        fixture = "community-baseline-v1",
        packageHash = hash,
        mapId,
        packageBytes = new FileInfo(first).Length,
        netProtocol = MphRead.Mods.Network.NetConfig.ProtocolVersion,
        pipeline = "existing-self-contained-ppmap",
        timingsMs = timings,
        peakProcessWorkingSetBytes = peakWorkingSet,
        stressSizeFenceChecked = stress,
        limitations = new[]
        {
            "Loopback results include JIT/warm-cache effects and are not internet throughput measurements.",
            "Prepare/build/install timing is not authoritative live lobby time-to-Ready.",
            "Process peak working set is not isolated package-stage managed allocation.",
            "Actual 512 MiB valid archive, Android, multiplayer and remote-host campaigns run in separate gates."
        }
    };
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    if (output != null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, json + Environment.NewLine);
    }
    Console.WriteLine(json);
}
finally
{
    stop.Cancel();
    if (server != null)
    {
        try { await server; }
        catch (OperationCanceledException) { }
    }
    CustomRooms.MapDirectory = oldMaps!;
    CustomRooms.UserMapDirectory = oldUserMaps!;
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}
