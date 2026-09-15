using System;

namespace MphRead
{
    /// <summary>
    /// Immutable balance data owned by one match.  A context captures the
    /// profile once at the rules boundary; all lookups are fixed switches over
    /// value fields and do not consult mutable process-wide state.
    /// </summary>
    internal sealed class MatchBalanceContext
    {
        private readonly HunterBalanceValues _samus;
        private readonly HunterBalanceValues _kanden;
        private readonly HunterBalanceValues _trace;
        private readonly HunterBalanceValues _sylux;
        private readonly HunterBalanceValues _noxus;
        private readonly HunterBalanceValues _spire;
        private readonly HunterBalanceValues _weavel;
        private readonly HunterBalanceValues _guardian;
        private readonly WeaponBalanceValues _powerBeam;
        private readonly WeaponBalanceValues _voltDriver;
        private readonly WeaponBalanceValues _missile;
        private readonly WeaponBalanceValues _battlehammer;
        private readonly WeaponBalanceValues _imperialist;
        private readonly WeaponBalanceValues _judicator;
        private readonly WeaponBalanceValues _magmaul;
        private readonly WeaponBalanceValues _shockCoil;
        private readonly WeaponBalanceValues _omegaCannon;

        public GameplayBalanceProfile Profile { get; }
        public bool IsClassic => Profile == GameplayBalanceProfile.Classic;
        public bool IsBalanced => Profile == GameplayBalanceProfile.BalancedV1;

        private MatchBalanceContext(GameplayBalanceProfile profile)
        {
            Profile = profile;
            HunterBalanceValues classic = HunterBalanceValues.Classic;
            _samus = classic;
            _kanden = classic;
            _trace = classic;
            _sylux = classic;
            _noxus = classic;
            _spire = classic;
            _weavel = classic;
            _guardian = classic;

            _powerBeam = WeaponBalanceValues.None;
            _voltDriver = WeaponBalanceValues.None;
            _missile = WeaponBalanceValues.None;
            _battlehammer = WeaponBalanceValues.None;
            _imperialist = WeaponBalanceValues.None;
            _judicator = WeaponBalanceValues.None;
            _magmaul = WeaponBalanceValues.None;
            _shockCoil = WeaponBalanceValues.None;
            _omegaCannon = WeaponBalanceValues.None;

            if (profile == GameplayBalanceProfile.BalancedV1)
            {
                _samus = classic with { AltSpeedMultiplier = 0.90f, BoostDamageBasisDelta = -12 };
                _kanden = classic with { StinglarvaDamageDelta = 5 };
                _trace = classic with { AltSpeedMultiplier = 0.95f, UniversalAmmoCapDelta = -300 };
                _sylux = classic with { AltSpeedMultiplier = 0.95f };
                _spire = classic with { CanClimbLedges = true };

                // Shared uncharged changes are table data. Owner/affinity-
                // specific charged values are selected by the resolver.
                _battlehammer = new WeaponBalanceValues(
                    UnchargedDamageDelta: 4,
                    MinChargeDamageDelta: 0,
                    ChargedDamageDelta: 0,
                    UnchargedHeadshotDamageDelta: 0,
                    MinChargeHeadshotDamageDelta: 0,
                    ChargedHeadshotDamageDelta: 0,
                    UnchargedSplashDamageDelta: 6,
                    MinChargeSplashDamageDelta: 0,
                    ChargedSplashDamageDelta: 0,
                    UnchargedSplashRadiusDelta: 8192,
                    MinChargeSplashRadiusDelta: 0,
                    ChargedSplashRadiusDelta: 0,
                    AmmoCostOverride: null,
                    MinChargeCostOverride: null,
                    ChargeCostOverride: null,
                    UnchargedSpeedOverride: null,
                    MinChargeSpeedOverride: null,
                    ChargedSpeedOverride: null,
                    UnchargedFinalSpeedOverride: null,
                    MinChargeFinalSpeedOverride: null,
                    ChargedFinalSpeedOverride: null);
                _magmaul = new WeaponBalanceValues(
                    UnchargedDamageDelta: 2,
                    MinChargeDamageDelta: 0,
                    ChargedDamageDelta: 0,
                    UnchargedHeadshotDamageDelta: 0,
                    MinChargeHeadshotDamageDelta: 0,
                    ChargedHeadshotDamageDelta: 0,
                    UnchargedSplashDamageDelta: 10,
                    MinChargeSplashDamageDelta: 0,
                    ChargedSplashDamageDelta: 0,
                    UnchargedSplashRadiusDelta: 0,
                    MinChargeSplashRadiusDelta: 0,
                    ChargedSplashRadiusDelta: 0,
                    AmmoCostOverride: null,
                    MinChargeCostOverride: null,
                    ChargeCostOverride: null,
                    UnchargedSpeedOverride: null,
                    MinChargeSpeedOverride: null,
                    ChargedSpeedOverride: null,
                    UnchargedFinalSpeedOverride: null,
                    MinChargeFinalSpeedOverride: null,
                    ChargedFinalSpeedOverride: null);
            }
        }

        internal static GameplayBalanceProfile ProfileFor(MatchRules rules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            return rules.BalancedMode
                ? GameplayBalanceProfile.BalancedV1
                : GameplayBalanceProfile.Classic;
        }

        public static MatchBalanceContext For(MatchRules rules)
        {
            return new MatchBalanceContext(ProfileFor(rules));
        }

        internal HunterBalanceValues GetHunter(Hunter hunter)
            => hunter switch
            {
                MphRead.Hunter.Samus => _samus,
                MphRead.Hunter.Kanden => _kanden,
                MphRead.Hunter.Trace => _trace,
                MphRead.Hunter.Sylux => _sylux,
                MphRead.Hunter.Noxus => _noxus,
                MphRead.Hunter.Spire => _spire,
                MphRead.Hunter.Weavel => _weavel,
                MphRead.Hunter.Guardian => _guardian,
                _ => HunterBalanceValues.Classic
            };

        internal WeaponBalanceValues GetWeapon(BeamType beam)
            => beam switch
            {
                BeamType.PowerBeam => _powerBeam,
                BeamType.VoltDriver => _voltDriver,
                BeamType.Missile => _missile,
                BeamType.Battlehammer => _battlehammer,
                BeamType.Imperialist => _imperialist,
                BeamType.Judicator => _judicator,
                BeamType.Magmaul => _magmaul,
                BeamType.ShockCoil => _shockCoil,
                BeamType.OmegaCannon => _omegaCannon,
                _ => WeaponBalanceValues.None
            };
    }
}
