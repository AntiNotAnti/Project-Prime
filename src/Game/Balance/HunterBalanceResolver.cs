using System;

namespace MphRead
{
    /// <summary>
    /// Resolves the small set of hunter movement/damage values that are used
    /// on the hot gameplay path.  The canonical value is always supplied by
    /// the caller; this type only applies the match-owned profile adjustment.
    /// </summary>
    internal static class HunterBalanceResolver
    {
        /// <summary>
        /// Apply the ordinary alternate-form horizontal cap adjustment.  A
        /// Samus boost deliberately keeps its authored Boost Ball cap, so the
        /// caller marks that state and this method returns the canonical cap.
        /// </summary>
        internal static float ResolveAltSpeedCap(float canonicalCap,
            Hunter hunter, bool boosting, MatchBalanceContext balance)
        {
            if (!float.IsFinite(canonicalCap) || balance == null || boosting)
            {
                return canonicalCap;
            }
            return canonicalCap * balance.GetHunter(hunter).AltSpeedMultiplier;
        }

        /// <summary>
        /// Resolve the damage basis before the authored charge interpolation.
        /// Clamping here is important: a low canonical value must not wrap
        /// when a negative Balanced delta is applied.
        /// </summary>
        internal static int ResolveBoostDamageBasis(int canonicalDamage,
            Hunter hunter, MatchBalanceContext balance)
        {
            if (balance == null)
            {
                return Math.Max(canonicalDamage, 0);
            }
            return Math.Max(canonicalDamage
                + balance.GetHunter(hunter).BoostDamageBasisDelta, 0);
        }

        /// <summary>
        /// Preserve the retail integer division while applying the profile to
        /// the basis first.  This is shared by authoritative and predicted
        /// PlayerInput instances.
        /// </summary>
        internal static ushort ResolveBoostDamage(ushort canonicalDamage,
            ushort charge, int maxCharge, Hunter hunter,
            MatchBalanceContext balance)
        {
            if (maxCharge <= 0)
            {
                return 0;
            }
            int basis = ResolveBoostDamageBasis(canonicalDamage, hunter, balance);
            long scaled = (long)basis * charge / maxCharge;
            return (ushort)Math.Clamp(scaled, 0L, (long)UInt16.MaxValue);
        }
    }
}
