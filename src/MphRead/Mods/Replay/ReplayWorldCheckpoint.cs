using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

/// <summary>A bounded value capsule. References are graph indices or construction
/// anchors in an independently loaded scene. Native resources and delegates never
/// enter the payload. Restore is only used on an unpublished, disposable replica.</summary>
internal sealed partial class ReplayWorldCheckpoint : IDisposable
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private const int MaximumObjects = 32768;
    private const uint Magic = 0x43575050; // PPWC
    private const ushort Version = 11;
    private const ushort CustomVersion = 12;
    private readonly ReplayPayload _data;
    internal ReadOnlySpan<byte> Bytes => _data.Span;
    internal ReplayPayload Payload => _data;
    internal uint Frame { get; }
    internal bool IndexedFirePresentation { get; }
    internal bool HasPresentationContract { get; }
    internal bool HasAuthorityFormHistory { get; }
    internal bool HasTurretOutcomeContract { get; }
    internal bool HasNativeTouchContract => (_appendices & ReplayWorldCheckpointAppendices.NativeTouch) != 0;
    internal bool HasGrappleContract => (_appendices & ReplayWorldCheckpointAppendices.Grapple) != 0;
    internal bool HasTimedPickupContract => (_appendices & ReplayWorldCheckpointAppendices.Pickups) != 0;
    internal bool HasTimedShotContract => (_decoderFeatures & ReplayReplicaCheckpointFeatures.TimedShots) != 0;
    internal bool HasCustomWeaponContract => (_appendices & ReplayWorldCheckpointAppendices.CustomWeapons) != 0;
    internal ReplayCustomWeaponDescriptor? CustomWeaponDescriptor { get; }
    internal bool RequiresNativeTouchContract => (_decoderFeatures & ReplayReplicaCheckpointFeatures.NativeTouch) != 0;
    private readonly ReplayWorldCheckpointAppendices _appendices;
    private readonly ReplayReplicaCheckpointFeatures _decoderFeatures;
    private readonly string _sourceContract;
    private ReplayWorldCheckpoint(ReplayPayload data, uint frame, string? sourceContract = null,
        bool indexedFirePresentation = false, bool hasPresentationContract = true, bool hasAuthorityFormHistory = true, bool hasTurretOutcomeContract = true,
        ReplayWorldCheckpointAppendices appendices = ReplayWorldCheckpointAppendices.NativeTouch | ReplayWorldCheckpointAppendices.Pickups | ReplayWorldCheckpointAppendices.Grapple,
        ReplayReplicaCheckpointFeatures decoderFeatures = ReplayReplicaCheckpointLayout.CurrentFeatures | ReplayReplicaCheckpointFeatures.Grapple,
        ReplayCustomWeaponDescriptor? customDescriptor = null)
    {
        _data = data; Frame = frame; _sourceContract = sourceContract ?? Contract;
        IndexedFirePresentation = indexedFirePresentation; HasPresentationContract = hasPresentationContract;
        HasAuthorityFormHistory = hasAuthorityFormHistory; HasTurretOutcomeContract = hasTurretOutcomeContract;
        _appendices = appendices; _decoderFeatures = decoderFeatures;
        CustomWeaponDescriptor = customDescriptor;
    }
    private bool _disposed;
    public void Dispose() { if (!_disposed) { _disposed = true; _data.Release(); GC.SuppressFinalize(this); } }
    ~ReplayWorldCheckpoint() => Dispose();
    internal static ReplayWorldCheckpoint FromBytes(ReadOnlySpan<byte> bytes, string? producerBuild = null)
        => FromBytesCore(bytes, producerBuild, custom: false);
    internal static ReplayWorldCheckpoint FromCustomBytes(ReadOnlySpan<byte> bytes, string? producerBuild = null)
        => FromBytesCore(bytes, producerBuild, custom: true);
    private static ReplayWorldCheckpoint FromBytesCore(ReadOnlySpan<byte> bytes, string? producerBuild, bool custom)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Replay world exceeds its checkpoint budget.");
        var data = ReplayPayload.Copy(bytes);
        try
        {
            using var stream = data.OpenRead(); using var reader = new BinaryReader(stream);
            var (version, declared) = ReadWorldHeader(reader);
            if ((version == CustomVersion) != custom)
                throw new InvalidDataException("An explicit custom world12 inspection contract is required; native and custom capsules are distinct.");
            string sourceContract = reader.ReadString();
            if (!SupportsContract(sourceContract, producerBuild))
                throw new InvalidDataException("This replay uses a different saved-world layout. Open it with the game version that recorded it.");
            string room = reader.ReadString(); var mode = (GameMode)reader.ReadInt32(); reader.ReadUInt64();
            uint frame = reader.ReadUInt32();
            var inspection = InspectWorldBody(reader, version, declared, room, mode, frame);
            return new(data, frame, sourceContract, indexedFirePresentation: inspection.IndexedFire,
                hasPresentationContract: version >= 6, hasAuthorityFormHistory: version >= 7,
                hasTurretOutcomeContract: version >= 8, appendices: inspection.Appendices,
                decoderFeatures: inspection.DecoderFeatures, customDescriptor: inspection.CustomDescriptor);
        }
        catch { data.Release(); throw; }
    }

    // Detached metadata only: safe to read on a writer worker, no Scene/resource access.
    internal ReplayMetadata ClipMetadata(uint leadIn, IReadOnlyList<ReplayPlayerInfo> players)
    {
        using var stream = _data.OpenRead(); using var reader = new BinaryReader(stream);
        ReadWorldHeader(reader); reader.ReadString();
        string room = reader.ReadString(); var mode = (GameMode)reader.ReadInt32(); ulong map = reader.ReadUInt64();
        uint origin = reader.ReadUInt32();
        return new ReplayMetadata { FormatVersion = HasCustomWeaponContract ? (byte)7 : HasTimedShotContract ? (byte)6 : (byte)4,
            TimedShotCertificates = HasTimedShotContract, Type = ReplayType.Clip, RoomKey = room, Mode = mode,
            MapHash = map, OriginRecordingFrame = origin, LeadInFrames = leadIn,
            WorldCheckpoint = Bytes.ToArray(), Players = players, CustomWeapons = CustomWeaponDescriptor,
            Bootstrap = HasCustomWeaponContract ? new() { Packets = new[] { ReplayTimelineArchive.Construction(DecoderState()) } } : new() };
    }
    internal ReplayReplicaCheckpoint ConstructionState()
    {
        using var stream = _data.OpenRead(); using var reader = new BinaryReader(stream);
        ReadWorldHeader(reader); reader.ReadString(); reader.ReadString(); reader.ReadInt32(); reader.ReadUInt64();
        reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32();
        return new(ReadBytes(reader, HasCustomWeaponContract ? ReplayReplicaCheckpoint.MaximumCustomBytes : ReplayReplicaCheckpoint.MaximumBytes));
    }
    internal ReplayReplicaCheckpoint DecoderState()
    {
        using var stream = _data.OpenRead(); using var reader = new BinaryReader(stream);
        ReadWorldHeader(reader); reader.ReadString(); reader.ReadString(); reader.ReadInt32(); reader.ReadUInt64();
        reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32();
        int maximum = HasCustomWeaponContract ? ReplayReplicaCheckpoint.MaximumCustomBytes : ReplayReplicaCheckpoint.MaximumBytes;
        ReadBytes(reader, maximum); return new(ReadBytes(reader, maximum));
    }

    private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> StructFields = new();
    private static readonly Dictionary<Type, FieldInfo[]> Fields = CreateFields();
    private static readonly Dictionary<string, Type> Types = CreateTypes();
    private static readonly Type[] ObjectTypes = Types.Values
        .Where(t => Fields.ContainsKey(t) || Collection(t) || t == typeof(ModelInstance) || t == typeof(WeaponInfo))
        .OrderBy(t => t.ToString(), StringComparer.Ordinal).ToArray();
    private static readonly Dictionary<Type, ushort> TypeIds = ObjectTypes.Select((type, index) => (type, index))
        .ToDictionary(pair => pair.type, pair => checked((ushort)pair.index));
    // FullName of a constructed generic embeds assembly versions for its
    // arguments. Type.ToString preserves type identity without tying saves to
    // the product version (e.g. ValueTuple<ChatLine, float>).
    private static readonly string Contract = ComputeContract(stable: true);
    private static readonly string LegacyContract = ComputeContract(stable: false);
    internal static string ComputeContract(bool stable, string? producerBuild = null)
    {
        string TypeName(Type type) => stable ? type.ToString() : type.FullName!;
        string schema = "world-codec-1|" + string.Join('|', Fields.OrderBy(p => p.Key.FullName, StringComparer.Ordinal)
            .Select(p => TypeName(p.Key) + ":" + string.Join(',', p.Value.Select(f =>
                TypeName(f.DeclaringType!) + "." + f.Name + ":" + f.FieldType))))
            + string.Join('|', Types.Values.Where(t => t.IsValueType && !t.IsPrimitive && !t.IsEnum)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).Select(t => TypeName(t) + ":" +
                    string.Join(',', ValueFields(t).Select(f => f.Name + ":" + f.FieldType))));
        if (!stable && producerBuild != null)
        {
            string versionText = producerBuild.Split('+')[0].TrimStart('v');
            if (!System.Version.TryParse(versionText, out var version)) return "";
            var normalized = new System.Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
            var assembly = typeof(Scene).Assembly.GetName();
            schema = schema.Replace(assembly.Name + ", Version=" + assembly.Version,
                assembly.Name + ", Version=" + normalized, StringComparison.Ordinal);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema)));
    }
    internal static bool SupportsContract(string contract, string? producerBuild = null)
        => contract.Length == 64 && (contract == Contract || contract == LegacyContract
            || (producerBuild != null && contract == ComputeContract(stable: false, producerBuild)));
    private static Dictionary<Type, FieldInfo[]> CreateFields()
    {
        var result = new Dictionary<Type, FieldInfo[]>();
        foreach (var pair in ReplayWorldSchemas.Fields)
        {
            Type type = typeof(Scene).Assembly.GetType(pair.Key, throwOnError: true)!;
            var fields = new List<FieldInfo>();
            for (Type? parent = type; parent != null; parent = parent.BaseType)
                if (ReplayWorldSchemas.Fields.TryGetValue(parent.FullName!, out string[]? names))
                    foreach (string name in names)
                        fields.Add(parent.GetField(name, InstanceFields | BindingFlags.DeclaredOnly)
                            ?? throw new InvalidOperationException($"Missing checkpoint field {parent.Name}.{name}."));
            result.Add(type, fields.ToArray());
        }
        return result;
    }
    private static FieldInfo[] ValueFields(Type type) => StructFields.GetOrAdd(type,
        static value => value.GetFields(InstanceFields).OrderBy(f => f.MetadataToken).ToArray());
    private static bool Collection(Type type) => type.IsArray || type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(Queue<>)
        || type.GetGenericTypeDefinition() == typeof(LinkedList<>));
    private static Type Element(Type type) => type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
    private static Dictionary<string, Type> CreateTypes()
    {
        var result = new Dictionary<string, Type>(StringComparer.Ordinal);
        void Add(Type type)
        {
            if (result.ContainsKey(type.ToString())) return;
            result.Add(type.ToString(), type);
            if (Collection(type)) Add(Element(type));
            else if (Nullable.GetUnderlyingType(type) is Type underlying) Add(underlying);
            else if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
                foreach (var field in ValueFields(type)) Add(field.FieldType);
        }
        foreach (var pair in Fields) { Add(pair.Key); foreach (var field in pair.Value) Add(field.FieldType); }
        foreach (Type type in new[] { typeof(ModelInstance), typeof(WeaponInfo), typeof(int), typeof(uint), typeof(float),
            typeof(bool), typeof(string), typeof(Vector3), typeof(EntityBase) }) Add(type);
        return result;
    }

    /// <summary>Anchors are owned by the destination world, never by the capsule.
    /// Construct this once, immediately after the room is initialized.</summary>
    internal sealed class Bindings
    {
        internal readonly CaptureGraph Graph = new();
        internal readonly List<Model> Models = new();
        internal readonly Dictionary<object, ulong> Names = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<ulong, object> Objects = new();
        internal readonly object[] Roots;
        internal Bindings(Scene scene)
        {
            Roots = [scene, scene.GameState, scene.Players, scene.PlayerReplication, scene.Services, scene.ContinuousPhase];
            for (int i = 0; i < Roots.Length; i++) Register(Roots[i], "root" + i);
        }
        private void Register(object? value, string name)
        {
            if (value == null || value is string || value.GetType().IsValueType || Names.ContainsKey(value)) return;
            Type type = value.GetType();
            if (value is WeaponInfo) return;
            if (!Fields.ContainsKey(type) && !Collection(type) && value is not ModelInstance)
                throw new InvalidOperationException($"Unsupported checkpoint anchor {type.Name}: {name}.");
            // Stable path hash, never a runtime object address. Collisions fail
            // construction rather than binding two unrelated objects together.
            ulong identity = 14695981039346656037;
            foreach (char character in name) { identity ^= character; identity *= 1099511628211; }
            if (identity == 0 || Objects.ContainsKey(identity)) throw new InvalidOperationException("Replay construction anchor collision.");
            Names.Add(value, identity); Objects.Add(identity, value);
            if (Collection(type))
            {
                // The collection itself remains an anchor. Its value/string
                // elements were always rejected by Register before insertion.
                Type element = Element(type);
                if (element.IsValueType || element == typeof(string)) return;
                int i = 0;
                foreach (object? item in (IEnumerable)value)
                {
                    if (Candidate(item)) Register(item, name + "/" + i);
                    i++; // Preserve original indices, including ignored entries.
                }
            }
            else if (Fields.TryGetValue(type, out FieldInfo[]? fields))
                foreach (var field in fields)
                {
                    if (field.FieldType.IsValueType || field.FieldType == typeof(string)) continue;
                    object? item = field.GetValue(value);
                    if (Candidate(item)) Register(item, name + "/" + field.DeclaringType!.Name + "." + field.Name);
                }
        }
        private bool Candidate(object? value)
            => value != null && value is not string && !value.GetType().IsValueType
                && value is not WeaponInfo && !Names.ContainsKey(value);
    }

    internal static ReplayWorldCheckpoint Capture(PassiveReplayScene replay, uint? recordingFrame = null, bool boundAccessors = true)
    {
        if (replay.State.CustomWeapons != null)
            throw new InvalidOperationException("Custom Scene capture requires the retained historical resource and presentation contract.");
        using var perf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.Checkpoint);
        using var stream = new ReplayCheckpointWriter();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic); writer.Write(Version);
        writer.Write((byte)(ReplayWorldCheckpointAppendices.NativeTouch | ReplayWorldCheckpointAppendices.Pickups | ReplayWorldCheckpointAppendices.Grapple));
        writer.Write(Contract);
        writer.Write(replay.Scene.Room!.Meta.Name); writer.Write((int)replay.Scene.GameState.Mode); writer.Write(replay.MapHash);
        uint frame = recordingFrame ?? replay.Session.CurrentFrame;
        writer.Write(frame); writer.Write(replay.Scene.Random.Rng1); writer.Write(replay.Scene.Random.Rng2);
        WriteBytes(writer, replay.InitialState.Bytes);
        long decoder = ReplayCheckpointWriter.BeginComponent(writer);
        replay.State.WriteCheckpoint(writer);
        ReplayCheckpointWriter.EndComponent(writer, decoder);
        var graph = replay.CheckpointBindings.Graph;
        try
        {
            foreach (object root in replay.CheckpointBindings.Roots) graph.Id(root);
            graph.Write(writer, replay.CheckpointBindings, boundAccessors);
            long assets = ReplayCheckpointWriter.BeginComponent(writer);
            ReplayAssetCheckpoint.Write(writer, replay.Scene, replay.CheckpointBindings.Models, boundAccessors);
            ReplayCheckpointWriter.EndComponent(writer, assets);
            // Optional presentation appendix keeps the gameplay graph/type IDs and
            // fingerprint unchanged, so pre-cosmetics world capsules still load.
            long cosmetics = ReplayCheckpointWriter.BeginComponent(writer);
            foreach (var player in replay.Scene.Players.Items) player.CosmeticDeathState.Write(writer);
            ReplayCheckpointWriter.EndComponent(writer, cosmetics);
            long bombs = ReplayCheckpointWriter.BeginComponent(writer);
            replay.Scene.Bombs.WriteCheckpoint(writer);
            ReplayCheckpointWriter.EndComponent(writer, bombs);
            long traversal = ReplayCheckpointWriter.BeginComponent(writer);
            foreach (var player in replay.Scene.Players.Items) player.ModWriteQ3Traversal(writer);
            ReplayCheckpointWriter.EndComponent(writer, traversal);
            // Reapplying a cached form sample after restore rewinds transition
            // animation. Keep this cache outside the stable legacy object graph.
            long forms = ReplayCheckpointWriter.BeginComponent(writer);
            replay.Scene.PlayerReplication.WriteReplayAuthorityForms(writer);
            ReplayCheckpointWriter.EndComponent(writer, forms);
            // The explicitly declared appendix preserves the native 30/60 Hz sample phase and boost
            // latch without changing the stable checkpoint object schema.
            long nativeTouch = ReplayCheckpointWriter.BeginComponent(writer);
            foreach (var player in replay.Scene.Players.Items)
                player.ModWriteNativeTouchCheckpoint(writer);
            ReplayCheckpointWriter.EndComponent(writer, nativeTouch);
            long pickups = ReplayCheckpointWriter.BeginComponent(writer);
            WritePickupItems(writer, graph, replay.State);
            ReplayCheckpointWriter.EndComponent(writer, pickups);
            long grapple = ReplayCheckpointWriter.BeginComponent(writer);
            Span<byte> grappleRow = stackalloc byte[23];
            for (int slot = 0; slot < 8; slot++)
            {
                grappleRow.Clear();
                if (replay.State.TryGetPlayer(slot, out var accepted) && accepted.SlotGeneration != 0 && accepted.LifeId != 0)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(grappleRow, accepted.SlotGeneration);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(grappleRow[2..], accepted.LifeId);
                    replay.Scene.Players.Items[slot].ModGrappleState.TryWrite(grappleRow[4..], out _);
                }
                writer.Write(grappleRow);
            }
            ReplayCheckpointWriter.EndComponent(writer, grapple);
            // Posed discrete firing (protocol 43+) changes world reconstruction.
            // A live canonical world has no indexed cursor; its capsule remains
            // a valid recording baseline, but cannot accelerate indexed seeks.
            bool indexedFirePresentation = replay.Scene.ReplayPoses is { } poses && poses.Prepare() && poses.IndexedFirePresentation;
            writer.Write(indexedFirePresentation);
            ReplayPerfTelemetry.CheckpointBytes = stream.Length;
            return new(stream.Detach(), frame, indexedFirePresentation: indexedFirePresentation,
                decoderFeatures: ReplayReplicaCheckpointLayout.CurrentFeatures | ReplayReplicaCheckpointFeatures.Grapple
                    | (replay.State.TimedShots.Enabled ? ReplayReplicaCheckpointFeatures.TimedShots : ReplayReplicaCheckpointFeatures.None));
        }
        finally { graph.Clear(); }
    }

    private sealed record Node(ushort Type, ulong Anchor, byte[] Data);
    internal sealed class CaptureGraph
    {
        private readonly Dictionary<object, int> _indices = new(4096, ReferenceEqualityComparer.Instance);
        private readonly List<object> _objects = new(4096);
        private readonly Func<object?, int> _reference;
        internal CaptureGraph() => _reference = Id;
        internal void Clear() { _indices.Clear(); _objects.Clear(); }
        internal IEnumerable<(int Id, ItemInstanceEntity Item)> PickupItems()
        {
            for (int i = 0; i < _objects.Count; i++)
                if (_objects[i] is ItemInstanceEntity item && item.ModPickupSpawn.HasValue) yield return (i, item);
        }
        internal int Id(object? value)
        {
            if (value == null) return -1;
            if (_indices.TryGetValue(value, out int id)) return id;
            if (_objects.Count == MaximumObjects) throw new InvalidDataException("Replay object budget exceeded.");
            id = _objects.Count; _objects.Add(value); _indices.Add(value, id); return id;
        }
        internal void Write(BinaryWriter writer, Bindings bindings, bool boundAccessors)
        {
            long countAt = writer.BaseStream.Position; writer.Write(0);
            for (int index = 0; index < _objects.Count; index++)
            {
                object value = _objects[index]; Type type = value.GetType();
                if (!TypeIds.TryGetValue(type, out ushort typeId)) throw new InvalidOperationException($"No replay schema for {type}.");
                writer.Write(typeId); writer.Write(bindings.Names.GetValueOrDefault(value));
                long node = ReplayCheckpointWriter.BeginComponent(writer);
                if (value is ModelInstance model)
                {
                    writer.Write(model.Model.Name); writer.Write(model.Model.FirstHunt);
                    long animation = ReplayCheckpointWriter.BeginComponent(writer);
                    ReplayModelCheckpoint.Write(writer, model);
                    ReplayCheckpointWriter.EndComponent(writer, animation);
                }
                else if (value is WeaponInfo weapon) WriteWeapon(writer, weapon);
                else
                {
                    if (value is ItemInstanceEntity item) writer.Write((int)item.ItemType);
                    if (value is HalfturretEntity turret) writer.Write(turret.Owner.SlotIndex);
                    if (value is SingleParticle single) writer.Write(SingleIdentity(single.ParticleDefinition));
                    if (boundAccessors && (ReplayCheckpointAccessors.Contract == Contract || ReplayCheckpointAccessors.Contract == LegacyContract) && ReplayCheckpointAccessors.Write(writer, value, _reference)) { }
                    else if (Collection(type))
                    {
                        if (value is Array array)
                            for (int dimension = 0; dimension < array.Rank; dimension++) writer.Write(array.GetLength(dimension));
                        else writer.Write(((ICollection)value).Count);
                        foreach (object? itemValue in (IEnumerable)value) WriteValue(writer, Element(type), itemValue, _reference);
                    }
                    else if (Fields.TryGetValue(type, out var fields))
                        foreach (var field in fields) WriteValue(writer, field.FieldType, field.GetValue(value), _reference);
                    else throw new InvalidOperationException($"No replay object schema for {type}.");
                }
                ReplayCheckpointWriter.EndComponent(writer, node);
            }
            long end = writer.BaseStream.Position; writer.BaseStream.Position = countAt;
            writer.Write(_objects.Count); writer.BaseStream.Position = end;
        }
    }

    // These are immutable engine asset identities. Never serialize WeaponInfo's
    // metadata graph or a process-local object address.
    private static readonly IReadOnlyList<WeaponInfo>[] WeaponsByFamily = [Weapons.WeaponsMP, Weapons.Weapons1P,
        Weapons.EnemyWeapons, Weapons.BossWeapons, Weapons.GoreaWeapons, Weapons.PlatformWeapons, Weapons.Ricochets];
    private static void WriteWeapon(BinaryWriter writer, WeaponInfo weapon)
    {
        for (int family = 0; family < WeaponsByFamily.Length; family++)
            for (int i = 0; i < WeaponsByFamily[family].Count; i++)
                if (ReferenceEquals(weapon, WeaponsByFamily[family][i])) { writer.Write(family); writer.Write(i); return; }
        throw new InvalidOperationException("Replay weapon is not an identified engine asset.");
    }
    private static readonly SingleType[] SingleTypes = Enum.GetValues<SingleType>();
    private static int SingleIdentity(Particle? particle)
    {
        if (particle == null) return -1;
        foreach (SingleType type in SingleTypes)
            if (ReferenceEquals(Read.GetSingleParticle(type), particle)) return (int)type;
        throw new InvalidOperationException("Replay single particle has no asset identity.");
    }

    internal void Restore(PassiveReplayScene replay, uint? playbackFrame = null)
    {
        if (HasCustomWeaponContract)
            throw new InvalidOperationException("Custom Scene restore requires the retained historical resource and presentation contract.");
        if (replay.HasStepped) throw new InvalidOperationException("Restore requires a new unpublished replica.");
        using var stream = _data.OpenRead(); using var reader = new BinaryReader(stream);
        if (Bytes.Length > MaximumBytes) throw new InvalidDataException("Invalid replay world capsule.");
        var (version, declared) = ReadWorldHeader(reader);
        if (version is < 1 or > Version
            || reader.ReadString() != _sourceContract || reader.ReadString() != replay.Scene.Room!.Meta.Name
            || reader.ReadInt32() != (int)replay.Scene.GameState.Mode || reader.ReadUInt64() != replay.MapHash)
            throw new InvalidDataException("Replay world checkpoint contract or room differs.");
        uint frame = reader.ReadUInt32(), rng1 = reader.ReadUInt32(), rng2 = reader.ReadUInt32();
        // Older v4 capsules can contain an earlier decoder version. Compare the
        // normalized values, not two different encodings of the same baseline.
        var construction = new ReplayReplicaState();
        construction.RestoreCheckpoint(new ReplayReplicaCheckpoint(ReadBytes(reader, ReplayReplicaCheckpoint.MaximumBytes)));
        if (!construction.CaptureCheckpoint().Bytes.SequenceEqual(replay.InitialState.Bytes))
            throw new InvalidDataException("Replay world construction baseline differs.");
        var decoder = new ReplayReplicaCheckpoint(ReadBytes(reader, ReplayReplicaCheckpoint.MaximumBytes));
        var decoded = new ReplayReplicaState(); decoded.RestoreCheckpoint(decoder);
        for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
        {
            var occupant = decoded.Occupant(slot);
            PlayerEntity player = replay.Scene.Players.Items[slot];
            if (occupant.Generation != 0 && player.Hunter != occupant.Hunter)
            {
                player.ModPrepareHunterResources(occupant.Hunter); player.ModSetHunter(occupant.Hunter); player.Initialize();
            }
        }
        int count = Count(reader, MaximumObjects);
        var nodes = new Node[count]; var types = new Type[count]; var objects = new object[count];
        for (int i = 0; i < count; i++)
        {
            ushort typeId = reader.ReadUInt16(); ulong anchor = reader.ReadUInt64();
            if (typeId >= ObjectTypes.Length)
                throw new InvalidDataException("Unknown replay object contract.");
            nodes[i] = new(typeId, anchor, ReadBytes(reader, MaximumBytes)); types[i] = ObjectTypes[typeId];
        }
        byte[]? assets = version >= 2 ? ReadBytes(reader, MaximumBytes) : null;
        byte[]? cosmetics = version >= 3 ? ReadBytes(reader, 4096) : null;
        byte[]? bombs = version >= 4 ? ReadBytes(reader, MaximumBytes) : null;
        byte[]? traversal = version >= 5 ? ReadBytes(reader, 256 * 1024) : null;
        byte[]? forms = version >= 7 ? ReadBytes(reader, 4096) : null;
        byte[]? nativeTouch = HasNativeTouchContract ? ReadBytes(reader, 4096) : null;
        byte[]? pickups = HasTimedPickupContract ? ReadBytes(reader, 65536) : null;
        byte[]? grapple = HasGrappleContract ? ReadBytes(reader, 184) : null;
        if (grapple != null) ValidateGrappleAppendix(grapple, decoded);
        bool indexedFirePresentation = version >= 6 && reader.ReadBoolean();
        if (indexedFirePresentation != IndexedFirePresentation) throw new InvalidDataException("Replay presentation contract marker differs.");
        if (stream.Position != stream.Length || frame != Frame) throw new InvalidDataException("Invalid replay world capsule length/frame.");
        // Allocate and bind every object before following any link. Only this new
        // replica is modified; its caller disposes it if any validation fails.
        for (int i = 0; i < count; i++)
        {
            Node node = nodes[i]; Type type = types[i];
            if (node.Anchor != 0)
            {
                if (!replay.CheckpointBindings.Objects.TryGetValue(node.Anchor, out object? anchor) || anchor.GetType() != type)
                    throw new InvalidDataException($"Construction anchor differs for {type.Name}.");
                objects[i] = Collection(type) ? Create(type, node.Data, replay.Scene) : anchor;
            }
            else objects[i] = Create(type, node.Data, replay.Scene);
        }
        object? Resolve(int index, Type type)
        {
            if (index == -1) return null;
            if ((uint)index >= count || !type.IsInstanceOfType(objects[index]))
                throw new InvalidDataException("Invalid replay object link.");
            return objects[index];
        }
        for (int i = 0; i < count; i++)
        {
            object target = objects[i]; Type type = types[i];
            using var data = new MemoryStream(nodes[i].Data, writable: false); using var input = new BinaryReader(data);
            if (target is ModelInstance model)
            {
                string name = input.ReadString(); bool firstHunt = input.ReadBoolean();
                if (model.Model.Name != name || model.Model.FirstHunt != firstHunt)
                    model.SetModel(replay.Scene.GetModelInstance(name, firstHunt).Model);
                ReplayModelCheckpoint.FromBytes(ReadBytes(input, 4096)).Restore(model);
            }
            else if (target is WeaponInfo) { input.ReadInt32(); input.ReadInt32(); }
            else
            {
                if (target is ItemInstanceEntity) input.ReadInt32();
                if (target is HalfturretEntity) input.ReadInt32();
                if (target is SingleParticle single)
                {
                    int identity = input.ReadInt32();
                    single.ParticleDefinition = identity == -1 ? null! : Enum.IsDefined((SingleType)identity)
                        ? Read.GetSingleParticle((SingleType)identity) : throw new InvalidDataException("Unknown replay particle asset.");
                }
                if (Collection(type))
                {
                    int rank = type.IsArray ? type.GetArrayRank() : 1;
                    var lengths = new int[rank]; int length = 1;
                    for (int d = 0; d < rank; d++) { lengths[d] = Count(input, MaximumObjects); length = checked(length * lengths[d]); }
                    if (length > MaximumObjects) throw new InvalidDataException("Replay collection too large.");
                    if (target is IList list && target is not Array) list.Clear();
                    var add = type.IsArray || target is IList ? null : type.GetMethod(type.GetGenericTypeDefinition() == typeof(Queue<>) ? "Enqueue" : "AddLast", [Element(type)]);
                    for (int n = 0; n < length; n++)
                    {
                        object? value = ReadValue(input, Element(type), Resolve);
                        if (target is Array array)
                        {
                            var indices = new int[rank]; int remainder = n;
                            for (int d = rank - 1; d >= 0; d--) { indices[d] = remainder % lengths[d]; remainder /= lengths[d]; }
                            array.SetValue(value, indices);
                        }
                        else if (target is IList values) values.Add(value);
                        else add!.Invoke(target, [value]);
                    }
                }
                else foreach (var field in Fields[type]) field.SetValue(target, ReadValue(input, field.FieldType, Resolve));
            }
            if (data.Position != data.Length) throw new InvalidDataException($"Trailing replay fields in {type.Name}.");
        }
        replay.Scene.FinishReplayWorldRestore();
        if (nativeTouch != null)
        {
            using var touchStream = new MemoryStream(nativeTouch, writable: false);
            using var touchReader = new BinaryReader(touchStream);
            foreach (var player in replay.Scene.Players.Items)
                player.ModReadNativeTouchCheckpoint(touchReader);
            if (touchStream.Position != touchStream.Length)
                throw new InvalidDataException("Invalid native touch checkpoint appendix.");
        }
        if (assets != null) ReplayAssetCheckpoint.Restore(replay.Scene, assets);
        if (!replay.Session.Reposition(playbackFrame ?? frame, 0, sourceClock: playbackFrame.HasValue))
            throw new InvalidDataException(replay.Session.LastError);
        replay.State.RestoreCheckpoint(decoder);
        ((ReplaySceneServices)replay.Scene.Services).RestoreMatchModifiers(replay.Scene);
        foreach (var player in replay.Scene.Players.Items)
        {
            player.ModRebindReplayAmmo();
            // Stance is already in the canonical snapshot. Reproject it after
            // field restore rather than changing the stable world schema or
            // advancing live crouch/slide physics during a passive seek.
            player.ModResetTraversalStance();
            if (replay.State.TryGetPlayer(player.SlotIndex, out var stance))
                player.ModApplyReplicatedCrouch(stance.Crouched, isLocal: false, tucked: stance.Tucked);
        }
        if (cosmetics != null)
        {
            using var cosmeticStream = new MemoryStream(cosmetics, writable: false);
            using var cosmeticReader = new BinaryReader(cosmeticStream);
            foreach (var player in replay.Scene.Players.Items) player.CosmeticDeathState.Read(cosmeticReader);
            if (cosmeticStream.Position != cosmeticStream.Length) throw new InvalidDataException("Invalid cosmetics appendix.");
        }
        if (bombs != null)
        {
            using var bombStream = new MemoryStream(bombs, writable: false);
            using var bombReader = new BinaryReader(bombStream);
            replay.Scene.Bombs.ReadCheckpoint(bombReader);
            if (bombStream.Position != bombStream.Length) throw new InvalidDataException("Invalid bomb checkpoint appendix.");
        }
        if (traversal != null)
        {
            using var traversalStream = new MemoryStream(traversal, writable: false);
            using var traversalReader = new BinaryReader(traversalStream);
            foreach (var player in replay.Scene.Players.Items) player.ModReadQ3Traversal(traversalReader);
            if (traversalStream.Position != traversalStream.Length) throw new InvalidDataException("Invalid Q3 traversal appendix.");
        }
        if (forms != null)
        {
            using var formStream = new MemoryStream(forms, writable: false);
            using var formReader = new BinaryReader(formStream);
            replay.Scene.PlayerReplication.ReadReplayAuthorityForms(formReader);
            if (formStream.Position != formStream.Length) throw new InvalidDataException("Invalid authority form cache appendix.");
        }
        if (pickups != null) ReadPickupItems(pickups, objects, replay.State);
        else foreach (var item in objects.OfType<ItemInstanceEntity>())
        { item.ModPickupSpawn = null; item.ModPickupHidden = false; }
        for (int slot = 0; slot < replay.Scene.Players.Items.Count; slot++)
        {
            PlayerGrappleState state = default; ushort generation = 0, life = 0;
            if (grapple != null)
            {
                var row = grapple.AsSpan(slot * 23, 23);
                generation = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(row);
                life = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(row[2..]);
                PlayerGrappleState.TryRead(row[4..], out state);
            }
            replay.Scene.Players.Items[slot].ModRestoreGrapplePresentation(state, generation, life);
        }
        replay.Scene.Random.SetRng1(rng1); replay.Scene.Random.SetRng2(rng2);
    }

    private static void WritePickupItems(BinaryWriter writer, CaptureGraph graph, ReplayReplicaState state)
    {
        var items = graph.PickupItems().ToArray();
        if (items.Length > 2048) throw new InvalidDataException("Replay pickup item appendix exceeds its budget.");
        writer.Write(items.Length);
        foreach (var entry in items)
        {
            var key = entry.Item.ModPickupSpawn!.Value;
            if (!ValidPickupItem(key, entry.Item, state)) throw new InvalidDataException("Invalid replay pickup item identity.");
            writer.Write(entry.Id); writer.Write(key.Epoch); writer.Write(key.Match); writer.Write(key.Room);
            writer.Write(key.SpawnId); writer.Write(key.Generation); writer.Write(entry.Item.ModPickupHidden);
        }
    }
    private static bool ValidPickupItem(PowerUpSpawnKey key, ItemInstanceEntity item, ReplayReplicaState state)
        => state.Match is { } match && key.Epoch == match.AuthorityEpoch && key.Match == match.MatchId
            && (state.PickupRoomIdentity == 0 || key.Room == state.PickupRoomIdentity)
            && NetPowerUpLedger.TryType(item.ItemType, out var type)
            && new PickupItemFact(key, type, 0, !item.ModPickupHidden, item.Position).IsValid;
    private static void ReadPickupItems(byte[] bytes, object[] objects, ReplayReplicaState state)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream);
        int count = Count(reader, 2048); var seen = new HashSet<int>(); var identities = new HashSet<PowerUpSpawnKey>();
        var rows = new List<(ItemInstanceEntity Item, PowerUpSpawnKey Key, bool Hidden)>(count);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            var key = new PowerUpSpawnKey(reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadUInt32());
            byte hidden = reader.ReadByte();
            if ((uint)id >= objects.Length || objects[id] is not ItemInstanceEntity item || hidden > 1
                || !seen.Add(id) || !identities.Add(key) || !ValidPickupItem(key, item, state))
                throw new InvalidDataException("Invalid replay pickup item appendix.");
            rows.Add((item, key, hidden != 0));
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing replay pickup item appendix data.");
        foreach (var item in objects.OfType<ItemInstanceEntity>())
        { item.ModPickupSpawn = null; item.ModPickupHidden = false; }
        foreach (var row in rows) { row.Item.ModPickupSpawn = row.Key; row.Item.ModPickupHidden = row.Hidden; }
    }

    private static object Create(Type type, byte[] data, Scene scene)
    {
        using var stream = new MemoryStream(data, writable: false); using var reader = new BinaryReader(stream);
        if (type == typeof(ModelInstance)) return scene.GetModelInstance(reader.ReadString(), reader.ReadBoolean());
        if (type == typeof(WeaponInfo))
        {
            int family = reader.ReadInt32(), index = reader.ReadInt32();
            if ((uint)family >= WeaponsByFamily.Length || (uint)index >= WeaponsByFamily[family].Count)
                throw new InvalidDataException("Unknown replay weapon asset.");
            return WeaponsByFamily[family][index];
        }
        if (type.IsArray)
        {
            var lengths = new int[type.GetArrayRank()]; int total = 1;
            for (int i = 0; i < lengths.Length; i++) { lengths[i] = Count(reader, MaximumObjects); total = checked(total * lengths[i]); }
            if (total > MaximumObjects) throw new InvalidDataException("Replay array too large.");
            return Array.CreateInstance(Element(type), lengths);
        }
        if (type == typeof(ItemInstanceEntity))
        {
            var item = (ItemType)reader.ReadInt32();
            if (!Enum.IsDefined(item)) throw new InvalidDataException("Unknown replay pickup type.");
            return new ItemInstanceEntity(new ItemInstanceEntityData(Vector3.Zero, item, -1), default, scene);
        }
        if (type == typeof(BeamProjectileEntity)) return new BeamProjectileEntity(scene);
        if (type == typeof(BombEntity)) return new BombEntity(scene);
        if (type == typeof(BeamEffectEntity)) return new BeamEffectEntity(scene);
        if (type == typeof(HalfturretEntity))
        {
            int slot = reader.ReadInt32(); if ((uint)slot >= PlayerEntity.SlotCapacity) throw new InvalidDataException("Invalid turret owner.");
            var turret = new HalfturretEntity(scene.Players.Items[slot], scene); turret.Create(); return turret;
        }
        if (typeof(EntityBase).IsAssignableFrom(type) || type == typeof(Scene) || type == typeof(SceneGameState)
            || type == typeof(ReplaySceneServices) || type == typeof(PlayerReplicationBridge))
            throw new InvalidDataException($"Missing construction anchor for {type.Name}.");
        return Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidDataException($"Cannot construct replay value {type.Name}.");
    }

    internal static void WriteValue(BinaryWriter writer, Type type, object? value, Func<object?, int> reference)
    {
        if (type == typeof(object))
        {
            Type actual = value?.GetType() ?? typeof(int);
            if (!Types.ContainsKey(actual.ToString())) throw new InvalidDataException($"Unsupported replay message parameter {actual}.");
            writer.Write(actual.ToString()); WriteValue(writer, actual, value ?? 0, reference); return;
        }
        if (Nullable.GetUnderlyingType(type) is Type underlying)
        { writer.Write(value != null); if (value != null) WriteValue(writer, underlying, value, reference); return; }
        if (type == typeof(string)) { writer.Write(value != null); if (value != null) writer.Write((string)value); return; }
        if (type.IsEnum) { WriteValue(writer, Enum.GetUnderlyingType(type), Convert.ChangeType(value, Enum.GetUnderlyingType(type)), reference); return; }
        if (type == typeof(bool)) writer.Write((bool)value!);
        else if (type == typeof(byte)) writer.Write((byte)value!);
        else if (type == typeof(sbyte)) writer.Write((sbyte)value!);
        else if (type == typeof(short)) writer.Write((short)value!);
        else if (type == typeof(ushort)) writer.Write((ushort)value!);
        else if (type == typeof(int)) writer.Write((int)value!);
        else if (type == typeof(uint)) writer.Write((uint)value!);
        else if (type == typeof(long)) writer.Write((long)value!);
        else if (type == typeof(ulong)) writer.Write((ulong)value!);
        else if (type == typeof(float)) writer.Write((float)value!);
        else if (type == typeof(double)) writer.Write((double)value!);
        else if (type == typeof(char)) writer.Write((ushort)(char)value!);
        else if (type.IsValueType) foreach (var field in ValueFields(type)) WriteValue(writer, field.FieldType, field.GetValue(value), reference);
        else writer.Write(reference(value));
    }
    private static object? ReadValue(BinaryReader reader, Type type, Func<int, Type, object?> reference)
    {
        if (type == typeof(object))
        {
            if (!Types.TryGetValue(reader.ReadString(), out Type? actual) || actual == typeof(object))
                throw new InvalidDataException("Unsupported replay message parameter.");
            return ReadValue(reader, actual, reference);
        }
        if (Nullable.GetUnderlyingType(type) is Type underlying) return reader.ReadBoolean() ? ReadValue(reader, underlying, reference) : null;
        if (type == typeof(string)) return reader.ReadBoolean() ? reader.ReadString() : null;
        if (type.IsEnum) return Enum.ToObject(type, ReadValue(reader, Enum.GetUnderlyingType(type), reference)!);
        if (type == typeof(bool)) return reader.ReadBoolean();
        if (type == typeof(byte)) return reader.ReadByte();
        if (type == typeof(sbyte)) return reader.ReadSByte();
        if (type == typeof(short)) return reader.ReadInt16();
        if (type == typeof(ushort)) return reader.ReadUInt16();
        if (type == typeof(int)) return reader.ReadInt32();
        if (type == typeof(uint)) return reader.ReadUInt32();
        if (type == typeof(long)) return reader.ReadInt64();
        if (type == typeof(ulong)) return reader.ReadUInt64();
        if (type == typeof(float)) return reader.ReadSingle();
        if (type == typeof(double)) return reader.ReadDouble();
        if (type == typeof(char)) return (char)reader.ReadUInt16();
        if (!type.IsValueType) return reference(reader.ReadInt32(), type);
        object value = Activator.CreateInstance(type)!;
        foreach (var field in ValueFields(type)) field.SetValue(value, ReadValue(reader, field.FieldType, reference));
        return value;
    }
    private static int Count(BinaryReader reader, int limit)
    { int count = reader.ReadInt32(); return count >= 0 && count <= limit ? count : throw new InvalidDataException("Replay length exceeds its bound."); }
    private static byte[] ReadBytes(BinaryReader reader, int limit)
    { int count = Count(reader, limit); byte[] bytes = reader.ReadBytes(count); return bytes.Length == count ? bytes : throw new EndOfStreamException(); }
    private static void WriteBytes(BinaryWriter writer, ReadOnlySpan<byte> bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
}
