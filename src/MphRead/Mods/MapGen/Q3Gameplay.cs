using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>Prime gameplay adaptation shared by preflight, conversion and legacy BSP imports.</summary>
public static class Q3Gameplay
{
    public static bool IsPlayerStart(string classname) => classname.ToLowerInvariant() is
        "info_player_start" or "info_player_deathmatch" or "team_ctf_redplayer"
        or "team_ctf_blueplayer" or "team_ctf_redspawn" or "team_ctf_bluespawn";

    public static bool TryVector(string? text, out float[] value)
    {
        value = new float[3];
        var parts = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts?.Length != 3) return false;
        for (int i = 0; i < 3; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value[i])
                || !float.IsFinite(value[i])) return false;
        return true;
    }

    public static IReadOnlyList<MapSpawn> Spawns(Q3Bsp bsp, float unitsPerUnit)
    {
        if (!float.IsFinite(unitsPerUnit) || unitsPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerUnit), "Import scale must be finite and positive.");
        var result = new List<MapSpawn>();
        foreach (var entity in bsp.Entities)
        {
            if (!IsPlayerStart(entity.GetValueOrDefault("classname") ?? "")
                || !TryVector(entity.GetValueOrDefault("origin"), out var origin)) continue;
            float angle = 0;
            if (TryVector(entity.GetValueOrDefault("angles"), out var angles)) angle = angles[1];
            else if (float.TryParse(entity.GetValueOrDefault("angle"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float yaw) && float.IsFinite(yaw)) angle = yaw;
            var position = new[] { origin[0] / unitsPerUnit, (origin[2] - 24) / unitsPerUnit, -origin[1] / unitsPerUnit };
            if (position.Any(v => !float.IsFinite(v) || Math.Abs(v) >= 524288)) continue;
            // CTF start/respawn entities can occupy the same spot. Prime uses neutral starts
            // by default; team assignments remain an explicit authoring choice.
            if (result.Any(s => s.Position.SequenceEqual(position))) continue;
            result.Add(new MapSpawn { Position = position, Yaw = (90 + angle % 360 + 360) % 360 });
        }
        return result;
    }

    public static IReadOnlyList<MapDiagnostic> Inspect(Q3Bsp bsp, float unitsPerUnit)
    {
        var result = new List<MapDiagnostic>();
        void Warn(string message) => result.Add(new("FP-MAP-023", MapDiagnosticSeverity.Warning, message));
        int starts = Spawns(bsp, unitsPerUnit).Count;
        if (starts == 0) Warn("No usable source player starts were found. Add Prime spawns before playtesting; script targets and spectator cameras are not safe spawn locations.");
        else if (starts < 4) Warn($"Source has only {starts} player start(s). If using these starts alone, add well-spaced Prime spawns before hosting larger matches.");
        int invalidStarts = bsp.Entities.Count(e => IsPlayerStart(e.GetValueOrDefault("classname") ?? ""))
            - bsp.Entities.Count(e => IsPlayerStart(e.GetValueOrDefault("classname") ?? "") && TryVector(e.GetValueOrDefault("origin"), out _));
        if (invalidStarts > 0) Warn($"Skipped {invalidStarts} player start(s) with missing or invalid coordinates.");
        foreach (var group in bsp.Entities.GroupBy(e => (e.GetValueOrDefault("classname") ?? "").ToLowerInvariant()))
        {
            string name = group.Key;
            if (name is "trigger_teleport" or "target_teleporter")
                Warn($"{group.Count()} {name} entity/entities need a Prime traversal replacement. Quake teleporters are not imported; check that all areas remain reachable.");
            else if (name.StartsWith("func_", StringComparison.Ordinal) && name != "func_group")
                Warn($"{group.Count()} {name} entity/entities are not simulated. Their rendered surfaces may remain, but movement and brush-model collision are not imported. Replace essential doors, lifts or platforms with Prime geometry/routes.");
            else if ((name.StartsWith("trigger_", StringComparison.Ordinal) && name != "trigger_push")
                || (name.StartsWith("target_", StringComparison.Ordinal) && name is not ("target_position" or "target_location"))
                || name.StartsWith("shooter_", StringComparison.Ordinal))
                Warn($"{group.Count()} {name} entity/entities have no Prime gameplay behavior. Replace required scripted actions or hazards before playtesting.");
        }
        var targets = bsp.Entities.Where(e => e.ContainsKey("targetname") && TryVector(e.GetValueOrDefault("origin"), out _))
            .Select(e => e["targetname"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int brokenPads = bsp.Entities.Count(e => string.Equals(e.GetValueOrDefault("classname"), "trigger_push", StringComparison.OrdinalIgnoreCase)
            && (!targets.Contains(e.GetValueOrDefault("target") ?? "")
                || !TryModel(e.GetValueOrDefault("model"), bsp.Models.Count, out _)));
        if (brokenPads > 0) Warn($"{brokenPads} jump pad(s) have no valid trigger model or target and cannot be imported. Add or repair their Prime jump pads.");
        return result;
    }

    public static bool TryModel(string? text, int modelCount, out int index)
    {
        index = -1;
        return text?.StartsWith('*') == true && int.TryParse(text.AsSpan(1), NumberStyles.None,
            CultureInfo.InvariantCulture, out index) && index > 0 && index < modelCount;
    }
}
