using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapEditor;

public enum NativeMapPropertyKind { Text, Number, Integer, Boolean, Choice }

public sealed record MapObjectPropertyField(string Id, string Label, NativeMapPropertyKind Kind,
    string Value, int MaxLength = 256, double? Min = null, double? Max = null,
    IReadOnlyList<string>? Choices = null);

/// <summary>Bounded, explicit authoring properties. Values and choices are detached from the document.</summary>
public static class MapObjectPropertySchema
{
    private sealed record Binding(MapObjectPropertyField Field, object Value, Action<object, object>? Set);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const double PositionLimit = 524287.96875;

    public static IReadOnlyList<MapObjectPropertyField> Read(MapDefinition definition, Guid id)
        => Array.AsReadOnly(Build(definition, id).Select(b => b.Field).ToArray());

    /// <summary>Validate the entire patch before issuing one ordinary object-history command.</summary>
    public static void Apply(MapDocument document, Guid id, IReadOnlyDictionary<string, string> patch)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(patch);
        // EditObjects supplies only selected objects. Global material bounds must be captured here.
        var bindings = Build(document.Project.Definition, id);
        var byId = bindings.ToDictionary(b => b.Field.Id, StringComparer.Ordinal);
        var values = bindings.ToDictionary(b => b.Field.Id, b => b.Value, StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in patch)
        {
            if (!byId.TryGetValue(pair.Key, out var binding))
                throw new FormatException("Unknown property: " + pair.Key);
            object value = Parse(binding.Field, pair.Value);
            values[pair.Key] = value;
            if (!Equals(value, binding.Value)) changed.Add(pair.Key);
        }
        object original = MapObjects.Find(document.Project.Definition, id)!.Value;
        bool Has(string prefix) => changed.Any(key => key.StartsWith(prefix + ".", StringComparison.Ordinal));
        if (original is MapMesh sourceMesh && (changed.Contains("material") || Has("uvScale") || Has("uvOffset") || changed.Contains("uvRotation")))
            MapMeshEditing.RequireEditableSourceChannels(sourceMesh);
        float[] Vector(string prefix, int count = 3) => Enumerable.Range(0, count)
            .Select(i => (float)values[prefix + "." + "xyz"[i]]).ToArray();
        if (original is MapBrush && (Has("minimum") || Has("maximum")))
        {
            var min = Vector("minimum"); var max = Vector("maximum");
            if (min.Zip(max, (a, b) => a == b).Any(equal => equal))
                throw new FormatException("Brush dimensions cannot be zero.");
        }
        if (original is MapJumpPad && (changed.Contains("launchMode") || changed.Contains("speed") || Has("direction")))
        {
            if ((string)values["launchMode"] == "Vector and speed"
                && (Vector("direction").All(v => v == 0) || (float)values["speed"] <= 0))
                throw new FormatException("Vector launch requires a nonzero direction and positive speed.");
        }
        if (changed.Count == 0) return;
        document.EditObjects("Edit properties", new[] { id }, scratch =>
        {
            object target = MapObjects.Find(scratch, id)?.Value
                ?? throw new InvalidOperationException("The selected object no longer exists.");
            if (Has("position"))
            {
                var current = new MapObject(id, "", "", target, _ => { });
                var position = Vector("position");
                current.Move(position.Zip(current.Position, (a, b) => a - b).ToArray());
            }
            foreach (var binding in bindings)
                if (changed.Contains(binding.Field.Id)) binding.Set?.Invoke(target, values[binding.Field.Id]);
            if (target is MapGeometry geometry && Has("rotation"))
            {
                float[] angles = Vector("rotation");
                var q = Quaternion.FromEulerAngles(new Vector3(angles[0], angles[1], angles[2]) * (MathF.PI / 180));
                geometry.Transform.Rotation = new[] { q.X, q.Y, q.Z, q.W };
            }
            if (target is MapNavigationLink link && Has("destination")) link.To = Vector("destination");
            if (target is MapJumpPad pad
                && (changed.Contains("launchMode") || Has("target") || Has("direction")))
            {
                bool useTarget = (string)values["launchMode"] == "Target";
                // An inactive page value is only a display default, not an authored channel.
                if (useTarget && (changed.Contains("launchMode") || Has("target")))
                { pad.Target = Vector("target"); pad.Vector = null; }
                if (!useTarget && (changed.Contains("launchMode") || Has("direction")))
                { pad.Vector = Vector("direction"); pad.Target = null; }
            }
            if (target is MapMesh mesh && changed.Contains("collisionOnly") && mesh.CollisionOnly) mesh.Solid = true;
        });
    }

    private static object Parse(MapObjectPropertyField field, string text)
    {
        if (text == null || text.Length > field.MaxLength || text.Any(char.IsControl))
            throw new FormatException(field.Label + " exceeds its supported text format.");
        object value;
        switch (field.Kind)
        {
            case NativeMapPropertyKind.Text: return text;
            case NativeMapPropertyKind.Boolean:
                if (!bool.TryParse(text, out bool flag)) throw new FormatException(field.Label + " must be true or false.");
                return flag;
            case NativeMapPropertyKind.Choice:
                string? choice = field.Choices?.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                return choice ?? throw new FormatException(field.Label + " is not a supported choice.");
            case NativeMapPropertyKind.Integer:
                if (!int.TryParse(text, NumberStyles.AllowLeadingSign, Invariant, out int integer))
                    throw new FormatException(field.Label + " must be an invariant integer.");
                value = integer; break;
            case NativeMapPropertyKind.Number:
                if (!float.TryParse(text, NumberStyles.Float, Invariant, out float number) || !float.IsFinite(number))
                    throw new FormatException(field.Label + " must be a finite invariant number.");
                value = number; break;
            default: throw new FormatException("Unsupported property type.");
        }
        double numeric = Convert.ToDouble(value, Invariant);
        if (field.Min.HasValue && numeric < field.Min.Value || field.Max.HasValue && numeric > field.Max.Value)
            throw new FormatException(field.Label + " is outside its supported range.");
        return value;
    }

    private static List<Binding> Build(MapDefinition definition, Guid id)
    {
        ArgumentNullException.ThrowIfNull(definition);
        object value = MapObjects.Find(definition, id)?.Value
            ?? throw new InvalidOperationException("The selected object no longer exists.");
        var result = new List<Binding>();
        void Add(string key, string label, NativeMapPropertyKind kind, object current,
            Action<object, object>? set = null, double? min = null, double? max = null,
            IEnumerable<string>? choices = null, int length = 64)
        {
            var options = choices == null ? null : Array.AsReadOnly(choices.ToArray());
            string text = current is bool flag ? (flag ? "true" : "false")
                : current is float number ? number.ToString("R", Invariant) : Convert.ToString(current, Invariant) ?? "";
            result.Add(new(new(key, label, kind, text, length, min, max, options), current, set));
        }
        void Text(string key, string label, string? current, Action<object, string> set)
            => Add(key, label, NativeMapPropertyKind.Text, current ?? "", (o, v) => set(o, (string)v), length: 256);
        void Number(string key, string label, float current, Action<object, float>? set = null,
            double min = -float.MaxValue, double max = float.MaxValue)
            => Add(key, label, NativeMapPropertyKind.Number, current,
                set == null ? null : (o, v) => set(o, (float)v), min, max);
        void Integer(string key, string label, int current, Action<object, int> set, int min, int max)
            => Add(key, label, NativeMapPropertyKind.Integer, current, (o, v) => set(o, (int)v), min, max);
        void Flag(string key, string label, bool current, Action<object, bool> set)
            => Add(key, label, NativeMapPropertyKind.Boolean, current, (o, v) => set(o, (bool)v));
        void Choice(string key, string label, string current, IEnumerable<string> choices, Action<object, string>? set = null)
            => Add(key, label, NativeMapPropertyKind.Choice, current, set == null ? null : (o, v) => set(o, (string)v), choices: choices);
        void Vec(string key, string label, float[] current, Func<object, float[]>? get = null,
            double min = -PositionLimit, double max = PositionLimit)
        {
            for (int i = 0; i < current.Length; i++)
            {
                int axis = i;
                Number(key + "." + "xyz"[axis], label + " " + char.ToUpperInvariant("xyz"[axis]), current[axis],
                    get == null ? null : (o, v) => get(o)[axis] = v, min, max);
            }
        }
        void Material(int current, Action<object, int> set)
            => Integer("material", "Material index", current, set, 0, definition.Materials.Count - 1);

        if (value is not MapBrush) Vec("position", "Position", MapObjects.Find(definition, id)!.Position);
        if (value is MapEntityDefinition entity) Text("label", "Label", entity.Label, (o, v) => ((MapEntityDefinition)o).Label = v);
        switch (value)
        {
            case MapGeometry g:
                Text("label", "Label", g.Label, (o, v) => ((MapGeometry)o).Label = v);
                Text("layer", "Layer", g.Layer, (o, v) => ((MapGeometry)o).Layer = v);
                Vec("size", "Size", g.Transform.Scale, o => ((MapGeometry)o).Transform.Scale, float.Epsilon, float.MaxValue);
                var rotation = new Quaternion(g.Transform.Rotation[0], g.Transform.Rotation[1], g.Transform.Rotation[2], g.Transform.Rotation[3])
                    .ToEulerAngles() * (180 / MathF.PI);
                Vec("rotation", "Rotation degrees", new[] { rotation.X, rotation.Y, rotation.Z }, min: -float.MaxValue, max: float.MaxValue);
                Material(g.Material, (o, v) => ((MapGeometry)o).Material = v);
                Number("shade", "Shade", g.Shade, (o, v) => ((MapGeometry)o).Shade = v, 0, 1);
                Choice("terrain", "Terrain", g.Terrain, Enum.GetNames<Terrain>(), (o, v) => ((MapGeometry)o).Terrain = v);
                Vec("uvScale", "UV scale", g.Uv.Scale, o => ((MapGeometry)o).Uv.Scale, float.Epsilon, float.MaxValue);
                Vec("uvOffset", "UV offset", g.Uv.Offset, o => ((MapGeometry)o).Uv.Offset, -float.MaxValue, float.MaxValue);
                Number("uvRotation", "UV rotation", g.Uv.Rotation, (o, v) => ((MapGeometry)o).Uv.Rotation = v);
                Flag("solid", "Collision", g.Solid, (o, v) => ((MapGeometry)o).Solid = v);
                Flag("damaging", "Damaging", g.Damaging, (o, v) => ((MapGeometry)o).Damaging = v);
                Flag("hidden", "Hidden", g.Hidden, (o, v) => ((MapGeometry)o).Hidden = v);
                Flag("locked", "Locked", g.Locked, (o, v) => ((MapGeometry)o).Locked = v);
                if (g is MapMesh mesh)
                {
                    Flag("collisionOnly", "Collision only", mesh.CollisionOnly, (o, v) => ((MapMesh)o).CollisionOnly = v);
                    Flag("reflectBeams", "Reflect beams", mesh.ReflectBeams, (o, v) => ((MapMesh)o).ReflectBeams = v);
                    Flag("ignorePlayers", "Ignore players and camera", mesh.IgnorePlayers, (o, v) => ((MapMesh)o).IgnorePlayers = v);
                    Flag("ignoreBeams", "Ignore beams", mesh.IgnoreBeams, (o, v) => ((MapMesh)o).IgnoreBeams = v);
                    Flag("ignoreScan", "Ignore scan", mesh.IgnoreScan, (o, v) => ((MapMesh)o).IgnoreScan = v);
                    Integer("slipperiness", "Slipperiness", mesh.Slipperiness, (o, v) => ((MapMesh)o).Slipperiness = v, 0, 3);
                }
                if (g is MapPrism prism) Integer("sides", "Prism sides", prism.Sides, (o, v) => ((MapPrism)o).Sides = v, 3, 32);
                break;
            case MapSpawn spawn:
                Number("yaw", "Yaw degrees", spawn.Yaw, (o, v) => ((MapSpawn)o).Yaw = v);
                Integer("team", "Team (-1 = neutral)", spawn.Team, (o, v) => ((MapSpawn)o).Team = v, -1, 3);
                break;
            case MapItem item:
                Choice("type", "Pickup type", item.Type, MapBuilder.MultiplayerItems.Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal), (o, v) => ((MapItem)o).Type = v);
                Integer("spawnInterval", "Respawn frames", item.SpawnInterval, (o, v) => ((MapItem)o).SpawnInterval = (ushort)v, 0, ushort.MaxValue);
                Flag("hasBase", "Has base", item.HasBase, (o, v) => ((MapItem)o).HasBase = v);
                break;
            case MapJumpPad pad:
                Vec("size", "Trigger size", pad.Size, o => ((MapJumpPad)o).Size, float.Epsilon);
                Choice("launchMode", "Launch mode", pad.Vector == null ? "Target" : "Vector and speed", new[] { "Target", "Vector and speed" });
                Vec("target", "Target", pad.Target ?? new[] { 0f, 4, 0 });
                Vec("direction", "Direction", pad.Vector ?? new[] { 0f, 1, 0 }, min: -float.MaxValue, max: float.MaxValue);
                Number("speed", "Speed", pad.Speed, (o, v) => ((MapJumpPad)o).Speed = v, 0);
                Integer("controlLockTime", "Control lock frames", pad.ControlLockTime, (o, v) => ((MapJumpPad)o).ControlLockTime = (ushort)v, 0, ushort.MaxValue);
                Integer("cooldownTime", "Cooldown frames", pad.CooldownTime, (o, v) => ((MapJumpPad)o).CooldownTime = (ushort)v, 0, ushort.MaxValue);
                break;
            case MapNavigationLink link:
                Vec("destination", "Destination", link.To);
                Choice("kind", "Traversal type", link.Kind.ToString(), Enum.GetNames<MapNavigationLinkKind>(), (o, v) => ((MapNavigationLink)o).Kind = Enum.Parse<MapNavigationLinkKind>(v));
                Choice("fromNodeKind", "From node type", link.FromNodeKind.ToString(), Enum.GetNames<MapNavigationAnchorKind>(), (o, v) => ((MapNavigationLink)o).FromNodeKind = Enum.Parse<MapNavigationAnchorKind>(v));
                Choice("toNodeKind", "To node type", link.ToNodeKind.ToString(), Enum.GetNames<MapNavigationAnchorKind>(), (o, v) => ((MapNavigationLink)o).ToNodeKind = Enum.Parse<MapNavigationAnchorKind>(v));
                Flag("bidirectional", "Bidirectional", link.Bidirectional, (o, v) => ((MapNavigationLink)o).Bidirectional = v);
                break;
            case MapBrush brush:
                Vec("minimum", "Minimum", brush.Min, o => ((MapBrush)o).Min);
                Vec("maximum", "Maximum", brush.Max, o => ((MapBrush)o).Max);
                Material(brush.Material, (o, v) => ((MapBrush)o).Material = v);
                break;
            default: throw new InvalidOperationException("Unsupported selected object type.");
        }
        return result;
    }
}
