using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using MphRead;
using MphRead.Mods.MapGen;

// PR-06: source-backed byte-equivalence check against the previous
// in-memory package algorithm. This is a real .ppmap archive/manifest test,
// not a benchmark of compiler, network, device rendering or multiplayer Ready.
int assetMiB = 3;
string? output = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--mib" && i + 1 < args.Length) assetMiB = int.Parse(args[++i]);
    else if (args[i] == "--output" && i + 1 < args.Length)
        output = Path.GetFullPath(args[++i]);
    else throw new ArgumentException("Usage: map-package-stream-check [--mib N] [--output JSON]");
}
if (assetMiB is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(assetMiB));

string root = Path.Combine(Path.GetTempPath(),
    "prime-package-stream-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool valid, string message)
{
    if (!valid) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
    checks++;
}
string originalPath = Paths.AllPaths[Paths.MphKey];
try
{
    Paths.SetPath(Paths.MphKey, Path.Combine(root, "runtime"));
    Directory.CreateDirectory(Paths.FileSystem);

    using (var tex = new BinaryWriter(File.Create(Path.Combine(root, "tile.tex"))))
    {
        tex.Write(Encoding.ASCII.GetBytes("FPTX"));
        tex.Write((ushort)1); tex.Write((ushort)1); tex.Write((ushort)0);
        tex.Write((ushort)8); tex.Write((ushort)8); tex.Write((ushort)1);
        tex.Write((ushort)0); tex.Write((ushort)32767); tex.Write(new byte[64]);
    }
    var source = new MapDefinition
    {
        FormatVersion = 2,
        MapId = Guid.Parse("7f975ada-6722-438f-a13d-78d88d8f9ead"),
        Name = "STREAMED_TEST",
        InGameName = "Streaming CI",
        Author = "Project Prime CI",
        Version = "1",
        BaseDirectory = root
    };
    source.Materials.Add(new MapMaterial
    {
        Id = Guid.Parse("d9ecee35-7b0d-47f6-8efe-50cbf20fca08"),
        Texture = "tile.tex"
    });
    source.Assets.Add(new MapAsset { Kind = "texture", Path = "tile.tex" });
    source.Geometry.Add(new MapBox
    {
        Id = Guid.Parse("6f13a8e7-ae81-49fd-9539-7094e04e3df4"),
        Transform = new() { Position = new[] { 0f, -1f, 0f }, Scale = new[] { 8f, 1f, 8f } }
    });
    source.Spawns.Add(new MapSpawn
    {
        Id = Guid.Parse("53b9063e-d575-4200-85ac-54ddd14129e8"),
        Position = new[] { -2f, 2f, 0f }
    });
    source.Spawns.Add(new MapSpawn
    {
        Id = Guid.Parse("d1393049-3a8d-4a70-869d-84074d990bbf"),
        Position = new[] { 2f, 2f, 0f }
    });

    string audioDir = Path.Combine(root, "audio");
    Directory.CreateDirectory(audioDir);
    for (int i = 0; i < 3; i++)
    {
        string asset = "audio/synthetic " + i + ".wav";
        string file = Path.Combine(audioDir, "synthetic " + i + ".wav");
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        long audioBytes = (long)assetMiB * 1024 * 1024;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((int)(audioBytes + 36));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1); writer.Write((short)2);
        writer.Write(48000); writer.Write(192000);
        writer.Write((short)4); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((int)audioBytes);
        byte[] block = new byte[65536];
        var random = new Random(1320 + i);
        for (int chunk = 0; chunk < assetMiB * 16; chunk++)
        {
            random.NextBytes(block);
            stream.Write(block);
        }
        source.Assets.Add(new MapAsset { Kind = "audio", Path = asset });
    }

    var streamedMethod = typeof(MapPackageBuilder).GetMethod(
        "WriteValidated", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("Current streaming builder missing");
    string Streamed(MapDefinition src, string destination)
        => (string)streamedMethod.Invoke(null, new object[]
            { src, destination, CancellationToken.None })!;

    // Exact historical algorithm scoped to a map with no BSP/collision import.
    // Preserves package entry ordering, ZIP metadata, SHA framing and content.
    string Reference(MapDefinition original, string destination)
    {
        MapDefinition definition = MapProjectSerializer.Clone(original);
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string asset in MapDependencyAnalyzer.PackageAssets(original))
            entries.Add(asset, MapAssets.Read(original, asset));
        definition.ModelSources.Clear();
        foreach (MapAsset asset in definition.Assets) asset.SourcePath = null;
        entries.Add("project.json", Encoding.UTF8.GetBytes(definition.Serialize()));

        var manifest = new MapPackageManifest
        {
            Format = definition.FormatVersion,
            MapId = definition.MapId,
            Name = definition.Name,
            DisplayName = definition.InGameName,
            MinimumProtocol = MphRead.Mods.Network.NetConfig.ProtocolVersion,
            SupportedModes = definition.Capabilities?.SupportedModes.ToArray()
                ?? Array.Empty<string>(),
            MinPlayers = definition.Capabilities?.MinPlayers ?? 1,
            MaxPlayers = definition.Capabilities?.MaxPlayers ?? 8,
            MapVersion = definition.Version,
            Author = definition.Author,
            Preview = definition.Assets.FirstOrDefault(a => a.Kind == "preview")?.Path,
            ContentHash = MapPackageReader.ContentHash(entries.Keys, n => entries[n])
        };
        entries.Add("manifest.json",
            JsonSerializer.SerializeToUtf8Bytes(manifest, MapPackageReader.JsonOptions));
        using (var fs = File.Create(destination))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            foreach (var pair in entries)
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.SmallestSize);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = 0;
                using Stream target = entry.Open();
                for (int offset = 0; offset < pair.Value.Length; offset += 65536)
                    target.Write(pair.Value.AsSpan(offset,
                        Math.Min(65536, pair.Value.Length - offset)));
            }
        using (var check = new MapPackageReader(destination))
            _ = check.ReadProject();
        return destination;
    }

    string oldFile = Path.Combine(root, "reference.ppmap");
    string newFile = Path.Combine(root, "streamed.ppmap");
    (double Ms, long Allocated) Measure(Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before);
    }

    var old = Measure(() => Reference(source, oldFile));
    var streamed = Measure(() => Streamed(source, newFile));
    string expectedHash = MapBuildFingerprint.HashFile(oldFile);
    string actualHash = MapBuildFingerprint.HashFile(newFile);
    Check(expectedHash == actualHash
        && new FileInfo(oldFile).Length == new FileInfo(newFile).Length,
        "streamed package is byte-for-byte SHA-256 identical to pre-PR06 builder");
    using (var read = new MapPackageReader(newFile))
    {
        Check(read.Manifest?.MapId == source.MapId
            && read.Manifest.ContentHash.Length == 64,
            "strict reader accepts streamed manifest and unchanged content digest");
        Check(read.ReadProject().Contains("STREAMED_TEST", StringComparison.Ordinal),
            "streamed package preserves authoring project JSON");
    }
    string repacked = Path.Combine(root, "repacked.ppmap");
    MapDefinition imported = MapDefinition.Load(newFile);
    Streamed(imported, repacked);
    Check(MapBuildFingerprint.HashFile(repacked) == expectedHash,
        "streaming source asset reads from an already verified .ppmap keep exact bytes");

    string unchanged = MapBuildFingerprint.HashFile(newFile);
    using (var cancel = new CancellationTokenSource())
    {
        cancel.Cancel();
        bool stopped = false;
        try
        {
            _ = streamedMethod.Invoke(null, new object[]
                { source, newFile, cancel.Token });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
        {
            stopped = true;
        }
        Check(stopped && MapBuildFingerprint.HashFile(newFile) == unchanged,
            "cancelled package build cannot replace an existing exact archive");
    }

    string tooLarge = Path.Combine(audioDir, "oversized.wav");
    using (FileStream sparse = File.Create(tooLarge))
        sparse.SetLength(MapPackageReader.MaxEntryBytes + 1);
    MapDefinition oversize = MapProjectSerializer.Clone(source);
    oversize.Assets.Add(new MapAsset
    {
        Kind = "audio", Path = "audio/oversized.wav"
    });
    bool sizeRejected = false;
    try { Streamed(oversize, newFile); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
    {
        sizeRejected = true;
    }
    Check(sizeRejected && MapBuildFingerprint.HashFile(newFile) == unchanged,
        "oversized sparse authoring asset is rejected without replacing valid package");
    File.Delete(tooLarge);

    var report = new
    {
        schemaVersion = 1,
        fixture = "streaming-package-large-authoring-assets",
        perAssetMiB = assetMiB,
        assetCount = 3,
        archiveBytes = new FileInfo(newFile).Length,
        packageHash = actualHash,
        historical = new { milliseconds = old.Ms, threadAllocatedBytes = old.Allocated },
        streaming = new { milliseconds = streamed.Ms, threadAllocatedBytes = streamed.Allocated },
        caveats = new[]
        {
            "Single-process same-thread encoder measurement; filesystem cache can bias elapsed times.",
            "Historical reference only exercises loose assets, not imported BSP/collision paths.",
            "Actual game compile, remote upload, multiplayer Ready and Android memory are excluded.",
            "The reader and builder preserve exact existing archive identities; no package migration."
        }
    };
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (output != null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, json + Environment.NewLine);
    }
    Console.WriteLine($"Map package streaming checks: {checks} PASS.");
}
finally
{
    Paths.SetPath(Paths.MphKey, originalPath);
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}
