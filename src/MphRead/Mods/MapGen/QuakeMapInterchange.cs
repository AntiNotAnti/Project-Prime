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
        }
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
        int lineNo = 0, brushIndex = 0, spawnIndex = 0;

        void CloseBrush()
        {
            if (faces.Count != 6)
            {
                Error("FP-RADIANT-BRUSH", lineNo,
                    "Native editable import supports exactly six axial planar brush faces.");
                return;
            }
            var limits = new List<float>[] { new(), new(), new() };
            string shader = faces[0].Shader;
            foreach (Face face in faces)
            {
                if (!string.Equals(face.Shader, shader, StringComparison.OrdinalIgnoreCase))
                    Error("FP-RADIANT-MATERIAL", face.Line,
                        "Different per-face shaders are not represented by a single MapBox.");
                var u = face.Points[1] - face.Points[0];
                var v = face.Points[2] - face.Points[0];
                Vector3 normal = Vector3.Cross(u, v);
                if (normal.LengthSquared() < 0.0001f)
                {
                    Error("FP-RADIANT-PLANE", face.Line, "Degenerate brush plane.");
                    continue;
                }
                normal = Vector3.Normalize(normal);
                float[] comps = { Math.Abs(normal.X), Math.Abs(normal.Y), Math.Abs(normal.Z) };
                int axis = Array.IndexOf(comps, comps.Max());
                if (comps[axis] < 0.999f)
                {
                    Error("FP-RADIANT-NONAXIAL", face.Line,
                        "Angled brushes need a lossless convex importer; no silent box conversion.");
                    continue;
                }
                limits[axis].Add(axis == 0 ? face.Points[0].X
                    : axis == 1 ? face.Points[0].Y : face.Points[0].Z);
            }
            if (diagnostics.Any(d => d.Error)) return;
            var lo = new float[3];
            var hi = new float[3];
            for (int axis = 0; axis < 3; axis++)
            {
                var distinct = limits[axis].Distinct().ToArray();
                if (distinct.Length != 2)
                {
                    Error("FP-RADIANT-BOUNDS", lineNo, "Box must have two distinct bounds on every axis.");
                    return;
                }
                lo[axis] = distinct.Min();
                hi[axis] = distinct.Max();
                if (hi[axis] - lo[axis] < 0.01f)
                {
                    Error("FP-RADIANT-BOUNDS", lineNo, "Brush thickness is too small.");
                    return;
                }
            }
            if (!materials.TryGetValue(shader, out int material))
            {
                material = definition.Materials.Count;
                materials.Add(shader, material);
                definition.Materials.Add(new MapMaterial
                {
                    Id = GuidFrom("prime-material:" + shader),
                    Name = shader, SourceMaterial = 2,
                    TexScale = 16f
                });
            }
            var center = new[] { (lo[0]+hi[0])*0.5f / unitsPerPrimeUnit,
                (lo[2]+hi[2])*0.5f / unitsPerPrimeUnit,
                -(lo[1]+hi[1])*0.5f / unitsPerPrimeUnit };
            var extent = new[] { (hi[0]-lo[0]) / unitsPerPrimeUnit,
                (hi[2]-lo[2]) / unitsPerPrimeUnit,
                (hi[1]-lo[1]) / unitsPerPrimeUnit };
            definition.Geometry.Add(new MapBox
            {
                Id = brushId != Guid.Empty ? brushId
                    : GuidFrom("prime-brush:" + mapId + ":" + brushIndex + ":"
                        + string.Join(",", lo) + ":" + string.Join(",", hi) + ":" + shader),
                Label = "Quake brush " + (brushIndex + 1),
                Material = material,
                Transform = new MapTransform { Position = center, Scale = extent }
            });
            brushIndex++;
            brushId = Guid.Empty;
            faces.Clear();
        }

        void CloseEntity()
        {
            string type = entity.GetValueOrDefault("classname") ?? "";
            if (type == "worldspawn")
            {
                foreach (string key in entity.Keys)
                    if (key is not ("classname" or "message" or "_color"))
                        Error("FP-RADIANT-WORLDSPAWN", lineNo,
                            "Unsupported worldspawn key " + key + "; preserve it in authoring or sidecar.");
            }
            else if (Q3Gameplay.IsPlayerStart(type))
            {
                if (!Q3Gameplay.TryVector(entity.GetValueOrDefault("origin"), out var xyz))
                { Error("FP-RADIANT-SPAWN", lineNo, "Spawn requires a valid Quake origin."); return; }
                foreach (string key in entity.Keys)
                    if (key is not ("classname" or "origin" or "angle" or "prime_id"))
                        Error("FP-RADIANT-ENTITY", lineNo, "Unsupported spawn key: " + key);
                float angle = 0;
                if (entity.TryGetValue("angle", out string? value) &&
                    !float.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out angle))
                    Error("FP-RADIANT-SPAWN", lineNo, "Invalid spawn heading.");
                Guid id = entity.TryGetValue("prime_id", out var idText)
                    && Guid.TryParse(idText, out Guid parsed)
                    ? parsed : GuidFrom("prime-spawn:" + mapId + ":" + spawnIndex);
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
                if (kv.Success) entity[kv.Groups["key"].Value] = kv.Groups["value"].Value;
                else Error("FP-RADIANT-ENTITY", lineNo,
                    "Unsupported entity syntax or escaped key/value.");
                continue;
            }
            if (depth == 2)
            {
                MatchCollection matches = PointPattern.Matches(line);
                if (matches.Count != 3)
                {
                    Error("FP-RADIANT-FACE", lineNo,
                        "Expected three Quake brush plane points; patches and brushDef3 unsupported.");
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
            if (geometry is not MapBox box || geometry.Hidden || !geometry.Solid
                || geometry.Damaging || geometry.FaceUv.Count != 0
                || geometry.Uv.Rotation != 0
                || geometry.Uv.Offset.Any(v => v != 0)
                || geometry.Uv.Scale.Any(v => v != 1)
                || box.Transform.Rotation.Length != 4
                || box.Transform.Rotation.Where((v, i) => v != (i == 3 ? 1 : 0)).Any())
                errors.Add(new("FP-RADIANT-EXPORT-GEOMETRY", index,
                    "Only visible, axis-aligned solid boxes with default UVs can round-trip.",
                    true));
            if (geometry.Material < 0 || geometry.Material >= map.Materials.Count)
                errors.Add(new("FP-RADIANT-EXPORT-MATERIAL", index,
                    "Missing material binding.", true));
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
        foreach (MapBox box in map.Geometry.Cast<MapBox>())
        {
            string shader = map.Materials[box.Material].Name;
            if (string.IsNullOrWhiteSpace(shader)
                || shader.Any(char.IsWhiteSpace)
                || shader.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("Shader name is not portable to Quake .map.");
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
            sb.AppendLine("{");
            sb.AppendLine("// PRIME_ID=" + box.Id);
            void Plane(int a, int b, int c, Vector3 expected)
            {
                Vector3 first = corner[a], second = corner[b], third = corner[c];
                if (Vector3.Dot(Vector3.Cross(second-first, third-first), expected) < 0)
                    (second, third) = (third, second);
                static string P(Vector3 p) => "( " + p.X.ToString("G9", CultureInfo.InvariantCulture)
                    + " " + p.Y.ToString("G9", CultureInfo.InvariantCulture)
                    + " " + p.Z.ToString("G9", CultureInfo.InvariantCulture) + " )";
                sb.AppendLine(P(first) + " " + P(second) + " " + P(third)
                    + " " + shader + " 0 0 0 1 1 0 0 0");
            }
            Plane(0,2,6,-Vector3.UnitX);
            Plane(1,5,7,Vector3.UnitX);
            Plane(0,4,5,-Vector3.UnitY);
            Plane(2,3,7,Vector3.UnitY);
            Plane(0,1,3,-Vector3.UnitZ);
            Plane(4,6,7,Vector3.UnitZ);
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
        AtomicFile.Write(full, Encoding.UTF8.GetBytes(sb.ToString()));
        string sidecar = sidecarPath ?? Path.Combine(
            Path.GetDirectoryName(full)!, "project-prime.json");
        AtomicFile.Write(sidecar, JsonSerializer.SerializeToUtf8Bytes(
            new QuakeMapSidecar(1, map.MapId, map.Name, unitsPerPrimeUnit,
                MapBuildFingerprint.HashFile(full)), MapPackageReader.JsonOptions));
    }

    private static Guid GuidFrom(string key)
        => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
