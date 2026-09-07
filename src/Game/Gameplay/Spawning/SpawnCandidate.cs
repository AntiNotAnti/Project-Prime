namespace MphRead
{
    /// <summary>Last chosen spawn's score components, available without allocating telemetry records.</summary>
    public readonly record struct SpawnCandidate(int EntityId, float Score, float NearestEnemyDistanceSquared,
        int VisibleEnemies, int FacingEnemies, int NearbyEnemies, float DeathPenalty,
        float UsePenalty, float FriendlyBonus, float ObjectivePenalty, float ResourcePenalty, bool CooldownFallback);
}
