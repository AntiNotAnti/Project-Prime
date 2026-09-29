using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;
namespace MphRead.Mods.Training;

/// <summary>Owns a local drill. No network, career, or replay state is serialized.</summary>
public sealed class AimTrainerSession
{
    private sealed class Shot
    {
        internal int Frame;
        internal bool Hit, Scoped, Charged;
        internal Vector3 Direction;
        internal readonly Dictionary<int, int> TargetAttempts = new();
        internal readonly HashSet<int> HitTargets = new();
        internal readonly Dictionary<int, int> TargetAppearances = new();
    }
    private readonly Scene _scene;
    private readonly Dictionary<int, AimTrainerTargetController> _targets = new();
    private readonly Dictionary<long, Shot> _shots = new();
    private readonly TrainingTargetMotion _random;
    private readonly int[] _inputFrames = new int[4];
    private long _sequence;
    private int _lastConnected = -100, _continuous;
    private AimTrainerHud? _hud;
    public AimTrainerDefinition Definition { get; }
    public LaunchPlan Plan { get; }
    public AimTrainerStats Stats { get; } = new();
    public bool Running { get; private set; }
    public bool Completed { get; private set; }
    public bool ResultsShown { get; set; }
    public bool NewPersonalBest { get; private set; }
    public string StorageError { get; private set; } = "";
    public string Feedback { get; private set; } = "READY";
    public TrainingInputSource DominantInput => (TrainingInputSource)Array.IndexOf(_inputFrames, _inputFrames.Max());
    internal long CurrentShotId { get; private set; }
    public int TargetLifetimeFrames => Definition.Drill == AimTrainerDrill.TimedFlick ? 90
        : Definition.Drill == AimTrainerDrill.MultiTargetFlick ? 0 : 480 - (int)Definition.Difficulty * 120;
    internal bool SkipImperialistReload(PlayerEntity player) => Running && !Completed
        && player == _scene.Players.Main && !Definition.NormalImperialistReload
        && Definition.Weapon == BeamType.Imperialist && player.Controls.Shoot.IsPressed;
    public bool Tracking => Definition.Drill is AimTrainerDrill.StrafeTracking or AimTrainerDrill.JumpTracking || Definition.Weapon == BeamType.ShockCoil && Definition.Drill is not (AimTrainerDrill.TimedFlick or AimTrainerDrill.MultiTargetFlick);
    private AimTrainerSession(Scene scene, LaunchPlan plan)
    {
        _scene = scene; Plan = plan; Definition = (plan.Training ?? AimTrainerDefinition.Default).Sanitize();
        _random = new(Definition.Seed);
        Stats.PerWeaponStats.Add(Definition.Weapon, new AimTrainerStats());
        foreach (var player in scene.Players.Items)
            if (player.SlotIndex > 0 && player.SlotIndex <= Definition.TargetCount) _targets.Add(player.SlotIndex, new(player));
    }
    public static void Attach(Scene scene, LaunchPlan plan)
    {
        if (plan.Kind != LaunchKind.AimTrainer || scene.Services.IsReplica || Network.NetSession.Active)
            throw new InvalidOperationException("Training requires a local, non-replay scene.");
        scene.AimTrainer = new(scene, plan);
        scene.GameState.MatchTime = -1;
        scene.GameState.PointGoal = 0;
    }
    internal bool Hidden(PlayerEntity player) => OwnsTarget(player) && _targets[player.SlotIndex].WakeFrame != 0;
    public bool OwnsTarget(PlayerEntity player) => player.OwningScene == _scene && _targets.ContainsKey(player.SlotIndex);
    public void Start()
    {
        if (Running || Completed) return;
        Running = true;
        _scene.Players.Main.ModNetSpawn(new(0, .1f, 12), -Vector3.UnitZ);
        _scene.Players.Main.ModArmWeapon(Definition.Weapon);
        foreach (var target in _targets.Values) Appear(target);
    }
    private void Appear(AimTrainerTargetController target)
    {
        int previous = target.Anchor;
        Vector3 origin = _scene.Players.Main.Position;
        bool elevated = Definition.Drill == AimTrainerDrill.MultiTargetFlick && target.Player.SlotIndex <= 2;
        bool SuitableHeight(int i) => Definition.Drill == AimTrainerDrill.MultiTargetFlick
            ? elevated ? TrainingTargetMotion.Anchors[i].Y > (target.Player.SlotIndex == 1 ? 7 : 1) : TrainingTargetMotion.Anchors[i].Y < 1
            : Definition.Movement == AimTrainerMovement.Static || TrainingTargetMotion.Anchors[i].Y < 1;
        var candidates = new List<int>();
        for (int i = 0; i < TrainingTargetMotion.Anchors.Length; i++)
        {
            if (i == previous || !SuitableHeight(i) || _targets.Values.Any(other => other != target && other.Anchor == i)) continue;
            var direction = TrainingTargetMotion.Anchors[i] - origin;
            float range = direction.Length;
            if (Definition.Distance == TrainingDistance.Short && range > 28
                || Definition.Distance == TrainingDistance.Medium && (range < 28 || range > 42)
                || Definition.Distance == TrainingDistance.Long && range < 42) continue;
            float angle = previous < 0 ? 180 : MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(
                direction.Normalized(), (TrainingTargetMotion.Anchors[previous] - origin).Normalized()), -1, 1)));
            if (Definition.Drill == AimTrainerDrill.Flick && angle < 12 + (int)Definition.Difficulty * 8) continue;
            candidates.Add(i);
        }
        if (candidates.Count == 0)
        {
            var available = Enumerable.Range(0, TrainingTargetMotion.Anchors.Length)
                .Where(i => SuitableHeight(i) && !_targets.Values.Any(other => other != target && other.Anchor == i)).ToList();
            candidates.AddRange(available.Where(i => i != previous));
            // Seven movers can occupy every ground lane. Reuse a lane only
            // when there is genuinely no unoccupied alternative.
            if (candidates.Count == 0) candidates.AddRange(available);
        }
        target.Anchor = candidates[_random.Next(candidates.Count)];
        if (previous >= 0)
        {
            float angle = MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(
                (TrainingTargetMotion.Anchors[target.Anchor] - origin).Normalized(),
                (TrainingTargetMotion.Anchors[previous] - origin).Normalized()), -1, 1)));
            Stats.AngularTransitions.Add(angle);
        }
        target.Player.ModTrainingHide(false);
        target.Player.ModNetSpawn(TrainingTargetMotion.Anchors[target.Anchor], Vector3.UnitZ);
        target.Player.IgnoreItemPickups = true;
        target.Attempts = 0;
        target.WakeFrame = 0; target.Appeared = Stats.ElapsedFrames; Stats.TargetsSpawned++;
    }
    public bool TryDriveTarget(PlayerEntity player)
    {
        if (!OwnsTarget(player)) return false;
        _targets[player.SlotIndex].Drive(Stats.ElapsedFrames, Definition, Running);
        return true;
    }
    public void ProcessFrame()
    {
        if (Completed || _scene.GameState.MenuPause || _scene.GameState.MatchState != MatchState.InProgress) return;
        if (!Running) Start();
        _scene.GameState.MatchTime = -1; _scene.GameState.PointGoal = 0;
        Stats.ElapsedFrames++;
        _inputFrames[PointerDevice.Current.Device == PointerDeviceType.Pen ? 3 : (int)InputSourceTracker.Current]++;
        var main = _scene.Players.Main;
        main.IgnoreItemPickups = true;
        if (main.CurrentWeapon != Definition.Weapon) main.ModArmWeapon(Definition.Weapon);
        if (Definition.InfiniteAmmo) main.ModSetAmmo(int.MaxValue, int.MaxValue);
        if (Definition.Weapon == BeamType.Imperialist && Definition.Scope != TrainingScope.Mixed)
            main.Controls.Zoom.IsPressed = main.EquipInfo.Zoomed != (Definition.Scope == TrainingScope.Scoped);
        foreach (var target in _targets.Values)
        {
            if (target.WakeFrame > 0 && Stats.ElapsedFrames >= target.WakeFrame) Appear(target);
            else if (!Tracking && TargetLifetimeFrames > 0 && target.WakeFrame == 0 && Stats.ElapsedFrames - target.Appeared >= TargetLifetimeFrames)
            {
                target.WakeFrame = Stats.ElapsedFrames + 18;
                target.Player.ModTrainingHide(true);
                Stats.TargetsExpired++; Stats.CurrentHitStreak = 0; Feedback = "TARGET TIMEOUT";
                if (Definition.HeadshotsOnly) Stats.Score -= 10;
            }
        }
        foreach (long id in _shots.Where(p => Stats.ElapsedFrames - p.Value.Frame > 600).Select(p => p.Key).ToArray()) Resolve(id);
        if (Tracking)
        {
            Stats.TrackingFrames++;
            if (_lastConnected == Stats.ElapsedFrames - 1)
            { Stats.TrackingHitFrames++; Stats.LongestContinuousTrack = Math.Max(Stats.LongestContinuousTrack, ++_continuous); Stats.Score += 1 + (int)Definition.Difficulty; }
            else if (_continuous > 0)
            { Stats.LockSamples.Add(_continuous); _continuous = 0; }
        }
        if (Stats.ElapsedFrames >= Definition.DurationSeconds * 60) Complete();
    }
    public void BeginShot(PlayerEntity player)
    {
        if (!Running || player != _scene.Players.Main) return;
        CurrentShotId = ++_sequence;
        var shot = new Shot { Direction = player.CameraInfo.Target - player.CameraInfo.Position, Frame = Stats.ElapsedFrames, Scoped = player.EquipInfo.Zoomed,
            Charged = player.EquipInfo.Weapon.MinCharge > 0 && player.EquipInfo.ChargeLevel >= player.EquipInfo.Weapon.MinCharge * 2 };
        foreach (var target in _targets.Values)
            if (target.WakeFrame == 0) { shot.TargetAppearances[target.Player.SlotIndex] = target.Appeared; shot.TargetAttempts[target.Player.SlotIndex] = ++target.Attempts; }
        _shots.Add(CurrentShotId, shot);
        if (shot.Charged) Stats.ChargedShots++;
        Stats.ShotsFired++;
        Stats.FirstShotAttempts += shot.TargetAttempts.Values.Count(attempt => attempt == 1);
        if (player.EquipInfo.Zoomed) Stats.ScopedShots++;
    }
    internal void NoteProjectile(BeamProjectileEntity beam)
    {
        if (Running && beam.Owner == _scene.Players.Main && _shots.ContainsKey(beam.TrainingShotId))
            Stats.DamagePotential += Math.Max(0, beam.Damage);
    }
    public void EndShot(bool spawned)
    {
        if (!spawned && _shots.Remove(CurrentShotId, out var shot))
        {
            Stats.ShotsFired--; if (shot.Charged) Stats.ChargedShots--; if (shot.Scoped) Stats.ScopedShots--;
            Stats.FirstShotAttempts -= shot.TargetAttempts.Values.Count(attempt => attempt == 1);
            foreach (int slot in shot.TargetAttempts.Keys) _targets[slot].Attempts--;
        }
        CurrentShotId = 0;
    }
    private void Resolve(long id)
    {
        if (!_shots.Remove(id, out var shot) || shot.Hit) return;
        Stats.ShotsMissed++; Stats.CurrentHitStreak = 0;
        if (Definition.HeadshotsOnly) Stats.Score -= 10;
        Feedback = "MISS";
    }
    public bool TryHandleDamage(PlayerEntity victim, EntityBase? source, uint damage, DamageFlags flags)
    {
        if (victim.OwningScene != _scene) return false;
        // The local rig cannot die to its own explosive or the arena floor.
        if (!OwnsTarget(victim)) return victim == _scene.Players.Main;
        if (!Running || source is not BeamProjectileEntity beam || beam.Owner != _scene.Players.Main || damage == 0) return true;
        var target = _targets[victim.SlotIndex];
        if (target.WakeFrame != 0 || !_shots.TryGetValue(beam.TrainingShotId, out var shot)) return true;
        if (!shot.TargetAppearances.TryGetValue(victim.SlotIndex, out int appearance) || appearance != target.Appeared) return true;
        Stats.DamageConnected += damage;
        bool head = flags.TestFlag(DamageFlags.Headshot);
        if (!Definition.HeadshotsOnly || head)
        {
            if (Tracking && _lastConnected >= 0 && Stats.ElapsedFrames - _lastConnected > 1)
                Stats.ReacquisitionSamples.Add(Stats.ElapsedFrames - _lastConnected);
            _lastConnected = Stats.ElapsedFrames;
        }
        if (!shot.HitTargets.Add(victim.SlotIndex)) return true;
        if (beam.EnhancedDirectHit) Stats.DirectHits++; else Stats.SplashHits++;
        if (shot.Charged) Stats.ChargedHits++;
        if (beam.Afflictions.TestFlag(Affliction.Freeze)) Stats.FreezeHits++;
        if (head) Stats.Headshots++; else Stats.BodyHits++;
        Feedback = head ? "HEADSHOT" : "BODY";
        if (!shot.Hit) { shot.Hit = true; Stats.ShotsHit++; if (shot.Scoped) Stats.ScopedHits++; }
        if (Definition.HeadshotsOnly && !head) return true;
        _lastConnected = Stats.ElapsedFrames;
        {
            bool firstShot = shot.TargetAttempts[victim.SlotIndex] == 1;
            if (firstShot) Stats.FirstShotHits++;
            Stats.TargetsHit++; Stats.CurrentHitStreak++; Stats.LongestHitStreak = Math.Max(Stats.LongestHitStreak, Stats.CurrentHitStreak);
            int reaction = Math.Max(0, shot.Frame - target.Appeared);
            if (Stats.ReactionSamples.Count > 0 && reaction < Stats.ReactionSamples.MinValue()) Feedback = "NEW BEST REACTION";
            Stats.ReactionSamples.Add(reaction);
            Stats.AcquisitionSamples.Add(Math.Max(0, Stats.ElapsedFrames - target.Appeared));
            if (!Tracking) Stats.Score += AimTrainerScoring.Hit(head, Definition.HeadshotsOnly, firstShot,
                Stats.ElapsedFrames - target.Appeared, Stats.CurrentHitStreak);
            if (Definition.ReloadOnHit) _scene.Players.Main.ModSetAmmo(int.MaxValue, int.MaxValue);
            if (!Tracking)
            {
                target.WakeFrame = Stats.ElapsedFrames + 18;
                // Offstage during acquisition delay; no collision or assist candidate while hidden.
                victim.ModTrainingHide(true);
            }
        }
        return true;
    }
    public void Complete()
    {
        if (Completed) return;
        if (_continuous > 0) Stats.LockSamples.Add(_continuous);
        foreach (long id in _shots.Keys.ToArray()) Resolve(id);
        var weaponStats = Stats.PerWeaponStats[Definition.Weapon];
        weaponStats.ShotsFired = Stats.ShotsFired; weaponStats.ShotsHit = Stats.ShotsHit; weaponStats.ShotsMissed = Stats.ShotsMissed;
        weaponStats.Headshots = Stats.Headshots; weaponStats.BodyHits = Stats.BodyHits;
        weaponStats.DirectHits = Stats.DirectHits; weaponStats.SplashHits = Stats.SplashHits;
        weaponStats.ChargedHits = Stats.ChargedHits; weaponStats.ChargedShots = Stats.ChargedShots;
        weaponStats.TrackingFrames = Stats.TrackingFrames; weaponStats.TrackingHitFrames = Stats.TrackingHitFrames;
        weaponStats.DamagePotential = Stats.DamagePotential; weaponStats.DamageConnected = Stats.DamageConnected;
        weaponStats.FreezeHits = Stats.FreezeHits; weaponStats.ElapsedFrames = Stats.ElapsedFrames; weaponStats.Score = Stats.Score;
        weaponStats.TargetsExpired = Stats.TargetsExpired;
        weaponStats.TargetsSpawned = Stats.TargetsSpawned; weaponStats.TargetsHit = Stats.TargetsHit;
        weaponStats.ScopedShots = Stats.ScopedShots; weaponStats.ScopedHits = Stats.ScopedHits;
        weaponStats.FirstShotAttempts = Stats.FirstShotAttempts; weaponStats.FirstShotHits = Stats.FirstShotHits;
        weaponStats.CurrentHitStreak = Stats.CurrentHitStreak; weaponStats.LongestHitStreak = Stats.LongestHitStreak;
        weaponStats.LongestContinuousTrack = Stats.LongestContinuousTrack;
        weaponStats.ReactionSamples.AddRange(Stats.ReactionSamples); weaponStats.AcquisitionSamples.AddRange(Stats.AcquisitionSamples);
        weaponStats.LockSamples.AddRange(Stats.LockSamples); weaponStats.ReacquisitionSamples.AddRange(Stats.ReacquisitionSamples);
        weaponStats.AngularTransitions.AddRange(Stats.AngularTransitions);
        Running = false; Completed = true;
        _scene.GameState.PauseMenu();
        try { NewPersonalBest = AimTrainerPersonalBests.Save(Definition, DominantInput, Stats); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { StorageError = "Personal best could not be saved: " + ex.Message; }
    }
    public void DrawHud() { if (!Completed) (_hud ??= new(_scene)).Draw(this); }
}
