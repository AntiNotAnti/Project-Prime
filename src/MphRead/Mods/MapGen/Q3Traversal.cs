using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>Version-three map traversal metadata in Prime world units. Absent on legacy maps.</summary>
public sealed class MapQ3Trigger
{
    public string Kind { get; set; } = "push";
    public int SourceEntity { get; set; }
    public float[] Min { get; set; } = new float[3];
    public float[] Max { get; set; } = new float[3];
    public float[] Destination { get; set; } = new float[3];
    public float Yaw { get; set; }
    /// <summary>World units per second squared, independent of hunter and form.</summary>
    public float Gravity { get; set; }
    public int CooldownFrames { get; set; } = 30;
}

public static class Q3Traversal
{
    private sealed record SceneTriggers(string Room, IReadOnlyList<MapQ3Trigger> Triggers);
    private static readonly ConditionalWeakTable<Scene, SceneTriggers> Scenes = new();
    public static void Bind(Scene scene, string room)
    {
        var definition = CustomRooms.Definitions.FirstOrDefault(d => d.Name == room);
        Bind(scene, room, definition?.Q3Triggers ?? (IReadOnlyList<MapQ3Trigger>)Array.Empty<MapQ3Trigger>());
    }
    internal static void Bind(Scene scene, string room, IReadOnlyList<MapQ3Trigger> source)
    {
        var triggers = source.Select(t => new MapQ3Trigger {
            Kind = t.Kind, SourceEntity = t.SourceEntity, Min = (float[])t.Min.Clone(), Max = (float[])t.Max.Clone(),
            Destination = (float[])t.Destination.Clone(), Yaw = t.Yaw, Gravity = t.Gravity,
            CooldownFrames = t.CooldownFrames }).ToArray();
        Scenes.Remove(scene); Scenes.Add(scene, new(room, triggers));
    }
    internal static IReadOnlyList<MapQ3Trigger> ForScene(Scene scene)
    {
        string room = scene.Room?.Meta.Name ?? "";
        if (!Scenes.TryGetValue(scene, out var snapshot) || snapshot.Room != room)
        { Bind(scene, room); snapshot = Scenes.GetValue(scene, _ => new(room, Array.Empty<MapQ3Trigger>())); }
        return snapshot.Triggers;
    }
    public static void Initialize(MapDefinition definition)
    {
        if (definition.Import is not {} import) return;
        string source = import.Resolve() ?? throw new InvalidDataException("Missing Q3 traversal source.");
        definition.Q3Triggers = Extract(Q3Bsp.Load(source, import.MapName), import.UnitsPerUnit).ToList();
        definition.FormatVersion = 3;
    }

    public static IReadOnlyList<MapQ3Trigger> Extract(Q3Bsp bsp, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0 || !float.IsFinite(800 / scale))
            throw new ArgumentOutOfRangeException(nameof(scale));
        float[] World(float[] p) => new[] { p[0] / scale, p[2] / scale, -p[1] / scale };
        var result = new List<MapQ3Trigger>();
        for (int i = 0; i < bsp.Entities.Count; i++)
        {
            var entity = bsp.Entities[i];
            string name = entity.GetValueOrDefault("classname") ?? "";
            if (name is not ("trigger_push" or "trigger_teleport")) continue;
            if (!Q3Gameplay.TryModel(entity.GetValueOrDefault("model"), bsp.Models.Count, out int model)) continue;
            string target = entity.GetValueOrDefault("target") ?? "";
            if (target.Length == 0) continue;
            // Quake targetnames are case-sensitive. Ambiguous destinations are not randomised:
            // source order selects the first usable destination on every client and replay.
            var destination = bsp.Entities.FirstOrDefault(e => e.GetValueOrDefault("targetname") == target
                && Q3Gameplay.TryVector(e.GetValueOrDefault("origin"), out _));
            if (destination == null || !Q3Gameplay.TryVector(destination.GetValueOrDefault("origin"), out var origin)) continue;
            var bounds = bsp.Models[model];
            var a = World(bounds.Mins); var b = World(bounds.Maxs);
            var trigger = new MapQ3Trigger { Kind = name == "trigger_push" ? "push" : "teleport", SourceEntity = i,
                Min = Enumerable.Range(0, 3).Select(n => Math.Min(a[n], b[n])).ToArray(),
                Max = Enumerable.Range(0, 3).Select(n => Math.Max(a[n], b[n])).ToArray(),
                Destination = World(origin), Gravity = 800 / scale };
            float yaw = 0;
            if (Q3Gameplay.TryVector(destination.GetValueOrDefault("angles"), out var angles)) yaw = angles[1];
            else float.TryParse(destination.GetValueOrDefault("angle"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out yaw);
            trigger.Yaw = float.IsFinite(yaw) ? (90 + yaw % 360 + 360) % 360 : 90;
            if (trigger.Kind == "teleport") trigger.Destination[1] -= 24 / scale;
            if (trigger.Kind == "push" && !TryLaunch(trigger, out _)) continue;
            if (Valid(trigger)) result.Add(trigger);
        }
        return result;
    }

