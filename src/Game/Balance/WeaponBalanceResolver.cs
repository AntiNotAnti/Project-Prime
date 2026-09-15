using System;

namespace MphRead
{
    /// <summary>
    /// Resolves and applies one immutable match profile to one equip boundary.
    /// The resolver works from the canonical <see cref="WeaponInfo"/> values;
    /// it never mutates the shared weapon tables.
    /// </summary>
    internal static class WeaponBalanceResolver
    {
        /// <summary>
        /// Reset the equip fallbacks, then apply the selected profile.  A
        /// reset is intentional even for Classic so a previous weapon/profile
        /// cannot leak runtime values into the next equip.
        /// </summary>
        internal static void Apply(EquipInfo equip, Hunter hunter,
            MatchBalanceContext balance)
        {
            if (equip == null) throw new ArgumentNullException(nameof(equip));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            equip.ResetRuntimeOverrides();
            WeaponInfo? weapon = equip.Weapon;
            if (weapon == null) return;

            WeaponBalanceValues values = Resolve(weapon, hunter, balance);
            ApplyDamageValues(equip, weapon, values);
            ApplyCostAndMovementValues(equip, values);
        }

        /// <summary>Compatibility-shaped overload for callers that keep the
        /// context before the shooter in their setup code.</summary>
        internal static void Apply(EquipInfo equip, MatchBalanceContext balance,
            Hunter hunter)
            => Apply(equip, hunter, balance);

        /// <summary>
        /// Resolve a canonical weapon and infer affinity from the canonical
        /// multiplayer weapon table and the shooter's affinity beam.
        /// </summary>
        internal static WeaponBalanceValues Resolve(WeaponInfo weapon,
            Hunter hunter, MatchBalanceContext balance)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            return Resolve(weapon, hunter, IsAffinityWeapon(weapon, hunter), balance);
        }

        /// <summary>
        /// Resolve with an explicit affinity bit for non-player equip owners
        /// such as a turret. The shooter still must own the beam for
        /// hunter-specific affinity adjustments to apply.
        /// </summary>
        internal static WeaponBalanceValues Resolve(WeaponInfo weapon,
            Hunter hunter, bool affinity, MatchBalanceContext balance)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            BeamType beam = weapon.Beam;
            WeaponBalanceValues values = balance.GetWeapon(beam);
            if (!balance.IsBalanced) return WeaponBalanceValues.None;

            bool shooterAffinity = affinity && IsAffinityBeam(beam, hunter);
            if (beam == BeamType.VoltDriver && shooterAffinity
                && hunter == Hunter.Kanden)
            {
                values = values with
                {
                    UnchargedDamageDelta = values.UnchargedDamageDelta + 2,
                    UnchargedHeadshotDamageDelta = values.UnchargedHeadshotDamageDelta + 2,
                    MinChargeDamageDelta = values.MinChargeDamageDelta + 8,
                    ChargedDamageDelta = values.ChargedDamageDelta + 8,
                    ChargedSpeedOverride = weapon.UnchargedSpeed,
                    ChargedFinalSpeedOverride = weapon.UnchargedFinalSpeed
                };
            }
            else if (beam == BeamType.Missile && shooterAffinity
                && hunter == Hunter.Samus)
            {
                values = values with
                {
                    UnchargedDamageDelta = values.UnchargedDamageDelta + 2,
                    MinChargeCostOverride = 20,
                    ChargeCostOverride = 20
                };
            }
            else if (beam == BeamType.Magmaul)
            {
                // All Magmaul variants receive the shared uncharged change.
                // Charged profiles are mutually exclusive: Spire's affinity
                // values replace, rather than add to, non-affinity values.
                if (shooterAffinity && hunter == Hunter.Spire)
                {
                    values = values with
                    {
                        MinChargeDamageDelta = 12,
                        ChargedDamageDelta = 12,
                        MinChargeSplashDamageDelta = 12,
                        ChargedSplashDamageDelta = 12
                    };
                }
                else if (!affinity)
                {
                    values = values with
                    {
                        MinChargeDamageDelta = 10,
                        ChargedDamageDelta = 10,
                        MinChargeSplashDamageDelta = 2,
                        ChargedSplashDamageDelta = 2
                    };
                }
            }
            else if (beam == BeamType.Judicator && shooterAffinity
                && hunter == Hunter.Noxus)
            {
                values = values with
                {
                    UnchargedDamageDelta = values.UnchargedDamageDelta + 2,
                    UnchargedHeadshotDamageDelta = values.UnchargedHeadshotDamageDelta + 2
                };
            }

