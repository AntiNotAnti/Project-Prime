using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen
{
    public static class MapPackageBuilder
    {
        private const int CopyBufferBytes = 65536;
        // Generated BSP/collision content is still produced by legacy parsers
        // as byte[], but large results are immediately spooled to a private
        // scratch file rather than retained alongside every authoring asset.
        private const int GeneratedMemoryLimit = 1024 * 1024;

        // A payload has one stable expected length, but opens a fresh readable
        // source stream for the hash pass and for ZIP emission. The callback
        // owns the stream lifetime, including the containing source ZIP.
        private sealed record Payload(long Length, Action<Action<Stream, long>> WithRead)
        {
            public void Read(Action<Stream> consumer)
                => WithRead((stream, length) =>
                {
                    if (length != Length)
                        throw new InvalidDataException(
                            "Map package source changed size between hashing and writing.");
                    consumer(stream);
                });
        }

        public static Guid LegacyId(string name) => new(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                "Fruity Prime legacy map:" + name.ToUpperInvariant())).AsSpan(0, 16));

        public static string Build(MapDefinition source, string outputPath)
        {
            if (source.LocalSourceOnly)
                throw new InvalidDataException("A local game-source import cannot be packaged or published.");
            return MapBuildScheduler.Shared.PackageAsync(
                MapBuildSnapshot.Capture(source), outputPath).GetAwaiter().GetResult();
        }

        // Called by the bounded scheduler after compiler validation. Keep
        // output entry names, ordering, timestamp, compression settings and
        // content-hash framing identical to the existing immutable .ppmap v2
        // contract. Changing any of them would invalidate old package hashes.
        internal static string WriteValidated(MapDefinition source,
            string outputPath, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (source.LocalSourceOnly)
                throw new InvalidDataException("A local game-source import cannot be packaged or published.");
            MapDefinition definition = MapProjectSerializer.Clone(source);
            var entries = new SortedDictionary<string, Payload>(StringComparer.Ordinal);
            string scratch = Path.Combine(Path.GetTempPath(),
                "prime-package-" + Guid.NewGuid().ToString("N"));
            int scratchCounter = 0;

            void Add(string name, Payload payload)
            {
                MapPackageReader.CanonicalName(name);
                if (payload.Length < 0 || payload.Length > MapPackageReader.MaxEntryBytes)
                    throw new InvalidDataException("Package asset is too large.");
                if (!entries.TryAdd(name, payload))
                    throw new InvalidDataException(
                        "Asset conflicts with a generated package entry: " + name);
            }

            void AddGenerated(string name, byte[] bytes)
            {
                cancellation.ThrowIfCancellationRequested();
                if (bytes.LongLength > MapPackageReader.MaxEntryBytes)
                    throw new InvalidDataException("Package asset is too large.");
                if (bytes.Length <= GeneratedMemoryLimit)
                {
                    Add(name, FromBytes(bytes));
                    return;
                }
                Directory.CreateDirectory(scratch);
                string spooled = Path.Combine(scratch,
                    (scratchCounter++).ToString("D4") + ".payload");
                File.WriteAllBytes(spooled, bytes);
                Add(name, FromFile(spooled));
            }

            try
            {
                if (definition.FormatVersion == 1)
                {
                    definition.FormatVersion = 2;
                    definition.MapId = LegacyId(definition.Name);
                }

                if (definition.Import is { } import)
                {
                    string level = import.Resolve() ?? throw new MapAuthoringException(
                        "FP-MAP-005", "Imported level is missing.");
                    AddGenerated("import/level.bsp",
                        Q3Bsp.Trim(Q3Bsp.ReadLevel(level, import.MapName)));
                    string? originalTextureName = import.Textures;
                    Payload? textures = null;
                    if (!string.IsNullOrEmpty(originalTextureName)
                        && import.BundlePath != null)
                    {
                        try
                        {
                            textures = FromBundle(import.BundlePath, originalTextureName);
                        }
                        catch (InvalidDataException ex) when (
                            ex.Message.StartsWith("Missing map asset:", StringComparison.Ordinal))
                        {
                            // Preserve the legacy fallback to a resolved loose
                            // texture pack when the bundle does not contain it.
                        }
                    }
                    string? textureFile = import.ResolveTextures();
                    if (textures == null && textureFile != null)
                        textures = FromFile(textureFile);
                    if (!string.IsNullOrEmpty(originalTextureName) && textures == null)
                        throw new MapAuthoringException("FP-MAP-006",
                            "Texture pack could not be baked or loaded.");
                    import.Source = "import/level.bsp";
                    import.MapName = "level";
                    import.Textures = textures == null ? null : "textures/map.tex";
                    if (textures != null) Add(import.Textures!, textures);
                }

                if (definition.Collision is { Source.Length: > 0 } collision)
                {
                    Payload payload;
                    if (collision.BundlePath != null)
                        payload = FromBundle(collision.BundlePath, collision.Source);
                    else
                    {
                        string file = collision.Resolve()
                            ?? throw new InvalidDataException("Collision mesh is missing.");
                        payload = FromFile(file);
                    }
                    collision.Source = "collision/mesh.obj";
                    Add(collision.Source, payload);
                }

                foreach (string asset in MapDependencyAnalyzer.PackageAssets(source))
                {
                    cancellation.ThrowIfCancellationRequested();
                    Add(asset, FromAsset(source, asset));
                }

                // Keep the Project Prime Studio authoring source editable, but
                // publish older-reader-compatible geometry: patch control grids
                // and Quake face projection metadata are lowered to the existing
                // explicit-UV MapMesh schema. Existing maps are byte-identical
                // because this path is a no-op when no new geometry is present.
                MapPackageGeometryCompatibility.LowerInPlace(definition);

                // Runtime package metadata must not expose private source paths
                // or model authoring baselines, just as before this change.
                definition.ModelSources.Clear();
                foreach (MapAsset asset in definition.Assets)
                    asset.SourcePath = null;
                AddGenerated("project.json",
                    Encoding.UTF8.GetBytes(definition.Serialize()));

                // Two bounded read passes are deliberate: content SHA-256
                // precedes manifest creation, and the original ZIP's ordinal
                // entry order puts manifest.json between other asset names.
                // Spooling every external texture just to write the manifest
                // later would waste enormous disk space and invalidate warm IO
                // measurements. A final strict reader catches any source drift.
                string contentHash = HashContent(entries, cancellation);
                var manifest = new MapPackageManifest
                {
                    Format = definition.FormatVersion,
                    MapId = definition.MapId,
                    Name = definition.Name,
                    DisplayName = definition.InGameName,
                    MinimumProtocol = Network.NetConfig.ProtocolVersion,
                    SupportedModes = definition.Capabilities?.SupportedModes.ToArray()
                        ?? Array.Empty<string>(),
                    MinPlayers = definition.Capabilities?.MinPlayers ?? 1,
                    MaxPlayers = definition.Capabilities?.MaxPlayers ?? 8,
                    MapVersion = definition.Version,
                    Author = definition.Author,
                    Preview = definition.Assets.FirstOrDefault(a => a.Kind == "preview")?.Path,
                    ContentHash = contentHash
                };
                AddGenerated("manifest.json",
                    JsonSerializer.SerializeToUtf8Bytes(manifest, MapPackageReader.JsonOptions));

                if (entries.Count > MapPackageReader.MaxEntries)
                    throw new InvalidDataException("Package contains too many entries.");
                long expanded = 0;
                foreach (var pair in entries)
                {
                    if ((expanded += pair.Value.Length) > MapPackageReader.MaxExpandedBytes)
                        throw new InvalidDataException("Package is too large.");
                }

                string full = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew,
                        FileAccess.ReadWrite, FileShare.None, CopyBufferBytes))
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                    {
                        foreach (var pair in entries)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var entry = archive.CreateEntry(pair.Key,
                                CompressionLevel.SmallestSize);
                            entry.LastWriteTime = new DateTimeOffset(
                                1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                            entry.ExternalAttributes = 0;
                            using Stream target = entry.Open();
                            pair.Value.Read(input => CopyExact(input, target,
                                pair.Value.Length, cancellation, hash: null));
                        }
                    }

                    // Writer success is not a trust decision. The strict reader
                    // recomputes content hash and verifies manifest/refs/limits.
                    // An edited asset or bundle mid-build fails before publish.
                    using (var check = new MapPackageReader(temporary))
                        _ = check.ReadProject();
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(temporary, full, overwrite: true);
                    return full;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            finally
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            }
        }

        private static Payload FromBytes(byte[] bytes)
            => new(bytes.LongLength, visit =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                visit(stream, bytes.LongLength);
            });

        private static Payload FromFile(string path)
        {
            string full = Path.GetFullPath(path);
            long length = new FileInfo(full).Length;
            return new(length, visit =>
            {
                using var stream = new FileStream(full, FileMode.Open,
                    FileAccess.Read, FileShare.Read, CopyBufferBytes,
                    FileOptions.SequentialScan);
                visit(stream, stream.Length);
            });
        }

        private static Payload FromBundle(string bundle, string entry)
        {
            long length = -1;
            MapBundle.WithReadEntryStream(bundle, entry,
                (_, size) => length = size);
            return new(length, visit =>
                MapBundle.WithReadEntryStream(bundle, entry, visit));
        }

        private static Payload FromAsset(MapDefinition definition, string asset)
        {
            long length = MapAssets.PackageLength(definition, asset);
            return new(length, visit => MapAssets.WithPackageRead(definition,
                asset, visit));
        }

        private static string HashContent(
            SortedDictionary<string, Payload> entries, CancellationToken cancellation)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> header = stackalloc byte[8];
            foreach (var pair in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                byte[] name = Encoding.UTF8.GetBytes(pair.Key);
                BinaryPrimitives.WriteInt32LittleEndian(header[..4], name.Length);
                hash.AppendData(header[..4]);
                hash.AppendData(name);
                BinaryPrimitives.WriteInt64LittleEndian(header, pair.Value.Length);
                hash.AppendData(header);
                pair.Value.Read(input => CopyExact(input, null, pair.Value.Length,
                    cancellation, hash));
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        private static void CopyExact(Stream source, Stream? destination,
            long expected, CancellationToken cancellation, IncrementalHash? hash)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
            try
            {
                long remaining = expected;
                while (remaining > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0)
                        throw new InvalidDataException("Map package source was truncated.");
                    hash?.AppendData(buffer, 0, read);
                    destination?.Write(buffer, 0, read);
                    remaining -= read;
                }
                if (source.ReadByte() != -1)
                    throw new InvalidDataException("Map package source grew during packaging.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
