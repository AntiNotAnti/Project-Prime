namespace MphRead
{
    /// <summary>
    /// Immutable weapon adjustments relative to the authored <see cref="WeaponInfo"/>
    /// values.  A nullable cost/speed field is an exact effective-value
    /// override; null means that the canonical value plus its delta is used.
    /// </summary>
    internal readonly record struct WeaponBalanceValues(
        int UnchargedDamageDelta,
        int MinChargeDamageDelta,
        int ChargedDamageDelta,
        int UnchargedHeadshotDamageDelta,
        int MinChargeHeadshotDamageDelta,
        int ChargedHeadshotDamageDelta,
        int UnchargedSplashDamageDelta,
        int MinChargeSplashDamageDelta,
        int ChargedSplashDamageDelta,
        int UnchargedSplashRadiusDelta,
        int MinChargeSplashRadiusDelta,
        int ChargedSplashRadiusDelta,
        int? AmmoCostOverride,
        int? MinChargeCostOverride,
        int? ChargeCostOverride,
        int? UnchargedSpeedOverride,
        int? MinChargeSpeedOverride,
        int? ChargedSpeedOverride,
        int? UnchargedFinalSpeedOverride,
        int? MinChargeFinalSpeedOverride,
        int? ChargedFinalSpeedOverride)
    {
        public static WeaponBalanceValues None => default;
    }
}
