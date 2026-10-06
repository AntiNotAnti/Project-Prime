using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public static partial class MapPrefabService
{
    public sealed record InstanceResult(Guid InstanceId, IReadOnlyList<Guid> ObjectIds,
        IReadOnlyList<string> GeneratedAssets, int PreservedOverrides);
    internal static readonly JsonSerializerOptions PrefabJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const int MaxMembers = 4096, MaxMaterials = 512;
    private static Guid StableSourceId(Guid sourceId, string kind, int index)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId.ToString("N") + "|" + kind + "|" + index)).AsSpan(0, 16));

    public static InstanceResult InsertLinked(MapDefinition destination, string prefabPath, string destinationRoot,
        MapTransform? transform = null)
        => InsertLinkedPrepared(destination, prefabPath, destinationRoot, transform, "prefab-assets");

    internal static InstanceResult InsertLinkedPrepared(MapDefinition destination, string prefabPath, string destinationRoot,
        MapTransform? transform, string assetPrefix)
    {
        var source = LoadSource(prefabPath);
        if (destination.PrefabInstances.Count >= 1024) throw new InvalidDataException("Map has too many prefab instances.");
        var candidate = MapSnapshotCopy.Copy(destination);
        var instance = new MapPrefabInstance { SourceId = source.PrefabSource!.Id, SourcePath = Path.GetFullPath(prefabPath),
            Transform = MapSnapshotCopy.Copy(transform ?? new MapTransform()) };
        candidate.PrefabInstances.Add(instance);
        return ApplySource(destination, candidate, source, instance, destinationRoot, assetPrefix);
    }

    public static InstanceResult Update(MapDefinition destination, Guid instanceId, string destinationRoot)
        => UpdatePrepared(destination, instanceId, destinationRoot, "prefab-assets");

    internal static InstanceResult UpdatePrepared(MapDefinition destination, Guid instanceId, string destinationRoot, string assetPrefix)
    {
        var candidate = MapSnapshotCopy.Copy(destination);
        var instance = RequireInstance(candidate, instanceId);
        CaptureOverrides(candidate, instance);
        var source = LoadSource(ResolveSource(destination, instance.SourcePath));
        if (source.PrefabSource!.Id != instance.SourceId) throw new InvalidDataException("Prefab source identity changed. Insert it as a new instance.");
        if (instance.SourceRevision == Revision(source))
        {
            CommitDefinition(destination, candidate);
            return new(instance.Id, instance.Members.Where(m => !m.Deleted).Select(m => m.ObjectId).ToArray(), Array.Empty<string>(),
                instance.Members.Count(m => m.Overrides != null) + instance.Materials.Count(m => m.Overrides != null));
        }
        return ApplySource(destination, candidate, source, instance, destinationRoot, assetPrefix);
    }

    public static void Detach(MapDefinition destination, Guid instanceId)
    {
        var instance = RequireInstance(destination, instanceId);
        destination.PrefabInstances.Remove(instance);
    }

    public static void SetTransform(MapDefinition destination, Guid instanceId, MapTransform transform)
    {
        var candidate = MapSnapshotCopy.Copy(destination);
        var instance = RequireInstance(candidate, instanceId);
        CaptureOverrides(candidate, instance);
        Matrix4x4 oldMatrix = Matrix(instance.Transform), nextMatrix = Matrix(transform);
        if (!Matrix4x4.Invert(oldMatrix, out var inverse)) throw new InvalidDataException("Prefab transform is not invertible.");
        foreach (var member in instance.Members)
        {
            object baseline = Decode(member.Kind, member.Baseline);
            TransformObject(baseline, inverse * nextMatrix);
            member.Baseline = Encode(baseline);
            var live = MapObjects.Find(candidate, member.ObjectId);
            if (live != null) { object value = MapSnapshotCopy.Copy(live.Value); TransformObject(value, inverse * nextMatrix); ReplaceObject(candidate, member.ObjectId, value); }
            // Recompute patches in the new coordinate frame, preserving ordinary object edits.
            live = MapObjects.Find(candidate, member.ObjectId);
            member.Overrides = live == null ? member.Overrides : Patch(member.Baseline, Encode(live.Value));
        }
        instance.Transform = MapSnapshotCopy.Copy(transform);
        CommitDefinition(destination, candidate);
    }

    /// <summary>Compile/package input: no source file or authoring provenance is needed to resolve the map.</summary>
    public static MapDefinition Flatten(MapDefinition source)
    {
        var result = MapSnapshotCopy.Copy(source); result.PrefabSource = null; result.PrefabInstances.Clear(); return result;
    }

    internal static string Revision(MapDefinition source)
        => Revision(source, null);

    private static string Revision(MapDefinition source, IReadOnlyDictionary<string, byte[]>? capturedAssets)
    {
        var definition = Flatten(source);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(MapProjectFolder.Serialize(definition)));
        foreach (string path in definition.Materials.SelectMany(Channels).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        { hash.AppendData(Encoding.UTF8.GetBytes(path)); hash.AppendData(capturedAssets == null ? MapAssets.Read(source, path) : capturedAssets[path]); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static MapDefinition LoadSource(string path)
    {
        var source = MapDefinition.Load(path);
        var objects = MapObjects.All(source).ToArray();
        if (objects.Length == 0 || objects.Length > MaxMembers || source.Materials.Count > MaxMaterials)
            throw new InvalidDataException("Prefab requires 1–4096 objects and at most 512 materials.");
        if (source.PrefabSource == null || source.PrefabSource.Id == Guid.Empty)
            throw new InvalidDataException("Save this selection as a prefab first to assign its stable source identity.");
        if (objects.Any(o => o.Id == Guid.Empty) || objects.Select(o => o.Id).Distinct().Count() != objects.Length
            || source.Materials.Any(m => m.Id == Guid.Empty) || source.Materials.Select(m => m.Id).Distinct().Count() != source.Materials.Count)
            throw new InvalidDataException("Prefab objects and materials require unique stable IDs.");
        return source;
    }

    private static InstanceResult ApplySource(MapDefinition destination, MapDefinition candidate, MapDefinition source,
        MapPrefabInstance instance, string destinationRoot, string assetPrefix)
    {
        Matrix4x4 transform = Matrix(instance.Transform);
        string root = Path.GetFullPath(destinationRoot);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sourceAssets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long capturedAssetBytes = 0;
        var materialSlots = new Dictionary<int, int>();
        int preserved = 0;
        foreach (var pair in source.Materials.Select((material, index) => (material, index)))
        {
            var material = MapSnapshotCopy.Copy(pair.material);
            var binding = instance.Materials.SingleOrDefault(x => x.SourceMaterialId == material.Id);
            if (binding == null) { binding = new() { SourceMaterialId = material.Id, MaterialId = Guid.NewGuid() }; instance.Materials.Add(binding); }
            material.Id = binding.MaterialId;
            void Copy(string? original, Action<string> set)
            {
                if (string.IsNullOrEmpty(original)) return;
                if (!sourceAssets.TryGetValue(original, out byte[]? bytes))
                {
                    bytes = MapAssets.Read(source, original);
                    capturedAssetBytes += bytes.LongLength;
                    if (capturedAssetBytes > 128L * 1024 * 1024) throw new InvalidDataException("Prefab assets exceed the 128 MiB preparation budget.");
                    sourceAssets[original] = bytes;
                }
                string relative = assetPrefix + "/" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + Path.GetExtension(original).ToLowerInvariant();
                assets[relative] = bytes; set(relative);
                if (!candidate.Assets.Any(a => a.Path == relative)) candidate.Assets.Add(new() { Path = relative, Kind = "texture", Name = material.Name });
            }
            Copy(material.Texture, x => material.Texture = x); Copy(material.Albedo, x => material.Albedo = x);
            Copy(material.Normal, x => material.Normal = x); Copy(material.SpecularRoughness, x => material.SpecularRoughness = x);
            Copy(material.Emissive, x => material.Emissive = x);
            if (material.Animation != null)
                for (int i = 0; i < material.Animation.FlipbookFrames.Count; i++) { int frame = i; Copy(material.Animation.FlipbookFrames[i], x => material.Animation.FlipbookFrames[frame] = x); }
            binding.Baseline = Encode(material);
            if (binding.Overrides != null) { material = (MapMaterial)Merge(typeof(MapMaterial), binding.Baseline, binding.Overrides); preserved++; }
            int slot = candidate.Materials.FindIndex(m => m.Id == binding.MaterialId);
            if (slot < 0) { slot = candidate.Materials.Count; candidate.Materials.Add(material); } else candidate.Materials[slot] = material;
            materialSlots[pair.index] = slot;
        }
        var sources = MapObjects.All(source).ToDictionary(o => o.Id);
        foreach (var old in instance.Members.Where(m => !sources.ContainsKey(m.SourceObjectId)).ToArray())
        {
            // A locally modified removed source object becomes independent; unchanged ones follow source deletion.
            if (old.Overrides == null) RemoveObject(candidate, old.ObjectId);
            instance.Members.Remove(old);
        }
        foreach (var sourceObject in sources.Values)
        {
            object value = MapSnapshotCopy.Copy(sourceObject.Value);
            var member = instance.Members.SingleOrDefault(x => x.SourceObjectId == sourceObject.Id);
            if (member == null) { member = new() { SourceObjectId = sourceObject.Id, ObjectId = Guid.NewGuid() }; instance.Members.Add(member); }
            SetObjectId(value, member.ObjectId);
            int Slot(int sourceSlot) => materialSlots.TryGetValue(sourceSlot, out int slot) ? slot : throw new InvalidDataException("Prefab material slot is invalid.");
            if (value is MapGeometry geometry) { geometry.Material = Slot(geometry.Material); if (geometry is MapMesh mesh) mesh.FaceMaterials = mesh.FaceMaterials.Select(Slot).ToList(); }
            if (value is MapBrush brush) brush.Material = Slot(brush.Material);
            TransformObject(value, transform);
            string kind = value.GetType().Name;
            if (member.Kind.Length != 0 && member.Kind != kind && member.Overrides != null)
                throw new InvalidDataException("Prefab changed an overridden object's type. Detach it before updating.");
            member.Kind = kind; member.Baseline = Encode(value);
            if (member.Overrides != null) { value = Merge(value.GetType(), member.Baseline, member.Overrides); preserved++; }
            RemoveObject(candidate, member.ObjectId); if (!member.Deleted) AddObject(candidate, value);
        }
        instance.SourceRevision = Revision(source, sourceAssets);
        if (instance.Members.Sum(m => m.Baseline.Length + (m.Overrides?.Length ?? 0)) > 16 * 1024 * 1024
            || assets.Sum(a => a.Value.LongLength) > 128L * 1024 * 1024) throw new InvalidDataException("Prefab instance exceeds its bounded authoring/asset budget.");
        var generated = new List<string>();
        try
        {
            foreach (var asset in assets)
            {
                string full = Path.Combine(root, asset.Key);
                EnsurePrivateAssetPath(root, full);
                if (File.Exists(full)) { if (!File.ReadAllBytes(full).AsSpan().SequenceEqual(asset.Value)) throw new InvalidDataException("Prefab asset conflicts with existing content."); continue; }
                AtomicFile.Write(full, asset.Value); generated.Add(asset.Key);
            }
            CommitDefinition(destination, candidate);
            return new(instance.Id, instance.Members.Where(m => !m.Deleted).Select(m => m.ObjectId).ToArray(), generated.AsReadOnly(), preserved);
        }
        catch { foreach (string path in generated) File.Delete(Path.Combine(root, path)); throw; }
    }

    private static void CaptureOverrides(MapDefinition definition, MapPrefabInstance instance)
    {
        foreach (var member in instance.Members)
        {
            var current = MapObjects.Find(definition, member.ObjectId);
            member.Deleted = current == null;
            if (current != null)
            {
                if (current.Value.GetType().Name != member.Kind)
                    throw new InvalidDataException("A linked prefab object's type was changed locally. Detach the instance before updating it to preserve the converted geometry.");
                member.Overrides = Patch(member.Baseline, Encode(current.Value));
            }
        }
        foreach (var material in instance.Materials)
        {
            var current = definition.Materials.SingleOrDefault(m => m.Id == material.MaterialId);
            if (current != null) material.Overrides = Patch(material.Baseline, Encode(current));
        }
    }
    private static string ResolveSource(MapDefinition definition, string path) => Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(definition.BaseDirectory ?? ".", path));
    private static MapPrefabInstance RequireInstance(MapDefinition d, Guid id) => d.PrefabInstances.SingleOrDefault(i => i.Id == id) ?? throw new InvalidDataException("Prefab instance no longer exists.");
    private static IEnumerable<string> Channels(MapMaterial material) => new[] { material.Texture, material.Albedo, material.Normal, material.SpecularRoughness, material.Emissive }.Where(x => !string.IsNullOrEmpty(x)).Cast<string>().Concat(material.Animation?.FlipbookFrames ?? new());
    private static string Encode(object value) => JsonSerializer.Serialize(value, value.GetType(), PrefabJson);
    private static object Decode(string kind, string json) => JsonSerializer.Deserialize(json, kind switch
    {
        nameof(MapBox) => typeof(MapBox), nameof(MapWedge) => typeof(MapWedge), nameof(MapPrism) => typeof(MapPrism),
        nameof(MapMesh) => typeof(MapMesh), nameof(MapConvexBrush) => typeof(MapConvexBrush), nameof(MapBrush) => typeof(MapBrush),
        nameof(MapSpawn) => typeof(MapSpawn), nameof(MapItem) => typeof(MapItem), nameof(MapJumpPad) => typeof(MapJumpPad),
        nameof(MapNavigationLink) => typeof(MapNavigationLink), _ => throw new InvalidDataException("Invalid prefab member kind.")
    }, PrefabJson) ?? throw new InvalidDataException("Invalid prefab baseline.");
    private static string? Patch(string baseline, string current)
    {
        JsonNode? Difference(JsonNode? a, JsonNode? b)
        {
            if (JsonNode.DeepEquals(a, b)) return null;
            if (a is JsonObject first && b is JsonObject second)
            {
                var patch = new JsonObject();
                foreach (string key in first.Select(p => p.Key).Union(second.Select(p => p.Key)))
                { if (JsonNode.DeepEquals(first[key], second[key])) continue; patch[key] = Difference(first[key], second[key]); }
                return patch;
            }
            return b?.DeepClone();
        }
        return Difference(JsonNode.Parse(baseline), JsonNode.Parse(current))?.ToJsonString();
    }
    private static object Merge(Type type, string baseline, string patch)
    {
        JsonNode? Apply(JsonNode? value, JsonNode? changes)
        {
            if (changes is not JsonObject fields) return changes?.DeepClone();
            var result = value?.DeepClone() as JsonObject ?? new JsonObject();
            foreach (var field in fields) { if (field.Value == null) result.Remove(field.Key); else result[field.Key] = Apply(result[field.Key], field.Value); }
            return result;
        }
        return Apply(JsonNode.Parse(baseline), JsonNode.Parse(patch))!.Deserialize(type, PrefabJson) ?? throw new InvalidDataException("Invalid prefab override.");
    }
    private static Matrix4x4 Matrix(MapTransform transform)
    {
        if (transform.Position?.Length != 3 || transform.Scale?.Length != 3 || transform.Rotation?.Length != 4
            || transform.Position.Concat(transform.Scale).Concat(transform.Rotation).Any(x => !float.IsFinite(x))
            || transform.Scale.Any(x => Math.Abs(x) < .00001f)) throw new InvalidDataException("Prefab transform requires finite position, nonzero scale and rotation.");
        var rotation = new Quaternion(transform.Rotation[0], transform.Rotation[1], transform.Rotation[2], transform.Rotation[3]);
        if (rotation.LengthSquared() < .00001f) throw new InvalidDataException("Prefab rotation is invalid.");
        return Matrix4x4.CreateScale(new Vector3(transform.Scale)) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) * Matrix4x4.CreateTranslation(new Vector3(transform.Position));
    }
    private static float[] Point(float[] point, Matrix4x4 matrix)
    {
        if (point?.Length != 3 || point.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Prefab contains invalid coordinates.");
        var result = Vector3.Transform(new Vector3(point), matrix); return new[] { result.X, result.Y, result.Z };
    }
    private static void TransformObject(object value, Matrix4x4 matrix)
    {
        if (value is MapGeometry geometry)
        {
            Matrix4x4 combined = Matrix(geometry.Transform) * matrix;
            if (!Matrix4x4.Decompose(combined, out var scale, out var rotation, out var position)) throw new InvalidDataException("Prefab transform cannot be resolved.");
            var composed = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
            if (Math.Abs(combined.M12 - composed.M12) + Math.Abs(combined.M13 - composed.M13) + Math.Abs(combined.M21 - composed.M21)
                + Math.Abs(combined.M23 - composed.M23) + Math.Abs(combined.M31 - composed.M31) + Math.Abs(combined.M32 - composed.M32) > .0001f)
                throw new InvalidDataException("This prefab transform would shear geometry. Use uniform scale or detach the instance.");
            geometry.Transform = new() { Position = new[] { position.X, position.Y, position.Z }, Scale = new[] { scale.X, scale.Y, scale.Z }, Rotation = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W } };
        }
        else if (value is MapBrush brush)
        {
            if (Math.Abs(matrix.M12) + Math.Abs(matrix.M13) + Math.Abs(matrix.M21) + Math.Abs(matrix.M23) + Math.Abs(matrix.M31) + Math.Abs(matrix.M32) > .0001f)
                throw new InvalidDataException("Upgrade legacy boxes before rotating a prefab.");
            var a = Point(brush.Min, matrix); var b = Point(brush.Max, matrix);
            brush.Min = a.Zip(b, Math.Min).ToArray(); brush.Max = a.Zip(b, Math.Max).ToArray();
        }
        else if (value is MapNavigationLink link) { link.From = Point(link.From, matrix); link.To = Point(link.To, matrix); }
        else if (value is MapEntityDefinition entity)
        {
            entity.Position = Point(entity.Position, matrix);
            if (entity is MapSpawn spawn)
            {
                float yaw = spawn.Yaw * MathF.PI / 180; var direction = Vector3.TransformNormal(new(MathF.Sin(yaw), 0, MathF.Cos(yaw)), matrix);
                spawn.Yaw = MathF.Atan2(direction.X, direction.Z) * 180 / MathF.PI;
            }
            if (entity is MapJumpPad pad)
            {
                if (pad.Target != null) pad.Target = Point(pad.Target, matrix);
                if (pad.Vector != null) { var direction = Vector3.TransformNormal(new Vector3(pad.Vector), matrix); pad.Vector = new[] { direction.X, direction.Y, direction.Z }; }
                var size = new Vector3(pad.Size); pad.Size = new[] { Math.Abs(matrix.M11) * size.X + Math.Abs(matrix.M21) * size.Y + Math.Abs(matrix.M31) * size.Z,
                    Math.Abs(matrix.M12) * size.X + Math.Abs(matrix.M22) * size.Y + Math.Abs(matrix.M32) * size.Z,
                    Math.Abs(matrix.M13) * size.X + Math.Abs(matrix.M23) * size.Y + Math.Abs(matrix.M33) * size.Z };
            }
        }
    }
    internal static void RemoveObject(MapDefinition d, Guid id)
    { d.Geometry.RemoveAll(o => o.Id == id); d.Brushes.RemoveAll(o => o.Id == id); d.Spawns.RemoveAll(o => o.Id == id); d.Items.RemoveAll(o => o.Id == id); d.JumpPads.RemoveAll(o => o.Id == id); d.NavigationLinks.RemoveAll(o => o.Id == id); }
    private static void ReplaceObject(MapDefinition d, Guid id, object value) { RemoveObject(d, id); AddObject(d, value); }
    private static void AddObject(MapDefinition d, object value)
    { switch (value) { case MapGeometry g: d.Geometry.Add(g); break; case MapBrush b: d.Brushes.Add(b); break; case MapSpawn s: d.Spawns.Add(s); break; case MapItem i: d.Items.Add(i); break; case MapJumpPad p: d.JumpPads.Add(p); break; case MapNavigationLink n: d.NavigationLinks.Add(n); break; default: throw new InvalidDataException("Unsupported prefab object."); } }
    private static void SetObjectId(object value, Guid id)
    { switch (value) { case MapGeometry g: g.Id = id; break; case MapBrush b: b.Id = id; break; case MapEntityDefinition e: e.Id = id; break; case MapNavigationLink n: n.Id = id; break; } }
    private static void CommitDefinition(MapDefinition destination, MapDefinition candidate)
    {
        destination.Geometry = candidate.Geometry; destination.Brushes = candidate.Brushes; destination.Spawns = candidate.Spawns;
        destination.Items = candidate.Items; destination.JumpPads = candidate.JumpPads; destination.NavigationLinks = candidate.NavigationLinks;
        destination.Materials = candidate.Materials; destination.Assets = candidate.Assets; destination.PrefabInstances = candidate.PrefabInstances;
    }
    private static void EnsurePrivateAssetPath(string root, string full)
    {
        Directory.CreateDirectory(root);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(full)!); directory != null; directory = directory.Parent)
        { if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Prefab asset directory cannot be a link."); if (directory.FullName == root) break; }
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Prefab asset cannot be a link.");
    }
}
