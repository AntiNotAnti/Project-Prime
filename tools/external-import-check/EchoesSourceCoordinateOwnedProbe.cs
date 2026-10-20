using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Import;

// Metadata-only owned-source probe. Source coordinates are never serialized or
// exported; hashes use explicit little-endian float32/int32 bit representations.
internal static class EchoesSourceCoordinateOwnedProbe
{
    private static readonly int[] ExpectedMaterials = { 107, 47, 43, 57, 42, 51 };
    private static readonly int[] ExpectedCandidates = { 88, 29, 35, 41, 29, 38 };
    private static readonly int[] ExpectedCandidateCornerVisits = { 25795, 14460, 17185, 16792, 19509, 15562 };

    internal static int Run(string ownedSource)
    {
        string? before = null, after = null, gameId = null, archiveSha1 = null, error = null;
        string assemblyBefore = HashFile(typeof(EchoesWorldGeometrySource).Assembly.Location);
        var arenas = new List<object>();
        int materialTotal = 0, candidateTotal = 0, usedTotal = 0;
        long triangleTotal = 0, candidateTriangleTotal = 0, sourceCornerVisits = 0, candidateSourceCornerVisits = 0;
        try
        {
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
            var resourceHashes = new Dictionary<uint, string>();
            string TextureHash(uint resource)
            {
                if (resourceHashes.TryGetValue(resource, out string? value)) return value;
                Require(pak.Contains(resource) && pak.Entry(resource).Type == "TXTR", "Candidate binding is not a TXTR.");
                value = Hash(pak.ReadResource(resource));
                resourceHashes.Add(resource, value);
                return value;
            }
            int ordinal = 0;
            foreach (var arena in EchoesArenaSources.All)
            {
                Require(ordinal < 6 && pak.Contains(arena.ResourceId) && pak.Entry(arena.ResourceId).Type == "MREA",
                    "Expected six-arena MREA source missing.");
                byte[] bytes = pak.ReadResource(arena.ResourceId);
                var area = EchoesMreaSections.Parse(bytes);
                EchoesWorldGeometry ordinary = EchoesWorldGeometrySource.Read(area);
                EchoesWorldSourceGeometry source = EchoesWorldGeometrySource.ReadWithSourceCoordinates(area);
                EchoesWorldGeometry geometry = source.Geometry;
                Require(SameFloatBits(geometry.Positions, ordinary.Positions)
                    && geometry.TriangleVertices.AsSpan().SequenceEqual(ordinary.TriangleVertices),
                    "Coordinate opt-in changed ordinary geometry bits or index order.");
                Require(source.Triangles.Count == geometry.TriangleCount, "Source triangle alignment differs from geometry.");
                var used = new HashSet<int>();
                using var coordinateHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var baseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var lightmapHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var baseBounds = new Bounds();
                var lightmapBounds = new Bounds();
                int candidateTriangles = 0, nullTriangles = 0;
                foreach (EchoesSourceTriangle triangle in source.Triangles)
                {
                    Require((uint)triangle.MaterialOrdinal < source.Materials.Count, "Source triangle material is out of range.");
                    used.Add(triangle.MaterialOrdinal);
                    var material = source.Materials[triangle.MaterialOrdinal];
                    Require(material.Ordinal == triangle.MaterialOrdinal, "Material ordinal order differs from its table.");
                    Require(triangle.Coordinates.HasValue == material.CommonOpaqueCandidate,
                        "Candidate channel must exist exactly on candidate triangles.");
                    if (triangle.Coordinates is not { } uv) { nullTriangles++; continue; }
                    candidateTriangles++;
                    Append(uv.A); Append(uv.B); Append(uv.C);
                }
                void Append(EchoesSourceCoordinates uv)
                {
                    baseBounds.Add(uv.BaseU, uv.BaseV);
                    lightmapBounds.Add(uv.LightmapU, uv.LightmapV);
                    Span<byte> encoded = stackalloc byte[16];
                    BinaryPrimitives.WriteSingleLittleEndian(encoded, uv.BaseU);
                    BinaryPrimitives.WriteSingleLittleEndian(encoded[4..], uv.BaseV);
                    BinaryPrimitives.WriteSingleLittleEndian(encoded[8..], uv.LightmapU);
                    BinaryPrimitives.WriteSingleLittleEndian(encoded[12..], uv.LightmapV);
                    coordinateHash.AppendData(encoded);
                    baseHash.AppendData(encoded[..8]);
                    lightmapHash.AppendData(encoded[8..]);
                }
                EchoesSourceMaterial[] candidates = source.Materials.Where(m => m.CommonOpaqueCandidate).ToArray();
                Require(source.Materials.Count == ExpectedMaterials[ordinal] && used.Count == ExpectedMaterials[ordinal]
                    && candidates.Length == ExpectedCandidates[ordinal] && candidates.All(m => used.Contains(m.Ordinal)),
                    "Material/candidate inventory differs from the recorded owned-source witness.");
                Require(source.CandidateSourceCornerVisits == ExpectedCandidateCornerVisits[ordinal],
                    "Candidate source corner visits differ from the unexpanded owned-source witness.");
                Require(candidateTriangles > 0 && nullTriangles + candidateTriangles == geometry.TriangleCount,
                    "Source triangle preservation inventory is incomplete.");
                var bindings = candidates.Select(m => new
                {
                    m.Ordinal, m.ProgramSha256,
                    BaseResourceId = m.BaseResourceId?.ToString("x8"),
                    BaseSourceSha256 = TextureHash(m.BaseResourceId ?? throw new InvalidDataException("Missing candidate base binding.")),
                    LightmapResourceId = m.LightmapResourceId?.ToString("x8"),
                    LightmapSourceSha256 = TextureHash(m.LightmapResourceId ?? throw new InvalidDataException("Missing candidate lightmap binding."))
                }).ToArray();
                materialTotal += source.Materials.Count; candidateTotal += candidates.Length; usedTotal += used.Count;
                triangleTotal += geometry.TriangleCount; candidateTriangleTotal += candidateTriangles;
                sourceCornerVisits += source.SourceCornerVisits;
                candidateSourceCornerVisits += source.CandidateSourceCornerVisits;
                arenas.Add(new
                {
                    arena.Key, arena.Name, MreaResourceId = arena.ResourceId.ToString("x8"), MreaSourceSha256 = Hash(bytes),
                    geometry.PositionCount, geometry.TriangleCount,
                    PositionsFloat32LittleEndianSha256 = HashFloats(geometry.Positions),
                    TriangleVerticesInt32LittleEndianSha256 = HashInts(geometry.TriangleVertices),
                    OrdinaryReadExactBits = true,
                    MaterialCount = source.Materials.Count, UsedMaterialCount = used.Count,
                    CandidateMaterialCount = candidates.Length, UsedCandidateMaterialCount = candidates.Length,
                    source.SourceCornerVisits, source.CandidateSourceCornerVisits, CandidateTriangleCount = candidateTriangles,
                    ExpandedCandidateCornerCount = checked(3L * candidateTriangles), NullUnqualifiedTriangles = nullTriangles,
                    BaseComponentBounds = baseBounds.Snapshot(), LightmapComponentBounds = lightmapBounds.Snapshot(),
                    PairedCoordinatesFloat32LittleEndianSha256 = Digest(coordinateHash),
                    BaseCoordinatesFloat32LittleEndianSha256 = Digest(baseHash),
                    LightmapCoordinatesFloat32LittleEndianSha256 = Digest(lightmapHash), CandidateBindings = bindings
                });
                ordinal++;
            }
            Require(arenas.Count == 6 && materialTotal == 347 && usedTotal == 347 && candidateTotal == 260
                && candidateSourceCornerVisits == 109303,
                "Expected complete six-arena material inventory was not preserved.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or OverflowException or KeyNotFoundException)
        { error = ex.GetType().Name + ": " + ex.Message; }
        finally
        {
            try { after = HashFile(ownedSource); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { error ??= ex.GetType().Name + ": " + ex.Message; }
        }
        string assemblyAfter = HashFile(typeof(EchoesWorldGeometrySource).Assembly.Location);
        bool unchanged = before != null && before == after;
        bool passed = error == null && unchanged && assemblyBefore == assemblyAfter && arenas.Count == 6;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Passed = passed, Error = error, GameId = gameId, ArchiveSha1 = archiveSha1,
            SourceSha256Before = before, SourceSha256After = after, SourceUnchanged = unchanged,
            AssemblySha256Before = assemblyBefore, AssemblySha256After = assemblyAfter,
            AssemblyUnchanged = assemblyBefore == assemblyAfter,
            SourcePreservationOnly = true, RenderingActivated = false, SourcePayloadsWritten = false,
            CoordinateContract = EchoesWorldGeometrySource.CoordinateContract,
            MaterialCount = materialTotal, UsedMaterialCount = usedTotal, CandidateMaterialCount = candidateTotal,
            SourceCornerVisits = sourceCornerVisits, CandidateSourceCornerVisits = candidateSourceCornerVisits, TriangleCount = triangleTotal,
            CandidateTriangleCount = candidateTriangleTotal, ExpandedCandidateCornerCount = 3 * candidateTriangleTotal,
            CoordinateOrder = "triangle-order/A-B-C/baseU-baseV-lightmapU-lightmapV/float32-little-endian",
            Arenas = arenas
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    private sealed class Bounds
    {
        private float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
        private float minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
        internal void Add(float u, float v)
        {
            Require(float.IsFinite(u) && float.IsFinite(v), "Nonfinite emitted source coordinate.");
            minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
        }
        internal object Snapshot() => new { MinimumU = minU, MaximumU = maxU, MinimumV = minV, MaximumV = maxV };
    }

    private static bool SameFloatBits(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
        return true;
    }
    private static string HashFloats(float[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4];
        foreach (float value in values) { BinaryPrimitives.WriteSingleLittleEndian(bytes, value); hash.AppendData(bytes); }
        return Digest(hash);
    }
    private static string HashInts(int[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4];
        foreach (int value in values) { BinaryPrimitives.WriteInt32LittleEndian(bytes, value); hash.AppendData(bytes); }
        return Digest(hash);
    }
    private static string Digest(IncrementalHash hash) => Convert.ToHexStringLower(hash.GetHashAndReset());
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static void Require(bool pass, string detail) { if (!pass) throw new InvalidDataException(detail); }
}