    public static bool Valid(MapQ3Trigger t) => t != null && t.Kind is "push" or "teleport"
        && t.SourceEntity is >= 0 and <= 32767 && Vector(t.Min) && Vector(t.Max) && Vector(t.Destination)
        && Enumerable.Range(0, 3).All(i => t.Min[i] < t.Max[i])
        && float.IsFinite(t.Yaw) && t.Yaw >= 0 && t.Yaw < 360
        && float.IsFinite(t.Gravity) && t.Gravity > 0 && t.Gravity <= 10000
        && t.CooldownFrames is >= 1 and <= 600;
    private static bool Vector(float[] p) => p?.Length == 3 && p.All(v => float.IsFinite(v) && Math.Abs(v) < 524288);
    public static Vector3 ToVector(float[] p) => new(p[0], p[1], p[2]);
    public static bool Contact(MapQ3Trigger trigger, Vector3 feet)
        => feet.X >= trigger.Min[0] && feet.X <= trigger.Max[0]
        && feet.Y >= trigger.Min[1] && feet.Y <= trigger.Max[1]
        && feet.Z >= trigger.Min[2] && feet.Z <= trigger.Max[2];
    public static bool Contact(MapQ3Trigger trigger, Vector3 previousFeet, Vector3 feet)
    {
        float enter = 0, leave = 1;
        Vector3 delta = feet - previousFeet;
        for (int axis = 0; axis < 3; axis++)
        {
            float start = previousFeet[axis], movement = delta[axis];
            if (MathF.Abs(movement) < 0.000001f)
            { if (start < trigger.Min[axis] || start > trigger.Max[axis]) return false; continue; }
            float a = (trigger.Min[axis] - start) / movement, b = (trigger.Max[axis] - start) / movement;
            enter = Math.Max(enter, Math.Min(a, b)); leave = Math.Min(leave, Math.Max(a, b));
            if (enter > leave) return false;
        }
        return true;
    }
    /// <summary>Quake target_position is the apex. Returned velocity uses Prime's 30 Hz speed units.</summary>
    public static bool TryLaunch(MapQ3Trigger trigger, out Vector3 velocity)
    {
        velocity = default;
        if (!Valid(trigger)) return false;
        Vector3 center = (ToVector(trigger.Min) + ToVector(trigger.Max)) / 2;
        Vector3 delta = ToVector(trigger.Destination) - center;
        if (delta.Y <= 0) return false;
        float seconds = MathF.Sqrt(2 * delta.Y / trigger.Gravity);
        velocity = new Vector3(delta.X / seconds, trigger.Gravity * seconds, delta.Z / seconds) / 30;
        return float.IsFinite(velocity.X) && float.IsFinite(velocity.Y) && float.IsFinite(velocity.Z)
            && velocity.LengthSquared <= 128 * 128;
    }
}
