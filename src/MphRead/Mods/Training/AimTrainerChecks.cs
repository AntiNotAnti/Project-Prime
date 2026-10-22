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
        foreach (var drill in Enum.GetValues<AimTrainerDrill>())
        {
            var settings = (AimTrainerDefinition.Default with { Drill = drill, TargetCount = 5, Movement = AimTrainerMovement.JumpStrafe }).Sanitize();
            Check(settings.Sanitize() == settings && settings.Movement == AimTrainerMovement.JumpStrafe,
                drill + " sanitization preserves movement and is stable");
        }
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
        var exported = Read.GetRoomModelForExport(AimTrainerLaunch.Room).Recolors[0];
        check(exported.TextureData.Count > 0 && System.Linq.Enumerable.All(
            System.Linq.Enumerable.Range(0, exported.Textures.Count), i =>
                exported.TextureData[i].Count == exported.Textures[i].Width * exported.Textures[i].Height),
            "headless-generated arena retains complete renderable textures");
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
            check(trainer.HitMarkerAlpha == 1f, "accepted target hit pulses Aim Lab hit marker");
            check(trainer.Hidden(target), "scored target enters transition");
            for (int i = 0; i < 7; i++) trainer.ProcessFrame();
            check(trainer.HitMarkerAlpha > 0 && trainer.HitMarkerAlpha < 1f, "Aim Lab hit marker fades over its final frames");
            for (int i = 7; i < 20; i++) trainer.ProcessFrame();
            check(trainer.HitMarkerAlpha == 0, "Aim Lab hit marker expires independently of network prediction");
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
                        check(Launcher.Gui.UiCapture.Capture(offline, Path.Combine(shots, $"training-config-{size.Width}x{size.Height}.png"), size), "lay out trainer controls " + size);
                        var drillRow = (Launcher.Gui.ChoiceRow)Launcher.Gui.ControllerNav.Find(offline, "offline.training.drill")!;
                        var targetRow = (Launcher.Gui.ChoiceRow)Launcher.Gui.ControllerNav.Find(offline, "offline.training.targets")!;
                        var movementRow = (Launcher.Gui.ChoiceRow)Launcher.Gui.ControllerNav.Find(offline, "offline.training.movement")!;
                        drillRow.Index = (int)AimTrainerDrill.JumpTracking;
                        check(movementRow.Index == (int)AimTrainerMovement.JumpStrafe, "jump preset visibly selects jump strafe");
                        drillRow.Index = (int)AimTrainerDrill.TimedFlick;
                        check(targetRow.Index == 0 && !targetRow.IsEnabled, "timed preset visibly selects one target");
                        drillRow.Index = (int)AimTrainerDrill.MultiTargetFlick;
                        check(targetRow.Index == 4 && targetRow.IsEnabled && movementRow.Index == 0, "multi-target preset visibly selects five targets");
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
        CheckSpeedDrills(check);
        var fullScene = new Scene(new Vector2i(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {});
        try
        {
            fullScene.Players.MaxPlayers = 8;
            for (int i = 0; i < 8; i++) fullScene.AddPlayer(Hunter.Samus);
            fullScene.AddRoom(AimTrainerLaunch.Room, GameMode.Battle);
            AimTrainerSession.Attach(fullScene, new LaunchPlan { Kind = LaunchKind.AimTrainer,
                Training = AimTrainerDefinition.Default with { TargetCount = 7, Movement = AimTrainerMovement.Jump } });
            fullScene.OnLoad(); fullScene.AimTrainer!.Start();
            for (int i = 0; i < 800; i++) fullScene.AimTrainer.ProcessFrame();
            check(fullScene.AimTrainer.Stats.TargetsSpawned >= 21, "all seven occupied movement lanes can expire and respawn safely");
        }
        finally { fullScene.DoCleanup(); }
        foreach (var movement in Enum.GetValues<AimTrainerMovement>())
        {
            var movingScene = new Scene(new Vector2i(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {});
            try
            {
                movingScene.AddPlayer(Hunter.Samus); movingScene.AddPlayer(Hunter.Samus);
                movingScene.AddRoom(AimTrainerLaunch.Room, GameMode.Battle);
                AimTrainerSession.Attach(movingScene, new LaunchPlan { Kind = LaunchKind.AimTrainer,
                    Training = AimTrainerDefinition.Default with { Drill = AimTrainerDrill.StrafeTracking, Movement = movement, Distance = TrainingDistance.Short } });
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
                check((movement is AimTrainerMovement.Static or AimTrainerMovement.Jump ? lateral < .5f : lateral > .5f && lateral < 11)
                    && !fired, movement + " honors lateral movement without firing");
                if (movement is AimTrainerMovement.Jump or AimTrainerMovement.JumpStrafe or AimTrainerMovement.AirborneCrossing)
                    check(vertical > 1, movement + " uses real airborne movement");
            }
            finally { movingScene.DoCleanup(); }
        }
    }
    private static void CheckSpeedDrills(Action<bool, string> check)
    {
        check((AimTrainerDefinition.Default with { Drill = AimTrainerDrill.Flick, TargetCount = 5,
            Movement = AimTrainerMovement.Jump }).Sanitize() is { TargetCount: 5, Movement: AimTrainerMovement.Jump },
            "flick settings preserve requested count and movement");
        foreach (var drill in new[] { AimTrainerDrill.TimedFlick, AimTrainerDrill.MultiTargetFlick })
        {
            var scene = new Scene(new Vector2i(256,192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => {}, () => {});
            try
            {
                var definition = (AimTrainerDefinition.Default with { Drill = drill, TargetCount = 5, Weapon = BeamType.Imperialist }).Sanitize();
                scene.Players.MaxPlayers = 8;
                scene.AddPlayer(Hunter.Samus);
                for (int i = 0; i < definition.TargetCount; i++) scene.AddPlayer((Hunter)(i % Hunters.Playable));
                scene.AddRoom(AimTrainerLaunch.Room, GameMode.Battle);
                AimTrainerSession.Attach(scene, new LaunchPlan { Kind = LaunchKind.AimTrainer, Training = definition });
                scene.OnLoad(); var trainer = scene.AimTrainer!; trainer.Start();
                var shooter = scene.Players.Main; var target = scene.Players.Items[1];
                if (drill == AimTrainerDrill.TimedFlick)
                {
                    for (int i = 0; i < 89; i++) trainer.ProcessFrame();
                    check(!trainer.Hidden(target), "timed target remains until 1.5 second boundary");
                    trainer.ProcessFrame();
                    check(trainer.Hidden(target) && trainer.Stats.TargetsExpired == 1, "timed target despawns at exactly 90 simulation frames");
                    for (int i = 0; i < 18; i++) trainer.ProcessFrame();
                    check(!trainer.Hidden(target) && trainer.Stats.TargetsSpawned == 2, "expired target returns after acquisition gap");
                }
                else
                {
                    for (int i = 0; i < 180; i++) scene.OnSimulationFrame();
                    var targets = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(scene.Players.Items, p => trainer.OwnsTarget(p)));
                    check(targets.Length == 5 && System.Linq.Enumerable.Count(targets, p => p.Position.Y > 3) == 2,
                        "five simultaneous targets retain two supported elevated positions");
                    check(System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(targets, p => p.Position))) == 5,
                        "simultaneous targets use distinct positions");
                    trainer.BeginShot(shooter);
                    var beam = new BeamProjectileEntity(scene) { Owner = shooter, TrainingShotId = trainer.CurrentShotId };
                    targets[0].TakeDamage(10, DamageFlags.Headshot, null, beam);
                    targets[1].TakeDamage(10, DamageFlags.Headshot, null, beam);
                    trainer.EndShot(true);
                    check(trainer.Stats.TargetsHit == 2 && trainer.Hidden(targets[0]) && trainer.Hidden(targets[1])
                        && trainer.Stats.ShotsHit == 1 && trainer.Stats.FirstShotAccuracy <= 100,
                        "one projectile scores and removes each struck target without inflating shot accuracy");
                }
                for (int i = 0; i < 30; i++) scene.OnSimulationFrame();
                var fire = typeof(PlayerEntity).GetMethod("TryFireWeapon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                shooter.Controls.Shoot.IsDown = shooter.Controls.Shoot.IsPressed = true;
                bool first = (bool)fire.Invoke(shooter, null)!;
                shooter.Controls.Shoot.IsPressed = false;
                bool held = (bool)fire.Invoke(shooter, null)!;
                shooter.Controls.Shoot.IsPressed = true;
                bool clicked = (bool)fire.Invoke(shooter, null)!;
                check(first && !held && clicked, drill + " Imperialist bypass requires a fresh click, not held fire");
                AimTrainerSession.Attach(scene, new LaunchPlan { Kind = LaunchKind.AimTrainer,
                    Training = definition with { NormalImperialistReload = true } });
                scene.AimTrainer!.Start();
                // First shot after spawning establishes the ordinary cooldown.
                fire.Invoke(shooter, null);
                check(!(bool)fire.Invoke(shooter, null)!, "normal Imperialist reload option retains cooldown");
            }
            finally { scene.DoCleanup(); }
        }
    }

}
