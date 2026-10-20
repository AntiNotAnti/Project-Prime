using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

// Explicitly opt-in transport witness. Every derived native/companion file is
// temporary and removed in finally; stdout contains only counts/hashes/status.
// No source coordinates, positions, images, assets or playable edition escape.
internal static class MapSourceGeometryOwnedProbe
{
    private const int FacesPerMesh = 4096;
    private readonly record struct CornerKey(int X, int Y, int Z, int U, int V, int S, int T);
    private sealed record MaterialWitness(int Ordinal, int Triangles, int Corners, string SourceUvSha256,
        string EmittedUvSha256, string SourcePackedPositionUvSha256, string EmittedPackedPositionUvSha256);

    internal static int Run(string ownedSource)
    {
        string? before = null, after = null, gameId = null, archiveSha1 = null, failure = null, exceptionMessageSha256 = null;
        string stage = "Initialize";
        string assemblyBefore = HashFile(typeof(MapSourceGeometry).Assembly.Location);
        string toolBefore = HashFile(typeof(MapSourceGeometryOwnedProbe).Assembly.Location);
        string root = Path.Combine(Path.GetTempPath(), "prime-owned-companions-" + Guid.NewGuid().ToString("N"));
        string oldMaps = CustomRooms.MapDirectory, oldUserMaps = CustomRooms.UserMapDirectory;
        bool oldDeferred = CustomRooms.DeferInitialRegistration;
        bool removed = false;
        var arenas = new List<object>();
        int materialTotal = 0, candidateTotal = 0, triangleTotal = 0, candidateTriangles = 0, sourceCorners = 0, candidateVisits = 0;
        long companionCorners = 0, companionEntries = 0;
        try
        {
            Directory.CreateDirectory(root);
            // Avoid consulting a caller's custom map catalog when Metadata's
            // built-in-name guard initializes. No configured MPH files needed.
            CustomRooms.MapDirectory = Path.Combine(root, "empty-maps");
            CustomRooms.UserMapDirectory = Path.Combine(root, "empty-library");
            CustomRooms.DeferInitialRegistration = true;
            stage = "VerifyOwnedSource";
            before = HashFile(ownedSource);
            using var disc = GameCubeDiscReader.Open(ownedSource);
            gameId = disc.GameId;
            Require(disc.Game == ExternalGameKind.Echoes && gameId == "G2ME01"
                && disc.Revision == 0 && disc.DiscNumber == 0, "Pinned USA revision-0 disc-0 Echoes source required.");
            var requirement = ExternalSourceRequirements.For(disc.Game).Single(r => r.Path == "Metroid6.pak");
            var proof = ExternalSourceRequirements.Verify(disc, requirement);
            archiveSha1 = proof.ActualSha1;
            Require(proof.Status == ExternalArchiveStatus.Verified, "Pinned Metroid6.pak verification failed.");
            using var stream = disc.OpenRead(requirement.Path);
            using var pak = new RetroPakReader(stream);
            int ordinal = 0;
            foreach (var arena in EchoesArenaSources.All)
            {
                Require(ordinal < 6 && pak.Contains(arena.ResourceId) && pak.Entry(arena.ResourceId).Type == "MREA",
                    "Expected six-arena MREA source missing.");
                stage = "ReadSourceGeometry";
                byte[] mrea = pak.ReadResource(arena.ResourceId);
                var source = EchoesWorldGeometrySource.ReadWithSourceCoordinates(EchoesMreaSections.Parse(mrea));
                Require(source.Triangles.Count == source.Geometry.TriangleCount, "Source triangle records lost alignment.");
                int candidates = source.Materials.Count(m => m.CommonOpaqueCandidate);
                int candidateCount = source.Triangles.Count(t => t.Coordinates != null);
                materialTotal += source.Materials.Count; candidateTotal += candidates;
                triangleTotal += source.Geometry.TriangleCount; candidateTriangles += candidateCount;
                sourceCorners += source.SourceCornerVisits; candidateVisits += source.CandidateSourceCornerVisits;
                string directory = Path.Combine(root, "arena-" + ordinal);
                Directory.CreateDirectory(directory);
                MapDefinition definition = Definition(source, arena.ResourceId, Hash(mrea), directory);
                var geometryIssues = AreaCounts(source.Geometry);
                stage = "MapCompiler";
                MapCompilation compilation = MapCompiler.Compile(definition);
                if (!compilation.Validation.IsValid || compilation.Map == null)
                {
                    failure ??= "Owned geometry did not satisfy the existing authored compiler; no source faces were filtered or repaired.";
                    arenas.Add(new { arena.Key, MreaResourceId = arena.ResourceId.ToString("x8"), MreaSha256 = Hash(mrea),
                        Passed = false, Stage = "MapCompiler", source.Geometry.PositionCount, source.Geometry.TriangleCount,
                        SourceMaterialCount = source.Materials.Count, CandidateMaterialCount = candidates,
                        CandidateTriangles = candidateCount, ExpectedCompanionCorners = 3L * candidateCount,
                        source.SourceCornerVisits, source.CandidateSourceCornerVisits, GeometryAreaCounts = geometryIssues,
                        SyntheticCollisionFaces = 1, SourceCollisionFaces = 0,
                        ErrorCount = compilation.Validation.Diagnostics.Count(d => d.Severity == MapDiagnosticSeverity.Error),
                        Errors = compilation.Validation.Diagnostics.Where(d => d.Severity == MapDiagnosticSeverity.Error)
                            .Take(8).Select(d => new { d.Code, d.Message }).ToArray() });
                    ordinal++; continue;
                }
                Require(compilation.Map.Solid.Count == 1 && compilation.Map.Faces.Count == source.Geometry.TriangleCount,
                    "Compiled source/collision counts differ; source faces must remain unfiltered and render-only.");
                stage = "MapPackerGenerate";
                MapPacker.Generate(compilation.Map, directory, directory, directory, verbose: false);
                MapOutputSet outputs = MapOutputSet.Create(definition, directory, directory, directory);
                Require(outputs.SourceGeometry != null && outputs.Files.Count() == 6 && outputs.Complete
                    && MapBuildManifest.IsCurrent(definition, outputs), "Six-output publication or manifest integrity failed.");
                byte[] native = File.ReadAllBytes(outputs.Model), component = File.ReadAllBytes(outputs.SourceGeometry!);
                var fingerprint = MapBuildFingerprint.Create(definition);
                stage = "DecodeCompanion";
                var decoded = MapSourceGeometry.Decode(component, native, definition.MapId, fingerprint.RecipeHash);
                Require(decoded.MapId == definition.MapId && decoded.RecipeSha256 == fingerprint.RecipeHash
                    && decoded.NativeModelSha256 == Hash(native) && decoded.ComponentSha256 == Hash(component),
                    "Decoded transport identity differs from the exact generated artifacts.");
                stage = "CompareSourceMultisets";
                var expected = Expected(source, definition.ScaleFactor);
                var actual = Emitted(decoded, native);
                var witnesses = new List<MaterialWitness>();
                foreach (var material in source.Materials)
                {
                    var wanted = expected.GetValueOrDefault(material.Ordinal) ?? new Dictionary<CornerKey, int>();
                    var got = actual.GetValueOrDefault(material.Ordinal) ?? new Dictionary<CornerKey, int>();
                    Require(Same(wanted, got), "Source packed-position/paired-UV multiset changed for material " + material.Ordinal + ".");
                    if (!material.CommonOpaqueCandidate)
                    { Require(wanted.Count == 0 && got.Count == 0, "Unknown program acquired a companion channel."); continue; }
                    Require(wanted.Values.Sum() > 0 && wanted.Values.Sum() % 3 == 0,
                        "Candidate material has no complete transported source triangles.");
                    string wantedUv = Digest(wanted, positions: false), gotUv = Digest(got, positions: false);
                    Require(wantedUv == gotUv, "Paired UV float bits changed independently of position identity.");
                    witnesses.Add(new(material.Ordinal, wanted.Values.Sum() / 3, wanted.Values.Sum(),
                        wantedUv, gotUv, Digest(wanted, positions: true), Digest(got, positions: true)));
                }
                int corners = decoded.Entries.Sum(e => e.Corners.Count);
                Require(actual.Keys.All(id => expected.ContainsKey(id)) && witnesses.Count == candidates
                    && decoded.Entries.All(e => e.Quads == 0) && decoded.Entries.Sum(e => e.Triangles) == candidateCount
                    && corners == checked(3 * candidateCount), "Companion inventory differs from expanded source triangles.");
                companionCorners += corners; companionEntries += decoded.Entries.Count;
                arenas.Add(new { arena.Key, MreaResourceId = arena.ResourceId.ToString("x8"), MreaSha256 = Hash(mrea),
                    Passed = true, Stage = "Verified", source.Geometry.PositionCount, source.Geometry.TriangleCount,
                    SourceMaterialCount = source.Materials.Count, CandidateMaterialCount = candidates,
                    CandidateTriangles = candidateCount, source.SourceCornerVisits, source.CandidateSourceCornerVisits,
                    CompanionEntries = decoded.Entries.Count, CompanionCorners = corners,
                    ExactPairedUvFloatBitsMultiset = true, ExactNativePackedPositionPairedUvMultiset = true,
                    DefinitionRecipeSha256 = fingerprint.RecipeHash, NativeModelSha256 = decoded.NativeModelSha256,
                    ComponentSha256 = decoded.ComponentSha256, GeometryAreaCounts = geometryIssues,
                    SyntheticCollisionFaces = 1, SourceCollisionFaces = 0,
                    Outputs = outputs.Files.Select(p => new { Name = Path.GetFileName(p), Bytes = new FileInfo(p).Length, Sha256 = HashFile(p) }).ToArray(),
                    MaterialWitnesses = witnesses });
                // Retain no generated payload from already verified arenas
                // while later arenas compile; the outer finally also cleans failures.
                Directory.Delete(directory, recursive: true);
                ordinal++;
            }
            Require(arenas.Count == 6 && materialTotal == 347 && candidateTotal == 260,
                "Expected complete six-arena material inventory changed.");
            if (failure == null)
                Require(candidateTriangles == 54069 && companionCorners == 162207,
                    "Expected candidate expanded-corner transport inventory changed.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { failure ??= ex.GetType().Name + ": transport probe operation failed."; exceptionMessageSha256 = Hash(Encoding.UTF8.GetBytes(ex.Message)); }
        finally
        {
            CustomRooms.MapDirectory = oldMaps; CustomRooms.UserMapDirectory = oldUserMaps;
            CustomRooms.DeferInitialRegistration = oldDeferred;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); removed = !Directory.Exists(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { failure ??= "Temporary derived payload cleanup failed: " + ex.GetType().Name; }
            try { after = HashFile(ownedSource); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { failure ??= "Source final hash failed: " + ex.GetType().Name; }
        }
        string assemblyAfter = HashFile(typeof(MapSourceGeometry).Assembly.Location);
        string toolAfter = HashFile(typeof(MapSourceGeometryOwnedProbe).Assembly.Location);
        bool unchanged = before != null && before == after;
        bool passed = failure == null && removed && unchanged && assemblyBefore == assemblyAfter && toolBefore == toolAfter && arenas.Count == 6;
        Console.WriteLine(JsonSerializer.Serialize(new {
            Schema = 1, Passed = passed, Error = failure, LastStage = stage, ExceptionMessageSha256 = exceptionMessageSha256, GameId = gameId, ArchiveSha1 = archiveSha1,
            SourceSha256Before = before, SourceSha256After = after, SourceUnchanged = unchanged,
            AssemblySha256Before = assemblyBefore, AssemblySha256After = assemblyAfter, AssemblyUnchanged = assemblyBefore == assemblyAfter,
            ToolSha256Before = toolBefore, ToolSha256After = toolAfter, ToolUnchanged = toolBefore == toolAfter,
            TransportVerificationOnly = true, RenderingActivated = false, RuntimeArenaEditionCreated = false,
            SourceMediaModified = false, TemporaryDerivedFilesRemoved = removed, CommercialPayloadsRetained = !removed,
            CoordinateContract = EchoesWorldGeometrySource.CoordinateContract,
            SourcePositionPolicy = "original source float positions; no projection, repair, scaling or axis conversion",
            PositionComparisonPolicy = "exact native signed16 fixed-point vertex components paired with original normalized float32 UV bits; not full-float position preservation",
            SourceMaterialCount = materialTotal, CandidateMaterialCount = candidateTotal, SourceTriangleCount = triangleTotal,
            CandidateTriangleCount = candidateTriangles, SourceCornerVisits = sourceCorners, CandidateSourceCornerVisits = candidateVisits,
            CompanionEntryCount = companionEntries, CompanionCornerCount = companionCorners,
            SyntheticCollisionFacesPerArena = 1, SourceCollisionFaces = 0, SourceFacesFiltered = 0,
            Arenas = arenas
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    private static MapDefinition Definition(EchoesWorldSourceGeometry source, uint resource, string mreaHash, string directory)
    {
        var definition = new MapDefinition { FormatVersion = 2, MapId = Stable("owned-companion-probe/" + resource.ToString("x8")),
            Name = "PROBE COMPANION " + resource.ToString("X8"), LocalSourceOnly = true, LocalSourceFingerprint = mreaHash,
            TextureSource = "NO BORROWED CARTRIDGE MATERIAL", BaseDirectory = directory, ScaleFactor = 0,
            FogEnabled = false, FarClip = 50000, KillHeight = -500000 };
        foreach (var material in source.Materials)
            definition.Materials.Add(new() { Id = Stable(definition.MapId.ToString("N") + "/material/" + material.Ordinal),
                Name = "M" + material.Ordinal.ToString("D4"), ProceduralWhite = true, TexScale = 1 });
        foreach (var group in Enumerable.Range(0, source.Triangles.Count).GroupBy(i => source.Triangles[i].MaterialOrdinal).OrderBy(g => g.Key))
        {
            Require((uint)group.Key < source.Materials.Count, "Source triangle material ordinal is invalid.");
            int[] indices = group.ToArray(); bool candidate = source.Materials[group.Key].CommonOpaqueCandidate;
            for (int start = 0; start < indices.Length; start += FacesPerMesh)
            {
                var mesh = new MapMesh { Id = Stable(definition.MapId.ToString("N") + "/material/" + group.Key + "/chunk/" + start),
                    Label = "Source visual material " + group.Key, Material = group.Key, Solid = false,
                    SourceUvFaces = candidate ? new List<MapSourceUvFace?>() : null };
                var local = new Dictionary<int, int>();
                foreach (int triangle in indices.Skip(start).Take(FacesPerMesh))
                {
                    var record = source.Triangles[triangle];
                    Require(record.Coordinates.HasValue == candidate, "Candidate source-coordinate coverage is incomplete.");
                    var ring = new int[3];
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = source.Geometry.TriangleVertices[3 * triangle + corner];
                        Require((uint)vertex < source.Geometry.PositionCount, "Source position index is invalid.");
                        if (!local.TryGetValue(vertex, out int mapped))
                        {
                            mapped = mesh.Vertices.Count; local.Add(vertex, mapped);
                            mesh.Vertices.Add(new[] { source.Geometry.Positions[3 * vertex], source.Geometry.Positions[3 * vertex + 1], source.Geometry.Positions[3 * vertex + 2] });
                        }
                        ring[corner] = mapped;
                    }
                    mesh.Faces.Add(ring); mesh.FaceMaterials.Add(group.Key);
                    if (record.Coordinates is { } uv)
                    {
                        var corners = new[] { uv.A, uv.B, uv.C };
                        mesh.SourceUvFaces!.Add(new MapSourceUvFace {
                            Base = corners.Select(c => new[] { c.BaseU, c.BaseV }).ToArray(),
                            Lightmap = corners.Select(c => new[] { c.LightmapU, c.LightmapV }).ToArray() });
                    }
                    else mesh.FaceTexcoords.Add(new[] { new float[2], new float[2], new float[2] });
                }
                definition.Geometry.Add(mesh);
            }
        }
        // Deliberately invented probe scaffolding only. Source visual faces
        // never become physics, and no inferred source collision is generated.
        definition.Geometry.Add(new MapMesh { Id = Stable(definition.MapId.ToString("N") + "/own-floor"),
            Label = "Own-generated probe collision scaffold", Material = 0, CollisionOnly = true, Solid = true,
            Vertices = new() { new[] { -1f, 0f, -1f }, new[] { 0f, 0f, 1f }, new[] { 1f, 0f, -1f } },
            Faces = new() { new[] { 0, 1, 2 } }, FaceTexcoords = new() { new[] { new float[2], new float[2], new float[2] } } });
        definition.Spawns.Add(new() { Id = Stable(definition.MapId.ToString("N") + "/own-spawn"), Position = new[] { 0f, 2f, 0f } });
        float extent = Math.Max(2, source.Geometry.Positions.Max(p => Math.Abs(p)));
        while (extent >= 8f * MathF.Pow(2, definition.ScaleFactor))
            if (++definition.ScaleFactor > 16) throw new InvalidDataException("Source positions exceed native probe extent.");
        return definition;
    }

    private static Dictionary<int, Dictionary<CornerKey, int>> Expected(EchoesWorldSourceGeometry source, int scaleFactor)
    {
        var result = new Dictionary<int, Dictionary<CornerKey, int>>(); float scale = MathF.Pow(2, scaleFactor);
        for (int i = 0; i < source.Triangles.Count; i++)
        {
            var triangle = source.Triangles[i]; if (triangle.Coordinates is not { } uv) continue;
            if (!result.TryGetValue(triangle.MaterialOrdinal, out var corners)) result.Add(triangle.MaterialOrdinal, corners = new());
            var coordinates = new[] { uv.A, uv.B, uv.C };
            for (int c = 0; c < 3; c++)
            {
                int vertex = source.Geometry.TriangleVertices[3 * i + c] * 3;
                int X = Fixed.ToInt(source.Geometry.Positions[vertex] / scale), Y = Fixed.ToInt(source.Geometry.Positions[vertex + 1] / scale), Z = Fixed.ToInt(source.Geometry.Positions[vertex + 2] / scale);
                Require(X is >= short.MinValue and <= short.MaxValue && Y is >= short.MinValue and <= short.MaxValue && Z is >= short.MinValue and <= short.MaxValue,
                    "Expected native vertex quantization exceeds signed16.");
                Add(corners, Key(X, Y, Z, coordinates[c]));
            }
        }
        return result;
    }
    private static Dictionary<int, Dictionary<CornerKey, int>> Emitted(MapSourceGeometry.Snapshot source, byte[] native)
    {
        var result = new Dictionary<int, Dictionary<CornerKey, int>>();
        var header = Read.ReadStruct<Header>(native.AsSpan(0, Sizes.Header));
        var lists = Read.DoOffsets<DisplayList>(native, header.DlistOffset, header.MeshCount);
        foreach (var entry in source.Entries)
        {
            if (!result.TryGetValue(entry.MaterialId, out var corners)) result.Add(entry.MaterialId, corners = new());
            int at = 0;
            Read.VisitRenderInstructions(native, lists[entry.DlistId], (code, args) => {
                if (code != InstructionCode.VTX_16) return;
                Require(at < entry.Corners.Count, "Native list overran companion corner stream.");
                Add(corners, Key(unchecked((short)(args[0] & 0xffff)), unchecked((short)(args[0] >> 16)), unchecked((short)(args[1] & 0xffff)), entry.Corners[at++]));
            });
            Require(at == entry.Corners.Count, "Native list did not consume the entire paired-coordinate stream.");
        }
        return result;
    }
    private static CornerKey Key(int x, int y, int z, EchoesSourceCoordinates uv) => new(x,y,z,
        BitConverter.SingleToInt32Bits(uv.BaseU),BitConverter.SingleToInt32Bits(uv.BaseV),
        BitConverter.SingleToInt32Bits(uv.LightmapU),BitConverter.SingleToInt32Bits(uv.LightmapV));
    private static void Add(Dictionary<CornerKey,int> target, CornerKey key) => target[key] = checked(target.GetValueOrDefault(key) + 1);
    private static bool Same(Dictionary<CornerKey,int> a, Dictionary<CornerKey,int> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
    private static string Digest(Dictionary<CornerKey,int> corners, bool positions)
    {
        if (!positions)
        {
            var collapsed = new Dictionary<CornerKey,int>();
            foreach(var pair in corners) { var key=pair.Key with { X=0,Y=0,Z=0 };collapsed[key]=checked(collapsed.GetValueOrDefault(key)+pair.Value); }
            corners=collapsed;
        }
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); Span<byte> bytes=stackalloc byte[32];
        foreach(var pair in corners.OrderBy(p=>p.Key.X).ThenBy(p=>p.Key.Y).ThenBy(p=>p.Key.Z).ThenBy(p=>p.Key.U).ThenBy(p=>p.Key.V).ThenBy(p=>p.Key.S).ThenBy(p=>p.Key.T))
        {
            int[] values={pair.Key.X,pair.Key.Y,pair.Key.Z,pair.Key.U,pair.Key.V,pair.Key.S,pair.Key.T,pair.Value};
            for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(bytes[(4*i)..],values[i]);hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static object AreaCounts(EchoesWorldGeometry geometry)
    {
        int zero=0,tiny=0;for(int i=0;i<geometry.TriangleCount;i++)
        {
            int a=3*geometry.TriangleVertices[3*i],b=3*geometry.TriangleVertices[3*i+1],c=3*geometry.TriangleVertices[3*i+2];
            float x=geometry.Positions[b]-geometry.Positions[a],y=geometry.Positions[b+1]-geometry.Positions[a+1],z=geometry.Positions[b+2]-geometry.Positions[a+2];
            float u=geometry.Positions[c]-geometry.Positions[a],v=geometry.Positions[c+1]-geometry.Positions[a+1],w=geometry.Positions[c+2]-geometry.Positions[a+2];
            float nx=y*w-z*v,ny=z*u-x*w,nz=x*v-y*u,squared=nx*nx+ny*ny+nz*nz;
            if(squared==0)zero++;else if(squared<1e-10f)tiny++;
        }
        return new { OriginalFloatPositionExactZeroCrossTriangles=zero,OriginalFloatPositionNonzeroBelowCompilerThresholdTriangles=tiny };
    }
    private static Guid Stable(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0,16));
    private static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path){using var file=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(file));}
    private static void Require(bool pass,string message){if(!pass)throw new InvalidDataException(message);}
}
