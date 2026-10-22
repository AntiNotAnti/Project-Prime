using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
namespace MphRead.Mods.Training;
internal static class AimTrainerPersonalBests
{
    internal sealed record Result(int Score, double Accuracy, double HeadshotPercent, double ReactionMs, int Seed);
    internal static bool Save(AimTrainerDefinition d, TrainingInputSource input, AimTrainerStats stats)
    {
        string path = Path.Combine(LauncherPrefs.Directory, "aim-trainer.json");
        var values = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Result>>(File.ReadAllText(path)) ?? new() : new Dictionary<string, Result>();
        string key = $"v2:{d.NormalImperialistReload}:{d.Drill}:{d.Weapon}:{d.Difficulty}:{input}:{d.HeadshotsOnly}:{d.DurationSeconds}:{d.Movement}:{d.TargetCount}:{d.Scope}:{d.Distance}:{d.InfiniteAmmo}:{d.ReloadOnHit}";
        if (values.TryGetValue(key, out Result? best) && best != null && best.Score >= stats.Score) return false;
        values[key] = new(stats.Score, stats.Accuracy, stats.HeadshotPercent, stats.AverageReactionMs, unchecked((int)d.Seed));
        Directory.CreateDirectory(LauncherPrefs.Directory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
        return true;
    }
}
