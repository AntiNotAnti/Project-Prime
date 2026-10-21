using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MphRead.Mods.MapGen;

static class Pk3DiscoveryChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string bsp = Path.Combine(root, "prime-fixture.bsp");
        string archive = Path.Combine(root, "direct-import.zip");
        byte[] level = File.ReadAllBytes(bsp);
        byte[] texture = Encoding.ASCII.GetBytes("textures/prime/test");
        for (int i = 0; i <= level.Length - texture.Length; i++)
            if (level.AsSpan(i, texture.Length).SequenceEqual(texture))
                Encoding.ASCII.GetBytes("textures/absent/art").CopyTo(level, i);
        void WriteLevelArchive(string path, byte[] bytes, params string[] entries)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (string entry in entries)
            { using var stream = zip.CreateEntry(entry).Open(); stream.Write(bytes); }
        }
        void Archive(params string[] entries)
            => WriteLevelArchive(archive, level, entries);
        Archive("maps/alpha.bsp", "maps/beta.bsp");
        check(Pk3DiscoveryService.Discover(archive).Select(l => l.Name).SequenceEqual(new[] { "alpha", "beta" }),
            "PK3 adapter discovers explicit levels in ZIP archives");
        var options = new Q3ImportService.Options(archive, "beta", "PK3_CACHE", Path.Combine(root, "pk3-first"),
            UnitsPerUnit:16, AutoHealCollision:false);
        string key = Pk3DiscoveryService.Fingerprint(options, "beta", new[] { archive });
        check(key != Pk3DiscoveryService.Fingerprint(options with { PatchLevel = 4 }, "beta", new[] { archive })
            && key != Pk3DiscoveryService.Fingerprint(options, "alpha", new[] { archive })
            && key != Pk3DiscoveryService.Fingerprint(options, "beta", new[] { archive }, converterVersion:"next"),
            "PK3 cache invalidates for level, patch options and converter version");
        string fingerprintMutation = Path.Combine(root, "fingerprint-mutation.bsp");
        File.WriteAllBytes(fingerprintMutation, File.ReadAllBytes(bsp));
        DateTime mutationTime = File.GetLastWriteTimeUtc(fingerprintMutation);
        var mutationOptions = options with { Source = fingerprintMutation };
        string beforeMutation = Pk3DiscoveryService.Fingerprint(mutationOptions, "beta", new[] { fingerprintMutation });
        byte[] changedSource = File.ReadAllBytes(fingerprintMutation);
        changedSource[^1] ^= 0x5a;
        File.WriteAllBytes(fingerprintMutation, changedSource);
        File.SetLastWriteTimeUtc(fingerprintMutation, mutationTime);
        string afterMutation = Pk3DiscoveryService.Fingerprint(mutationOptions, "beta", new[] { fingerprintMutation });
        check(new FileInfo(fingerprintMutation).Length == changedSource.Length
            && File.GetLastWriteTimeUtc(fingerprintMutation) == mutationTime && beforeMutation != afterMutation,
            "PK3 conversion fingerprint detects same-size source edits with restored timestamps");

        string priorityOne = Path.Combine(root, "priority-one.pk3");
        string priorityTwo = Path.Combine(root, "priority-two.pk3");
        void WritePriorityArchive(string path, byte color)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var stream = zip.CreateEntry("textures/prime/test.tga", CompressionLevel.NoCompression).Open();
            byte[] image = new byte[30]; image[2] = 2; image[12] = 2; image[14] = 2;
            image[16] = 24; image[17] = 0x20;
            for (int i = 18; i < image.Length; i += 3) image[i + 2] = color;
            stream.Write(image);
        }
        WritePriorityArchive(priorityOne, 240);
        WritePriorityArchive(priorityTwo, 32);
        string forwardPrecedence = Pk3DiscoveryService.Fingerprint(options, "beta",
            new[] { archive, priorityOne, priorityTwo });
        string reversePrecedence = Pk3DiscoveryService.Fingerprint(options, "beta",
            new[] { archive, priorityTwo, priorityOne });
        check(forwardPrecedence != reversePrecedence,
            "PK3 conversion fingerprint invalidates when duplicate-texture archive precedence changes");

        string? previousCacheOverride = Pk3DiscoveryService.CacheDirectoryOverride;
        string? previousCacheEnvironment = Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE");
        string envCache = Path.Combine(root, "environment-q3-cache");
        string overrideCache = Path.Combine(root, "override-q3-cache");
        string cacheProbe = Path.Combine(root, "cache-env-probe");
        Directory.CreateDirectory(cacheProbe);
        File.WriteAllText(Path.Combine(cacheProbe, "probe.bin"), "cache root probe");
        try
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE", envCache);
            Pk3DiscoveryService.CacheDirectoryOverride = overrideCache;
            string overrideKey = new string('a', 64);
            Pk3DiscoveryService.Store(overrideKey, cacheProbe, default);
            Pk3DiscoveryService.CacheDirectoryOverride = null;
            string environmentKey = new string('b', 64);
            Pk3DiscoveryService.Store(environmentKey, cacheProbe, default);
            check(File.Exists(Path.Combine(overrideCache, overrideKey, "probe.bin"))
                && File.Exists(Path.Combine(envCache, environmentKey, "probe.bin")),
                "Q3 conversion cache honors internal test override before PROJECT_PRIME_Q3_CACHE");

            string orphanCache = Path.Combine(root, "orphan-q3-cache");
            string orphanKey = new string('c', 64);
            string orphanPending = Path.Combine(orphanCache,
                orphanKey + ".pending-" + Guid.NewGuid().ToString("N"));
            string unrelatedPending = Path.Combine(orphanCache, "unrelated.pending-not-owned");
            Directory.CreateDirectory(orphanPending);
            Directory.CreateDirectory(unrelatedPending);
            Pk3DiscoveryService.CacheDirectoryOverride = orphanCache;
            string recoveryKey = new string('d', 64);
            bool missingRecoveryArtifact = !Pk3DiscoveryService.Restore(recoveryKey,
                Path.Combine(root, "orphan-recovery-staging"), default);
            Pk3DiscoveryService.Store(recoveryKey, cacheProbe, default);
            check(missingRecoveryArtifact && !Directory.Exists(orphanPending)
                && Directory.Exists(unrelatedPending)
                && File.Exists(Path.Combine(orphanCache, recoveryKey, "probe.bin")),
                "Q3 cache removes only abandoned valid pending artifacts and recovers publication");
        }
        finally
        {
            Pk3DiscoveryService.CacheDirectoryOverride = previousCacheOverride;
            Environment.SetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE", previousCacheEnvironment);
        }

        var noFallback = Q3ImportService.Import(options with { AllowFallbackTextures = false,
            DestinationDirectory = Path.Combine(root, "pk3-no-fallback") });
        check(!noFallback.Succeeded && !Directory.Exists(Path.Combine(root, "pk3-no-fallback")),
            "PK3 explicit fallback texture choice rejects missing art before publication");
        byte[] original = File.ReadAllBytes(archive);
        var first = Q3ImportService.Import(options);
        var second = Q3ImportService.Import(options with { DestinationDirectory = Path.Combine(root, "pk3-second") });
        check(first.Succeeded && second.Succeeded && second.Diagnostics.Any(d => d.Message == "Q3 conversion cache hit."),
            "PK3 unchanged second import reuses conversion cache");
        if (first.ProjectPath != null && second.ProjectPath != null)
        {
            var firstMap = MapDefinition.Load(first.ProjectPath);
            var secondMap = MapDefinition.Load(second.ProjectPath);
            check(firstMap.MapId != secondMap.MapId && secondMap.Import!.MapName == "beta"
                && firstMap.FormatVersion == 3 && secondMap.FormatVersion == 3,
                "PK3 cached imports receive independent map identities and selected level");
            firstMap.Description = "authored description";
            Guid identity = firstMap.MapId;
            var reimported = Q3ImportService.Reimport(firstMap, options, first.ProjectPath);
            var reopened = MapDefinition.Load(first.ProjectPath);
            check(reimported.Succeeded && reopened.MapId == identity && reopened.Description == "authored description",
                "PK3 cached reimport preserves authored metadata and map identity");
        }

        string relocationOne = Path.Combine(root, "job-uuid-one");
        string relocationTwo = Path.Combine(root, "job-uuid-two");
        Directory.CreateDirectory(relocationOne); Directory.CreateDirectory(relocationTwo);
        string relocatedSourceOne = Path.Combine(relocationOne, "upload.pk3");
        string relocatedSourceTwo = Path.Combine(relocationTwo, "upload.pk3");
        string relocatedDependencyOne = Path.Combine(relocationOne, "textures.pk3");
        string relocatedDependencyTwo = Path.Combine(relocationTwo, "textures.pk3");
        string relocatedDependencyFixture = Path.Combine(root, "relocated-textures.pk3");
        File.Copy(priorityOne, relocatedDependencyFixture);
        File.Copy(archive, relocatedSourceOne);
        File.Copy(archive, relocatedSourceTwo);
        File.Copy(relocatedDependencyFixture, relocatedDependencyOne);
        File.Copy(relocatedDependencyFixture, relocatedDependencyTwo);
        var relocatedOptionsOne = options with
        {
            Source = relocatedSourceOne, RoomName = "PK3_RELOCATED",
            DestinationDirectory = Path.Combine(root, "relocated-import-one"),
            Dependencies = new[] { relocatedDependencyOne }
        };
        var relocatedOptionsTwo = options with
        {
            Source = relocatedSourceTwo, RoomName = "PK3_RELOCATED",
            DestinationDirectory = Path.Combine(root, "relocated-import-two"),
            Dependencies = new[] { relocatedDependencyTwo }
        };
        IReadOnlyList<string> relocatedArchivesOne = MapTextureBake.DiscoverArchives(
            relocatedOptionsOne.Source, relocatedOptionsOne.Dependencies);
        IReadOnlyList<string> relocatedArchivesTwo = MapTextureBake.DiscoverArchives(
            relocatedOptionsTwo.Source, relocatedOptionsTwo.Dependencies);
        string relocatedKeyOne = Pk3DiscoveryService.Fingerprint(relocatedOptionsOne, "beta", relocatedArchivesOne);
        string relocatedKeyTwo = Pk3DiscoveryService.Fingerprint(relocatedOptionsTwo, "beta", relocatedArchivesTwo);
        var relocatedFirst = Q3ImportService.Import(relocatedOptionsOne);
        var relocatedSecond = Q3ImportService.Import(relocatedOptionsTwo);
        bool relativeRelocatedPaths = false;
        if (relocatedSecond.ProjectPath != null)
        {
            var relocatedMap = MapDefinition.Load(relocatedSecond.ProjectPath);
            string projectDirectory = Path.GetDirectoryName(relocatedSecond.ProjectPath)!;
            relativeRelocatedPaths = relocatedMap.Import != null
                && !Path.IsPathRooted(relocatedMap.Import.Source)
                && !String.IsNullOrEmpty(relocatedMap.Import.Textures)
                && !Path.IsPathRooted(relocatedMap.Import.Textures)
                && File.Exists(Path.Combine(projectDirectory, relocatedMap.Import.Source))
                && File.Exists(Path.Combine(projectDirectory, relocatedMap.Import.Textures));
        }
        check(relocatedKeyOne == relocatedKeyTwo && relocatedFirst.Succeeded && relocatedSecond.Succeeded
            && relocatedSecond.Diagnostics.Any(d => d.Message == "Q3 conversion cache hit.") && relativeRelocatedPaths,
            "identical relocated PK3 imports reuse conversion while retaining relative source and texture paths");

        check(Pk3DiscoveryService.CacheDirectoryOverride != null
            && Path.GetFullPath(Pk3DiscoveryService.CacheDirectoryOverride).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal), "Q3 checks isolate conversion cache from user artifacts");
        string rollbackFile = Path.Combine(root, "reimport-rollback-original.bin");
        File.WriteAllBytes(rollbackFile, new byte[] { 1, 2, 3 });
        string blockedParent = Path.Combine(root, "reimport-blocked-parent");
        File.WriteAllBytes(blockedParent, new byte[] { 0 });
        bool publicationRejected = false;
        try
        {
            Q3ImportService.PublishReimport(new Dictionary<string, byte[]>
            {
                [rollbackFile] = new byte[] { 9, 8, 7 },
                [Path.Combine(blockedParent, "cannot-write.bin")] = new byte[] { 4 }
            }, Path.Combine(root, "reimport-rollback-backups"), default);
        }
        catch (IOException) { publicationRejected = true; }
        check(publicationRejected && File.ReadAllBytes(rollbackFile).SequenceEqual(new byte[] { 1, 2, 3 }),
            "Q3 failed multi-file reimport publication restores original assets");
        string hugeArtifact = Path.Combine(Pk3DiscoveryService.CacheDirectoryOverride!, new string('f', 64));
        Directory.CreateDirectory(hugeArtifact);
        File.WriteAllText(Path.Combine(hugeArtifact, ".cache-hashes"), "{}");
        using (var huge = File.Create(Path.Combine(hugeArtifact, "oversized.bin")))
            huge.SetLength(Pk3DiscoveryService.MaxArchiveBytes + 1);
        string hugeRestore = Path.Combine(root, "oversized-cache-restore");
        check(!Pk3DiscoveryService.Restore(new string('f', 64), hugeRestore, default)
            && !Directory.Exists(hugeRestore) && !Directory.Exists(hugeArtifact),
            "Q3 cache rejects oversized local artifacts before restoring bytes");
        check(File.ReadAllBytes(archive).SequenceEqual(original), "PK3 discovery and cached imports never mutate source archives");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        { using var stream = zip.CreateEntry("extra.txt").Open(); stream.WriteByte(42); }
        check(key != Pk3DiscoveryService.Fingerprint(options, "beta", new[] { archive }),
            "PK3 cache fingerprints changed archive bytes");
        string dependency = Path.Combine(root, "dependency.pk3");
        using (var zip = ZipFile.Open(dependency, ZipArchiveMode.Create)) zip.CreateEntry("textures/info.txt");
        string dependencyKey = Pk3DiscoveryService.Fingerprint(options, "beta", new[] { archive, dependency });
        using (var zip = ZipFile.Open(dependency, ZipArchiveMode.Update)) zip.CreateEntry("textures/changed.txt");
        check(dependencyKey != Pk3DiscoveryService.Fingerprint(options, "beta", new[] { archive, dependency }),
            "PK3 cache fingerprints dependency archive bytes");
        var raceOptions = options with { RoomName = "PK3_RACE", DestinationDirectory = Path.Combine(root, "pk3-source-race") };
        string raceKey = Pk3DiscoveryService.Fingerprint(raceOptions, "beta", MapTextureBake.DiscoverArchives(archive));
        bool changedDuringImport = false;
        var race = Q3ImportService.Import(raceOptions, progress: message =>
        {
            if (changedDuringImport || message != "Copying source and preparing textures…") return;
            changedDuringImport = true;
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Update);
            using var stream = zip.CreateEntry("changed-during-import.txt").Open(); stream.WriteByte(17);
        });
        check(changedDuringImport && !race.Succeeded && !Directory.Exists(raceOptions.DestinationDirectory)
            && !Directory.Exists(Path.Combine(Pk3DiscoveryService.CacheDirectoryOverride!, raceKey))
            && race.Diagnostics.Any(d => d.Message.Contains("changed during import")),
            "Q3 changing source is rejected before cache storage and project publication");
        string art = Path.Combine(root, "art-cache.pk3");
        void WriteArt(byte color)
        {
            File.Delete(art);
            using var zip = ZipFile.Open(art, ZipArchiveMode.Create);
            using var stream = zip.CreateEntry("textures/prime/test.tga", CompressionLevel.NoCompression).Open();
            byte[] image = new byte[30]; image[2] = 2; image[12] = 2; image[14] = 2;
            image[16] = 24; image[17] = 0x20;
            for (int i = 18; i < image.Length; i += 3) image[i + 2] = color;
            stream.Write(image);
        }
        WriteArt(240);
        DateTime timestamp = File.GetLastWriteTimeUtc(art);
        string artPack = Path.Combine(root, "art-cache.tex");
        MapTextureBake.Bake(Q3Bsp.Load(bsp, null), new[] { art }, artPack);
        byte[] oldArt = File.ReadAllBytes(artPack);
        WriteArt(32); File.SetLastWriteTimeUtc(art, timestamp);
        MapTextureBake.Bake(Q3Bsp.Load(bsp, null), new[] { art }, artPack);
        check(!File.ReadAllBytes(artPack).SequenceEqual(oldArt),
            "Q3 texture bake cache detects changed art with unchanged archive size and timestamp");
        File.Delete(art);
        string bomb = Path.Combine(root, "compression-bomb.pk3");
        using (var zip = ZipFile.Open(bomb, ZipArchiveMode.Create))
        { using var stream = zip.CreateEntry("huge.bin", CompressionLevel.SmallestSize).Open(); stream.Write(new byte[4 * 1024 * 1024]); }
        bool bombRejected = false;
        try { Pk3DiscoveryService.Discover(bomb); } catch (InvalidDataException) { bombRejected = true; }
        check(bombRejected, "PK3 decompression bomb rejected before reading payload");
        File.Delete(bomb);
        string oversized = Path.Combine(root, "oversized.pk3");
        using (var stream = File.Create(oversized)) stream.SetLength(Pk3DiscoveryService.MaxArchiveBytes + 1);
        bool oversizedRejected = false;
        try { Pk3DiscoveryService.Discover(oversized); } catch (InvalidDataException) { oversizedRejected = true; }
        check(oversizedRejected, "PK3 oversized archive rejected before opening ZIP");
        File.Delete(oversized);
        byte[] unsupported = File.ReadAllBytes(bsp);
        BitConverter.GetBytes(47).CopyTo(unsupported, 4);
        string unsupportedBsp = Path.Combine(root, "unsupported.bsp");
        File.WriteAllBytes(unsupportedBsp, unsupported);
        var unsupportedResult = Q3ImportService.Import(options with { Source = unsupportedBsp, MapName = null,
            DestinationDirectory = Path.Combine(root, "unsupported-project") });
        check(!unsupportedResult.Succeeded && !Directory.Exists(Path.Combine(root, "unsupported-project")),
            "Q3 unsupported BSP version rejected transactionally");
        foreach (string[] entries in new[] { new[] { "../escape.bsp" }, new[] { "maps/a.bsp", "MAPS/A.BSP" },
            new[] { "maps/a.bsp", "other/a.bsp" }, new[] { "/absolute.bsp" } })
        {
            File.Delete(archive); Archive(entries);
            string destination = Path.Combine(root, "pk3-rejected");
            var result = Q3ImportService.Import(options with { DestinationDirectory = destination });
            check(!result.Succeeded && !Directory.Exists(destination)
                && !Directory.EnumerateDirectories(root, ".pk3-rejected.import-*" ).Any(),
                "PK3 malicious or ambiguous archive rejected transactionally: " + string.Join(",", entries));
        }
    }
}
