using System;
using MphRead.Entities;

namespace MphRead.Mods.Training;

public enum AimTrainerDrill { StaticPrecision, Flick, StrafeTracking, JumpTracking, HeadshotPrecision, ImperialistPrecision, WeaponMastery, TimedFlick, MultiTargetFlick }
public enum AimTrainerMovement { Static, HorizontalStrafe, WideStrafe, DirectionChange, Jump, JumpStrafe, AirborneCrossing, Circular, RandomBurst }
public enum TrainingDifficulty { Beginner, Standard, Advanced, Master }
public enum TrainingInputSource { KeyboardMouse, Gamepad, Touch, Stylus }
public enum TrainingDistance { Random, Short, Medium, Long }
public enum TrainingScope { Mixed, Scoped, Unscoped }
public readonly record struct AimTrainerDefinition
{
    public AimTrainerDrill Drill { get; init; }
    public BeamType Weapon { get; init; }
    public int DurationSeconds { get; init; }
    public int TargetCount { get; init; }
    public AimTrainerMovement Movement { get; init; }
    public TrainingDifficulty Difficulty { get; init; }
    public TrainingDistance Distance { get; init; }
    public TrainingScope Scope { get; init; }
    public bool HeadshotsOnly { get; init; }
    public bool InfiniteAmmo { get; init; }
    public bool NormalImperialistReload { get; init; }
    public bool ReloadOnHit { get; init; }
    public bool FixedSeed { get; init; }
    public uint Seed { get; init; }
    public static AimTrainerDefinition Default => new() { DurationSeconds = 60, TargetCount = 1, InfiniteAmmo = true, Difficulty = TrainingDifficulty.Standard, Seed = 1 };
    public AimTrainerDefinition Sanitize() => this with
    {
        Drill = Enum.IsDefined(Drill) ? Drill : AimTrainerDrill.StaticPrecision,
        Weapon = Drill == AimTrainerDrill.ImperialistPrecision ? BeamType.Imperialist
            : (int)Weapon >= 0 && (int)Weapon <= (int)BeamType.ShockCoil ? Weapon : BeamType.PowerBeam,
        DurationSeconds = Math.Clamp(DurationSeconds, 15, 600),
        TargetCount = !Enum.IsDefined(Drill) || Drill == AimTrainerDrill.TimedFlick ? 1
            : Drill == AimTrainerDrill.MultiTargetFlick ? Math.Clamp(TargetCount, 4, 5) : Math.Clamp(TargetCount, 1, PlayerEntity.SlotCapacity - 1),
        Movement = !Enum.IsDefined(Movement) ? AimTrainerMovement.Static : Movement,
        HeadshotsOnly = HeadshotsOnly || Drill == AimTrainerDrill.HeadshotPrecision,
        Difficulty = Enum.IsDefined(Difficulty) ? Difficulty : TrainingDifficulty.Standard,
        Distance = Enum.IsDefined(Distance) ? Distance : TrainingDistance.Random,
        Scope = Enum.IsDefined(Scope) ? Scope : TrainingScope.Mixed,
        Seed = Seed == 0 ? 1u : Seed
    };
    public AimTrainerDefinition Retry() => this with { Seed = FixedSeed ? Seed : (uint)Random.Shared.Next(1, int.MaxValue) };
}
