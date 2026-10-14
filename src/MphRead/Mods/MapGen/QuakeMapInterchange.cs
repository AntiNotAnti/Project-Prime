using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MphRead.Mods.MapGen;

public sealed record QuakeMapDiagnostic(string Code, int Line, string Message, bool Error);
public sealed record QuakeMapImportResult(
    MapDefinition Definition, QuakeMapDiagnostic[] Diagnostics);
public sealed record QuakeMapSidecar(
    int SchemaVersion, Guid MapId, string RoomName, float UnitsPerPrimeUnit,
    string SourceSha256);

public sealed class QuakeMapInterchangeException(
    QuakeMapDiagnostic[] diagnostics) : InvalidDataException(
        string.Join("; ", diagnostics.Select(x => x.Code + " line "
            + x.Line + ": " + x.Message)))
{
    public QuakeMapDiagnostic[] Diagnostics { get; } = diagnostics;
}

/// <summary>
/// Direct, editable Quake3 .map subset. Exactly six axial brush planes with
/// default projection become native MapBox geometry. Spawns are normalized
/// through the existing Q3->Prime coordinate convention (x,z,-y) and stable
/// prime_id properties/comments. Unsupported patches, rotation, brushes,
/// entities and UVs fail loudly; they never silently change gameplay.
/// </summary>
public static class QuakeMapInterchange
{
    public const float DefaultUnits = 16f;
    private static readonly Regex PointPattern = new(
        @"\(\s*(?<x>[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s+(?<y>[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s+(?<z>[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EntityPattern = new(
        "^\"(?<key>[^\"]+)\"\\s+\"(?<value>[^\"]*)\"$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Face(Vector3[] Points, string Shader, int Line);

    public static QuakeMapImportResult Import(string source, string? sidecarPath = null,
        string? roomName = null, float unitsPerPrimeUnit = DefaultUnits)
    {
        if (!float.IsFinite(unitsPerPrimeUnit)
            || unitsPerPrimeUnit is < 1f or > 1024f)
            throw new ArgumentOutOfRangeException(nameof(unitsPerPrimeUnit));
        string full = Path.GetFullPath(source);
        if (!File.Exists(full) || Path.GetExtension(full).ToLowerInvariant() != ".map"
            || new FileInfo(full).Length is <= 0 or > 32L * 1024 * 1024)
            throw new InvalidDataException("Input must be a bounded editable Quake .map file.");

        var diagnostics = new List<QuakeMapDiagnostic>();
        void Error(string code, int line, string message)
            => diagnostics.Add(new(code, line, message, true));
        string inferred = roomName ?? Path.GetFileNameWithoutExtension(full).ToUpperInvariant();
        QuakeMapSidecar? sidecar = null;
        if (sidecarPath != null && File.Exists(sidecarPath))
        {
            if (new FileInfo(sidecarPath).Length > 65536)
                throw new InvalidDataException("Quake project sidecar metadata is oversized.");
            sidecar = JsonSerializer.Deserialize<QuakeMapSidecar>(
                File.ReadAllBytes(sidecarPath), MapPackageReader.JsonOptions);
            if (sidecar?.SchemaVersion != 1 || sidecar.MapId == Guid.Empty)
                throw new InvalidDataException("Unsupported or invalid map identity sidecar.");
            inferred = sidecar.RoomName;
            unitsPerPrimeUnit = sidecar.UnitsPerPrimeUnit;
            if (sidecar.SourceSha256 is not { Length: 64 }
                || sidecar.SourceSha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Identity sidecar must pin a valid SHA-256 source fingerprint.");
        }
        // Sidecars are untrusted input. Revalidate after applying their scale:
        // otherwise zero, NaN or infinity can enter gameplay coordinates.
        if (!float.IsFinite(unitsPerPrimeUnit) || unitsPerPrimeUnit is < 1f or > 1024f)
            throw new InvalidDataException("Quake map sidecar scale must be finite and between 1 and 1024.");
        MapValidator.RequireRuntimeName(inferred);
        Guid mapId = sidecar?.MapId ?? GuidFrom("prime-map:" + inferred);
        string sourceHash = MapBuildFingerprint.HashFile(full);
        if (sidecar != null && sidecar.SourceSha256 != sourceHash)
            diagnostics.Add(new("FP-RADIANT-SOURCE-CHANGED", 0,
                "The source .map changed after the sidecar was saved; check entity/brush IDs.",
                false));

        var definition = new MapDefinition
        {
            FormatVersion = 2, MapId = mapId,
            Name = inferred, InGameName = inferred,
            Version = "1", BaseDirectory = Path.GetDirectoryName(full)
        };
        var materials = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var faces = new List<Face>();
        Guid brushId = Guid.Empty;
        int depth = 0;
        int lineNo = 0, brushIndex = 0, spawnIndex = 0, worldspawnCount = 0;
        var seenBrushIds = new HashSet<Guid>();
        var seenSpawnIds = new HashSet<Guid>();

        void CloseBrush()
        {
            if (faces.Count is < 4 or > 32)
            {
                Error("FP-RADIANT-BRUSH", lineNo,
                    "Editable Quake convex brushes require between 4 and 32 complete planes.");
                faces.Clear();
                brushId = Guid.Empty;
                return;
            }
            string shader = faces[0].Shader;
            foreach (Face face in faces)
                if (!string.Equals(face.Shader, shader, StringComparison.OrdinalIgnoreCase))
                    Error("FP-RADIANT-MATERIAL", face.Line,
                        "Per-face Quake materials require a lossless material/UV importer.");
            if (diagnostics.Any(d => d.Error)) return;

            MapGeometry geometry;
            try
            {
                geometry = QuakeConvexHull.FromQuakePlanes(
                    faces.Select(f => f.Points).ToArray(), unitsPerPrimeUnit);
            }
            catch (InvalidOperationException ex)
            {
                Error("FP-RADIANT-CONVEX", faces[0].Line, ex.Message);
                return;
            }
            if (!materials.TryGetValue(shader, out int material))
            {
                material = definition.Materials.Count;
                materials.Add(shader, material);
                definition.Materials.Add(new MapMaterial
                {
                    Id = GuidFrom("prime-material:" + shader),
                    Name = shader, SourceMaterial = 2, TexScale = 16f
                });
            }
            Guid id = brushId != Guid.Empty ? brushId
                : GuidFrom("prime-brush:" + mapId + ":" + brushIndex + ":"
                    + string.Join("|", faces.Select(f =>
                        string.Join(",", f.Points.SelectMany(p => new[] { p.X, p.Y, p.Z })
                            .Select(v => v.ToString("R", CultureInfo.InvariantCulture))))));
            if (!seenBrushIds.Add(id))
            {
                Error("FP-RADIANT-ID", lineNo,
                    "Duplicate brush identity; map authoring IDs must be unique.");
                return;
            }
            geometry.Id = id;
            geometry.Label = "Quake brush " + (brushIndex + 1);
            geometry.Material = material;
            // A solved hull still needs to satisfy Prime's native face and UV
            // budgets before it becomes an editable authoring document.
            try { GeometryCompiler.Compile(geometry, definition.Materials[material].TexScale); }
            catch (MapAuthoringException ex)
            {
                Error(ex.Code, lineNo, ex.Message);
                return;
            }
            definition.Geometry.Add(geometry);
            brushIndex++;
            brushId = Guid.Empty;
            faces.Clear();
        }

        void CloseEntity()
        {
            string type = entity.GetValueOrDefault("classname") ?? "";
            if (type.Equals("worldspawn", StringComparison.OrdinalIgnoreCase))
            {
                worldspawnCount++;
                if (worldspawnCount != 1)
                    Error("FP-RADIANT-WORLDSPAWN", lineNo, "Exactly one worldspawn is required.");
                // The native MapDefinition currently has no source-key metadata.
                // Even apparently cosmetic keys cannot be discarded silently.
                foreach (string key in entity.Keys)
                    if (!key.Equals("classname", StringComparison.OrdinalIgnoreCase))
                        Error("FP-RADIANT-WORLDSPAWN", lineNo,
                            "Unrepresented worldspawn key " + key + " requires explicit preservation.");
            }
            else if (Q3Gameplay.IsPlayerStart(type))
            {
                if (!Q3Gameplay.TryVector(entity.GetValueOrDefault("origin"), out var xyz))
                { Error("FP-RADIANT-SPAWN", lineNo, "Spawn requires a valid Quake origin."); return; }
                foreach (string key in entity.Keys)
                    if (key is not ("classname" or "origin" or "angle" or "prime_id"))
                        Error("FP-RADIANT-ENTITY", lineNo, "Unsupported spawn key: " + key);
                float angle = 0;
                if (entity.TryGetValue("angle", out string? value)
                    && (!float.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out angle) || !float.IsFinite(angle)))
                    Error("FP-RADIANT-SPAWN", lineNo, "Invalid spawn heading.");
                Guid id;
                if (entity.TryGetValue("prime_id", out var idText))
                {
                    if (!Guid.TryParse(idText, out id) || id == Guid.Empty)
                    {
                        Error("FP-RADIANT-ID", lineNo, "Spawn prime_id must be a nonempty GUID.");
                        return;
                    }
                }
                else id = GuidFrom("prime-spawn:" + mapId + ":" + spawnIndex);
                if (!seenSpawnIds.Add(id))
                {
                    Error("FP-RADIANT-ID", lineNo, "Duplicate spawn prime_id is not allowed.");
                    return;
                }
                definition.Spawns.Add(new MapSpawn
                {
                    Id = id, Position = new[]
                    {
                        xyz[0] / unitsPerPrimeUnit,
                        (xyz[2] - 24f) / unitsPerPrimeUnit,
                        -xyz[1] / unitsPerPrimeUnit
                    }, Yaw = (90f + angle % 360f + 360f) % 360f
                });
                spawnIndex++;
            }
            else
                Error("FP-RADIANT-ENTITY", lineNo,
                    "Unsupported Quake entity: " + type + ". No gameplay entity was silently dropped.");
            entity.Clear();
        }

        foreach (string raw in File.ReadLines(full))
        {
            lineNo++;
            if (lineNo > 200000)
                throw new InvalidDataException("Quake map contains too many lines.");
            string line = raw.Trim();
            if (line.StartsWith("// PRIME_ID=", StringComparison.OrdinalIgnoreCase)
                && depth == 2)
            {
                if (!Guid.TryParse(line[12..], out brushId))
                    Error("FP-RADIANT-ID", lineNo, "Invalid brush identity marker.");
                continue;
            }
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line == "{")
            {
                depth++;
                if (depth > 2)
                {
                    Error("FP-RADIANT-PATCH", lineNo,
                        "Patch/brushDef nested geometry requires explicit import support.");
                    break;
                }
                if (depth == 2 && !entity.GetValueOrDefault("classname", "").Equals(
                    "worldspawn", StringComparison.OrdinalIgnoreCase))
                    Error("FP-RADIANT-BRUSH-ENTITY", lineNo,
                        "Only worldspawn brush geometry is supported by direct editable import.");
                continue;
            }
            if (line == "}")
            {
                if (depth == 2) CloseBrush();
                else if (depth == 1) CloseEntity();
                else Error("FP-RADIANT-BRACE", lineNo, "Unexpected closing brace.");
                depth--;
                continue;
            }
            if (depth == 1)
            {
                Match kv = EntityPattern.Match(line);
                if (kv.Success)
                {
                    if (!entity.TryAdd(kv.Groups["key"].Value, kv.Groups["value"].Value))
                        Error("FP-RADIANT-ENTITY", lineNo,
                            "Duplicate entity key; silently replacing metadata is unsafe.");
                }
                else Error("FP-RADIANT-ENTITY", lineNo,
                    "Unsupported entity syntax or escaped key/value.");
                continue;
            }
            if (depth == 2)
            {
                MatchCollection matches = PointPattern.Matches(line);
                if (matches.Count != 3
                    || (matches.Count == 3 && (matches[0].Index != 0
                        || !string.IsNullOrWhiteSpace(line[
                            (matches[0].Index + matches[0].Length)..matches[1].Index])
                        || !string.IsNullOrWhiteSpace(line[
                            (matches[1].Index + matches[1].Length)..matches[2].Index]))))
                {
                    Error("FP-RADIANT-FACE", lineNo,
                        "Expected exactly three Quake plane points without unsupported prefixes.");
                    continue;
                }
                var points = new Vector3[3];
                for (int i = 0; i < 3; i++)
                {
                    Match m = matches[i];
                    points[i] = new Vector3(
                        float.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture),
                        float.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                        float.Parse(m.Groups["z"].Value, CultureInfo.InvariantCulture));
                }
                if (points.Any(p => !float.IsFinite(p.X)
                    || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
                {
                    Error("FP-RADIANT-PLANE", lineNo, "Nonfinite brush plane coordinate.");
                    continue;
                }
                string tail = line[(matches[2].Index + matches[2].Length)..].Trim();
                string[] args = tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length < 6 || args.Length > 9 || args[0].Contains(".."))
                {
                    Error("FP-RADIANT-UV", lineNo, "Unsupported surface shader/UV syntax.");
                    continue;
                }
                bool uvDefault = args.Skip(1).Take(5)
                    .Select(v => float.TryParse(v, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float x) ? x : float.NaN)
                    .SequenceEqual(new float[] { 0, 0, 0, 1, 1 });
                bool flagsDefault = args.Skip(6).All(s => s == "0");
                if (!uvDefault || !flagsDefault)
                {
                    Error("FP-RADIANT-UV", lineNo,
                        "Nondefault UV projection/surface flags cannot be represented losslessly yet.");
                    continue;
                }
                faces.Add(new Face(points, args[0], lineNo));
                continue;
            }
            Error("FP-RADIANT-SYNTAX", lineNo,
                "Unsupported text outside entities or brush geometry.");
        }
        if (depth != 0)
            Error("FP-RADIANT-BRACE", lineNo, "Unclosed Quake map entity or brush.");
        if (worldspawnCount != 1)
            Error("FP-RADIANT-WORLDSPAWN", lineNo, "Expected exactly one worldspawn.");
        if (MapBuildFingerprint.HashFile(full) != sourceHash)
            Error("FP-RADIANT-SOURCE-RACE", lineNo,
                "The .map source changed while being read; retry from a frozen source.");
        if (!definition.Geometry.Any())
            Error("FP-RADIANT-GEOMETRY", lineNo, "No supported editable world brushes found.");
        if (diagnostics.Any(d => d.Error))
            throw new QuakeMapInterchangeException(diagnostics.ToArray());
        return new QuakeMapImportResult(definition, diagnostics.ToArray());
    }

