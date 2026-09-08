namespace MphRead;

/// <summary>Explicit client preference snapshot for a new scene.</summary>
public static class ClientMatchFeatures
{
    public static MatchFeatureSet Capture() => new()
    {
        AllowInvalidTeams = Features.AllowInvalidTeams,
        HalfSecondAlarm = Features.HalfSecondAlarm,
        FullBoostCharge = Features.FullBoostCharge,
        BoostOpensDoors = Features.BoostOpensDoors,
        MaxPlayerDetail = Features.MaxPlayerDetail,
        NoIdleSway = Features.NoIdleSway,
        DelayedIdleSway = Features.DelayedIdleSway,
        FixedWeapon = Features.FixedWeapon,
        FixedCrosshair = Features.FixedCrosshair,
        MaxRoomDetail = Features.MaxRoomDetail,
        Bugfixes = new()
        {
            SmoothCamSeqHandoff = Bugfixes.SmoothCamSeqHandoff,
            BetterCamSeqNodeRef = Bugfixes.BetterCamSeqNodeRef,
            NoStrayRespawnText = Bugfixes.NoStrayRespawnText,
            CorrectBountySfx = Bugfixes.CorrectBountySfx,
            NoDoubleEnemyDeath = Bugfixes.NoDoubleEnemyDeath,
        },
        Cheats = new()
        {
            FreeWeaponSelect = Cheats.FreeWeaponSelect,
            UnlimitedJumps = Cheats.UnlimitedJumps,
            UnlockAllDoors = Cheats.UnlockAllDoors,
            WalkThroughWalls = Cheats.WalkThroughWalls,
            QuadrupleDamage = Cheats.QuadrupleDamage,
        },
    };
}