            // Battlehammer's +8192 splash-radius adjustment is non-affinity
            // only. Direct and splash damage apply to both variants.
            if (beam == BeamType.Battlehammer && shooterAffinity)
            {
                values = values with
                {
                    UnchargedSplashRadiusDelta = 0,
                    MinChargeSplashRadiusDelta = 0,
                    ChargedSplashRadiusDelta = 0
                };
            }

            return values;
        }

        internal static WeaponBalanceValues Resolve(WeaponInfo weapon,
            Hunter hunter, MatchBalanceContext balance, bool affinity)
            => Resolve(weapon, hunter, affinity, balance);

        /// <summary>
        /// Select the canonical multiplayer weapon for a player's requested
        /// beam.  The returned table entry is the only input used by the
        /// player equip boundary; callers must not apply balance constants
        /// independently.
        /// </summary>
        internal static WeaponInfo SelectWeapon(BeamType beam, Hunter hunter,
            bool forceAffinity = false)
        {
            bool affinity = forceAffinity || IsAffinityBeam(beam, hunter);
            return affinity
                ? Weapons.Current[(int)beam + 9]
                : Weapons.Current[(int)beam];
        }

        /// <summary>Return an effective ammo cost without mutating an equip.
        /// This is used by weapon selection and bot decision code that must
        /// inspect a candidate before it becomes the active equip.</summary>
        internal static ushort GetEffectiveAmmoCost(WeaponInfo weapon,
            Hunter hunter, MatchBalanceContext balance)
        {
            WeaponBalanceValues values = Resolve(weapon, hunter, balance);
            return (ushort)(values.AmmoCostOverride ?? weapon.AmmoCost);
        }

        internal static ushort GetEffectiveMinChargeCost(WeaponInfo weapon,
            Hunter hunter, MatchBalanceContext balance)
        {
            WeaponBalanceValues values = Resolve(weapon, hunter, balance);
            return (ushort)(values.MinChargeCostOverride ?? weapon.MinChargeCost);
        }

        internal static ushort GetEffectiveChargeCost(WeaponInfo weapon,
            Hunter hunter, MatchBalanceContext balance)
        {
            WeaponBalanceValues values = Resolve(weapon, hunter, balance);
            return (ushort)(values.ChargeCostOverride ?? weapon.ChargeCost);
        }

        internal static bool IsAffinityWeapon(WeaponInfo weapon, Hunter hunter)
        {
            if (weapon == null || !IsStandardWeapon(weapon)) return false;
            return IsAffinityBeam(weapon.Beam, hunter)
                && ReferenceEquals(weapon, Weapons.Current[(int)weapon.Beam + 9]);
        }

        private static bool IsAffinityBeam(BeamType beam, Hunter hunter)
            => IsStandardBeam(beam) && hunter != Hunter.Random
                && Weapons.GetAffinityBeam(hunter) == beam;

        private static bool IsStandardWeapon(WeaponInfo weapon)
            => IsStandardBeam(weapon.Beam)
                && (ReferenceEquals(weapon, Weapons.Current[(int)weapon.Beam])
                    || ReferenceEquals(weapon, Weapons.Current[(int)weapon.Beam + 9]));

        private static bool IsStandardBeam(BeamType beam)
            => (uint)(int)beam < 9;

        private static void ApplyDamageValues(EquipInfo equip, WeaponInfo weapon,
            WeaponBalanceValues values)
        {
            if (values.UnchargedDamageDelta != 0)
            {
                equip.SetRuntimeUnchargedDamage(
                    AddDamage(weapon.UnchargedDamage, values.UnchargedDamageDelta));
            }
            if (values.MinChargeDamageDelta != 0)
            {
                equip.SetRuntimeMinChargeDamage(
                    AddDamage(weapon.MinChargeDamage, values.MinChargeDamageDelta));
            }
            if (values.ChargedDamageDelta != 0)
            {
                equip.SetRuntimeChargedDamage(
                    AddDamage(weapon.ChargedDamage, values.ChargedDamageDelta));
            }
            if (values.UnchargedHeadshotDamageDelta != 0)
            {
                equip.SetRuntimeHeadshotDamage(
                    AddDamage(weapon.HeadshotDamage, values.UnchargedHeadshotDamageDelta));
            }
            if (values.MinChargeHeadshotDamageDelta != 0)
            {
                equip.SetRuntimeMinChargeHeadshotDamage(
                    AddDamage(weapon.MinChargeHeadshotDamage,
                        values.MinChargeHeadshotDamageDelta));
            }
            if (values.ChargedHeadshotDamageDelta != 0)
            {
                equip.SetRuntimeChargedHeadshotDamage(
                    AddDamage(weapon.ChargedHeadshotDamage,
                        values.ChargedHeadshotDamageDelta));
            }
            if (values.UnchargedSplashDamageDelta != 0)
            {
                equip.SetRuntimeSplashDamage(
                    AddDamage(weapon.SplashDamage, values.UnchargedSplashDamageDelta));
            }
            if (values.MinChargeSplashDamageDelta != 0)
            {
                equip.SetRuntimeMinChargeSplashDamage(
                    AddDamage(weapon.MinChargeSplashDamage,
                        values.MinChargeSplashDamageDelta));
            }
            if (values.ChargedSplashDamageDelta != 0)
            {
                equip.SetRuntimeChargedSplashDamage(
                    AddDamage(weapon.ChargedSplashDamage,
                        values.ChargedSplashDamageDelta));
            }
            if (values.UnchargedSplashRadiusDelta != 0)
            {
                equip.SetRuntimeUnchargedSplashRadius(
                    AddRadius(weapon.UnchargedSplashRadius,
                        values.UnchargedSplashRadiusDelta));
            }
            if (values.MinChargeSplashRadiusDelta != 0)
            {
                equip.SetRuntimeMinChargeSplashRadius(
                    AddRadius(weapon.MinChargeSplashRadius,
                        values.MinChargeSplashRadiusDelta));
            }
            if (values.ChargedSplashRadiusDelta != 0)
            {
                equip.SetRuntimeChargedSplashRadius(
                    AddRadius(weapon.ChargedSplashRadius,
                        values.ChargedSplashRadiusDelta));
            }
        }

        private static void ApplyCostAndMovementValues(EquipInfo equip,
            WeaponBalanceValues values)
        {
            if (values.AmmoCostOverride.HasValue)
                equip.SetRuntimeAmmoCost(values.AmmoCostOverride.Value);
            if (values.MinChargeCostOverride.HasValue)
                equip.SetRuntimeMinChargeCost(values.MinChargeCostOverride.Value);
            if (values.ChargeCostOverride.HasValue)
                equip.SetRuntimeChargeCost(values.ChargeCostOverride.Value);
            if (values.UnchargedSpeedOverride.HasValue)
                equip.SetRuntimeUnchargedSpeed(values.UnchargedSpeedOverride.Value);
            if (values.MinChargeSpeedOverride.HasValue)
                equip.SetRuntimeMinChargeSpeed(values.MinChargeSpeedOverride.Value);
            if (values.ChargedSpeedOverride.HasValue)
                equip.SetRuntimeChargedSpeed(values.ChargedSpeedOverride.Value);
            if (values.UnchargedFinalSpeedOverride.HasValue)
                equip.SetRuntimeUnchargedFinalSpeed(values.UnchargedFinalSpeedOverride.Value);
            if (values.MinChargeFinalSpeedOverride.HasValue)
                equip.SetRuntimeMinChargeFinalSpeed(values.MinChargeFinalSpeedOverride.Value);
            if (values.ChargedFinalSpeedOverride.HasValue)
                equip.SetRuntimeChargedFinalSpeed(values.ChargedFinalSpeedOverride.Value);
        }

        private static ushort AddDamage(ushort canonical, int delta)
            => (ushort)Math.Clamp((int)canonical + delta, 0, UInt16.MaxValue);

        private static int AddRadius(int canonical, int delta)
            => (int)Math.Clamp((long)canonical + delta, 0L, Int32.MaxValue);
    }

    /// <summary>Named application facade for callers that prefer the plan's
    /// application terminology. It deliberately delegates to the one resolver
    /// implementation so there is only one balance table and one reset point.</summary>
    internal static class WeaponBalanceApplier
    {
        internal static void Apply(EquipInfo equip, Hunter hunter,
            MatchBalanceContext balance)
            => WeaponBalanceResolver.Apply(equip, hunter, balance);
    }
}
