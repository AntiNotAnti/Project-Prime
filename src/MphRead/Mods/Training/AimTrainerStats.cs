using System;
using System.Collections.Generic;
namespace MphRead.Mods.Training;
public sealed class AimTrainerStats
{
    public int ElapsedFrames { get; internal set; }
    public double ElapsedTime => ElapsedFrames / 60.0;
    public int Score { get; internal set; }
    public int ShotsFired { get; internal set; }
    public int ShotsHit { get; internal set; }
    public int ShotsMissed { get; internal set; }
    public int BodyHits { get; internal set; }
    public int Headshots { get; internal set; }
    public double Accuracy => ShotsFired == 0 ? 0 : 100.0 * ShotsHit / ShotsFired;
    public double HeadshotPercent => BodyHits + Headshots == 0 ? 0 : 100.0 * Headshots / (BodyHits + Headshots);
    public int FirstShotAttempts { get; internal set; }
    public int FirstShotHits { get; internal set; }
    public double FirstShotAccuracy => FirstShotAttempts == 0 ? 0 : 100.0 * FirstShotHits / FirstShotAttempts;
    public List<int> ReacquisitionSamples { get; } = new();
    public double AverageReacquisitionMs => Average(ReacquisitionSamples);
    public List<int> LockSamples { get; } = new();
    public double AverageLockSeconds => Average(LockSamples) / 1000;
    public int DirectHits { get; internal set; }
    public int SplashHits { get; internal set; }
    public int ChargedShots { get; internal set; }
    public int ChargedHits { get; internal set; }
    public int FreezeHits { get; internal set; }
    public double DamagePotential { get; internal set; }
    public Dictionary<BeamType, AimTrainerStats> PerWeaponStats { get; } = new();
    public double DamageConnected { get; internal set; }
    public int TargetsSpawned { get; internal set; }
    public int TargetsExpired { get; internal set; }
    public int TargetsHit { get; internal set; }
    public List<float> AngularTransitions { get; } = new();
    public List<int> ReactionSamples { get; } = new();
    public List<int> AcquisitionSamples { get; } = new();
    private static double Average(List<int> values) { long total = 0; foreach (int v in values) total += v; return values.Count == 0 ? 0 : total * (1000.0 / 60) / values.Count; }
    public double AverageReactionMs => Average(ReactionSamples);
    public double AverageAcquisitionMs => Average(AcquisitionSamples);
    public double BestReactionMs => ReactionSamples.Count == 0 ? 0 : ReactionSamples.MinValue() * (1000.0 / 60);
    public int CurrentHitStreak { get; internal set; }
    public int LongestHitStreak { get; internal set; }
    public int TrackingFrames { get; internal set; }
    public int TrackingHitFrames { get; internal set; }
    public double TrackingPercent => TrackingFrames == 0 ? 0 : 100.0 * TrackingHitFrames / TrackingFrames;
    public int LongestContinuousTrack { get; internal set; }
    public int ScopedShots { get; internal set; }
    public int ScopedHits { get; internal set; }
    public int UnscopedShots => ShotsFired - ScopedShots;
    public int UnscopedHits => ShotsHit - ScopedHits;
}
internal static class TrainingSamples { internal static int MinValue(this List<int> values) { int min = int.MaxValue; foreach (int v in values) min = Math.Min(min, v); return min; } }
