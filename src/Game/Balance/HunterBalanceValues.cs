namespace MphRead
{
    /// <summary>
    /// Immutable hunter adjustments relative to the canonical metadata.  The
    /// ledge flag is consumed by the shared collision policy, keeping the
    /// authoritative and predicted simulations on the same rule.
    /// </summary>
    internal readonly record struct HunterBalanceValues(
        float AltSpeedMultiplier,
        int BoostDamageBasisDelta,
        int StinglarvaDamageDelta,
        int UniversalAmmoCapDelta,
        bool CanClimbLedges)
    {
        public int BoostDamageDelta => BoostDamageBasisDelta;
        public int AltAttackDamageDelta => BoostDamageBasisDelta;
        public bool LedgeClimbEnabled => CanClimbLedges;

        public static HunterBalanceValues Classic
            => new(1.0f, 0, 0, 0, false);
    }
}
