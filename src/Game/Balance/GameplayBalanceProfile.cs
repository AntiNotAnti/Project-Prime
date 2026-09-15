namespace MphRead
{
    /// <summary>
    /// The match-owned balance profile.  This is intentionally derived from
    /// <see cref="MatchRules.BalancedMode"/> rather than being another mutable
    /// rule identity.
    /// </summary>
    internal enum GameplayBalanceProfile : byte
    {
        Classic = 0,
        BalancedV1 = 1
    }
}
