using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;

internal static partial class Program
{
    private sealed record CapsuleDiagnostic(uint Frame, uint Rng1, uint Rng2, string GraphHash,
        Dictionary<string, byte[]> Components, Dictionary<string, byte[]> Fields, int Nodes);

    private static void CaptureReplayCapsule(StudioReplayPlayer owner, string destination)
    {
        var player = (PassiveReplayPlayer)owner.GetType().GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        using var resources = ((StudioReplayResources)owner.GetType().GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!).Enter();
        using var capsule = ReplayWorldCheckpoint.Capture(player.Current);
        File.WriteAllBytes(destination, capsule.Bytes.ToArray());
    }

    private static void CompareReplayCapsules(string before, string after, string report)
    {
        CapsuleDiagnostic left = ReadCapsule(before), right = ReadCapsule(after);
        object[] differences = left.Fields.Keys.Concat(right.Fields.Keys).Distinct().Order(StringComparer.Ordinal)
            .Where(key => !left.Fields.TryGetValue(key, out var a) || !right.Fields.TryGetValue(key, out var b) || !a.SequenceEqual(b))
            .Take(256).Select(key => (object)new
            {
                Field = key,
                Before = left.Fields.TryGetValue(key, out var a) ? Describe(a) : null,
                After = right.Fields.TryGetValue(key, out var b) ? Describe(b) : null
            }).ToArray();
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            Before = new { left.Frame, left.Rng1, left.Rng2, left.GraphHash, left.Nodes },
            After = new { right.Frame, right.Rng1, right.Rng2, right.GraphHash, right.Nodes },
            Components = left.Components.Keys.Concat(right.Components.Keys).Distinct().Select(key => new
            {
                Name = key,
                Equal = left.Components.TryGetValue(key, out var a) && right.Components.TryGetValue(key, out var b) && a.SequenceEqual(b),
                Before = left.Components.TryGetValue(key, out a) ? Describe(a) : null,
                After = right.Components.TryGetValue(key, out b) ? Describe(b) : null
            }),
            DifferenceLimit = 256, Differences = differences
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Describe(byte[] value)
    {
        float? number = value.Length == 4 ? BitConverter.ToSingle(value) : null;
        if (number.HasValue && !float.IsFinite(number.Value)) number = null;
        return new
        {
            Length = value.Length, Sha256 = Convert.ToHexString(SHA256.HashData(value)),
            PrefixHex = Convert.ToHexString(value.AsSpan(0, Math.Min(value.Length, 16))), Float32 = number,
            Int32 = value.Length == 4 ? BitConverter.ToInt32(value) : (int?)null
        };
    }

    private static CapsuleDiagnostic ReadCapsule(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var capsule = ReplayWorldCheckpoint.FromBytes(bytes);
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream);
        reader.ReadUInt32(); ushort version = reader.ReadUInt16(); reader.ReadString(); reader.ReadString(); reader.ReadInt32(); reader.ReadUInt64();
        uint frame = reader.ReadUInt32(), rng1 = reader.ReadUInt32(), rng2 = reader.ReadUInt32();
        var parts = new Dictionary<string, byte[]>(); var fields = new Dictionary<string, byte[]>();
        parts["Construction baseline"] = Component(reader); parts["Replica decoder state"] = Component(reader);
        Type[] types = (Type[])typeof(ReplayWorldCheckpoint).GetField("ObjectTypes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var schema = (Dictionary<Type, FieldInfo[]>)typeof(ReplayWorldCheckpoint).GetField("Fields", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        int count = Bounded(reader.ReadInt32(), 32768);
        for (int index = 0; index < count; index++)
        {
            ushort id = reader.ReadUInt16(); reader.ReadUInt64(); byte[] data = Component(reader);
            if (id >= types.Length) throw new InvalidDataException("Unknown capsule diagnostic type.");
            Type type = types[id]; string key = $"Node {index:D5} {type}";
            if (type == typeof(ModelInstance) || type == typeof(WeaponInfo)) { fields[key] = data; continue; }
            using var node = new MemoryStream(data); using var input = new BinaryReader(node);
            if (type == typeof(ItemInstanceEntity) || type == typeof(HalfturretEntity) || type.Name == "SingleParticle") input.ReadInt32();
            if (schema.TryGetValue(type, out var definition))
            {
                foreach (var field in definition)
                {
                    long start = node.Position; SkipValue(input, field.FieldType);
                    fields[key + "." + field.DeclaringType!.Name + "." + field.Name] = data.AsSpan((int)start, (int)(node.Position - start)).ToArray();
                }
            }
            else { fields[key] = data; node.Position = node.Length; }
            if (node.Position != node.Length) throw new InvalidDataException("Capsule field diagnostic consumed a different schema length: " + type);
        }
        if (version >= 2) { parts["Mutable model assets / room activation"] = Component(reader); ReadAssetFields(parts["Mutable model assets / room activation"], fields); }
        if (version >= 3) parts["Cosmetic death presentation"] = Component(reader);
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing diagnostic capsule bytes.");
        return new(frame, rng1, rng2, capsule.GraphFingerprint(), parts, fields, count);
    }

    private static int Bounded(int count, int maximum) => count is >= 0 && count <= maximum ? count : throw new InvalidDataException("Capsule diagnostic bound exceeded.");
    private static byte[] Component(BinaryReader reader)
    {
        int count = Bounded(reader.ReadInt32(), ReplayWorldCheckpoint.MaximumBytes); byte[] result = reader.ReadBytes(count);
        return result.Length == count ? result : throw new EndOfStreamException();
    }
    private static void SkipValue(BinaryReader reader, Type type)
    {
        if (type == typeof(object))
        {
            var types = (Dictionary<string, Type>)typeof(ReplayWorldCheckpoint).GetField("Types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            SkipValue(reader, types[reader.ReadString()]); return;
        }
        if (Nullable.GetUnderlyingType(type) is { } inner) { if (reader.ReadBoolean()) SkipValue(reader, inner); return; }
        if (type == typeof(string)) { if (reader.ReadBoolean()) reader.ReadString(); return; }
        if (type.IsEnum) { SkipValue(reader, Enum.GetUnderlyingType(type)); return; }
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)) reader.ReadByte();
        else if (type == typeof(short) || type == typeof(ushort) || type == typeof(char)) reader.ReadUInt16();
        else if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) reader.ReadUInt64();
        else if (!type.IsValueType || type == typeof(int) || type == typeof(uint) || type == typeof(float)) reader.ReadUInt32();
        else foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(field => field.MetadataToken)) SkipValue(reader, field.FieldType);
    }

    private static void ReadAssetFields(byte[] data, Dictionary<string, byte[]> fields)
    {
        using var stream = new MemoryStream(data); using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 1) throw new InvalidDataException("Unknown asset diagnostic format.");
        int count = Bounded(reader.ReadInt32(), 4096);
        foreach (int model in Enumerable.Range(0, count))
        {
            string name = reader.ReadString(); bool firstHunt = reader.ReadBoolean(); string prefix = $"Asset {name} FH={firstHunt}";
            ReadProperties("Node", typeof(Node), "NodeFields", includeBounds: true);
            ReadProperties("Mesh", typeof(Mesh), "MeshFields");
            ReadProperties("Material", typeof(Material), "MaterialFields");
            int matrices = Bounded(reader.ReadInt32(), 65535 * 16); long start = stream.Position;
            stream.Position += matrices * 4L;
            fields[prefix + ".MatrixStackValues"] = data.AsSpan((int)start, matrices * 4).ToArray();

            void ReadProperties(string kind, Type type, string schema, bool includeBounds = false)
            {
                var properties = (PropertyInfo[])typeof(ReplayAssetCheckpoint).GetField(schema, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                int entries = Bounded(reader.ReadInt32(), 65535);
                for (int index = 0; index < entries; index++)
                {
                    foreach (var property in properties)
                    {
                        long at = stream.Position; SkipAsset(reader, property.PropertyType);
                        fields[$"{prefix}.{kind}[{index}].{property.Name}"] = data.AsSpan((int)at, (int)(stream.Position - at)).ToArray();
                    }
                    if (includeBounds)
                    {
                        // Canonical Node.Bounds is an XYZ min/max six-float array.
                        long at = stream.Position; stream.Position += 6 * 4;
                        fields[$"{prefix}.{kind}[{index}].Bounds"] = data.AsSpan((int)at, 6 * 4).ToArray();
                    }
                }
            }
        }
        fields["Room activation tail"] = data.AsSpan((int)stream.Position).ToArray();
    }
    private static void SkipAsset(BinaryReader reader, Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner) { if (reader.ReadBoolean()) SkipAsset(reader, inner); return; }
        int bytes = type.IsEnum || type == typeof(int) || type == typeof(float) ? 4
            : type == typeof(bool) || type == typeof(byte) ? 1
            : type == typeof(OpenTK.Mathematics.Vector3) ? 12 : type == typeof(OpenTK.Mathematics.Vector4) ? 16
            : type == typeof(OpenTK.Mathematics.Matrix4) ? 64 : type == typeof(ColorRgb) ? 3
            : throw new InvalidDataException("Unknown diagnostic asset property type: " + type);
        reader.BaseStream.Position += bytes;
    }
}
