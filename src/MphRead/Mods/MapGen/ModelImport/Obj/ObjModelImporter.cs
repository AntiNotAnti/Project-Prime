using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Authoring-only OBJ reader. Runtime consumes ordinary MapMesh and baked texture assets.</summary>
public sealed class ObjModelImporter : IModelImporter
{
    public const int MaxSourceBytes = 32 * 1024 * 1024;
    private const int MaxElements = 65535;
    public bool CanImport(string extension) => extension.Equals(".obj", StringComparison.OrdinalIgnoreCase);
    public ImportedModel Import(string path, ModelImportSettings settings, CancellationToken cancellation = default)
    {
        if (!float.IsFinite(settings.Scale) || settings.Scale <= 0) throw new InvalidDataException("Model scale must be positive and finite.");
        path = Path.GetFullPath(path);
        string root = Path.GetDirectoryName(path)!;
        var vertices = new List<float[]>(); var uv = new List<float[]>(); int normals = 0;
        var meshes = new List<MapMesh>(); var materials = new List<MapMaterial>();
        var materialIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var assetSources = new Dictionary<string,string>(StringComparer.Ordinal);
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        void Warn(string message) { if (warnings.Count < 128) warnings.Add(message); }
        var libraries = new HashSet<string>(StringComparer.Ordinal);
        long sourceBytes = new FileInfo(path).Length;
        long textureBytes = 0;
        int triangleCount = 0;
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        int activeMaterial = -1; string label = Path.GetFileNameWithoutExtension(path);
        MapMesh? mesh = null; int faces = 0;
        int Material(string name)
        {
            if (name.Length is < 1 or > 128) throw new InvalidDataException("Material names require 1–128 characters.");
            if (materialIds.TryGetValue(name, out int index)) return index;
            if (materials.Count >= 256) throw new InvalidDataException("Model exceeds 256 materials.");
            index = materials.Count; materialIds.Add(name, index);
            materials.Add(new MapMaterial { Name = name, Id = Guid.NewGuid() }); return index;
        }
        foreach (string line in Lines(path, cancellation))
        {
            string[] fields = Tokens(line); if (fields.Length == 0) continue;
            switch (fields[0])
            {
                case "v":
                    Require(fields, 4); Limit(vertices.Count);
                    float x = Number(fields[1]) * settings.Scale, y = Number(fields[2]) * settings.Scale, z = Number(fields[3]) * settings.Scale;
                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new InvalidDataException("Scaled coordinates exceed finite range.");
                    vertices.Add(settings.ZUp ? new[] { x, z, -y } : new[] { x, y, z }); break;
                case "vt":
                    Require(fields, 3); Limit(uv.Count); uv.Add(new[] { Number(fields[1]) * 64, (settings.FlipUvVertical ? 1 - Number(fields[2]) : Number(fields[2])) * 64 }); break;
                case "vn": Require(fields, 4); Number(fields[1]); Number(fields[2]); Number(fields[3]); Limit(normals++); break;
                case "o": case "g":
                    label = string.Join(" ", fields.Skip(1));
                    if (label.Length > 128) throw new InvalidDataException("Object/group names require at most 128 characters.");
                    mesh = null; break;
                case "s": if (fields.Length > 1 && fields[1] is not ("off" or "0")) Warn("Smoothing groups use the runtime's flat face normals."); break;
                case "usemtl": activeMaterial = Material(string.Join(" ", fields.Skip(1))); break;
                case "mtllib":
                    foreach (string file in fields.Skip(1))
                    {
                        string mtl = Resolve(root, root, file);
                        if (!File.Exists(mtl)) { Warn("Missing material library: " + file); continue; }
                        if (!libraries.Add(mtl)) continue;
                        if (libraries.Count > 32 || (sourceBytes += new FileInfo(mtl).Length) > MaxSourceBytes)
                            throw new InvalidDataException("Model libraries exceed source budget.");
                        int current = -1; var defined = new HashSet<string>();
                        foreach (string materialLine in Lines(mtl, cancellation))
                        {
                            string[] f = Tokens(materialLine); if (f.Length == 0) continue;
                            switch (f[0])
                            {
                                case "newmtl":
                                    string name = string.Join(" ", f.Skip(1));
                                    if (!defined.Add(name)) Warn("Duplicate material: " + name);
                                    current = Material(name); break;
                                case "map_Kd" when current >= 0:
                                    string? imageName = DiffuseMapPath(f, Warn);
                                    if (imageName == null) break;
                                    string image = Resolve(root, Path.GetDirectoryName(mtl)!, imageName);
                                    if (!File.Exists(image)) { Warn("Missing texture: " + imageName); break; }
                                    if (new FileInfo(image).Length > MaxSourceBytes) throw new InvalidDataException("Texture exceeds source byte limit.");
                                    string hash = MapHash256.HashFile(image).ToString();
                                    if (!textures.TryGetValue(hash, out string? asset))
                                    {
                                        if (textures.Count >= 128) throw new InvalidDataException("Model exceeds 128 textures.");
                                        asset = "textures/model-" + hash + ".tex";
                                        byte[] imageBytes = File.ReadAllBytes(image);
                                        if ((textureBytes += imageBytes.Length) > 128 * 1024 * 1024) throw new InvalidDataException("Model textures exceed byte budget.");
                                        ValidateImage(imageBytes);
                                        assets.Add(asset, MapTextureBake.BakeImage(imageBytes, cancellation)); textures.Add(hash, asset);
                                    }
                                    assetSources.TryAdd(asset,image);
                                    materials[current].Texture = asset; break;
                                case "Kd" when current >= 0:
                                    Require(f, 4);
                                    byte[] color = Solid(Number(f[1]), Number(f[2]), Number(f[3]));
                                    string colorPath = "assets/color-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(color)).ToLowerInvariant() + ".tex";
                                    assets.TryAdd(colorPath, color); assetSources.TryAdd(colorPath,mtl); materials[current].Texture ??= colorPath; break;
                                case "d": case "Tr":
                                    Require(f, 2); if (Number(f[1]) != (f[0] == "d" ? 1 : 0)) Warn("Transparent OBJ materials are imported as opaque."); break;
                                default: Warn("Unsupported MTL property: " + f[0]); break;
                            }
                        }
                    }
                    break;
                case "f":
                    if (fields.Length is < 4 or > 33) throw new InvalidDataException("OBJ faces require 3–32 corners.");
                    Limit(faces++);
                    mesh ??= NewMesh();
                    if(activeMaterial<0)activeMaterial=Material("Default");
                    var indices = new int[fields.Length - 1]; var coords = new float[indices.Length][]; bool completeUv = true;
                    for (int i = 0; i < indices.Length; i++)
                    {
                        string[] corner = fields[i + 1].Split('/');
                        if (corner.Length > 3) throw new InvalidDataException("Invalid OBJ corner.");
                        indices[i] = Index(corner[0], vertices.Count);
                        if (corner.Length > 1 && corner[1].Length > 0) coords[i] = uv[Index(corner[1], uv.Count)];
                        else completeUv = false;
                        if (corner.Length > 2 && corner[2].Length > 0) Index(corner[2], normals);
                    }
                    if (!completeUv) Warn("Faces without complete UVs use projected mapping.");
                    if (settings.FlipWinding) { Array.Reverse(indices); Array.Reverse(coords); }
                    Vector3[] polygon=indices.Select(i => new Vector3(vertices[i][0], vertices[i][1], vertices[i][2])).ToArray();
                    if(IsNonPlanar(polygon))Warn("Non-planar OBJ polygons were triangulated automatically.");
                    foreach (int[] triangle in Triangulate(polygon))
                    {
                        Limit(triangleCount++);
                        mesh.Faces.Add(triangle.Select(i => indices[i]).ToArray()); mesh.FaceMaterials.Add(activeMaterial);
                        mesh.FaceTexcoords.Add(completeUv ? triangle.Select(i => coords[i].ToArray()).ToArray() : null);
                        if (mesh.Faces.Count > MaxElements) throw new InvalidDataException("Model exceeds runtime face budget.");
                    }
                    break;
                default: Warn("Unsupported OBJ property: " + fields[0]); break;
            }
        }
        if (meshes.Count == 0) throw new InvalidDataException("Model contains no faces.");
        if(materials.Any(material=>material.Texture==null))
        {
            byte[] fallback=Solid(1,1,1);
            string fallbackAsset="textures/model-default-"
                +Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fallback)).ToLowerInvariant()+".tex";
            assets.TryAdd(fallbackAsset,fallback);assetSources.TryAdd(fallbackAsset,path);
            foreach(var material in materials)material.Texture??=fallbackAsset;
        }
        foreach (var item in meshes)
        {
            // Remap each group to its own compact vertex table, preserving face-corner UV seams.
            var used = item.Faces.SelectMany(f => f).Distinct().ToArray();
            var remap = used.Select((value, index) => (value, index)).ToDictionary(p => p.value, p => p.index);
            item.Vertices = used.Select(i => vertices[i].ToArray()).ToList();
            item.Faces = item.Faces.Select(f => f.Select(i => remap[i]).ToArray()).ToList();
            _ = GeometryCompiler.Compile(item, 16);
        }
        return new(meshes, materials, assets, warnings.ToArray()) { AssetSources = assetSources, Dependencies = libraries.Append(path).Concat(assetSources.Values).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
        MapMesh NewMesh()
        {
            var item = new MapMesh { Label = label, Solid = settings.VisualCollision };
            meshes.Add(item); return item;
        }
    }
    private static void Limit(int count) { if (count >= MaxElements) throw new InvalidDataException("Model exceeds element budget."); }
    private static void Require(string[] fields, int count) { if (fields.Length < count) throw new InvalidDataException("Incomplete model statement."); }
    private static float Number(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number)
        ? number : throw new InvalidDataException("Invalid model number.");
    private static int Index(string value, int count)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index == 0)
            throw new InvalidDataException("Invalid OBJ index.");
        long resolved = index > 0 ? (long)index - 1 : (long)count + index;
        return resolved >= 0 && resolved < count ? (int)resolved : throw new InvalidDataException("OBJ index is out of range.");
    }
    private static string[] Tokens(string line) => line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static IEnumerable<string> Lines(string path, CancellationToken cancellation)
    {
        if (new FileInfo(path).Length > MaxSourceBytes) throw new InvalidDataException("Model source exceeds byte limit.");
        using var reader = new StreamReader(path);
        var line = new System.Text.StringBuilder(); int next;
        while ((next = reader.Read()) != -1)
        {
            cancellation.ThrowIfCancellationRequested();
            if (next == '\n') { yield return line.ToString(); line.Clear(); }
            else { if (line.Length >= 16384) throw new InvalidDataException("Model line exceeds limit."); line.Append((char)next); }
        }
        if (line.Length > 0) yield return line.ToString();
    }
    private static string SafeSourceRelativePath(string relative)
    {
        relative=relative.Trim().Trim('"').Replace('\\','/');
        if(string.IsNullOrWhiteSpace(relative)||relative.Length>240||relative.StartsWith('/')
            ||relative.Contains(':')||relative.Any(ch=>ch<32||"<>\"|?*".Contains(ch)))
            throw new InvalidDataException("Unsafe model asset path.");
        foreach(string part in relative.Split('/'))
        {
            if(part is "" or "." or ".."||part.Trim()!=part||part.EndsWith('.'))
                throw new InvalidDataException("Unsafe model asset path.");
            string stem=part.Split('.')[0].TrimEnd(' ','.').ToUpperInvariant();
            if(stem is "CON" or "PRN" or "AUX" or "NUL"
                ||stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)
                    ||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3] is >= '1' and <= '9')
                throw new InvalidDataException("Unsafe model asset path.");
        }
        return relative;
    }

    private static string Resolve(string root, string directory, string relative)
    {
        // Source dependencies are ordinary authoring filenames, not package entry
        // names. Validate traversal and Windows-unsafe forms without imposing the
        // package/runtime naming policy on legitimate OBJ/MTL texture filenames.
        relative = SafeSourceRelativePath(relative);
        if (relative.Split('/').Length > 16) throw new InvalidDataException("Model path is too deep.");
        string candidate = Path.GetFullPath(Path.Combine(directory, relative));
        foreach (string path in new[] { candidate, Path.Combine(root, relative), Path.Combine(directory, Path.GetFileName(relative)), Path.Combine(directory, "textures", Path.GetFileName(relative)), Path.Combine(root, "textures", Path.GetFileName(relative)) }.Distinct())
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Model asset escapes import root.");
            for (var part = new FileInfo(full) as FileSystemInfo; part != null && part.FullName != root; part = new DirectoryInfo(Path.GetDirectoryName(part.FullName)!))
                if (part.LinkTarget != null) throw new InvalidDataException("Model asset symlinks are not allowed.");
            if (File.Exists(full)) return full;
            string parent = Path.GetDirectoryName(full)!;
            if (Directory.Exists(parent))
            {
                var matches = Directory.EnumerateFiles(parent).Take(4096)
                    .Where(p => Path.GetFileName(p).Equals(Path.GetFileName(full), StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Ambiguous model asset filename.");
                if (matches.Length == 1)
                {
                    if (new FileInfo(matches[0]).LinkTarget != null) throw new InvalidDataException("Model asset symlinks are not allowed.");
                    return matches[0];
                }
            }
        }
        return candidate;
    }
    private static string? DiffuseMapPath(string[] fields, Action<string> warn)
    {
        int at=1;bool options=false;
        while(at<fields.Length&&fields[at].StartsWith("-",StringComparison.Ordinal))
        {
            options=true;string option=fields[at++].ToLowerInvariant();
            if(option is "-o" or "-s" or "-t")
            {
                int count=0;
                while(at<fields.Length&&count<3
                    &&float.TryParse(fields[at],NumberStyles.Float,CultureInfo.InvariantCulture,out _))
                {at++;count++;}
                if(count==0){warn("Invalid map_Kd option: "+option);return null;}
                continue;
            }
            int arguments=option switch
            {
                "-blendu" or "-blendv" or "-boost" or "-bm" or "-cc" or "-clamp"
                    or "-colorspace" or "-imfchan" or "-texres" or "-type"=>1,
                "-mm"=>2,
                _=>-1
            };
            if(arguments<0||at+arguments>fields.Length)
            {warn("Unsupported map_Kd option: "+option);return null;}
            at+=arguments;
        }
        if(at>=fields.Length){warn("map_Kd has no texture path.");return null;}
        if(options)warn("map_Kd options are ignored; the diffuse image is still imported.");
        string path=string.Join(" ",fields.Skip(at)).Trim().Trim('"');
        if(path.Length==0){warn("map_Kd has no texture path.");return null;}
        return path;
    }

    internal static void ValidateImage(byte[] bytes)
    {
        // Inspect dimensions before handing hostile input to the native decoder.
        int width = 0, height = 0;
        if (bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }))
        {
            width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
            height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        }
        else if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216)
        {
            int at = 2;
            while (at + 3 < bytes.Length)
            {
                if (bytes[at++] != 255) break;
                while (at < bytes.Length && bytes[at] == 255) at++;
                if (at >= bytes.Length) break;
                int marker = bytes[at++];
                if (marker is 216 or 217 or 218) break;
                if (at + 2 > bytes.Length) break;
                int length = (bytes[at] << 8) | bytes[at + 1];
                if (length < 2 || at + length > bytes.Length) break;
                if (marker is 192 or 193 or 194 && length >= 8)
                { height = (bytes[at + 3] << 8) | bytes[at + 4]; width = (bytes[at + 5] << 8) | bytes[at + 6]; break; }
                at += length;
            }
        }
        else if(bytes.Length>=26&&bytes[0]=='B'&&bytes[1]=='M')
        {
            width=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
            int rawHeight=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22));
            height=rawHeight==int.MinValue?0:Math.Abs(rawHeight);
        }
        else if(bytes.Length>=18&&bytes[2] is 1 or 2 or 3 or 9 or 10 or 11)
        {
            width=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12));
            height=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14));
        }
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
            throw new InvalidDataException("Model textures must be PNG, JPEG, TGA or BMP with dimensions at most 4096×4096.");
    }

    internal static byte[] Solid(float r, float g, float b)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 70, 80, 84, 88 }); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((ushort)0); writer.Write((ushort)64); writer.Write((ushort)64); writer.Write((ushort)1); writer.Write((ushort)0);
        writer.Write((ushort)((int)(Math.Clamp(r, 0, 1) * 31) | ((int)(Math.Clamp(g, 0, 1) * 31) << 5) | ((int)(Math.Clamp(b, 0, 1) * 31) << 10)));
        writer.Write(new byte[4096]); return stream.ToArray();
    }
    private static bool IsNonPlanar(Vector3[] points)
    {
        if(points.Length<4)return false;
        var normal=Vector3.Zero;
        for(int i=0;i<points.Length;i++)normal+=Vector3.Cross(points[i],points[(i+1)%points.Length]);
        if(normal.LengthSquared()<1e-12f)return false;
        normal=Vector3.Normalize(normal);
        return points.Any(point=>MathF.Abs(Vector3.Dot(point-points[0],normal))>.01f);
    }

    private static IEnumerable<int[]> Triangulate(Vector3[] points)
    {
        var normal = Vector3.Zero;
        for (int i = 0; i < points.Length; i++) normal += Vector3.Cross(points[i], points[(i + 1) % points.Length]);
        if (normal.LengthSquared() < 1e-12f) throw new InvalidDataException("Degenerate OBJ polygon.");
        normal = Vector3.Normalize(normal);
        // Ear clipping happens in the dominant 2D projection, while emitted
        // triangles retain the original 3D vertices. This intentionally accepts
        // warped quads/n-gons exported by Blender and similar tools instead of
        // forcing authors to triangulate them before import.
        int axis = Math.Abs(normal.X) > Math.Abs(normal.Y) ? 0 : 1;
        if (Math.Abs(normal.Z) > Math.Abs(axis == 0 ? normal.X : normal.Y)) axis = 2;
        var projected = points.Select(p => axis == 0 ? new Vector2(p.Y, p.Z) : axis == 1 ? new Vector2(p.Z, p.X) : new Vector2(p.X, p.Y)).ToArray();
        float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        float area = 0; for (int i = 0; i < projected.Length; i++) area += Cross(Vector2.Zero, projected[i], projected[(i+1)%projected.Length]);
        float sign = MathF.Sign(area); var remaining = Enumerable.Range(0, points.Length).ToList();
        while (remaining.Count > 3)
        {
            bool found = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int a = remaining[(i+remaining.Count-1)%remaining.Count], b = remaining[i], c = remaining[(i+1)%remaining.Count];
                if (Cross(projected[a], projected[b], projected[c]) * sign <= 1e-8f) continue;
                if (remaining.Any(j => j != a && j != b && j != c && Cross(projected[a],projected[b],projected[j])*sign >= -1e-8f
                    && Cross(projected[b],projected[c],projected[j])*sign >= -1e-8f && Cross(projected[c],projected[a],projected[j])*sign >= -1e-8f)) continue;
                yield return new[] { a,b,c }; remaining.RemoveAt(i); found = true; break;
            }
            if (!found) throw new InvalidDataException("Cannot triangulate OBJ polygon; check for self intersections.");
        }
        yield return remaining.ToArray();
    }
}
