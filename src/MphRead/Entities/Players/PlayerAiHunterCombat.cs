using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private bool _hunterOwnsAltForm;
        private int _hunterAltTicks;
        private int _hunterMorphPendingTicks;
        private int _hunterMorphCooldownTicks;
        private int _hunterAbilityCooldownTicks;
        private int _hunterBoostChargeTicks;
        private int _hunterSpinTicks;

        private void ResetCompetitiveHunterCombat()
        {
            _hunterOwnsAltForm = false;
            _hunterAltTicks = 0;
            _hunterMorphPendingTicks = 0;
            _hunterMorphCooldownTicks = 0;
            _hunterAbilityCooldownTicks = 0;
            _hunterBoostChargeTicks = 0;
            _hunterSpinTicks = 0;
        }

        /// <summary>
        /// Adds deliberate ability decisions to multiplayer bots that have no
        /// Enhanced-Hunters-specific policy. No gameplay shortcuts: all actions
        /// are expressed as ordinary native input and use native ability gates.
        /// </summary>
        private void ApplyCompetitiveHunterCombat()
        {
            if (!_scene.GameState.Multiplayer || _scene.GameState.EnhancedHunters
                || _scene.GameState.InstaGib || _scene.GameState.Fiesta
                || _scene.GameState.OneInTheChamber || _scene.GameState.Mode == GameMode.GunGame
                || _player.Health <= 0 || !_player.ModInPlay)
            {
                ResetCompetitiveHunterCombat();
                return;
            }

            if (_hunterMorphCooldownTicks > 0) _hunterMorphCooldownTicks--;
            if (_hunterAbilityCooldownTicks > 0) _hunterAbilityCooldownTicks--;

            bool inAlt = _player.IsAltForm;
            if (!inAlt && _hunterOwnsAltForm && !_player.IsMorphing && !_player.IsUnmorphing)
            {
                if (_hunterMorphPendingTicks > 0)
                    _hunterMorphPendingTicks--;
                else
                {
                    _hunterOwnsAltForm = false;
                    _hunterMorphCooldownTicks = Math.Max(_hunterMorphCooldownTicks, 160);
                }
            }
            if (_player.IsMorphing || _player.IsUnmorphing) return;

            // Never commandeer a native objective/health route, or make a flag
            // carrier enter a form in which it cannot retain the flag.
            bool ownsObjective = _player.OctolithFlag != null
                || _scene.GameState.IsTokenMode
                    && (uint)_player.SlotIndex < (uint)_scene.GameState.TokenCarried.Length
                    && _scene.GameState.TokenCarried[_player.SlotIndex] > 0;
            bool nativeRoute = _tacticalMovementActive && !_tacticalSteeringActive;
            if (ownsObjective || nativeRoute)
            {
                if (_hunterOwnsAltForm && inAlt)
                    TryHunterUnmorph();
                return;
            }

            PlayerEntity? target = _tacticalTarget;
            bool visible = target != null && TacticalTargetValid(target) && TacticalCanSee(target);
            float distance = visible && target != null
                ? (target.Position - _player.Position).Length : float.PositiveInfinity;

            if (inAlt)
            {
                // The ROM tree still owns form changes it initiated, especially
                // map navigation. Only control forms entered by this subsystem.
                if (!_hunterOwnsAltForm) return;
                _hunterAltTicks++;

                if (visible && target != null && _player.Values.AltFormStrafe != 0)
                {
                    // Only visible, genuinely observed enemy positions influence
                    // the aim of Trace/Weavel/Sylux alt forms.
                    _field1038 = target.Position - _player.CameraInfo.Position;
                    Func21447E8();
                }

                if (BotHunterCombatPolicy.ShouldExitAlt(_player.Hunter,
                    _hunterAltTicks, visible, distance))
                {
                    TryHunterUnmorph();
                    return;
                }
                if (!visible || target == null)
                {
                    _hunterSpinTicks = 0;
                    _hunterBoostChargeTicks = 0;
                    return;
                }
                PerformHunterAltAction(
                    BotHunterCombatPolicy.ChooseAltAction(_player.Hunter,
                        distance, targetVisible: true, target.ModFrozen),
                    target, distance);
                return;
            }

            if (!visible || target == null || _hunterMorphCooldownTicks > 0
                || !_player.Flags1.TestFlag(PlayerFlags1.Grounded)
                || !_player._abilities.TestFlag(AbilityFlags.AltForm))
            {
                return;
            }

            float healthFraction = _player.HealthMax <= 0 ? 1f
                : Math.Clamp(_player.Health / (float)_player.HealthMax, 0f, 1f);
            if (BotHunterCombatPolicy.ShouldEnterAlt(_player.Hunter,
                distance, healthFraction, visible, grounded: true,
                navigationCommitted: nativeRoute, target.ModFrozen,
                onObjective: TacticalOccupiesObjective(_player)))
            {
                _touchButtons.Morph.IsDown = true;
                _hunterOwnsAltForm = true;
                _hunterMorphPendingTicks = 40;
                _hunterAltTicks = 0;
                _hunterBoostChargeTicks = 0;
                _hunterSpinTicks = 0;
                // Cooldown starts upon entering, and is extended upon exit.
                _hunterMorphCooldownTicks = 120;
                _hunterAbilityCooldownTicks = 0;
                return;
            }

            // Affinity decisions make better use of charged missiles, Volt
            // Driver disruption, Judicator freeze and Magmaul pressure.
            // The ordinary native charging/release code still owns the shot.
            if (BotHunterCombatPolicy.WantsChargedAffinity(_player.Hunter,
                _player.CurrentWeapon, distance, target.ModFrozen)
                && CheckCharge(_player.CurrentWeapon))
            {
                Flags4 |= AiFlags4.Bit1;
            }

            if (_player.Hunter == Hunter.Trace && _player.CurrentWeapon == BeamType.Imperialist
                && distance >= 12 && !_player.EquipInfo.Zoomed
                && _buttons.Select.FramesUp >= 12)
            {
                _buttons.Select.IsDown = true;
            }
            else if (_player.Hunter == Hunter.Sylux
                && _player.CurrentWeapon == BeamType.ShockCoil
                && distance <= 12 && Flags2.TestFlag(AiFlags2.Bit8)
                && !_botSuppressFireThisTick && TacticalShotSafe(target))
            {
                // A continuous weapon should keep tracking and firing in
                // its optimal engagement range, not stutter on fire cadence.
                _buttons.R.IsDown = true;
            }
        }

        private void TryHunterUnmorph()
        {
            if (!_hunterOwnsAltForm || !_player.IsAltForm || _player.IsUnmorphing
                || _player.Flags1.TestFlag(PlayerFlags1.NoUnmorph))
            {
                return;
            }
            _touchButtons.Unmorph.IsDown = true;
            _hunterOwnsAltForm = false;
            _hunterMorphPendingTicks = 0;
            _hunterMorphCooldownTicks = Math.Max(_hunterMorphCooldownTicks, 180);
            _hunterSpinTicks = 0;
            _hunterBoostChargeTicks = 0;
        }

        private void PerformHunterAltAction(BotHunterAltAction action,
            PlayerEntity target, float distance)
        {
            if (_hunterSpinTicks > 0)
            {
                _hunterSpinTicks--;
                if (action == BotHunterAltAction.Spin)
                {
                    _buttons.L.IsDown = true;
                    return;
                }
                _hunterSpinTicks = 0;
            }
            if (_hunterBoostChargeTicks > 0)
            {
                // Holding Boost builds the stock Morph Ball charge. Releasing
                // it triggers native speed, collision and attack cooldowns.
                int chargeFrames = Math.Max(4, _player.Values.BoostChargeMin * 2 + 3);
                if (_hunterBoostChargeTicks < chargeFrames)
                {
                    _buttons.R.IsDown = true;
                    _hunterBoostChargeTicks++;
                }
                else
                {
                    _buttons.R.IsDown = false;
                    _hunterBoostChargeTicks = 0;
                }
                return;
            }
            if (_hunterAbilityCooldownTicks > 0) return;

            switch (action)
            {
                case BotHunterAltAction.Boost:
                    if (_player._abilities.TestFlag(AbilityFlags.Boost)
                        && _player._altAttackCooldown == 0
                        && TacticalCanMove(_player.FacingVector.WithY(0))
                        && Vector3.Dot(_player.FacingVector.WithY(0).Normalized(),
                            (target.Position - _player.Position).WithY(0).Normalized()) >= 0.55f)
                    {
                        _hunterBoostChargeTicks = 1;
                        _buttons.R.IsDown = true;
                        _hunterAbilityCooldownTicks = 100;
                    }
                    break;
                case BotHunterAltAction.Bomb:
                    if (_player._abilities.TestFlag(AbilityFlags.Bombs)
                        && _player._bombCooldown == 0 && _player._bombAmmo > 0
                        && (_player.Hunter != Hunter.Sylux || _player.SyluxBombCount < 3)
                        && _buttons.L.FramesUp > 1)
                    {
                        _buttons.L.IsDown = true;
                        _hunterAbilityCooldownTicks = _player.Hunter == Hunter.Sylux ? 24 : 35;
                    }
                    break;
                case BotHunterAltAction.Spin:
                    if (_player._abilities.TestFlag(AbilityFlags.NoxusAltAttack)
                        && _buttons.L.FramesUp > 1)
                    {
                        _hunterSpinTicks = 26;
                        _buttons.L.IsDown = true;
                        _hunterAbilityCooldownTicks = 75;
                    }
                    break;
                case BotHunterAltAction.Lunge:
                case BotHunterAltAction.Slam:
                    if (_player._altAttackCooldown == 0
                        && _buttons.L.FramesUp > 1
                        && TacticalCanMove((target.Position - _player.Position).WithY(0))
                        && (action == BotHunterAltAction.Slam
                            ? _player._abilities.TestFlag(AbilityFlags.SpireAltAttack)
                            : (_player.Hunter == Hunter.Trace
                                ? _player._abilities.TestFlag(AbilityFlags.TraceAltAttack)
                                : _player._abilities.TestFlag(AbilityFlags.WeavelAltAttack))))
                    {
                        _buttons.L.IsDown = true;
                        _hunterAbilityCooldownTicks = 70;
                    }
                    break;
                // Weavel's stationary Halfturret targets automatically through
                // the native turret simulation; don't fabricate extra bullets.
                case BotHunterAltAction.HoldTurret:
                case BotHunterAltAction.None:
                    break;
            }
        }
    }
}
