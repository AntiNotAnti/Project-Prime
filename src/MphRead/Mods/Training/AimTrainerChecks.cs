using System;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Launcher;
using MphRead.Mods.Input;
#if MPHREAD_SHELL
using Avalonia.Controls;
#endif
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Mods.Training;
public static class AimTrainerChecks
{
    public static int Run(bool simulation = false, string? shots = null)
    {
        int failures = 0;
        void Check(bool pass, string name) { Console.WriteLine($"[trainingcheck] {(pass ? "PASS" : "FAIL")} {name}"); if (!pass) failures++; }
        var invalid = new AimTrainerDefinition { Drill = (AimTrainerDrill)99, Weapon = BeamType.Enemy, DurationSeconds = -1, TargetCount = 500, Movement = (AimTrainerMovement)99 }.Sanitize();
        Check(invalid.DurationSeconds == 15 && invalid.TargetCount == 1 && invalid.Weapon == BeamType.PowerBeam && invalid.Seed == 1, "invalid configuration clamps safely");
        Check((AimTrainerDefinition.Default with { DurationSeconds = int.MaxValue }).Sanitize().DurationSeconds == 600, "duration upper bound");
        var fixedRun = AimTrainerDefinition.Default with { FixedSeed = true, Seed = 42 };
        Check(fixedRun.Retry() == fixedRun, "fixed-seed retry is exact");
        var retry = AimTrainerDefinition.Default.Retry();
        Check((retry with { Seed = 1 }) == AimTrainerDefinition.Default, "retry preserves settings");
        var first = new TrainingTargetMotion(42); var second = new TrainingTargetMotion(42);
        bool deterministic = true;
        for (int i = 0; i < 1000; i++) deterministic &= first.Next(7) == second.Next(7);
        Check(deterministic, "deterministic target sequence");
        Check(AimTrainerScoring.Hit(false, true, true, 1, 100) == 0 && AimTrainerScoring.Hit(true, true, false, 90, 1) == 100, "headshot-only scoring uses fixed points");
        Check((AimTrainerDefinition.Default with { Drill = AimTrainerDrill.ImperialistPrecision }).Sanitize().Weapon == BeamType.Imperialist, "Imperialist drill selects real weapon");
        var stats = new AimTrainerStats { ElapsedFrames = 3600, ShotsFired = 4, ShotsHit = 3, BodyHits = 1, Headshots = 2 };
        stats.ReactionSamples.AddRange(new[] { 6, 12 });
        Check(stats.ElapsedTime == 60 && stats.AverageReactionMs == 150 && stats.BestReactionMs == 100 && stats.Accuracy == 75, "metrics use 60 Hz simulation frames");
        string original = LauncherPrefs.Directory;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-training-check-" + Guid.NewGuid().ToString("N"));
        var originalSettings = LauncherPrefs.Training;
        try
        {
            LauncherPrefs.Directory = temporary;
            Check(AimTrainerPersonalBests.Save(fixedRun, TrainingInputSource.Gamepad, stats), "first controller personal best");
            Check(!AimTrainerPersonalBests.Save(fixedRun, TrainingInputSource.Gamepad, stats), "equal score does not overwrite personal best");
            Check(AimTrainerPersonalBests.Save(fixedRun, TrainingInputSource.KeyboardMouse, stats), "mouse personal best is independent");
            LauncherPrefs.Training = fixedRun; LauncherPrefs.Save(); LauncherPrefs.Training = default; LauncherPrefs.Load();
            Check(LauncherPrefs.Training == fixedRun, "trainer preferences round trip");
            if (simulation) RunSimulation(Check, shots);
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally { LauncherPrefs.Directory = original; LauncherPrefs.Training = originalSettings; if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        return failures == 0 ? 0 : 1;
    }
    private static void RunSimulation(Action<bool, string> check, string? shots)
    {
        if (!GameFiles.Ready) { check(false, "simulation requires extracted game files"); return; }
        Headless.Enter(); GameFiles.ApplyPaths();
        MapGen.CustomRooms.GenerateMissing(AimTrainerLaunch.Room);
        var scene = new Scene(new Vector2i(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {});
        try
        {
            scene.AddPlayer(Hunter.Samus); scene.AddPlayer(Hunter.Samus);
            scene.AddRoom(AimTrainerLaunch.Room, GameMode.Battle);
            var definition = AimTrainerDefinition.Default with { FixedSeed = true, DurationSeconds = 15 };
            var plan = new LaunchPlan { Kind = LaunchKind.AimTrainer, Training = definition, Hunter = Hunter.Samus };
            AimTrainerSession.Attach(scene, plan); scene.OnLoad();
            var trainer = scene.AimTrainer!;
            bool rejected = false;
            try { AimTrainerSession.Attach(scene, new LaunchPlan { Kind = LaunchKind.Online }); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && scene.AimTrainer == trainer, "online launch cannot attach or replace trainer state");
            trainer.Start();
            var shooter = scene.Players.Items[0]; var target = scene.Players.Items[1];
            check(trainer.TryDriveTarget(target) && !target.Controls.Shoot.IsDown && target.IgnoreItemPickups, "targets bypass combat AI and pickups");
            trainer.BeginShot(shooter);
            var beam = new BeamProjectileEntity(scene) { Owner = shooter, Beam = BeamType.PowerBeam, TrainingShotId = trainer.CurrentShotId, EnhancedDirectHit = true };
            int health = target.Health;
            target.TakeDamage(10, DamageFlags.Headshot, null, beam); trainer.EndShot(true);
            check(trainer.Stats.Headshots == 1 && trainer.Stats.ShotsHit == 1 && target.Health == health, "central damage consumes genuine headshot flags without death");
            check(trainer.Hidden(target), "scored target enters transition");
            for (int i = 0; i < 20; i++) trainer.ProcessFrame();
            check(!trainer.Hidden(target) && trainer.Stats.TargetsSpawned == 2, "target reappears on authored anchor");
            int previousHits = trainer.Stats.ShotsHit;
            trainer.BeginShot(shooter);
            var result = BeamProjectileEntity.Spawn(shooter, shooter.EquipInfo,
                target.Position + new Vector3(0, .7f, 3), -Vector3.UnitZ,
                BeamSpawnFlags.None, target.NodeRef, scene);
            trainer.EndShot(result != BeamResultFlags.NoSpawn);
            for (int i = 0; i < 40; i++) scene.OnSimulationFrame();
            check(trainer.Stats.ShotsHit > previousHits && trainer.Stats.BodyHits > 0, "real projectile collision records body hit");
            check(target.Controls.Shoot.IsDown == false && trainer.Stats.ShotsFired == 2, "targets never fire during real simulation");
            check(TrainingTargetGeometry.HeadTop(target) - TrainingTargetGeometry.HeadBottom(target) > .299f,
                "head region uses actual hunter geometry");
            trainer.BeginShot(shooter); trainer.EndShot(true);
            for (int i = 0; i < 900; i++) trainer.ProcessFrame();
            check(trainer.Completed && trainer.Stats.ElapsedFrames == 900 && trainer.Stats.ShotsMissed == 1, "timer completes exactly and resolves pending misses");
            int score = trainer.Stats.Score; trainer.ProcessFrame(); target.TakeDamage(10, DamageFlags.Headshot, null, beam);
            check(score == trainer.Stats.Score && trainer.Stats.ElapsedFrames == 900, "completed run is immutable");
#if MPHREAD_SHELL
            if (shots != null)
            {
                Directory.CreateDirectory(shots);
                if (Launcher.Gui.GuiLauncher.EnsureSetup(requireDisplay: false)) Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                {
                    foreach (var size in new[] { new Avalonia.Size(1280,720), new Avalonia.Size(830,390), new Avalonia.Size(390,830) })
                    {
                        Launcher.Gui.Deck.Phone = size.Width < 1000;
                        var offline = new Launcher.Gui.OfflineWorkspace(new MenuSettings(), new[] { AimTrainerLaunch.Room }, new Launcher.Gui.PrimeOverlayHost());
                        check(Launcher.Gui.UiCapture.Capture(offline, Path.Combine(shots, $"training-config-{size.Width}x{size.Height}.png"), size), "capture trainer config " + size);
                        Launcher.Gui.ControllerNav.Find(offline, "offline.training.start")?.BringIntoView();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        check(Launcher.Gui.UiCapture.Capture(offline, Path.Combine(shots, $"training-config-{size.Width}x{size.Height}.png"), size), "capture scrolled trainer " + size);
                        check(Launcher.Gui.UiCapture.Capture(new Launcher.Gui.AimTrainerResultsView(trainer, () => {}, () => {}, () => {}),
                            Path.Combine(shots, $"training-results-{size.Width}x{size.Height}.png"), size), "capture trainer results " + size);
                    }
                    Launcher.Gui.Deck.Phone = false;
                });
            }
#endif
            check(Array.TrueForAll(CareerMatchStats.Damage, value => value == 0)
                && Array.TrueForAll(CareerMatchStats.Assists, value => value == 0), "career counters remain untouched");
            check(scene.GameState.ShotsFired[0] == 0 && scene.GameState.MatchTime == -1, "training does not author match counters or end Battle");
        }
        finally { scene.DoCleanup(); }
        check(scene.AimTrainer == null, "scene cleanup releases trainer");
        foreach (var drill in new[] { AimTrainerDrill.StrafeTracking, AimTrainerDrill.JumpTracking })
        {
            var movingScene = new Scene(new Vector2i(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {});
            try
            {
                movingScene.AddPlayer(Hunter.Samus); movingScene.AddPlayer(Hunter.Samus);
                movingScene.AddRoom(AimTrainerLaunch.Room, GameMode.Battle);
                AimTrainerSession.Attach(movingScene, new LaunchPlan { Kind = LaunchKind.AimTrainer,
                    Training = AimTrainerDefinition.Default with { Drill = drill, Distance = TrainingDistance.Short } });
                movingScene.OnLoad(); movingScene.AimTrainer!.Start();
                var target = movingScene.Players.Items[1]; var initial = target.Position;
                float lateral = 0, vertical = 0; bool fired = false;
                for (int frame = 0; frame < 600; frame++)
                {
                    movingScene.OnSimulationFrame();
                    lateral = Math.Max(lateral, Math.Abs(target.Position.X - initial.X));
                    vertical = Math.Max(vertical, target.Position.Y - initial.Y);
                    fired |= target.Controls.Shoot.IsDown;
                }
                check(lateral > .5f && lateral < 11 && !fired, drill + " moves within its lane without firing");
                if (drill == AimTrainerDrill.JumpTracking) check(vertical > 1, "jump tracking uses real airborne movement");
            }
            finally { movingScene.DoCleanup(); }
        }
    }
}