    public static void Export(MapDefinition map, string output, string? sidecarPath = null,
        float unitsPerPrimeUnit = DefaultUnits)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!float.IsFinite(unitsPerPrimeUnit) || unitsPerPrimeUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerPrimeUnit));
        MapValidator.RequireRuntimeName(map.Name);
        if (map.MapId == Guid.Empty)
            throw new InvalidDataException("Cannot export an identity-less map; assign a MapId first.");
        var errors = new List<QuakeMapDiagnostic>();
        if (map.Import != null || map.NativeRoom != null || map.Collision != null
            || map.Brushes.Count > 0 || map.Items.Count > 0 || map.JumpPads.Count > 0
            || map.Q3Triggers?.Count > 0 || map.NavigationLinks.Count > 0)
            errors.Add(new("FP-RADIANT-EXPORT", 0,
                "Native imported, custom collision and advanced gameplay entities need a lossless exporter.",
                true));

        int index = 0;
        foreach (var geometry in map.Geometry)
        {
            if (geometry is not (MapBox or MapConvexBrush)
                || geometry.Hidden || !geometry.Solid || geometry.Damaging
                || geometry.FaceUv.Count != 0
                || geometry.Uv.Rotation != 0
                || geometry.Uv.Offset.Any(v => v != 0)
                || geometry.Uv.Scale.Any(v => v != 1)
                || geometry.Transform.Rotation.Length != 4
                || geometry.Transform.Rotation.Where((v, i) => v != (i == 3 ? 1 : 0)).Any()
                || (geometry is MapConvexBrush
                    && (geometry.Transform.Position.Any(v => v != 0)
                        || geometry.Transform.Scale.Any(v => v != 1))))
                errors.Add(new("FP-RADIANT-EXPORT-GEOMETRY", index,
                    "Only solid boxes and untransformed native convex brushes with default UVs can round-trip.",
                    true));
            if (geometry.Material < 0 || geometry.Material >= map.Materials.Count)
                errors.Add(new("FP-RADIANT-EXPORT-MATERIAL", index,
                    "Missing material binding.", true));
            else if (geometry is MapConvexBrush)
            {
                try { GeometryCompiler.Compile(geometry, map.Materials[geometry.Material].TexScale); }
                catch (MapAuthoringException ex)
                {
                    errors.Add(new("FP-RADIANT-EXPORT-CONVEX", index, ex.Message, true));
                }
            }
            index++;
        }
        if (map.Spawns.Any(s => s.Team != -1
            || s.Position.Length != 3 || !float.IsFinite(s.Yaw)))
            errors.Add(new("FP-RADIANT-EXPORT-SPAWN", 0,
                "Only neutral fixed-location spawn entities can round-trip.", true));
        if (errors.Count != 0) throw new QuakeMapInterchangeException(errors.ToArray());

        var sb = new StringBuilder();
        sb.AppendLine("// Project Prime original Quake3 .map brush authoring source");
        sb.AppendLine("{");
        sb.AppendLine("\"classname\" \"worldspawn\"");
        foreach (MapGeometry geometry in map.Geometry)
        {
            string shader = map.Materials[geometry.Material].Name;
            if (string.IsNullOrWhiteSpace(shader)
                || shader.Any(char.IsWhiteSpace)
                || shader.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("Shader name is not portable to Quake .map.");
            sb.AppendLine("{");
            sb.AppendLine("// PRIME_ID=" + geometry.Id);
            static string P(Vector3 p) => "( " + p.X.ToString("G9", CultureInfo.InvariantCulture)
                + " " + p.Y.ToString("G9", CultureInfo.InvariantCulture)
                + " " + p.Z.ToString("G9", CultureInfo.InvariantCulture) + " )";
            void Plane(Vector3 first, Vector3 second, Vector3 third, Vector3 expected)
            {
                if (Vector3.Dot(Vector3.Cross(second - first, third - first), expected) < 0)
                    (second, third) = (third, second);
                sb.AppendLine(P(first) + " " + P(second) + " " + P(third)
                    + " " + shader + " 0 0 0 1 1 0 0 0");
            }
            if (geometry is MapBox box)
            {
                float[] p = box.Transform.Position, s = box.Transform.Scale;
                if (p.Length != 3 || s.Length != 3 || s.Any(x => !float.IsFinite(x) || x <= 0)
                    || p.Any(x => !float.IsFinite(x)))
                    throw new InvalidDataException("Invalid authored box transform.");

                float minX = (p[0] - s[0] / 2) * unitsPerPrimeUnit;
                float maxX = (p[0] + s[0] / 2) * unitsPerPrimeUnit;
                float minY = (-p[2] - s[2] / 2) * unitsPerPrimeUnit;
                float maxY = (-p[2] + s[2] / 2) * unitsPerPrimeUnit;
                float minZ = (p[1] - s[1] / 2) * unitsPerPrimeUnit;
                float maxZ = (p[1] + s[1] / 2) * unitsPerPrimeUnit;
                Vector3[] corner =
                {
                    new(minX,minY,minZ), new(maxX,minY,minZ),
                    new(minX,maxY,minZ), new(maxX,maxY,minZ),
                    new(minX,minY,maxZ), new(maxX,minY,maxZ),
                    new(minX,maxY,maxZ), new(maxX,maxY,maxZ)
                };
                Plane(corner[0], corner[2], corner[6], -Vector3.UnitX);
                Plane(corner[1], corner[5], corner[7], Vector3.UnitX);
                Plane(corner[0], corner[4], corner[5], -Vector3.UnitY);
                Plane(corner[2], corner[3], corner[7], Vector3.UnitY);
                Plane(corner[0], corner[1], corner[3], -Vector3.UnitZ);
                Plane(corner[4], corner[6], corner[7], Vector3.UnitZ);
            }
            else if (geometry is MapConvexBrush brush)
            {
                Vector3[] primeVertices = brush.Vertices.Select(v =>
                    new Vector3(v[0], v[1], v[2])).ToArray();
                Vector3 centroid = primeVertices.Aggregate(Vector3.Zero,
                    (sum, point) => sum + point) / primeVertices.Length;
                static Vector3 Q(Vector3 prime, float units)
                    => new(prime.X * units, -prime.Z * units, prime.Y * units);
                Vector3 quakeCenter = Q(centroid, unitsPerPrimeUnit);
                foreach (int[] face in brush.Faces)
                {
                    Vector3 first = Q(primeVertices[face[0]], unitsPerPrimeUnit);
                    Vector3 second = Q(primeVertices[face[1]], unitsPerPrimeUnit);
                    Vector3 third = Q(primeVertices[face[2]], unitsPerPrimeUnit);
                    Plane(first, second, third, first - quakeCenter);
                }
            }
            sb.AppendLine("}");
        }
        sb.AppendLine("}");

        foreach (MapSpawn spawn in map.Spawns)
        {
            float x = spawn.Position[0] * unitsPerPrimeUnit;
            float y = -spawn.Position[2] * unitsPerPrimeUnit;
            float z = spawn.Position[1] * unitsPerPrimeUnit + 24f;
            float angle = (spawn.Yaw - 90f + 360f) % 360f;
            string Flo(float n) => n.ToString("G9", CultureInfo.InvariantCulture);
            sb.AppendLine("{");
            sb.AppendLine("\"classname\" \"info_player_deathmatch\"");
            sb.AppendLine("\"origin\" \"" + Flo(x) + " " + Flo(y)
                + " " + Flo(z) + "\"");
            sb.AppendLine("\"angle\" \"" + Flo(angle) + "\"");
            sb.AppendLine("\"prime_id\" \"" + spawn.Id + "\"");
            sb.AppendLine("}");
        }
        string full = Path.GetFullPath(output);
        if (!Path.GetExtension(full).Equals(".map", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Quake source exports require a .map destination.");
        string sidecar = Path.GetFullPath(sidecarPath ?? Path.Combine(
            Path.GetDirectoryName(full)!, "project-prime.json"));
        if (string.Equals(full, sidecar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Quake source and identity sidecar must be distinct files.");
        if (File.Exists(sidecar))
        {
            QuakeMapSidecar? previous;
            try
            {
                if (new FileInfo(sidecar).Length > 65536)
                    throw new InvalidDataException("Existing Quake sidecar is oversized.");
                previous = JsonSerializer.Deserialize<QuakeMapSidecar>(
                    File.ReadAllBytes(sidecar), MapPackageReader.JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Refusing to overwrite an unrelated identity sidecar.", ex);
            }
            if (previous?.MapId != map.MapId || previous.SchemaVersion != 1)
                throw new InvalidDataException(
                    "Refusing to overwrite a sidecar belonging to a different map identity.");
        }
        // Refuse identity conflicts before modifying either source or sidecar.
        AtomicFile.Write(full, Encoding.UTF8.GetBytes(sb.ToString()));
        AtomicFile.Write(sidecar, JsonSerializer.SerializeToUtf8Bytes(
            new QuakeMapSidecar(1, map.MapId, map.Name, unitsPerPrimeUnit,
                MapBuildFingerprint.HashFile(full)), MapPackageReader.JsonOptions));
    }

    private static Guid GuidFrom(string key)
        => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
