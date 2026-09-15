using System;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private CombatActor _enhancedTarget = CombatActor.None;
        private ushort _enhancedTargetTicks;
        private ushort _chilledTicks;
        private byte _overcharge;
        private ushort _overchargeIdleTicks;
        private ushort _cloakFadeTicks;
        private float _cloakFadeStartAlpha = 1;
        private bool _enhancedFreeze;
        private ushort _concussionTicks;
        private SpireScorchPatchEntity? _spireScorchPatch;

        internal bool EnhancedHuntersEnabled => _scene.Match.Rules.EnhancedHunters;
        internal CombatActor EnhancedTarget => _enhancedTarget;
        internal ushort EnhancedTargetTicks => _enhancedTargetTicks;
        internal ushort ChilledTicks => _chilledTicks;
        internal byte Overcharge => _overcharge;
        internal ushort OverchargeIdleTicks => _overchargeIdleTicks;
        internal ushort CloakFadeTicks => _cloakFadeTicks;
        internal ushort ConcussionTicks => _concussionTicks;

        internal void TickEnhancedHunterState(bool wasFrozen)
        {
            if (!EnhancedHuntersEnabled)
            {
                ResetEnhancedHunterState();
                return;
            }
            if (_enhancedTargetTicks > 0)
            {
                _enhancedTargetTicks--;
                if (_enhancedTargetTicks == 0 || _enhancedTarget.Slot >= _scene.Players.Count
                    || _scene.IsHeadless && _scene.Players[_enhancedTarget.Slot].ServerCombatIdentity != _enhancedTarget
                    || _scene.Services.Combat?.IsStaleActor(_enhancedTarget) == true)
                {
                    ClearEnhancedTarget();
                }
            }
            if (_chilledTicks > 0) _chilledTicks--;
            if (_cloakFadeTicks > 0) _cloakFadeTicks--;
            if (_concussionTicks > 0) _concussionTicks--;
            if (_overcharge > 0 && !_scene.Services.IsReplica)
            {
                if (IsPrimeHunter || _overchargeIdleTicks == 0) ClearEnhancedOvercharge();
                else if (--_overchargeIdleTicks == 0) ClearEnhancedOvercharge();
            }
            if (wasFrozen && _frozenTimer == 0 && _enhancedFreeze)
            {
                ApplyEnhancedChill();
                _enhancedFreeze = false;
            }
        }

        internal void ResetEnhancedHunterState()
        {
            ClearEnhancedTarget();
            _chilledTicks = 0;
            _overcharge = 0;
            _overchargeIdleTicks = 0;
            _cloakFadeTicks = 0;
            _cloakFadeStartAlpha = 1;
            _enhancedFreeze = false;
            _concussionTicks = 0;
            if (_spireScorchPatch != null)
            {
                _spireScorchPatch.Expire();
                _spireScorchPatch = null;
            }
        }

        private void ClearEnhancedTarget()
        {
            _enhancedTarget = CombatActor.None;
            _enhancedTargetTicks = 0;
        }

        internal void SetEnhancedTarget(CombatActor actor, ushort ticks)
        {
            if (!EnhancedHuntersEnabled || !actor.IsValid || ticks == 0)
            {
                ClearEnhancedTarget();
                return;
            }
            _enhancedTarget = actor;
            _enhancedTargetTicks = ticks;
        }

        internal bool TryConsumeEnhancedTarget(out CombatActor actor)
        {
            actor = _enhancedTarget;
            bool valid = EnhancedHuntersEnabled && Hunter == Hunter.Samus
                && _enhancedTargetTicks > 0 && actor.IsValid
                && actor.Slot < _scene.Players.Count
                && (!_scene.IsHeadless || _scene.Players[actor.Slot].ServerCombatIdentity == actor);
            ClearEnhancedTarget();
            return valid;
        }

        internal void MarkEnhancedFreeze() { if (EnhancedHuntersEnabled) { _enhancedFreeze = true; _chilledTicks = 0; } }
        internal void ApplyEnhancedChill() { if (EnhancedHuntersEnabled) _chilledTicks = EnhancedHunterTuning.NoxusChillTicks; }

        internal void AddEnhancedOvercharge(int amount)
        {
            if (!EnhancedHuntersEnabled || Hunter != Hunter.Sylux || IsPrimeHunter || amount <= 0) return;
            _overcharge = (byte)Math.Min(EnhancedHunterTuning.SyluxOverchargeMaximum, _overcharge + amount);
            _overchargeIdleTicks = EnhancedHunterTuning.SyluxOverchargeGraceTicks;
        }

        internal void GainEnhancedAffinityHealth(int amount)
        {
            int before = Health;
            GainHealth(amount);
            int overflow = amount - (Health - before);
            if (overflow > 0) AddEnhancedOvercharge(overflow);
            else if (EnhancedHuntersEnabled && Hunter == Hunter.Sylux
                && !IsPrimeHunter && _overcharge > 0)
            {
                _overchargeIdleTicks = EnhancedHunterTuning.SyluxOverchargeGraceTicks;
            }
        }

        internal void HandleEnhancedAcceptedHit(PlayerEntity? attacker,
            BeamProjectileEntity? beam, DamageFlags flags, int resolvedDamage)
        {
            if (!EnhancedHuntersEnabled || resolvedDamage <= 0) return;
            bool enhancedFreezeHit = beam != null && attacker?.Hunter == Hunter.Noxus
                && beam.Beam == BeamType.Judicator && beam.CombatShot.Affinity
                && beam.Afflictions.TestFlag(Affliction.Freeze);
            if (enhancedFreezeHit && Health > 0)
            {
                MarkEnhancedFreeze();
            }
            else if (_enhancedFreeze && _frozenTimer > 0
                && flags.TestFlag(DamageFlags.Direct)
                && !flags.TestAny(DamageFlags.Burn | DamageFlags.Death))
            {
                _frozenTimer = 0;
                ApplyEnhancedChill();
                _enhancedFreeze = false;
            }
            if (attacker == null || beam == null || !beam.CombatShot.Affinity) return;
            bool charged = beam.Flags.TestFlag(BeamFlags.Charged);
            if (attacker.Hunter == Hunter.Samus && beam.Beam == BeamType.Missile
                && charged && flags.TestFlag(DamageFlags.Direct) && Health > 0)
            {
                attacker.SetEnhancedTarget(ServerCombatIdentity,
                    EnhancedHunterTuning.SamusTargetLockTicks);
            }
            else if (attacker.Hunter == Hunter.Kanden && beam.Beam == BeamType.VoltDriver
                && charged && _disruptedTimer > 0 && Health > 0)
            {
                attacker.SetEnhancedTarget(ServerCombatIdentity, _disruptedTimer);
            }

            if (Health > 0 && flags.TestFlag(DamageFlags.Concussive)) ApplyEnhancedConcussion();
        }

        internal int AbsorbEnhancedOvercharge(int damage, DamageFlags flags)
        {
            if (!EnhancedHuntersEnabled || damage <= 0 || _overcharge == 0
                || flags.TestFlag(DamageFlags.Death)) return 0;
            int absorbed = Math.Min(damage, _overcharge);
            _overcharge -= (byte)absorbed;
            if (_overcharge == 0) _overchargeIdleTicks = 0;
            return absorbed;
        }

        internal void ClearEnhancedOvercharge()
        {
            _overcharge = 0;
            _overchargeIdleTicks = 0;
        }

        internal void BeginEnhancedCloakFade(float startAlpha = 5 / 31f)
        {
            if (EnhancedHuntersEnabled && Hunter == Hunter.Trace && !IsPrimeHunter && _cloakTimer == 0)
            {
                _cloakFadeStartAlpha = Math.Clamp(startAlpha, 0, 1);
                _cloakFadeTicks = EnhancedHunterTuning.TraceCloakFadeTicks;
            }
        }

        internal void CancelEnhancedCloakFade()
        {
            _cloakFadeTicks = 0;
            _cloakFadeStartAlpha = 1;
            if (!Flags2.TestFlag(PlayerFlags2.Cloaking))
            {
                _targetAlpha = 1;
                _curAlpha = 1;
            }
        }

        internal float EnhancedCloakFadeAlpha
            => _cloakFadeStartAlpha + (1 - _cloakFadeStartAlpha)
                * (1 - _cloakFadeTicks / (float)EnhancedHunterTuning.TraceCloakFadeTicks);
        internal static bool IsFullyEstablishedPersonalTraceCloak(Hunter hunter,
            bool primeHunter, bool cloakPowerup, ushort cloakTimer, float targetAlpha)
            => hunter == Hunter.Trace && !primeHunter && !cloakPowerup
                && cloakTimer >= SimTicks.From30HzFrames(30)
                && targetAlpha <= 5 / 31f;

        internal static Vector3 ApplyEnhancedChillAcceleration(
            Vector3 speedDelta, ushort chilledTicks)
        {
            if (chilledTicks == 0) return speedDelta;
            speedDelta.X *= EnhancedHunterTuning.NoxusChillAccelerationMultiplier;
            speedDelta.Z *= EnhancedHunterTuning.NoxusChillAccelerationMultiplier;
            return speedDelta;
        }
        internal void ApplyEnhancedConcussion() { if (EnhancedHuntersEnabled) _concussionTicks = EnhancedHunterTuning.WeavelConcussionTicks; }

        internal void SpawnEnhancedSpireScorch(Vector3 position, NodeRef nodeRef,
            in CombatShot shot)
        {
            if (!EnhancedHuntersEnabled || Hunter != Hunter.Spire || IsPrimeHunter
                || !shot.Affinity || !shot.IsValid || _scene.Services.IsReplica) return;
            _spireScorchPatch?.Expire();
            var patch = new SpireScorchPatchEntity(this, shot, position, nodeRef, _scene);
            _spireScorchPatch = patch;
            _scene.AddEntity(patch);
            _scene.Services.Combat?.NoteEnhancedEffect(shot, BeamType.Magmaul,
                CombatEventFlags.LingeringHeat, position);
        }

        internal void ForgetEnhancedSpireScorch(SpireScorchPatchEntity patch)
        {
            if (ReferenceEquals(_spireScorchPatch, patch)) _spireScorchPatch = null;
        }

        private void ApplyEnhancedSnapshot(in SnapshotPlayer state, bool reset)
        {
            if (reset)
            {
                ResetEnhancedHunterState();
                return;
            }
            _enhancedTarget = state.EnhancedTargetSlot == 255
                ? CombatActor.None
                : new CombatActor(state.EnhancedTargetSlot, 1, 1);
            _enhancedTargetTicks = state.EnhancedTargetTicks;
            _overcharge = state.Overcharge;
            _chilledTicks = state.ChilledTicks;
            if (state.CloakFadeTicks > 0 && _cloakFadeTicks == 0)
                _cloakFadeStartAlpha = GetReplicaCloakFadeStartAlpha(state.Flags);
            _cloakFadeTicks = state.CloakFadeTicks;
            if (_cloakFadeTicks == 0) _cloakFadeStartAlpha = 1;
        }

        internal static float GetReplicaCloakFadeStartAlpha(SnapshotPlayerFlags flags)
            => flags.TestFlag(SnapshotPlayerFlags.AltForm) ? 1 / 31f : 5 / 31f;
    }
}
