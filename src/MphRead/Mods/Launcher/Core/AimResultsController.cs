#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.Core;

public enum AimResultsAction { Retry, ChangeDrill, Exit }
public readonly record struct AimResultMetric(string Label, string Value);
public sealed record AimResultsReport(string Summary, ImmutableArray<AimResultMetric> Metrics, string Status)
{
    public static AimResultsReport From(AimTrainerSession session) => From(session.Definition, session.Stats,
        session.DominantInput, session.Tracking, session.NewPersonalBest, session.StorageError);
    public static AimResultsReport From(AimTrainerDefinition definition, AimTrainerStats stats,
        TrainingInputSource input, bool tracking, bool personalBest, string storageError)
    {
        var rows = ImmutableArray.CreateBuilder<AimResultMetric>();
        void Add(string label, string value) => rows.Add(new(label, value));
        Add("SCORE", stats.Score.ToString("N0"));
        if (tracking) { Add("TRACKING", $"{stats.TrackingPercent:F1}%"); Add("LONGEST LOCK", $"{stats.LongestContinuousTrack / 60.0:F2}s"); Add("AVERAGE LOCK", $"{stats.AverageLockSeconds:F2}s"); }
        else { Add("ACCURACY", $"{stats.Accuracy:F1}%"); Add("FIRST SHOT", $"{stats.FirstShotAccuracy:F1}%"); }
        Add("HEADSHOT RATE", $"{stats.HeadshotPercent:F1}%"); Add("AVG REACTION", $"{stats.AverageReactionMs:F0}ms"); Add("BEST REACTION", $"{stats.BestReactionMs:F0}ms");
        Add("LONGEST STREAK", stats.LongestHitStreak.ToString()); Add("SHOTS / HITS / MISSES", $"{stats.ShotsFired} / {stats.ShotsHit} / {stats.ShotsMissed}");
        Add("TARGETS HIT / EXPIRED", $"{stats.TargetsHit} / {stats.TargetsExpired}");
        if (definition.Weapon == BeamType.ShockCoil) { Add("TICK CONNECTION", $"{stats.Accuracy:F1}%"); Add("REACQUISITION", $"{stats.AverageReacquisitionMs:F0}ms"); }
        Add("DIRECT / SPLASH", $"{stats.DirectHits} / {stats.SplashHits}"); Add("CHARGED / FREEZE HITS", $"{stats.ChargedHits} / {stats.FreezeHits}");
        if (definition.Weapon == BeamType.Imperialist) { Add("SCOPED HITS / SHOTS", $"{stats.ScopedHits}/{stats.ScopedShots}"); Add("UNSCOPED HITS / SHOTS", $"{stats.UnscopedHits}/{stats.UnscopedShots}"); }
        return new($"{TrainingLabels.Display(definition.Drill.ToString())} / {TrainingLabels.Display(definition.Weapon.ToString())} / {TrainingLabels.Display(input.ToString())}",
            rows.ToImmutable(), personalBest ? "NEW PERSONAL BEST" : storageError ?? "");
    }
}
public sealed class AimResultsController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    public Guid Lifetime { get; } = Guid.NewGuid();
    public AimResultsReport Report { get; }
    public bool Closed { get; private set; }
    public AimResultsController(AimResultsReport report) => Report = report ?? throw new ArgumentNullException(nameof(report));
    public bool Dispatch(Guid lifetime, AimResultsAction action)
    {
        CheckOwner();
        if (Closed || lifetime != Lifetime || !Enum.IsDefined(action)) return false;
        Closed = true;
        return true;
    }
    public void Dispose() { CheckOwner(); Closed = true; }
    private void CheckOwner() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Training result actions belong to the engine thread."); }
}
#endif
