#if MPHREAD_AVALONIA
using System;
using Avalonia.Controls;
using MphRead.Mods.Training;
namespace MphRead.Mods.Launcher.Gui;
public sealed class AimTrainerResultsView : UserControl
{
    public AimTrainerResultsView(AimTrainerSession session, Action retry, Action change, Action exit)
    {
        var s = session.Stats;
        var body = PrimeChrome.Stack(PrimeChrome.Title("TARGETING SIMULATION COMPLETE"),
            PrimeChrome.Text($"{TrainingLabels.Display(session.Definition.Drill.ToString())} / {TrainingLabels.Display(session.Definition.Weapon.ToString())} / {TrainingLabels.Display(session.DominantInput.ToString())}"),
            PrimeChrome.Text($"SCORE  {s.Score:N0}\n" + (session.Tracking ? $"TRACKING  {s.TrackingPercent:F1}%\nLONGEST LOCK  {s.LongestContinuousTrack / 60.0:F2}s\nAVERAGE LOCK  {s.AverageLockSeconds:F2}s\n" : $"ACCURACY  {s.Accuracy:F1}%\nFIRST SHOT  {s.FirstShotAccuracy:F1}%\n")
                + $"HEADSHOT RATE  {s.HeadshotPercent:F1}%\nAVG REACTION  {s.AverageReactionMs:F0}ms\nBEST REACTION  {s.BestReactionMs:F0}ms\nLONGEST STREAK  {s.LongestHitStreak}\nSHOTS  {s.ShotsFired} / HITS  {s.ShotsHit} / MISSES  {s.ShotsMissed}"),
            PrimeChrome.Text(session.Definition.Weapon == BeamType.ShockCoil
                ? $"TICK CONNECTION  {s.Accuracy:F1}% / REACQUISITION  {s.AverageReacquisitionMs:F0}ms" : ""),
            PrimeChrome.Text($"DIRECT  {s.DirectHits} / SPLASH  {s.SplashHits} / CHARGED HITS  {s.ChargedHits} / FREEZE HITS  {s.FreezeHits}\n"
                + (session.Definition.Weapon == BeamType.Imperialist ? $"SCOPED  {s.ScopedHits}/{s.ScopedShots} / UNSCOPED  {s.UnscopedHits}/{s.UnscopedShots}" : "")),
            PrimeChrome.Text(session.NewPersonalBest ? "NEW PERSONAL BEST" : session.StorageError),
            new PrimeButton("RETRY", retry, true), new PrimeButton("CHANGE DRILL", change), new PrimeButton("EXIT TRAINING", exit));
        Content = new ScrollViewer { Content = new PrimePanel(body), Margin = new Avalonia.Thickness(24) };
    }
}
#endif
