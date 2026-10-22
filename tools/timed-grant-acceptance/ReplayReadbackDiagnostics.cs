using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;

// Value-only receipts, including exact scalar float bits. No state is restored,
// normalized or adjusted to make readbacks match.
internal static class ReplayReadbackDiagnostics
{
    private static object? Leaf(object? value)
    {
        if (value == null) return null;
        Type type = value.GetType();
        if (value is float single) return new { value = single.ToString("R", System.Globalization.CultureInfo.InvariantCulture), bits = BitConverter.SingleToInt32Bits(single).ToString("X8") };
        if (value is double number) return new { value = number.ToString("R", System.Globalization.CultureInfo.InvariantCulture), bits = BitConverter.DoubleToInt64Bits(number).ToString("X16") };
        if (type.IsEnum) return value.ToString();
        if (type.IsPrimitive || value is string || value is decimal) return value;
        if (type.IsValueType) return Fields(value);
        return null;
    }
    private static SortedDictionary<string, object?> Fields(object value)
    {
        var result = new SortedDictionary<string, object?>();
        for (Type? type = value.GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(Reflection.All | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic))
            {
                object? sample = field.GetValue(value);
                if (sample == null || sample is string || field.FieldType.IsValueType)
                    result[type.Name + "." + field.Name] = Leaf(sample);
                else if (sample is Array array && array.Length <= 2048 && array.GetType().GetElementType()!.IsValueType)
                    result[type.Name + "." + field.Name] = array.Cast<object>().Select(Leaf).ToArray();
            }
        return result;
    }
    private static object Model(ModelInstance model) => new { name = model.Model.Name,
        state = Fields(model), animation = Fields(model.AnimInfo),
        materials = model.Model.Materials.Select(Fields).ToArray() };
    private static object Actor(PlayerEntity player)
    {
        var models = new List<object>();
        if (Reflection.Get(player, "_models") is IEnumerable values)
            foreach (var item in values) if (item is ModelInstance model) models.Add(Model(model));
        return new { player.SlotIndex, native = Fields(player), camera = Fields(player.CameraInfo), models };
    }
    internal static void OriginalOpaqueSequence(Scene scene, string label, Acceptance.Report report)
    {
        if (!report.OriginalOpaqueOrderCounterfactual) return;
        object world = Reflection.Get(scene, "_retainedRenderWorld")!;
        var packets = (IList)Reflection.Get(world, "_opaque")!;
        var batches = (IList)Reflection.Get(world, "_opaqueBatches")!;
        object[] original = packets.Cast<object>().OrderBy(packet => Reflection.Int(packet, "Sequence")).ToArray();
        int[] before = packets.Cast<object>().Select(packet => Reflection.Int(packet, "Sequence")).ToArray();
        if (!original.Select(packet => Reflection.Int(packet, "Sequence")).SequenceEqual(Enumerable.Range(0, original.Length)))
            throw new InvalidOperationException("Counterfactual native opaque packet sequence is incomplete.");
        packets.Clear(); foreach (object packet in original) packets.Add(packet);
        batches.Clear();
        Reflection.Call(world.GetType(), "BuildAdjacentBatches", packets, batches,
            Reflection.Bool(world.GetType(), "RoomGroupsEnabled"));
        ((IList)Reflection.Get(scene, "_roomBatchPlans")!).Clear();
        report.Stages.Add(new { stage = "counterfactual-only-native-opaque-sequence", label,
            acceptance = false, before, after = Enumerable.Range(0, original.Length).ToArray(), batches = batches.Count });
    }
    internal static void Save(object replay, string label, Acceptance.Report report)
    {
        Scene scene = (Scene)Reflection.Get(replay, "Scene")!;
        object session = Reflection.Get(replay, "Session")!;
        var effects = new List<object>();
        if (Reflection.Get(scene, "_activeElements") is IEnumerable active)
            foreach (var element in active)
            {
                var particles = new List<object>();
                if (Reflection.Get(element!, "Particles") is IEnumerable entries)
                    foreach (var particle in entries) particles.Add(Fields(particle!));
                effects.Add(new { native = Fields(element!), particles });
            }
        var entities = new List<object>();
        foreach (var entity in scene.Entities)
        {
            var models = new List<object>();
            if (Reflection.Get(entity, "_models") is IEnumerable entries)
                foreach (var entry in entries) if (entry is ModelInstance model) models.Add(Model(model));
            entities.Add(new { entity.Id, type = entity.GetType().FullName, native = Fields(entity), models });
        }
        var renderItems = new SortedDictionary<string, object?>();
        foreach (string field in new[] { "_usedRenderItems", "_nonDecalItems", "_decalItems", "_translucentItems" })
            if (Reflection.Get(scene, field) is IEnumerable items)
                renderItems[field] = items.Cast<object>().Select(Fields).ToArray();
        var retained = new SortedDictionary<string, object?>();
        object world = Reflection.Get(scene, "_retainedRenderWorld")!;
        foreach (string field in new[] { "_opaque", "_decals", "_translucent" })
            if (Reflection.Get(world, field) is IEnumerable items)
                retained[field] = items.Cast<object>().Select(packet => new {
                    packet = Fields(packet), item = Fields(Reflection.Get(packet, "Item")!) }).ToArray();
        string path = Path.Combine(report.Output, label + "-native-state.json");
        var state = new { label, frame = Reflection.Get(session, "CurrentFrame"), recordingFrame = Reflection.Get(session, "RecordingFrame"),
            scene = Fields(scene), random = Fields(scene.Random), room = scene.Room == null ? null : Fields(scene.Room),
            actors = scene.Players.Items.Select(Actor).ToArray(), entities, effects, renderItems, retained };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(path, bytes);
        report.Stages.Add(new { stage = "native-private-replay-state", label, path,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)), bytes = bytes.Length });
        // Preserve full actual checkpoint bytes as an additional visual-state
        // falsifier. The checkpoint is captured and disposed, never restored.
        object checkpoint = Reflection.Call(Reflection.Type("Mods.Replay.ReplayWorldCheckpoint"), "Capture", replay, null, true)!;
        try
        {
            object payload = Reflection.Get(checkpoint, "Payload")!;
            using var stream = (MemoryStream)Reflection.Call(payload, "OpenRead")!;
            byte[] native = stream.ToArray();
            string checkpointPath = Path.Combine(report.Output, label + "-native-checkpoint.bin");
            File.WriteAllBytes(checkpointPath, native);
            report.Stages.Add(new { stage = "native-private-replay-checkpoint", label, path = checkpointPath,
                sha256 = Convert.ToHexString(SHA256.HashData(native)), bytes = native.Length });
        }
        finally { ((IDisposable)checkpoint).Dispose(); }
    }
}
