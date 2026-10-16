using System;
using System.Collections.Generic;
using System.Globalization;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Bounded source-side Quake brush mover descriptors. These are reference data
/// only, NOT authoritative movers or automatically published map format fields.
/// Dynamic collision, model grouping, network/world snapshots and replay
/// reconstruction must exist before any descriptor can become live gameplay.
/// </summary>
internal enum Q3MoverKind { Door, Bobbing, Rotating, Pendulum }

internal readonly record struct Q3MoverDescriptor(
    Q3MoverKind Kind, int SourceEntity, int ModelIndex,
    Vector3 Minimum, Vector3 Maximum, Vector3 Direction,
    float Speed, float Distance, float Phase, float WaitSeconds)
{
    /// <summary>Deterministic 60-Hz reference trajectory, never used for gameplay.</summary>
    internal Vector3 ReferenceOffset(uint frame, uint activatedAt = 0)
    {
        float seconds = unchecked(frame - activatedAt) / 60f;
        if (seconds < 0 || seconds > 86400f) return Vector3.Zero;
        if (Kind == Q3MoverKind.Bobbing)
            return Direction * (Distance * MathF.Sin(2f * MathF.PI * (seconds / Speed + Phase)));
        if (Kind != Q3MoverKind.Door || Distance <= 0) return Vector3.Zero;
        float duration = Distance / Speed;
        if (duration <= 0) return Vector3.Zero;
        float t = seconds;
        if (t < duration) return Direction * (Distance * (t / duration));
        if (WaitSeconds < 0 || t < duration + WaitSeconds) return Direction * Distance;
        t -= duration + WaitSeconds;
        return Direction * (Distance * (1f - Math.Clamp(t / duration, 0f, 1f)));
    }

    internal float ReferenceAngleDegrees(uint frame)
    {
        float seconds = frame / 60f;
        return Kind switch
        {
            Q3MoverKind.Rotating => (Speed * seconds) % 360f,
            Q3MoverKind.Pendulum => Distance *
                MathF.Sin(2f * MathF.PI * (seconds / Speed + Phase)),
            _ => 0f
        };
    }
}

internal static class Q3MoverSource
{
    internal const int MaximumSourceMovers = 128;

    internal static bool TryRead(IReadOnlyDictionary<string, string> e, Q3Model model,
        int modelIndex, int sourceEntity, float unitsPerUnit,
        out Q3MoverDescriptor mover, out string problem)
    {
        mover = default;
        problem = "";
        if (!float.IsFinite(unitsPerUnit) || unitsPerUnit <= 0
            || modelIndex <= 0 || sourceEntity < 0)
        {
            problem = "Invalid source scale, model or entity index.";
            return false;
        }
        string name = e.GetValueOrDefault("classname") ?? "";
        Q3MoverKind kind;
        switch (name)
        {
            case "func_door": kind = Q3MoverKind.Door; break;
            case "func_bobbing": kind = Q3MoverKind.Bobbing; break;
            case "func_rotating": kind = Q3MoverKind.Rotating; break;
            case "func_pendulum": kind = Q3MoverKind.Pendulum; break;
            default: problem = "Unsupported source mover type."; return false;
        }
        bool Number(string key, float fallback, float minimum, float maximum, out float value)
        {
            string? raw = e.GetValueOrDefault(key);
            if (string.IsNullOrWhiteSpace(raw)) { value = fallback; return true; }
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && float.IsFinite(value) && value >= minimum && value <= maximum;
        }
        if (!Number("speed", kind == Q3MoverKind.Door ? 400 : kind == Q3MoverKind.Rotating ? 100 : 4,
                .01f, 4096f, out float speed)
            || !Number("height", kind == Q3MoverKind.Bobbing ? 32 : 0,
                0f, 16384f, out float height)
            || !Number("phase", 0, 0, 1, out float phase)
            || !Number("wait", 2, -1, 3600, out float wait)
            || !Number("angle", 0, -2, 360, out float angle))
        {
            problem = "A source mover parameter is nonfinite, out of range or malformed.";
            return false;
        }
        for (int i = 0; i < 3; i++)
        {
            if (!float.IsFinite(model.Mins[i]) || !float.IsFinite(model.Maxs[i])
                || model.Mins[i] > model.Maxs[i])
            {
                problem = "Invalid source brush-model bounds.";
                return false;
            }
        }
        static Vector3 World(float[] v, float scale)
            => new(v[0] / scale, v[2] / scale, -v[1] / scale);
        Vector3 a = World(model.Mins, unitsPerUnit), b = World(model.Maxs, unitsPerUnit);
        Vector3 min = Vector3.ComponentMin(a, b), max = Vector3.ComponentMax(a, b);
        if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(min.Z)
            || !float.IsFinite(max.X) || !float.IsFinite(max.Y) || !float.IsFinite(max.Z)
            || MathF.Max(MathF.Abs(min.X), MathF.Max(MathF.Abs(min.Y), MathF.Abs(min.Z))) >= 524288f
            || MathF.Max(MathF.Abs(max.X), MathF.Max(MathF.Abs(max.Y), MathF.Abs(max.Z))) >= 524288f)
        {
            problem = "Brush mover exceeds Project Prime coordinate bounds.";
            return false;
        }
        int flags = 0;
        if (e.TryGetValue("spawnflags", out string? text)
            && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags)
                || flags < 0 || flags > 65535))
        {
            problem = "Invalid Quake mover spawnflags.";
            return false;
        }
        Vector3 direction = kind == Q3MoverKind.Door
            ? angle == -1 ? Vector3.UnitY : angle == -2 ? -Vector3.UnitY
                : new Vector3(MathF.Cos(angle * MathF.PI / 180f), 0,
                    -MathF.Sin(angle * MathF.PI / 180f))
            : (flags & 1) != 0 ? Vector3.UnitX
                : (flags & 2) != 0 ? -Vector3.UnitZ
                : Vector3.UnitY;
        float distance = kind switch
        {
            Q3MoverKind.Door => MathF.Max(0,
                Vector3.Dot(max - min,
                    new Vector3(MathF.Abs(direction.X), MathF.Abs(direction.Y), MathF.Abs(direction.Z)))
                - 8 / unitsPerUnit),
            Q3MoverKind.Bobbing => height / unitsPerUnit,
            Q3MoverKind.Pendulum => height,
            _ => 0
        };
        if (!float.IsFinite(distance) || distance >= 524288f)
        {
            problem = "Mover trajectory exceeds runtime bounds.";
            return false;
        }
        float worldSpeed = kind == Q3MoverKind.Door ? speed / unitsPerUnit : speed;
        mover = new(kind, sourceEntity, modelIndex, min, max, direction, worldSpeed,
            distance, phase, wait);
        return true;
    }

    internal static IReadOnlyList<Q3MoverDescriptor> Extract(Q3Bsp bsp, float unitsPerUnit)
    {
        var movers = new List<Q3MoverDescriptor>();
        for (int i = 0; i < bsp.Entities.Count; i++)
        {
            var entity = bsp.Entities[i];
            if (!Q3Gameplay.TryModel(entity.GetValueOrDefault("model"), bsp.Models.Count, out int model))
                continue;
            if (TryRead(entity, bsp.Models[model], model, i, unitsPerUnit,
                    out var parsed, out _))
            {
                if (movers.Count == MaximumSourceMovers) break;
                movers.Add(parsed);
            }
        }
        return movers;
    }
}
