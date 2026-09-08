namespace MphRead;

// Captured at scene construction; never reads process-wide client preferences.
public sealed record MatchFeatureSet
{
    public bool AllowInvalidTeams { get; init; } = true;
    public bool HalfSecondAlarm { get; init; } = false;
    public bool FullBoostCharge { get; init; } = false;
    public bool BoostOpensDoors { get; init; } = false;
    public bool MaxPlayerDetail { get; init; } = true;
    public bool NoIdleSway { get; init; } = false;
    public bool DelayedIdleSway { get; init; } = true;
    public bool FixedWeapon { get; init; } = false;
    public bool FixedCrosshair { get; init; } = false;
    public bool MaxRoomDetail { get; init; } = false;
    public MatchBugfixSet Bugfixes { get; init; } = new();
    public MatchCheatSet Cheats { get; init; } = new();
}

public sealed record MatchBugfixSet
{
    public bool SmoothCamSeqHandoff { get; init; } = false;
    public bool BetterCamSeqNodeRef { get; init; } = true;
    public bool NoStrayRespawnText { get; init; } = false;
    public bool CorrectBountySfx { get; init; } = true;
    public bool NoDoubleEnemyDeath { get; init; } = true;
}

public sealed record MatchCheatSet
{
    public bool FreeWeaponSelect { get; init; } = false;
    public bool UnlimitedJumps { get; init; } = false;
    public bool UnlockAllDoors { get; init; } = false;
    public bool WalkThroughWalls { get; init; } = false;
    public bool QuadrupleDamage { get; init; } = false;
}

