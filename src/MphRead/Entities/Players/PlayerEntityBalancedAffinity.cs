using System;
using MphRead.Mods.Multiplayer;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private ushort _balancedDisruptImmunityTimer;
        private ulong _balancedLifeDrainWindowStart = UInt64.MaxValue;
        private byte _balancedLifeDrainWindowHeal;

        internal ushort ModBalancedDisruptImmunityTimer => _balancedDisruptImmunityTimer;
        internal ushort ModDisruptedTimer => _disruptedTimer;

        // Gameplay application shared by a native beam and the controlled
        // timed rescue path. Snapshot projection keeps ModSetDisrupted's
        // separate flag-following behavior.
        internal bool ModApplyNativeDisruption(bool balancedKanden)
        {
            if (_health <= 0 || balancedKanden && !ModTryBalancedDisrupt()) return false;
            if (!balancedKanden) _disruptedTimer = 60 * 2;
            if (IsMainPlayer)
            {
                HudOnDisrupted();
                _soundSource.PlaySfx(SfxId.LOB_DISRUPT);
            }
            return true;
        }

        internal void ModResetBalancedAffinityState()
        {
            _balancedDisruptImmunityTimer = 0;
            _balancedLifeDrainWindowStart = UInt64.MaxValue;
            _balancedLifeDrainWindowHeal = 0;
        }

        internal void ModTickBalancedAffinityState()
        {
            if (_balancedDisruptImmunityTimer > 0)
            {
                _balancedDisruptImmunityTimer--;
            }
        }

        internal bool ModTryBalancedDisrupt()
        {
            if (_balancedDisruptImmunityTimer > 0)
            {
                return false;
            }

            _disruptedTimer = BalancedModeRules.AffinityControlDurationFrames;
            _balancedDisruptImmunityTimer = (ushort)(
                BalancedModeRules.AffinityControlDurationFrames
                + BalancedModeRules.AffinityControlImmunityFrames);
            return true;
        }

        internal int ModBalancedLifeDrainHeal(int actualDamage, int missingHealth = Int32.MaxValue)
        {
            int requested = BalancedModeRules.LifeDrainHealForActualDamage(actualDamage);
            if (requested <= 0)
            {
                return 0;
            }

            int recoverable = Math.Min(requested, Math.Max(0, missingHealth));
            ulong frame = _scene.FrameCount;
            if (_balancedLifeDrainWindowStart == UInt64.MaxValue
                || frame - _balancedLifeDrainWindowStart >= 60)
            {
                _balancedLifeDrainWindowStart = frame;
                _balancedLifeDrainWindowHeal = 0;
            }

            int remaining = Math.Max(0, BalancedModeRules.SyluxLifeDrainPerSecondCap
                - _balancedLifeDrainWindowHeal);
            int heal = Math.Min(recoverable, remaining);
            if (heal > 0)
            {
                _balancedLifeDrainWindowHeal += (byte)heal;
            }
            BalancedModeTelemetry.NoteSyluxDrain(this, requested, heal);
            return heal;
        }
    }
}
